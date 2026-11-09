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
    IProjectRepository projectRepository,
    IFormRepository forms) : IFormAuthorizationService
{
    private readonly ScopeAuthorizationEvaluator _scopeAuthorization =
        new(users, workspaces, groups, projects);

    public Task<bool> CanCreateForm(Guid userId, Guid? workspaceId, Guid? groupId, Guid? projectId, CancellationToken cancellationToken = default) =>
        ReadCurrentAuthorityAsync(userId, async token =>
        {
            var parentWorkspaceId = await ResolveWorkspaceIdAsync(workspaceId, groupId, projectId, token);
            return parentWorkspaceId.HasValue &&
                await workspaces.CanViewWorkspace(userId, parentWorkspaceId.Value, token) &&
                await _scopeAuthorization.CanManageScopeAsync(userId, workspaceId, groupId, projectId, token);
        }, cancellationToken);

    public Task<bool> CanViewForm(Guid userId, InternalForm form, CancellationToken cancellationToken = default) =>
        WithCurrentFormAsync(userId, form, async (current, token) =>
        {
            if (current.DeletedAt.HasValue || current.Status is FormStatus.Archived or FormStatus.Draft)
                return await CanManageCurrentFormAsync(userId, current, token);
            return await CanAccessCurrentScopeAsync(userId, current, token);
        }, cancellationToken);

    public Task<bool> CanManageForm(Guid userId, InternalForm form, CancellationToken cancellationToken = default) =>
        WithCurrentFormAsync(userId, form, (current, token) => CanManageCurrentFormAsync(userId, current, token), cancellationToken);

    private async Task<bool> CanManageCurrentFormAsync(Guid userId, InternalForm form, CancellationToken cancellationToken)
    {
        var parentWorkspaceId = await ResolveWorkspaceIdAsync(form.WorkspaceId, form.GroupId, form.ProjectId, cancellationToken);
        if (!parentWorkspaceId.HasValue ||
            !await workspaces.CanViewWorkspace(userId, parentWorkspaceId.Value, cancellationToken))
        {
            return false;
        }

        if (form.CreatedByUserId == userId && await CanAccessCurrentScopeAsync(userId, form, cancellationToken))
        {
            return true;
        }

        // Scope management intentionally retains explicit archived-Project manager
        // report access even when ordinary Project visibility is unavailable.
        return await _scopeAuthorization.CanManageScopeAsync(
            userId, form.WorkspaceId, form.GroupId, form.ProjectId, cancellationToken);
    }

    public Task<bool> CanModifyForm(Guid userId, InternalForm form, CancellationToken cancellationToken = default) =>
        WithCurrentFormAsync(userId, form, (current, token) => CanModifyCurrentFormAsync(userId, current, token), cancellationToken);

    private async Task<bool> CanModifyCurrentFormAsync(Guid userId, InternalForm form, CancellationToken cancellationToken)
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
             await CanAccessCurrentScopeAsync(userId, form, cancellationToken));
    }

    public Task<bool> CanAccessScope(Guid userId, InternalForm form, CancellationToken cancellationToken = default) =>
        WithCurrentFormAsync(userId, form, (current, token) => CanAccessCurrentScopeAsync(userId, current, token), cancellationToken);

    private Task<bool> CanAccessCurrentScopeAsync(Guid userId, InternalForm form, CancellationToken cancellationToken)
    {
        return _scopeAuthorization.CanViewScopeAsync(
            userId,
            form.WorkspaceId,
            form.GroupId,
            form.ProjectId,
            cancellationToken);
    }

    private Task<bool> WithCurrentFormAsync(Guid userId, InternalForm form,
        Func<InternalForm, CancellationToken, Task<bool>> authorization, CancellationToken cancellationToken) =>
        ReadCurrentAuthorityAsync(userId, async token =>
        {
            var current = await forms.GetCurrentAuthorizationAsync(form.Id, token);
            // A stale caller must not authorize or mutate a different persisted
            // parent, owner or lifecycle through its previously tracked graph.
            if (current is null || current.TenantId != form.TenantId ||
                current.WorkspaceId != form.WorkspaceId || current.GroupId != form.GroupId ||
                current.ProjectId != form.ProjectId || current.CreatedByUserId != form.CreatedByUserId ||
                current.Status != form.Status || current.DeletedAt != form.DeletedAt)
                return false;
            return await authorization(current, token);
        }, cancellationToken);

    private Task<bool> ReadCurrentAuthorityAsync(Guid userId,
        Func<CancellationToken, Task<bool>> authorization, CancellationToken cancellationToken) =>
        forms.ReadCurrentAuthorizationAsync(async token =>
        {
            var user = await users.GetByIdAsync(userId, token);
            return user is { Status: UserStatus.Active, DeletedAt: null } && await authorization(token);
        }, cancellationToken);

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
