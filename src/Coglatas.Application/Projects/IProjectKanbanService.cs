using Coglatas.Application.Common;

namespace Coglatas.Application.Projects;

public interface IProjectKanbanService
{
    Task<Result<ProjectKanbanSnapshot>> GetAsync(Guid projectId, ProjectKanbanQuery query, CancellationToken cancellationToken = default);
    Task<Result<ProjectKanbanCommandResponse>> UpdateConfigAsync(Guid projectId, UpdateProjectKanbanConfigRequest request, CancellationToken cancellationToken = default);
    Task<Result<ProjectKanbanCommandResponse>> MoveAsync(Guid taskId, MoveTaskOnKanbanRequest request, CancellationToken cancellationToken = default);
}
