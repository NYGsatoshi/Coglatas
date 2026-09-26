using System.Text.Json.Serialization;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Realtime;
using Coglatas.Application.Tenancy;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Projects;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProjectVisibilityMutationRequest(
    [property: JsonRequired]
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    ProjectVisibility Visibility,
    [property: JsonRequired] long ExpectedVersion);

public sealed record ProjectVisibilityMutationResponse(
    Guid ProjectId,
    ProjectVisibility Visibility,
    long VersionNo);

public interface IProjectVisibilityService
{
    Task<Result<ProjectVisibilityMutationResponse>> UpdateAsync(
        Guid projectId,
        ProjectVisibilityMutationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Canonical explicit Project Visibility classification/mutation boundary.
/// Historical LegacyUnknown rows are never inferred: a caller must name the
/// public value and pass current authorization plus the Project concurrency token.
/// </summary>
public sealed class ProjectVisibilityService(
    IProjectRepository projects,
    IWorkspaceRepository workspaces,
    ICapabilityGrantEvaluator capabilityGrants,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IAuditLogger auditLogger,
    IBusinessInvalidationPublisher invalidations,
    IAuthorizationStateChangePublisher authorizationChanges,
    ITaskCommandUnitOfWork unitOfWork) : IProjectVisibilityService
{
    public async Task<Result<ProjectVisibilityMutationResponse>> UpdateAsync(
        Guid projectId,
        ProjectVisibilityMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var actorUserId))
        {
            return Failure("AuthenticationRequired", "Authentication is required.");
        }

        if (currentTenant is not { IsAvailable: true, IsPlatformScope: false } || currentTenant.TenantId == Guid.Empty)
        {
            return Failure("TenantMembershipRequired", "An active Tenant membership is required.");
        }

        if (!Enum.IsDefined(typeof(ProjectVisibility), request.Visibility))
        {
            return Failure("ValidationFailed", "Project visibility is invalid.", "body.visibility");
        }

        var tenantId = currentTenant.TenantId;
        var project = await projects.GetProjectAsync(projectId, cancellationToken);
        if (project is null ||
            project.TenantId != tenantId ||
            project.DeletedAt.HasValue ||
            project.Status == ProjectStatus.Deleted)
        {
            return NotFound();
        }

        var workspace = await workspaces.GetByIdAsync(project.WorkspaceId, cancellationToken);
        var workspaceMembership = await workspaces.GetMemberAsync(project.WorkspaceId, actorUserId, cancellationToken);
        if (workspace is null ||
            workspace.TenantId != tenantId ||
            workspace.DeletedAt.HasValue ||
            workspace.Status == WorkspaceStatus.Deleted ||
            workspaceMembership is not { Status: MembershipStatus.Active } ||
            workspaceMembership.TenantId != tenantId)
        {
            return NotFound();
        }

        var hasWorkspaceGovernance = workspaceMembership.Role.CanManage();
        var hasVisibilityCapability = await HasScopedCapabilityAsync(
            actorUserId,
            tenantId,
            project.WorkspaceId,
            cancellationToken);
        if (!hasWorkspaceGovernance && !hasVisibilityCapability)
        {
            return Failure(
                "CapabilityDenied",
                "You are not allowed to change Project visibility.",
                "body.visibility");
        }

        if (workspace.Status != WorkspaceStatus.Active || project.Status == ProjectStatus.Archived)
        {
            return Failure(
                "InvalidStateTransition",
                "Archived Workspaces or Projects are read-only.",
                "project");
        }

        if (request.ExpectedVersion <= 0)
        {
            return Failure("ValidationFailed", "ExpectedVersion must be a positive integer.", "body.expectedVersion");
        }
        if (project.VersionNo != request.ExpectedVersion)
        {
            return Failure(
                "ConcurrentModification",
                "Project has changed. Refetch and retry.",
                "body.expectedVersion");
        }

        if (project.Visibility == request.Visibility)
        {
            return Result<ProjectVisibilityMutationResponse>.Success(
                new ProjectVisibilityMutationResponse(project.Id, request.Visibility, project.VersionNo));
        }

        var previousVisibility = project.Visibility;
        var affectedUsers = (await projects.ListCurrentReaderUserIdsAsync(project.Id, cancellationToken))
            .Where(userId => userId != Guid.Empty)
            .ToHashSet();
        affectedUsers.Add(actorUserId);

        var becomesBroadlyReadable = request.Visibility == ProjectVisibility.WorkspaceVisible &&
                                     project.ActivationState == ProjectActivationState.Activated &&
                                     project.Status is ProjectStatus.Active or ProjectStatus.Review or ProjectStatus.Completed;
        if (becomesBroadlyReadable)
        {
            foreach (var workspaceMember in await workspaces.ListMembersAsync(project.WorkspaceId, cancellationToken))
            {
                if (workspaceMember.Status == MembershipStatus.Active)
                {
                    affectedUsers.Add(workspaceMember.UserId);
                }
            }
        }

        project.Visibility = request.Visibility;
        try
        {
            await auditLogger.LogAsync(new AuditLogEntry(
                actorUserId,
                "ProjectVisibilityChanged",
                "Project",
                project.Id,
                "Project visibility changed by an explicit authorized command.",
                WorkspaceId: project.WorkspaceId,
                ProjectId: project.Id,
                Metadata: new Dictionary<string, object?>
                {
                    ["previousVisibility"] = previousVisibility?.ToString() ?? "LegacyUnknown",
                    ["visibility"] = request.Visibility.ToString(),
                    ["versionBefore"] = request.ExpectedVersion
                },
                TenantId: project.TenantId), cancellationToken);
            await invalidations.ProjectChangedAsync(project, actorUserId, "visibilityChanged", cancellationToken);
            foreach (var affectedUserId in affectedUsers.OrderBy(userId => userId))
            {
                await authorizationChanges.PublishAsync(
                    project.TenantId,
                    affectedUserId,
                    "project",
                    project.Id,
                    "visibilityChanged",
                    cancellationToken);
            }
        }
        catch (Exception exception) when (exception is RequiredOutboxStagingException or InvalidOperationException)
        {
            unitOfWork.ClearTaskCommandTracking();
            return Failure("DependencyUnavailable", "Project visibility could not be changed safely.");
        }

        var save = await unitOfWork.SaveTaskCommandAsync(cancellationToken);
        if (!save.IsSaved)
        {
            unitOfWork.ClearTaskCommandTracking();
            return Failure(
                "ConcurrentModification",
                "Project has changed. Refetch and retry.",
                "body.expectedVersion");
        }

        return Result<ProjectVisibilityMutationResponse>.Success(
            new ProjectVisibilityMutationResponse(project.Id, request.Visibility, project.VersionNo));
    }

    private async Task<bool> HasScopedCapabilityAsync(
        Guid actorUserId,
        Guid tenantId,
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        if (await capabilityGrants.HasActiveGrantAsync(
                actorUserId,
                tenantId,
                CapabilityKeys.ProjectVisibilityManage,
                CapabilityScopeType.Workspace,
                workspaceId,
                cancellationToken))
        {
            return true;
        }

        return await capabilityGrants.HasActiveGrantAsync(
            actorUserId,
            tenantId,
            CapabilityKeys.ProjectVisibilityManage,
            CapabilityScopeType.Tenant,
            tenantId,
            cancellationToken);
    }

    private bool TryCurrentUser(out Guid userId)
    {
        userId = currentUser.UserId ?? Guid.Empty;
        return currentUser.IsAuthenticated && currentUser.UserId.HasValue && userId != Guid.Empty;
    }

    private static Result<ProjectVisibilityMutationResponse> NotFound() =>
        Failure("NotFound", "The requested resource was not found.");

    private static Result<ProjectVisibilityMutationResponse> Failure(
        string code,
        string message,
        string? target = null) =>
        Result<ProjectVisibilityMutationResponse>.Failure(
            new ApplicationErrorDetail(code, message, Target: target));
}
