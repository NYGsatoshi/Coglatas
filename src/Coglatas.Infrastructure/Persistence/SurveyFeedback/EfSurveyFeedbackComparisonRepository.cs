using Coglatas.Application.SurveyFeedback;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence.SurveyFeedback;

public sealed class EfSurveyFeedbackComparisonRepository(AppDbContext dbContext) : ISurveyFeedbackComparisonRepository
{
    private const int MaximumCohortValues = 100_000;

    public async Task<IReadOnlyList<FeedbackComparisonSample>?> ReadAvailableCohortAsync(Guid tenantId,
        Guid eventId, Guid[] presentationIds, string[] commonFieldKeys, CancellationToken cancellationToken)
    {
        var available = dbContext.Set<Presentation>().AsNoTracking()
            .Where(presentation => presentation.TenantId == tenantId && presentation.EventId == eventId &&
                presentationIds.Contains(presentation.Id) &&
                !dbContext.Set<EventFeedbackSettings>().Any(settings =>
                    settings.TenantId == tenantId && settings.EventId == eventId && settings.ResultsStopped) &&
                !dbContext.Set<PresentationFeedbackSettings>().Any(settings =>
                    settings.TenantId == tenantId && settings.PresentationId == presentation.Id && settings.ResultsStopped) &&
                !dbContext.Set<SurveyTarget>().Any(target =>
                    target.TenantId == tenantId && target.EventId == eventId &&
                    target.PresentationId == presentation.Id && target.ResultsStopped));
        if (await available.CountAsync(cancellationToken) != presentationIds.Length)
        {
            return null;
        }

        var values =
            from submission in dbContext.Set<FormSubmission>().AsNoTracking()
            join target in dbContext.Set<SurveyTarget>().AsNoTracking()
                on submission.SurveyTargetId equals target.Id
            join answer in dbContext.Set<FormSubmissionValue>().AsNoTracking()
                on submission.Id equals answer.FormSubmissionId
            join field in dbContext.Set<FormFieldDefinition>().AsNoTracking()
                on answer.FormFieldDefinitionId equals field.Id
            where available.Count() == presentationIds.Length &&
                submission.TenantId == tenantId && submission.Status == FormSubmissionStatus.Submitted &&
                target.TenantId == tenantId && target.EventId == eventId &&
                presentationIds.Contains(target.PresentationId) &&
                answer.TenantId == tenantId && answer.PublishedSurveySchemaId != null &&
                answer.PublishedSurveySchemaId == submission.PublishedSurveySchemaId &&
                field.TenantId == tenantId && field.FormVersionId.HasValue && commonFieldKeys.Contains(field.FieldKey)
            select new FeedbackComparisonSample(submission.Id, field.FieldKey, field.CompatibilityRootFieldId ?? field.Id,
                field.FieldType,
                field.FieldType == SurveyFieldType.Number || field.FieldType == SurveyFieldType.Scale ||
                field.FieldType == SurveyFieldType.SingleChoice || field.FieldType == SurveyFieldType.MultipleChoice
                    ? answer.ValueJson : "null");
        var samples = await values.Take(MaximumCohortValues + 1).ToListAsync(cancellationToken);

        // Avoid computing or disclosing a comparison from a truncated set.
        return samples.Count <= MaximumCohortValues ? samples : null;
    }
}
