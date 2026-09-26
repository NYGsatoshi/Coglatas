using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Common.Interfaces;

public sealed record ProjectKanbanDefinitionReadModel(
    Guid Id,
    Guid TenantId,
    Guid WorkspaceId,
    Guid ProjectId,
    bool ReviewEnforcementEnabled,
    ProjectKanbanSwimlane DefaultSwimlane,
    long Version);

public sealed record ProjectKanbanStageReadModel(
    Guid Id,
    string Name,
    TaskStageCategory Category,
    long SortKey,
    int? WipWarningLimit);

public sealed record ProjectKanbanTaskReadModel(
    Guid Id,
    Guid ProjectId,
    Guid WorkflowStageId,
    string Title,
    long SortKey,
    Guid? ParentTaskId,
    string? ParentTitle,
    int ChildCount,
    int CompletedChildCount,
    int IncompleteChildCount,
    int ProgressPercent,
    DateOnly? PlannedStartDate,
    DateOnly? PlannedEndDate,
    Guid CreatedByUserId,
    Guid? PrimaryAssigneeUserId,
    string? PrimaryAssigneeName,
    Guid? TargetGroupId,
    string? TargetGroupName,
    Guid? ReviewerUserId,
    TaskReviewStatus ReviewStatus,
    TaskPriority Priority,
    bool IsBlocked,
    long Version);

public sealed record ProjectKanbanReadModel(
    ProjectKanbanDefinitionReadModel Definition,
    IReadOnlyList<ProjectKanbanStageReadModel> Stages,
    IReadOnlyList<ProjectKanbanTaskReadModel> Tasks,
    IReadOnlyDictionary<Guid, int> CountsByStage,
    int TotalCount);

public interface IProjectKanbanRepository
{
    Task<ProjectKanbanReadModel?> ReadAsync(
        Guid projectId,
        DateTimeOffset completedCutoffUtc,
        bool includeOlderCompleted,
        Guid? primaryAssigneeUserId,
        Guid? targetGroupId,
        TaskPriority? priority,
        Guid? parentTaskId,
        int take,
        CancellationToken cancellationToken = default);

    Task<TaskWorkflowDefinition?> GetDefinitionForUpdateAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TaskItem>> ListStageTasksForUpdateAsync(Guid projectId, Guid workflowStageId, int take, CancellationToken cancellationToken = default);
}
