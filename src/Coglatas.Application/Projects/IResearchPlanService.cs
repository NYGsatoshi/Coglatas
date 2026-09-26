using Coglatas.Application.Common;

namespace Coglatas.Application.Projects;

public interface IResearchPlanService
{
    Task<Result<ResearchPlanResponse>> GetAsync(Guid taskItemId, CancellationToken cancellationToken = default);
    Task<Result<ResearchPlanPreviewResponse>> PreviewAsync(Guid taskItemId, PreviewResearchPlanRequest request, CancellationToken cancellationToken = default);
    Task<Result<ResearchPlanResponse>> ReplaceAsync(Guid taskItemId, ReplaceResearchPlanRequest request, CancellationToken cancellationToken = default);
}
