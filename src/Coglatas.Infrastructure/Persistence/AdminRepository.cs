using Coglatas.Application.Admin;
using Coglatas.Application.Audit;
using Coglatas.Application.Common;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

public sealed class AdminRepository(AppDbContext dbContext) : IAdminRepository
{
    public async Task<PagedResponse<AdminUserListItemResponse>> ListUsersAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Users.AsNoTracking().OrderBy(user => user.Email);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(user => new AdminUserListItemResponse(
                user.Id,
                user.DisplayName,
                user.Email,
                user.SystemRole,
                user.Status,
                user.LastLoginAt,
                user.CreatedAt,
                user.UpdatedAt,
                user.DeletedAt))
            .ToListAsync(cancellationToken);

        return new PagedResponse<AdminUserListItemResponse>(items, page, pageSize, totalCount);
    }

    public Task<User?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return dbContext.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);
    }

    public Task<User?> GetUserByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
    {
        return dbContext.Users.FirstOrDefaultAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken);
    }

    public Task<int> CountSystemAdminsAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.Users.CountAsync(user => user.SystemRole == SystemRole.SystemAdmin && user.Status == UserStatus.Active && user.DeletedAt == null, cancellationToken);
    }

    public Task<int> CountSystemAdminsExcludingAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return dbContext.Users.CountAsync(user => user.Id != userId && user.SystemRole == SystemRole.SystemAdmin && user.Status == UserStatus.Active && user.DeletedAt == null, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListActiveSystemAdminIdsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Users
            .AsNoTracking()
            .Where(user =>
                user.SystemRole == SystemRole.SystemAdmin &&
                user.Status == UserStatus.Active &&
                user.DeletedAt == null)
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResponse<AdminInviteResponse>> ListInvitesAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Invites.AsNoTracking().OrderByDescending(invite => invite.CreatedAt);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(invite => new AdminInviteResponse(
                invite.Id,
                invite.WorkspaceId,
                invite.Email,
                invite.Role,
                invite.ExpiresAt,
                invite.AcceptedAt,
                invite.RevokedAt,
                invite.InvitedByUserId,
                invite.CreatedAt,
                null))
            .ToListAsync(cancellationToken);

        return new PagedResponse<AdminInviteResponse>(items, page, pageSize, totalCount);
    }

    public async Task AddInviteAsync(Invite invite, CancellationToken cancellationToken = default)
    {
        await dbContext.Invites.AddAsync(invite, cancellationToken);
    }

    public Task<Invite?> GetInviteAsync(Guid inviteId, CancellationToken cancellationToken = default)
    {
        return dbContext.Invites.FirstOrDefaultAsync(invite => invite.Id == inviteId, cancellationToken);
    }

    public Task<Workspace?> GetWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        return dbContext.Workspaces.FirstOrDefaultAsync(workspace => workspace.Id == workspaceId, cancellationToken);
    }

    public Task<Group?> GetGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        return dbContext.Groups.FirstOrDefaultAsync(group => group.Id == groupId, cancellationToken);
    }

    public Task<Project?> GetProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        return dbContext.Projects.FirstOrDefaultAsync(project => project.Id == projectId, cancellationToken);
    }

    public Task<Channel?> GetChannelAsync(Guid channelId, CancellationToken cancellationToken = default)
    {
        return dbContext.Channels.FirstOrDefaultAsync(channel => channel.Id == channelId, cancellationToken);
    }

    public async Task<IReadOnlyList<SystemSetting>> ListSettingsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.SystemSettings.AsNoTracking().OrderBy(setting => setting.Key).ToListAsync(cancellationToken);
    }

    public Task<SystemSetting?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        return dbContext.SystemSettings.FirstOrDefaultAsync(setting => setting.Key == key, cancellationToken);
    }

    public async Task AddSettingAsync(SystemSetting setting, CancellationToken cancellationToken = default)
    {
        await dbContext.SystemSettings.AddAsync(setting, cancellationToken);
    }

    public async Task<AdminDashboardSnapshot> GetDashboardSnapshotAsync(int recentCount, DateOnly today, CancellationToken cancellationToken = default)
    {
        var userCount = await dbContext.Users.CountAsync(user => user.DeletedAt == null, cancellationToken);
        var activeUserCount = await dbContext.Users.CountAsync(user => user.Status == UserStatus.Active && user.DeletedAt == null, cancellationToken);
        var workspaceCount = await dbContext.Workspaces.CountAsync(workspace => workspace.DeletedAt == null, cancellationToken);
        var groupCount = await dbContext.Groups.CountAsync(group => group.DeletedAt == null, cancellationToken);
        var projectCount = await dbContext.Projects.CountAsync(project => project.DeletedAt == null, cancellationToken);
        var openTaskCount = await dbContext.TaskItems.CountAsync(task => task.DeletedAt == null && task.Status != TaskItemStatus.Completed && task.Status != TaskItemStatus.Cancelled, cancellationToken);
        var overdueTaskCount = await dbContext.TaskItems.CountAsync(task => task.DeletedAt == null && task.DueDate.HasValue && task.DueDate.Value < today && task.Status != TaskItemStatus.Completed && task.Status != TaskItemStatus.Cancelled, cancellationToken);
        var storageUsageEstimateBytes = await dbContext.Attachments.Where(attachment => attachment.DeletedAt == null).SumAsync(attachment => (long?)attachment.SizeBytes, cancellationToken) ?? 0;

        var recentAuditLogs = await dbContext.AuditLogs
            .AsNoTracking()
            .OrderByDescending(log => log.CreatedAt)
            .Take(recentCount)
            .Select(log => new AuditLogListItemResponse(log.Id, log.ActorUserId, log.ActorUser == null ? null : log.ActorUser.DisplayName, log.Action, log.EntityType, log.EntityId, log.WorkspaceId, log.GroupId, log.ProjectId, log.Summary, log.MetadataJson, log.CorrelationId, log.CreatedAt))
            .ToListAsync(cancellationToken);

        var recentSecurityEvents = await dbContext.SecurityEvents
            .AsNoTracking()
            .OrderByDescending(evt => evt.CreatedAt)
            .Take(recentCount)
            .Select(evt => new SecurityEventListItemResponse(evt.Id, evt.EventType, evt.UserId, evt.Email, evt.IpAddress, evt.UserAgent, evt.Severity, evt.Summary, evt.MetadataJson, evt.CreatedAt))
            .ToListAsync(cancellationToken);

        return new AdminDashboardSnapshot(userCount, activeUserCount, workspaceCount, groupCount, projectCount, openTaskCount, overdueTaskCount, storageUsageEstimateBytes, recentAuditLogs, recentSecurityEvents);
    }
}
