using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

public sealed class WorkspaceMemberManagementReadRepository(AppDbContext db)
    : IWorkspaceMemberManagementReadRepository
{
    public Task<bool> HasActiveTenantIdentityAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        ActiveTenantUsers(tenantId).AnyAsync(member => member.UserId == userId, cancellationToken);

    public Task<WorkspaceMemberManagementTargetSnapshot?> GetTargetAsync(
        Guid tenantId, Guid workspaceId, Guid userId, CancellationToken cancellationToken)
    {
        var activeTenantUsers = ActiveTenantUsers(tenantId);
        return db.WorkspaceMembers.AsNoTracking()
            .Where(member => member.TenantId == tenantId && member.WorkspaceId == workspaceId && member.UserId == userId)
            .Select(member => new WorkspaceMemberManagementTargetSnapshot(
                member.UserId, member.Role, member.Status,
                member.User != null && member.User.DeletedAt == null,
                activeTenantUsers.Any(tenantMember => tenantMember.UserId == member.UserId)))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkspaceMemberCandidateIdentity>> ListCandidatesAsync(
        Guid tenantId, Guid workspaceId, string prefix, int take, Guid? afterUserId,
        CancellationToken cancellationToken)
    {
        var query = ActiveTenantUsers(tenantId).Where(member =>
            member.User!.DisplayName.StartsWith(prefix) &&
            !db.WorkspaceMembers.Any(existing => existing.TenantId == tenantId &&
                existing.WorkspaceId == workspaceId && existing.UserId == member.UserId));
        if (afterUserId.HasValue)
            query = query.Where(member => member.UserId.CompareTo(afterUserId.Value) > 0);
        return await query.OrderBy(member => member.UserId).Take(take)
            .Select(member => new WorkspaceMemberCandidateIdentity(member.UserId, member.User!.DisplayName))
            .ToListAsync(cancellationToken);
    }

    private IQueryable<TenantUser> ActiveTenantUsers(Guid tenantId) => db.TenantUsers.AsNoTracking().Where(member =>
        member.TenantId == tenantId && member.Status == TenantUserStatus.Active &&
        member.User != null && member.User.Status == UserStatus.Active && member.User.DeletedAt == null &&
        member.Tenant != null && member.Tenant.Status == TenantStatus.Active && member.Tenant.DeletedAt == null);
}
