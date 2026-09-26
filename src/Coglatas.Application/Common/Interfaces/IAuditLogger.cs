using Coglatas.Domain.Enums;

namespace Coglatas.Application.Common.Interfaces;

public interface IAuditLogger
{
    Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);

    Task LogAsync(
        string action,
        string entityType,
        Guid? entityId,
        string? summary = null,
        IReadOnlyDictionary<string, object?>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        return LogAsync(new AuditLogEntry(null, action, entityType, entityId, summary, Metadata: metadata), cancellationToken);
    }

    Task LogSecurityAsync(
        string action,
        string summary,
        IReadOnlyDictionary<string, object?>? metadata = null,
        SecurityEventSeverity severity = SecurityEventSeverity.Info,
        CancellationToken cancellationToken = default)
    {
        return LogAsync(new AuditLogEntry(null, action, "SecurityEvent", null, summary, Metadata: metadata), cancellationToken);
    }

    Task LogUserActionAsync(
        Guid actorUserId,
        string action,
        string entityType,
        Guid? entityId,
        string? summary = null,
        IReadOnlyDictionary<string, object?>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        return LogAsync(new AuditLogEntry(actorUserId, action, entityType, entityId, summary, Metadata: metadata), cancellationToken);
    }
}

public sealed record AuditLogEntry(
    Guid? ActorUserId,
    string Action,
    string EntityType,
    Guid? EntityId,
    string? Summary = null,
    Guid? WorkspaceId = null,
    Guid? GroupId = null,
    Guid? ProjectId = null,
    string? IpAddress = null,
    string? UserAgent = null,
    IReadOnlyDictionary<string, object?>? Metadata = null,
    Guid? TenantId = null,
    string? CorrelationId = null);
