using Coglatas.Application.Common;
using Coglatas.Application.SurveyFeedback;
using Coglatas.Application.SurveyFeedback.Csv;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence.SurveyFeedback;

public sealed class EfFeedbackCsvRepository(AppDbContext dbContext) : IFeedbackCsvRepository
{
    public Task<SurveyFeedbackExportRequest?> FindRequestAsync(
        Guid tenantId, Guid requesterId, Guid requestId, CancellationToken cancellationToken) =>
        dbContext.Set<SurveyFeedbackExportRequest>().AsNoTracking().SingleOrDefaultAsync(
            item => item.TenantId == tenantId && item.RequesterId == requesterId && item.Id == requestId,
            cancellationToken);

    public Task<SurveyFeedbackExportGrant?> FindGrantAsync(
        Guid tenantId, Guid requesterId, Guid exportRequestId, Guid grantId, CancellationToken cancellationToken) =>
        dbContext.Set<SurveyFeedbackExportGrant>().AsNoTracking().SingleOrDefaultAsync(
            item => item.TenantId == tenantId && item.RecipientUserId == requesterId &&
                item.ExportRequestId == exportRequestId && item.Id == grantId, cancellationToken);

    public void AddRequest(SurveyFeedbackExportRequest request) => dbContext.Set<SurveyFeedbackExportRequest>().Add(request);

    public void AddGrant(SurveyFeedbackExportGrant grant) => dbContext.Set<SurveyFeedbackExportGrant>().Add(grant);

    public async Task<Result<FeedbackCsvSnapshot>> ReadVisibleAsync(
        PresentationFeedbackScope scope, int maximum, CancellationToken cancellationToken)
    {
        var legalScope = LegalScope(scope);
        var settings = await legalScope.SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
            return Result<FeedbackCsvSnapshot>.Failure(new ApplicationErrorDetail("NotFound", "The requested resource was not found."));

        var targets = dbContext.Set<SurveyTarget>().AsNoTracking().Where(target =>
            target.TenantId == scope.TenantId && target.EventId == scope.EventId &&
            target.PresentationId == scope.Id && legalScope.Any());
        var targetVersions = await targets.OrderBy(item => item.Id)
            .Select(item => new FeedbackCsvTarget(item.Id, item.VersionNo, item.ResultsStopped))
            .Take(maximum + 1).ToArrayAsync(cancellationToken);
        if (targetVersions.Length > maximum)
            return TooLarge();

        var submissions =
            from submission in dbContext.Set<FormSubmission>().AsNoTracking()
            join target in targets on submission.SurveyTargetId equals (Guid?)target.Id
            where submission.TenantId == scope.TenantId && !target.ResultsStopped &&
                submission.Status == FormSubmissionStatus.Submitted
            select submission;
        Task<FeedbackCsvSubmission[]> ReadSubmissionVersionsAsync() => submissions.OrderBy(item => item.Id)
            .Select(item => new FeedbackCsvSubmission(item.Id, item.SubmittedAtUtc, item.VersionNo))
            .Take(maximum + 1).ToArrayAsync(cancellationToken);
        var submissionVersions = await ReadSubmissionVersionsAsync();
        if (submissionVersions.Length > maximum)
            return TooLarge();

        var answers =
            from submission in submissions
            join answer in dbContext.Set<FormSubmissionValue>().AsNoTracking()
                on submission.Id equals answer.FormSubmissionId
            join field in dbContext.Set<FormFieldDefinition>().AsNoTracking()
                on answer.FormFieldDefinitionId equals field.Id
            join decision in dbContext.Set<AnswerVisibilityDecision>().AsNoTracking()
                .Where(item => item.TenantId == scope.TenantId)
                on answer.Id equals decision.FormSubmissionValueId into decisions
            from visibility in decisions.DefaultIfEmpty()
            where answer.TenantId == scope.TenantId && field.TenantId == scope.TenantId &&
                (visibility == null || !visibility.IsHidden)
            orderby submission.SubmittedAtUtc, submission.Id, field.SortOrder, answer.Id
            select new FeedbackCsvAnswer(answer.Id, submission.Id, field.Id, field.FieldKey, field.Label, field.FieldType,
                field.OptionsJson, answer.ValueJson, visibility == null ? 1 : visibility.VersionNo, false,
                field.CompatibilityRootFieldId);
        var values = await answers.Take(maximum + 1).ToArrayAsync(cancellationToken);
        if (values.Length > maximum)
            return TooLarge();

        var currentSettings = await legalScope.SingleOrDefaultAsync(cancellationToken);
        var currentTargetVersions = await targets.OrderBy(item => item.Id)
            .Select(item => new FeedbackCsvTarget(item.Id, item.VersionNo, item.ResultsStopped))
            .Take(maximum + 1).ToArrayAsync(cancellationToken);
        var currentSubmissionVersions = await ReadSubmissionVersionsAsync();
        var submissionIds = submissionVersions.Select(item => item.Id).ToHashSet();
        if (currentSettings != settings || !currentTargetVersions.SequenceEqual(targetVersions) ||
            !currentSubmissionVersions.SequenceEqual(submissionVersions) ||
            values.Any(item => !submissionIds.Contains(item.SubmissionId)))
        {
            return Result<FeedbackCsvSnapshot>.Failure(new ApplicationErrorDetail(
                "ConcurrentModification", "The presentation changed during export preparation."));
        }

        return Result<FeedbackCsvSnapshot>.Success(new FeedbackCsvSnapshot(
            settings.PresentationVersion, settings.EventVersion, settings.PresentationSettingsVersion,
            targetVersions, submissionVersions, values));
    }

