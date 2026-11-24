using System.Data;
using Coglatas.Application.Common;
using Coglatas.Application.SurveyFeedback;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Coglatas.Infrastructure.Persistence.SurveyFeedback;

public sealed class EfFeedbackLifecycleRepository(AppDbContext dbContext) : IFeedbackLifecycleRepository
{
    public async Task<Result<T>> InWriteTransactionAsync<T>(Func<CancellationToken, Task<Result<T>>> operation,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var result = await operation(cancellationToken);
            if (result.IsSuccess)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
            }

            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return Conflict<T>("RowVersionConflict");
        }
        catch (Exception exception) when (IsConcurrentFailure(exception))
        {
            dbContext.ChangeTracker.Clear();
            return Conflict<T>("ConcurrentModification");
        }
    }

    public Task<FeedbackInvitationPolicy?> ReadInvitationPolicyAsync(Guid tenantId, Guid eventId,
        CancellationToken cancellationToken) =>
        dbContext.Set<FeedbackInvitationPolicy>().AsNoTracking()
            .SingleOrDefaultAsync(policy => policy.TenantId == tenantId && policy.EventId == eventId, cancellationToken);

    public Task<FeedbackViewerGrant?> GetInvitationAsync(Guid tenantId, Guid invitationId,
        CancellationToken cancellationToken) =>
        dbContext.Set<FeedbackViewerGrant>()
            .SingleOrDefaultAsync(grant => grant.TenantId == tenantId && grant.Id == invitationId, cancellationToken);

    public async Task<IReadOnlyList<FeedbackViewerGrant>> GetLiveInvitationsAsync(Guid tenantId, Guid presentationId,
        Guid recipientId, CancellationToken cancellationToken) =>
        await dbContext.Set<FeedbackViewerGrant>().Where(grant => grant.TenantId == tenantId &&
            grant.PresentationId == presentationId && grant.RecipientUserId == recipientId &&
            (grant.Status == FeedbackViewerGrantStatus.Pending || grant.Status == FeedbackViewerGrantStatus.Active))
            .ToListAsync(cancellationToken);

    public async Task<FeedbackAnswerResource?> FindAnswerAsync(Guid tenantId, Guid answerId,
        CancellationToken cancellationToken)
    {
        var query =
            from answer in dbContext.Set<FormSubmissionValue>().AsNoTracking()
            join field in dbContext.Set<FormFieldDefinition>().AsNoTracking()
                on answer.FormFieldDefinitionId equals field.Id
            join submission in dbContext.Set<FormSubmission>().AsNoTracking()
                on answer.FormSubmissionId equals submission.Id
            join target in dbContext.Set<SurveyTarget>().AsNoTracking()
                on submission.SurveyTargetId equals (Guid?)target.Id
            where answer.TenantId == tenantId && field.TenantId == tenantId && submission.TenantId == tenantId &&
                target.TenantId == tenantId && answer.Id == answerId && !target.ResultsStopped &&
                submission.Status == FormSubmissionStatus.Submitted
            select new { target.PresentationId, SubmissionId = submission.Id, AnswerId = answer.Id, field.FieldKey, field.FieldType };
        var row = await query.SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var decision = await GetVisibilityAsync(tenantId, row.AnswerId, cancellationToken);
        return new FeedbackAnswerResource(row.PresentationId, row.SubmissionId, row.AnswerId,
            row.FieldKey, row.FieldType.ToString(), decision?.IsHidden ?? false, decision?.VersionNo ?? 1);
    }

    public Task<AnswerVisibilityDecision?> GetVisibilityAsync(Guid tenantId, Guid answerId,
        CancellationToken cancellationToken) =>
        dbContext.Set<AnswerVisibilityDecision>().SingleOrDefaultAsync(
            decision => decision.TenantId == tenantId && decision.FormSubmissionValueId == answerId,
            cancellationToken);

    public Task<string?> ReadAnswerValueJsonAsync(Guid tenantId, Guid answerId, CancellationToken cancellationToken) =>
        dbContext.Set<FormSubmissionValue>().Where(answer => answer.TenantId == tenantId && answer.Id == answerId)
            .Select(answer => answer.ValueJson).SingleOrDefaultAsync(cancellationToken);

    public Task<FeedbackReportCase?> GetReportAsync(Guid tenantId, Guid reportId, CancellationToken cancellationToken) =>
        dbContext.Set<FeedbackReportCase>().SingleOrDefaultAsync(
            report => report.TenantId == tenantId && report.Id == reportId, cancellationToken);

    public Task<FeedbackSubmissionResource?> GetSubmissionAsync(Guid tenantId, Guid submissionId,
        CancellationToken cancellationToken) =>
        (from submission in dbContext.Set<FormSubmission>()
         join target in dbContext.Set<SurveyTarget>().AsNoTracking() on submission.SurveyTargetId equals (Guid?)target.Id
         where submission.TenantId == tenantId && submission.Id == submissionId && target.TenantId == tenantId &&
             !target.ResultsStopped
         select new FeedbackSubmissionResource(target.PresentationId, submission))
        .SingleOrDefaultAsync(cancellationToken);

    public Task<FeedbackReadState?> GetReadStateAsync(Guid tenantId, Guid submissionId, Guid userId,
        CancellationToken cancellationToken) =>
        dbContext.Set<FeedbackReadState>().SingleOrDefaultAsync(state => state.TenantId == tenantId &&
            state.FormSubmissionId == submissionId && state.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ReadInvalidationCandidatesAsync(Guid tenantId, Guid presentationId,
        CancellationToken cancellationToken) =>
        await dbContext.Set<PresentationParticipant>().AsNoTracking()
            .Where(participant => participant.TenantId == tenantId && participant.PresentationId == presentationId &&
                participant.UserId.HasValue)
            .Select(participant => participant.UserId!.Value)
            .Union(dbContext.Set<FeedbackViewerGrant>().AsNoTracking()
                .Where(grant => grant.TenantId == tenantId && grant.PresentationId == presentationId &&
                    grant.Status == FeedbackViewerGrantStatus.Active)
                .Select(grant => grant.RecipientUserId))
            .ToListAsync(cancellationToken);

    public void AddInvitation(FeedbackViewerGrant invitation) => dbContext.Set<FeedbackViewerGrant>().Add(invitation);

    public void AddVisibility(AnswerVisibilityDecision decision) => dbContext.Set<AnswerVisibilityDecision>().Add(decision);

    public void AddReport(FeedbackReportCase report) => dbContext.Set<FeedbackReportCase>().Add(report);

    public void AddReadState(FeedbackReadState state) => dbContext.Set<FeedbackReadState>().Add(state);

    private static Result<T> Conflict<T>(string code) => Result<T>.Failure(
        new ApplicationErrorDetail(code, "A concurrent feedback change prevented this operation."));

    private static bool IsConcurrentFailure(Exception exception) =>
        (exception as PostgresException ?? exception.InnerException as PostgresException)?.SqlState is
            PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation;
}
