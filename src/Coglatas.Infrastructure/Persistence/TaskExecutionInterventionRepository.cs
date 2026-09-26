using Coglatas.Application.Projects;
using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

public sealed class TaskExecutionInterventionRepository(AppDbContext dbContext) : ITaskExecutionInterventionRepository
{
    public Task<TaskExecutionRun?> GetRunForUpdateAsync(
        Guid runId,
        CancellationToken cancellationToken = default) =>
        dbContext.TaskExecutionRuns.SingleOrDefaultAsync(run => run.Id == runId, cancellationToken);

    public Task AddActivityAsync(
        ActivityLog activity,
        CancellationToken cancellationToken = default)
    {
        dbContext.Set<ActivityLog>().Add(activity);
        return Task.CompletedTask;
    }
}
