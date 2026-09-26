using Coglatas.Application.Planning;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Projects;

public sealed record ProjectListQuery(
    bool Archived = false,
    string? Search = null,
    ProjectStatus? Status = null,
    int Page = 1,
    int PageSize = 50,
    Guid? WorkspaceId = null)
{
    public int SafePage => Page < 1 ? 1 : Page;
    public int SafePageSize => PageSize < 1 ? 50 : Math.Min(PageSize, 100);
}

public sealed record ProjectResponse(
    Guid Id,
    Guid WorkspaceId,
    Guid? GroupId,
    Guid OwnerUserId,
    string Title,
    string? Description,
    ProjectStatus Status,
    DateOnly? StartDate,
    DateOnly? EndDate,
    long VersionNo,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    ProjectUiPermissionResponse UiPermissions,
    ProjectVisibility? Visibility = null,
    ProjectActivationState ActivationState = ProjectActivationState.LegacyUnknown,
    DateTimeOffset? ActivatedAtUtc = null,
    int? ActivationVersion = null,
    ProjectStatus? SuspendedFromStatus = null,
    ProjectStatus? ArchivedFromStatus = null);
public sealed record ProjectUiPermissionResponse(bool CanCreateTask, bool CanActivate = false);
public sealed record CreateProjectRequest(Guid WorkspaceId, Guid GroupId, string Title, string? Description, DateOnly? StartDate, DateOnly? EndDate);
public sealed record UpdateProjectRequest(string? Title, string? Description, ProjectStatus? Status, DateOnly? StartDate, DateOnly? EndDate);
public sealed record ProjectMemberResponse(Guid UserId, string DisplayName, string Email, ProjectRole Role, DateTimeOffset JoinedAt);
public sealed record AddProjectMemberRequest(Guid UserId, ProjectRole Role);
public sealed record UpdateProjectMemberRequest(ProjectRole Role);
public sealed record ProjectChildListQuery(string? Search = null, int Page = 1, int PageSize = 50)
{
    public int SafePage => Page < 1 ? 1 : Page;
    public int SafePageSize => PageSize < 1 ? 50 : Math.Min(PageSize, 100);
}

public sealed record TaskListQuery(
    string? Search = null,
    TaskItemStatus? Status = null,
    TaskPriority? Priority = null,
    Guid? MilestoneId = null,
    Guid? AssignedUserId = null,
    int Page = 1,
    int PageSize = 50)
{
    public int SafePage => Page < 1 ? 1 : Page;
    public int SafePageSize => PageSize < 1 ? 50 : Math.Min(PageSize, 100);
}

public sealed record MilestoneResponse(Guid Id, Guid ProjectId, string Title, string? Description, DateOnly? DueDate, MilestoneStatus Status, int DisplayOrder, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, long Version);
public sealed record CreateMilestoneRequest(string Title, string? Description, DateOnly? DueDate, int DisplayOrder);
public sealed record UpdateMilestoneRequest(
    string? Title,
    string? Description,
    DateOnly? DueDate,
    MilestoneStatus? Status,
    int? DisplayOrder,
    [property: System.Text.Json.Serialization.JsonRequired] long ExpectedVersion);
// Compatibility shape retained for the existing project screen.  The appended
// values mirror the canonical Task detail contract so a list row cannot expose
// stale parent-derived fields or a different aggregate version.
public sealed record TaskItemResponse(
    Guid Id,
    Guid ProjectId,
    Guid? MilestoneId,
    string Title,
    string? Description,
    TaskItemStatus Status,
    TaskPriority Priority,
    DateOnly? StartDate,
    DateOnly? DueDate,
    int ProgressPercent,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    TaskUiPermissionResponse UiPermissions,
    DateOnly? PlannedStartDate = null,
    DateOnly? PlannedEndDate = null,
    bool ProgressIsDerived = false,
    bool IsOverdue = false,
    long Version = 0,
    Guid? WorkflowStageId = null,
    string WorkflowStageName = "",
    [property: System.Text.Json.Serialization.JsonConverter(
        typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    TaskStageCategory StageCategory = TaskStageCategory.Todo,
    bool IsBlocked = false,
    bool HasArtifact = false);
public sealed record TaskUiPermissionResponse(bool CanEdit, bool CanAssign, bool CanChangeStatus, bool CanDelete, IReadOnlyList<TaskItemStatus> AllowedTransitions, string? RowVersion);
public sealed record CreateTaskItemRequest(
    Guid? MilestoneId,
    string Title,
    string? Description,
    TaskPriority Priority,
    DateOnly? StartDate,
    DateOnly? DueDate,
    string? Goal = null,
    string? Deliverable = null,
    string? Constraints = null);
public sealed record UpdateTaskItemRequest(Guid? MilestoneId, string? Title, string? Description, TaskItemStatus? Status, TaskPriority? Priority, DateOnly? StartDate, DateOnly? DueDate, int? ProgressPercent);
public sealed record TaskAssignmentResponse(Guid Id, Guid TaskItemId, Guid UserId, string DisplayName, TaskAssignmentRole Role, decimal? EstimatedHours, decimal? ActualHours, DateTimeOffset AssignedAt, Guid AssignedByUserId);
public sealed record AddTaskAssignmentRequest(Guid UserId, TaskAssignmentRole Role, decimal? EstimatedHours);
public sealed record UpdateTaskAssignmentRequest(TaskAssignmentRole Role, decimal? EstimatedHours, decimal? ActualHours);
public sealed record TaskDependencyResponse(
    Guid Id,
    Guid PredecessorTaskId,
    Guid SuccessorTaskId,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))] TaskDependencyType DependencyType,
    DateTimeOffset CreatedAt,
    long Version,
    bool Editable,
    IReadOnlyList<GanttWarningResponse> Warnings);
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(
    System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record AddTaskDependencyRequest(
    [property: System.Text.Json.Serialization.JsonRequired] Guid PredecessorTaskId,
    [property: System.Text.Json.Serialization.JsonRequired]
    [property: System.Text.Json.Serialization.JsonConverter(
        typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    TaskDependencyType DependencyType,
    long ExpectedVersion = 0);
public sealed record CommentResponse(Guid Id, CommentTargetType TargetType, Guid TargetId, Guid AuthorUserId, string Body, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);
public sealed record CreateCommentRequest(CommentTargetType TargetType, Guid TargetId, string Body);
public sealed record UpdateCommentRequest(string Body, long? ExpectedVersion = null);
