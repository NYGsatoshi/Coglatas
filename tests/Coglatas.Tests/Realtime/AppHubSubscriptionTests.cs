using System.Security.Claims;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Realtime;
using Coglatas.Web.Realtime;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coglatas.Tests.Realtime;

// Actual Hub, authorizer and registry with a test group transport; no WebSocket or G05 acceptance claim.
public sealed class AppHubSubscriptionTests
{
    [Theory]
    [InlineData("missing-http")]
    [InlineData("missing-service")]
    [InlineData("unavailable")]
    [InlineData("missing-slug")]
    public async Task UnresolvedConnectionTenantClearsStaleScopeAndAbortsWithoutAuthorization(string invalid)
    {
        using var fixture = new HubFixture();
        if (invalid == "missing-http") { fixture.Caller.Features.Set<IHttpContextFeature>(null); }
        if (invalid == "missing-service") { fixture.Http.RequestServices = new EmptyServices(); }
        if (invalid == "unavailable") { fixture.RequestTenant.IsAvailable = false; }
        if (invalid == "missing-slug") { fixture.RequestTenant.TenantSlug = null; }

        await fixture.Hub.OnConnectedAsync();

        Assert.True(fixture.Caller.Aborted);
        Assert.False(fixture.Authorization.Tenant.IsAvailable);
        Assert.Empty(fixture.Authorization.Sessions.Calls);
        Assert.Empty(fixture.Authorization.Features.Calls);
        Assert.Empty(fixture.Groups.Added);
        Assert.Equal(1, fixture.Diagnostics.Snapshot().SubscriptionDenialCount);
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("disabled-feature")]
    [InlineData("revoked-session")]
    public async Task FailedConnectionAuthenticationAbortsBeforeAnyGroupRegistration(string denied)
    {
        using var fixture = new HubFixture();
        if (denied == "anonymous") { fixture.Caller.Principal = SubscriptionAuthorizationFixture.Principal(null, null, false); }
        if (denied == "disabled-feature") { fixture.Authorization.Features.SignalREnabled = false; }
        if (denied == "revoked-session") { fixture.Authorization.Sessions.Valid = false; }

        await fixture.Hub.OnConnectedAsync();

        Assert.True(fixture.Caller.Aborted);
        Assert.Empty(fixture.Groups.Added);
        Assert.Empty(fixture.Registry.RemoveConnection(fixture.Caller.ConnectionId));
        Assert.Equal(1, fixture.Diagnostics.Snapshot().SubscriptionDenialCount);
    }

    [Fact]
    public async Task AuthenticatedConnectionUsesResolvedTenantAndExactConnectionToken()
    {
        using var fixture = new HubFixture();
        fixture.Authorization.Tenant.SetTenant(Guid.NewGuid(), "stale");

        await fixture.Hub.OnConnectedAsync();

        Assert.False(fixture.Caller.Aborted);
        Assert.Equal(fixture.RequestTenant.TenantId, fixture.Authorization.Tenant.TenantId);
        Assert.Equal("synthetic", fixture.Authorization.Tenant.TenantSlug);
        Assert.Equal(new SessionCall(fixture.Authorization.UserId, fixture.Authorization.SessionId,
            fixture.RequestTenant.TenantId, true, fixture.Caller.ConnectionAborted), Assert.Single(fixture.Authorization.Sessions.Calls));
        Assert.Empty(fixture.Groups.Added);
        Assert.Equal(0, fixture.Diagnostics.Snapshot().SubscriptionDenialCount);
    }

    [Fact]
    public async Task UserAndTenantGroupsDeriveOnlyFromValidatedIdentityAndResolvedConnectionTenant()
    {
        using var fixture = new HubFixture();
        fixture.Caller.Principal.AddIdentity(new ClaimsIdentity([new Claim("tenant_id", Guid.NewGuid().ToString())]));

        Assert.Equal(new HubSubscriptionResult(true, "Subscribed"), await fixture.Hub.SubscribeUser());
        fixture.Authorization.Tenant.SetTenant(Guid.NewGuid(), "stale-invocation-scope");
        Assert.Equal(new HubSubscriptionResult(true, "Subscribed"), await fixture.Hub.SubscribeTenant());

        var user = Assert.Single(fixture.Registry.GetForTarget(fixture.Authorization.TenantId, RealtimeSubscriptionType.User, fixture.Authorization.UserId));
        var tenant = Assert.Single(fixture.Registry.GetForTarget(fixture.Authorization.TenantId, RealtimeSubscriptionType.Tenant, fixture.Authorization.TenantId));
        Assert.Equal(fixture.Authorization.UserId, user.UserId);
        Assert.Equal(fixture.Authorization.SessionId, user.SessionId);
        Assert.Equal(fixture.Authorization.TenantId, user.TenantId);
        Assert.Equal(fixture.Authorization.UserId, tenant.UserId);
        Assert.Equal(fixture.Authorization.SessionId, tenant.SessionId);
        Assert.Equal(fixture.Caller.ConnectionId, user.ConnectionId);
        Assert.Equal(fixture.Caller.ConnectionId, tenant.ConnectionId);
        Assert.Equal(new[] { $"user:{fixture.Authorization.UserId:D}", $"tenant:{fixture.Authorization.TenantId:D}" }, fixture.Groups.Added.Select(call => call.Group));
        Assert.All(fixture.Groups.Added, call => Assert.Equal(fixture.Caller.ConnectionAborted, call.Token));
        Assert.All(fixture.Authorization.Sessions.Calls, call => Assert.Equal(fixture.Authorization.TenantId, call.TenantId));
        Assert.Empty(fixture.Authorization.Resources.Calls);
    }

