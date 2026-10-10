using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Coglatas.Application.Realtime;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Scope = Coglatas.Tests.SecurityArchitecture.SecurityArchitectureSignalRTests.Scope;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed class SecurityArchitectureSignalRProducerTests
{
    private const string MessageRoute = "/api/messages/{messageId}";
    private const string ReplyRoute = "/api/messages/{messageId}/thread/messages";
    private const string ReadRoute = "/api/conversations/{conversationId}/read";
    private const string Original = "HTTP_BUSINESS_ORIGINAL_PRODUCER";
    private const string Restored = "HTTP_BUSINESS_RESTORED_CURRENT_AUTHORITY";

    [PostgreSqlFact]
    public async Task ActualMessagingHttpProducersReauthorizeCurrentResourceWithoutMutationEffects()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var memberClient = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
            using var ownerClient = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAOwnerEmail);
            await using var member = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug);
            await using var owner = await app.ConnectAsync(ownerClient, "owner", SecurityCiFixtureSeed.TenantASlug);
            var scope = await SecurityArchitectureSignalRTests.ScopeAsync(database, SecurityCiFixtureSeed.TenantASlug);
            var controls = SecurityArchitectureSignalRControlRecorder.Create(GetType());
            var http = SecurityArchitectureHttpControlRecorder.Create(GetType(), "ACTUAL_TEST_WEB_ENTRY_POINT_AND_MIGRATED_POSTGRESQL");
            await SecurityArchitectureSignalREventTests.InvokeWithReceiptAsync(member, controls, "SubscribeConversation", true, "Subscribed", scope.Conversation);
            await SecurityArchitectureSignalREventTests.InvokeWithReceiptAsync(member, controls, "SubscribeUser", true, "Subscribed");
            Assert.True(await owner.SubscribeAsync("SubscribeConversation", scope.Conversation));
            Assert.True(await owner.SubscribeAsync("SubscribeUser"));

            var root = await CreateMessageAsync(ownerClient, database, scope);
            await member.WaitEventAsync(root.Event);
            await owner.WaitEventAsync(root.Event);
            await app.WaitDeliveredAsync(root.Event);
            controls.ObservePositive("Messaging.MessageCreated.v1", RealtimeSubscriptionType.Conversation, Original, member, root.Event);
            var editable = await CreateMessageAsync(memberClient, database, scope);
            var disposable = await CreateMessageAsync(memberClient, database, scope);
            await app.WaitDeliveredAsync(editable.Event);
            await app.WaitDeliveredAsync(disposable.Event);

            var before = await EventIdsAsync(database);
            using (var updated = await memberClient.PatchAsJsonAsync($"/api/messages/{editable.Message:D}", new { body = "Synthetic authorized edit" }))
            {
                Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
                using var payload = JsonDocument.Parse(await updated.Content.ReadAsStringAsync());
                Assert.Equal(editable.Message, payload.RootElement.GetProperty("id").GetGuid());
                Assert.Equal(2, payload.RootElement.GetProperty("version").GetInt64());
                http.Observe(updated, MessageRoute, "AUTHORIZED_SAME_SCOPE", HttpStatusCode.OK);
            }
            await ObserveProducedAsync(database, app, controls, member, "Messaging.MessageUpdated.v1", editable.Message, before, Original);

            before = await EventIdsAsync(database);
            using (var replied = await memberClient.PostAsJsonAsync($"/api/messages/{root.Message:D}/thread/messages",
                       new { body = "Synthetic authorized reply", clientRequestId = Guid.NewGuid() }))
            {
                Assert.Equal(HttpStatusCode.OK, replied.StatusCode);
                using var payload = JsonDocument.Parse(await replied.Content.ReadAsStringAsync());
                Assert.Equal(root.Message, payload.RootElement.GetProperty("message").GetProperty("threadRootMessageId").GetGuid());
                Assert.Equal(1, payload.RootElement.GetProperty("summary").GetProperty("replyCount").GetInt32());
                http.Observe(replied, ReplyRoute, "AUTHORIZED_SAME_SCOPE", HttpStatusCode.OK);
            }
            await ObserveProducedAsync(database, app, controls, member, "Messaging.ThreadChanged.v1", root.Message, before, Original);

            before = await EventIdsAsync(database);
            using (var read = await memberClient.PostAsJsonAsync($"/api/conversations/{scope.Conversation:D}/read", new { lastReadMessageId = root.Message }))
            {
                Assert.Equal(HttpStatusCode.OK, read.StatusCode);
                http.Observe(read, ReadRoute, "AUTHORIZED_SAME_SCOPE", HttpStatusCode.OK);
            }
            var memberReadState = await ReadStateIdAsync(database, scope, SecurityCiFixtureSeed.TenantAMemberUserId);
            await ObserveProducedAsync(database, app, controls, member, "Messaging.ConversationUnreadChanged.v1", memberReadState, before, Original);

            before = await EventIdsAsync(database);
            using (var deleted = await memberClient.DeleteAsync($"/api/messages/{disposable.Message:D}"))
            {
                Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
                http.Observe(deleted, MessageRoute, "AUTHORIZED_SAME_SCOPE", HttpStatusCode.OK);
            }
            await AssertTombstoneAsync(database, disposable.Message, SecurityCiFixtureSeed.TenantAMemberUserId);
            await ObserveProducedAsync(database, app, controls, member, "Messaging.MessageDeleted.v1", disposable.Message, before, Original);

            await SetMemberAuthorityAsync(database, scope, false);
            await AssertDeniedAsync(memberClient, database, scope, http, HttpMethod.Patch,
                $"/api/messages/{editable.Message:D}", MessageRoute, new { body = "Synthetic denied edit" },
                "communication.message_edit_denied", "Message", editable.Message, "You are not allowed to edit this message.", "author_required");
            await AssertDeniedAsync(memberClient, database, scope, http, HttpMethod.Delete,
                $"/api/messages/{editable.Message:D}", MessageRoute, null,
                "communication.message_delete_denied", "Message", editable.Message, "You are not allowed to delete this message.", "moderation_permission_denied");
            await AssertDeniedAsync(memberClient, database, scope, http, HttpMethod.Post,
                $"/api/messages/{root.Message:D}/thread/messages", ReplyRoute, new { body = "Synthetic denied reply", clientRequestId = Guid.NewGuid() },
                "MessageThreadReplyDenied", "Message", root.Message, "Message thread not found.", "conversation_post_denied");
            await AssertDeniedAsync(memberClient, database, scope, http, HttpMethod.Post,
                $"/api/conversations/{scope.Conversation:D}/read", ReadRoute, new { lastReadMessageId = editable.Message },
                "ConversationReadDenied", "Conversation", scope.Conversation, "Conversation not found.", "participant_missing");

            // Current legitimate owner operations still reach the live transport;
            // the already-connected revoked participant must receive none of them.
            before = await EventIdsAsync(database);
            using (var updated = await ownerClient.PatchAsJsonAsync($"/api/messages/{root.Message:D}", new { body = "Synthetic owner edit after revocation" }))
                Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            await ObserveProducedAsync(database, app, controls, owner, "Messaging.MessageUpdated.v1", root.Message, before,
                "HTTP_BUSINESS_CURRENT_CONVERSATION_AUTHORITY", member);
            before = await EventIdsAsync(database);
            Guid ownerReply;
            using (var replied = await ownerClient.PostAsJsonAsync($"/api/messages/{root.Message:D}/thread/messages",
                       new { body = "Synthetic owner reply after revocation", clientRequestId = Guid.NewGuid() }))
            {
                Assert.Equal(HttpStatusCode.OK, replied.StatusCode);
                using var payload = JsonDocument.Parse(await replied.Content.ReadAsStringAsync());
                ownerReply = payload.RootElement.GetProperty("message").GetProperty("id").GetGuid();
            }
            await ObserveProducedAsync(database, app, controls, owner, "Messaging.MessageCreated.v1", ownerReply, before,
                "HTTP_BUSINESS_CURRENT_CONVERSATION_AUTHORITY", member);
            await ObserveProducedAsync(database, app, controls, owner, "Messaging.ThreadChanged.v1", root.Message, before,
                "HTTP_BUSINESS_CURRENT_CONVERSATION_AUTHORITY", member);
            before = await EventIdsAsync(database);
            using (var deleted = await ownerClient.DeleteAsync($"/api/messages/{ownerReply:D}"))
                Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
            await AssertTombstoneAsync(database, ownerReply, SecurityCiFixtureSeed.TenantAOwnerUserId);
            await ObserveProducedAsync(database, app, controls, owner, "Messaging.MessageDeleted.v1", ownerReply, before,
                "HTTP_BUSINESS_CURRENT_CONVERSATION_AUTHORITY", member);

            var fresh = await CreateMessageAsync(ownerClient, database, scope);
            await owner.WaitEventAsync(fresh.Event);
            await app.WaitDeliveredAsync(fresh.Event);
            Assert.False(member.Received(fresh.Event));
            await SetMemberAuthorityAsync(database, scope, true);
            Assert.True(await member.SubscribeAsync("SubscribeConversation", scope.Conversation));
            Assert.True(await member.SubscribeAsync("SubscribeUser"));
            before = await EventIdsAsync(database);
            using (var updated = await memberClient.PatchAsJsonAsync($"/api/messages/{editable.Message:D}", new { body = "Synthetic restored edit" }))
            {
                Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
                http.Observe(updated, MessageRoute, "AUTHORIZED_RESTORED_SCOPE", HttpStatusCode.OK);
            }
            await ObserveProducedAsync(database, app, controls, member, "Messaging.MessageUpdated.v1", editable.Message, before, Restored);
            before = await EventIdsAsync(database);
            using (var replied = await memberClient.PostAsJsonAsync($"/api/messages/{root.Message:D}/thread/messages",
                       new { body = "Synthetic restored reply", clientRequestId = Guid.NewGuid() }))
            {
                Assert.Equal(HttpStatusCode.OK, replied.StatusCode);
                http.Observe(replied, ReplyRoute, "AUTHORIZED_RESTORED_SCOPE", HttpStatusCode.OK);
            }
            await ObserveProducedAsync(database, app, controls, member, "Messaging.ThreadChanged.v1", root.Message, before, Restored);
            before = await EventIdsAsync(database);
            using (var read = await memberClient.PostAsJsonAsync($"/api/conversations/{scope.Conversation:D}/read", new { lastReadMessageId = fresh.Message }))
            {
                Assert.Equal(HttpStatusCode.OK, read.StatusCode);
                http.Observe(read, ReadRoute, "AUTHORIZED_RESTORED_SCOPE", HttpStatusCode.OK);
            }
            await ObserveProducedAsync(database, app, controls, member, "Messaging.ConversationUnreadChanged.v1", memberReadState, before, Restored);
            before = await EventIdsAsync(database);
            using (var deleted = await memberClient.DeleteAsync($"/api/messages/{editable.Message:D}"))
            {
                Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
                http.Observe(deleted, MessageRoute, "AUTHORIZED_RESTORED_SCOPE", HttpStatusCode.OK);
            }
            await AssertTombstoneAsync(database, editable.Message, SecurityCiFixtureSeed.TenantAMemberUserId, 4);
            await ObserveProducedAsync(database, app, controls, member, "Messaging.MessageDeleted.v1", editable.Message, before, Restored);
            await controls.SaveAsync();
            await http.SaveAsync();
        });
    }

    private static async Task<(Guid Message, Guid Event)> CreateMessageAsync(HttpClient client, string database, Scope scope)
    {
        using var response = await client.PostAsJsonAsync($"/api/conversations/{scope.Conversation:D}/messages",
            new { body = "Synthetic actual producer message " + Guid.NewGuid().ToString("N"), clientRequestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var message = payload.RootElement.GetProperty("id").GetGuid();
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var eventId = await db.OutboxEvents.AsNoTracking().Where(item => item.AggregateId == message &&
            item.EventType == "Messaging.MessageCreated.v1").Select(item => item.Id).SingleAsync();
        return (message, eventId);
    }

    private static async Task<Guid[]> EventIdsAsync(string database)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        return await db.OutboxEvents.AsNoTracking().Select(item => item.Id).ToArrayAsync();
    }

    private static async Task<Guid> ReadStateIdAsync(string database, Scope scope, Guid userId)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        return await db.ReadStates.AsNoTracking().Where(item => item.ConversationId == scope.Conversation && item.UserId == userId)
            .Select(item => item.Id).SingleAsync();
    }

    private static async Task ObserveProducedAsync(string database, SecurityArchitectureSignalRFixture app,
        SecurityArchitectureSignalRControlRecorder controls, RealtimeSocket recipient, string eventType,
        Guid aggregateId, Guid[] before, string control, RealtimeSocket? excluded = null)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var eventId = await db.OutboxEvents.AsNoTracking().Where(item => item.AggregateId == aggregateId &&
            item.EventType == eventType && !before.Contains(item.Id)).Select(item => item.Id).SingleAsync();
        await recipient.WaitEventAsync(eventId);
        await app.WaitDeliveredAsync(eventId);
        var target = eventType == "Messaging.ConversationUnreadChanged.v1" ? RealtimeSubscriptionType.User : RealtimeSubscriptionType.Conversation;
        if (excluded is null) controls.ObservePositive(eventType, target, control, recipient, eventId);
        else controls.ObserveIsolation(eventType, target, control, recipient, eventId, excluded, eventId);
    }

    private static async Task SetMemberAuthorityAsync(string database, Scope scope, bool allowed)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var participant = await db.ConversationMembers.SingleAsync(item => item.ConversationId == scope.Conversation &&
            item.UserId == SecurityCiFixtureSeed.TenantAMemberUserId);
        participant.CanRead = allowed;
        participant.CanPost = allowed;
        await db.SaveChangesAsync();
    }

    private static async Task AssertTombstoneAsync(string database, Guid messageId, Guid actor, long expectedVersion = 2)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var message = await db.Messages.IgnoreQueryFilters().AsNoTracking().SingleAsync(item => item.Id == messageId);
        Assert.NotNull(message.DeletedAt);
        Assert.Equal(actor, message.DeletedByUserId);
        Assert.Empty(message.Body);
        Assert.Equal(expectedVersion, message.Version);
    }

    private static async Task AssertDeniedAsync(HttpClient client, string database, Scope scope,
        SecurityArchitectureHttpControlRecorder http, HttpMethod method, string path, string route, object? body,
        string action, string entityType, Guid entityId, string error, string reason)
    {
        var before = await StateAsync(database, scope);
        long auditBefore;
        await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            auditBefore = await db.AuditLogs.LongCountAsync(item => item.ActorUserId == SecurityCiFixtureSeed.TenantAMemberUserId &&
                item.Action == action && item.EntityType == entityType && item.EntityId == entityId);
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var denied = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        using var payload = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
        Assert.Equal(error, payload.RootElement.GetProperty("error").GetString());
        var after = await StateAsync(database, scope);
        Assert.Equal(before.StateDigest, after.StateDigest);
        Assert.Equal(before.OutboxCount, after.OutboxCount);
        await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
        {
            var audits = await db.AuditLogs.AsNoTracking().Where(item => item.ActorUserId == SecurityCiFixtureSeed.TenantAMemberUserId &&
                item.Action == action && item.EntityType == entityType && item.EntityId == entityId).OrderBy(item => item.CreatedAt).ToArrayAsync();
            Assert.Equal(auditBefore + 1, audits.LongLength);
            using var metadata = JsonDocument.Parse(audits[^1].MetadataJson!);
            Assert.Equal("deny", metadata.RootElement.GetProperty("decision").GetString());
            Assert.Equal(action, metadata.RootElement.GetProperty("targetOperation").GetString());
            Assert.Equal(reason, metadata.RootElement.GetProperty("reasonCode").GetString());
        }
        http.Observe(denied, route, "CURRENT_CONVERSATION_AUTHORITY_REVOKED", HttpStatusCode.BadRequest,
            responseAssertion: "CURRENT_PERMISSION_ERROR_UNCHANGED_MESSAGE_READ_STATE_OUTBOX_WITH_NEW_DENIAL_AUDIT");
    }

    private static async Task<StateSnapshot> StateAsync(string database, Scope scope)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var messages = await db.Messages.IgnoreQueryFilters().AsNoTracking().Where(item => item.ConversationId == scope.Conversation)
            .OrderBy(item => item.Id).Select(item => new { item.Id, item.AuthorUserId, item.ThreadRootMessageId, item.Body,
                item.Version, item.CreatedAt, item.EditedAt, item.DeletedAt, item.DeletedByUserId, item.DeleteReason }).ToArrayAsync();
        var reads = await db.ReadStates.AsNoTracking().Where(item => item.ConversationId == scope.Conversation)
            .OrderBy(item => item.Id).Select(item => new { item.Id, item.UserId, item.LastReadSequence, item.StateVersion,
                item.LastReadItemId, item.LastReadMessageId, item.LastReadAt }).ToArrayAsync();
        var members = await db.ConversationMembers.AsNoTracking().Where(item => item.ConversationId == scope.Conversation)
            .OrderBy(item => item.Id).Select(item => new { item.Id, item.CanRead, item.CanPost,
                item.LastReadMessageId, item.LastReadAt, item.UnreadCursorMessageId }).ToArrayAsync();
        // Compare exact relevant state without placing synthetic row contents in assertion output.
        var digest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { messages, reads, members })));
        return new StateSnapshot(digest, await db.OutboxEvents.LongCountAsync());
    }

    private sealed record StateSnapshot(string StateDigest, long OutboxCount);
}
