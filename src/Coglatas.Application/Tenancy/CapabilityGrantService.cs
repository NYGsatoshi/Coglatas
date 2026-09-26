using System.Text.RegularExpressions;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Tenancy;

public sealed record GrantCapabilityRequest(
    Guid SubjectUserId,
    string CapabilityKey,
    CapabilityScopeType ScopeType,
    Guid? ScopeId,
    DateTimeOffset? ExpiresAt);

public sealed record CapabilityGrantResponse(
    Guid Id,
    Guid TenantId,
    Guid SubjectUserId,
    string CapabilityKey,
    CapabilityScopeType ScopeType,
    Guid? ScopeId,
    Guid GrantedByUserId,
    DateTimeOffset GrantedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    long VersionNo);

public interface ICapabilityGrantService
{
    Task<Result<IReadOnlyList<CapabilityGrantResponse>>> ListAsync(CancellationToken cancellationToken = default);

    Task<Result<CapabilityGrantResponse>> GrantAsync(
        GrantCapabilityRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<CapabilityGrantResponse>> RevokeAsync(
        Guid grantId,
        CancellationToken cancellationToken = default);
}

public sealed partial class CapabilityGrantService(
    ICapabilityGrantRepository grants,
    ITenantRepository tenants,
    IWorkspaceRepository workspaces,
    ITenantAuthorizationService tenantAuthorization,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    IClock clock,
    IAuditLogger auditLogger,
    IAuthorizationStateChangePublisher authorizationChanges,
    IUnitOfWork unitOfWork) : ICapabilityGrantService
{
    [GeneratedRegex("^[a-z][a-z0-9]*(?:\\.[a-z0-9][a-z0-9_-]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex CapabilityKeyPattern();

    public async Task<Result<IReadOnlyList<CapabilityGrantResponse>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var authority = await ResolveManagerAsync(cancellationToken);
        if (!authority.IsSuccess)
        {
            return Result<IReadOnlyList<CapabilityGrantResponse>>.Failure(authority.ErrorDetail!);
        }

        var items = await grants.ListAsync(authority.Value!.TenantId, cancellationToken);
        return Result<IReadOnlyList<CapabilityGrantResponse>>.Success(items.Select(ToResponse).ToList());
    }

    public async Task<Result<CapabilityGrantResponse>> GrantAsync(
        GrantCapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        var authority = await ResolveManagerAsync(cancellationToken);
        if (!authority.IsSuccess)
        {
            return Result<CapabilityGrantResponse>.Failure(authority.ErrorDetail!);
        }

        var tenantId = authority.Value!.TenantId;
        var actorUserId = authority.Value.ActorUserId;
        var capabilityKey = request.CapabilityKey?.Trim() ?? string.Empty;
        if (!IsCapabilityKeyValid(capabilityKey))
        {
            return ValidationFailure("Capability key is invalid.", "body.capabilityKey");
        }
        if (request.SubjectUserId == Guid.Empty)
        {
            return ValidationFailure("Subject user is required.", "body.subjectUserId");
        }
        if (!CapabilityGrantEvaluator.IsScopeShapeValid(tenantId, request.ScopeType, request.ScopeId))
        {
            return ValidationFailure("Capability scope is invalid.", "body.scopeId");
        }
        if (capabilityKey == CapabilityKeys.WorkspaceCreate &&
            (request.ScopeType != CapabilityScopeType.Tenant || request.ScopeId != tenantId))
        {
            return ValidationFailure("workspace.create must use the current Tenant scope.", "body.scopeType");
        }

        var now = clock.UtcNow;
        if (request.ExpiresAt.HasValue && request.ExpiresAt.Value <= now)
        {
            return ValidationFailure("Capability expiry must be in the future.", "body.expiresAt");
        }

        var subjectMembership = await tenants.GetTenantUserAsync(tenantId, request.SubjectUserId, cancellationToken);
        var subjectUser = subjectMembership?.User ?? await tenants.GetUserAsync(request.SubjectUserId, cancellationToken);
        if (subjectMembership is not { Status: TenantUserStatus.Active } ||
            subjectUser is not { Status: UserStatus.Active, DeletedAt: null })
        {
            return Result<CapabilityGrantResponse>.Failure(new ApplicationErrorDetail(
                "InvalidSubject",
                "Capability subject must be a current active Tenant user.",
                Target: "body.subjectUserId"));
        }

        if (request.ScopeType == CapabilityScopeType.Workspace)
        {
            var workspace = await workspaces.GetByIdAsync(request.ScopeId!.Value, cancellationToken);
            if (workspace is null ||
                workspace.TenantId != tenantId ||
                workspace.DeletedAt.HasValue ||
                workspace.Status == WorkspaceStatus.Deleted)
            {
                return ValidationFailure("Workspace capability scope is unavailable.", "body.scopeId");
            }
        }

        var grant = await grants.FindSlotAsync(
            tenantId,
            request.SubjectUserId,
            capabilityKey,
            request.ScopeType,
            request.ScopeId,
            cancellationToken);
        if (grant is null)
        {
            grant = new CapabilityGrant
            {
                TenantId = tenantId,
                SubjectUserId = request.SubjectUserId,
                CapabilityKey = capabilityKey,
                ScopeType = request.ScopeType,
                ScopeId = request.ScopeId,
                GrantedByUserId = actorUserId,
                GrantedAt = now,
                ExpiresAt = request.ExpiresAt,
                VersionNo = 1
            };
            await grants.AddAsync(grant, cancellationToken);
        }
        else
        {
            grant.GrantedByUserId = actorUserId;
            grant.GrantedAt = now;
            grant.ExpiresAt = request.ExpiresAt;
            grant.RevokedAt = null;
            grant.VersionNo = checked(grant.VersionNo + 1);
        }

        try
        {
            await auditLogger.LogAsync(new AuditLogEntry(
                actorUserId,
                "CapabilityGranted",
                "CapabilityGrant",
                grant.Id,
                Metadata: new Dictionary<string, object?>
                {
                    ["subjectUserId"] = grant.SubjectUserId,
                    ["capabilityKey"] = grant.CapabilityKey,
                    ["scopeType"] = grant.ScopeType.ToString(),
                    ["scopeId"] = grant.ScopeId,
                    ["expiresAt"] = grant.ExpiresAt
                },
                TenantId: tenantId), cancellationToken);
            await authorizationChanges.PublishAsync(
                tenantId,
                grant.SubjectUserId,
                "capability",
                grant.Id,
                "granted",
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (RequiredOutboxStagingException)
        {
            return DependencyUnavailable();
        }

        return Result<CapabilityGrantResponse>.Success(ToResponse(grant));
    }

    public async Task<Result<CapabilityGrantResponse>> RevokeAsync(
        Guid grantId,
        CancellationToken cancellationToken = default)
    {
        var authority = await ResolveManagerAsync(cancellationToken);
        if (!authority.IsSuccess)
        {
            return Result<CapabilityGrantResponse>.Failure(authority.ErrorDetail!);
        }
        if (grantId == Guid.Empty)
        {
            return Result<CapabilityGrantResponse>.Failure(NotFound());
        }

        var tenantId = authority.Value!.TenantId;
        var actorUserId = authority.Value.ActorUserId;
        var grant = await grants.GetByIdAsync(tenantId, grantId, cancellationToken);
        if (grant is null)
        {
            return Result<CapabilityGrantResponse>.Failure(NotFound());
        }
        if (grant.RevokedAt.HasValue)
        {
            return Result<CapabilityGrantResponse>.Success(ToResponse(grant));
        }

        grant.RevokedAt = clock.UtcNow;
        grant.VersionNo = checked(grant.VersionNo + 1);
        try
        {
            await auditLogger.LogAsync(new AuditLogEntry(
                actorUserId,
                "CapabilityRevoked",
                "CapabilityGrant",
                grant.Id,
                Metadata: new Dictionary<string, object?>
                {
                    ["subjectUserId"] = grant.SubjectUserId,
                    ["capabilityKey"] = grant.CapabilityKey,
                    ["scopeType"] = grant.ScopeType.ToString(),
                    ["scopeId"] = grant.ScopeId
                },
                TenantId: tenantId), cancellationToken);
            await authorizationChanges.PublishAsync(
                tenantId,
                grant.SubjectUserId,
                "capability",
                grant.Id,
                "revoked",
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (RequiredOutboxStagingException)
        {
            return DependencyUnavailable();
        }

        return Result<CapabilityGrantResponse>.Success(ToResponse(grant));
    }

    private async Task<Result<ManagerAuthority>> ResolveManagerAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || !currentUser.UserId.HasValue)
        {
            return Result<ManagerAuthority>.Failure(new ApplicationErrorDetail(
                "AuthenticationRequired",
                "Authentication is required."));
        }
        if (currentTenant is not { IsAvailable: true, IsPlatformScope: false })
        {
            return Result<ManagerAuthority>.Failure(new ApplicationErrorDetail(
                "TenantMembershipRequired",
                "An active Tenant membership is required."));
        }

        var actorUserId = currentUser.UserId.Value;
        if (!await tenantAuthorization.CanManageTenantAsync(actorUserId, currentTenant.TenantId, cancellationToken))
        {
            return Result<ManagerAuthority>.Failure(new ApplicationErrorDetail(
                "CapabilityDenied",
                "Tenant Owner or Admin authority is required.",
                Target: "tenant"));
        }

        return Result<ManagerAuthority>.Success(new ManagerAuthority(currentTenant.TenantId, actorUserId));
    }

    private static bool IsCapabilityKeyValid(string capabilityKey) =>
        capabilityKey.Length is >= 3 and <= 120 && CapabilityKeyPattern().IsMatch(capabilityKey);

    private static Result<CapabilityGrantResponse> ValidationFailure(string message, string target) =>
        Result<CapabilityGrantResponse>.Failure(new ApplicationErrorDetail(
            "ValidationFailed",
            message,
            Target: target));

    private static Result<CapabilityGrantResponse> DependencyUnavailable() =>
        Result<CapabilityGrantResponse>.Failure(new ApplicationErrorDetail(
            "DependencyUnavailable",
            "Capability update could not be committed."));

    private static ApplicationErrorDetail NotFound() =>
        new("NotFound", "The requested resource was not found.");

    private static CapabilityGrantResponse ToResponse(CapabilityGrant grant) =>
        new(
            grant.Id,
            grant.TenantId,
            grant.SubjectUserId,
            grant.CapabilityKey,
            grant.ScopeType,
            grant.ScopeId,
            grant.GrantedByUserId,
            grant.GrantedAt,
            grant.ExpiresAt,
            grant.RevokedAt,
            grant.VersionNo);

    private sealed record ManagerAuthority(Guid TenantId, Guid ActorUserId);
}