    [Theory]
    [InlineData(RealtimeSubscriptionType.Workspace)]
    [InlineData(RealtimeSubscriptionType.Conversation)]
    [InlineData(RealtimeSubscriptionType.Project)]
    public async Task DeniedResourceNeverReachesTransportOrRegistry(RealtimeSubscriptionType type)
    {
        using var fixture = new HubFixture();
        fixture.Authorization.Resources.Allowed = false;
        var resource = Guid.NewGuid();

        Assert.Equal(new HubSubscriptionResult(false, "AccessDenied"), await SubscribeAsync(fixture.Hub, type, resource));

        Assert.False(fixture.Caller.Aborted);
        Assert.Empty(fixture.Groups.Added);
        Assert.Empty(fixture.Registry.GetForTarget(fixture.Authorization.TenantId, type, resource));
        Assert.Equal(new ResourceCall(type, fixture.Authorization.UserId, resource, fixture.Caller.ConnectionAborted), Assert.Single(fixture.Authorization.Resources.Calls));
        Assert.All(fixture.Authorization.Sessions.Calls, call => Assert.Equal(fixture.Caller.ConnectionAborted, call.Token));
        Assert.All(fixture.Authorization.Features.Calls, call => Assert.Equal(fixture.Caller.ConnectionAborted, call.Token));
        Assert.Equal(1, fixture.Diagnostics.Snapshot().SubscriptionDenialCount);
    }

    [Fact]
    public async Task InvocationWithoutConnectionTenantAbortsBeforeAnyGroupOrSessionAccess()
    {
        using var fixture = new HubFixture();
        fixture.RequestTenant.IsAvailable = false;

        Assert.Equal(new HubSubscriptionResult(false, "ConnectionInvalid"), await fixture.Hub.SubscribeProject(Guid.NewGuid()));

        Assert.True(fixture.Caller.Aborted);
        Assert.False(fixture.Authorization.Tenant.IsAvailable);
        Assert.Empty(fixture.Authorization.Sessions.Calls);
        Assert.Empty(fixture.Groups.Added);
    }

    [Fact]
    public async Task EachInvocationRechecksCurrentResourceAndSessionAuthorityBeforeAddingGroups()
    {
        using var fixture = new HubFixture();
        var workspace = Guid.NewGuid();
        Assert.True((await fixture.Hub.SubscribeWorkspace(workspace)).Allowed);
        fixture.Authorization.Resources.Allowed = false;

        Assert.Equal(new HubSubscriptionResult(false, "AccessDenied"), await fixture.Hub.SubscribeWorkspace(workspace));
        Assert.Equal(2, fixture.Authorization.Resources.Calls.Count);
        Assert.Single(fixture.Groups.Added);
        fixture.Authorization.Sessions.Valid = false;
        Assert.Equal(new HubSubscriptionResult(false, "ConnectionInvalid"), await fixture.Hub.SubscribeProject(Guid.NewGuid()));
        Assert.True(fixture.Caller.Aborted);
        Assert.Single(fixture.Groups.Added);
        await fixture.Hub.OnDisconnectedAsync(null);
        Assert.Empty(fixture.Registry.GetForTarget(fixture.Authorization.TenantId, RealtimeSubscriptionType.Workspace, workspace));
        Assert.Equal(2, fixture.Diagnostics.Snapshot().SubscriptionDenialCount);
    }

