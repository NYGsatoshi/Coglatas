using Coglatas.Application.Common;

namespace Coglatas.Application.Planning;

public interface IPlanningService
{
    Task<Result<ProjectGanttResponse>> GetGanttAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<Result<ProjectDashboardResponse>> GetDashboardAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<Result<MyTasksProjectionPage>> ListMyTasksAsync(MyTasksQuery query, CancellationToken cancellationToken = default);

    Task<Result<MyTasksCountsResponse>> GetMyTaskCountsAsync(MyTasksQuery query, CancellationToken cancellationToken = default);

    Task<Result<ProjectWorkloadResponse>> GetWorkloadAsync(Guid projectId, CancellationToken cancellationToken = default);
}
