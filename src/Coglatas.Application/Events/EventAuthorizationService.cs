using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Groups;
using Coglatas.Application.Projects;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Events;

public sealed class EventAuthorizationService(
    IUserRepository users,
    IWorkspaceAuthorizationService workspaces,
    IGroupAuthorizationService groups,
    IProjectAuthorizationService projects) : IEventAuthorizationService
{
    public Task<bool> CanCreateEvent(Guid userId, Guid? workspaceId, Guid? groupId, Guid? projectId, CancellationToken cancellationToken = default)
    {
        return CanManageScopeAsync(userId, workspaceId, groupId, projectId, cancellationToken);
    }

    public async Task<bool> CanViewEvent(Guid userId, ActivityEvent activityEvent, CancellationToken cancellationToken = default)
    {
        if (activityEvent.Status == EventStatus.Draft)
        {
            return activityEvent.CreatedByUserId == userId ||
                await CanManageScopeAsync(userId, activityEvent.WorkspaceId, activityEvent.GroupId, activityEvent.ProjectId, cancellationToken);
        }

        return await CanViewScopeAsync(userId, activityEvent.WorkspaceId, activityEvent.GroupId, activityEvent.ProjectId, cancellationToken);
    }

    public async Task<bool> CanManageEvent(Guid userId, ActivityEvent activityEvent, CancellationToken cancellationToken = default)
    {
        return activityEvent.CreatedByUserId == userId ||
            await CanManageScopeAsync(userId, activityEvent.WorkspaceId, activityEvent.GroupId, activityEvent.ProjectId, cancellationToken);
    }

    public Task<bool> CanManageAttendance(Guid userId, ActivityEvent activityEvent, CancellationToken cancellationToken = default)
    {
        return CanManageEvent(userId, activityEvent, cancellationToken);
    }

    public Task<bool> CanAccessScope(Guid userId, ActivityEvent activityEvent, CancellationToken cancellationToken = default)
    {
        return CanViewScopeAsync(userId, activityEvent.WorkspaceId, activityEvent.GroupId, activityEvent.ProjectId, cancellationToken);
    }

    private async Task<bool> CanViewScopeAsync(Guid userId, Guid? workspaceId, Guid? groupId, Guid? projectId, CancellationToken cancellationToken)
    {
        if (workspaceId.HasValue)
        {
            return await workspaces.CanViewWorkspace(userId, workspaceId.Value, cancellationToken);
        }

        if (groupId.HasValue)
        {
            return await groups.CanViewGroup(userId, groupId.Value, cancellationToken);
        }

        if (projectId.HasValue)
        {
            return await projects.CanViewProject(userId, projectId.Value, cancellationToken);
        }

        return false;
    }

    private async Task<bool> CanManageScopeAsync(Guid userId, Guid? workspaceId, Guid? groupId, Guid? projectId, CancellationToken cancellationToken)
    {
        if (workspaceId.HasValue)
        {
            return await workspaces.CanManageWorkspace(userId, workspaceId.Value, cancellationToken) ||
                await CanElevatedUserManageVisibleScopeAsync(userId, () => workspaces.CanViewWorkspace(userId, workspaceId.Value, cancellationToken), cancellationToken);
        }

        if (groupId.HasValue)
        {
            return await groups.CanManageGroup(userId, groupId.Value, cancellationToken) ||
                await CanElevatedUserManageVisibleScopeAsync(userId, () => groups.CanViewGroup(userId, groupId.Value, cancellationToken), cancellationToken);
        }

        if (projectId.HasValue)
        {
            return await projects.CanManageProject(userId, projectId.Value, cancellationToken) ||
                await CanElevatedUserManageVisibleScopeAsync(userId, () => projects.CanViewProject(userId, projectId.Value, cancellationToken), cancellationToken);
        }

        return false;
    }

    private async Task<bool> CanElevatedUserManageVisibleScopeAsync(Guid userId, Func<Task<bool>> canViewScope, CancellationToken cancellationToken)
    {
        return await IsElevatedUserAsync(userId, cancellationToken) && await canViewScope();
    }

    private async Task<bool> IsElevatedUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        return user is
        {
            Status: UserStatus.Active,
            SystemRole: SystemRole.Teacher or SystemRole.Admin or SystemRole.SystemAdmin
        };
    }
}
