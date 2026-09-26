using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Notifications;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

public sealed class TaskNotificationPreferenceRepository(
    AppDbContext dbContext,
    ICurrentTenant currentTenant) : ITaskNotificationPreferenceRepository
{
    public async Task<TaskNotificationPreferenceContext?> GetAccessibleAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (!currentTenant.IsAvailable)
        {
            return null;
        }

        return await dbContext.WorkspaceMembers
            .AsNoTracking()
            .Where(member =>
                member.TenantId == currentTenant.TenantId &&
                member.WorkspaceId == workspaceId &&
                member.UserId == userId &&
                member.Status == MembershipStatus.Active)
            .Join(
                dbContext.Workspaces.AsNoTracking().Where(workspace =>
                    workspace.TenantId == currentTenant.TenantId &&
                    workspace.Status == WorkspaceStatus.Active &&
                    workspace.DeletedAt == null),
                member => member.WorkspaceId,
                workspace => workspace.Id,
                (member, workspace) => new TaskNotificationPreferenceContext(
                    member.TaskDeadlineDigestLocalTime,
                    workspace.DefaultTaskDeadlineDigestLocalTime,
                    member.TaskNotificationPreferenceVersion))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> TryUpdateAsync(
        Guid workspaceId,
        Guid userId,
        long expectedVersion,
        TimeOnly? deadlineDigestLocalTime,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        if (!currentTenant.IsAvailable)
        {
            return false;
        }

        var members = dbContext.WorkspaceMembers.Where(member =>
            member.TenantId == currentTenant.TenantId &&
            member.WorkspaceId == workspaceId &&
            member.UserId == userId &&
            member.Status == MembershipStatus.Active &&
            member.TaskNotificationPreferenceVersion == expectedVersion &&
            dbContext.Workspaces.Any(workspace =>
                workspace.Id == workspaceId &&
                workspace.TenantId == currentTenant.TenantId &&
                workspace.Status == WorkspaceStatus.Active &&
                workspace.DeletedAt == null));

        // EF Core's InMemory provider does not support ExecuteUpdate. The
        // production PostgreSQL path below remains one conditional SQL update.
        if (string.Equals(
                dbContext.Database.ProviderName,
                "Microsoft.EntityFrameworkCore.InMemory",
                StringComparison.Ordinal))
        {
            var member = await members.SingleOrDefaultAsync(cancellationToken);
            if (member is null)
            {
                return false;
            }

            member.TaskDeadlineDigestLocalTime = deadlineDigestLocalTime;
            member.TaskNotificationPreferenceVersion = checked(member.TaskNotificationPreferenceVersion + 1);
            member.UpdatedAt = updatedAt;
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        var affected = await members.ExecuteUpdateAsync(setters => setters
            .SetProperty(member => member.TaskDeadlineDigestLocalTime, deadlineDigestLocalTime)
            .SetProperty(member => member.TaskNotificationPreferenceVersion, member => member.TaskNotificationPreferenceVersion + 1)
            .SetProperty(member => member.UpdatedAt, updatedAt), cancellationToken);

        return affected == 1;
    }
}
