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
        CancellationToken cancellationToken, string concurrentErrorCode = "ConcurrentModification")
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
            return Conflict<T>(concurrentErrorCode);
        }
    }

    public Task FlushAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    public Task<FeedbackInvitationPolicy?> ReadInvitationPolicyAsync(Guid tenantId, Guid eventId,
        CancellationToken cancellationToken) =>
        dbContext.Set<FeedbackInvitationPolicy>().AsNoTracking()
            .SingleOrDefaultAsync(policy => policy.TenantId == tenantId && policy.EventId == eventId, cancellationToken);

    public Task<FeedbackInvitationPolicy?> FindInvitationPolicyForUpdateAsync(Guid tenantId, Guid eventId,
        CancellationToken cancellationToken) =>
        dbContext.Set<FeedbackInvitationPolicy>()
            .SingleOrDefaultAsync(policy => policy.TenantId == tenantId && policy.EventId == eventId, cancellationToken);

    public void AddInvitationPolicy(FeedbackInvitationPolicy policy) =>
        dbContext.Set<FeedbackInvitationPolicy>().Add(policy);

    public async Task<IReadOnlyList<FeedbackViewerGrant>> ReadInvitationsAsync(Guid tenantId, Guid userId,
        string? direction, FeedbackPagePosition? after, CancellationToken cancellationToken)
    {
        var query = dbContext.Set<FeedbackViewerGrant>().AsNoTracking()
            .Where(grant => grant.TenantId == tenantId &&
                (direction != "received" && grant.InviterUserId == userId ||
                 direction != "issued" && grant.RecipientUserId == userId));
        if (after is not null)
        {
            query = query.Where(grant => grant.CreatedAt < after.SubmittedAt ||
                grant.CreatedAt == after.SubmittedAt && grant.Id.CompareTo(after.Id) < 0);
        }
        return await query.OrderByDescending(grant => grant.CreatedAt).ThenByDescending(grant => grant.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FeedbackApprovalCandidate>> ReadApprovalCandidatesAsync(Guid tenantId,
        Guid eventId, DateTimeOffset now, FeedbackPagePosition? after, int take, CancellationToken cancellationToken)
    {
        var query =
            from invitation in dbContext.Set<FeedbackViewerGrant>().AsNoTracking()
            join presentation in dbContext.Set<Presentation>().AsNoTracking()
                on invitation.PresentationId equals presentation.Id
            join policy in dbContext.Set<FeedbackInvitationPolicy>().AsNoTracking()
                on invitation.InvitationPolicyId equals policy.Id
            join inviter in dbContext.Set<User>().AsNoTracking() on invitation.InviterUserId equals inviter.Id
            join recipient in dbContext.Set<User>().AsNoTracking() on invitation.RecipientUserId equals recipient.Id
            where invitation.TenantId == tenantId && presentation.TenantId == tenantId &&
                policy.TenantId == tenantId && policy.EventId == eventId && presentation.EventId == eventId &&
                invitation.InvitationPolicyVersion == policy.VersionNo && policy.Enabled &&
                !policy.ApprovedForAutomaticApproval && invitation.Status == FeedbackViewerGrantStatus.Pending &&
                invitation.ApprovedAtUtc == null && invitation.DecisionDeadlineUtc > now
            select new { Invitation = invitation, InviterLabel = inviter.DisplayName, RecipientLabel = recipient.DisplayName };
        if (after is not null)
        {
            query = query.Where(row => row.Invitation.CreatedAt < after.SubmittedAt ||
                row.Invitation.CreatedAt == after.SubmittedAt && row.Invitation.Id.CompareTo(after.Id) < 0);
        }

        return await query.OrderByDescending(row => row.Invitation.CreatedAt)
            .ThenByDescending(row => row.Invitation.Id).Take(take)
            .Select(row => new FeedbackApprovalCandidate(row.Invitation, row.InviterLabel, row.RecipientLabel))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EligibleFeedbackRecipient>> ReadRecipientCandidatesAsync(Guid tenantId,
        Guid workspaceId, Guid inviterId, Guid? after, int take, CancellationToken cancellationToken)
    {
        var candidates =
            from member in dbContext.Set<WorkspaceMember>().AsNoTracking()
            join user in dbContext.Set<User>().AsNoTracking() on member.UserId equals user.Id
            where member.TenantId == tenantId && member.WorkspaceId == workspaceId &&
                member.Status == MembershipStatus.Active && member.UserId != inviterId &&
                user.Status == UserStatus.Active && !user.DeletedAt.HasValue &&
                user.DisplayName != "" &&
                dbContext.Set<TenantUser>().Any(tenantMember => tenantMember.TenantId == tenantId &&
                    tenantMember.UserId == member.UserId && tenantMember.Status == TenantUserStatus.Active)
            select new { RecipientRef = member.UserId, Label = user.DisplayName };
        if (after is { } last)
        {
            candidates = candidates.Where(candidate => candidate.RecipientRef.CompareTo(last) > 0);
        }

        return await candidates.OrderBy(candidate => candidate.RecipientRef).Take(take)
            .Select(candidate => new EligibleFeedbackRecipient(candidate.RecipientRef, candidate.Label))
            .ToListAsync(cancellationToken);
    }

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

    public async Task<IReadOnlyList<FeedbackReportQueueProjection>> ReadReportQueueAsync(Guid tenantId,
        Guid presentationId, FeedbackReportStatus? status, FeedbackPagePosition? after, int take,
        CancellationToken cancellationToken)
    {
        var query = ScopedReports(tenantId).AsNoTracking()
            .Where(report => report.PresentationId == presentationId &&
                (!status.HasValue || report.Status == status));
        if (after is not null)
        {
            query = query.Where(report => report.CreatedAt < after.SubmittedAt ||
                report.CreatedAt == after.SubmittedAt && report.Id.CompareTo(after.Id) < 0);
        }

        return await query.OrderByDescending(report => report.CreatedAt).ThenByDescending(report => report.Id)
            .Take(take)
            .Select(report => new FeedbackReportQueueProjection(report.Id, report.Status, report.VersionNo,
                report.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public Task<FeedbackReportCase?> GetReportAsync(Guid tenantId, Guid reportId, CancellationToken cancellationToken) =>
        ScopedReports(tenantId).SingleOrDefaultAsync(report => report.Id == reportId, cancellationToken);

    private IQueryable<FeedbackReportCase> ScopedReports(Guid tenantId) =>
        from report in dbContext.Set<FeedbackReportCase>()
        join answer in dbContext.Set<FormSubmissionValue>() on report.FormSubmissionValueId equals answer.Id
        join submission in dbContext.Set<FormSubmission>() on answer.FormSubmissionId equals submission.Id
        join target in dbContext.Set<SurveyTarget>() on submission.SurveyTargetId equals (Guid?)target.Id
        where report.TenantId == tenantId && answer.TenantId == tenantId &&
            submission.TenantId == tenantId && target.TenantId == tenantId &&
            target.PresentationId == report.PresentationId && !target.ResultsStopped
        select report;

    public Task<FeedbackSubmissionResource?> GetSubmissionAsync(Guid tenantId, Guid submissionId,
        CancellationToken cancellationToken) =>
        (from submission in dbContext.Set<FormSubmission>()
         join target in dbContext.Set<SurveyTarget>() on submission.SurveyTargetId equals (Guid?)target.Id
         where submission.TenantId == tenantId && submission.Id == submissionId && target.TenantId == tenantId &&
             !target.ResultsStopped
         select new FeedbackSubmissionResource(target.PresentationId, submission))
        .AsTracking()
        .SingleOrDefaultAsync(cancellationToken);

    public Task<FeedbackReadState?> GetReadStateAsync(Guid tenantId, Guid submissionId, Guid userId,
        CancellationToken cancellationToken) =>
        dbContext.Set<FeedbackReadState>().SingleOrDefaultAsync(state => state.TenantId == tenantId &&
            state.FormSubmissionId == submissionId && state.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ReadInvalidationCandidatesAsync(Guid tenantId, Guid presentationId,
        CancellationToken cancellationToken)
    {
        var operatorCandidates =
            from presentation in dbContext.Set<Presentation>().AsNoTracking()
            join project in dbContext.Set<Project>().AsNoTracking() on presentation.ProjectId equals project.Id
            from grant in dbContext.Set<CapabilityGrant>().AsNoTracking()
            where presentation.TenantId == tenantId && project.TenantId == tenantId &&
                presentation.Id == presentationId && grant.TenantId == tenantId &&
                grant.RevokedAt == null && grant.CapabilityKey == "feedback.read.managed" &&
                (grant.ScopeType == CapabilityScopeType.Workspace && grant.ScopeId == project.WorkspaceId ||
                 grant.ScopeType == CapabilityScopeType.Tenant && grant.ScopeId == tenantId)
            select grant.SubjectUserId;
        return await dbContext.Set<PresentationParticipant>().AsNoTracking()
            .Where(participant => participant.TenantId == tenantId && participant.PresentationId == presentationId &&
                participant.UserId.HasValue)
            .Select(participant => participant.UserId!.Value)
            .Union(dbContext.Set<FeedbackViewerGrant>().AsNoTracking()
                .Where(grant => grant.TenantId == tenantId && grant.PresentationId == presentationId &&
                    grant.Status == FeedbackViewerGrantStatus.Active)
                .Select(grant => grant.RecipientUserId))
            .Union(operatorCandidates)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TeacherPrivateNote>> ReadPrivateNotesAsync(Guid tenantId, Guid presentationId,
        Guid authorId, FeedbackPagePosition? after, int take, CancellationToken cancellationToken)
    {
        var query = dbContext.Set<TeacherPrivateNote>().AsNoTracking()
            .Where(note => note.TenantId == tenantId && note.PresentationId == presentationId && note.AuthorUserId == authorId);
        if (after is not null)
        {
            query = query.Where(note => note.CreatedAt < after.SubmittedAt ||
                note.CreatedAt == after.SubmittedAt && note.Id.CompareTo(after.Id) < 0);
        }
        return await query.OrderByDescending(note => note.CreatedAt).ThenByDescending(note => note.Id)
            .Take(take).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SharedTeacherFeedback>> ReadSharedFeedbackAsync(Guid tenantId, Guid presentationId,
        Guid? authorId, FeedbackPagePosition? after, int take, CancellationToken cancellationToken)
    {
        var query = dbContext.Set<SharedTeacherFeedback>().AsNoTracking()
            .Where(note => note.TenantId == tenantId && note.PresentationId == presentationId &&
                (!authorId.HasValue || note.AuthorUserId == authorId));
        if (after is not null)
        {
            query = query.Where(note => note.CreatedAt < after.SubmittedAt ||
                note.CreatedAt == after.SubmittedAt && note.Id.CompareTo(after.Id) < 0);
        }
        return await query.OrderByDescending(note => note.CreatedAt).ThenByDescending(note => note.Id)
            .Take(take).ToListAsync(cancellationToken);
    }

    public void AddPrivateNote(TeacherPrivateNote note) => dbContext.Set<TeacherPrivateNote>().Add(note);

    public void AddSharedFeedback(SharedTeacherFeedback note) => dbContext.Set<SharedTeacherFeedback>().Add(note);

    public void AddInvitation(FeedbackViewerGrant invitation) => dbContext.Set<FeedbackViewerGrant>().Add(invitation);

    public void AddVisibility(AnswerVisibilityDecision decision) => dbContext.Set<AnswerVisibilityDecision>().Add(decision);

    public void AddReport(FeedbackReportCase report) => dbContext.Set<FeedbackReportCase>().Add(report);

    public void AddReadState(FeedbackReadState state) => dbContext.Set<FeedbackReadState>().Add(state);

    private static Result<T> Conflict<T>(string code) => Result<T>.Failure(
        new ApplicationErrorDetail(code, "A concurrent feedback change prevented this operation."));

    private static bool IsConcurrentFailure(Exception exception)
    {
        while (exception is not PostgresException && exception.InnerException is { } inner)
        {
            exception = inner;
        }

        return exception is PostgresException
        {
            SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation
        };
    }
}
