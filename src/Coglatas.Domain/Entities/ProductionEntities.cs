using Coglatas.Domain.Common;
using Coglatas.Domain.Enums;

namespace Coglatas.Domain.Entities;

public sealed class Project : SoftDeletableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid? GroupId { get; set; }
    public Guid OwnerUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Planning;
    /// <summary>
    /// Canonical visibility. Null is the internal LegacyUnknown migration state
    /// and must never be inferred from legacy relationships or lifecycle state.
    /// </summary>
    public ProjectVisibility? Visibility { get; set; }
    public ProjectActivationState ActivationState { get; set; } = ProjectActivationState.NeverActivated;
    public DateTimeOffset? ActivatedAtUtc { get; set; }
    public int? ActivationVersion { get; set; }
    public ProjectStatus? SuspendedFromStatus { get; set; }
    public ProjectStatus? ArchivedFromStatus { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public long VersionNo { get; set; }
    public Guid CreatedByUserId { get; set; }

    public Workspace? Workspace { get; set; }
    public Group? Group { get; set; }
    public User? CreatedByUser { get; set; }
    public User? OwnerUser { get; set; }
    public ICollection<ProjectMember> Members { get; } = new List<ProjectMember>();
    public ICollection<Milestone> Milestones { get; } = new List<Milestone>();
    public ICollection<TaskItem> Tasks { get; } = new List<TaskItem>();
    public ICollection<TaskWorkflowDefinition> TaskWorkflowDefinitions { get; } = new List<TaskWorkflowDefinition>();
    public ProjectExecutionScope? ExecutionScope { get; set; }
}

public sealed class ProjectMember : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }
    public ProjectRole Role { get; set; } = ProjectRole.Contributor;
    public DateTimeOffset JoinedAt { get; set; }

    public Project? Project { get; set; }
    public User? User { get; set; }
}

public sealed class Milestone : SoftDeletableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateOnly? DueDate { get; set; }
    public MilestoneStatus Status { get; set; } = MilestoneStatus.NotStarted;
    public int SortOrder { get; set; }
    public long VersionNo { get; set; } = 1;

    public Project? Project { get; set; }
    public ICollection<TaskItem> Tasks { get; } = new List<TaskItem>();
}

public sealed class TaskItem : SoftDeletableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? MilestoneId { get; set; }
    public Guid? ParentTaskItemId { get; set; }
    public Guid? WorkflowStageId { get; set; }
    public WorkItemKind Kind { get; set; } = WorkItemKind.Task;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? BriefGoal { get; set; }
    public string? BriefDeliverable { get; set; }
    public string? BriefConstraints { get; set; }
    public TaskItemStatus Status { get; set; } = TaskItemStatus.NotStarted;
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    public DateOnly? StartDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public bool IsBlocked { get; set; }
    public string? BlockedReason { get; set; }
    public Guid? TargetGroupId { get; set; }
    public Guid? PrimaryAssigneeUserId { get; set; }
    public Guid? ReviewerUserId { get; set; }
    public DateOnly? PlannedStartDate { get; set; }
    public DateOnly? PlannedEndDate { get; set; }
    public DateTimeOffset? DeadlineAt { get; set; }
    public DateTimeOffset? ActualStartAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int? EstimatedEffortMinutes { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public TaskReviewStatus ReviewStatus { get; set; }
    public DateTimeOffset? ReviewSubmittedAt { get; set; }
    public DateTimeOffset? ReviewResolvedAt { get; set; }
    public Guid? ReviewResolvedByUserId { get; set; }
    public string? ReviewReturnReason { get; set; }
    public long SortKey { get; set; }
    public long VersionNo { get; set; } = 1;
    public int ProgressPercent { get; set; }
    public int SortOrder { get; set; }
    public Guid CreatedByUserId { get; set; }

    public Project? Project { get; set; }
    public Milestone? Milestone { get; set; }
    public TaskItem? ParentTaskItem { get; set; }
    public ICollection<TaskItem> ChildTaskItems { get; } = new List<TaskItem>();
    public TaskWorkflowStage? WorkflowStage { get; set; }
    public Group? TargetGroup { get; set; }
    public User? PrimaryAssigneeUser { get; set; }
    public User? ReviewerUser { get; set; }
    public User? CreatedByUser { get; set; }
    public ICollection<TaskAssignment> Assignments { get; } = new List<TaskAssignment>();
    public ICollection<TaskDependency> PredecessorDependencies { get; } = new List<TaskDependency>();
    public ICollection<TaskDependency> SuccessorDependencies { get; } = new List<TaskDependency>();
    public ICollection<WorkItemCollaborator> Collaborators { get; } = new List<WorkItemCollaborator>();
    public ICollection<TaskChecklistItem> ChecklistItems { get; } = new List<TaskChecklistItem>();
    public ICollection<TaskComment> TaskComments { get; } = new List<TaskComment>();
    public ICollection<WorkItemWatchState> WatchStates { get; } = new List<WorkItemWatchState>();
    public ICollection<WorkItemLabel> Labels { get; } = new List<WorkItemLabel>();
    public TaskExecutionScopeOverride? ExecutionScopeOverride { get; set; }
    public ICollection<TaskExecutionRun> ExecutionRuns { get; } = new List<TaskExecutionRun>();
    public ResearchPlan? ResearchPlan { get; set; }
}

