using Coglatas.Application.Common;

namespace Coglatas.Application.Files;

public interface IFileService
{
    Task<Result<AttachmentResponse>> UploadAsync(AttachmentUploadInput input, CancellationToken cancellationToken = default);

    Task<Result<AttachmentResponse>> GetAsync(Guid attachmentId, CancellationToken cancellationToken = default);

    Task<Result<FileDownloadGrantResponse>> RequestDownloadGrantAsync(
        Guid attachmentId,
        FileDownloadGrantRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<FileDownloadResponse>> DownloadWithGrantAsync(
        Guid fileDownloadGrantId,
        string token,
        CancellationToken cancellationToken = default);

    Task<Result<FileDownloadResponse>> DownloadAsync(Guid attachmentId, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid attachmentId, CancellationToken cancellationToken = default);
}
