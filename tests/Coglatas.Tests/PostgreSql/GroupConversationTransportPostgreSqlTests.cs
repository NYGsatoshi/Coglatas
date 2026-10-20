using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.SecurityArchitecture;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed class GroupConversationTransportPostgreSqlTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task GroupBusinessDeliveryReconnectAndReplayUseCurrentWorkspaceAndParticipantAuthority() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var ownerClient = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAOwnerEmail);
            using var memberClient = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
            Guid workspace;
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                workspace = await db.Workspaces.Where(row => row.Slug == SecurityCiFixtureSeed.TenantAWorkspaceSlug).Select(row => row.Id).SingleAsync();
            using var created = await ownerClient.PostAsJsonAsync("/api/conversations", new
            {
                type = 7, workspaceId = workspace, title = "Synthetic Group transport",
                memberUserIds = new[] { SecurityCiFixtureSeed.TenantAMemberUserId, SecurityCiFixtureSeed.TenantARestrictedUserId }
            });
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            using var detail = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            var group = detail.RootElement.GetProperty("id").GetGuid();
            Assert.Equal("Group", detail.RootElement.GetProperty("type").GetString());
            await using var owner = await app.ConnectAsync(ownerClient, "owner", SecurityCiFixtureSeed.TenantASlug);
            Assert.True(await owner.SubscribeAsync("SubscribeUser"));
            Assert.True(await owner.SubscribeAsync("SubscribeConversation", group));
            Guid originalMessage;
            Guid originalEvent;
            Guid memberMention;
            Guid memberUnread;
            Guid ownerUnread;
            await using (var member = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug))
            {
                Assert.True(await member.SubscribeAsync("SubscribeUser"));
                Assert.True(await member.SubscribeAsync("SubscribeConversation", group));
                (originalMessage, originalEvent) = await PostAsync(ownerClient, database, group, SecurityCiFixtureSeed.TenantAMemberUserId);
                await DeliveredAsync(app, originalEvent, "Messaging.MessageCreated.v1", owner, member);
                memberMention = await MentionAsync(database, originalMessage, SecurityCiFixtureSeed.TenantAMemberUserId);
                await DeliveredAsync(app, memberMention, "Notifications.NotificationCreated.v1", member);
                await ExcludedAsync(owner, memberMention);
                memberUnread = await MarkReadAsync(memberClient, database, group, originalMessage, SecurityCiFixtureSeed.TenantAMemberUserId);
                await DeliveredAsync(app, memberUnread, "Messaging.ConversationUnreadChanged.v1", member);
                await ExcludedAsync(owner, memberUnread);
                var response = await PostAsync(memberClient, database, group);
                await DeliveredAsync(app, response.Event, "Messaging.MessageCreated.v1", owner, member);
                ownerUnread = await MarkReadAsync(ownerClient, database, group, response.Message, SecurityCiFixtureSeed.TenantAOwnerUserId);
                await DeliveredAsync(app, ownerUnread, "Messaging.ConversationUnreadChanged.v1", owner);
                await ExcludedAsync(member, ownerUnread);
            }
            var missed = await PostAsync(ownerClient, database, group);
            await DeliveredAsync(app, missed.Event, "Messaging.MessageCreated.v1", owner);
            await using var reconnected = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug);
            Assert.True(await reconnected.SubscribeAsync("SubscribeUser"));
            Assert.True(await reconnected.SubscribeAsync("SubscribeConversation", group));
            await CatchUpAsync(memberClient, group, missed.Message);
            var connected = await PostAsync(ownerClient, database, group);
            await DeliveredAsync(app, connected.Event, "Messaging.MessageCreated.v1", owner, reconnected);

            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                Assert.Equal(1, await db.WorkspaceMembers.Where(row => row.WorkspaceId == workspace && row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, MembershipStatus.Suspended)));
                var participant = await db.ConversationMembers.AsNoTracking().SingleAsync(row => row.ConversationId == group && row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId);
                Assert.Null(participant.LeftAt);
                Assert.Null(participant.RemovedAt);
                Assert.True(participant.CanRead);
                Assert.True(participant.CanPost);
            }
            await DeniedAsync(reconnected, memberClient, group);
            var revoked = await PostAsync(ownerClient, database, group);
            await DeliveredAsync(app, revoked.Event, "Messaging.MessageCreated.v1", owner);
            await ExcludedAsync(reconnected, revoked.Event);
            // Requeue previously business-produced envelopes through the actual
            // repository/dispatcher. This is not the operator replay capability.
            Assert.True(await owner.SubscribeAsync("SubscribeUser"));
            Assert.True(await reconnected.SubscribeAsync("SubscribeUser"));
            var ownerBefore = owner.DeliveryCount(originalEvent);
            var ownerUnreadBefore = owner.DeliveryCount(ownerUnread);
            var memberBefore = new[] { originalEvent, memberMention, memberUnread, ownerUnread }.ToDictionary(id => id, reconnected.DeliveryCount);
            var replayStarted = DateTimeOffset.UtcNow;
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                var repository = new OutboxEventRepository(db);
                foreach (var id in memberBefore.Keys) Assert.True(await repository.ReplayAsync(id, replayStarted));
                await db.SaveChangesAsync();
            }
            foreach (var id in memberBefore.Keys) await app.WaitDeliveredAsync(id, replayStarted);
            await owner.WaitEventAsync(originalEvent, ownerBefore + 1);
            await owner.WaitEventAsync(ownerUnread, ownerUnreadBefore + 1);
            Assert.True(await reconnected.SubscribeAsync("SubscribeUser"));
            foreach (var pair in memberBefore) Assert.Equal(pair.Value, reconnected.DeliveryCount(pair.Key));
            await using (var deniedReconnect = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug))
                await DeniedAsync(deniedReconnect, memberClient, group);

            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                await db.WorkspaceMembers.Where(row => row.WorkspaceId == workspace && row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, MembershipStatus.Active));
            Assert.True(await reconnected.SubscribeAsync("SubscribeConversation", group));
            await ChangeMembershipAsync(app, ownerClient, reconnected, database, group, add: false);
            await DeniedAsync(reconnected, memberClient, group);
            var afterRemoval = await PostAsync(ownerClient, database, group);
            await DeliveredAsync(app, afterRemoval.Event, "Messaging.MessageCreated.v1", owner);
            await ExcludedAsync(reconnected, afterRemoval.Event);
            await ChangeMembershipAsync(app, ownerClient, reconnected, database, group, add: true);
            Assert.True(await reconnected.SubscribeAsync("SubscribeUser"));
            Assert.True(await reconnected.SubscribeAsync("SubscribeConversation", group));
            await CatchUpAsync(memberClient, group, originalMessage);
            await CatchUpAsync(memberClient, group, afterRemoval.Message);
            var restored = await PostAsync(memberClient, database, group);
            await DeliveredAsync(app, restored.Event, "Messaging.MessageCreated.v1", owner, reconnected);
            using (var held = await memberClient.GetAsync($"/api/messages/{originalMessage}/thread"))
                Assert.Equal(HttpStatusCode.BadRequest, held.StatusCode);
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                var messageIds = await db.Messages.Where(row => row.ConversationId == group).Select(row => row.Id).ToListAsync();
                Assert.False(await db.Conversations.AnyAsync(row => row.ParentConversationId == group));
                Assert.False(await db.OutboxEvents.AnyAsync(row => row.EventType == "Messaging.ThreadChanged.v1" && messageIds.Contains(row.AggregateId)));
            }
        });

    private static async Task<(Guid Message, Guid Event)> PostAsync(HttpClient client, string database, Guid group, Guid? mention = null)
    {
        using var sent = await client.PostAsJsonAsync($"/api/conversations/{group}/messages", new
        {
            body = "Synthetic Group transport body " + Guid.NewGuid().ToString("N"), clientRequestId = Guid.NewGuid(),
            mentionedUserIds = mention.HasValue ? new[] { mention.Value } : Array.Empty<Guid>()
        });
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        using var json = JsonDocument.Parse(await sent.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(group, json.RootElement.GetProperty("conversationId").GetGuid());
        return (id, await EventAsync(database, "Messaging.MessageCreated.v1", id));
    }

    private static async Task<Guid> MarkReadAsync(HttpClient client, string database, Guid group, Guid message, Guid user)
    {
        using var read = await client.PostAsJsonAsync($"/api/conversations/{group}/read", new { lastReadMessageId = message });
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var state = await db.ReadStates.AsNoTracking().SingleAsync(row => row.ConversationId == group && row.UserId == user);
        Assert.Equal(message, state.LastReadMessageId);
        return await EventAsync(database, "Messaging.ConversationUnreadChanged.v1", state.Id);
    }

    private static async Task<Guid> MentionAsync(string database, Guid message, Guid user)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var notification = await db.Notifications.AsNoTracking().SingleAsync(row => row.UserId == user && row.RelatedEntityId == message && row.RelatedEntityType == "Message");
        Assert.Equal(NotificationType.Mention, notification.NotificationType);
        return await EventAsync(database, "Notifications.NotificationCreated.v1", notification.Id);
    }

    private static async Task<Guid> EventAsync(string database, string type, Guid aggregate)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        return await db.OutboxEvents.Where(row => row.EventType == type && row.AggregateId == aggregate).Select(row => row.Id).SingleAsync();
    }

    private static async Task ChangeMembershipAsync(SecurityArchitectureSignalRFixture app, HttpClient owner, RealtimeSocket member, string database, Guid group, bool add)
    {
        var user = SecurityCiFixtureSeed.TenantAMemberUserId;
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var before = await db.OutboxEvents.Where(row => row.EventType == "Security.AuthorizationStateChanged.v1" && row.AggregateId == user).Select(row => row.Id).ToListAsync();
        using var response = add
            ? await owner.PostAsJsonAsync($"/api/conversations/{group}/members", new { userId = user })
            : await owner.DeleteAsync($"/api/conversations/{group}/members/{user}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var invalidation = await db.OutboxEvents.Where(row => row.EventType == "Security.AuthorizationStateChanged.v1" && row.AggregateId == user && !before.Contains(row.Id)).Select(row => row.Id).SingleAsync();
        await DeliveredAsync(app, invalidation, "Security.AuthorizationStateChanged.v1", member);
        // This command intentionally invalidates existing subscriptions. Await its
        // real delivered event before the subsequent explicit current subscription.
        Assert.True(await member.SubscribeAsync("SubscribeUser"));
    }

    private static async Task DeliveredAsync(SecurityArchitectureSignalRFixture app, Guid id, string type, params RealtimeSocket[] sockets)
    {
        foreach (var socket in sockets)
        {
            await socket.WaitEventAsync(id);
            Assert.True(socket.Received(id, type));
        }
        await app.WaitDeliveredAsync(id);
    }

    private static async Task ExcludedAsync(RealtimeSocket socket, params Guid[] ids)
    {
        Assert.True(await socket.SubscribeAsync("SubscribeUser"));
        foreach (var id in ids) Assert.False(socket.Received(id));
    }

    private static async Task DeniedAsync(RealtimeSocket socket, HttpClient client, Guid group)
    {
        var response = await socket.InvokeAsync("SubscribeConversation", group);
        Assert.False(response.GetProperty("allowed").GetBoolean());
        Assert.Equal("AccessDenied", response.GetProperty("code").GetString());
        Assert.True(await socket.SubscribeAsync("SubscribeUser"));
        using var body = await client.GetAsync($"/api/conversations/{group}/messages");
        Assert.Equal(HttpStatusCode.BadRequest, body.StatusCode);
        Assert.DoesNotContain("Synthetic Group transport body", await body.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using var send = await client.PostAsJsonAsync($"/api/conversations/{group}/messages", new { body = "Denied Group transport send", clientRequestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, send.StatusCode);
    }

    private static async Task CatchUpAsync(HttpClient client, Guid group, Guid message)
    {
        using var response = await client.GetAsync($"/api/conversations/{group}/messages?anchorMessageId={message}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains(json.RootElement.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == message && item.GetProperty("body").GetString()!.StartsWith("Synthetic Group transport body", StringComparison.Ordinal));
    }
}
