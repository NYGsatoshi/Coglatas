using Coglatas.Domain.Common;
using Coglatas.Domain.Enums;

namespace Coglatas.Domain.Entities;

/// <summary>
/// The Project-owned default source policy. The legacy booleans are the V1
/// compatibility projection; item identities are stored by the V2 policy
/// document boundary rather than on this aggregate row.
/// </summary>
public sealed class ProjectExecutionScope : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ProjectId { get; set; }
    public bool WebEnabled { get; set; }
    public bool ProjectFilesEnabled { get; set; }
    public long VersionNo { get; set; } = 1;
    public Guid UpdatedByUserId { get; set; }

    public Project? Project { get; set; }
    public User? UpdatedByUser { get; set; }
}

/// <summary>
/// A complete Task-local replacement for the Project default source policy.
/// Its absence means the Task inherits the current Project default.
/// </summary>
public sealed class TaskExecutionScopeOverride : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid TaskItemId { get; set; }
    public bool WebEnabled { get; set; }
    public bool ProjectFilesEnabled { get; set; }
    public long VersionNo { get; set; } = 1;
    public Guid UpdatedByUserId { get; set; }

    public Project? Project { get; set; }
    public TaskItem? TaskItem { get; set; }
    public User? UpdatedByUser { get; set; }
}

/// <summary>
/// An immutable, server-owned Task execution request. V3 stores its itemized
/// policy in the immutable source-policy document table while these columns
/// retain the V1/V2 compatibility projection.
/// </summary>
public sealed class TaskExecutionRun : Entity, ITenantEntity
{
    public const int SnapshotSchemaVersion1 = 1;
    public const int SnapshotSchemaVersion2 = 2;
    public const int SnapshotSchemaVersion3 = 3;
    /// <summary>
    /// Version 2 adds the optional Research Plan revision. Version 3 adds an
    /// immutable Source Policy V2 document keyed by the Run id.
    /// </summary>
    public const int CurrentSnapshotSchemaVersion = SnapshotSchemaVersion3;
    public const int RuntimeContractVersion1 = 1;

    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid TaskItemId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public DateTimeOffset? QueuedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public TaskExecutionProvider RuntimeProvider { get; set; } = TaskExecutionProvider.FirstPartyProjectFilesRuntimeV1;
    public int RuntimeContractVersion { get; set; } = RuntimeContractVersion1;
    public TaskExecutionRunStatus Status { get; set; } = TaskExecutionRunStatus.Accepted;
    public string? FailureCode { get; set; }
    public long VersionNo { get; set; } = 1;

    public int SnapshotSchemaVersion { get; set; } = CurrentSnapshotSchemaVersion;
    public TaskExecutionScopeOrigin SnapshotScopeOrigin { get; set; }
    public long SnapshotProjectScopeVersion { get; set; }
    public long? SnapshotTaskOverrideVersion { get; set; }
    public bool SnapshotWebEnabled { get; set; }
    public bool SnapshotProjectFilesEnabled { get; set; }
    public Guid? SnapshotResearchPlanRevisionId { get; set; }
    public long? SnapshotResearchPlanRevisionNo { get; set; }

    public Project? Project { get; set; }
    public TaskItem? TaskItem { get; set; }
    public User? RequestedByUser { get; set; }
    public ResearchPlanRevision? SnapshotResearchPlanRevision { get; set; }
}
