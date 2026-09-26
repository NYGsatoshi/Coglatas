namespace Coglatas.Application.Workspaces;

/// <summary>
/// Produces the authorized, Tenant-scoped read model used by the active
/// Workspace dashboard. Implementations must keep query count bounded with
/// respect to the number of returned Workspaces.
/// </summary>
public interface IWorkspaceDashboardQuery
{
    bool IsAvailable { get; }

    Task<IReadOnlyList<WorkspaceDashboardListItemResponse>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
