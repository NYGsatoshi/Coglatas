using Coglatas.Application.Common;

namespace Coglatas.Application.Audit;

public interface IAuditQueryService
{
    Task<Result<PagedResponse<AuditLogListItemResponse>>> ListAuditLogsAsync(AuditLogQuery query, CancellationToken cancellationToken = default);

    Task<Result<PagedResponse<AuditGridRowResponse>>> ListAuditGridAsync(AuditLogQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the same bounded, redacted projection used by the audit grid.
    /// This is intentionally not a general AuditLog detail API: callers never
    /// receive metadata JSON, actor IDs, target IDs, or other raw fields.
    /// </summary>
    Task<Result<AuditGridRowResponse>> GetAuditGridRowAsync(Guid auditId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns only the selected event's stored, recursively sanitized metadata.
    /// The independent audit.sensitive_metadata.view capability and exact
    /// Tenant/platform scope are checked before the identifier is queried.
    /// </summary>
    Task<Result<AuditSensitiveMetadataResponse>> GetAuditSensitiveMetadataAsync(
        Guid auditId,
        CancellationToken cancellationToken = default);

    Task<Result<PagedResponse<SecurityEventListItemResponse>>> ListSecurityEventsAsync(SecurityEventQuery query, CancellationToken cancellationToken = default);
}
