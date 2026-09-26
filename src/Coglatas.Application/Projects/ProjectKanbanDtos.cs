using Coglatas.Domain.Enums;

namespace Coglatas.Application.Projects;

public sealed record ProjectKanbanQuery(
    bool IncludeOlderCompleted = false,
    ProjectKanbanSwimlane? Swimlane = null,
    Guid? PrimaryAssigneeUserId = null,
    Guid? TargetGroupId = null,
    TaskPriority? Priority = null,
    Guid? ParentTaskId = null,
    int MaxCards = 300)
{
    public int SafeMaxCards => Math.Clamp(MaxCards, 1, 500);
}

public sealed record ProjectKanbanWarning(
    string Code,
    string Message,
    Guid? WorkflowStageId = null,
    int? CurrentCount = null,
    int? Limit = null);

public sealed record ProjectKanbanBoardPermissions(bool CanConfigure);

public sealed record ProjectKanbanBoard(
    Guid ProjectId,
    long Version,
    string TimeZone,
    ProjectKanbanSwimlane DefaultSwimlane,
    ProjectKanbanSwimlane SelectedSwimlane,
    IReadOnlyList<ProjectKanbanSwimlane> SupportedSwimlanes,
    IReadOnlyList<string> SupportedFilters,
    bool IncludesOlderCompleted,
    int DoneWindowDays,
    int TotalAuthorizedCardCount,
    bool IsTruncated,
    ProjectKanbanBoardPermissions UiPermissions,
    IReadOnlyList<ProjectKanbanWarning> Warnings);

public sealed record ProjectKanbanColumnPermissions(bool CanConfigure);

public sealed record ProjectKanbanColumn(
    Guid WorkflowStageId,
    string DisplayName,
    TaskStageCategory Category,
    long DisplayOrder,
    int? WipWarningLimit,
    int CurrentAuthorizedCardCount,
    bool HasWipWarning,
    ProjectKanbanColumnPermissions UiPermissions);

public sealed record ProjectKanbanCardPermissions(
    bool CanOpen,
    bool CanMove,
    IReadOnlyList<Guid> AllowedTargetWorkflowStageIds);

public sealed record ProjectKanbanCard(
    Guid TaskId,
    string Summary,
    Guid WorkflowStageId,
    long BoardOrder,
    Guid? ParentTaskId,
    string? ParentSummary,
    bool IsParentSummary,
    bool IsLeaf,
    int CompletedChildCount,
    int ChildCount,
    int ProgressPercent,
    DateOnly? PlannedStartDate,
    DateOnly? PlannedEndDate,
    Guid? PrimaryAssigneeUserId,
    string PrimaryAssigneeLabel,
    Guid? TargetGroupId,
    string TargetGroupLabel,
    TaskPriority Priority,
    bool IsBlocked,
    long Version,
    string SwimlaneKey,
    string SwimlaneLabel,
    ProjectKanbanCardPermissions UiPermissions);

public sealed record ProjectKanbanSnapshot(
    ProjectKanbanBoard Board,
    IReadOnlyList<ProjectKanbanColumn> Columns,
    IReadOnlyList<ProjectKanbanCard> Cards);

public sealed record ProjectKanbanStageConfig(
    Guid WorkflowStageId,
    int DisplayOrder,
    int? WipWarningLimit);

public sealed record UpdateProjectKanbanConfigRequest(
    long ExpectedBoardVersion,
    ProjectKanbanSwimlane DefaultSwimlane,
    IReadOnlyList<ProjectKanbanStageConfig> Columns);

public sealed record MoveTaskOnKanbanRequest(
    Guid TargetWorkflowStageId,
    Guid? TargetBeforeTaskId,
    Guid? TargetAfterTaskId,
    long ExpectedTaskVersion,
    long ExpectedBoardVersion,
    string? Reason = null);

public sealed record ProjectKanbanCommandResponse(
    ProjectKanbanSnapshot Snapshot,
    Guid? FocusTaskId,
    IReadOnlyList<ProjectKanbanWarning> Warnings);
