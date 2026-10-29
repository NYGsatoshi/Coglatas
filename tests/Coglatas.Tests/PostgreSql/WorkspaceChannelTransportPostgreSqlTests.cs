using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.SecurityArchitecture;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed class WorkspaceChannelTransportPostgreSqlTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task CanonicalWorkspaceChannelDeliversToCurrentPeersAndReauthorizesReconnectAndReplay()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var ownerClient = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAOwnerEmail);
            using var memberClient = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
            var scope = await CreateCanonicalScopeAsync(ownerClient, database);
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                // Initialization invalidations must settle before the new subscriptions.
                var initialization = await db.OutboxEvents.Where(row => row.EventType == "Security.AuthorizationStateChanged.v1")
                    .Select(row => row.Id).ToListAsync();
                foreach (var id in initialization) await app.WaitDeliveredAsync(id);
            }

            await using var owner = await app.ConnectAsync(ownerClient, "owner", SecurityCiFixtureSeed.TenantASlug);
            await SubscribeAsync(owner, scope.Conversation);
            Guid rootMessage;
            Guid memberUnread;
            Guid ownerUnread;
            Guid memberMention;
            Guid ownerMention;
            await using (var member = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug))
            {
                await SubscribeAsync(member, scope.Conversation);
                var root = await PostMessageAsync(ownerClient, database, scope, SecurityCiFixtureSeed.TenantAMemberUserId);
                rootMessage = root.Message;
                await AssertDeliveredAsync(app, root.Event, "Messaging.MessageCreated.v1", owner, member);
                memberMention = await MentionEventAsync(database, root.Message, SecurityCiFixtureSeed.TenantAMemberUserId);
                await AssertRecipientAsync(app, memberMention, "Notifications.NotificationCreated.v1", member, owner);

                var reply = await PostReplyAsync(memberClient, database, root.Message, SecurityCiFixtureSeed.TenantAOwnerUserId);
                await AssertDeliveredAsync(app, reply.Event, "Messaging.MessageCreated.v1", owner, member);
                var changed = await EventAsync(database, "Messaging.ThreadChanged.v1", root.Message);
                await AssertDeliveredAsync(app, changed, "Messaging.ThreadChanged.v1", owner, member);
                ownerMention = await MentionEventAsync(database, reply.Message, SecurityCiFixtureSeed.TenantAOwnerUserId);
                await AssertRecipientAsync(app, ownerMention, "Notifications.NotificationCreated.v1", owner, member);

                // Unread is the current actor's read-state change, not a send broadcast.
                memberUnread = await MarkReadAsync(memberClient, database, scope, root.Message, SecurityCiFixtureSeed.TenantAMemberUserId);
                ownerUnread = await MarkReadAsync(ownerClient, database, scope, reply.Message, SecurityCiFixtureSeed.TenantAOwnerUserId);
                await AssertRecipientAsync(app, memberUnread, "Messaging.ConversationUnreadChanged.v1", member, owner);
                await AssertRecipientAsync(app, ownerUnread, "Messaging.ConversationUnreadChanged.v1", owner, member);
            }

            var missed = await PostMessageAsync(ownerClient, database, scope);
            await AssertDeliveredAsync(app, missed.Event, "Messaging.MessageCreated.v1", owner);
            await using var reconnected = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug);
            await SubscribeAsync(reconnected, scope.Conversation);
            await AssertCatchUpAsync(memberClient, scope, rootMessage, missed.Message);
            var connected = await PostMessageAsync(ownerClient, database, scope);
            await AssertDeliveredAsync(app, connected.Event, "Messaging.MessageCreated.v1", owner, reconnected);

            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                Assert.Equal(1, await db.WorkspaceMembers.Where(row => row.WorkspaceId == scope.Workspace && row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, MembershipStatus.Suspended)));
                var participant = await db.ConversationMembers.AsNoTracking().SingleAsync(row => row.ConversationId == scope.Conversation && row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId);
                Assert.Null(participant.LeftAt);
                Assert.Null(participant.RemovedAt);
                Assert.True(participant.CanRead);
                Assert.True(participant.CanPost);
                Assert.True(await db.TenantUsers.AnyAsync(row => row.TenantId == scope.Tenant && row.UserId == participant.UserId && row.Status == TenantUserStatus.Active));
                Assert.True(await db.Users.AnyAsync(row => row.Id == participant.UserId && row.Status == UserStatus.Active && row.SystemRole == SystemRole.User && row.DeletedAt == null));
            }
            await AssertSubscriptionDeniedAsync(reconnected, scope.Conversation);
            await AssertBodyDeniedAsync(memberClient, scope, rootMessage);
            var revoked = await PostMessageAsync(ownerClient, database, scope, SecurityCiFixtureSeed.TenantAMemberUserId);
            await AssertDeliveredAsync(app, revoked.Event, "Messaging.MessageCreated.v1", owner);
            await AssertExcludedAsync(reconnected, revoked.Event);
            var deniedMention = await MentionEventAsync(database, revoked.Message, SecurityCiFixtureSeed.TenantAMemberUserId);
            await app.WaitDeliveredAsync(deniedMention);
            await AssertExcludedAsync(reconnected, deniedMention);
            var revokedReply = await PostReplyAsync(ownerClient, database, rootMessage);
            await AssertDeliveredAsync(app, revokedReply.Event, "Messaging.MessageCreated.v1", owner);
            await AssertExcludedAsync(reconnected, revokedReply.Event);
            var latestThread = await LatestEventAsync(database, "Messaging.ThreadChanged.v1", rootMessage);
            await AssertDeliveredAsync(app, latestThread, "Messaging.ThreadChanged.v1", owner);
            await AssertExcludedAsync(reconnected, latestThread);

            // Replay existing business-produced user events through the real dispatcher.
            // This does not qualify the separate operator replay capability API.
            Assert.True(await owner.SubscribeAsync("SubscribeUser"));
            var unreadBeforeReplay = owner.DeliveryCount(ownerUnread);
            var mentionBeforeReplay = owner.DeliveryCount(ownerMention);
            var replayStarted = DateTimeOffset.UtcNow;
            var replayIds = new[] { memberUnread, ownerUnread, memberMention, ownerMention };
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                var repository = new OutboxEventRepository(db);
                foreach (var id in replayIds) Assert.True(await repository.ReplayAsync(id, replayStarted));
                await db.SaveChangesAsync();
            }
            foreach (var id in replayIds) await app.WaitDeliveredAsync(id, replayStarted);
            await owner.WaitEventAsync(ownerUnread, minimumCount: unreadBeforeReplay + 1);
            await owner.WaitEventAsync(ownerMention, minimumCount: mentionBeforeReplay + 1);
            await AssertExcludedAsync(reconnected, memberUnread, memberMention, ownerUnread, ownerMention);

            await using var deniedReconnect = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug);
            await AssertSubscriptionDeniedAsync(deniedReconnect, scope.Conversation);
            await AssertBodyDeniedAsync(memberClient, scope, rootMessage);
            await AssertCatchUpAsync(ownerClient, scope, rootMessage, revoked.Message);
        });
    }

    /// <summary>
    /// SEC-17: Workspace admission alone is not private-channel subscription
    /// authority. Uses real HTTP, migrated PostgreSQL, the real Hub/dispatcher
    /// and actual WebSocket receivers; only synthetic fixture actors and data.
    /// </summary>
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "SEC17")]
    public async Task PrivateChannelInvitationAndWorkspaceRevocationIsolateRealSignalRDelivery()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var ownerClient = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug,
                SecurityCiFixtureSeed.TenantAOwnerEmail);
            using var memberClient = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug,
                SecurityCiFixtureSeed.TenantAMemberEmail);
            using var betaClient = await app.LoginAsync("beta", SecurityCiFixtureSeed.TenantBSlug,
                SecurityCiFixtureSeed.TenantBOwnerEmail);
            Guid workspace;
            Guid tenant;
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                var original = await db.Workspaces.AsNoTracking().SingleAsync(
                    row => row.Slug == SecurityCiFixtureSeed.TenantAWorkspaceSlug);
                workspace = original.Id;
                tenant = original.TenantId;
                Assert.True(await db.WorkspaceMembers.AsNoTracking().AnyAsync(
                    row => row.WorkspaceId == workspace && row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId &&
                           row.Status == MembershipStatus.Active));
            }

            var route = $"/api/workspaces/{workspace:D}/chat-channels";
            Guid privateId;
            using (var request = new HttpRequestMessage(HttpMethod.Post, route))
            {
                request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
                request.Content = JsonContent.Create(new
                {
                    name = "SEC17 synthetic private real-transport " + Guid.NewGuid().ToString("N"),
                    visibility = "Private"
                });
                using var created = await ownerClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, created.StatusCode);
                using var payload = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
                privateId = payload.RootElement.GetProperty("id").GetGuid();
                Assert.Equal("Private", payload.RootElement.GetProperty("visibility").GetString());
                Assert.True(payload.RootElement.GetProperty("isMember").GetBoolean());
            }
            var scope = new ChannelScope(tenant, workspace, privateId);
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                Assert.False(await db.ConversationMembers.AsNoTracking().AnyAsync(row =>
                    row.ConversationId == privateId && row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId));
                Assert.Equal(ConversationVisibility.Private, await db.Conversations.AsNoTracking()
                    .Where(row => row.Id == privateId).Select(row => row.Visibility).SingleAsync());
            }

            // New channel creation may enqueue a user authorization invalidation;
            // settle it before adding live subscriptions to avoid stale groups.
            await SettleAuthorizationEventsAsync(app, database);
            await using var owner = await app.ConnectAsync(ownerClient, "owner", SecurityCiFixtureSeed.TenantASlug);
            await using var member = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug);
            await using var beta = await app.ConnectAsync(betaClient, "beta", SecurityCiFixtureSeed.TenantBSlug);
            var controls = SecurityArchitectureSignalRControlRecorder.Create(GetType());
            Assert.True(await owner.SubscribeAsync("SubscribeConversation", privateId));
            Assert.True(await member.SubscribeAsync("SubscribeWorkspace", workspace));
            var uninvited = await member.InvokeAsync("SubscribeConversation", privateId);
            controls.ObserveInvocation("SubscribeConversation", uninvited, expectedAllowed: false, "AccessDenied");
            var foreign = await beta.InvokeAsync("SubscribeConversation", privateId);
            controls.ObserveInvocation("SubscribeConversation", foreign, expectedAllowed: false, "AccessDenied");
            using (var deniedHistory = await memberClient.GetAsync($"/api/conversations/{privateId:D}/messages"))
                Assert.Equal(HttpStatusCode.BadRequest, deniedHistory.StatusCode);

            // Business-produced event with a receiving authorized owner and two
            // connected excluded peers: no zero-event false-green acceptance.
            var before = await PostMessageAsync(ownerClient, database, scope);
            await AssertDeliveredAsync(app, before.Event, "Messaging.MessageCreated.v1", owner);
            await AssertExcludedAsync(member, before.Event);
            await AssertExcludedAsync(beta, before.Event);
            controls.ObserveIsolation("Messaging.MessageCreated.v1", RealtimeSubscriptionType.Conversation,
                "SAME_WORKSPACE_PRIVATE_UNINVITED", owner, before.Event, member, before.Event);
            controls.ObserveIsolation("Messaging.MessageCreated.v1", RealtimeSubscriptionType.Conversation,
                "CROSS_TENANT_PRIVATE_UNINVITED", owner, before.Event, beta, before.Event);

            // A real scoped HTTP command, not a manual addition to conversation_members.
            using (var invited = await ownerClient.PostAsJsonAsync(
                $"{route}/{privateId:D}/members",
                new { userId = SecurityCiFixtureSeed.TenantAMemberUserId }))
                Assert.Equal(HttpStatusCode.OK, invited.StatusCode);
            await SettleAuthorizationEventsAsync(app, database);
            Assert.True(await member.SubscribeAsync("SubscribeConversation", privateId));
            Assert.True(await owner.SubscribeAsync("SubscribeConversation", privateId));
            using (var history = await memberClient.GetAsync($"/api/conversations/{privateId:D}/messages"))
                Assert.Equal(HttpStatusCode.OK, history.StatusCode);
            var admitted = await PostMessageAsync(ownerClient, database, scope);
            await AssertDeliveredAsync(app, admitted.Event, "Messaging.MessageCreated.v1", owner, member);
            await AssertExcludedAsync(beta, admitted.Event);
            controls.ObservePositive("Messaging.MessageCreated.v1", RealtimeSubscriptionType.Conversation,
                "CURRENT_PRIVATE_INVITATION_DELIVERY", member, admitted.Event);

            // Keep the same member connection alive. Revocation uses the real
            // Workspace API; no authorization bypass or mocked Hub group.
            using (var revoked = await ownerClient.DeleteAsync(
                $"/api/workspaces/{workspace:D}/members/{SecurityCiFixtureSeed.TenantAMemberUserId:D}"))
                Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
            await SettleAuthorizationEventsAsync(app, database);
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                Assert.False(await db.WorkspaceMembers.AsNoTracking().AnyAsync(row =>
                    row.WorkspaceId == workspace && row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId &&
                    row.Status == MembershipStatus.Active));
                var retained = await db.ConversationMembers.AsNoTracking().SingleAsync(row =>
                    row.ConversationId == privateId && row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId);
                Assert.True(retained.CanRead);
            }
            var denied = await member.InvokeAsync("SubscribeConversation", privateId);
            controls.ObserveInvocation("SubscribeConversation", denied, expectedAllowed: false, "AccessDenied");
            using (var revokedHistory = await memberClient.GetAsync($"/api/conversations/{privateId:D}/messages"))
                Assert.Equal(HttpStatusCode.BadRequest, revokedHistory.StatusCode);
            Assert.True(await owner.SubscribeAsync("SubscribeConversation", privateId));
            var after = await PostMessageAsync(ownerClient, database, scope);
            await AssertDeliveredAsync(app, after.Event, "Messaging.MessageCreated.v1", owner);
            await AssertExcludedAsync(member, after.Event);
            await AssertExcludedAsync(beta, after.Event);
            controls.ObserveIsolation("Messaging.MessageCreated.v1", RealtimeSubscriptionType.Conversation,
                "REVOKED_WORKSPACE_STALE_PRIVATE_SUBSCRIPTION", owner, after.Event, member, after.Event);

            // Fresh WebSocket must use current Workspace/Conversation authority
            // while the synthetic member's Tenant login remains valid.
            await using var reconnected = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug);
            var reconnectDenied = await reconnected.InvokeAsync("SubscribeConversation", privateId);
            controls.ObserveInvocation("SubscribeConversation", reconnectDenied, expectedAllowed: false, "AccessDenied");
            Assert.True(await reconnected.SubscribeAsync("SubscribeUser"));
            var final = await PostMessageAsync(ownerClient, database, scope);
            await AssertDeliveredAsync(app, final.Event, "Messaging.MessageCreated.v1", owner);
            await AssertExcludedAsync(member, final.Event);
            await AssertExcludedAsync(reconnected, final.Event);
            controls.ObserveIsolation("Messaging.MessageCreated.v1", RealtimeSubscriptionType.Conversation,
                "RECONNECT_AFTER_WORKSPACE_REVOCATION", owner, final.Event, reconnected, final.Event);
            await controls.SaveAsync();
        });
    }

    private static async Task SettleAuthorizationEventsAsync(SecurityArchitectureSignalRFixture app, string database)
    {
        Guid[] events;
        await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            events = await db.OutboxEvents.AsNoTracking()
                .Where(row => row.EventType == "Security.AuthorizationStateChanged.v1")
                .Select(row => row.Id).ToArrayAsync();
        foreach (var id in events) await app.WaitDeliveredAsync(id);
    }

    private sealed record ChannelScope(Guid Tenant, Guid Workspace, Guid Conversation);

    private static async Task<ChannelScope> CreateCanonicalScopeAsync(HttpClient ownerClient, string database)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspaces");
        request.Content = JsonContent.Create(new { name = "Synthetic channel transport " + Guid.NewGuid().ToString("N") });
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var created = await ownerClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var response = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var workspace = response.RootElement.GetProperty("data").GetProperty("id").GetGuid();
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var conversation = await db.Conversations.AsNoTracking().SingleAsync(row => row.WorkspaceId == workspace);
        Assert.Equal(ConversationType.WorkspaceChannel, conversation.Type);
        Assert.Equal(ConversationDefaultKind.WorkspaceGeneral, conversation.DefaultKind);
        Assert.Equal(ConversationVisibility.PublicWithinScope, conversation.Visibility);
        Assert.Null(conversation.ProjectId);
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            TenantId = conversation.TenantId, WorkspaceId = workspace, UserId = SecurityCiFixtureSeed.TenantAMemberUserId,
            Role = WorkspaceRole.Member, Status = MembershipStatus.Active
        });
        // Only the second synthetic user's membership is fixture setup; creation used HTTP.
        db.ConversationMembers.Add(new ConversationMember
        {
            TenantId = conversation.TenantId, ConversationId = conversation.Id, UserId = SecurityCiFixtureSeed.TenantAMemberUserId,
            Role = ConversationMemberRole.Member, CanRead = true, CanPost = true, CanCreateThread = true, JoinedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return new(conversation.TenantId, workspace, conversation.Id);
    }

    private static async Task SubscribeAsync(RealtimeSocket socket, Guid conversation)
    {
        Assert.True(await socket.SubscribeAsync("SubscribeUser"));
        Assert.True(await socket.SubscribeAsync("SubscribeConversation", conversation));
    }

    private static async Task AssertSubscriptionDeniedAsync(RealtimeSocket socket, Guid conversation)
    {
        var denied = await socket.InvokeAsync("SubscribeConversation", conversation);
        Assert.False(denied.GetProperty("allowed").GetBoolean());
        Assert.Equal("AccessDenied", denied.GetProperty("code").GetString());
        Assert.True(await socket.SubscribeAsync("SubscribeUser"));
    }

    private static async Task<(Guid Message, Guid Event)> PostMessageAsync(HttpClient client, string database, ChannelScope scope, Guid? mention = null)
    {
        using var posted = await client.PostAsJsonAsync($"/api/conversations/{scope.Conversation}/messages", new
        {
            body = "Synthetic protected WorkspaceChannel body " + Guid.NewGuid().ToString("N"), clientRequestId = Guid.NewGuid(),
            mentionedUserIds = mention.HasValue ? new[] { mention.Value } : Array.Empty<Guid>()
        });
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        using var json = JsonDocument.Parse(await posted.Content.ReadAsStringAsync());
        var message = json.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(scope.Conversation, json.RootElement.GetProperty("conversationId").GetGuid());
        return (message, await EventAsync(database, "Messaging.MessageCreated.v1", message));
    }

    private static async Task<(Guid Message, Guid Event)> PostReplyAsync(HttpClient client, string database, Guid root, Guid? mention = null)
    {
        using var replied = await client.PostAsJsonAsync($"/api/messages/{root}/thread/messages", new
        {
            body = "Synthetic protected channel reply " + Guid.NewGuid().ToString("N"), clientRequestId = Guid.NewGuid(),
            mentionedUserIds = mention.HasValue ? new[] { mention.Value } : Array.Empty<Guid>()
        });
        Assert.Equal(HttpStatusCode.OK, replied.StatusCode);
        using var json = JsonDocument.Parse(await replied.Content.ReadAsStringAsync());
        var message = json.RootElement.GetProperty("message").GetProperty("id").GetGuid();
        Assert.Equal(root, json.RootElement.GetProperty("message").GetProperty("threadRootMessageId").GetGuid());
        return (message, await EventAsync(database, "Messaging.MessageCreated.v1", message));
    }

    private static async Task<Guid> MentionEventAsync(string database, Guid message, Guid user)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var notification = await db.Notifications.AsNoTracking().SingleAsync(row => row.UserId == user && row.RelatedEntityType == "Message" && row.RelatedEntityId == message);
        Assert.Equal(NotificationType.Mention, notification.NotificationType);
        return await EventAsync(database, "Notifications.NotificationCreated.v1", notification.Id);
    }

    private static async Task<Guid> MarkReadAsync(HttpClient client, string database, ChannelScope scope, Guid message, Guid user)
    {
        using var read = await client.PostAsJsonAsync($"/api/conversations/{scope.Conversation}/read", new { lastReadMessageId = message });
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var state = await db.ReadStates.AsNoTracking().SingleAsync(row => row.ConversationId == scope.Conversation && row.UserId == user);
        Assert.Equal(message, state.LastReadMessageId);
        return await EventAsync(database, "Messaging.ConversationUnreadChanged.v1", state.Id);
    }

    private static async Task<Guid> EventAsync(string database, string type, Guid aggregate)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        return await db.OutboxEvents.AsNoTracking().Where(row => row.EventType == type && row.AggregateId == aggregate).Select(row => row.Id).SingleAsync();
    }

    private static async Task<Guid> LatestEventAsync(string database, string type, Guid aggregate)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        return await db.OutboxEvents.AsNoTracking().Where(row => row.EventType == type && row.AggregateId == aggregate)
            .OrderByDescending(row => row.OccurredAt).Select(row => row.Id).FirstAsync();
    }

    private static async Task AssertDeliveredAsync(SecurityArchitectureSignalRFixture app, Guid eventId, string type, params RealtimeSocket[] sockets)
    {
        foreach (var socket in sockets)
        {
            await socket.WaitEventAsync(eventId);
            Assert.True(socket.Received(eventId, type));
        }
        await app.WaitDeliveredAsync(eventId);
    }

    private static async Task AssertRecipientAsync(SecurityArchitectureSignalRFixture app, Guid eventId, string type, RealtimeSocket recipient, RealtimeSocket other)
    {
        await AssertDeliveredAsync(app, eventId, type, recipient);
        await AssertExcludedAsync(other, eventId);
    }

    private static async Task AssertExcludedAsync(RealtimeSocket socket, params Guid[] eventIds)
    {
        // Delivered precedes this same-connection RPC. Its completion drains earlier
        // durable frames from this receiver and also proves the excluded peer is live.
        Assert.True(await socket.SubscribeAsync("SubscribeUser"));
        foreach (var id in eventIds) Assert.False(socket.Received(id));
    }

    private static async Task AssertCatchUpAsync(HttpClient client, ChannelScope scope, Guid root, Guid message)
    {
        using var detail = await client.GetAsync($"/api/conversations/{scope.Conversation}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var messages = await client.GetAsync($"/api/conversations/{scope.Conversation}/messages?anchorMessageId={message}");
        Assert.Equal(HttpStatusCode.OK, messages.StatusCode);
        using var json = JsonDocument.Parse(await messages.Content.ReadAsStringAsync());
        Assert.Contains(json.RootElement.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == message);
        using var thread = await client.GetAsync($"/api/messages/{root}/thread");
        Assert.Equal(HttpStatusCode.OK, thread.StatusCode);
    }

    private static async Task AssertBodyDeniedAsync(HttpClient client, ChannelScope scope, Guid root)
    {
        foreach (var route in new[] { $"/api/conversations/{scope.Conversation}", $"/api/conversations/{scope.Conversation}/messages", $"/api/messages/{root}/thread" })
        {
            using var denied = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            using var json = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
            Assert.Single(json.RootElement.EnumerateObject());
            Assert.Contains(json.RootElement.GetProperty("error").GetString(), new[] { "Conversation not found.", "Message thread not found." });
            Assert.DoesNotContain("Synthetic protected", json.RootElement.GetRawText(), StringComparison.Ordinal);
        }
    }
}