public sealed class TaskChecklistItem : Entity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid TaskItemId { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? CompletedByUserId { get; set; }
    public long SortKey { get; set; }
    public long VersionNo { get; set; } = 1;
    public TaskItem? TaskItem { get; set; }
    public User? CompletedByUser { get; set; }
}

public sealed class TaskComment : SoftDeletableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid TaskItemId { get; set; }
    public Guid AuthorUserId { get; set; }
    public string BodyPlainText { get; set; } = string.Empty;
    public bool IsImportant { get; set; }
    public long VersionNo { get; set; } = 1;
    public TaskItem? TaskItem { get; set; }
    public User? AuthorUser { get; set; }
}

[Flags]
public enum WorkItemWatchAutomaticSource { None = 0, Creator = 1, PrimaryAssignee = 2, Collaborator = 4, Reviewer = 8 }

public sealed class WorkItemWatchState : Entity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid TaskItemId { get; set; }
    public Guid UserId { get; set; }
    public WorkItemWatchAutomaticSource AutomaticSources { get; set; }
    /// <summary>Durable user intent, distinct from relationship-derived watching.</summary>
    public bool IsManualWatch { get; set; }
    public bool IsExplicitOptOut { get; set; }
    public bool IsWatching { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long VersionNo { get; set; } = 1;
    public TaskItem? TaskItem { get; set; }
    public User? User { get; set; }
}

public sealed class ProjectTaskLabel : Entity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Database-computed trim/case-insensitive identity for <see cref="Name"/>.</summary>
    public string NormalizedName { get; private set; } = string.Empty;
    public string? Description { get; set; }
    public long SortKey { get; set; }
    public bool IsArchived { get; set; }
    public long VersionNo { get; set; } = 1;
    public Project? Project { get; set; }
}

public sealed class WorkItemLabel : Entity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid TaskItemId { get; set; }
    public Guid LabelId { get; set; }
    public DateTimeOffset AddedAt { get; set; }
    public Guid AddedByUserId { get; set; }
    public TaskItem? TaskItem { get; set; }
    public ProjectTaskLabel? Label { get; set; }
}

public sealed class TaskWorkflowDefinition : Entity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "Default";
    public bool ReviewEnforcementEnabled { get; set; } = true;
    public ProjectKanbanSwimlane KanbanDefaultSwimlane { get; set; }
    public long VersionNo { get; set; } = 1;

    public Project? Project { get; set; }
    public ICollection<TaskWorkflowStage> Stages { get; } = new List<TaskWorkflowStage>();
}

public sealed class TaskWorkflowStage : Entity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid DefinitionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public TaskStageCategory InternalCategory { get; set; }
    public long SortKey { get; set; }
    public int? WipWarningLimit { get; set; }
    public bool IsInitialStage { get; set; }
    public bool IsTerminalStage { get; set; }
    public long VersionNo { get; set; } = 1;

    public TaskWorkflowDefinition? Definition { get; set; }
}

