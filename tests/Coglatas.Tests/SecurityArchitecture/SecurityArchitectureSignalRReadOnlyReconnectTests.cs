using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Scope = Coglatas.Tests.SecurityArchitecture.SecurityArchitectureSignalRTests.Scope;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed class SecurityArchitectureSignalRReadOnlyReconnectTests
{
    [PostgreSqlFact]
    public async Task ReadOnlyReconnectRetainsReadsButDeniesThreadWriteUntilCurrentRoleRestoration()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var ownerClient = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAOwnerEmail);
            using var memberClient = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
            var scope = await SecurityArchitectureSignalRTests.ScopeAsync(database, SecurityCiFixtureSeed.TenantASlug);
            await using var owner = await app.ConnectAsync(ownerClient, "owner", SecurityCiFixtureSeed.TenantASlug);
            await using var retained = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug);
            await SubscribeAsync(owner, scope);
            await SubscribeAsync(retained, scope);

            var root = await PostRootAsync(ownerClient, database, scope);
            await AssertDeliveryAsync(app, root.Event, "Messaging.MessageCreated.v1", owner, retained);
            var original = await PostReplyAsync(memberClient, database, root.Message);
            await AssertReplyDeliveryAsync(app, original, owner, retained);

            // Only this isolated fixture participant changes. The real HTTP/Hub
            // authorizers, cookie/session validation and dispatcher are unchanged.
            await SetPostingRoleAsync(database, scope, readOnly: true);
            await using var reconnected = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug);
            await SubscribeAsync(retained, scope);
            await SubscribeAsync(reconnected, scope);
            await AssertThreadReadAsync(memberClient, scope, root.Message, original.Message);
            await AssertDeniedReplyAsync(memberClient, database, scope, root.Message);

            // ReadOnly retains read authority. Actual owner-produced reply and
            // Thread events must reach both the retained and freshly connected peer.
            var readable = await PostReplyAsync(ownerClient, database, root.Message);
            await AssertReplyDeliveryAsync(app, readable, owner, retained, reconnected);
            await AssertThreadReadAsync(memberClient, scope, root.Message, readable.Message);
            await AssertDeniedReplyAsync(memberClient, database, scope, root.Message);

            await SetPostingRoleAsync(database, scope, readOnly: false);
            await SubscribeAsync(retained, scope);
            await SubscribeAsync(reconnected, scope);
            var restored = await PostReplyAsync(memberClient, database, root.Message);
            await AssertReplyDeliveryAsync(app, restored, owner, retained, reconnected);
            await AssertThreadReadAsync(memberClient, scope, root.Message, restored.Message);
        });
    }

    private static async Task SubscribeAsync(RealtimeSocket socket, Scope scope)
    {
        Assert.True(await socket.SubscribeAsync("SubscribeUser"));
        Assert.True(await socket.SubscribeAsync("SubscribeConversation", scope.Conversation));
    }

    private static async Task SetPostingRoleAsync(string database, Scope scope, bool readOnly)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var member = await db.ConversationMembers.SingleAsync(row => row.ConversationId == scope.Conversation &&
            row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId);
        Assert.True(member.CanRead);
        Assert.True(member.CanCreateThread);
        Assert.Null(member.LeftAt);
        Assert.Null(member.RemovedAt);
        member.Role = readOnly ? ConversationMemberRole.ReadOnly : ConversationMemberRole.Member;
        member.CanPost = !readOnly;
        await db.SaveChangesAsync();
    }

    private static async Task<(Guid Message, Guid Event)> PostRootAsync(HttpClient client, string database, Scope scope)
    {
        using var response = await client.PostAsJsonAsync($"/api/conversations/{scope.Conversation:D}/messages",
            new { body = "Synthetic role reconnect root " + Guid.NewGuid().ToString("N"), clientRequestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(scope.Conversation, body.RootElement.GetProperty("conversationId").GetGuid());
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var eventId = await db.OutboxEvents.AsNoTracking().Where(row => row.AggregateId == id &&
            row.EventType == "Messaging.MessageCreated.v1").Select(row => row.Id).SingleAsync();
        return (id, eventId);
    }

    private sealed record Reply(Guid Message, Guid CreatedEvent, Guid ThreadEvent);

    private static async Task<Reply> PostReplyAsync(HttpClient client, string database, Guid root)
    {
        Guid[] earlier;
        await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            earlier = await db.OutboxEvents.AsNoTracking().Where(row => row.AggregateId == root &&
                row.EventType == "Messaging.ThreadChanged.v1").Select(row => row.Id).ToArrayAsync();
        using var response = await client.PostAsJsonAsync($"/api/messages/{root:D}/thread/messages",
            new { body = "Synthetic role reconnect reply " + Guid.NewGuid().ToString("N"), clientRequestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var message = body.RootElement.GetProperty("message");
        var id = message.GetProperty("id").GetGuid();
        Assert.Equal(root, message.GetProperty("threadRootMessageId").GetGuid());
        await using var context = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var created = await context.OutboxEvents.AsNoTracking().Where(row => row.AggregateId == id &&
            row.EventType == "Messaging.MessageCreated.v1").Select(row => row.Id).SingleAsync();
        var changed = await context.OutboxEvents.AsNoTracking().Where(row => row.AggregateId == root &&
            row.EventType == "Messaging.ThreadChanged.v1" && !earlier.Contains(row.Id)).Select(row => row.Id).SingleAsync();
        return new(id, created, changed);
    }

    private static async Task AssertReplyDeliveryAsync(SecurityArchitectureSignalRFixture app, Reply reply, params RealtimeSocket[] sockets)
    {
        await AssertDeliveryAsync(app, reply.CreatedEvent, "Messaging.MessageCreated.v1", sockets);
        await AssertDeliveryAsync(app, reply.ThreadEvent, "Messaging.ThreadChanged.v1", sockets);
    }

    private static async Task AssertDeliveryAsync(SecurityArchitectureSignalRFixture app, Guid eventId, string type, params RealtimeSocket[] sockets)
    {
        foreach (var socket in sockets)
        {
            await socket.WaitEventAsync(eventId);
            Assert.True(socket.Received(eventId, type));
        }
        await app.WaitDeliveredAsync(eventId);
    }

    private static async Task AssertThreadReadAsync(HttpClient client, Scope scope, Guid root, Guid reply)
    {
        using (var history = await client.GetAsync($"/api/conversations/{scope.Conversation:D}/messages"))
        {
            Assert.Equal(HttpStatusCode.OK, history.StatusCode);
            using var body = JsonDocument.Parse(await history.Content.ReadAsStringAsync());
            Assert.Contains(body.RootElement.GetProperty("items").EnumerateArray(), row => row.GetProperty("id").GetGuid() == root);
        }
        using var response = await client.GetAsync($"/api/messages/{root:D}/thread");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var thread = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(root, thread.RootElement.GetProperty("rootMessage").GetProperty("id").GetGuid());
        Assert.Contains(thread.RootElement.GetProperty("replies").EnumerateArray(), row => row.GetProperty("id").GetGuid() == reply);
    }

    private static async Task AssertDeniedReplyAsync(HttpClient client, string database, Scope scope, Guid root)
    {
        var before = await ProtectedStateAsync(database, scope);
        long auditsBefore;
        await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            auditsBefore = await db.AuditLogs.LongCountAsync(row => row.ActorUserId == SecurityCiFixtureSeed.TenantAMemberUserId &&
                row.Action == "MessageThreadReplyDenied" && row.EntityId == root);
        using var response = await client.PostAsJsonAsync($"/api/messages/{root:D}/thread/messages",
            new { body = "Synthetic denied role reconnect reply", clientRequestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(text.Length < 512);
        using var body = JsonDocument.Parse(text);
        Assert.Single(body.RootElement.EnumerateObject());
        Assert.True(string.Equals("Message thread not found.", body.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal),
            "Thread denial must retain the exact masked error.");
        Assert.Null(response.Headers.Location);
        Assert.Null(response.Content.Headers.ContentDisposition);
        Assert.Equal(before, await ProtectedStateAsync(database, scope));
        await using var context = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var audits = await context.AuditLogs.AsNoTracking().Where(row => row.ActorUserId == SecurityCiFixtureSeed.TenantAMemberUserId &&
            row.Action == "MessageThreadReplyDenied" && row.EntityId == root).OrderBy(row => row.CreatedAt).ToArrayAsync();
        Assert.Equal(auditsBefore + 1, audits.LongLength);
        using var metadata = JsonDocument.Parse(audits[^1].MetadataJson!);
        Assert.True(string.Equals("deny", metadata.RootElement.GetProperty("decision").GetString(), StringComparison.Ordinal),
            "Thread denial must retain the exact audit decision.");
        Assert.True(string.Equals("conversation_post_denied", metadata.RootElement.GetProperty("reasonCode").GetString(), StringComparison.Ordinal),
            "Thread denial must retain the exact audit reason.");
    }

    private static async Task<string> ProtectedStateAsync(string database, Scope scope)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var messages = await db.Messages.IgnoreQueryFilters().AsNoTracking().Where(row => row.ConversationId == scope.Conversation)
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.Body, row.Version, row.ThreadRootMessageId,
                row.AuthorUserId, row.DeletedAt, row.EditedAt }).ToArrayAsync();
        var members = await db.ConversationMembers.AsNoTracking().Where(row => row.ConversationId == scope.Conversation)
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.Role, row.CanRead, row.CanPost, row.CanCreateThread,
                row.LeftAt, row.RemovedAt }).ToArrayAsync();
        var events = await db.OutboxEvents.AsNoTracking().OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync();
        // Hash protected snapshots so assertion diagnostics expose no bodies or routing.
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { messages, members, events })));
    }
}
