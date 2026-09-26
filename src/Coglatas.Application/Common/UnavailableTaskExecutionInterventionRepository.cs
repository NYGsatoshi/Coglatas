using Coglatas.Application.Projects;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.Common;

/// <summary>
/// Fail-closed fallback for hosts that compose <c>AddApplication()</c> without
/// Infrastructure persistence. Full application composition registers the
/// canonical repository later and overrides this fallback.
/// </summary>
internal sealed class UnavailableTaskExecutionInterventionRepository : ITaskExecutionInterventionRepository
{
    public Task<TaskExecutionRun?> GetRunForUpdateAsync(
        Guid runId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<TaskExecutionRun?>(null);

    public Task AddActivityAsync(
        ActivityLog activity,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Task execution intervention persistence is unavailable.");
}
