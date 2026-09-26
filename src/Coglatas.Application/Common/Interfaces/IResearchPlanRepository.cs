using Coglatas.Domain.Entities;

namespace Coglatas.Application.Common.Interfaces;

/// <summary>
/// Persistence boundary for Task-owned Research Plans. Revision content is
/// append-only; only the aggregate's current-revision pointer is mutable.
/// </summary>
public interface IResearchPlanRepository
{
    Task<ResearchPlan?> GetForTaskAsync(Guid taskItemId, CancellationToken cancellationToken = default);
    Task<ResearchPlan?> GetForTaskForUpdateAsync(Guid taskItemId, CancellationToken cancellationToken = default);
    Task<ResearchPlanExecutionSnapshot?> GetCurrentExecutionSnapshotForTaskAsync(
        Guid taskItemId,
        CancellationToken cancellationToken = default);
    Task<ResearchPlanRevision?> GetRevisionAsync(Guid researchPlanId, Guid revisionId, CancellationToken cancellationToken = default);
    Task<long?> GetLatestRevisionNumberAsync(Guid researchPlanId, CancellationToken cancellationToken = default);
    Task AddPlanAsync(ResearchPlan plan, CancellationToken cancellationToken = default);
    Task AddRevisionAsync(ResearchPlanRevision revision, CancellationToken cancellationToken = default);
    Task AddStepsAsync(IEnumerable<ResearchPlanStep> steps, CancellationToken cancellationToken = default);
}

/// <summary>
/// Minimal immutable plan provenance copied into a Task execution run. The
/// revision itself remains the canonical, append-only plan content.
/// </summary>
public sealed record ResearchPlanExecutionSnapshot(Guid RevisionId, long RevisionNo);
