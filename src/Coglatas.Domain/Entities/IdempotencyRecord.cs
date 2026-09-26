using Coglatas.Domain.Common;

namespace Coglatas.Domain.Entities;

/// <summary>
/// Durable identity for a committed retry-safe create operation. The raw client
/// key and request payload are deliberately not persisted.
/// </summary>
public sealed class IdempotencyRecord : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid ActorUserId { get; set; }

    public string Operation { get; set; } = string.Empty;

    public string KeyHash { get; set; } = string.Empty;

    public string RequestHash { get; set; } = string.Empty;

    public string ResourceType { get; set; } = string.Empty;

    public Guid ResourceId { get; set; }

    public User? ActorUser { get; set; }
}
