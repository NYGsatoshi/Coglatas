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
