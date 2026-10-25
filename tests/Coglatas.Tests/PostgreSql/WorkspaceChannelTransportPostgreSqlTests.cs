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

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "SEC17")]
    public async Task PrivateChannelThreadReplyAndReplayRequireCurrentConversationMembership()
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
            var workspaceScope = await CreateCanonicalScopeAsync(ownerClient, database);
            var route = $"/api/workspaces/{workspaceScope.Workspace:D}/chat-channels";
            Guid conversation;
            using (var request = new HttpRequestMessage(HttpMethod.Post, route))
            {
                request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
                request.Content = JsonContent.Create(new
                {
                    name = "Synthetic protected Thread " + Guid.NewGuid().ToString("N"), visibility = "Private"
                });
                using var created = await ownerClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, created.StatusCode);
                using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
                conversation = json.RootElement.GetProperty("id").GetGuid();
                Assert.Equal("Private", json.RootElement.GetProperty("visibility").GetString());
            }
            var scope = new ChannelScope(workspaceScope.Tenant, workspaceScope.Workspace, conversation);
            var invitationRoute = $"{route}/{conversation:D}/members";
            using (var invited = await ownerClient.PostAsJsonAsync(invitationRoute,
                new { userId = SecurityCiFixtureSeed.TenantAMemberUserId }))
                Assert.Equal(HttpStatusCode.OK, invited.StatusCode);
            await SettleThreadAuthorizationAsync(app, database);

            await using var owner = await app.ConnectAsync(ownerClient, "owner", SecurityCiFixtureSeed.TenantASlug);
            await using var member = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug);
            await using var beta = await app.ConnectAsync(betaClient, "beta", SecurityCiFixtureSeed.TenantBSlug);
            await SubscribeAsync(owner, conversation);
            await SubscribeAsync(member, conversation);
            await AssertSubscriptionDeniedAsync(beta, conversation);
            var controls = SecurityArchitectureSignalRControlRecorder.Create(GetType());
            var root = await PostMessageAsync(ownerClient, database, scope);
            await AssertDeliveredAsync(app, root.Event, "Messaging.MessageCreated.v1", owner, member);
            var before = await PostReplyAsync(memberClient, database, root.Message);
            var beforeThread = await LatestEventAsync(database, "Messaging.ThreadChanged.v1", root.Message);
            await AssertDeliveredAsync(app, before.Event, "Messaging.MessageCreated.v1", owner, member);
            await AssertDeliveredAsync(app, beforeThread, "Messaging.ThreadChanged.v1", owner, member);
            await AssertPrivateThreadReplyAsync(memberClient, scope, root.Message, before.Message);

            using (var removed = await ownerClient.DeleteAsync(
                $"/api/conversations/{conversation:D}/members/{SecurityCiFixtureSeed.TenantAMemberUserId:D}"))
                Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
            await SettleThreadAuthorizationAsync(app, database);
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                Assert.True(await db.WorkspaceMembers.AsNoTracking().AnyAsync(row =>
                    row.WorkspaceId == scope.Workspace && row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId &&
                    row.Status == MembershipStatus.Active));
                var removed = await db.ConversationMembers.AsNoTracking().SingleAsync(row =>
                    row.ConversationId == conversation && row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId);
                Assert.NotNull(removed.LeftAt);
                Assert.NotNull(removed.RemovedAt);
            }
            Assert.True(await member.SubscribeAsync("SubscribeWorkspace", scope.Workspace));
            await AssertSubscriptionDeniedAsync(member, conversation);
            await AssertBodyDeniedAsync(memberClient, scope, root.Message);
            var beforeDenied = await ThreadWriteCountsAsync(database, scope, root.Message);
            var deniedRequest = Guid.NewGuid();
            using (var deniedReply = await memberClient.PostAsJsonAsync($"/api/messages/{root.Message:D}/thread/messages",
                new { body = "Synthetic protected denied reply", clientRequestId = deniedRequest }))
            {
                Assert.Equal(HttpStatusCode.BadRequest, deniedReply.StatusCode);
                using var error = JsonDocument.Parse(await deniedReply.Content.ReadAsStringAsync());
                Assert.Single(error.RootElement.EnumerateObject());
                Assert.Equal("Message thread not found.", error.RootElement.GetProperty("error").GetString());
            }
            var afterDenied = await ThreadWriteCountsAsync(database, scope, root.Message);
            Assert.Equal(beforeDenied.Messages, afterDenied.Messages);
            Assert.Equal(beforeDenied.Events, afterDenied.Events);
            Assert.Equal(beforeDenied.SuccessAudits, afterDenied.SuccessAudits);
            Assert.Equal(beforeDenied.DeniedAudits + 1, afterDenied.DeniedAudits);
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                Assert.False(await db.Messages.AsNoTracking().AnyAsync(row => row.ClientRequestId == deniedRequest));

            var after = await PostReplyAsync(ownerClient, database, root.Message);
            var afterThread = await LatestEventAsync(database, "Messaging.ThreadChanged.v1", root.Message);
            await AssertDeliveredAsync(app, after.Event, "Messaging.MessageCreated.v1", owner);
            await AssertDeliveredAsync(app, afterThread, "Messaging.ThreadChanged.v1", owner);
            await AssertExcludedAsync(member, after.Event, afterThread);
            await AssertExcludedAsync(beta, after.Event, afterThread);
            foreach (var (id, type) in new[] { (after.Event, "Messaging.MessageCreated.v1"),
                         (afterThread, "Messaging.ThreadChanged.v1") })
            {
                controls.ObserveIsolation(type, RealtimeSubscriptionType.Conversation,
                    "PRIVATE_REMOVED_MEMBER_THREAD_DELIVERY", owner, id, member, id);
                controls.ObserveIsolation(type, RealtimeSubscriptionType.Conversation,
                    "PRIVATE_THREAD_CROSS_TENANT", owner, id, beta, id);
            }

            await using var fresh = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug);
            Assert.True(await fresh.SubscribeAsync("SubscribeWorkspace", scope.Workspace));
            await AssertSubscriptionDeniedAsync(fresh, conversation);
            // Replay a business-produced reply and Thread hint previously received
            // by the removed member. Counts must stay fixed on that live socket.
            var priorMemberReplyCount = member.DeliveryCount(before.Event);
            var priorMemberThreadCount = member.DeliveryCount(beforeThread);
            Assert.True(priorMemberReplyCount > 0);
            Assert.True(priorMemberThreadCount > 0);
            await ReplayThreadEventsAsync(app, database, owner, before.Event, beforeThread);
            Assert.True(await member.SubscribeAsync("SubscribeUser"));
            Assert.Equal(priorMemberReplyCount, member.DeliveryCount(before.Event));
            Assert.Equal(priorMemberThreadCount, member.DeliveryCount(beforeThread));
            await AssertExcludedAsync(fresh, before.Event, beforeThread);
            await AssertExcludedAsync(beta, before.Event, beforeThread);
            controls.ObserveIsolation("Messaging.MessageCreated.v1", RealtimeSubscriptionType.Conversation,
                "PRIVATE_REPLY_REPLAY_AFTER_REMOVAL_RECONNECT", owner, before.Event, fresh, before.Event);
            controls.ObserveIsolation("Messaging.ThreadChanged.v1", RealtimeSubscriptionType.Conversation,
                "PRIVATE_THREAD_REPLAY_AFTER_REMOVAL_RECONNECT", owner, beforeThread, fresh, beforeThread);

            // Current invitation, rather than a retained session or old event ID,
            // restores read/subscription authority and both replay deliveries.
            using (var invited = await ownerClient.PostAsJsonAsync(invitationRoute,
                new { userId = SecurityCiFixtureSeed.TenantAMemberUserId }))
                Assert.Equal(HttpStatusCode.OK, invited.StatusCode);
            await SettleThreadAuthorizationAsync(app, database);
            await SubscribeAsync(member, conversation);
            await SubscribeAsync(owner, conversation);
            await AssertPrivateThreadReplyAsync(memberClient, scope, root.Message, after.Message);
            await ReplayThreadEventsAsync(app, database, owner, before.Event, beforeThread);
            await member.WaitEventAsync(before.Event, priorMemberReplyCount + 1);
            await member.WaitEventAsync(beforeThread, priorMemberThreadCount + 1);
            controls.ObservePositive("Messaging.MessageCreated.v1", RealtimeSubscriptionType.Conversation,
                "CURRENT_PRIVATE_REINVITATION_REPLY_REPLAY", member, before.Event);
            controls.ObservePositive("Messaging.ThreadChanged.v1", RealtimeSubscriptionType.Conversation,
                "CURRENT_PRIVATE_REINVITATION_THREAD_REPLAY", member, beforeThread);
            await controls.SaveAsync();
        });
    }

    private static async Task AssertPrivateThreadReplyAsync(HttpClient client, ChannelScope scope, Guid root, Guid reply)
    {
        await AssertCatchUpAsync(client, scope, root, root);
        using var response = await client.GetAsync($"/api/messages/{root:D}/thread?anchorReplyMessageId={reply:D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(root, json.RootElement.GetProperty("rootMessage").GetProperty("id").GetGuid());
        // A boolean assertion keeps protected DTO bodies out of failure output.
        Assert.True(json.RootElement.GetProperty("replies").EnumerateArray().Any(row =>
            row.GetProperty("id").GetGuid() == reply && row.GetProperty("threadRootMessageId").GetGuid() == root));
    }

    private static async Task<(int Messages, int Events, int SuccessAudits, int DeniedAudits)> ThreadWriteCountsAsync(
        string database, ChannelScope scope, Guid root)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        return (await db.Messages.CountAsync(row => row.ConversationId == scope.Conversation),
            await db.OutboxEvents.CountAsync(row => row.TenantId == scope.Tenant &&
                (row.EventType == "Messaging.MessageCreated.v1" || row.EventType == "Messaging.ThreadChanged.v1")),
            await db.AuditLogs.CountAsync(row => row.WorkspaceId == scope.Workspace &&
                (row.Action == "communication.message_posted" || row.Action == "communication.thread_reply_posted")),
            await db.AuditLogs.CountAsync(row => row.EntityId == root && row.Action == "MessageThreadReplyDenied"));
    }

    private static async Task SettleThreadAuthorizationAsync(SecurityArchitectureSignalRFixture app, string database)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var ids = await db.OutboxEvents.AsNoTracking().Where(row => row.EventType == "Security.AuthorizationStateChanged.v1")
            .Select(row => row.Id).ToArrayAsync();
        foreach (var id in ids) await app.WaitDeliveredAsync(id);
    }

    private static async Task ReplayThreadEventsAsync(SecurityArchitectureSignalRFixture app, string database,
        RealtimeSocket owner, params Guid[] ids)
    {
        var counts = ids.Select(owner.DeliveryCount).ToArray();
        var started = DateTimeOffset.UtcNow;
        await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
        {
            var repository = new OutboxEventRepository(db);
            foreach (var id in ids) Assert.True(await repository.ReplayAsync(id, started));
            await db.SaveChangesAsync();
        }
        for (var index = 0; index < ids.Length; index++)
        {
            await app.WaitDeliveredAsync(ids[index], started);
            await owner.WaitEventAsync(ids[index], counts[index] + 1);
        }
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
