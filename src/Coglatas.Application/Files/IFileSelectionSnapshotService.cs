using Coglatas.Application.Common;
using Coglatas.Application.Search;

namespace Coglatas.Application.Files;

/// <summary>
/// Captures a bounded, actor-bound set of Workspace File identities for a
/// subsequent batch command. Client-side rows and counts are never authority
/// for an all-results mutation.
/// </summary>
public interface IFileSelectionSnapshotService
{
    Task<Result<FileSelectionSnapshotCaptureResponse>> CaptureAsync(
        FileSelectionSnapshotCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<FileSelectionSnapshotDeleteResponse>> DeleteAsync(
        Guid selectionSnapshotId,
        CancellationToken cancellationToken = default);
}

public sealed record FileSelectionSnapshotCreateRequest(
    Guid WorkspaceId,
    string? Q = null,
    FileSearchKind FileKind = FileSearchKind.All,
    DateTimeOffset? FromDate = null,
    bool OnlyMyUploads = false);

public sealed record FileSelectionSnapshotCaptureResponse(
    string Outcome,
    Guid? SelectionSnapshotId,
    int SelectedCount,
    int MaximumSelectionCount,
    DateTimeOffset? ExpiresAt);

public sealed record FileSelectionSnapshotDeleteItemResponse(
    Guid FileObjectId,
    bool Succeeded,
    string Outcome);

public sealed record FileSelectionSnapshotDeleteResponse(
    Guid SelectionSnapshotId,
    int AttemptedCount,
    int SucceededCount,
    int FailedCount,
    IReadOnlyList<FileSelectionSnapshotDeleteItemResponse> Items);
