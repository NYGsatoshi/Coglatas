using Coglatas.Application.Common;
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
    IProjectAuthorizationService projects,
    IGroupRepository groupRepository,
    IProjectRepository projectRepository) : IFormAuthorizationService
{
    private readonly ScopeAuthorizationEvaluator _scopeAuthorization =
        new(users, workspaces, groups, projects);

    public async Task<bool> CanCreateForm(Guid userId, Guid? workspaceId, Guid? groupId, Guid? projectId, CancellationToken cancellationToken = default)
    {
        var parentWorkspaceId = await ResolveWorkspaceIdAsync(workspaceId, groupId, projectId, cancellationToken);
        return parentWorkspaceId.HasValue &&
            await workspaces.CanViewWorkspace(userId, parentWorkspaceId.Value, cancellationToken) &&
            await _scopeAuthorization.CanManageScopeAsync(userId, workspaceId, groupId, projectId, cancellationToken);
    }

    public async Task<bool> CanViewForm(Guid userId, InternalForm form, CancellationToken cancellationToken = default)
    {
        if (form.DeletedAt.HasValue || form.Status == FormStatus.Archived || form.Status == FormStatus.Draft)
        {
            return await CanManageForm(userId, form, cancellationToken);
        }

        return await _scopeAuthorization.CanViewScopeAsync(
            userId,
            form.WorkspaceId,
            form.GroupId,
            form.ProjectId,
            cancellationToken);
    }

    public async Task<bool> CanManageForm(Guid userId, InternalForm form, CancellationToken cancellationToken = default)
    {
        var parentWorkspaceId = await ResolveWorkspaceIdAsync(form.WorkspaceId, form.GroupId, form.ProjectId, cancellationToken);
        if (!parentWorkspaceId.HasValue ||
            !await workspaces.CanViewWorkspace(userId, parentWorkspaceId.Value, cancellationToken))
        {
            return false;
        }

        if (form.CreatedByUserId == userId && await CanAccessScope(userId, form, cancellationToken))
        {
            return true;
        }

        // Scope management intentionally retains explicit archived-Project manager
        // report access even when ordinary Project visibility is unavailable.
        return await _scopeAuthorization.CanManageScopeAsync(
            userId, form.WorkspaceId, form.GroupId, form.ProjectId, cancellationToken);
    }

    public async Task<bool> CanModifyForm(Guid userId, InternalForm form, CancellationToken cancellationToken = default)
    {
        var parentWorkspaceId = await ResolveWorkspaceIdAsync(form.WorkspaceId, form.GroupId, form.ProjectId, cancellationToken);
        if (!parentWorkspaceId.HasValue ||
            !await workspaces.CanViewWorkspace(userId, parentWorkspaceId.Value, cancellationToken))
        {
            return false;
        }

        // Preserve independent scope-management grants; contribution is required
        // when ownership alone supplies the mutation authority.
        return await _scopeAuthorization.CanManageScopeAsync(
                userId, form.WorkspaceId, form.GroupId, form.ProjectId, cancellationToken) ||
            (form.CreatedByUserId == userId &&
             await workspaces.CanContributeWorkspace(userId, parentWorkspaceId.Value, cancellationToken) &&
             await CanAccessScope(userId, form, cancellationToken));
    }

    public Task<bool> CanAccessScope(Guid userId, InternalForm form, CancellationToken cancellationToken = default)
    {
        return _scopeAuthorization.CanViewScopeAsync(
            userId,
            form.WorkspaceId,
            form.GroupId,
            form.ProjectId,
            cancellationToken);
    }

    private async Task<Guid?> ResolveWorkspaceIdAsync(Guid? workspaceId, Guid? groupId, Guid? projectId, CancellationToken cancellationToken)
    {
        if (!ScopedResourceValidation.HasExactlyOneScope(workspaceId, groupId, projectId))
        {
            return null;
        }

        if (workspaceId.HasValue)
        {
            return workspaceId;
        }

        return groupId.HasValue
            ? (await groupRepository.GetByIdAsync(groupId.Value, cancellationToken))?.WorkspaceId
            : (await projectRepository.GetProjectAsync(projectId!.Value, cancellationToken))?.WorkspaceId;
    }
}
