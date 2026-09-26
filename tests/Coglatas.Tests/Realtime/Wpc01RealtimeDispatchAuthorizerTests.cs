using System.Text.Json;
using Coglatas.Application.Auth;
using Coglatas.Application.Common;
using Coglatas.Application.Messaging;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Enums;
using Coglatas.Web.Realtime;

namespace Coglatas.Tests.Realtime;

[Trait("Scope", "WPC01")]
public sealed class Wpc01RealtimeDispatchAuthorizerTests
{
    [Fact]
    public async Task PlanningProjectUnreadEventRequiresCurrentExplicitProjectMembership()
    {
        var fixture = CreateFixture(ProjectStatus.Planning, hasExplicitProjectMembership: true);
        var envelope = fixture.UnreadEnvelope();

        Assert.True(await fixture.Authorizer.CanReceiveAsync(
            fixture.Subscription,
            RealtimeSubscriptionType.User,
            fixture.UserId,
            envelope));

        fixture.Conversations.HasExplicitProjectMembership = false;

        Assert.False(await fixture.Authorizer.CanReceiveAsync(
            fixture.Subscription,
            RealtimeSubscriptionType.User,
            fixture.UserId,
            envelope));
    }

    [Theory]
    [InlineData(ProjectStatus.Suspended)]
    [InlineData(ProjectStatus.Archived)]
    public async Task PreviouslyStagedUnreadEventIsDeniedAfterProjectLosesBroadVisibility(ProjectStatus currentStatus)
    {
        var fixture = CreateFixture(ProjectStatus.Active, hasExplicitProjectMembership: false);
        var envelope = fixture.UnreadEnvelope();

        Assert.True(await fixture.Authorizer.CanReceiveAsync(
            fixture.Subscription,
            RealtimeSubscriptionType.User,
            fixture.UserId,
            envelope));

        fixture.Conversations.ProjectStatus = currentStatus;

        Assert.False(await fixture.Authorizer.CanReceiveAsync(
            fixture.Subscription,
            RealtimeSubscriptionType.User,
            fixture.UserId,
            envelope));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"conversationId\":null}")]
    [InlineData("{\"conversationId\":\"not-a-guid\"}")]
    [InlineData("{\"conversationId\":\"00000000-0000-0000-0000-000000000000\"}")]
    public async Task UnreadEventWithInvalidConversationIdentityFailsClosed(string payloadJson)
    {
        var fixture = CreateFixture(ProjectStatus.Active, hasExplicitProjectMembership: true);
        using var document = JsonDocument.Parse(payloadJson);
        var envelope = fixture.UnreadEnvelope() with { Payload = document.RootElement.Clone() };

        Assert.False(await fixture.Authorizer.CanReceiveAsync(
            fixture.Subscription,
            RealtimeSubscriptionType.User,
            fixture.UserId,
            envelope));
    }

    private static Fixture CreateFixture(ProjectStatus status, bool hasExplicitProjectMembership)
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var conversations = new CurrentConversationAuthorization(conversationId, status, hasExplicitProjectMembership);
        var authorizer = new RealtimeDispatchAuthorizer(
            new ValidSessionService(),
            null!,
            conversations,
            null!,
            null!,
            null!,
            null!,
            null!);
        var subscription = new HubSubscription(
            "wpc01-connection",
            userId,
            sessionId,
            tenantId,
            RealtimeSubscriptionType.User,
            userId,
            $"user:{userId:D}",
            DateTimeOffset.UtcNow);
        return new Fixture(authorizer, conversations, subscription, tenantId, userId, conversationId);
    }

    private sealed record Fixture(
        RealtimeDispatchAuthorizer Authorizer,
        CurrentConversationAuthorization Conversations,
        HubSubscription Subscription,
        Guid TenantId,
        Guid UserId,
        Guid ConversationId)
    {
        public DurableEventEnvelope UnreadEnvelope() => new(
            Guid.NewGuid(),
            "Messaging.ConversationUnreadChanged.v1",
            RealtimeEventCatalog.PayloadSchemaVersion1,
            DateTimeOffset.UtcNow,
            TenantId,
            "ConversationReadState",
            Guid.NewGuid(),
            1,
            RealtimeActor.System(),
            null,
            null,
            JsonSerializer.SerializeToElement(new { conversationId = ConversationId, unreadCount = 1 }));
    }

    private sealed class CurrentConversationAuthorization(
        Guid conversationId,
        ProjectStatus projectStatus,
        bool hasExplicitProjectMembership) : IConversationAuthorizationService
    {
        public ProjectStatus ProjectStatus { get; set; } = projectStatus;
        public bool HasExplicitProjectMembership { get; set; } = hasExplicitProjectMembership;

        public Task<bool> CanViewConversation(Guid userId, Guid candidateConversationId, CancellationToken cancellationToken = default)
        {
            var allowed = candidateConversationId == conversationId && ProjectStatus switch
            {
                ProjectStatus.Archived or ProjectStatus.Deleted => false,
                ProjectStatus.Planning or ProjectStatus.Suspended => HasExplicitProjectMembership,
                _ => true
            };
            return Task.FromResult(allowed);
        }

        public Task<bool> CanSendMessage(Guid userId, Guid candidateConversationId, CancellationToken cancellationToken = default) =>
            CanViewConversation(userId, candidateConversationId, cancellationToken);

        public Task<bool> CanManageConversation(Guid userId, Guid candidateConversationId, CancellationToken cancellationToken = default) =>
            CanViewConversation(userId, candidateConversationId, cancellationToken);

        public Task<bool> CanModerateConversation(Guid userId, Guid candidateConversationId, CancellationToken cancellationToken = default) =>
            CanViewConversation(userId, candidateConversationId, cancellationToken);

        public Task<bool> CanCreateThread(Guid userId, Guid parentConversationId, CancellationToken cancellationToken = default) =>
            CanViewConversation(userId, parentConversationId, cancellationToken);

        public Task<bool> CanEditMessage(Guid userId, Guid messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> CanDeleteMessage(Guid userId, Guid messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class ValidSessionService : IUserSessionService
    {
        public Task<SessionValidationResult> ValidateSessionAsync(
            Guid userId,
            Guid sessionId,
            Guid? tenantId,
            bool requireActiveTenantMembership,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SessionValidationResult.Success());

        public Task<Result> RevokeSessionAsync(
            Guid sessionId,
            Guid? actorUserId,
            string reason,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());

        public Task<Result<int>> RevokeUserSessionsAsync(
            Guid userId,
            Guid? actorUserId,
            string reason,
            Guid? exceptSessionId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<int>.Success(0));
    }
}
