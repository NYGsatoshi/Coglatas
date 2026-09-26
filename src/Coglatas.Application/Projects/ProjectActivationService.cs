using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Projects;

/// <summary>
/// Canonical explicit first-activation command (WPC-DEC-032 / WPC-DEC-033).
/// Database-dependent validation and all required effects execute inside one
/// activation-owned serializable transaction.
/// </summary>
public sealed class ProjectActivationService(
    IProjectRepository projects,
    IWorkspaceRepository workspaces,
    ITenantRepository tenants,
    IProjectAuthorizationService authorization,
    IProjectGeneralActivationProvisioner projectGeneral,
    IProjectTaskWorkflowActivationProvisioner taskWorkflow,
    IProjectActivationUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IClock clock,
    IAuditLogger auditLogger,
    IBusinessInvalidationPublisher invalidations) : IProjectActivationService
{
    public const int CanonicalActivationVersion = 1;

    public async Task<Result> ActivateAsync(
        Guid projectId,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId))
        {
            return Failure("AuthenticationRequired", "Authentication is required.");
        }

        if (currentTenant is not { IsAvailable: true, IsPlatformScope: false } ||
            currentTenant.TenantId == Guid.Empty)
        {
            return Failure("TenantMembershipRequired", "An active Tenant membership is required.");
        }

        if (projectId == Guid.Empty)
        {
            return NotFound();
        }

        if (expectedVersion <= 0)
        {
            return Failure(
                "ValidationFailed",
                "ExpectedVersion must be a positive integer.",
                "body.expectedVersion");
        }

        var tenantId = currentTenant.TenantId;
        return await unitOfWork.ExecuteActivationAsync(
            token => ActivateInsideTransactionAsync(
                projectId,
                expectedVersion,
                userId,
                tenantId,
                token),
            cancellationToken);
    }

    private async Task<Result> ActivateInsideTransactionAsync(
        Guid projectId,
        long expectedVersion,
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        // Every database-dependent authority/scope check is deliberately inside
        // the activation transaction so there is one serializable business view.
        var tenantMembership = await tenants.GetTenantUserAsync(tenantId, userId, cancellationToken);
        if (tenantMembership is not { Status: TenantUserStatus.Active })
        {
            return Failure("TenantMembershipRequired", "An active Tenant membership is required.");
        }

        var tenant = tenantMembership.Tenant ?? await tenants.GetTenantAsync(tenantId, cancellationToken);
        var user = tenantMembership.User ?? await tenants.GetUserAsync(userId, cancellationToken);
        if (tenant is not { Status: TenantStatus.Active, DeletedAt: null } ||
            user is not { Status: UserStatus.Active, DeletedAt: null })
        {
            return Failure("TenantMembershipRequired", "An active Tenant membership is required.");
        }

        var project = await projects.GetProjectAsync(projectId, cancellationToken);
        if (project is null ||
            project.TenantId != tenantId ||
            project.DeletedAt.HasValue ||
            project.Status == ProjectStatus.Deleted)
        {
            return NotFound();
        }

        var workspace = await workspaces.GetByIdAsync(project.WorkspaceId, cancellationToken);
        if (workspace is null ||
            workspace.TenantId != tenantId ||
            workspace.DeletedAt.HasValue ||
            workspace.Status == WorkspaceStatus.Deleted)
        {
            return NotFound();
        }

        var workspaceMembership = await workspaces.GetMemberAsync(project.WorkspaceId, userId, cancellationToken);
        if (workspaceMembership is not { Status: MembershipStatus.Active } ||
            workspaceMembership.TenantId != tenantId)
        {
            // Project membership and platform role do not outlive the current
            // Workspace authorization boundary for activation.
            return NotFound();
        }

        if (workspace.Status != WorkspaceStatus.Active)
        {
            return Failure(
                "InvalidStateTransition",
                "Project activation requires an active Workspace.",
                "workspace.status");
        }

        if (!await authorization.CanManageProject(userId, projectId, cancellationToken))
        {
            return Failure("CapabilityDenied", "You are not allowed to activate this Project.", "project");
        }

        if (project.VersionNo != expectedVersion)
        {
            return Failure(
                "ConcurrentModification",
                "Project has changed. Refetch the Project before activation.",
                "body.expectedVersion");
        }

        if (!project.Visibility.HasValue)
        {
            // NULL is WPC-02A's internal LegacyUnknown visibility. First
            // activation must never infer a canonical visibility classification.
            return Failure(
                "InvalidStateTransition",
                "Project visibility must be explicitly classified before activation.",
                "project.visibility");
        }

        if (project.ActivationState != ProjectActivationState.NeverActivated ||
            project.Status != ProjectStatus.Planning ||
            project.ActivatedAtUtc.HasValue ||
            project.ActivationVersion.HasValue)
        {
            return Failure(
                "InvalidStateTransition",
                "Only a canonical NeverActivated Planning Project can be activated.",
                "project.status");
        }

        try
        {
            var generalResult = await projectGeneral.StageAsync(project, userId, cancellationToken);
            if (!generalResult.IsSuccess)
            {
                return NormalizeDependencyFailure(generalResult, "ProjectGeneral provisioning failed.");
            }
        }
        catch (RequiredOutboxStagingException)
        {
            return Failure("DependencyUnavailable", "Project activation is temporarily unavailable.");
        }

        // The configured workflow adapter reuses the same scoped AppDbContext and
        // current transaction, so Workspace/Tenant defaults and template rows are
        // read from the same serializable snapshot as authorization and Project state.
        var workflowResult = await taskWorkflow.StageAsync(project, cancellationToken);
        if (!workflowResult.IsSuccess)
        {
            return workflowResult;
        }

        var activatedAt = clock.UtcNow;
        project.ActivationState = ProjectActivationState.Activated;
        project.ActivatedAtUtc = activatedAt;
        project.ActivationVersion = CanonicalActivationVersion;
        project.Status = ProjectStatus.Active;

        await auditLogger.LogAsync(
            new AuditLogEntry(
                userId,
                "ProjectActivated",
                "Project",
                project.Id,
                WorkspaceId: project.WorkspaceId,
                GroupId: project.GroupId,
                ProjectId: project.Id,
                TenantId: project.TenantId),
            cancellationToken);

        try
        {
            await invalidations.ProjectChangedAsync(project, userId, "activated", cancellationToken);
        }
        catch (RequiredOutboxStagingException)
        {
            return Failure("DependencyUnavailable", "Project activation is temporarily unavailable.");
        }
        catch (InvalidOperationException)
        {
            // BusinessInvalidationPublisher throws when its required durable
            // event cannot be staged. The transaction executor rolls back every
            // staged activation effect when this result is returned.
            return Failure("DependencyUnavailable", "Project activation is temporarily unavailable.");
        }

        // The unit of work saves and commits only after this callback succeeds.
        return Result.Success();
    }

    private bool TryCurrentUser(out Guid userId)
    {
        userId = currentUser.UserId ?? Guid.Empty;
        return currentUser.IsAuthenticated && currentUser.UserId.HasValue && userId != Guid.Empty;
    }

    private static Result NormalizeDependencyFailure(Result result, string fallback)
    {
        if (result.ErrorDetail is not null)
        {
            return Result.Failure(result.ErrorDetail);
        }

        return Result.Failure(new ApplicationErrorDetail(
            "DependencyUnavailable",
            string.IsNullOrWhiteSpace(result.Error) ? fallback : result.Error));
    }

    private static Result NotFound() =>
        Failure("NotFound", "The requested resource was not found.");

    private static Result Failure(string code, string message, string? target = null) =>
        Result.Failure(new ApplicationErrorDetail(code, message, Target: target));
}
