using System.Text.Json.Serialization;
using Coglatas.Application.Common;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Projects;

/// <summary>
/// Canonical Project-scoped Task creation boundary.  The compatibility
/// <c>POST /api/projects/{projectId}/tasks</c> command remains separate so its
/// historical, non-idempotent contract is not changed by the Task-create UI.
/// </summary>
public interface ICanonicalTaskCreateService
{
    Task<Result<TaskCreateOptionsResponse>> GetCreateOptionsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<Result<CanonicalTaskCreateResponse>> CreateAsync(
        Guid projectId,
        CanonicalCreateTaskRequest request,
        string? clientRequestIdentity,
        CancellationToken cancellationToken = default);
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TaskCreateSourceScopeMode
{
    Inherit = 0,
    TaskOverride = 1
}

/// <summary>
/// A complete source-policy replacement selected while the Task is created.
/// This contains policy booleans only: no source identifiers, URLs, file
/// metadata, content, credentials, providers, or execution instructions.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TaskCreateSourceScopePolicyRequest(
    [property: JsonRequired] bool WebEnabled,
    [property: JsonRequired] bool ProjectFilesEnabled);

/// <summary>
/// Strict body for the canonical Task-create command.  A Task either inherits
/// the current Project default or names a complete Task-local replacement.
/// The request never starts an execution run or captures a source snapshot.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CanonicalCreateTaskRequest(
    [property: JsonRequired] string Title,
    string? Description = null,
    TaskPriority Priority = TaskPriority.Medium,
    Guid? MilestoneId = null,
    DateOnly? StartDate = null,
    DateOnly? DueDate = null,
    string? Goal = null,
    string? Deliverable = null,
    string? Constraints = null,
    Guid? PrimaryAssigneeUserId = null,
    [property: JsonRequired] TaskCreateSourceScopeMode SourceScopeMode = TaskCreateSourceScopeMode.Inherit,
    TaskCreateSourceScopePolicyRequest? TaskOverridePolicy = null);

public sealed record CanonicalTaskCreateResponse(
    Guid TaskId,
    Guid ProjectId,
    Guid WorkspaceId,
    Guid? MilestoneId,
    Guid? PrimaryAssigneeUserId,
    string Title,
    TaskPriority Priority,
    TaskItemStatus Status,
    Guid WorkflowStageId,
    long Version,
    TaskCreateSourceScopeMode SourceScopeMode,
    TaskExecutionSourcePolicyResponse? TaskOverridePolicy);

public sealed record TaskCreateOptionsResponse(
    Guid ProjectId,
    Guid WorkspaceId,
    string ProjectTitle,
    bool CanCreateTask,
    bool CanManageProject,
    IReadOnlyList<TaskCreateMilestoneOptionResponse> Milestones,
    IReadOnlyList<TaskCreateAssigneeOptionResponse> Assignees,
    TaskCreateProjectScopeResponse ProjectScope);

public sealed record TaskCreateMilestoneOptionResponse(Guid Id, string Title);

public sealed record TaskCreateAssigneeOptionResponse(Guid UserId, string DisplayName);

/// <summary>
/// The current Project default exposed to the create review.  It is not a
/// Task-run snapshot; an inheriting Task follows the then-current default when
/// a separately approved future run is accepted.
/// </summary>
public sealed record TaskCreateProjectScopeResponse(
    TaskExecutionSourcePolicyResponse Policy,
    long Version,
    bool CanSetTaskOverride);
