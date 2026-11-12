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
            var original = await PostReplyAsync(memberClient, database, root.Message, SecurityCiFixtureSeed.TenantAMemberUserId);
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
            var readable = await PostReplyAsync(ownerClient, database, root.Message, SecurityCiFixtureSeed.TenantAOwnerUserId);
            await AssertReplyDeliveryAsync(app, readable, owner, retained, reconnected);
            await AssertThreadReadAsync(memberClient, scope, root.Message, readable.Message);
            await AssertDeniedReplyAsync(memberClient, database, scope, root.Message);

            await SetPostingRoleAsync(database, scope, readOnly: false);
            await SubscribeAsync(retained, scope);
            await SubscribeAsync(reconnected, scope);
            var restored = await PostReplyAsync(memberClient, database, root.Message, SecurityCiFixtureSeed.TenantAMemberUserId);
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
        Assert.True(member.LeftAt is null, "The fixture participant must remain joined.");
        Assert.True(member.RemovedAt is null, "The fixture participant must remain admitted.");
        member.Role = readOnly ? ConversationMemberRole.ReadOnly : ConversationMemberRole.Member;
        member.CanPost = !readOnly;
        await db.SaveChangesAsync();
    }

    private static async Task<(Guid Message, Guid Event)> PostRootAsync(HttpClient client, string database, Scope scope)
    {
        Guid[] previousMessages;
        Guid[] previousEvents;
        await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
        {
            previousMessages = await db.Messages.IgnoreQueryFilters().AsNoTracking().Select(row => row.Id).ToArrayAsync();
            previousEvents = await db.OutboxEvents.AsNoTracking().Select(row => row.Id).ToArrayAsync();
        }
        var requestId = Guid.NewGuid();
        using var response = await client.PostAsJsonAsync($"/api/conversations/{scope.Conversation:D}/messages",
            new { body = "Synthetic role reconnect root " + Guid.NewGuid().ToString("N"), clientRequestId = requestId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("id").GetGuid();
        Assert.True(scope.Conversation == body.RootElement.GetProperty("conversationId").GetGuid(),
            "The root response must belong to the current Conversation.");
        Assert.False(previousMessages.Contains(id), "The root POST must create a new Message identity.");
        await using var context = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var message = await context.Messages.AsNoTracking().SingleAsync(row => row.Id == id);
        Assert.True(message.ClientRequestId == requestId && message.AuthorUserId == SecurityCiFixtureSeed.TenantAOwnerUserId &&
            message.TenantId == scope.Tenant && message.WorkspaceId == scope.Workspace &&
            message.ConversationId == scope.Conversation && message.ThreadRootMessageId is null,
            "The new persisted root must match this request, actor and scope.");
        var eventId = await context.OutboxEvents.AsNoTracking().Where(row => row.AggregateId == id &&
            row.EventType == "Messaging.MessageCreated.v1" && !previousEvents.Contains(row.Id)).Select(row => row.Id).SingleAsync();
        return (id, eventId);
    }

    private sealed record Reply(Guid Message, Guid CreatedEvent, Guid ThreadEvent);

    private static async Task<Reply> PostReplyAsync(HttpClient client, string database, Guid root, Guid author)
    {
        Guid[] previousMessages;
        Guid[] previousEvents;
        Guid tenant;
        Guid workspace;
        Guid conversation;
        await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
        {
            var rootMessage = await db.Messages.AsNoTracking().SingleAsync(row => row.Id == root);
            tenant = rootMessage.TenantId;
            workspace = rootMessage.WorkspaceId;
            conversation = rootMessage.ConversationId;
            previousMessages = await db.Messages.IgnoreQueryFilters().AsNoTracking().Select(row => row.Id).ToArrayAsync();
            previousEvents = await db.OutboxEvents.AsNoTracking().Select(row => row.Id).ToArrayAsync();
        }
        var requestId = Guid.NewGuid();
        using var response = await client.PostAsJsonAsync($"/api/messages/{root:D}/thread/messages",
            new { body = "Synthetic role reconnect reply " + Guid.NewGuid().ToString("N"), clientRequestId = requestId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var message = body.RootElement.GetProperty("message");
        var id = message.GetProperty("id").GetGuid();
        Assert.True(root == message.GetProperty("threadRootMessageId").GetGuid(),
            "The reply response must retain the current Thread root.");
        Assert.False(previousMessages.Contains(id), "Each reply POST must create a new Message identity.");
        await using var context = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var persisted = await context.Messages.AsNoTracking().SingleAsync(row => row.Id == id);
        Assert.True(persisted.ClientRequestId == requestId && persisted.AuthorUserId == author &&
            persisted.TenantId == tenant && persisted.WorkspaceId == workspace &&
            persisted.ConversationId == conversation && persisted.ThreadRootMessageId == root,
            "The new persisted reply must match this request, actor, root and scope.");
        var created = await context.OutboxEvents.AsNoTracking().Where(row => row.AggregateId == id &&
            row.EventType == "Messaging.MessageCreated.v1" && !previousEvents.Contains(row.Id)).Select(row => row.Id).SingleAsync();
        var changed = await context.OutboxEvents.AsNoTracking().Where(row => row.AggregateId == root &&
            row.EventType == "Messaging.ThreadChanged.v1" && !previousEvents.Contains(row.Id)).Select(row => row.Id).SingleAsync();
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
            Assert.True(body.RootElement.GetProperty("items").EnumerateArray().Any(row => row.GetProperty("id").GetGuid() == root),
                "Authorized history must contain the current root.");
        }
        using var response = await client.GetAsync($"/api/messages/{root:D}/thread");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var thread = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(root == thread.RootElement.GetProperty("rootMessage").GetProperty("id").GetGuid(),
            "The authorized Thread must retain the current root.");
        Assert.True(thread.RootElement.GetProperty("replies").EnumerateArray().Any(row => row.GetProperty("id").GetGuid() == reply),
            "The authorized Thread must contain the newly persisted reply.");
    }

    private static async Task AssertDeniedReplyAsync(HttpClient client, string database, Scope scope, Guid root)
    {
        var before = await ProtectedStateAsync(database, scope);
        Guid[] auditsBefore;
        await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            auditsBefore = await db.AuditLogs.AsNoTracking().Where(row => row.ActorUserId == SecurityCiFixtureSeed.TenantAMemberUserId &&
                row.Action == "MessageThreadReplyDenied" && row.EntityType == "Message" && row.EntityId == root)
                .Select(row => row.Id).ToArrayAsync();
        using var response = await client.PostAsJsonAsync($"/api/messages/{root:D}/thread/messages",
            new { body = "Synthetic denied role reconnect reply", clientRequestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(text.Length < 512);
        using var body = JsonDocument.Parse(text);
        Assert.True(body.RootElement.EnumerateObject().Count() == 1, "The denial must retain its single-property masked envelope.");
        Assert.True(string.Equals("Message thread not found.", body.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal),
            "Thread denial must retain the exact masked error.");
        Assert.True(response.Headers.Location is null, "A denied reply must not expose a redirect location.");
        Assert.True(response.Content.Headers.ContentDisposition is null, "A denied reply must not expose download metadata.");
        Assert.Equal(before, await ProtectedStateAsync(database, scope));
        await using var context = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var audits = await context.AuditLogs.AsNoTracking().Where(row => row.ActorUserId == SecurityCiFixtureSeed.TenantAMemberUserId &&
            row.Action == "MessageThreadReplyDenied" && row.EntityType == "Message" && row.EntityId == root).ToArrayAsync();
        var created = audits.Where(row => !auditsBefore.Contains(row.Id)).ToArray();
        Assert.True(audits.Length == auditsBefore.Length + 1 && created.Length == 1,
            "Each denied reply must append exactly one new matching audit identity.");
        using var metadata = JsonDocument.Parse(created[0].MetadataJson!);
        Assert.True(string.Equals("deny", metadata.RootElement.GetProperty("decision").GetString(), StringComparison.Ordinal),
            "Thread denial must retain the exact audit decision.");
        Assert.True(string.Equals("conversation_post_denied", metadata.RootElement.GetProperty("reasonCode").GetString(), StringComparison.Ordinal),
            "Thread denial must retain the exact audit reason.");
    }

    private static async Task<string> ProtectedStateAsync(string database, Scope scope)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var messages = await db.Messages.IgnoreQueryFilters().AsNoTracking().Where(row => row.ConversationId == scope.Conversation)
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.TenantId, row.WorkspaceId, row.ConversationId,
                row.AuthorUserId, row.ClientRequestId, row.ThreadRootMessageId, row.Version, row.Body,
                row.EditedAt, row.CreatedAt, row.UpdatedAt, row.DeletedAt, row.DeletedByUserId, row.DeleteReason }).ToArrayAsync();
        var members = await db.ConversationMembers.AsNoTracking().Where(row => row.ConversationId == scope.Conversation)
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.TenantId, row.ConversationId, row.UserId,
                row.Role, row.CanRead, row.CanPost, row.CanManageMembers, row.CanCreateThread, row.JoinedAt,
                row.LeftAt, row.RemovedAt, row.RemovedByUserId, row.LastOpenedAt, row.LastReadMessageId,
                row.LastReadAt, row.UnreadCursorMessageId, row.IsMuted, row.IsArchived, row.IsLater,
                row.CreatedAt, row.UpdatedAt }).ToArrayAsync();
        var events = await db.OutboxEvents.AsNoTracking().OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync();
        // Hash protected snapshots so assertion diagnostics expose no bodies or routing.
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { messages, members, events })));
    }
}
