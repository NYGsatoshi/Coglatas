using Coglatas.Application.Common;

namespace Coglatas.Application.Files;

public interface IFileObjectService
{
    Task<Result<AttachmentResponse>> UploadAsync(AttachmentUploadInput input, CancellationToken cancellationToken = default);

    Task<Result<PagedResponse<FileListItemResponse>>> ListFileObjectsAsync(
        Guid workspaceId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // Default is fail closed for adapters that have not adopted the private review candidate.
    Task<Result<PagedResponse<FilteredFileListItemResponse>>> ListFilteredFileObjectsAsync(
        Guid workspaceId, string? scope, Guid? projectId, Guid? folderId, bool? atRoot,
        int page, int pageSize, string? sort, string? direction,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<PagedResponse<FilteredFileListItemResponse>>.Failure(
            new ApplicationErrorDetail("FileScopeUnavailable", "Files not found.")));

    Task<Result<FileObjectResponse>> GetFileObjectAsync(Guid fileObjectId, CancellationToken cancellationToken = default);

    Task<Result<FileDownloadGrantResponse>> RequestFileObjectDownloadGrantAsync(
        Guid fileObjectId,
        FileDownloadGrantRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<FileDownloadResponse>> DownloadFileObjectWithGrantAsync(
        Guid fileDownloadGrantId,
        string token,
        CancellationToken cancellationToken = default);

    Task<Result<FileDownloadResponse>> DownloadFileObjectAsync(Guid fileObjectId, CancellationToken cancellationToken = default);

    Task<Result> DeleteFileObjectAsync(Guid fileObjectId, string? reason = null, CancellationToken cancellationToken = default);
}
