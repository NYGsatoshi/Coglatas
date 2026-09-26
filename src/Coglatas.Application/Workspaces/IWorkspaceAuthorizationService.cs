namespace Coglatas.Application.Workspaces;

public interface IWorkspaceAuthorizationService
{
    Task<bool> CanViewWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default);

    Task<bool> CanContributeWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default);

    Task<bool> CanManageWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates Workspace governance authority for read-scope decisions.
    /// Unlike CanManageWorkspace, this may remain true for an archived
    /// Workspace because archival is read-only rather than an authority eraser.
    /// </summary>
    Task<bool> CanGovernWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default) =>
        CanManageWorkspace(userId, workspaceId, cancellationToken);

    /// <summary>
    /// Canonical archived-Workspace restore authority. Implementations that do
    /// not explicitly support restore fail closed.
    /// </summary>
    Task<bool> CanRestoreWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    Task<bool> CanCreateWorkspace(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);
}
