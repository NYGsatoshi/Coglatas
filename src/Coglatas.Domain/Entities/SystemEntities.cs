using Coglatas.Domain.Common;
using Coglatas.Domain.Enums;

namespace Coglatas.Domain.Entities;

public sealed class OutboxEvent : AuditableEntity, ITenantEntity
{
    public OutboxEvent(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Outbox event identity is required.", nameof(id));
        }

        Id = id;
    }

    public Guid TenantId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int PayloadSchemaVersion { get; set; }
    public string AggregateType { get; set; } = string.Empty;
    public Guid AggregateId { get; set; }
    public long? AggregateVersion { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public string RoutingJson { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
    public string? CausationId { get; set; }
    public OutboxEventStatus Status { get; set; } = OutboxEventStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? LockedAt { get; set; }
    public string? LockOwner { get; set; }
    public Guid? LockToken { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorSummary { get; set; }
    public DateTimeOffset? DeadLetteredAt { get; set; }
}

public sealed class Attachment : SoftDeletableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid FileObjectId { get; set; }
    public Guid WorkspaceId { get; set; }
    public AttachmentOwnerType? OwnerType { get; set; }
    public Guid? OwnerId { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string StorageProvider { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public FileScanStatus ScanStatus { get; set; } = FileScanStatus.Pending;

    public FileObject? FileObject { get; set; }
    public Workspace? Workspace { get; set; }
    public User? OwnerUser { get; set; }
    public User? UploadedByUser { get; set; }
    public ICollection<FileScanResult> ScanResults { get; } = new List<FileScanResult>();
}

public sealed class FileObject : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string? HashSha256 { get; set; }
    public DataClassification? Classification { get; set; }
    /// <summary>
    /// Controls the baseline audience for direct Workspace attachments. This
    /// is intentionally separate from data classification.
    /// </summary>
    public FileSharingPolicy SharingPolicy { get; set; } = FileSharingPolicy.Private;
    /// <summary>
    /// Incremented for each sharing mutation and used as an optimistic
    /// concurrency token by the File-sharing commands.
    /// </summary>
    public long SharingVersion { get; set; } = 1;
    public FileObjectStatus Status { get; set; } = FileObjectStatus.Active;
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedByUserId { get; set; }
    public string? DeleteReason { get; set; }

    public Workspace? Workspace { get; set; }
    public Group? Group { get; set; }
    public Project? Project { get; set; }
    public User? UploadedByUser { get; set; }

    public void MarkDeleted(DateTimeOffset deletedAt, Guid deletedByUserId, string? reason)
    {
        Status = FileObjectStatus.Deleted;
        DeletedAt = deletedAt;
        DeletedByUserId = deletedByUserId;
        DeleteReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }
}

/// <summary>
/// An explicit, audited FileObject-level access grant. The recipient must
/// remain an active same-Tenant Workspace member or a Project-scoped external
/// user in the recorded Workspace; revoked and stale grants fail closed.
/// </summary>
public sealed class FileAccessGrant : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid FileObjectId { get; set; }
    public Guid RecipientUserId { get; set; }
    public FileAccessGrantRecipientKind RecipientKind { get; set; }
    public Guid GrantedByUserId { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }

    public Workspace? Workspace { get; set; }
    public FileObject? FileObject { get; set; }
    public User? RecipientUser { get; set; }
    public User? GrantedByUser { get; set; }
    public User? RevokedByUser { get; set; }
}

public sealed class FileDownloadGrant : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid FileObjectId { get; set; }
    public Guid AttachmentId { get; set; }
    public AttachmentOwnerType TargetScopeType { get; set; }
    public Guid TargetScopeId { get; set; }
    public DataClassification Classification { get; set; }
    public string AllowedOperation { get; set; } = "download";
    public string TokenHash { get; set; } = string.Empty;
    public string PolicyStamp { get; set; } = string.Empty;
    public string? Purpose { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? DownloadedAt { get; set; }
}

/// <summary>
/// An immutable, short-lived server capture of the exact Workspace File
/// identities selected by one actor from one normalized search.
/// </summary>
public sealed class FileSelectionSnapshot : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid WorkspaceId { get; set; }
    public string NormalizedQuery { get; set; } = string.Empty;
    public string FileKind { get; set; } = string.Empty;
    public DateTimeOffset? FromDateUtc { get; set; }
    public bool OnlyMyUploads { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public int ConsumptionVersion { get; set; }
    public ICollection<FileSelectionSnapshotItem> Items { get; } = new List<FileSelectionSnapshotItem>();
}

public sealed class FileSelectionSnapshotItem
{
    public Guid SelectionSnapshotId { get; set; }
    public Guid FileObjectId { get; set; }
    public FileSelectionSnapshot? SelectionSnapshot { get; set; }
}

public sealed class FileScanResult : Entity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid AttachmentId { get; set; }
    public FileScanStatus Status { get; set; }
    public string ScannerName { get; set; } = string.Empty;
    public string? ResultSummary { get; set; }
    public DateTimeOffset ScannedAt { get; set; }

    public Attachment? Attachment { get; set; }
}

