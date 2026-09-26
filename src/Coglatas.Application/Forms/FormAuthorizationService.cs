using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Groups;
using Coglatas.Application.Projects;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Forms;

public sealed class FormAuthorizationService(
    IUserRepository users,
    IWorkspaceAuthorizationService workspaces,
    IGroupAuthorizationService groups,
    IProjectAuthorizationService projects) : IFormAuthorizationService
{
    public Task<bool> CanCreateForm(Guid userId, Guid? workspaceId, Guid? groupId, Guid? projectId, CancellationToken cancellationToken = default)
    {
        return CanManageScopeAsync(userId, workspaceId, groupId, projectId, cancellationToken);
    }

    public async Task<bool> CanViewForm(Guid userId, InternalForm form, CancellationToken cancellationToken = default)
    {
        if (form.DeletedAt.HasValue || form.Status == FormStatus.Archived)
        {
            return await CanManageForm(userId, form, cancellationToken);
        }

        if (form.Status == FormStatus.Draft)
        {
            return form.CreatedByUserId == userId || await CanManageScopeAsync(userId, form.WorkspaceId, form.GroupId, form.ProjectId, cancellationToken);
        }

        return await CanViewScopeAsync(userId, form.WorkspaceId, form.GroupId, form.ProjectId, cancellationToken);
    }

    public Task<bool> CanManageForm(Guid userId, InternalForm form, CancellationToken cancellationToken = default)
    {
        return form.CreatedByUserId == userId
            ? Task.FromResult(true)
            : CanManageScopeAsync(userId, form.WorkspaceId, form.GroupId, form.ProjectId, cancellationToken);
    }

    public Task<bool> CanAccessScope(Guid userId, InternalForm form, CancellationToken cancellationToken = default)
    {
        return CanViewScopeAsync(userId, form.WorkspaceId, form.GroupId, form.ProjectId, cancellationToken);
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
        var user = await users.GetByIdAsync(userId, cancellationToken);
        return user is { Status: UserStatus.Active, SystemRole: SystemRole.Teacher or SystemRole.Admin or SystemRole.SystemAdmin } &&
            await canViewScope();
    }
}
