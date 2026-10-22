using Coglatas.Application.Common;
using Coglatas.Application.Messaging;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

public sealed class WorkspaceChannelDirectoryStore(AppDbContext db) : IWorkspaceChannelDirectoryStore
{
    public Task<WorkspaceRole?> GetCurrentActorRoleAsync(
        Guid tenantId, Guid workspaceId, Guid userId, CancellationToken cancellationToken = default) =>
        AdmittedMembers(tenantId, workspaceId, userId)
            .Select(member => (WorkspaceRole?)member.Role).SingleOrDefaultAsync(cancellationToken);

    public async Task<PagedResponse<WorkspaceChannelDirectoryItem>> ListAsync(
        Guid tenantId, Guid workspaceId, Guid userId, int page, int pageSize, string? search,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var admitted = AdmittedMembers(tenantId, workspaceId, userId);
        var participants = db.ConversationMembers.AsNoTracking().Where(member =>
            member.TenantId == tenantId && member.UserId == userId);
        var readable = participants.Where(member => member.CanRead && member.LeftAt == null && member.RemovedAt == null);
        var candidates = Channels(tenantId, workspaceId)
            .Where(channel => !channel.IsArchived && admitted.Any() &&
                (channel.Visibility == ConversationVisibility.PublicWithinScope ||
                 readable.Any(member => member.ConversationId == channel.Id)));
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Treat the search term as literal text; escaped %, _ and backslash
            // must not broaden discovery, especially across private channels.
            var escaped = search.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal);
            var pattern = $"%{escaped}%";
            candidates = candidates.Where(channel => channel.Title != null &&
                EF.Functions.ILike(channel.Title, pattern, "\\"));
        }
        var total = await candidates.CountAsync(cancellationToken);
        var offset = (int)Math.Min((page - 1L) * pageSize, int.MaxValue);
        var items = await candidates.OrderByDescending(channel => channel.DefaultKind.HasValue)
            .ThenBy(channel => channel.Title).ThenBy(channel => channel.Id)
            .Skip(offset).Take(pageSize)
            .Select(channel => new WorkspaceChannelDirectoryItem(
                channel.Id,
                channel.WorkspaceId,
                channel.Title ?? string.Empty,
                channel.Visibility == ConversationVisibility.PublicWithinScope ? "PublicWithinScope" :
                    channel.Visibility == ConversationVisibility.Private ? "Private" : "ParticipantOnly",
                readable.Any(member => member.ConversationId == channel.Id),
                channel.Visibility == ConversationVisibility.PublicWithinScope && !channel.DefaultKind.HasValue &&
                    !channel.IsLocked && !participants.Any(member => member.ConversationId == channel.Id &&
                        (member.RemovedAt != null || member.LeftAt == null || !member.CanRead)),
                !channel.DefaultKind.HasValue && !channel.IsLocked && channel.Visibility != null &&
                    admitted.Any(member => member.Role != WorkspaceRole.ReadOnly) &&
                    readable.Any(member => member.ConversationId == channel.Id &&
                        (member.Role == ConversationMemberRole.Admin || member.CanManageMembers)),
                channel.DefaultKind.HasValue))
            .ToListAsync(cancellationToken);
        return new(items, page, pageSize, total);
    }

    public Task<Conversation?> GetCurrentAsync(
        Guid tenantId, Guid workspaceId, Guid conversationId, CancellationToken cancellationToken = default) =>
        Channels(tenantId, workspaceId).SingleOrDefaultAsync(channel => channel.Id == conversationId, cancellationToken);

    public Task<ConversationMember?> GetCurrentMemberAsync(
        Guid tenantId, Guid conversationId, Guid userId, CancellationToken cancellationToken = default) =>
        db.ConversationMembers.AsNoTracking().SingleOrDefaultAsync(member =>
            db.ActiveTenantId == tenantId && member.TenantId == tenantId &&
            member.ConversationId == conversationId && member.UserId == userId, cancellationToken);

    public async Task LockAsync(
        Guid tenantId, Guid workspaceId, Guid conversationId, CancellationToken cancellationToken = default)
    {
        if (db.ActiveTenantId != tenantId || !db.Database.IsRelational()) return;
        RequireTransaction();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM conversations WHERE \"TenantId\" = {tenantId} AND \"WorkspaceId\" = {workspaceId} AND \"Id\" = {conversationId} FOR UPDATE",
            cancellationToken);
    }

    public async Task<ConversationMember?> GetMemberForMutationAsync(
        Guid tenantId, Guid conversationId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (db.ActiveTenantId != tenantId) return null;
        if (db.Database.IsRelational())
        {
            RequireTransaction();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM conversation_members WHERE \"TenantId\" = {tenantId} AND \"ConversationId\" = {conversationId} AND \"UserId\" = {userId} FOR UPDATE",
                cancellationToken);
        }
        var member = await db.ConversationMembers.SingleOrDefaultAsync(item =>
            item.TenantId == tenantId && item.ConversationId == conversationId && item.UserId == userId, cancellationToken);
        if (member is null) return null;
        await db.Entry(member).ReloadAsync(cancellationToken);
        return db.Entry(member).State == EntityState.Detached ? null : member;
    }

    private IQueryable<Conversation> Channels(Guid tenantId, Guid workspaceId) =>
        db.Conversations.AsNoTracking().Where(channel => db.ActiveTenantId == tenantId &&
            channel.TenantId == tenantId && channel.WorkspaceId == workspaceId &&
            channel.Type == ConversationType.WorkspaceChannel && channel.ProjectId == null &&
            channel.ParentConversationId == null && channel.RootConversationId == null &&
            (channel.DefaultKind == null ||
             (channel.DefaultKind == ConversationDefaultKind.WorkspaceGeneral && channel.Title == "general" &&
              channel.Visibility == ConversationVisibility.PublicWithinScope)));

    private IQueryable<WorkspaceMember> AdmittedMembers(Guid tenantId, Guid workspaceId, Guid userId) =>
        db.WorkspaceMembers.AsNoTracking().Where(member =>
            db.ActiveTenantId == tenantId && member.TenantId == tenantId && member.WorkspaceId == workspaceId &&
            member.UserId == userId && member.Status == MembershipStatus.Active &&
            (member.Role == WorkspaceRole.Owner || member.Role == WorkspaceRole.Admin ||
             member.Role == WorkspaceRole.Adviser || member.Role == WorkspaceRole.Member || member.Role == WorkspaceRole.ReadOnly) &&
            db.Workspaces.Any(workspace => workspace.Id == workspaceId && workspace.TenantId == tenantId &&
                workspace.Status == WorkspaceStatus.Active && workspace.DeletedAt == null) &&
            db.Users.Any(user => user.Id == userId && user.Status == UserStatus.Active && user.DeletedAt == null) &&
            db.Tenants.Any(tenant => tenant.Id == tenantId && tenant.Status == TenantStatus.Active && tenant.DeletedAt == null) &&
            db.TenantUsers.Any(tenantUser => tenantUser.TenantId == tenantId && tenantUser.UserId == userId &&
                tenantUser.Status == TenantUserStatus.Active));

    private void RequireTransaction()
    {
        if (db.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL" || db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Workspace channel mutation requires a PostgreSQL transaction.");
        }
    }
}
