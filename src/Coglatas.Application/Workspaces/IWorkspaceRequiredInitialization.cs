using Coglatas.Application.Common;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.Workspaces;

/// <summary>
/// Stages the required canonical Workspace defaults in the caller-owned
/// transaction. Implementations must not save or commit independently.
/// </summary>
public interface IWorkspaceRequiredInitialization
{
    bool IsAvailable { get; }

    Task<Result> StageAsync(
        Workspace workspace,
        Guid creatorUserId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Fail-closed registration used until Messaging supplies a canonical,
/// idempotent WorkspaceChannel general provisioner.
/// </summary>
public sealed class UnavailableWorkspaceRequiredInitialization : IWorkspaceRequiredInitialization
{
    public bool IsAvailable => false;

    public Task<Result> StageAsync(
        Workspace workspace,
        Guid creatorUserId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure("Required Workspace initialization is unavailable."));
}
