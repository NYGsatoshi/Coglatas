using Coglatas.Application.Realtime;
using Coglatas.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Coglatas.Web.Realtime;

[Authorize]
public sealed class AppHub(
    IHubSubscriptionAuthorizer authorizer,
    HubSubscriptionRegistry registry,
    RealtimeDiagnostics diagnostics,
    IOptions<RealtimeOptions> options,
    ICurrentTenantAccessor currentTenant,
    ILogger<AppHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (!InitializeTenantFromConnection() ||
            await authorizer.ValidateConnectionAsync(Context.User, Context.ConnectionAborted) is null)
        {
            logger.LogWarning("Realtime connection denied: {Reason}", "ConnectionAuthenticationDenied");
            diagnostics.RecordSubscriptionDenial();
            Context.Abort();
            return;
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        registry.RemoveConnection(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public Task<HubSubscriptionResult> SubscribeUser() => SubscribeAsync(RealtimeSubscriptionType.User, null);
    public Task<HubSubscriptionResult> SubscribeTenant() => SubscribeAsync(RealtimeSubscriptionType.Tenant, null);
    public Task<HubSubscriptionResult> SubscribeWorkspace(Guid workspaceId) => SubscribeAsync(RealtimeSubscriptionType.Workspace, workspaceId);
    public Task<HubSubscriptionResult> SubscribeConversation(Guid conversationId) => SubscribeAsync(RealtimeSubscriptionType.Conversation, conversationId);
    public Task<HubSubscriptionResult> SubscribeProject(Guid projectId) => SubscribeAsync(RealtimeSubscriptionType.Project, projectId);

    public Task<HubSubscriptionResult> UnsubscribeWorkspace(Guid workspaceId) => UnsubscribeAsync(RealtimeSubscriptionType.Workspace, workspaceId);
    public Task<HubSubscriptionResult> UnsubscribeConversation(Guid conversationId) => UnsubscribeAsync(RealtimeSubscriptionType.Conversation, conversationId);
    public Task<HubSubscriptionResult> UnsubscribeProject(Guid projectId) => UnsubscribeAsync(RealtimeSubscriptionType.Project, projectId);

    private async Task<HubSubscriptionResult> SubscribeAsync(RealtimeSubscriptionType subscriptionType, Guid? requestedResourceId)
    {
        var now = DateTimeOffset.UtcNow;
        var configured = options.Value;
        if (!registry.TryRecordAttempt(Context.ConnectionId, now, Math.Max(1, configured.SubscriptionAttemptsPerMinute)))
        {
            return Denied("RateLimited");
        }

        if (!InitializeTenantFromConnection())
        {
            Context.Abort();
            return Denied("ConnectionInvalid");
        }

        var context = await authorizer.ValidateConnectionAsync(Context.User, Context.ConnectionAborted);
        if (context is null)
        {
            Context.Abort();
            return Denied("ConnectionInvalid");
        }

        var resourceId = subscriptionType switch
        {
            RealtimeSubscriptionType.User => context.UserId,
            RealtimeSubscriptionType.Tenant => context.TenantId,
            _ => requestedResourceId ?? Guid.Empty
        };
        if (!await authorizer.CanSubscribeAsync(context, subscriptionType, resourceId, Context.ConnectionAborted))
        {
            return Denied("AccessDenied");
        }

        var subscription = new HubSubscription(
            Context.ConnectionId,
            context.UserId,
            context.SessionId,
            context.TenantId,
            subscriptionType,
            resourceId,
            CanonicalGroupName(subscriptionType, resourceId),
            now);
        if (!registry.TryAdd(subscription, Math.Max(1, configured.SubscriptionLimitPerConnection)))
        {
            return Denied("SubscriptionLimitReached");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, subscription.CanonicalGroupName, Context.ConnectionAborted);
        return new HubSubscriptionResult(true, "Subscribed");
    }

    private async Task<HubSubscriptionResult> UnsubscribeAsync(RealtimeSubscriptionType subscriptionType, Guid resourceId)
    {
        if (resourceId == Guid.Empty || !registry.TryRemove(Context.ConnectionId, subscriptionType, resourceId, out var subscription) || subscription is null)
        {
            return new HubSubscriptionResult(true, "NotSubscribed");
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, subscription.CanonicalGroupName, Context.ConnectionAborted);
        return new HubSubscriptionResult(true, "Unsubscribed");
    }

    private HubSubscriptionResult Denied(string code)
    {
        diagnostics.RecordSubscriptionDenial();
        logger.LogWarning("Realtime subscription denied: {ReasonCode}", code);
        return new HubSubscriptionResult(false, code);
    }

    private bool InitializeTenantFromConnection()
    {
        // The middleware establishes the tenant in the HTTP connection scope,
        // whereas SignalR creates a Hub scope for invocations. Copy only the
        // already-resolved, same-connection tenant into that scope; never infer
        // a tenant from a hub method argument or a client-provided value.
        var requestTenant = Context.GetHttpContext()?.RequestServices.GetService<ICurrentTenant>();
        if (requestTenant?.IsAvailable != true || requestTenant.TenantSlug is null)
        {
            currentTenant.Clear();
            return false;
        }

        currentTenant.SetTenant(requestTenant.TenantId, requestTenant.TenantSlug);
        return true;
    }

    internal static string CanonicalGroupName(RealtimeSubscriptionType subscriptionType, Guid resourceId) => subscriptionType switch
    {
        RealtimeSubscriptionType.User => $"user:{resourceId:D}",
        RealtimeSubscriptionType.Tenant => $"tenant:{resourceId:D}",
        RealtimeSubscriptionType.Workspace => $"workspace:{resourceId:D}",
        RealtimeSubscriptionType.Conversation => $"conversation:{resourceId:D}",
        RealtimeSubscriptionType.Project => $"project:{resourceId:D}",
        _ => throw new ArgumentOutOfRangeException(nameof(subscriptionType))
    };
}

public sealed record HubSubscriptionResult(bool Allowed, string Code);
