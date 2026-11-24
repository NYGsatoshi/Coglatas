using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.SurveyFeedback;

public sealed class FeedbackInvitationService(
    SurveyFeedbackAuthorization authorization,
    IFeedbackLifecycleRepository repository,
    ISurveyFeedbackInvalidation invalidation,
    IAuditLogger audit,
    IClock clock)
{
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
                if (!grant.ExpireIfElapsed(now))
                {
                    return Conflict("InvalidStateTransition");
                }
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
