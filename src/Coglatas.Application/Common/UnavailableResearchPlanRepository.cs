using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.Common;

/// <summary>
/// Fail-closed fallback for hosts that compose <c>AddApplication()</c> without
/// Infrastructure persistence. Full application composition registers the
/// canonical repository later and overrides this fallback.
/// </summary>
internal sealed class UnavailableResearchPlanRepository : IResearchPlanRepository
{
    public Task<ResearchPlan?> GetForTaskAsync(
        Guid taskItemId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ResearchPlan?>(null);

    public Task<ResearchPlan?> GetForTaskForUpdateAsync(
        Guid taskItemId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ResearchPlan?>(null);

    public Task<ResearchPlanExecutionSnapshot?> GetCurrentExecutionSnapshotForTaskAsync(
        Guid taskItemId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ResearchPlanExecutionSnapshot?>(null);

    public Task<ResearchPlanRevision?> GetRevisionAsync(
        Guid researchPlanId,
        Guid revisionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ResearchPlanRevision?>(null);

    public Task<long?> GetLatestRevisionNumberAsync(
        Guid researchPlanId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<long?>(null);

    public Task AddPlanAsync(ResearchPlan plan, CancellationToken cancellationToken = default) =>
        throw Unavailable();

    public Task AddRevisionAsync(ResearchPlanRevision revision, CancellationToken cancellationToken = default) =>
        throw Unavailable();

    public Task AddStepsAsync(
        IEnumerable<ResearchPlanStep> steps,
        CancellationToken cancellationToken = default) =>
        throw Unavailable();

    private static InvalidOperationException Unavailable() =>
        new("Research Plan persistence is unavailable.");
}