    [Fact]
    public async Task SessionRevokedAfterConnectionValidationCannotAuthorizeThePendingGroupJoin()
    {
        using var fixture = new HubFixture();
        fixture.Authorization.Sessions.AfterValidation = () => fixture.Authorization.Sessions.Valid = false;
        var workspace = Guid.NewGuid();

        Assert.Equal(new HubSubscriptionResult(false, "AccessDenied"), await fixture.Hub.SubscribeWorkspace(workspace));

        Assert.Equal(2, fixture.Authorization.Sessions.Calls.Count);
        Assert.Empty(fixture.Authorization.Resources.Calls);
        Assert.Empty(fixture.Groups.Added);
        Assert.Empty(fixture.Registry.GetForTarget(fixture.Authorization.TenantId, RealtimeSubscriptionType.Workspace, workspace));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task SubscriptionCapPreservesExistingGroupAndUnsubscribeReleasesCapacity(int configuredLimit)
    {
        using var fixture = new HubFixture();
        fixture.Options.SubscriptionLimitPerConnection = configuredLimit;
        var workspace = Guid.NewGuid();
        var project = Guid.NewGuid();
        Assert.True((await fixture.Hub.SubscribeWorkspace(workspace)).Allowed);
        Assert.True((await fixture.Hub.SubscribeWorkspace(workspace)).Allowed);

        Assert.Equal(new HubSubscriptionResult(false, "SubscriptionLimitReached"), await fixture.Hub.SubscribeProject(project));

        Assert.Single(fixture.Registry.GetForTarget(fixture.Authorization.TenantId, RealtimeSubscriptionType.Workspace, workspace));
        Assert.Empty(fixture.Registry.GetForTarget(fixture.Authorization.TenantId, RealtimeSubscriptionType.Project, project));
        Assert.Equal(2, fixture.Groups.Added.Count);
        Assert.Equal(new HubSubscriptionResult(true, "Unsubscribed"), await fixture.Hub.UnsubscribeWorkspace(workspace));
        Assert.True((await fixture.Hub.SubscribeProject(project)).Allowed);
        Assert.Single(fixture.Registry.GetForTarget(fixture.Authorization.TenantId, RealtimeSubscriptionType.Project, project));
        Assert.Empty(fixture.Registry.GetForTarget(fixture.Authorization.TenantId, RealtimeSubscriptionType.Workspace, workspace));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 2)]
    public async Task RateBoundRejectsFurtherAttemptsBeforeAnyNewAuthorizationOrGroupEffects(int configuredLimit, int acceptedAttempts)
    {
        using var fixture = new HubFixture();
        fixture.Options.SubscriptionAttemptsPerMinute = configuredLimit;
        var workspace = Guid.NewGuid();
        for (var attempt = 0; attempt < acceptedAttempts; attempt++)
        {
            Assert.True((await fixture.Hub.SubscribeWorkspace(workspace)).Allowed);
        }
        var sessionCalls = fixture.Authorization.Sessions.Calls.Count;
        var resourceCalls = fixture.Authorization.Resources.Calls.Count;

        Assert.Equal(new HubSubscriptionResult(false, "RateLimited"), await fixture.Hub.SubscribeProject(Guid.NewGuid()));

        Assert.Equal(sessionCalls, fixture.Authorization.Sessions.Calls.Count);
        Assert.Equal(resourceCalls, fixture.Authorization.Resources.Calls.Count);
        Assert.Equal(acceptedAttempts, fixture.Groups.Added.Count);
        Assert.Single(fixture.Registry.GetForTarget(fixture.Authorization.TenantId, RealtimeSubscriptionType.Workspace, workspace));
        Assert.False(fixture.Caller.Aborted);
    }

    [Theory]
    [InlineData(RealtimeSubscriptionType.Workspace, "workspace")]
    [InlineData(RealtimeSubscriptionType.Conversation, "conversation")]
    [InlineData(RealtimeSubscriptionType.Project, "project")]
    public async Task UnsubscribeRemovesOnlyCallingConnectionAndRetainsAuthorizedPeer(RealtimeSubscriptionType type, string prefix)
    {
        using var fixture = new HubFixture();
        var resource = Guid.NewGuid();
        Assert.True((await SubscribeAsync(fixture.Hub, type, resource)).Allowed);
        var own = Assert.Single(fixture.Registry.GetForTarget(fixture.Authorization.TenantId, type, resource));
        Assert.True(fixture.Registry.TryAdd(own with { ConnectionId = "synthetic-peer" }, 32));

        Assert.Equal(new HubSubscriptionResult(true, "Unsubscribed"), await UnsubscribeAsync(fixture.Hub, type, resource));

        Assert.Equal("synthetic-peer", Assert.Single(fixture.Registry.GetForTarget(fixture.Authorization.TenantId, type, resource)).ConnectionId);
        Assert.Equal(new GroupCall(fixture.Caller.ConnectionId, $"{prefix}:{resource:D}", fixture.Caller.ConnectionAborted), Assert.Single(fixture.Groups.Removed));
        Assert.Equal(new HubSubscriptionResult(true, "NotSubscribed"), await UnsubscribeAsync(fixture.Hub, type, resource));
        Assert.Equal(new HubSubscriptionResult(true, "NotSubscribed"), await UnsubscribeAsync(fixture.Hub, type, Guid.Empty));
        Assert.Single(fixture.Groups.Removed);
    }

