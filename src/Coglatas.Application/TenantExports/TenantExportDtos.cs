using Coglatas.Domain.Enums;

namespace Coglatas.Application.TenantExports;

public sealed record TenantExportRequest(Guid? TenantId = null);

public sealed record TenantExportFileResponse(
    Guid ExportJobId,
    Guid TenantId,
    ExportJobStatus Status,
    string FileName,
    string ContentType,
    byte[] Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public sealed record TenantExportJobResponse(
    Guid ExportJobId,
    Guid TenantId,
    ExportJobStatus Status,
    TenantExportType ExportType,
    Guid? FileObjectId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorMessage);
