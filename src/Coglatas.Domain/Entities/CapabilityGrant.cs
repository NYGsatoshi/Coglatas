using Coglatas.Domain.Common;
using Coglatas.Domain.Enums;

namespace Coglatas.Domain.Entities;

/// <summary>
/// Tenant-owned delegated authority slot. Capability meaning is interpreted by
/// the backend evaluator; an unknown key grants nothing by itself.
/// </summary>
public sealed class CapabilityGrant : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid SubjectUserId { get; set; }
    public string CapabilityKey { get; set; } = string.Empty;
    public CapabilityScopeType ScopeType { get; set; }
    public Guid? ScopeId { get; set; }
    public Guid GrantedByUserId { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public long VersionNo { get; set; } = 1;

    public User? SubjectUser { get; set; }
    public User? GrantedByUser { get; set; }
}
