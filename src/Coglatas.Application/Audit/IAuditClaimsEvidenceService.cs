using Coglatas.Application.Common;

namespace Coglatas.Application.Audit;

public interface IAuditClaimsEvidenceService
{
    Task<Result<AuditClaimsEvidenceResponse>> GetAsync(
        Guid artifactVersionId,
        CancellationToken cancellationToken = default);
}
