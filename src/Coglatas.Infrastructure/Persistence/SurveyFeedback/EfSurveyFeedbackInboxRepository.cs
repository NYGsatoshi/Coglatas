using Coglatas.Application.SurveyFeedback;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence.SurveyFeedback;

public sealed class EfSurveyFeedbackInboxRepository(AppDbContext dbContext) : ISurveyFeedbackInboxRepository
{
    public async Task<FeedbackInboxSnapshotPage> ReadAsync(Guid tenantId, Guid userId, Guid[] readable,
        Guid[] moderatorScopes, FeedbackInboxFilters filters, FeedbackPagePosition? after,
        int take, CancellationToken cancellationToken)
    {
        if (readable.Length == 0)
        {
            return new FeedbackInboxSnapshotPage([], 0, null);
        }

        var reportFilter = filters.HasReport.HasValue || filters.ReportStatus is not null;
        var answers = Answers(tenantId, filters.FieldKey);
        var reports = dbContext.Set<FeedbackReportCase>().AsNoTracking().Where(report => report.TenantId == tenantId);
        var query =
            from submission in dbContext.Set<FormSubmission>().AsNoTracking()
            join target in dbContext.Set<SurveyTarget>().AsNoTracking()
                on submission.SurveyTargetId equals (Guid?)target.Id
            join readerState in dbContext.Set<FeedbackReadState>().AsNoTracking()
                .Where(state => state.TenantId == tenantId && state.UserId == userId) on submission.Id
                equals readerState.FormSubmissionId into readerStates
            from state in readerStates.DefaultIfEmpty()
            where submission.TenantId == tenantId && target.TenantId == tenantId &&
                readable.Contains(target.PresentationId) && !target.ResultsStopped &&
                submission.Status == FormSubmissionStatus.Submitted &&
                !dbContext.Set<EventFeedbackSettings>().Any(settings => settings.TenantId == tenantId &&
                    settings.EventId == target.EventId && settings.ResultsStopped) &&
                !dbContext.Set<PresentationFeedbackSettings>().Any(settings => settings.TenantId == tenantId &&
                    settings.PresentationId == target.PresentationId && settings.ResultsStopped)
            let moderate = moderatorScopes.Contains(target.PresentationId)
            let isRead = state != null && state.IsRead && state.ObservedRevision == submission.VersionNo
            let hasReport = moderate && reports.Any(report => dbContext.Set<FormSubmissionValue>().Any(value =>
                value.TenantId == tenantId && value.Id == report.FormSubmissionValueId &&
                value.FormSubmissionId == submission.Id))
            let openReports = moderate ? reports.Count(report => report.Status == FeedbackReportStatus.Open &&
                dbContext.Set<FormSubmissionValue>().Any(value => value.TenantId == tenantId &&
                    value.Id == report.FormSubmissionValueId && value.FormSubmissionId == submission.Id)) : 0
            where (!reportFilter || moderate) &&
                (!filters.HasReport.HasValue || hasReport == filters.HasReport.Value) &&
                (!filters.IsRead.HasValue || isRead == filters.IsRead.Value) &&
                (filters.ReportStatus == null || filters.ReportStatus == "none" && !hasReport ||
                    filters.ReportStatus == "open" && openReports > 0 ||
                    filters.ReportStatus == "resolved" && hasReport && openReports == 0) &&
                ((filters.Visibility == null && filters.FieldKey == null) || answers.Any(value =>
                    value.SubmissionId == submission.Id && (!value.IsHidden || moderate) &&
                    (filters.Visibility == null || value.IsHidden == (filters.Visibility == "hidden"))))
            select new
            {
                submission.Id,
                target.PresentationId,
                SubmittedAt = submission.SubmittedAtUtc,
                Revision = submission.VersionNo,
                IsRead = isRead,
                HasReport = hasReport,
                OpenReports = openReports,
                CanModerate = moderate
            };

        var unreadCount = await query.LongCountAsync(row => !row.IsRead, cancellationToken);
        var unresolved = moderatorScopes.Length == 0
            ? (long?)null : await query.SumAsync(row => (long)row.OpenReports, cancellationToken);
        if (after is not null)
        {
            query = query.Where(row => row.SubmittedAt < after.SubmittedAt ||
                row.SubmittedAt == after.SubmittedAt && row.Id.CompareTo(after.Id) < 0);
        }
        var rows = await query.OrderByDescending(row => row.SubmittedAt).ThenByDescending(row => row.Id)
            .Take(take).ToListAsync(cancellationToken);

        var selected = rows.Select(row => row.Id).ToArray();
        var values = await answers.Where(answer => selected.Contains(answer.SubmissionId))
            .OrderBy(answer => answer.SortOrder).ThenBy(answer => answer.FieldKey).ToListAsync(cancellationToken);
        var bySubmission = values.ToLookup(answer => answer.SubmissionId);
        return new FeedbackInboxSnapshotPage(rows.Select(row =>
        {
            var selectedValues = bySubmission[row.Id].Where(answer =>
                    (!answer.IsHidden || row.CanModerate) &&
                    (filters.Visibility is null || answer.IsHidden == (filters.Visibility == "hidden")))
                .Select(answer => new FeedbackValueSnapshot(answer.Id, answer.FieldKey, answer.FieldType.ToString(),
                    answer.CompatibilityRootFieldId.ToString("D"), answer.ValueJson, answer.IsHidden, answer.VisibilityVersionNo, Label: answer.Label))
                .ToArray();
            return new FeedbackInboxSnapshot(
                new SurveyFeedbackSnapshot(row.Id, row.PresentationId, row.SubmittedAt, selectedValues),
                new FeedbackReaderState(row.IsRead, row.Revision),
                row.CanModerate ? row.OpenReports > 0 ? "open" : row.HasReport ? "resolved" : "none" : null,
                row.CanModerate);
        }).ToArray(), unreadCount, unresolved);
    }

    private IQueryable<AnswerProjection> Answers(Guid tenantId, string? fieldKey) =>
        from answer in dbContext.Set<FormSubmissionValue>().AsNoTracking()
        join field in dbContext.Set<FormFieldDefinition>().AsNoTracking() on answer.FormFieldDefinitionId equals field.Id
        join visibility in dbContext.Set<AnswerVisibilityDecision>().AsNoTracking().Where(item => item.TenantId == tenantId)
            on answer.Id equals visibility.FormSubmissionValueId into decisions
        from decision in decisions.DefaultIfEmpty()
        where answer.TenantId == tenantId && field.TenantId == tenantId &&
            (fieldKey == null || field.FieldKey == fieldKey)
        let hidden = decision != null && decision.IsHidden
        select new AnswerProjection
        {
            Id = answer.Id,
            SubmissionId = answer.FormSubmissionId,
            CompatibilityRootFieldId = field.CompatibilityRootFieldId ?? field.Id,
            FieldKey = field.FieldKey,
            Label = field.Label,
            FieldType = field.FieldType,
            ValueJson = hidden ? "true" : answer.ValueJson,
            SortOrder = field.SortOrder,
            IsHidden = hidden,
            VisibilityVersionNo = decision == null ? 1 : decision.VersionNo
        };

    private sealed class AnswerProjection
    {
        public Guid Id { get; init; }
        public Guid SubmissionId { get; init; }
        public Guid CompatibilityRootFieldId { get; init; }
        public required string FieldKey { get; init; }
        public required string Label { get; init; }
        public SurveyFieldType FieldType { get; init; }
        public required string ValueJson { get; init; }
        public int SortOrder { get; init; }
        public bool IsHidden { get; init; }
        public long VisibilityVersionNo { get; init; }
    }
}
