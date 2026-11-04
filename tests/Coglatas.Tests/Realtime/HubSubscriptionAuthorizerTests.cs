using System.Security.Claims;
using Coglatas.Application.Auth;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Messaging;
using Coglatas.Application.Projects;
using Coglatas.Application.Realtime;
using Coglatas.Application.Workspaces;
using Coglatas.Web.Realtime;

namespace Coglatas.Tests.Realtime;

// Direct production-authorizer controls with synthetic dependencies; persisted authority is qualified separately.
public sealed class HubSubscriptionAuthorizerTests
{
    [Theory]
    [InlineData("missing-principal")]
    [InlineData("anonymous")]
    [InlineData("missing-user")]
    [InlineData("invalid-user")]
    [InlineData("missing-session")]
    [InlineData("invalid-session")]
    [InlineData("missing-tenant")]
    public async Task InvalidConnectionIdentityNeverReachesFeaturesOrSessionValidation(string invalid)
    {
        var fixture = new SubscriptionAuthorizationFixture();
        var user = invalid == "missing-user" ? null : invalid == "invalid-user" ? "invalid" : fixture.UserId.ToString();
        var session = invalid == "missing-session" ? null : invalid == "invalid-session" ? "invalid" : fixture.SessionId.ToString();
        var principal = invalid == "missing-principal" ? null : SubscriptionAuthorizationFixture.Principal(user, session, invalid != "anonymous");
        if (invalid == "missing-tenant") { fixture.Tenant.Clear(); }

        Assert.Null(await fixture.Authorizer.ValidateConnectionAsync(principal));

        Assert.Empty(fixture.Features.Calls);
        Assert.Empty(fixture.Sessions.Calls);
        Assert.Empty(fixture.Resources.Calls);
    }

    [Fact]
    public async Task DisabledSignalRStopsBeforeSessionAccess()
    {
        var fixture = new SubscriptionAuthorizationFixture { Features = { SignalREnabled = false } };
        using var cancellation = new CancellationTokenSource();

        Assert.Null(await fixture.Authorizer.ValidateConnectionAsync(fixture.AuthenticatedPrincipal, cancellation.Token));

        Assert.Equal((FeatureKeys.RealtimeSignalR, cancellation.Token), Assert.Single(fixture.Features.Calls));
        Assert.Empty(fixture.Sessions.Calls);
    }

    [Fact]
    public async Task ConnectionValidationUsesExactClaimedSessionAndCurrentTenantOnEveryCall()
    {
        var fixture = new SubscriptionAuthorizationFixture();
        using var cancellation = new CancellationTokenSource();

        var authorized = await fixture.Authorizer.ValidateConnectionAsync(fixture.AuthenticatedPrincipal, cancellation.Token);
        fixture.Sessions.Valid = false;
        var revoked = await fixture.Authorizer.ValidateConnectionAsync(fixture.AuthenticatedPrincipal, cancellation.Token);

        Assert.Equal(fixture.Context, authorized);
        Assert.Null(revoked);
        Assert.Equal(2, fixture.Sessions.Calls.Count);
        Assert.All(fixture.Sessions.Calls, call => Assert.Equal(
            new SessionCall(fixture.UserId, fixture.SessionId, fixture.TenantId, true, cancellation.Token), call));
        Assert.Empty(fixture.Resources.Calls);
    }

    [Theory]
    [InlineData("empty-resource")]
    [InlineData("unavailable-tenant")]
    [InlineData("foreign-tenant")]
    public async Task InvalidSubscriptionScopeCannotQueryAuthority(string invalid)
    {
        var fixture = new SubscriptionAuthorizationFixture();
        var resource = invalid == "empty-resource" ? Guid.Empty : Guid.NewGuid();
        var context = fixture.Context;
        if (invalid == "unavailable-tenant") { fixture.Tenant.Clear(); }
        if (invalid == "foreign-tenant") { context = context with { TenantId = Guid.NewGuid() }; }

        Assert.False(await fixture.Authorizer.CanSubscribeAsync(context, RealtimeSubscriptionType.Project, resource));

        Assert.Empty(fixture.Features.Calls);
        Assert.Empty(fixture.Sessions.Calls);
        Assert.Empty(fixture.Resources.Calls);
    }

    [Fact]
    public async Task DisabledAuthorizedGroupsStopsBeforeSessionAndResourceQueries()
    {
        var fixture = new SubscriptionAuthorizationFixture { Features = { GroupsEnabled = false } };
        using var cancellation = new CancellationTokenSource();

        Assert.False(await fixture.Authorizer.CanSubscribeAsync(fixture.Context, RealtimeSubscriptionType.Workspace, Guid.NewGuid(), cancellation.Token));

        Assert.Equal((FeatureKeys.AuthorizedRealtimeGroups, cancellation.Token), Assert.Single(fixture.Features.Calls));
        Assert.Empty(fixture.Sessions.Calls);
        Assert.Empty(fixture.Resources.Calls);
    }

