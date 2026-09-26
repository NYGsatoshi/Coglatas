using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Projects;

public interface IProjectMembershipService
{
    Task<Result<ProjectMemberResponse>> AddAsync(
        Guid projectId,
        AddProjectMemberRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<ProjectMemberResponse>> UpdateAsync(
        Guid projectId,
        Guid userId,
        UpdateProjectMemberRequest request,
        CancellationToken cancellationToken = default);

    Task<Result> RemoveAsync(
        Guid projectId,
        Guid userId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Canonical Project member mutation boundary. Project membership, the
/// ProjectGeneral participant projection, audit, Project invalidation and
/// authorization invalidations are staged before one caller-owned save.
/// </summary>
public sealed class ProjectMembershipService(
    IProjectRepository projects,
    IWorkspaceRepository workspaces,
    IGroupRepository groups,
    IUserRepository users,
    IProjectAuthorizationService projectAuthorization,
    IProjectGeneralMembershipSynchronizer projectGeneral,
    ICurrentUser currentUser,
    IClock clock,
    IAuditLogger auditLogger,
    IBusinessInvalidationPublisher invalidations,
    IAuthorizationStateChangePublisher authorizationChanges,
    ITaskCommandUnitOfWork unitOfWork) : IProjectMembershipService
{
    public async Task<Result<ProjectMemberResponse>> AddAsync(
        Guid projectId,
        AddProjectMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var actorUserId) ||
            !await projectAuthorization.CanManageProject(actorUserId, projectId, cancellationToken))
        {
            return Hidden<ProjectMemberResponse>();
        }

        var project = await projects.GetProjectAsync(projectId, cancellationToken);
        var mutable = EnsureMutable<ProjectMemberResponse>(project);
        if (mutable is not null)
        {
            return mutable;
        }

        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null || user.DeletedAt.HasValue || user.Status != UserStatus.Active)
        {
            return Result<ProjectMemberResponse>.Failure(new ApplicationErrorDetail(
                "NotFound",
                "Project or user not found."));
        }

        var access = await ValidateParentAccessAsync(project!, request.UserId, cancellationToken);
        if (!access.IsSuccess)
        {
            return Result<ProjectMemberResponse>.Failure(access.ErrorDetail ?? new ApplicationErrorDetail(
                "ValidationFailed",
                access.Error ?? "Project membership parent scope is invalid."));
        }

        if (await projects.GetMemberAsync(projectId, request.UserId, cancellationToken) is not null)
        {
            return Result<ProjectMemberResponse>.Failure(new ApplicationErrorDetail(
                "Conflict",
                "User is already a Project member."));
        }

        var member = new ProjectMember
        {
            TenantId = project!.TenantId,
            ProjectId = project.Id,
            UserId = request.UserId,
            User = user,
            Role = request.Role,
            JoinedAt = clock.UtcNow
        };

        await projects.AddMemberAsync(member, cancellationToken);
        var synchronized = await projectGeneral.StageAsync(
            project,
            member,
            request.UserId,
            previousRole: null,
            isCurrentMember: true,
            actorUserId,
            cancellationToken);
        if (!synchronized.IsSuccess)
        {
            unitOfWork.ClearTaskCommandTracking();
            return Result<ProjectMemberResponse>.Failure(synchronized.ErrorDetail!);
        }

        try
        {
            await StageMembershipEvidenceAsync(
                project,
                actorUserId,
                request.UserId,
                "ProjectMemberAdded",
                "membershipChanged",
                cancellationToken);
        }
        catch (Exception exception) when (exception is RequiredOutboxStagingException or InvalidOperationException)
        {
            unitOfWork.ClearTaskCommandTracking();
            return DependencyFailure<ProjectMemberResponse>();
        }

        var save = await unitOfWork.SaveTaskCommandAsync(cancellationToken);
        if (!save.IsSaved)
        {
            unitOfWork.ClearTaskCommandTracking();
            return Conflict<ProjectMemberResponse>();
        }

        return Result<ProjectMemberResponse>.Success(ToResponse(member));
    }

