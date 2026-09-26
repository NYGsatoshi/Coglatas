using Coglatas.Application.Projects;
using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>
/// EF-backed staging store for the activation-owned Project workflow. All
/// mutations remain uncommitted until the caller's activation unit of work is saved.
/// </summary>
public sealed class ProjectActivationWorkflowStore(AppDbContext dbContext) : IProjectActivationWorkflowStore
{
    public Task<TaskWorkflowDefinition?> GetDefinitionAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        dbContext.TaskWorkflowDefinitions
            .SingleOrDefaultAsync(definition => definition.ProjectId == projectId, cancellationToken);

    public async Task<IReadOnlyList<TaskWorkflowStage>> ListStagesAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        await dbContext.TaskWorkflowStages
            .AsNoTracking()
            .Where(stage => stage.ProjectId == projectId)
            .OrderBy(stage => stage.SortKey)
            .ThenBy(stage => stage.Id)
            .ToListAsync(cancellationToken);

    public Task AddDefinitionAsync(
        TaskWorkflowDefinition definition,
        CancellationToken cancellationToken = default)
    {
        dbContext.TaskWorkflowDefinitions.Add(definition);
        return Task.CompletedTask;
    }

    public Task AddStageAsync(
        TaskWorkflowStage stage,
        CancellationToken cancellationToken = default)
    {
        dbContext.TaskWorkflowStages.Add(stage);
        return Task.CompletedTask;
    }
}