    [Theory]
    [InlineData(RealtimeSubscriptionType.Workspace, true)]
    [InlineData(RealtimeSubscriptionType.Workspace, false)]
    [InlineData(RealtimeSubscriptionType.Conversation, true)]
    [InlineData(RealtimeSubscriptionType.Conversation, false)]
    [InlineData(RealtimeSubscriptionType.Project, true)]
    [InlineData(RealtimeSubscriptionType.Project, false)]
    public async Task ResourceDecisionUsesOnlyTheCurrentReadAuthorizerWithExactArguments(RealtimeSubscriptionType type, bool allowed)
    {
        var fixture = new SubscriptionAuthorizationFixture { Resources = { Allowed = allowed } };
        var resource = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();

        Assert.Equal(allowed, await fixture.Authorizer.CanSubscribeAsync(fixture.Context, type, resource, cancellation.Token));

        Assert.Equal(new ResourceCall(type, fixture.UserId, resource, cancellation.Token), Assert.Single(fixture.Resources.Calls));
        Assert.Equal(new SessionCall(fixture.UserId, fixture.SessionId, fixture.TenantId, true, cancellation.Token), Assert.Single(fixture.Sessions.Calls));
        Assert.Equal((FeatureKeys.AuthorizedRealtimeGroups, cancellation.Token), Assert.Single(fixture.Features.Calls));
    }

    [Theory]
    [InlineData(RealtimeSubscriptionType.User)]
    [InlineData(RealtimeSubscriptionType.Tenant)]
    public async Task IdentitySubscriptionsRejectAnotherResourceDespiteAValidSession(RealtimeSubscriptionType type)
    {
        var fixture = new SubscriptionAuthorizationFixture();
        var ownResource = type == RealtimeSubscriptionType.User ? fixture.UserId : fixture.TenantId;

        Assert.True(await fixture.Authorizer.CanSubscribeAsync(fixture.Context, type, ownResource));
        Assert.False(await fixture.Authorizer.CanSubscribeAsync(fixture.Context, type, Guid.NewGuid()));

        Assert.Equal(2, fixture.Sessions.Calls.Count);
        Assert.Empty(fixture.Resources.Calls);
    }

    [Fact]
    public async Task RevokedSessionPreventsAnotherResourceDecisionAndUnknownTypesRemainDenied()
    {
        var fixture = new SubscriptionAuthorizationFixture();
        var resource = Guid.NewGuid();
        Assert.True(await fixture.Authorizer.CanSubscribeAsync(fixture.Context, RealtimeSubscriptionType.Workspace, resource));
        fixture.Sessions.Valid = false;

        Assert.False(await fixture.Authorizer.CanSubscribeAsync(fixture.Context, RealtimeSubscriptionType.Workspace, resource));
        Assert.Single(fixture.Resources.Calls);
        fixture.Sessions.Valid = true;
        Assert.False(await fixture.Authorizer.CanSubscribeAsync(fixture.Context, (RealtimeSubscriptionType)999, resource));
        Assert.Equal(3, fixture.Sessions.Calls.Count);
        Assert.Single(fixture.Resources.Calls);
    }

    [Fact]
    public async Task SessionCancellationPropagatesWithoutAResourceDecision()
    {
        var fixture = new SubscriptionAuthorizationFixture();
        using var cancellation = new CancellationTokenSource();
        var expected = new OperationCanceledException(cancellation.Token);
        fixture.Sessions.Failure = expected;

        var actual = await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Authorizer.CanSubscribeAsync(
            fixture.Context, RealtimeSubscriptionType.Conversation, Guid.NewGuid(), cancellation.Token));

        Assert.Same(expected, actual);
        Assert.Equal(cancellation.Token, Assert.Single(fixture.Sessions.Calls).Token);
        Assert.Empty(fixture.Resources.Calls);
    }
}

internal sealed class SubscriptionAuthorizationFixture
{
    public Guid UserId { get; } = Guid.NewGuid();
    public Guid SessionId { get; } = Guid.NewGuid();
    public Guid TenantId { get; } = Guid.NewGuid();
    public CurrentTenantService Tenant { get; } = new();
    public RecordingSubscriptionSessions Sessions { get; } = new();
    public RecordingSubscriptionFeatures Features { get; } = new();
    public RecordingSubscriptionResources Resources { get; } = new();
    public HubSubscriptionAuthorizer Authorizer { get; }
    public HubAuthorizationContext Context => new(UserId, SessionId, TenantId);
    public ClaimsPrincipal AuthenticatedPrincipal => Principal(UserId.ToString(), SessionId.ToString());

    public SubscriptionAuthorizationFixture()
    {
        Tenant.SetTenant(TenantId, "synthetic");
        Authorizer = new HubSubscriptionAuthorizer(Sessions, Tenant, Features, Resources, Resources, Resources);
    }

    public static ClaimsPrincipal Principal(string? user, string? session, bool authenticated = true)
    {
        var claims = new List<Claim>();
        if (user is not null) { claims.Add(new Claim(ClaimTypes.NameIdentifier, user)); }
        if (session is not null) { claims.Add(new Claim("session_id", session)); }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "Synthetic" : null));
    }
}

