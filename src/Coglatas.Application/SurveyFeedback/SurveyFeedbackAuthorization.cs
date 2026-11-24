using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Projects;
using Coglatas.Application.Tenancy;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.SurveyFeedback;

public sealed record FeedbackActor(Guid TenantId, Guid UserId);

public sealed record PresentationFeedbackAccess(
    PresentationFeedbackScope Scope,
    bool Presenter,
    bool Reviewer,
    bool Granted,
    bool Operator)
{
    public bool CanRead => !Scope.ResultsStopped && (Presenter || Reviewer || Granted || Operator);

    public string[] Relations => new[]
    {
        Presenter ? "own" : null,
        Reviewer ? "assigned" : null,
        Granted ? "granted" : null,
        Operator ? "managed" : null
    }.OfType<string>().ToArray();
}

public sealed class SurveyFeedbackAuthorization(
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    ITenantRepository tenants,
    IWorkspaceRepository workspaces,
    IProjectAuthorizationService projects,
    ICapabilityGrantEvaluator capabilities,
    ISurveyFeedbackRepository repository,
    IClock clock)
{
    public async Task<FeedbackActor?> ReadActorAsync(CancellationToken cancellationToken)
    {
        if (currentTenant is not { IsAvailable: true, IsPlatformScope: false } ||
            !currentUser.IsAuthenticated || currentUser.UserId is not { } userId)
        {
            return null;
        }

        var actor = new FeedbackActor(currentTenant.TenantId, userId);
        return await IsCurrentMemberAsync(actor, cancellationToken) ? actor : null;
    }

    public async Task<bool> IsCurrentMemberAsync(FeedbackActor actor, CancellationToken cancellationToken)
    {
        var membership = await tenants.GetCurrentTenantUserAsync(actor.TenantId, actor.UserId, cancellationToken);
        return membership is
        {
            Status: TenantUserStatus.Active,
            User: { Status: UserStatus.Active, DeletedAt: null },
            Tenant: { Status: TenantStatus.Active, DeletedAt: null }
        };
    }

    public async Task<PresentationFeedbackAccess?> ReadAccessAsync(
        FeedbackActor actor, Guid presentationId, CancellationToken cancellationToken)
    {
        var scope = await repository.GetScopeAsync(actor.TenantId, presentationId, cancellationToken);
        if (scope is null || !await projects.CanViewProject(actor.UserId, scope.ProjectId, cancellationToken))
        {
            return null;
        }

        var (presenter, reviewer) = await repository.GetAssignmentsAsync(
            actor.TenantId, scope.Id, actor.UserId, cancellationToken);
        var managed = await HasCapabilityAsync(actor, scope, "feedback.read.managed", cancellationToken);
        var granted = !scope.ResultsStopped && await HasEligibleViewerGrantAsync(actor, scope, cancellationToken);

        return new PresentationFeedbackAccess(scope, presenter, reviewer, granted, managed);
    }

    public async Task<bool> CanReceiveInvalidationAsync(
        FeedbackActor actor, Guid presentationId, CancellationToken cancellationToken)
    {
        if (!await IsCurrentMemberAsync(actor, cancellationToken))
            return false;

        var scope = await repository.GetScopeAsync(actor.TenantId, presentationId, cancellationToken);
        if (scope is not { ResultsStopped: false } || scope.TenantId != actor.TenantId
            || !await projects.CanViewProject(actor.UserId, scope.ProjectId, cancellationToken))
        {
            return false;
        }

        var (presenter, reviewer) = await repository.GetAssignmentsAsync(
            actor.TenantId, scope.Id, actor.UserId, cancellationToken);
        // An invalidation only needs the Boolean read decision. A current direct
        // assignment already provides it, independently of managed or invited relations.
        return presenter || reviewer
            || await HasCapabilityAsync(actor, scope, "feedback.read.managed", cancellationToken)
            || await HasEligibleViewerGrantAsync(actor, scope, cancellationToken);
    }

    private async Task<bool> HasEligibleViewerGrantAsync(
        FeedbackActor actor, PresentationFeedbackScope scope, CancellationToken cancellationToken)
    {
        var issuers = await repository.GetActiveGrantIssuersAsync(
            actor.TenantId, scope.Id, actor.UserId, clock.UtcNow, cancellationToken);
        foreach (var issuerId in issuers)
        {
            if (await IsEligibleInviterAsync(new FeedbackActor(actor.TenantId, issuerId), scope, cancellationToken))
                return true;
        }

        return false;
    }

    public async Task<FeedbackActionCapabilities> ReadActionCapabilitiesAsync(FeedbackActor actor,
        PresentationFeedbackAccess access, CancellationToken cancellationToken) =>
        new(
            await CanReportAsync(actor, access, cancellationToken),
            access.Presenter && await IsEligibleInviterAsync(actor, access.Scope, cancellationToken) &&
                await repository.IsInvitationEnabledAsync(actor.TenantId, access.Scope.EventId, cancellationToken),
            await CanUsePrivateNotesAsync(actor, access, cancellationToken),
            await CanShareFeedbackAsync(actor, access, cancellationToken),
            await CanExportAsync(actor, access, detail: false, cancellationToken),
            await CanExportAsync(actor, access, detail: true, cancellationToken));

    public Task<bool> CanReportAsync(FeedbackActor actor, PresentationFeedbackAccess access,
        CancellationToken cancellationToken) => access.CanRead
        ? HasCapabilityAsync(actor, access.Scope, "feedback.report", cancellationToken)
        : Task.FromResult(false);

    public async Task<bool> CanUsePrivateNotesAsync(FeedbackActor actor, PresentationFeedbackAccess? access,
        CancellationToken cancellationToken)
    {
        if (access is not { CanRead: true, Reviewer: true } ||
            !await HasCapabilityAsync(actor, access.Scope, "feedback.note.private", cancellationToken))
        {
            return false;
        }

        return await workspaces.GetCurrentMemberAsync(access.Scope.WorkspaceId, actor.UserId, cancellationToken) is
            { Status: MembershipStatus.Active, Role: WorkspaceRole.Adviser };
    }

    public async Task<bool> CanShareFeedbackAsync(FeedbackActor actor, PresentationFeedbackAccess? access,
        CancellationToken cancellationToken) =>
        access is { CanRead: true, Reviewer: true } &&
        await HasCapabilityAsync(actor, access.Scope, "feedback.feedback.share", cancellationToken);

    public async Task<bool> CanExportAsync(FeedbackActor actor, PresentationFeedbackAccess access, bool detail,
        CancellationToken cancellationToken) =>
        access.CanRead && (detail ? access.Reviewer || access.Operator : access.Presenter || access.Reviewer || access.Operator) &&
        await HasCapabilityAsync(actor, access.Scope, detail ? "feedback.export.detail" : "feedback.export.aggregate",
            cancellationToken);

    public async Task<bool> CanApproveEventInvitationsAsync(FeedbackActor actor, Guid eventId,
        CancellationToken cancellationToken)
    {
        var scope = await repository.GetEventScopeAsync(actor.TenantId, eventId, cancellationToken);
        return scope is not null && await projects.CanViewProject(actor.UserId, scope.ProjectId, cancellationToken) &&
            await HasCapabilityAsync(actor, scope.WorkspaceId, "feedback.invite.approve", cancellationToken);
    }

    public async Task<bool> CanCompareAsync(FeedbackActor actor, Guid eventId, CancellationToken cancellationToken)
    {
        var scope = await repository.GetEventScopeAsync(actor.TenantId, eventId, cancellationToken);
        return scope is not null && await projects.CanViewProject(actor.UserId, scope.ProjectId, cancellationToken) &&
            await HasCapabilityAsync(actor, scope.WorkspaceId, "feedback.aggregate.cross", cancellationToken);
    }

    public async Task<bool> IsEligibleInviterAsync(
        FeedbackActor actor, PresentationFeedbackScope scope, CancellationToken cancellationToken)
    {
        if (!await IsCurrentMemberAsync(actor, cancellationToken) ||
            !await projects.CanViewProject(actor.UserId, scope.ProjectId, cancellationToken))
        {
            return false;
        }

        var (isPresenter, _) = await repository.GetAssignmentsAsync(
            actor.TenantId, scope.Id, actor.UserId, cancellationToken);
        return isPresenter && await HasCapabilityAsync(actor, scope, "feedback.invite.create", cancellationToken);
    }

    public async Task<bool> IsEligibleRecipientAsync(
        FeedbackActor recipient, PresentationFeedbackScope scope, CancellationToken cancellationToken) =>
        recipient.TenantId == scope.TenantId &&
        await IsCurrentMemberAsync(recipient, cancellationToken) &&
        await workspaces.GetCurrentMemberAsync(scope.WorkspaceId, recipient.UserId, cancellationToken) is
            { Status: MembershipStatus.Active } membership && membership.TenantId == scope.TenantId &&
        await projects.CanViewProject(recipient.UserId, scope.ProjectId, cancellationToken);

    public async Task<bool> HasCapabilityAsync(FeedbackActor actor, PresentationFeedbackScope scope,
        string key, CancellationToken cancellationToken)
    {
        if (actor.TenantId != scope.TenantId)
        {
            return false;
        }

        return await HasCapabilityAsync(actor, scope.WorkspaceId, key, cancellationToken);
    }

    private async Task<bool> HasCapabilityAsync(FeedbackActor actor, Guid workspaceId, string key,
        CancellationToken cancellationToken) =>
        await capabilities.HasActiveGrantAsync(actor.UserId, actor.TenantId, key,
                   CapabilityScopeType.Workspace, workspaceId, cancellationToken) ||
               await capabilities.HasActiveGrantAsync(actor.UserId, actor.TenantId, key,
                   CapabilityScopeType.Tenant, actor.TenantId, cancellationToken);
}
