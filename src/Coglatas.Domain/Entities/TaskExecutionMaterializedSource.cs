using Coglatas.Domain.Common;

namespace Coglatas.Domain.Entities;

/// <summary>
/// Immutable, metadata-only provenance for one server-materialized Project
/// File used by a Task execution run. Raw bytes, text, names, paths, storage
/// keys, credentials, and provider configuration are deliberately excluded.
/// </summary>
public sealed class TaskExecutionMaterializedSource : Entity, ITenantEntity
{
    public const int SchemaVersion1 = 1;

    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid TaskItemId { get; set; }
    public Guid TaskExecutionRunId { get; set; }
    public Guid FileObjectId { get; set; }
    public Guid AttachmentId { get; set; }
    public int SchemaVersion { get; set; } = SchemaVersion1;
    public string ContentSha256 { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public long MaterializedByteCount { get; set; }
    public DateTimeOffset MaterializedAtUtc { get; set; }
}