    private IQueryable<SettingsVersions> LegalScope(PresentationFeedbackScope scope) =>
        from presentation in dbContext.Set<Presentation>().AsNoTracking()
        join activityEvent in dbContext.Set<ActivityEvent>().AsNoTracking() on presentation.EventId equals activityEvent.Id
        join project in dbContext.Set<Project>().AsNoTracking() on presentation.ProjectId equals project.Id
        where presentation.TenantId == scope.TenantId && presentation.Id == scope.Id &&
            activityEvent.TenantId == scope.TenantId && activityEvent.Id == scope.EventId &&
            project.TenantId == scope.TenantId && project.Id == scope.ProjectId && project.WorkspaceId == scope.WorkspaceId &&
            !activityEvent.DeletedAt.HasValue && !project.DeletedAt.HasValue && activityEvent.ProjectId == project.Id
        where !dbContext.Set<EventFeedbackSettings>().Any(settings =>
                settings.TenantId == scope.TenantId && settings.EventId == activityEvent.Id && settings.ResultsStopped) &&
            !dbContext.Set<PresentationFeedbackSettings>().Any(settings =>
                settings.TenantId == scope.TenantId && settings.PresentationId == presentation.Id && settings.ResultsStopped)
        select new SettingsVersions(presentation.VersionNo,
            dbContext.Set<EventFeedbackSettings>()
                .Where(settings => settings.TenantId == scope.TenantId && settings.EventId == activityEvent.Id)
                .Select(settings => settings.VersionNo).FirstOrDefault(),
            dbContext.Set<PresentationFeedbackSettings>()
                .Where(settings => settings.TenantId == scope.TenantId && settings.PresentationId == presentation.Id)
                .Select(settings => settings.VersionNo).FirstOrDefault());

    private static Result<FeedbackCsvSnapshot> TooLarge() => Result<FeedbackCsvSnapshot>.Failure(
        new ApplicationErrorDetail("ExportTooLarge", "The export exceeds the supported bounds."));

    private sealed record SettingsVersions(long PresentationVersion, long EventVersion, long PresentationSettingsVersion);
}
