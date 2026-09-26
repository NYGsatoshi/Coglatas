using Coglatas.Application.Common;

namespace Coglatas.Application.Audit;

public sealed record AuditPackageSectionPreviewResponse(
    string Key,
    string Label,
    int ItemCount);

public sealed record AuditPackageExportPreviewResponse(
    Guid ArtifactId,
    Guid ArtifactVersionId,
    int ArtifactVersionNumber,
    string ArtifactTitle,
    string ScopeLabel,
    bool CanExport,
    bool SensitiveMetadataIncluded,
    IReadOnlyList<AuditPackageSectionPreviewResponse> Sections);

public sealed record QueueAuditPackageExportRequest(Guid ArtifactVersionId);

public sealed record AuditPackageExportJobResponse(
    Guid JobId,
    Guid ArtifactVersionId,
    string FileName,
    string State,
    int ProgressPercent,
    string? ErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public sealed record AuditPackageExportDownloadResponse(
    string FileName,
    string ContentType,
    Stream Content);

public interface IAuditPackageExportService
{
    Task<Result<AuditPackageExportPreviewResponse>> PreviewAsync(
        Guid artifactVersionId,
        CancellationToken cancellationToken = default);

    Task<Result<AuditPackageExportJobResponse>> QueueAsync(
        QueueAuditPackageExportRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<AuditPackageExportJobResponse>> GetJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    Task<Result<AuditPackageExportJobResponse>> RetryAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    Task<Result<AuditPackageExportDownloadResponse>> DownloadAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);
}

public interface IAuditPackageExportProcessor
{
    Task<IReadOnlyList<Guid>> ListQueuedTenantIdsAsync(
        int take,
        DateTimeOffset staleBefore,
        CancellationToken cancellationToken = default);

    Task<int> RecoverStaleRunningAsync(
        DateTimeOffset staleBefore,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> ListQueuedJobIdsAsync(
        int take,
        CancellationToken cancellationToken = default);

    Task ProcessAsync(Guid jobId, CancellationToken cancellationToken = default);
}
