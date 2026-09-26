using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Groups;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Projects;

public sealed class ProjectAuthorizationService(
    IProjectRepository projects,
    IWorkspaceAuthorizationService workspaces,
    IGroupAuthorizationService groups,
    IGroupRepository groupRepository) : IProjectAuthorizationService, ITaskAuthorizationService, ICommentAuthorizationService
{
    public async Task<bool> CanViewProject(Guid userId, Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await projects.GetProjectAsync(projectId, cancellationToken);
        if (project is null ||
            project.DeletedAt.HasValue ||
            project.Status is ProjectStatus.Archived or ProjectStatus.Deleted)
        {
            return false;
        }

        // Project membership never outlives the current Workspace read boundary.
        // In particular, archived Workspaces require a current active membership;
        // SystemAdmin alone is not a historical-read grant.
        if (!await workspaces.CanViewWorkspace(userId, project.WorkspaceId, cancellationToken))
        {
            return false;
        }

        var member = await projects.GetMemberAsync(projectId, userId, cancellationToken);
        if (member is not null)
        {
            return true;
        }

        if (!project.Visibility.HasValue)
        {
            // NULL is the internal LegacyUnknown migration state. Preserve the
            // pre-canonical compatibility boundary exactly; never guess a
            // canonical Visibility from GroupId, lifecycle state, or membership.
            return await CanViewLegacyUnknownProjectAsync(userId, project, cancellationToken);
        }

        return project.Visibility.Value switch
        {
            ProjectVisibility.WorkspaceVisible =>
                project.ActivationState == ProjectActivationState.Activated &&
                project.Status is ProjectStatus.Active or ProjectStatus.Review or ProjectStatus.Completed,
            ProjectVisibility.MembersOnly => false,
            ProjectVisibility.Restricted => false,
            _ => false
        };
    }

    public async Task<bool> CanManageProject(Guid userId, Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await projects.GetProjectAsync(projectId, cancellationToken);
        if (project is null || project.DeletedAt.HasValue || project.Status == ProjectStatus.Deleted)
        {
            return false;
        }

        // Ordinary Project operations are unavailable while the containing
        // Workspace is archived. CanContributeWorkspace is active-only and
        // therefore closes the ProjectMember bypass through an archived parent.
        if (!await workspaces.CanContributeWorkspace(userId, project.WorkspaceId, cancellationToken))
        {
            return false;
        }

        var member = await projects.GetMemberAsync(projectId, userId, cancellationToken);
        var isProjectManager = member?.Role is ProjectRole.Owner or ProjectRole.Manager;

        // Archived Projects retain the established explicit-member management
        // boundary so restore cannot become a Workspace/SystemAdmin shortcut.
        if (project.Status == ProjectStatus.Archived)
        {
            return isProjectManager;
        }

        if (!project.Visibility.HasValue)
        {
            if (RequiresExplicitMembership(project.Status))
            {
                return isProjectManager;
            }

            if (await workspaces.CanGovernWorkspace(userId, project.WorkspaceId, cancellationToken))
            {
                return true;
            }

            if (project.GroupId.HasValue && await groups.CanManageGroup(userId, project.GroupId.Value, cancellationToken))
            {
                return true;
            }

            return isProjectManager;
        }

        return project.Visibility.Value switch
        {
            // MembersOnly and Restricted body access remains project-explicit.
            // Workspace visibility/governance changes are a separate capability
            // boundary owned by the canonical create/visibility workflow.
            ProjectVisibility.MembersOnly or ProjectVisibility.Restricted => isProjectManager,
            ProjectVisibility.WorkspaceVisible =>
                isProjectManager ||
                await workspaces.CanGovernWorkspace(userId, project.WorkspaceId, cancellationToken) ||
                (project.GroupId.HasValue && await groups.CanManageGroup(userId, project.GroupId.Value, cancellationToken)),
            _ => false
        };
    }

    public async Task<bool> CanContributeProject(Guid userId, Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await projects.GetProjectAsync(projectId, cancellationToken);
        if (project is null ||
            project.DeletedAt.HasValue ||
            project.ActivationState != ProjectActivationState.Activated ||
            (project.Status != ProjectStatus.Active && project.Status != ProjectStatus.Review))
        {
            return false;
        }

        // Project visibility is a read/discovery input only. Content mutation
        // also requires an active contributing Workspace membership and an
        // explicit non-viewer Project membership.
        if (!await workspaces.CanContributeWorkspace(userId, project.WorkspaceId, cancellationToken))
        {
            return false;
        }

        var member = await projects.GetMemberAsync(projectId, userId, cancellationToken);
        return member?.Role is
            ProjectRole.Owner or
            ProjectRole.Manager or
            ProjectRole.Contributor or
            ProjectRole.Reviewer;
    }

    public async Task<bool> CanCreateProject(Guid userId, Guid workspaceId, Guid groupId, CancellationToken cancellationToken = default)
    {
        if (groupId == Guid.Empty)
        {
            return false;
        }

        var group = await groupRepository.GetByIdAsync(groupId, cancellationToken);
        return group is not null &&
            group.WorkspaceId == workspaceId &&
            await groups.CanManageGroup(userId, groupId, cancellationToken);
    }

    public async Task<bool> CanCreateTask(Guid userId, Guid projectId, CancellationToken cancellationToken = default)
    {
        if (!await CanUseTaskMutationScopeAsync(userId, projectId, cancellationToken))
        {
            return false;
        }

        var member = await projects.GetMemberAsync(projectId, userId, cancellationToken);
        if (member is not null && member.Role != ProjectRole.Viewer)
        {
            return true;
        }

        return await CanManageProject(userId, projectId, cancellationToken);
    }

    public async Task<bool> CanUpdateTask(Guid userId, Guid taskItemId, CancellationToken cancellationToken = default)
    {
        var task = await projects.GetTaskAsync(taskItemId, cancellationToken);
        if (task is null ||
            task.DeletedAt.HasValue ||
            !await CanUseTaskMutationScopeAsync(userId, task.ProjectId, cancellationToken))
        {
            return false;
        }

        if (await CanManageProject(userId, task.ProjectId, cancellationToken))
        {
            return true;
        }

        var member = await projects.GetMemberAsync(task.ProjectId, userId, cancellationToken);
        return member is not null &&
               member.Role != ProjectRole.Viewer &&
               (task.CreatedByUserId == userId || task.PrimaryAssigneeUserId == userId);
    }

    public async Task<bool> CanAssignTask(Guid userId, Guid taskItemId, CancellationToken cancellationToken = default)
    {
        var task = await projects.GetTaskAsync(taskItemId, cancellationToken);
        // Assignment/delete/override authorization is a live-row capability.
        // Restore handles its soft-deleted exception explicitly at the command
        // boundary instead of widening this reusable authorization primitive.
        return task is not null &&
               !task.DeletedAt.HasValue &&
               await CanUseTaskMutationScopeAsync(userId, task.ProjectId, cancellationToken) &&
               await CanManageProject(userId, task.ProjectId, cancellationToken);
    }

    public Task<bool> CanDeleteTask(Guid userId, Guid taskItemId, CancellationToken cancellationToken = default) =>
        CanAssignTask(userId, taskItemId, cancellationToken);

    public async Task<bool> CanReviewTask(Guid userId, Guid taskItemId, CancellationToken cancellationToken = default)
    {
        var task = await projects.GetTaskAsync(taskItemId, cancellationToken);
        if (task is null ||
            task.DeletedAt.HasValue ||
            !await CanUseTaskMutationScopeAsync(userId, task.ProjectId, cancellationToken))
        {
            return false;
        }

        if (await CanManageProject(userId, task.ProjectId, cancellationToken))
        {
            return true;
        }

        // Reviewer is a narrow relationship authority. It permits review
        // outcomes but does not imply unrestricted Task-body editing or allow a
        // Project Viewer to cross the read-only boundary.
        var member = await projects.GetMemberAsync(task.ProjectId, userId, cancellationToken);
        return member is not null &&
               member.Role != ProjectRole.Viewer &&
               task.ReviewerUserId == userId;
    }

    public Task<bool> CanOverrideTaskReview(Guid userId, Guid taskItemId, CancellationToken cancellationToken = default) =>
        CanAssignTask(userId, taskItemId, cancellationToken);

    private async Task<bool> CanUseTaskMutationScopeAsync(
        Guid userId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var project = await projects.GetProjectAsync(projectId, cancellationToken);
        return project is not null &&
               !project.DeletedAt.HasValue &&
               project.ActivationState == ProjectActivationState.Activated &&
               (project.Status == ProjectStatus.Active || project.Status == ProjectStatus.Review) &&
               await workspaces.CanContributeWorkspace(userId, project.WorkspaceId, cancellationToken) &&
               await CanViewProject(userId, projectId, cancellationToken);
    }

    private async Task<bool> CanMutateProjectContentAsync(
        Guid userId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (await CanContributeProject(userId, projectId, cancellationToken))
        {
            return true;
        }

        var project = await projects.GetProjectAsync(projectId, cancellationToken);
        return project is not null &&
               !project.DeletedAt.HasValue &&
               project.ActivationState == ProjectActivationState.Activated &&
               (project.Status == ProjectStatus.Active || project.Status == ProjectStatus.Review) &&
               await CanManageProject(userId, projectId, cancellationToken);
    }

    private async Task<bool> CanViewLegacyUnknownProjectAsync(
        Guid userId,
        Project project,
        CancellationToken cancellationToken)
    {
        if (RequiresExplicitMembership(project.Status))
        {
            return false;
        }

        if (!project.GroupId.HasValue)
        {
            return true;
        }

        return await workspaces.CanGovernWorkspace(userId, project.WorkspaceId, cancellationToken) ||
               await groupRepository.GetMemberAsync(project.GroupId.Value, userId, cancellationToken) is not null;
    }

    private static bool RequiresExplicitMembership(ProjectStatus status) =>
        status is ProjectStatus.Planning or ProjectStatus.Suspended or ProjectStatus.Archived or ProjectStatus.Deleted;

    public async Task<bool> CanCommentOnTarget(Guid userId, CommentTargetType targetType, Guid targetId, CancellationToken cancellationToken = default)
    {
        if (targetType == CommentTargetType.Project)
        {
            return await CanMutateProjectContentAsync(userId, targetId, cancellationToken);
        }
        if (targetType == CommentTargetType.TaskItem)
        {
            var task = await projects.GetTaskAsync(targetId, cancellationToken);
            return task is not null &&
                   !task.DeletedAt.HasValue &&
                   await CanMutateProjectContentAsync(userId, task.ProjectId, cancellationToken);
        }
        if (targetType == CommentTargetType.Milestone)
        {
            var milestone = await projects.GetMilestoneAsync(targetId, cancellationToken);
            return milestone is not null &&
                   await CanMutateProjectContentAsync(userId, milestone.ProjectId, cancellationToken);
        }
        return false;
    }
}
