using System.Buffers;
using System.Text;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Workspaces;

public interface IWorkspaceMemberManagementProjectionService
{
    Task<Result<WorkspaceMemberMutationOptionsResponse>> GetOptionsAsync(
        Guid workspaceId, Guid? userId, CancellationToken cancellationToken = default);

    Task<Result<WorkspaceMemberCandidatePageResponse>> ListCandidatesAsync(
        Guid workspaceId, string? prefix, int take = 20, Guid? afterUserId = null,
        CancellationToken cancellationToken = default);
}

public interface IWorkspaceMemberManagementReadRepository
{
    Task<bool> HasActiveTenantIdentityAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
    Task<WorkspaceMemberManagementTargetSnapshot?> GetTargetAsync(
        Guid tenantId, Guid workspaceId, Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkspaceMemberCandidateIdentity>> ListCandidatesAsync(
        Guid tenantId, Guid workspaceId, string prefix, int take, Guid? afterUserId,
        CancellationToken cancellationToken);
}

public sealed record WorkspaceMemberManagementTargetSnapshot(
    Guid UserId, WorkspaceRole Role, MembershipStatus Status,
    bool HasReadableIdentity, bool HasActiveTenantIdentity);

public sealed record WorkspaceMemberCandidateIdentity(Guid UserId, string DisplayName);

/// <summary>Bounded previews of existing command prerequisites, never write authority.</summary>
public sealed class WorkspaceMemberManagementProjectionService(
    IWorkspaceMemberManagementReadRepository reads,
    IWorkspaceAuthorizationService authorization,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant) : IWorkspaceMemberManagementProjectionService
{
    private static readonly IReadOnlyList<WorkspaceRole> AllowedRoles = Array.AsReadOnly(
    [
        WorkspaceRole.Owner, WorkspaceRole.Admin, WorkspaceRole.Adviser,
        WorkspaceRole.Member, WorkspaceRole.ReadOnly
    ]);

    public async Task<Result<WorkspaceMemberMutationOptionsResponse>> GetOptionsAsync(
        Guid workspaceId, Guid? userId, CancellationToken cancellationToken = default)
    {
        var access = await AuthorizeAsync(workspaceId, cancellationToken);
        if (!access.IsSuccess)
            return Result<WorkspaceMemberMutationOptionsResponse>.Failure(access.ErrorDetail!);

        if (userId is null)
            return Result<WorkspaceMemberMutationOptionsResponse>.Success(new(workspaceId, true, null));

        var target = await reads.GetTargetAsync(currentTenant.TenantId, workspaceId, userId.Value, cancellationToken);
        if (target is not { HasReadableIdentity: true })
            return Result<WorkspaceMemberMutationOptionsResponse>.Failure(NotFound());
        if (!Enum.IsDefined(target.Role) || !Enum.IsDefined(target.Status))
            return Result<WorkspaceMemberMutationOptionsResponse>.Failure(new ApplicationErrorDetail(
                "DependencyUnavailable", "Workspace member options are temporarily unavailable."));

        // A role-only PATCH preserves Status. Active rows must still satisfy the
        // existing general synchronizer's account/Tenant admission; inactive rows
        // remain on its removal path and do not imply reactivation.
        var canChangeRole = target.Status != MembershipStatus.Active || target.HasActiveTenantIdentity;
        var options = new WorkspaceMemberTargetOptionsResponse(target.UserId, target.Role, target.Status,
            canChangeRole, canChangeRole ? AllowedRoles : Array.Empty<WorkspaceRole>(), true);
        return Result<WorkspaceMemberMutationOptionsResponse>.Success(new(workspaceId, true, options));
    }

    public async Task<Result<WorkspaceMemberCandidatePageResponse>> ListCandidatesAsync(
        Guid workspaceId, string? prefix, int take = 20, Guid? afterUserId = null,
        CancellationToken cancellationToken = default)
    {
        var access = await AuthorizeAsync(workspaceId, cancellationToken);
        if (!access.IsSuccess)
            return Result<WorkspaceMemberCandidatePageResponse>.Failure(access.ErrorDetail!);

        prefix = prefix?.Trim();
        if (!ValidPrefix(prefix))
            return Result<WorkspaceMemberCandidatePageResponse>.Failure(Invalid(
                "q", "A display-name prefix of 2 to 100 Unicode scalars is required."));
        if (take is < 1 or > 50)
            return Result<WorkspaceMemberCandidatePageResponse>.Failure(Invalid("take", "Take must be between 1 and 50."));
        if (afterUserId == Guid.Empty)
            return Result<WorkspaceMemberCandidatePageResponse>.Failure(Invalid("afterUserId", "Continuation position is invalid."));

        var rows = await reads.ListCandidatesAsync(currentTenant.TenantId, workspaceId, prefix!,
            take + 1, afterUserId, cancellationToken);
        var hasMore = rows.Count > take;
        var items = rows.Take(take).Select(row => new WorkspaceMemberCandidateResponse(
            row.UserId, row.DisplayName, AllowedRoles)).ToArray();
        return Result<WorkspaceMemberCandidatePageResponse>.Success(new(workspaceId, items, hasMore,
            hasMore ? items[^1].UserId : null));
    }

    private async Task<Result> AuthorizeAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (!CurrentUserIdentity.TryGetAuthenticatedUserId(currentUser, out var actorId))
            return Result.Failure(new ApplicationErrorDetail("AuthenticationRequired", "Authentication is required."));
        if (currentTenant is not { IsAvailable: true, IsPlatformScope: false } ||
            currentTenant.TenantId == Guid.Empty)
            return Result.Failure(NotFound());
        if (!await reads.HasActiveTenantIdentityAsync(currentTenant.TenantId, actorId, cancellationToken))
            return Result.Failure(new ApplicationErrorDetail("AuthenticationRequired", "Authentication is required."));
        if (!await authorization.CanViewWorkspace(actorId, workspaceId, cancellationToken))
            return Result.Failure(NotFound());
        if (!await authorization.CanManageWorkspace(actorId, workspaceId, cancellationToken))
            return Result.Failure(new ApplicationErrorDetail("CapabilityDenied", "You are not allowed to manage members.", Target: "workspace"));
        return Result.Success();
    }

    private static bool ValidPrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return false;
        var remaining = prefix.AsSpan();
        var count = 0;
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var rune, out var consumed) != OperationStatus.Done ||
                rune.Value == 0 || ++count > 100) return false;
            remaining = remaining[consumed..];
        }
        return count >= 2;
    }

    private static ApplicationErrorDetail NotFound() => new("NotFound", "The requested resource was not found.");
    private static ApplicationErrorDetail Invalid(string target, string message) => new("ValidationFailed", message, Target: target);
}
