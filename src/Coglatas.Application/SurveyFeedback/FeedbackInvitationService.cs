using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.SurveyFeedback;

public sealed class FeedbackInvitationService(
    SurveyFeedbackAuthorization authorization,
    IFeedbackLifecycleRepository repository,
    ISurveyFeedbackInvalidation invalidation,
    IAuditLogger audit,
    ISurveyFeedbackCursor cursors,
    IClock clock)
{
    public async Task<Result<FeedbackPage<FeedbackInvitationSummary>>> ListAsync(string? cursor, string? direction,
        string? status, int pageSize, CancellationToken cancellationToken)
    {
        var actor = await authorization.ReadActorAsync(cancellationToken);
        if (actor is null)
        {
            return SurveyFeedbackService.AuthenticationRequired<FeedbackPage<FeedbackInvitationSummary>>();
        }

        var cursorScope = new FeedbackCursorScope(actor.TenantId, actor.UserId, null, $"invitations/{direction}/{status}");
        FeedbackPagePosition? after = null;
        if (pageSize is < 1 or > 100 || direction is not (null or "issued" or "received") ||
            status is not (null or "pending" or "active" or "declined" or "revoked" or "expired") ||
            cursor is not null && !cursors.TryRead(cursor, cursorScope, out after))
        {
            return SurveyFeedbackService.Invalid<FeedbackPage<FeedbackInvitationSummary>>();
        }

        var now = clock.UtcNow;
        var invitations = await repository.ReadInvitationsAsync(actor.TenantId, actor.UserId, direction, after,
            cancellationToken);
        var rows = new List<FeedbackInvitationSummary>();
        FeedbackPagePosition? next = null;
        foreach (var invitation in invitations)
        {
            var expired = invitation.Status == FeedbackViewerGrantStatus.Pending && invitation.DecisionDeadlineUtc <= now ||
                invitation.Status == FeedbackViewerGrantStatus.Active && invitation.ExpiresAtUtc <= now;
            var state = expired ? "expired" : invitation.Status.ToString().ToLowerInvariant();
            if (status is not null && state != status)
            {
                continue;
            }

            var access = await authorization.ReadAccessAsync(actor, invitation.PresentationId, cancellationToken);
            if (access is null)
            {
                continue;
            }

            if (rows.Count == pageSize)
            {
                return Result<FeedbackPage<FeedbackInvitationSummary>>.Success(new(rows, cursors.Write(cursorScope, next!)));
            }

            rows.Add(new FeedbackInvitationSummary(invitation.Id, access.Scope.Title,
                invitation.InviterUserId == actor.UserId ? "issued" : "received", state,
                invitation.VersionNo, invitation.DecisionDeadlineUtc, invitation.ExpiresAtUtc));
            next = new FeedbackPagePosition(invitation.CreatedAt, invitation.Id);
        }

        return Result<FeedbackPage<FeedbackInvitationSummary>>.Success(new(rows));
    }

    public Task<Result<FeedbackInvitationDto>> IssueAsync(Guid presentationId, Guid recipientId,
        int? durationSeconds, CancellationToken cancellationToken) =>
        repository.InWriteTransactionAsync(async token =>
        {
            var actor = await authorization.ReadActorAsync(token);
            if (actor is null)
            {
                return SurveyFeedbackService.AuthenticationRequired<FeedbackInvitationDto>();
            }

            var access = await authorization.ReadAccessAsync(actor, presentationId, token);
            if (access is not { Presenter: true } ||
                !await authorization.IsEligibleInviterAsync(actor, access.Scope, token))
            {
                return SurveyFeedbackService.NotFound<FeedbackInvitationDto>();
            }

            if (actor.UserId == recipientId || !await authorization.IsEligibleRecipientAsync(
                    new FeedbackActor(actor.TenantId, recipientId), access.Scope, token))
            {
                return SurveyFeedbackService.Invalid<FeedbackInvitationDto>();
            }

            var policy = await repository.ReadInvitationPolicyAsync(actor.TenantId, access.Scope.EventId, token);
            if (policy is not { Enabled: true })
            {
                return Conflict("InvalidStateTransition");
            }

            var now = clock.UtcNow;
            var prior = await repository.GetLiveInvitationsAsync(actor.TenantId, presentationId, recipientId, token);
            foreach (var grant in prior)
            {
                if (grant.ExpireIfElapsed(now))
                {
                    await WriteAuditAsync(actor, grant, "Expired", token);
                    continue;
                }

                var originalIssuer = new FeedbackActor(actor.TenantId, grant.InviterUserId);
                if (grant.InvitationPolicyId == policy.Id && grant.InvitationPolicyVersion == policy.VersionNo &&
                    await authorization.IsEligibleInviterAsync(originalIssuer, access.Scope, token))
                {
                    return Conflict("InvalidStateTransition");
                }

                grant.Terminate(decline: false, "Invitation eligibility changed.", grant.VersionNo, now);
                await WriteAuditAsync(actor, grant, "EligibilityEnded", token);
                await invalidation.InvalidateAccessAsync(actor.TenantId, presentationId, recipientId, token);
            }

            FeedbackViewerGrant invitation;
            try
            {
                invitation = FeedbackViewerGrant.Issue(presentationId, actor.UserId, recipientId, policy, durationSeconds, now);
            }
            catch (InvalidOperationException)
            {
                return SurveyFeedbackService.Invalid<FeedbackInvitationDto>();
            }

            if (prior.Count > 0)
            {
                // Free the live uniqueness slot before inserting a replacement; both writes remain in this transaction.
                await repository.FlushAsync(token);
            }
            repository.AddInvitation(invitation);
            await WriteAuditAsync(actor, invitation, "Issued", token);
            return Result<FeedbackInvitationDto>.Success(ToDto(invitation, policy));
        }, cancellationToken);

    public Task<Result<FeedbackInvitationDto>> DecideAsync(Guid invitationId, string decision,
        long expectedVersionNo, bool? consent, string? reason, CancellationToken cancellationToken) =>
        repository.InWriteTransactionAsync(async token =>
        {
            var actor = await authorization.ReadActorAsync(token);
            if (actor is null)
            {
                return SurveyFeedbackService.AuthenticationRequired<FeedbackInvitationDto>();
            }

            var invitation = await repository.GetInvitationAsync(actor.TenantId, invitationId, token);
            var access = invitation is null
                ? null : await authorization.ReadAccessAsync(actor, invitation.PresentationId, token);
            if (invitation is null || access is null || !await CanDecideAsync(actor, access, invitation, decision, token))
            {
                return SurveyFeedbackService.NotFound<FeedbackInvitationDto>();
            }

            if (expectedVersionNo <= 0 || decision == "accept" && consent != true ||
                decision == "revoke" && string.IsNullOrWhiteSpace(reason) || reason is { Length: > 1000 })
            {
                return SurveyFeedbackService.Invalid<FeedbackInvitationDto>();
            }

            if (invitation.VersionNo != expectedVersionNo)
            {
                return Conflict("RowVersionConflict");
            }

            var now = clock.UtcNow;
            var policy = await repository.ReadInvitationPolicyAsync(actor.TenantId, access.Scope.EventId, token);
            if (decision is "accept" or "approve")
            {
                if (policy is not { Enabled: true } || access.Scope.ResultsStopped ||
                    !await authorization.IsEligibleInviterAsync(new FeedbackActor(actor.TenantId, invitation.InviterUserId),
                        access.Scope, token) ||
                    !await authorization.IsEligibleRecipientAsync(new FeedbackActor(actor.TenantId, invitation.RecipientUserId),
                        access.Scope, token))
                {
                    return Conflict("InvalidStateTransition");
                }
            }

            try
            {
                switch (decision)
                {
                    case "accept":
                        invitation.Accept(policy!, expectedVersionNo, now);
                        break;
                    case "approve":
                        invitation.Approve(policy!, actor.UserId, expectedVersionNo, now);
                        break;
                    case "revoke":
                    case "decline":
                        invitation.Terminate(decision == "decline", reason, expectedVersionNo, now);
                        break;
                }
            }
            catch (InvalidOperationException)
            {
                return Conflict("InvalidStateTransition");
            }

            await WriteAuditAsync(actor, invitation, decision, token);
            if (invitation.Status == FeedbackViewerGrantStatus.Active)
            {
                await invalidation.InvalidateAccessAsync(actor.TenantId, invitation.PresentationId,
                    invitation.RecipientUserId, token);
            }
            else if (invitation.Status == FeedbackViewerGrantStatus.Revoked)
            {
                await invalidation.InvalidateAccessAsync(actor.TenantId, invitation.PresentationId,
                    invitation.RecipientUserId, token);
            }

            return Result<FeedbackInvitationDto>.Success(ToDto(invitation, policy));
        }, cancellationToken);

    private async Task<bool> CanDecideAsync(FeedbackActor actor, PresentationFeedbackAccess access,
        FeedbackViewerGrant invitation, string decision, CancellationToken cancellationToken)
    {
        var recipient = invitation.RecipientUserId == actor.UserId;
        return decision switch
        {
            "accept" or "decline" => recipient,
            "approve" => await authorization.HasCapabilityAsync(actor, access.Scope, "feedback.invite.approve", cancellationToken),
            "revoke" => invitation.InviterUserId == actor.UserId ||
                recipient && invitation.Status == FeedbackViewerGrantStatus.Active ||
                await authorization.HasCapabilityAsync(actor, access.Scope, "feedback.invite.revoke", cancellationToken),
            _ => false
        };
    }

    private Task WriteAuditAsync(FeedbackActor actor, FeedbackViewerGrant invitation, string action,
        CancellationToken cancellationToken) =>
        audit.LogUserActionAsync(actor.UserId, $"FeedbackInvitation.{action}", nameof(FeedbackViewerGrant), invitation.Id,
            metadata: new Dictionary<string, object?> { ["status"] = invitation.Status.ToString() },
            cancellationToken: cancellationToken);

    private static FeedbackInvitationDto ToDto(FeedbackViewerGrant invitation, FeedbackInvitationPolicy? policy)
    {
        var approval = invitation.ApprovedAtUtc.HasValue ? "operatorApproved" :
            policy is { ApprovedForAutomaticApproval: true } &&
            policy.VersionNo == invitation.InvitationPolicyVersion ? "approvedEventPolicy" : "awaitingOperator";
        return new FeedbackInvitationDto(invitation.Id, invitation.PresentationId,
            invitation.Status.ToString().ToLowerInvariant(), invitation.VersionNo, approval,
            invitation.RecipientAcceptedAtUtc.HasValue, invitation.DecisionDeadlineUtc, invitation.ExpiresAtUtc);
    }

    private static Result<FeedbackInvitationDto> Conflict(string code) =>
        Result<FeedbackInvitationDto>.Failure(new ApplicationErrorDetail(code,
            "The invitation cannot make the requested transition."));
}
