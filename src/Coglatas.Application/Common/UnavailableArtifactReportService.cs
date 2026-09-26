using Coglatas.Application.Artifacts;
namespace Coglatas.Application.Common;
public sealed class UnavailableArtifactReportService : IArtifactReportService
{
    private static Result<T> Fail<T>() => Result<T>.Failure(new ApplicationErrorDetail("ReportUnavailable", "The report service is unavailable."));
    public Task<Result<ArtifactReportAttachedResponse>> AttachAsync(Guid id, AttachArtifactReportRequest request, CancellationToken ct = default) => Task.FromResult(Fail<ArtifactReportAttachedResponse>());
    public Task<Result<ArtifactReportResponse>> GetAsync(Guid projectId, Guid id, Guid? taskId, CancellationToken ct = default) => Task.FromResult(Fail<ArtifactReportResponse>());
}