    [Fact]
    public async Task DisconnectRemovesSubscriptionsAndAttemptBudgetForOnlyThatConnection()
    {
        using var fixture = new HubFixture();
        fixture.Options.SubscriptionAttemptsPerMinute = 1;
        Assert.True((await fixture.Hub.SubscribeUser()).Allowed);
        var own = Assert.Single(fixture.Registry.GetForTarget(fixture.Authorization.TenantId, RealtimeSubscriptionType.User, fixture.Authorization.UserId));
        Assert.True(fixture.Registry.TryAdd(own with { ConnectionId = "synthetic-peer" }, 32));
        Assert.False(fixture.Registry.TryRecordAttempt(fixture.Caller.ConnectionId, DateTimeOffset.UtcNow, 1));

        await fixture.Hub.OnDisconnectedAsync(new IOException("Synthetic disconnect."));

        Assert.Equal("synthetic-peer", Assert.Single(fixture.Registry.GetForTarget(fixture.Authorization.TenantId, RealtimeSubscriptionType.User, fixture.Authorization.UserId)).ConnectionId);
        Assert.True(fixture.Registry.TryRecordAttempt(fixture.Caller.ConnectionId, DateTimeOffset.UtcNow, 1));
    }

    private static Task<HubSubscriptionResult> SubscribeAsync(AppHub hub, RealtimeSubscriptionType type, Guid resource) => type switch
    {
        RealtimeSubscriptionType.Workspace => hub.SubscribeWorkspace(resource),
        RealtimeSubscriptionType.Conversation => hub.SubscribeConversation(resource),
        RealtimeSubscriptionType.Project => hub.SubscribeProject(resource),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static Task<HubSubscriptionResult> UnsubscribeAsync(AppHub hub, RealtimeSubscriptionType type, Guid resource) => type switch
    {
        RealtimeSubscriptionType.Workspace => hub.UnsubscribeWorkspace(resource),
        RealtimeSubscriptionType.Conversation => hub.UnsubscribeConversation(resource),
        RealtimeSubscriptionType.Project => hub.UnsubscribeProject(resource),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private sealed class HubFixture : IDisposable
    {
        private readonly ServiceProvider _services;
        public SubscriptionAuthorizationFixture Authorization { get; } = new();
        public RequestTenant RequestTenant { get; }
        public DefaultHttpContext Http { get; } = new();
        public TestHubCaller Caller { get; }
        public RecordingGroups Groups { get; } = new();
        public HubSubscriptionRegistry Registry { get; } = new();
        public RealtimeDiagnostics Diagnostics { get; } = new();
        public RealtimeOptions Options { get; } = new();
        public AppHub Hub { get; }

        public HubFixture()
        {
            RequestTenant = new RequestTenant { TenantId = Authorization.TenantId };
            _services = new ServiceCollection().AddSingleton<ICurrentTenant>(RequestTenant).BuildServiceProvider();
            Http.RequestServices = _services;
            Caller = new TestHubCaller(Authorization.AuthenticatedPrincipal);
            Caller.Features.Set<IHttpContextFeature>(new TestHttpContextFeature { HttpContext = Http });
            Hub = new AppHub(Authorization.Authorizer, Registry, Diagnostics,
                Microsoft.Extensions.Options.Options.Create(Options), Authorization.Tenant, NullLogger<AppHub>.Instance)
            { Context = Caller, Groups = Groups };
        }

        public void Dispose()
        {
            Hub.Dispose();
            Caller.Dispose();
            _services.Dispose();
        }
    }

    private sealed class RequestTenant : ICurrentTenant
    {
        public Guid TenantId { get; init; }
        public bool IsAvailable { get; set; } = true;
        public string? TenantSlug { get; set; } = "synthetic";
        public bool IsPlatformScope => false;
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class TestHttpContextFeature : IHttpContextFeature
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class TestHubCaller(ClaimsPrincipal principal) : HubCallerContext, IDisposable
    {
        private readonly CancellationTokenSource _connection = new();
        public ClaimsPrincipal Principal { get; set; } = principal;
        public bool Aborted { get; private set; }
        public override string ConnectionId => "synthetic-connection";
        public override string? UserIdentifier => "untrusted-alternate-user-identifier";
        public override ClaimsPrincipal? User => Principal;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => _connection.Token;
        public override void Abort() => Aborted = true;
        public void Dispose() => _connection.Dispose();
    }

    private sealed record GroupCall(string Connection, string Group, CancellationToken Token);

    private sealed class RecordingGroups : IGroupManager
    {
        public List<GroupCall> Added { get; } = [];
        public List<GroupCall> Removed { get; } = [];
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Added.Add(new GroupCall(connectionId, groupName, cancellationToken));
            return Task.CompletedTask;
        }
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Removed.Add(new GroupCall(connectionId, groupName, cancellationToken));
            return Task.CompletedTask;
        }
    }
}
