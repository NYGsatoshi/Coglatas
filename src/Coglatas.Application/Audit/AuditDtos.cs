using System.Text.Json.Nodes;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Audit;

public sealed record AuditLogQuery(
    string? Action = null,
    string? EntityType = null,
    Guid? ActorUserId = null,
    Guid? WorkspaceId = null,
    Guid? GroupId = null,
    Guid? ProjectId = null,
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null,
    int Page = 1,
    int PageSize = 20,
    string? Q = null,
    string? Actor = null,
    string? Severity = null,
    string? Result = null);

public sealed record AuditLogListItemResponse(
    Guid Id,
    Guid? ActorUserId,
    string? ActorDisplayName,
    string Action,
    string EntityType,
    Guid? EntityId,
    Guid? WorkspaceId,
    Guid? GroupId,
    Guid? ProjectId,
    string? Summary,
    string? MetadataJson,
    string? CorrelationId,
    DateTimeOffset CreatedAt);

public sealed record AuditGridRowResponse(
    Guid Id,
    DateTimeOffset CreatedAt,
    string Action,
    string ActorDisplayName,
    string TargetType,
    string? WorkspaceLabel,
    string Severity,
    string Result,
    string Summary,
    string? RequestId);

/// <summary>
/// Exact-event disclosure contract for the stored, redacted Audit metadata.
/// Metadata is a JSON object rather than the persisted JSON string so clients
/// cannot accidentally render or reparse an unreviewed transport payload.
/// </summary>
public sealed record AuditSensitiveMetadataResponse(
    Guid AuditId,
    JsonObject Metadata,
    bool RedactionApplied);

public sealed record SecurityEventQuery(
    SecurityEventType? EventType = null,
    SecurityEventSeverity? Severity = null,
    Guid? UserId = null,
    string? Email = null,
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null,
    int Page = 1,
    int PageSize = 20);

public sealed record SecurityEventListItemResponse(
    Guid Id,
    SecurityEventType EventType,
    Guid? UserId,
    string? Email,
    string? IpAddress,
    string? UserAgent,
    SecurityEventSeverity Severity,
    string Summary,
    string? MetadataJson,
    DateTimeOffset CreatedAt);