    public async Task<Result<ProjectMemberResponse>> UpdateAsync(
        Guid projectId,
        Guid userId,
        UpdateProjectMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var actorUserId) ||
            !await projectAuthorization.CanManageProject(actorUserId, projectId, cancellationToken))
        {
            return Hidden<ProjectMemberResponse>();
        }

        var project = await projects.GetProjectAsync(projectId, cancellationToken);
        var mutable = EnsureMutable<ProjectMemberResponse>(project);
        if (mutable is not null)
        {
            return mutable;
        }

        var member = await projects.GetMemberAsync(projectId, userId, cancellationToken);
        if (member is null)
        {
            return Result<ProjectMemberResponse>.Failure(new ApplicationErrorDetail(
                "NotFound",
                "Project member not found."));
        }

        var previousRole = member.Role;
        member.Role = request.Role;
        var synchronized = await projectGeneral.StageAsync(
            project!,
            member,
            userId,
            previousRole,
            isCurrentMember: true,
            actorUserId,
            cancellationToken);
        if (!synchronized.IsSuccess)
        {
            unitOfWork.ClearTaskCommandTracking();
            return Result<ProjectMemberResponse>.Failure(synchronized.ErrorDetail!);
        }

        try
        {
            await StageMembershipEvidenceAsync(
                project!,
                actorUserId,
                userId,
                "ProjectMemberUpdated",
                "membershipChanged",
                cancellationToken);
        }
        catch (Exception exception) when (exception is RequiredOutboxStagingException or InvalidOperationException)
        {
            unitOfWork.ClearTaskCommandTracking();
            return DependencyFailure<ProjectMemberResponse>();
        }

        var save = await unitOfWork.SaveTaskCommandAsync(cancellationToken);
        if (!save.IsSaved)
        {
            unitOfWork.ClearTaskCommandTracking();
            return Conflict<ProjectMemberResponse>();
        }

        return Result<ProjectMemberResponse>.Success(ToResponse(member));
    }

    public async Task<Result> RemoveAsync(
        Guid projectId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var actorUserId) ||
            !await projectAuthorization.CanManageProject(actorUserId, projectId, cancellationToken))
        {
            return Hidden();
        }

        var project = await projects.GetProjectAsync(projectId, cancellationToken);
        var mutable = EnsureMutable(project);
        if (mutable is not null)
        {
            return mutable;
        }

        var member = await projects.GetMemberAsync(projectId, userId, cancellationToken);
        if (member is null)
        {
            return Result.Failure(new ApplicationErrorDetail(
                "NotFound",
                "Project member not found."));
        }

        var previousRole = member.Role;
        projects.RemoveMember(member);
        var synchronized = await projectGeneral.StageAsync(
            project!,
            member: null,
            userId,
            previousRole,
            isCurrentMember: false,
            actorUserId,
            cancellationToken);
        if (!synchronized.IsSuccess)
        {
            unitOfWork.ClearTaskCommandTracking();
            return Result.Failure(synchronized.ErrorDetail!);
        }

        try
        {
            await StageMembershipEvidenceAsync(
                project!,
                actorUserId,
                userId,
                "ProjectMemberRemoved",
                "revoked",
                cancellationToken);
        }
        catch (Exception exception) when (exception is RequiredOutboxStagingException or InvalidOperationException)
        {
            unitOfWork.ClearTaskCommandTracking();
            return DependencyFailure();
        }

        var save = await unitOfWork.SaveTaskCommandAsync(cancellationToken);
        if (!save.IsSaved)
        {
            unitOfWork.ClearTaskCommandTracking();
            return Conflict();
        }

        return Result.Success();
    }

    private async Task StageMembershipEvidenceAsync(
        Project project,
        Guid actorUserId,
        Guid affectedUserId,
        string auditAction,
        string authorizationChange,
        CancellationToken cancellationToken)
    {
        await auditLogger.LogAsync(new AuditLogEntry(
            actorUserId,
            auditAction,
            "Project",
            project.Id,
            "Project membership changed.",
            WorkspaceId: project.WorkspaceId,
            ProjectId: project.Id,
            Metadata: new Dictionary<string, object?>
            {
                ["affectedUserId"] = affectedUserId
            },
            TenantId: project.TenantId), cancellationToken);
        await invalidations.ProjectChangedAsync(project, actorUserId, "memberChanged", cancellationToken);
        await authorizationChanges.PublishAsync(
            project.TenantId,
            affectedUserId,
            "project",
            project.Id,
            authorizationChange,
            cancellationToken);
    }

    private async Task<Result> ValidateParentAccessAsync(
        Project project,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var workspaceMember = await workspaces.GetMemberAsync(project.WorkspaceId, userId, cancellationToken);
        if (workspaceMember is not { Status: MembershipStatus.Active })
        {
            return Result.Failure(new ApplicationErrorDetail(
                "ValidationFailed",
                "User must belong to the Workspace before joining the Project."));
        }

        if (project.GroupId.HasValue &&
            await groups.GetMemberAsync(project.GroupId.Value, userId, cancellationToken) is null)
        {
            return Result.Failure(new ApplicationErrorDetail(
                "ValidationFailed",
                "User must belong to the Group before joining the Project."));
        }

        return Result.Success();
    }

    private static Result<T>? EnsureMutable<T>(Project? project)
    {
        if (project is null || project.DeletedAt.HasValue || project.Status == ProjectStatus.Deleted)
        {
            return Result<T>.Failure(new ApplicationErrorDetail(
                "NotFound",
                "The requested resource was not found."));
        }

        if (project.Status == ProjectStatus.Archived)
        {
            return Result<T>.Failure(new ApplicationErrorDetail(
                "InvalidStateTransition",
                "Archived Projects are read-only.",
                Target: "project"));
        }

        return null;
    }

    private static Result? EnsureMutable(Project? project)
    {
        if (project is null || project.DeletedAt.HasValue || project.Status == ProjectStatus.Deleted)
        {
            return Result.Failure(new ApplicationErrorDetail(
                "NotFound",
                "The requested resource was not found."));
        }

        if (project.Status == ProjectStatus.Archived)
        {
            return Result.Failure(new ApplicationErrorDetail(
                "InvalidStateTransition",
                "Archived Projects are read-only.",
                Target: "project"));
        }

        return null;
    }

    private bool TryCurrentUser(out Guid userId)
    {
        userId = currentUser.UserId ?? Guid.Empty;
        return currentUser.IsAuthenticated && currentUser.UserId.HasValue && userId != Guid.Empty;
    }

    private static ProjectMemberResponse ToResponse(ProjectMember member) =>
        new(
            member.UserId,
            member.User?.DisplayName ?? string.Empty,
            member.User?.Email ?? string.Empty,
            member.Role,
            member.JoinedAt);

    private static Result<T> Hidden<T>() =>
        Result<T>.Failure(new ApplicationErrorDetail("NotFound", "The requested resource was not found."));

    private static Result Hidden() =>
        Result.Failure(new ApplicationErrorDetail("NotFound", "The requested resource was not found."));

    private static Result<T> Conflict<T>() =>
        Result<T>.Failure(new ApplicationErrorDetail(
            "PROJECT_CONFLICT",
            "Project state has changed. Refetch and retry."));

    private static Result Conflict() =>
        Result.Failure(new ApplicationErrorDetail(
            "PROJECT_CONFLICT",
            "Project state has changed. Refetch and retry."));

    private static Result<T> DependencyFailure<T>() =>
        Result<T>.Failure(new ApplicationErrorDetail(
            "DependencyUnavailable",
            "Project membership could not be changed safely."));

    private static Result DependencyFailure() =>
        Result.Failure(new ApplicationErrorDetail(
            "DependencyUnavailable",
            "Project membership could not be changed safely."));
}
