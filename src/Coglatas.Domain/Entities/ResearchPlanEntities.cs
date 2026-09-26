using Coglatas.Domain.Common;
using Coglatas.Domain.Enums;

namespace Coglatas.Domain.Entities;

/// <summary>
/// Task-owned Research Plan aggregate. The mutable aggregate only points to
/// its current revision; plan content itself is append-only.
/// </summary>
public sealed class ResearchPlan : Entity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid TaskItemId { get; set; }
    public Guid? CurrentRevisionId { get; set; }
    public long VersionNo { get; set; } = 1;

    public Project? Project { get; set; }
    public TaskItem? TaskItem { get; set; }
    /// <summary>
    /// The current revision is constrained to this same plan. It is nullable
    /// only until the first saved revision is appended.
    /// </summary>
    public ResearchPlanRevision? CurrentRevision { get; set; }
    public ICollection<ResearchPlanRevision> Revisions { get; } = new List<ResearchPlanRevision>();
}

/// <summary>
/// An immutable complete snapshot of a Research Plan. Editing a plan creates
/// a new revision instead of mutating an earlier one.
/// </summary>
public sealed class ResearchPlanRevision : Entity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid TaskItemId { get; set; }
    public Guid ResearchPlanId { get; set; }
    public long RevisionNo { get; set; } = 1;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }

    public ResearchPlan? ResearchPlan { get; set; }
    public Project? Project { get; set; }
    public TaskItem? TaskItem { get; set; }
    public User? CreatedByUser { get; set; }
    public ICollection<ResearchPlanStep> Steps { get; } = new List<ResearchPlanStep>();
}

/// <summary>
/// An immutable ordered step belonging to one Research Plan revision.
/// </summary>
public sealed class ResearchPlanStep : Entity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid TaskItemId { get; set; }
    public Guid ResearchPlanId { get; set; }
    public Guid ResearchPlanRevisionId { get; set; }
    public int SortOrder { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string ScopeSummary { get; set; } = string.Empty;
    public ResearchPlanStepStatus Status { get; set; } = ResearchPlanStepStatus.Planned;

    public ResearchPlan? ResearchPlan { get; set; }
    public ResearchPlanRevision? ResearchPlanRevision { get; set; }
    public Project? Project { get; set; }
    public TaskItem? TaskItem { get; set; }
}
