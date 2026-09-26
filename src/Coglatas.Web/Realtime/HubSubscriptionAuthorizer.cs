using System.Security.Claims;
using Coglatas.Application.Auth;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Messaging;
using Coglatas.Application.Projects;
using Coglatas.Application.Realtime;
using Coglatas.Application.Workspaces;

namespace Coglatas.Web.Realtime;

public sealed class HubSubscriptionAuthorizer(
    IUserSessionService sessions,
    ICurrentTenant currentTenant,
    IFeatureFlagService featureFlags,
    IWorkspaceAuthorizationService workspaces,
    IConversationAuthorizationService conversations,
    IProjectAuthorizationService projects) : IHubSubscriptionAuthorizer
{
    public async Task<HubAuthorizationContext?> ValidateConnectionAsync(ClaimsPrincipal? principal, CancellationToken cancellationToken = default)
    {
        // SignalR owns the connection lifetime. Read its authenticated principal
        // directly rather than relying on IHttpContextAccessor from a scoped
        // dependency, which may no longer point at the negotiate HTTP request.
        var userId = TryGetGuid(principal, ClaimTypes.NameIdentifier);
        var sessionId = TryGetGuid(principal, "session_id");
        if (!currentTenant.IsAvailable || principal?.Identity?.IsAuthenticated != true || !userId.HasValue || !sessionId.HasValue ||
            !await featureFlags.IsEnabledAsync(FeatureKeys.RealtimeSignalR, cancellationToken))
        {
            return null;
        }

        var session = await sessions.ValidateSessionAsync(userId.Value, sessionId.Value, currentTenant.TenantId, true, cancellationToken);
        return session.IsValid
            ? new HubAuthorizationContext(userId.Value, sessionId.Value, currentTenant.TenantId)
            : null;
    }

    public async Task<bool> CanSubscribeAsync(HubAuthorizationContext context, RealtimeSubscriptionType subscriptionType, Guid resourceId, CancellationToken cancellationToken = default)
    {
        if (resourceId == Guid.Empty || !currentTenant.IsAvailable || currentTenant.TenantId != context.TenantId ||
            !await featureFlags.IsEnabledAsync(FeatureKeys.AuthorizedRealtimeGroups, cancellationToken))
        {
            return false;
        }

        var session = await sessions.ValidateSessionAsync(context.UserId, context.SessionId, context.TenantId, true, cancellationToken);
        if (!session.IsValid)
        {
            return false;
        }

        return subscriptionType switch
        {
            RealtimeSubscriptionType.User => resourceId == context.UserId,
            RealtimeSubscriptionType.Tenant => resourceId == context.TenantId,
            RealtimeSubscriptionType.Workspace => await workspaces.CanViewWorkspace(context.UserId, resourceId, cancellationToken),
            RealtimeSubscriptionType.Conversation => await conversations.CanViewConversation(context.UserId, resourceId, cancellationToken),
            RealtimeSubscriptionType.Project => await projects.CanViewProject(context.UserId, resourceId, cancellationToken),
            _ => false
        };
    }

    private static Guid? TryGetGuid(ClaimsPrincipal? principal, string claimType)
    {
        return Guid.TryParse(principal?.FindFirstValue(claimType), out var value) ? value : null;
    }
}

public sealed record HubAuthorizationContext(Guid UserId, Guid SessionId, Guid TenantId);

public interface IHubSubscriptionAuthorizer
{
    Task<HubAuthorizationContext?> ValidateConnectionAsync(ClaimsPrincipal? principal, CancellationToken cancellationToken = default);
    Task<bool> CanSubscribeAsync(HubAuthorizationContext context, RealtimeSubscriptionType subscriptionType, Guid resourceId, CancellationToken cancellationToken = default);
}
