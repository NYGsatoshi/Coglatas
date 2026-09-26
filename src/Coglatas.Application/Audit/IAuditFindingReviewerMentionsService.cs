using Coglatas.Application.Common;

namespace Coglatas.Application.Audit;

public interface IAuditFindingReviewerMentionsService
{
    Task<Result> MentionAsync(
        Guid findingId,
        MentionAuditFindingReviewerRequest request,
        CancellationToken cancellationToken = default);
}
