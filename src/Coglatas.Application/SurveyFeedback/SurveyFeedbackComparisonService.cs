using Coglatas.Application.Common;

namespace Coglatas.Application.SurveyFeedback;

// This source must provide the current approved event cohort policy.
// There is deliberately no production registration or inferred policy in the core host.
public interface IApprovedFeedbackComparisonPolicySource
{
    Task<ApprovedFeedbackComparisonPolicy?> ReadCurrentAsync(Guid tenantId, Guid eventId,
        CancellationToken cancellationToken);
}

public interface ISurveyFeedbackComparisonRepository
{
    Task<IReadOnlyList<FeedbackComparisonSample>?> ReadAvailableCohortAsync(Guid tenantId, Guid eventId,
        Guid[] presentationIds, string[] commonFieldKeys, CancellationToken cancellationToken);
}

public sealed class SurveyFeedbackComparisonService(
    SurveyFeedbackAuthorization authorization,
    ISurveyFeedbackComparisonRepository repository,
    IApprovedFeedbackComparisonPolicySource? policies = null)
{
    public async Task<Result<FeedbackComparison>> CompareAsync(Guid eventId, string? comparisonRef,
        CancellationToken cancellationToken)
    {
        var actor = await authorization.ReadActorAsync(cancellationToken);
        if (actor is null)
        {
            return SurveyFeedbackService.AuthenticationRequired<FeedbackComparison>();
        }

        if (!await authorization.CanCompareAsync(actor, eventId, cancellationToken))
        {
            return SurveyFeedbackService.NotFound<FeedbackComparison>();
        }

        var policy = policies is null ? null : await policies.ReadCurrentAsync(actor.TenantId, eventId, cancellationToken);
        if (!SurveyFeedbackComparisonProjection.Matches(policy, actor.TenantId, eventId, comparisonRef))
        {
            return Result<FeedbackComparison>.Success(new());
        }

        var fields = policy.Fields.Select(field => field.FieldKey).ToArray();
        var cohorts = new List<FeedbackComparisonCohort>();
        foreach (var cohort in policy.Cohorts)
        {
            foreach (var id in cohort.PresentationIds)
            {
                var access = await authorization.ReadAccessAsync(actor, id, cancellationToken);
                if (access is not { CanRead: true } || access.Scope.EventId != eventId ||
                    !(access.Reviewer || access.Operator))
                {
                    // A partial cohort would become a new, unapproved differencing query.
                    return Result<FeedbackComparison>.Success(new());
                }
            }

            var samples = await repository.ReadAvailableCohortAsync(actor.TenantId, eventId,
                cohort.PresentationIds.ToArray(), fields, cancellationToken);
            if (samples is null)
            {
                return Result<FeedbackComparison>.Success(new());
            }

            cohorts.Add(SurveyFeedbackComparisonProjection.Project(policy, cohort, samples));
        }

        return Result<FeedbackComparison>.Success(new(new(cohorts)));
    }
}
