using Coglatas.Application.Common;

namespace Coglatas.Application.Audit;

public interface IAuditFindingsService
{
    Task<Result<AuditFindingsResponse>> ListAsync(
        AuditFindingsQuery query,
        CancellationToken cancellationToken = default);

    Task<Result> UpdateTriageAsync(
        Guid findingId,
        UpdateAuditFindingTriageRequest request,
        CancellationToken cancellationToken = default);

    Task<Result> UpdateWorkflowAsync(
        Guid findingId,
        UpdateAuditFindingWorkflowRequest request,
        CancellationToken cancellationToken = default);
}
