using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.SecurityArchitecture;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed class DirectConversationSelfLeaveTransportPostgreSqlTests
{
    private const string Tenant = SecurityCiFixtureSeed.TenantASlug;
    private const string MessageEvent = "Messaging.MessageCreated.v1";
    private const string BodyMarker = "Synthetic Direct self-leave body";

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task ActiveDirectSelfLeavePersistsAndDeniesSameSessionHttpAndEstablishedAndReconnectingTransport() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var ownerClient = await app.LoginAsync("owner", Tenant, SecurityCiFixtureSeed.TenantAOwnerEmail);
            using var memberClient = await app.LoginAsync("member", Tenant, SecurityCiFixtureSeed.TenantAMemberEmail);
            Guid workspace;
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                workspace = await db.Workspaces.Where(row => row.Slug == SecurityCiFixtureSeed.TenantAWorkspaceSlug).Select(row => row.Id).SingleAsync();
            using var created = await ownerClient.PostAsJsonAsync("/api/conversations", new
            {
                type = (int)ConversationType.DirectMessage, workspaceId = workspace,
                memberUserIds = new[] { SecurityCiFixtureSeed.TenantAMemberUserId }
            });
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            using var detail = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            var direct = detail.RootElement.GetProperty("id").GetGuid();
            Assert.Equal("DirectMessage", detail.RootElement.GetProperty("type").GetString());
            Assert.Equal(workspace, detail.RootElement.GetProperty("workspaceId").GetGuid());
            Assert.Equal(JsonValueKind.Null, detail.RootElement.GetProperty("projectId").ValueKind);
            Assert.Equal(2, detail.RootElement.GetProperty("members").GetArrayLength());

            await using var owner = await app.ConnectAsync(ownerClient, "owner", Tenant);
            await using var established = await app.ConnectAsync(memberClient, "member", Tenant);
            foreach (var socket in new[] { owner, established })
            {
                Assert.True(await socket.SubscribeAsync("SubscribeUser"));
                Assert.True(await socket.SubscribeAsync("SubscribeConversation", direct));
            }
            var beforeLeave = await PostAndDeliverAsync(app, ownerClient, database, direct, owner, established);
            await AssertReadableAsync(memberClient, direct, beforeLeave.Message);
            var memberPositive = await PostAndDeliverAsync(app, memberClient, database, direct, owner, established);
            await AssertReadableAsync(ownerClient, direct, memberPositive.Message);

            var leaveStarted = DateTimeOffset.UtcNow;
            using (var leave = await memberClient.PostAsync($"/api/conversations/{direct}/leave", null))
                Assert.Equal(HttpStatusCode.OK, leave.StatusCode);
            var leaveCompleted = DateTimeOffset.UtcNow;
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                var departed = await db.ConversationMembers.AsNoTracking().SingleAsync(row => row.ConversationId == direct && row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId);
                Assert.NotNull(departed.LeftAt);
                Assert.InRange(departed.LeftAt.Value, leaveStarted, leaveCompleted);
                Assert.Null(departed.RemovedAt);
                Assert.Null(departed.RemovedByUserId);
                Assert.True(departed.CanRead);
                Assert.True(departed.CanPost);
                var peer = await db.ConversationMembers.AsNoTracking().SingleAsync(row => row.ConversationId == direct && row.UserId == SecurityCiFixtureSeed.TenantAOwnerUserId);
                Assert.Null(peer.LeftAt);
                Assert.Null(peer.RemovedAt);
                Assert.Equal(MembershipStatus.Active, await db.WorkspaceMembers.Where(row => row.WorkspaceId == workspace && row.UserId == departed.UserId).Select(row => row.Status).SingleAsync());
                Assert.Single(await db.AuditLogs.AsNoTracking().Where(row => row.ActorUserId == departed.UserId && row.EntityId == direct &&
                    row.Action == "ConversationMemberLeft" && row.CreatedAt >= leaveStarted && row.CreatedAt <= leaveCompleted).ToListAsync());
            }
            // Keep the same authenticated cookie jar: only participation changed.
            using (var currentSession = await memberClient.GetAsync("/api/auth/me"))
                Assert.Equal(HttpStatusCode.OK, currentSession.StatusCode);
            foreach (var path in new[] { $"/api/conversations/{direct}", $"/api/conversations/{direct}/messages" })
            {
                using var denied = await memberClient.GetAsync(path);
                await AssertMaskedErrorAsync(denied, "Conversation not found.");
            }
            var deniedRequest = Guid.NewGuid();
            using (var denied = await memberClient.PostAsJsonAsync($"/api/conversations/{direct}/messages", new
            { body = BodyMarker, clientRequestId = deniedRequest }))
                await AssertMaskedErrorAsync(denied, "You are not allowed to send messages.");
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                Assert.False(await db.Messages.AnyAsync(row => row.ConversationId == direct && row.ClientRequestId == deniedRequest));

            await using var reconnected = await app.ConnectAsync(memberClient, "member", Tenant);
            Assert.True(await reconnected.SubscribeAsync("SubscribeUser"));
            await AssertSubscriptionDeniedAsync(reconnected, direct);
            // Leave the old conversation subscription untouched until the actual
            // dispatcher has delivered a peer's new business event. Both receivers
            // must stay open throughout the existing bounded passive observation.
            var afterLeave = await PostAndDeliverAsync(app, ownerClient, database, direct, owner);
            await Task.WhenAll(established.AssertNotReceivedAsync(afterLeave.Event), reconnected.AssertNotReceivedAsync(afterLeave.Event));
            await AssertSubscriptionDeniedAsync(established, direct);
            Assert.True(await established.SubscribeAsync("SubscribeUser"));
            await AssertReadableAsync(ownerClient, direct, beforeLeave.Message);
            await AssertReadableAsync(ownerClient, direct, afterLeave.Message);
        });

    private static async Task<(Guid Message, Guid Event)> PostAndDeliverAsync(
        SecurityArchitectureSignalRFixture app, HttpClient sender, string database, Guid direct, params RealtimeSocket[] receivers)
    {
        using var response = await sender.PostAsJsonAsync($"/api/conversations/{direct}/messages", new
        { body = BodyMarker + " " + Guid.NewGuid().ToString("N"), clientRequestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var message = json.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(direct, json.RootElement.GetProperty("conversationId").GetGuid());
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var eventId = await db.OutboxEvents.Where(row => row.EventType == MessageEvent && row.AggregateId == message).Select(row => row.Id).SingleAsync();
        Assert.NotEmpty(receivers);
        foreach (var receiver in receivers)
        {
            await receiver.WaitEventAsync(eventId);
            Assert.True(receiver.Received(eventId, MessageEvent));
        }
        await app.WaitDeliveredAsync(eventId);
        return (message, eventId);
    }

    private static async Task AssertReadableAsync(HttpClient client, Guid direct, Guid message)
    {
        using (var detail = await client.GetAsync($"/api/conversations/{direct}"))
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var response = await client.GetAsync($"/api/conversations/{direct}/messages?anchorMessageId={message}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains(json.RootElement.GetProperty("items").EnumerateArray(), row =>
            row.GetProperty("id").GetGuid() == message && row.GetProperty("body").GetString()!.StartsWith(BodyMarker, StringComparison.Ordinal));
    }

    private static async Task AssertMaskedErrorAsync(HttpResponseMessage response, string error)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Single(json.RootElement.EnumerateObject());
        Assert.Equal(error, json.RootElement.GetProperty("error").GetString());
        Assert.DoesNotContain(BodyMarker, body, StringComparison.Ordinal);
    }

    private static async Task AssertSubscriptionDeniedAsync(RealtimeSocket socket, Guid direct)
    {
        var result = await socket.InvokeAsync("SubscribeConversation", direct);
        Assert.False(result.GetProperty("allowed").GetBoolean());
        Assert.Equal("AccessDenied", result.GetProperty("code").GetString());
    }
}
