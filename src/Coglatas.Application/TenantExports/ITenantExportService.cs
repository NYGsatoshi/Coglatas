using Coglatas.Application.Common;

namespace Coglatas.Application.TenantExports;

public interface ITenantExportService
{
    Task<Result<TenantExportFileResponse>> ExportAsync(TenantExportRequest request, CancellationToken cancellationToken = default);

    Task<Result<TenantExportJobResponse>> GetJobAsync(Guid exportJobId, CancellationToken cancellationToken = default);
}
