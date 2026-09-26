using System.Text.Json.Serialization;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Projects;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateProjectExecutionScopeRequest(
    bool WebEnabled,
    bool ProjectFilesEnabled,
    long ExpectedVersion,
    TaskExecutionSourcePolicyV2? PolicyV2 = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateTaskExecutionScopeOverrideRequest(
    bool WebEnabled,
    bool ProjectFilesEnabled,
    long ExpectedVersion,
    TaskExecutionSourcePolicyV2? PolicyV2 = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ClearTaskExecutionScopeOverrideRequest(long ExpectedVersion);

/// <summary>
/// An Idempotency-Key identifies a requested immutable policy snapshot; source
/// material is never accepted in a run request.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequestTaskExecutionRunRequest;

public sealed record TaskExecutionSourcePolicyResponse(
    bool WebEnabled,
    bool ProjectFilesEnabled,
    TaskExecutionSourcePolicyV2? PolicyV2 = null);

public sealed record ProjectExecutionScopeResponse(
    TaskExecutionSourcePolicyResponse Policy,
    long Version,
    bool CanManage);

public sealed record TaskExecutionScopeResponse(
    TaskExecutionSourcePolicyResponse EffectivePolicy,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] TaskExecutionScopeOrigin Origin,
    long ProjectDefaultVersion,
    long? TaskOverrideVersion,
    TaskExecutionSourcePolicyResponse? TaskOverridePolicy,
    bool CanManage,
    TaskExecutionRunResponse? LatestRun,
    string ChangesApplyTo,
    IReadOnlyList<TaskExecutionSourceInventoryItemResponse>? SourceInventory = null);

public sealed record TaskExecutionRunResponse(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] TaskExecutionRunStatus Status,
    string? FailureCode,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    int SnapshotSchemaVersion,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] TaskExecutionScopeOrigin SnapshotScopeOrigin,
    long SnapshotProjectScopeVersion,
    long? SnapshotTaskOverrideVersion,
    bool SnapshotWebEnabled,
    bool SnapshotProjectFilesEnabled,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] TaskExecutionProvider RuntimeProvider = TaskExecutionProvider.FirstPartyProjectFilesRuntimeV1,
    int RuntimeContractVersion = TaskExecutionRun.RuntimeContractVersion1,
    DateTimeOffset? QueuedAtUtc = null,
    DateTimeOffset? StartedAtUtc = null,
    Guid? SnapshotResearchPlanRevisionId = null,
    long? SnapshotResearchPlanRevisionNo = null,
    TaskExecutionSourcePolicyV2? SnapshotPolicyV2 = null)
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TaskExecutionMajorState MajorState => Status switch
    {
        TaskExecutionRunStatus.Accepted => TaskExecutionMajorState.Accepted,
        TaskExecutionRunStatus.Queued => TaskExecutionMajorState.Queued,
        TaskExecutionRunStatus.Running => TaskExecutionMajorState.Running,
        TaskExecutionRunStatus.Succeeded => TaskExecutionMajorState.Succeeded,
        TaskExecutionRunStatus.Failed => TaskExecutionMajorState.Failed,
        TaskExecutionRunStatus.Stopped => TaskExecutionMajorState.Stopped,
        TaskExecutionRunStatus.Redirected => TaskExecutionMajorState.Redirected,
        _ => TaskExecutionMajorState.Failed
    };
}
