using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

public sealed class ResearchPlanRepository(AppDbContext dbContext) : IResearchPlanRepository
{
    public Task<ResearchPlan?> GetForTaskAsync(Guid taskItemId, CancellationToken cancellationToken = default) =>
        dbContext.ResearchPlans
            .AsNoTracking()
            .SingleOrDefaultAsync(plan => plan.TaskItemId == taskItemId, cancellationToken);

    public Task<ResearchPlan?> GetForTaskForUpdateAsync(Guid taskItemId, CancellationToken cancellationToken = default) =>
        dbContext.ResearchPlans
            .SingleOrDefaultAsync(plan => plan.TaskItemId == taskItemId, cancellationToken);

    public Task<ResearchPlanExecutionSnapshot?> GetCurrentExecutionSnapshotForTaskAsync(
        Guid taskItemId,
        CancellationToken cancellationToken = default) =>
        dbContext.ResearchPlans
            .AsNoTracking()
            .Where(plan => plan.TaskItemId == taskItemId && plan.CurrentRevisionId != null)
            .Select(plan => new ResearchPlanExecutionSnapshot(
                plan.CurrentRevisionId!.Value,
                plan.CurrentRevision!.RevisionNo))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<ResearchPlanRevision?> GetRevisionAsync(
        Guid researchPlanId,
        Guid revisionId,
        CancellationToken cancellationToken = default) =>
        dbContext.ResearchPlanRevisions
            .AsNoTracking()
            .Include(revision => revision.Steps)
            .SingleOrDefaultAsync(
                revision => revision.ResearchPlanId == researchPlanId && revision.Id == revisionId,
                cancellationToken);

    public Task<long?> GetLatestRevisionNumberAsync(
        Guid researchPlanId,
        CancellationToken cancellationToken = default) =>
        dbContext.ResearchPlanRevisions
            .Where(revision => revision.ResearchPlanId == researchPlanId)
            .MaxAsync(revision => (long?)revision.RevisionNo, cancellationToken);

    public Task AddPlanAsync(ResearchPlan plan, CancellationToken cancellationToken = default)
    {
        dbContext.ResearchPlans.Add(plan);
        return Task.CompletedTask;
    }

    public Task AddRevisionAsync(ResearchPlanRevision revision, CancellationToken cancellationToken = default)
    {
        dbContext.ResearchPlanRevisions.Add(revision);
        return Task.CompletedTask;
    }

    public Task AddStepsAsync(IEnumerable<ResearchPlanStep> steps, CancellationToken cancellationToken = default)
    {
        dbContext.ResearchPlanSteps.AddRange(steps);
        return Task.CompletedTask;
    }
}
