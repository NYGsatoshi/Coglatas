using Coglatas.Application.Common;

namespace Coglatas.Application.Artifacts;

public interface IWorkspaceArtifactService
{
    Task<Result<WorkspaceArtifactPageResponse>> ListAsync(
        Guid workspaceId,
        WorkspaceArtifactListRequest request,
        CancellationToken cancellationToken = default);
}

public interface IWorkspaceArtifactRepository
{
    // Return at most PageSize + 1 current authorized rows, in canonical order.
    Task<IReadOnlyList<ArtifactListItemResponse>> ReadAsync(
        WorkspaceArtifactReadQuery query,
        CancellationToken cancellationToken = default);
}

public interface IWorkspaceArtifactCursorCodec
{
    string Encode(WorkspaceArtifactCursor cursor);
    bool TryDecode(string token, out WorkspaceArtifactCursor? cursor);
}
