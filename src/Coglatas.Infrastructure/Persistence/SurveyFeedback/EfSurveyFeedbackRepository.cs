using Coglatas.Application.SurveyFeedback;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence.SurveyFeedback;

public sealed class EfSurveyFeedbackRepository(AppDbContext dbContext) : ISurveyFeedbackRepository
{
    public Task<PresentationFeedbackScope?> GetScopeAsync(Guid tenantId, Guid presentationId,
        CancellationToken cancellationToken) =>
        Scopes(tenantId).SingleOrDefaultAsync(scope => scope.Id == presentationId, cancellationToken);

    public Task<bool> IsInvitationEnabledAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken) =>
        dbContext.Set<FeedbackInvitationPolicy>().AsNoTracking().AnyAsync(policy => policy.TenantId == tenantId &&
            policy.EventId == eventId && policy.Enabled, cancellationToken);

    public Task<FeedbackEventScope?> GetEventScopeAsync(Guid tenantId, Guid eventId,
        CancellationToken cancellationToken) =>
        (from activityEvent in dbContext.Set<ActivityEvent>().AsNoTracking()
         join project in dbContext.Set<Project>().AsNoTracking() on activityEvent.ProjectId equals (Guid?)project.Id
         where activityEvent.Id == eventId && activityEvent.TenantId == tenantId && project.TenantId == tenantId &&
             !activityEvent.DeletedAt.HasValue && !project.DeletedAt.HasValue
         select new FeedbackEventScope(activityEvent.Id, project.Id, project.WorkspaceId))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PresentationFeedbackScope>> GetCandidateScopesAsync(Guid tenantId,
        Guid userId, CancellationToken cancellationToken) =>
        await Scopes(tenantId).ToListAsync(cancellationToken);

    public async Task<(bool Presenter, bool Reviewer)> GetAssignmentsAsync(Guid tenantId, Guid presentationId,
        Guid userId, CancellationToken cancellationToken)
    {
        var roles = await dbContext.Set<PresentationParticipant>().AsNoTracking()
            .Where(participant => participant.TenantId == tenantId && participant.PresentationId == presentationId &&
                participant.UserId == userId)
            .Select(participant => participant.Role)
            .ToListAsync(cancellationToken);
        return (roles.Contains(PresentationParticipantRole.Presenter), roles.Contains(PresentationParticipantRole.Reviewer));
    }

    public async Task<IReadOnlyList<Guid>> GetActiveGrantIssuersAsync(Guid tenantId, Guid presentationId,
        Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await (from grant in dbContext.Set<FeedbackViewerGrant>().AsNoTracking()
               join policy in dbContext.Set<FeedbackInvitationPolicy>().AsNoTracking()
                   on grant.InvitationPolicyId equals policy.Id
               where grant.TenantId == tenantId && grant.PresentationId == presentationId &&
                   grant.RecipientUserId == userId && grant.Status == FeedbackViewerGrantStatus.Active &&
                   grant.ExpiresAtUtc > now && policy.TenantId == tenantId && policy.Enabled &&
                   policy.VersionNo == grant.InvitationPolicyVersion
               select grant.InviterUserId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SurveyFeedbackSnapshot>> ReadAsync(Guid tenantId, Guid presentationId,
        string? fieldKey, string visibility, FeedbackPagePosition? after, int take, CancellationToken cancellationToken)
    {
        var questions = Values(tenantId, presentationId, fieldKey);
        var submissions = Submissions(tenantId, presentationId);
        if (visibility != "all")
        {
            questions = questions.Where(answer => answer.IsHidden == (visibility == "hidden"));
        }

        if (fieldKey is not null || visibility == "hidden")
        {
            submissions = submissions.Where(submission =>
                questions.Any(answer => answer.SubmissionId == submission.Id));
        }
        if (after is not null)
        {
            submissions = submissions.Where(submission => submission.SubmittedAtUtc < after.SubmittedAt ||
                submission.SubmittedAtUtc == after.SubmittedAt && submission.Id.CompareTo(after.Id) < 0);
        }

        var rows = await submissions
            .OrderByDescending(submission => submission.SubmittedAtUtc)
            .ThenByDescending(submission => submission.Id)
            .Take(take)
            .Select(submission => new { submission.Id, submission.SubmittedAtUtc })
            .ToArrayAsync(cancellationToken);
        var submissionIds = rows.Select(row => row.Id).ToArray();
        var values = await questions.Where(answer => submissionIds.Contains(answer.SubmissionId))
            .OrderBy(answer => answer.SortOrder)
            .ThenBy(answer => answer.FieldKey)
            .ToListAsync(cancellationToken);

        var bySubmission = values.ToLookup(answer => answer.SubmissionId);
        return rows.Select(row => new SurveyFeedbackSnapshot(row.Id, presentationId, row.SubmittedAtUtc,
            bySubmission[row.Id].Select(ToValue).ToArray())).ToArray();
    }

    public async Task<FeedbackSummaryInput> ReadSummaryAsync(Guid tenantId, Guid presentationId, string? fieldKey,
        CancellationToken cancellationToken)
    {
        var total = await Submissions(tenantId, presentationId).LongCountAsync(cancellationToken);
        var values = await Values(tenantId, presentationId, fieldKey).ToListAsync(cancellationToken);
        return new FeedbackSummaryInput(total, values.Select(ToValue).ToArray());
    }

    private IQueryable<PresentationFeedbackScope> Scopes(Guid tenantId) =>
        from presentation in dbContext.Set<Presentation>().AsNoTracking()
        join activityEvent in dbContext.Set<ActivityEvent>().AsNoTracking() on presentation.EventId equals activityEvent.Id
        join project in dbContext.Set<Project>().AsNoTracking() on presentation.ProjectId equals project.Id
        where presentation.TenantId == tenantId && activityEvent.TenantId == tenantId && project.TenantId == tenantId &&
            !activityEvent.DeletedAt.HasValue && !project.DeletedAt.HasValue && activityEvent.ProjectId == project.Id
        select new PresentationFeedbackScope(tenantId, presentation.Id, presentation.EventId, presentation.ProjectId,
            project.WorkspaceId, presentation.Title, presentation.StartsAtUtc, presentation.EndsAtUtc, presentation.Venue,
            dbContext.Set<EventFeedbackSettings>().Any(settings => settings.TenantId == tenantId &&
                settings.EventId == presentation.EventId && settings.ResultsStopped) ||
            dbContext.Set<PresentationFeedbackSettings>().Any(settings => settings.TenantId == tenantId &&
                settings.PresentationId == presentation.Id && settings.ResultsStopped));

    private IQueryable<FormSubmission> Submissions(Guid tenantId, Guid presentationId) =>
        from submission in dbContext.Set<FormSubmission>().AsNoTracking()
        join target in dbContext.Set<SurveyTarget>().AsNoTracking() on submission.SurveyTargetId equals (Guid?)target.Id
        where submission.TenantId == tenantId && target.TenantId == tenantId &&
            target.PresentationId == presentationId && !target.ResultsStopped &&
            !dbContext.Set<EventFeedbackSettings>().Any(settings => settings.TenantId == tenantId &&
                settings.EventId == target.EventId && settings.ResultsStopped) &&
            !dbContext.Set<PresentationFeedbackSettings>().Any(settings => settings.TenantId == tenantId &&
                settings.PresentationId == target.PresentationId && settings.ResultsStopped) &&
            submission.Status == FormSubmissionStatus.Submitted
        select submission;

    private IQueryable<AnswerProjection> Values(Guid tenantId, Guid presentationId, string? fieldKey) =>
        from submission in Submissions(tenantId, presentationId)
        join answer in dbContext.Set<FormSubmissionValue>().AsNoTracking()
            on submission.Id equals answer.FormSubmissionId
        join field in dbContext.Set<FormFieldDefinition>().AsNoTracking() on answer.FormFieldDefinitionId equals field.Id
        join visibility in dbContext.Set<AnswerVisibilityDecision>().AsNoTracking().Where(item => item.TenantId == tenantId)
            on answer.Id equals visibility.FormSubmissionValueId into decisions
        from decision in decisions.DefaultIfEmpty()
        where answer.TenantId == tenantId && field.TenantId == tenantId &&
            (fieldKey == null || field.FieldKey == fieldKey)
        let hidden = decision != null && decision.IsHidden
        select new AnswerProjection(answer.Id, submission.Id, field.CompatibilityRootFieldId ?? field.Id, field.FieldKey, field.Label, field.FieldType,
            hidden ? "true" : answer.ValueJson, field.SortOrder, hidden, decision == null ? 1 : decision.VersionNo);

    private static FeedbackValueSnapshot ToValue(AnswerProjection answer) =>
        new(answer.Id, answer.FieldKey, answer.FieldType.ToString(), answer.CompatibilityRootFieldId.ToString("D"), answer.ValueJson, answer.IsHidden, answer.VisibilityVersionNo, Label: answer.Label);

    private sealed record AnswerProjection(Guid Id, Guid SubmissionId, Guid CompatibilityRootFieldId, string FieldKey, string Label,
        SurveyFieldType FieldType, string ValueJson, int SortOrder, bool IsHidden, long VisibilityVersionNo);
}