internal sealed record SessionCall(Guid UserId, Guid SessionId, Guid? TenantId, bool RequireMembership, CancellationToken Token);
internal sealed record ResourceCall(RealtimeSubscriptionType Type, Guid UserId, Guid ResourceId, CancellationToken Token);

internal sealed class RecordingSubscriptionSessions : IUserSessionService
{
    public List<SessionCall> Calls { get; } = [];
    public bool Valid { get; set; } = true;
    public Exception? Failure { get; set; }
    public Action? AfterValidation { get; set; }
    public Task<SessionValidationResult> ValidateSessionAsync(Guid userId, Guid sessionId, Guid? tenantId, bool requireActiveTenantMembership, CancellationToken cancellationToken = default)
    {
        Calls.Add(new SessionCall(userId, sessionId, tenantId, requireActiveTenantMembership, cancellationToken));
        if (Failure is { } error) { return Task.FromException<SessionValidationResult>(error); }
        var result = Valid ? SessionValidationResult.Success() : SessionValidationResult.Failure("Synthetic revoked session.");
        AfterValidation?.Invoke();
        return Task.FromResult(result);
    }
    public Task<Result> RevokeSessionAsync(Guid sessionId, Guid? actorUserId, string reason, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Subscription must not mutate sessions.");
    public Task<Result<int>> RevokeUserSessionsAsync(Guid userId, Guid? actorUserId, string reason, Guid? exceptSessionId = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Subscription must not mutate sessions.");
}

internal sealed class RecordingSubscriptionFeatures : IFeatureFlagService
{
    public List<(string Key, CancellationToken Token)> Calls { get; } = [];
    public bool SignalREnabled { get; set; } = true;
    public bool GroupsEnabled { get; set; } = true;
    public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default)
    {
        Calls.Add((key, cancellationToken));
        return Task.FromResult(key switch
        {
            FeatureKeys.RealtimeSignalR => SignalREnabled,
            FeatureKeys.AuthorizedRealtimeGroups => GroupsEnabled,
            _ => throw new InvalidOperationException("Unexpected subscription feature.")
        });
    }
    public Task<Result> RequireEnabledAsync(string key, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected feature mutation guard.");
    public Task<IReadOnlyList<string>> GetEnabledFeaturesAsync(Guid tenantId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected feature enumeration.");
}

internal sealed class RecordingSubscriptionResources : IWorkspaceAuthorizationService, IConversationAuthorizationService, IProjectAuthorizationService
{
    public List<ResourceCall> Calls { get; } = [];
    public bool Allowed { get; set; } = true;
    private Task<bool> ReadAsync(RealtimeSubscriptionType type, Guid userId, Guid resourceId, CancellationToken token)
    {
        Calls.Add(new ResourceCall(type, userId, resourceId, token));
        return Task.FromResult(Allowed);
    }
    public Task<bool> CanViewWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default) => ReadAsync(RealtimeSubscriptionType.Workspace, userId, workspaceId, cancellationToken);
    public Task<bool> CanViewConversation(Guid userId, Guid conversationId, CancellationToken cancellationToken = default) => ReadAsync(RealtimeSubscriptionType.Conversation, userId, conversationId, cancellationToken);
    public Task<bool> CanViewProject(Guid userId, Guid projectId, CancellationToken cancellationToken = default) => ReadAsync(RealtimeSubscriptionType.Project, userId, projectId, cancellationToken);
    public Task<bool> CanContributeWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default) => throw Unexpected();
    public Task<bool> CanManageWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default) => throw Unexpected();
    public Task<bool> CanCreateWorkspace(Guid userId, Guid tenantId, CancellationToken cancellationToken = default) => throw Unexpected();
    public Task<bool> CanSendMessage(Guid userId, Guid conversationId, CancellationToken cancellationToken = default) => throw Unexpected();
    public Task<bool> CanManageConversation(Guid userId, Guid conversationId, CancellationToken cancellationToken = default) => throw Unexpected();
    public Task<bool> CanModerateConversation(Guid userId, Guid conversationId, CancellationToken cancellationToken = default) => throw Unexpected();
    public Task<bool> CanCreateThread(Guid userId, Guid parentConversationId, CancellationToken cancellationToken = default) => throw Unexpected();
    public Task<bool> CanEditMessage(Guid userId, Guid messageId, CancellationToken cancellationToken = default) => throw Unexpected();
    public Task<bool> CanDeleteMessage(Guid userId, Guid messageId, CancellationToken cancellationToken = default) => throw Unexpected();
    public Task<bool> CanManageProject(Guid userId, Guid projectId, CancellationToken cancellationToken = default) => throw Unexpected();
    public Task<bool> CanCreateProject(Guid userId, Guid workspaceId, Guid groupId, CancellationToken cancellationToken = default) => throw Unexpected();
    private static InvalidOperationException Unexpected() => new("Subscription must not substitute mutation authority for resource read authority.");
}
