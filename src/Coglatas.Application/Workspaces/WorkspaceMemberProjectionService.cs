using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Workspaces;

/// <summary>
/// Owns the privacy boundary for Workspace member reads. Ordinary member surfaces and
/// membership-management surfaces intentionally use different response contracts.
/// </summary>
public interface IWorkspaceMemberProjectionService
{
    Task<Result<IReadOnlyList<WorkspaceMemberResponse>>> ListAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    Task<Result<WorkspaceMemberResponse>> GetAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<WorkspaceMemberManagementResponse>>> ListManagementAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    Task<Result<WorkspaceMemberManagementResponse>> GetManagementAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken cancellationToken = default);
}

public sealed class WorkspaceMemberProjectionService(
    IWorkspaceRepository workspaces,
    IWorkspaceAuthorizationService authorization,
    ICurrentUser currentUser) : IWorkspaceMemberProjectionService
{
    public async Task<Result<IReadOnlyList<WorkspaceMemberResponse>>> ListAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var actorUserId) ||
            !await authorization.CanViewWorkspace(actorUserId, workspaceId, cancellationToken))
        {
            return Result<IReadOnlyList<WorkspaceMemberResponse>>.Failure(NotFoundError());
        }

        var members = await workspaces.ListMembersAsync(workspaceId, cancellationToken);
        var projection = members
            .Where(IsOrdinaryVisible)
            .Select(ToMinimal)
            .ToList();

        return Result<IReadOnlyList<WorkspaceMemberResponse>>.Success(projection);
    }

    public async Task<Result<WorkspaceMemberResponse>> GetAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var actorUserId) ||
            !await authorization.CanViewWorkspace(actorUserId, workspaceId, cancellationToken))
        {
            return Result<WorkspaceMemberResponse>.Failure(NotFoundError());
        }

        var member = await workspaces.GetMemberAsync(workspaceId, userId, cancellationToken);
        return member is not null && IsOrdinaryVisible(member)
            ? Result<WorkspaceMemberResponse>.Success(ToMinimal(member))
            : Result<WorkspaceMemberResponse>.Failure(NotFoundError());
    }

    public async Task<Result<IReadOnlyList<WorkspaceMemberManagementResponse>>> ListManagementAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        var access = await AuthorizeManagementAsync(workspaceId, cancellationToken);
        if (!access.IsSuccess)
        {
            return Result<IReadOnlyList<WorkspaceMemberManagementResponse>>.Failure(access.ErrorDetail!);
        }

        var members = await workspaces.ListMembersAsync(workspaceId, cancellationToken);
        var projection = members
            .Where(HasReadableIdentity)
            .Select(ToManagement)
            .ToList();

        return Result<IReadOnlyList<WorkspaceMemberManagementResponse>>.Success(projection);
    }

    public async Task<Result<WorkspaceMemberManagementResponse>> GetManagementAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var access = await AuthorizeManagementAsync(workspaceId, cancellationToken);
        if (!access.IsSuccess)
        {
            return Result<WorkspaceMemberManagementResponse>.Failure(access.ErrorDetail!);
        }

        var member = await workspaces.GetMemberAsync(workspaceId, userId, cancellationToken);
        return member is not null && HasReadableIdentity(member)
            ? Result<WorkspaceMemberManagementResponse>.Success(ToManagement(member))
            : Result<WorkspaceMemberManagementResponse>.Failure(NotFoundError());
    }

    private async Task<Result> AuthorizeManagementAsync(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        if (!TryCurrentUser(out var actorUserId))
        {
            return Result.Failure(AuthenticationRequiredError());
        }

        // Establish current record-level visibility first so a wrong/cross-scope Workspace
        // is indistinguishable from a missing resource.
        if (!await authorization.CanViewWorkspace(actorUserId, workspaceId, cancellationToken))
        {
            return Result.Failure(NotFoundError());
        }

        if (!await authorization.CanManageWorkspace(actorUserId, workspaceId, cancellationToken))
        {
            return Result.Failure(CapabilityDeniedError());
        }

        return Result.Success();
    }

    private bool TryCurrentUser(out Guid userId)
    {
        userId = currentUser.UserId ?? Guid.Empty;
        return currentUser.IsAuthenticated &&
               currentUser.UserId.HasValue &&
               userId != Guid.Empty;
    }

    private static bool IsOrdinaryVisible(WorkspaceMember member) =>
        member.Status == MembershipStatus.Active &&
        member.User is { DeletedAt: null, Status: UserStatus.Active };

    private static bool HasReadableIdentity(WorkspaceMember member) =>
        member.User is { DeletedAt: null };

    private static WorkspaceMemberResponse ToMinimal(WorkspaceMember member) =>
        new(
            member.UserId,
            member.User?.DisplayName ?? string.Empty,
            member.Role,
            member.Status);

    private static WorkspaceMemberManagementResponse ToManagement(WorkspaceMember member) =>
        new(
            member.UserId,
            member.User?.DisplayName ?? string.Empty,
            member.User?.Email ?? string.Empty,
            member.Role,
            member.Status,
            member.User?.Status ?? UserStatus.Suspended,
            member.JoinedAt);

    private static ApplicationErrorDetail AuthenticationRequiredError() =>
        new("AuthenticationRequired", "Authentication is required.");

    private static ApplicationErrorDetail NotFoundError() =>
        new("NotFound", "The requested resource was not found.");

    private static ApplicationErrorDetail CapabilityDeniedError() =>
        new("CapabilityDenied", "You are not allowed to manage members.", Target: "workspace");
}
