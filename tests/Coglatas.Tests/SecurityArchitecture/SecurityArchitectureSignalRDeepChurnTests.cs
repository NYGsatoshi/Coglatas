using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed class SecurityArchitectureSignalRDeepChurnTests
{
    private const string Tenant = SecurityCiFixtureSeed.TenantASlug;
    private const string MessageEvent = "Messaging.MessageCreated.v1";
    private const string BodyMarker = "SEC-17 synthetic bounded Direct churn";
    private const int Cycles = 3;

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "SecurityArchitectureDeep")]
    public Task ConcurrentIndependentDirectScopeTransitionsReauthorizeEveryBoundedReconnectCycle() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await AssertTypedIdentityCannotBeOverwrittenAsync();
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var ownerClient = await app.LoginAsync("owner", Tenant, SecurityCiFixtureSeed.TenantAOwnerEmail);
            using var memberClient = await app.LoginAsync("member", Tenant, SecurityCiFixtureSeed.TenantAMemberEmail);
            var first = await SecurityArchitectureSignalRTests.ScopeAsync(database, Tenant, workspaceDirect: true);
            var second = await IndependentDirectAsync(database, first.Tenant);
            Assert.True(first.Workspace != second.Workspace, "The synthetic Workspaces must be independent.");
            Assert.True(first.Conversation != second.Conversation, "The synthetic Direct scopes must be independent.");
            var conversations = new[] { first.Conversation, second.Conversation };
            await using var owner = await app.ConnectAsync(ownerClient, "owner", Tenant);
            await using var established = await app.ConnectAsync(memberClient, "member", Tenant);
            foreach (var conversation in conversations)
            {
                Assert.True(await owner.SubscribeAsync("SubscribeConversation", conversation));
                Assert.True(await established.SubscribeAsync("SubscribeConversation", conversation));
                var initial = await PostAndDeliverAsync(app, ownerClient, database, conversation, owner, established);
                await AssertHttpReadAsync(memberClient, conversation, initial.Message, allowed: true);
            }

            var completed = 0;
            for (var cycle = 0; cycle < Cycles; cycle++)
            {
                // This connection is closed after each complete deny/restore cycle.
                // The established connection retains both old subscriptions.
                await using var fresh = await app.ConnectAsync(memberClient, "member", Tenant);
                foreach (var conversation in conversations)
                {
                    Assert.True(await fresh.SubscribeAsync("SubscribeConversation", conversation));
                    await PostAndDeliverAsync(app, ownerClient, database, conversation, owner, established, fresh);
                }

                await ChangeBothAfterPreparedBarrierAsync(database, first.Workspace, second.Conversation, authorized: false);
                // Only post-commit events are asserted; no ordering of concurrent commits is promised.
                var denied = new List<(Guid Conversation, Guid Message, Guid Event)>();
                foreach (var conversation in conversations)
                {
                    var sent = await PostAndDeliverAsync(app, ownerClient, database, conversation, owner);
                    denied.Add((conversation, sent.Message, sent.Event));
                    await AssertHttpReadAsync(memberClient, conversation, sent.Message, allowed: false);
                    await AssertHttpReadAsync(ownerClient, conversation, sent.Message, allowed: true);
                }
                // Keep old subscriptions untouched during passive observation. A second
                // reconnect after committed denial must also fail current subscription checks.
                await using var deniedReconnect = await app.ConnectAsync(memberClient, "member", Tenant);
                foreach (var conversation in conversations)
                    await AssertDeniedSubscriptionAsync(deniedReconnect, conversation);
                foreach (var socket in new[] { owner, established, fresh, deniedReconnect })
                    Assert.True(await socket.SubscribeAsync("SubscribeUser"));
                await Task.WhenAll(denied.SelectMany(sent => new[] { established, fresh, deniedReconnect }
                    .Select(socket => socket.AssertNotReceivedAsync(sent.Event))));
                foreach (var conversation in conversations)
                {
                    await AssertDeniedSubscriptionAsync(established, conversation);
                    await AssertDeniedSubscriptionAsync(fresh, conversation);
                }

                await ChangeBothAfterPreparedBarrierAsync(database, first.Workspace, second.Conversation, authorized: true);
                foreach (var conversation in conversations)
                {
                    foreach (var socket in new[] { established, fresh, deniedReconnect })
                        Assert.True(await socket.SubscribeAsync("SubscribeConversation", conversation));
                    var restored = await PostAndDeliverAsync(app, ownerClient, database, conversation,
                        owner, established, fresh, deniedReconnect);
                    await AssertHttpReadAsync(memberClient, conversation, restored.Message, allowed: true);
                    // A restored live receiver must still have no denied event, even after
                    // a later authorized event has traversed that same transport.
                    foreach (var sent in denied)
                        foreach (var socket in new[] { established, fresh, deniedReconnect })
                            Assert.False(socket.Received(sent.Event));
                }
                completed++;
            }
            Assert.Equal(Cycles, completed);
        });

    private static async Task AssertTypedIdentityCannotBeOverwrittenAsync()
    {
        await using var recorder = new RealtimeSocket();
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var message = Guid.NewGuid();
        var conversation = Guid.NewGuid();
        recorder.RecordIdentity(id, tenant, "Message", message, conversation);
        recorder.RecordIdentity(id, tenant, "Message", message, conversation);
        // A wrong earlier identity cannot be erased by a later correct duplicate.
        Assert.Throws<InvalidOperationException>(() => recorder.RecordIdentity(id, Guid.NewGuid(), "Message", message, conversation));
        Assert.Throws<InvalidOperationException>(() => recorder.RecordIdentity(id, tenant, "Other", message, conversation));
        Assert.Throws<InvalidOperationException>(() => recorder.RecordIdentity(id, tenant, "Message", Guid.NewGuid(), conversation));
        Assert.Throws<InvalidOperationException>(() => recorder.RecordIdentity(id, tenant, "Message", message, Guid.NewGuid()));
        recorder.RecordIdentity(id, tenant, "Message", message, conversation);
    }

    private static async Task ChangeBothAfterPreparedBarrierAsync(
        string database, Guid workspace, Guid conversation, bool authorized)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new[] {
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var transitions = new[] { TransitionAsync(0), TransitionAsync(1) };
        try
        {
            // Both independent transactions must have changed their rows while still
            // uncommitted. Merely starting two Tasks is insufficient evidence.
            await Task.WhenAll(ready.Select(item => item.Task)).WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            release.TrySetResult();
            // Always observe/finish both tasks before disposing their database fixture.
            await Task.WhenAll(transitions);
        }

        async Task TransitionAsync(int index)
        {
            try
            {
                await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
                await using var transaction = await db.Database.BeginTransactionAsync();
                if (index == 0)
                    Assert.Equal(1, await db.WorkspaceMembers.Where(row => row.WorkspaceId == workspace &&
                        row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId).ExecuteUpdateAsync(update =>
                        update.SetProperty(row => row.Status, authorized ? MembershipStatus.Active : MembershipStatus.Suspended)));
                else
                    Assert.Equal(1, await db.ConversationMembers.Where(row => row.ConversationId == conversation &&
                        row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId).ExecuteUpdateAsync(update =>
                        update.SetProperty(row => row.CanRead, authorized)));
                ready[index].TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(20));
                await transaction.CommitAsync();
            }
            catch (Exception exception)
            {
                ready[index].TrySetException(exception);
                throw;
            }
        }
    }

    private static async Task<(Guid Workspace, Guid Conversation)> IndependentDirectAsync(string database, Guid tenant)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var owner = SecurityCiFixtureSeed.TenantAOwnerUserId;
        var workspace = new Workspace { TenantId = tenant, Name = "SEC-17 independent synthetic Workspace",
            Slug = "sec17-deep-" + Guid.NewGuid().ToString("N"), CreatedByUserId = owner, Status = WorkspaceStatus.Active };
        var conversation = new Conversation { TenantId = tenant, WorkspaceId = workspace.Id,
            Type = ConversationType.DirectMessage, Visibility = ConversationVisibility.Private,
            Title = "SEC-17 independent synthetic Direct", CreatedByUserId = owner };
        db.AddRange(workspace, conversation);
        foreach (var user in new[] { owner, SecurityCiFixtureSeed.TenantAMemberUserId })
        {
            db.WorkspaceMembers.Add(new WorkspaceMember { TenantId = tenant, WorkspaceId = workspace.Id, UserId = user,
                Role = user == owner ? WorkspaceRole.Owner : WorkspaceRole.Member,
                Status = MembershipStatus.Active, JoinedAt = DateTimeOffset.UtcNow });
            db.ConversationMembers.Add(new ConversationMember { TenantId = tenant, ConversationId = conversation.Id,
                UserId = user, Role = user == owner ? ConversationMemberRole.Admin : ConversationMemberRole.Member,
                CanRead = true, CanPost = true, JoinedAt = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync();
        return (workspace.Id, conversation.Id);
    }

    private static async Task<(Guid Message, Guid Event)> PostAndDeliverAsync(
        SecurityArchitectureSignalRFixture app, HttpClient sender, string database, Guid conversation,
        params RealtimeSocket[] receivers)
    {
        var request = Guid.NewGuid();
        using var response = await sender.PostAsJsonAsync($"/api/conversations/{conversation}/messages",
            new { body = BodyMarker + " " + Guid.NewGuid().ToString("N"), clientRequestId = request });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(conversation == json.RootElement.GetProperty("conversationId").GetGuid(), "The HTTP message must belong to the requested Direct.");
        var message = json.RootElement.GetProperty("id").GetGuid();
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var persisted = await db.Messages.AsNoTracking().SingleAsync(row => row.Id == message);
        var scope = await db.Conversations.AsNoTracking().Where(row => row.Id == conversation)
            .Select(row => new { row.TenantId, row.WorkspaceId, row.ProjectId, row.Type }).SingleAsync();
        Assert.True(persisted.ClientRequestId == request && persisted.ConversationId == conversation &&
            persisted.TenantId == scope.TenantId && persisted.WorkspaceId == scope.WorkspaceId &&
            scope.ProjectId is null && scope.Type == ConversationType.DirectMessage &&
            persisted.AuthorUserId == SecurityCiFixtureSeed.TenantAOwnerUserId &&
            persisted.ThreadRootMessageId is null, "The persisted root must match the actual HTTP request and scope.");
        var durable = await db.OutboxEvents.AsNoTracking().SingleAsync(row => row.EventType == MessageEvent &&
            row.AggregateId == message);
        var eventId = durable.Id;
        using var payload = JsonDocument.Parse(durable.PayloadJson);
        var stored = payload.RootElement;
        AssertEnvelope(stored, persisted.TenantId, message, conversation);
        Assert.True(durable.TenantId == persisted.TenantId && durable.PayloadSchemaVersion == 1 &&
            durable.AggregateType == "Message", "The durable event row must match its current Message.");
        Assert.NotEmpty(receivers);
        foreach (var socket in receivers)
        {
            await socket.WaitEventAsync(eventId);
            Assert.True(socket.Received(eventId, MessageEvent));
            Assert.True(socket.ReceivedMessageIdentity(eventId, persisted.TenantId, message, conversation),
                "The received native identity must match the persisted current Message, Tenant and Direct resource.");
        }
        await app.WaitDeliveredAsync(eventId);
        return (message, eventId);
    }

    private static void AssertEnvelope(JsonElement envelope, Guid tenant, Guid message, Guid conversation)
    {
        var payload = envelope.GetProperty("payload");
        Assert.True(envelope.GetProperty("eventType").GetString() == MessageEvent &&
            envelope.GetProperty("payloadSchemaVersion").GetInt32() == 1 &&
            envelope.GetProperty("tenantId").GetGuid() == tenant &&
            envelope.GetProperty("aggregateType").GetString() == "Message" &&
            envelope.GetProperty("aggregateId").GetGuid() == message &&
            payload.GetProperty("message").GetProperty("conversationId").GetGuid() == conversation &&
            payload.GetProperty("message").GetProperty("id").GetGuid() == message,
            "The native envelope must match the persisted current Message, Tenant and Direct resource.");
    }

    private static async Task AssertHttpReadAsync(HttpClient client, Guid conversation, Guid message, bool allowed)
    {
        foreach (var path in new[] { $"/api/conversations/{conversation}",
            $"/api/conversations/{conversation}/messages?anchorMessageId={message}" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.BadRequest, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!allowed)
            {
                Assert.True(json.RootElement.EnumerateObject().Count() == 1, "Denied HTTP must expose only its generic error.");
                Assert.True(json.RootElement.GetProperty("error").GetString() == "Conversation not found.", "Denied HTTP reads must retain the generic error.");
            }
            else if (path.Contains("/messages", StringComparison.Ordinal))
            {
                Assert.True(json.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array,
                    "Authorized HTTP catch-up must return the current bounded Message list.");
                Assert.True(items.EnumerateArray().Any(row => row.GetProperty("id").GetGuid() == message &&
                    row.GetProperty("body").GetString()!.StartsWith(BodyMarker, StringComparison.Ordinal)),
                    "Authorized HTTP catch-up must contain the persisted synthetic Message.");
            }
            else
                Assert.True(json.RootElement.GetProperty("id").GetGuid() == conversation,
                    "Authorized HTTP detail must resolve the exact current Direct.");
        }
    }

    private static async Task AssertDeniedSubscriptionAsync(RealtimeSocket socket, Guid conversation)
    {
        var result = await socket.InvokeAsync("SubscribeConversation", conversation);
        Assert.False(result.GetProperty("allowed").GetBoolean());
        Assert.True(result.GetProperty("code").GetString() == "AccessDenied", "The current subscription must be denied generically.");
        Assert.True(result.EnumerateObject().All(property => property.Name is "allowed" or "code"),
            "Denied subscriptions must expose only generic result fields.");
    }
}
