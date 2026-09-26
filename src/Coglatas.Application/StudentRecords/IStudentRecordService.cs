using Coglatas.Application.Common;

namespace Coglatas.Application.StudentRecords;

public interface IStudentRecordService
{
    Task<Result<StudentRecordPublicResponse>> GetPublicAsync(Guid studentRecordId, CancellationToken cancellationToken = default);
    Task<Result<StudentRecordRestrictedResponse>> GetRestrictedAsync(Guid studentRecordId, StudentRecordRestrictedRequest request, CancellationToken cancellationToken = default);
    Task<Result<StudentRecordExportGrantResponse>> RequestRestrictedExportAsync(Guid studentRecordId, StudentRecordExportRequest request, CancellationToken cancellationToken = default);
    Task<Result<StudentRecordExportPackageResponse>> BuildRestrictedExportAsync(Guid exportPackageGrantId, CancellationToken cancellationToken = default);
    Task<Result<StudentRecordExportPackageResponse>> DownloadRestrictedExportAsync(Guid exportPackageGrantId, CancellationToken cancellationToken = default);
}