public sealed class WorkItemCollaborator : Entity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid TaskItemId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset AddedAt { get; set; }
    public Guid AddedByUserId { get; set; }

    public TaskItem? TaskItem { get; set; }
    public User? User { get; set; }
    public User? AddedByUser { get; set; }
}

/// <summary>Immutable migration findings retained for operator review before command cutover.</summary>
public sealed class TaskMigrationInventory : Entity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? TaskItemId { get; set; }
    public string FindingCode { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TaskAssignment : Entity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid TaskItemId { get; set; }
    public Guid UserId { get; set; }
    public TaskAssignmentRole Role { get; set; } = TaskAssignmentRole.Assignee;
    public Guid AssignedByUserId { get; set; }
    public decimal? EstimatedHours { get; set; }
    public decimal? ActualHours { get; set; }
    public DateTimeOffset AssignedAt { get; set; }

    public TaskItem? TaskItem { get; set; }
    public User? User { get; set; }
    public User? AssignedByUser { get; set; }
}

public sealed class TaskDependency : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid PredecessorTaskItemId { get; set; }
    public Guid SuccessorTaskItemId { get; set; }
    public TaskDependencyType DependencyType { get; set; } = TaskDependencyType.FinishToStart;

    public Project? Project { get; set; }
    public TaskItem? PredecessorTaskItem { get; set; }
    public TaskItem? SuccessorTaskItem { get; set; }
}

public sealed class ActivityLog : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? TaskItemId { get; set; }
    public Guid AuthorUserId { get; set; }
    public ActivityLogType ActivityType { get; set; } = ActivityLogType.Note;
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }

    public Project? Project { get; set; }
    public TaskItem? TaskItem { get; set; }
    public User? AuthorUser { get; set; }
}

public sealed class Artifact : SoftDeletableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? TaskItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ArtifactType ArtifactType { get; set; } = ArtifactType.Other;
    public ArtifactStatus Status { get; set; } = ArtifactStatus.Draft;
    public Guid? CurrentVersionId { get; set; }
    public Guid CreatedByUserId { get; set; }

    public Project? Project { get; set; }
    public TaskItem? TaskItem { get; set; }
    public ArtifactVersion? CurrentVersion { get; set; }
    public User? CreatedByUser { get; set; }
    public ICollection<ArtifactVersion> Versions { get; } = new List<ArtifactVersion>();
}

public sealed class ArtifactVersion : SoftDeletableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ArtifactId { get; set; }
    public int VersionNumber { get; set; }
    public Guid AttachmentId { get; set; }
    public Guid? FileObjectId { get; set; }
    public string? Notes { get; set; }
    public Guid CreatedByUserId { get; set; }

    public Artifact? Artifact { get; set; }
    public Attachment? Attachment { get; set; }
    public FileObject? FileObject { get; set; }
    public User? CreatedByUser { get; set; }
}

public sealed class Comment : SoftDeletableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid AuthorUserId { get; set; }
    public CommentTargetType TargetType { get; set; }
    public Guid TargetId { get; set; }
    public string Body { get; set; } = string.Empty;

    // Application use cases must authorize comments per TargetType before reading or writing.
    public Workspace? Workspace { get; set; }
    public User? AuthorUser { get; set; }
}

public sealed class Feedback : SoftDeletableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid AuthorUserId { get; set; }
    public Guid? TargetUserId { get; set; }
    public FeedbackTargetType TargetType { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? TaskItemId { get; set; }
    public Guid? ArtifactId { get; set; }
    public Guid? ActivityLogId { get; set; }
    public string Body { get; set; } = string.Empty;
    public int? Rating { get; set; }

    // Only one target reference should be populated per feedback item; validate this in Application use cases.
    public Workspace? Workspace { get; set; }
    public User? AuthorUser { get; set; }
    public User? TargetUser { get; set; }
    public Project? Project { get; set; }
    public TaskItem? TaskItem { get; set; }
    public Artifact? Artifact { get; set; }
    public ActivityLog? ActivityLog { get; set; }
}