public sealed class AuditLog : Entity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? ProjectId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Summary { get; set; }
    public string? MetadataJson { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User? ActorUser { get; set; }
    public Workspace? Workspace { get; set; }
    public Group? Group { get; set; }
    public Project? Project { get; set; }
}

public sealed class SecurityEvent : Entity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public SecurityEventType EventType { get; set; } = SecurityEventType.AccessDenied;
    public Guid? UserId { get; set; }
    public string? Email { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public SecurityEventSeverity Severity { get; set; } = SecurityEventSeverity.Info;
    public string Summary { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User? User { get; set; }
}

public sealed class SystemSetting : Entity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string ValueType { get; set; } = "String";
    public string? Description { get; set; }
    public bool IsSensitive { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    public User? UpdatedByUser { get; set; }
}

public sealed class FeatureModule : AuditableEntity
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
    public SystemRole? RequiredRole { get; set; }
    public string DefaultRoute { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public int SortOrder { get; set; }

    public ICollection<PanelDefinition> PanelDefinitions { get; } = new List<PanelDefinition>();
    public ICollection<CommandDefinition> CommandDefinitions { get; } = new List<CommandDefinition>();
}

public sealed class PanelDefinition : AuditableEntity
{
    public Guid FeatureModuleId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public DockArea DefaultDockArea { get; set; } = DockArea.Center;
    public string DefaultPosition { get; set; } = "Center";
    public int MinWidth { get; set; }
    public int MinHeight { get; set; }
    public int DefaultWidth { get; set; } = 480;
    public int DefaultHeight { get; set; } = 320;
    public string? RequiredPermission { get; set; }
    public bool IsDockable { get; set; } = true;
    public bool IsClosable { get; set; } = true;
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; } = true;

    public FeatureModule? FeatureModule { get; set; }
}

public sealed class UserLayout : Entity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public LayoutScopeType ScopeType { get; set; } = LayoutScopeType.Global;
    public Guid? ScopeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string LayoutJson { get; set; } = "{}";
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User? User { get; set; }
    public Workspace? Workspace { get; set; }
}

public sealed class CommandDefinition : AuditableEntity
{
    public Guid? FeatureModuleId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public CommandActionType ActionType { get; set; } = CommandActionType.Navigate;
    public string? Route { get; set; }
    public string? HandlerKey { get; set; }
    public string? RequiredPermission { get; set; }
    public CommandContextType ContextType { get; set; } = CommandContextType.Global;
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; } = true;

    public FeatureModule? FeatureModule { get; set; }
}

public sealed class RadialMenuProfile : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public string ProfileKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public CommandContextType ContextType { get; set; } = CommandContextType.Global;
    public RadialMenuScope Scope { get; set; } = RadialMenuScope.Global;
    public bool IsDefault { get; set; }

    public User? User { get; set; }
    public Workspace? Workspace { get; set; }
    public ICollection<RadialMenuItem> Items { get; } = new List<RadialMenuItem>();
}

public sealed class RadialMenuItem : Entity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid RadialMenuProfileId { get; set; }
    public Guid? CommandDefinitionId { get; set; }
    public Guid? ParentItemId { get; set; }
    public RadialMenuDirection Direction { get; set; } = RadialMenuDirection.Center;
    public string CommandKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public decimal? AngleDegrees { get; set; }
    public int SortOrder { get; set; }
    public string? PayloadJson { get; set; }

    public RadialMenuProfile? RadialMenuProfile { get; set; }
    public CommandDefinition? CommandDefinition { get; set; }
    public RadialMenuItem? ParentItem { get; set; }
    public ICollection<RadialMenuItem> ChildItems { get; } = new List<RadialMenuItem>();
}
