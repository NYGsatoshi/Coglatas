using Coglatas.Application.Common;

namespace Coglatas.Application.Workspaces;

public interface IWorkspaceService
{
    Task<Result<IReadOnlyList<WorkspaceDashboardListItemResponse>>> ListAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<WorkspaceListItemResponse>>> ListArchivedAsync(CancellationToken cancellationToken = default);
    Task<Result<WorkspaceCapabilitiesResponse>> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
    Task<Result<WorkspaceDetailResponse>> CreateAsync(CreateWorkspaceRequest request, string? clientRequestIdentity, CancellationToken cancellationToken = default);
    Task<Result<WorkspaceDetailResponse>> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<Result<WorkspaceDetailResponse>> UpdateAsync(Guid workspaceId, UpdateWorkspaceRequest request, CancellationToken cancellationToken = default);
    Task<Result> ArchiveAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<Result> RestoreAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<WorkspaceMemberResponse>>> ListMembersAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<Result<WorkspaceMemberResponse>> AddMemberAsync(Guid workspaceId, AddWorkspaceMemberRequest request, CancellationToken cancellationToken = default);
    Task<Result<WorkspaceMemberResponse>> UpdateMemberAsync(Guid workspaceId, Guid userId, UpdateWorkspaceMemberRequest request, CancellationToken cancellationToken = default);
    Task<Result> RemoveMemberAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default);
}
