using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.SecurityArchitecture;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed class PrivateChannelAccountSuspensionTransportPostgreSqlTests
{
    private const string Tenant = SecurityCiFixtureSeed.TenantASlug;
    private const string EventType = "Messaging.MessageCreated.v1";
    private const string ProtectedBody = "Synthetic private channel suspended account body";

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task SuspendedAccountWithUnchangedSessionAndMembershipCannotReceivePrivateChannelDeliveryReplayOrReconnect() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var ownerClient = await app.LoginAsync("owner", Tenant, SecurityCiFixtureSeed.TenantAOwnerEmail);
            using var memberClient = await app.LoginAsync("member", Tenant, SecurityCiFixtureSeed.TenantAMemberEmail);
            Guid workspace;
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                workspace = await db.Workspaces.Where(row => row.Slug == SecurityCiFixtureSeed.TenantAWorkspaceSlug)
                    .Select(row => row.Id).SingleAsync();
            var channel = await CreatePrivateChannelAsync(ownerClient, workspace);
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                var scope = await db.Conversations.AsNoTracking().Where(row => row.Id == channel)
                    .Select(row => new { row.TenantId, row.WorkspaceId, row.Type, row.Visibility }).SingleAsync();
                var tenant = await db.Tenants.Where(row => row.Slug == Tenant).Select(row => row.Id).SingleAsync();
                Assert.Equal(tenant, scope.TenantId);
                Assert.Equal(workspace, scope.WorkspaceId);
                Assert.Equal(ConversationType.WorkspaceChannel, scope.Type);
                Assert.Equal(ConversationVisibility.Private, scope.Visibility);
            }
            using (var invited = await ownerClient.PostAsJsonAsync($"/api/workspaces/{workspace}/chat-channels/{channel}/members",
                new { userId = SecurityCiFixtureSeed.TenantAMemberUserId }))
                Assert.Equal(HttpStatusCode.OK, invited.StatusCode);

            // Creation and invitation stage real authorization invalidations.
            // Fence setup completion before establishing subscriptions so those
            // earlier invalidations cannot remove a newly installed baseline.
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                var initialization = await db.OutboxEvents.AsNoTracking()
                    .Where(row => row.EventType == "Security.AuthorizationStateChanged.v1")
                    .Select(row => row.Id).ToListAsync();
                Assert.NotEmpty(initialization);
                foreach (var eventId in initialization) await app.WaitDeliveredAsync(eventId);
            }

            await using var owner = await app.ConnectAsync(ownerClient, "owner", Tenant);
            await using var established = await app.ConnectAsync(memberClient, "member", Tenant);
            foreach (var socket in new[] { owner, established })
            {
                Assert.True(await socket.SubscribeAsync("SubscribeUser"));
                Assert.True(await socket.SubscribeAsync("SubscribeConversation", channel));
            }
            var initial = await PostAsync(ownerClient, database, channel);
            await DeliveredAsync(app, initial.Event, owner, established);
            Assert.True(established.DeliveryCount(initial.Event) > 0);
            using (var history = await memberClient.GetAsync($"/api/conversations/{channel}/messages"))
            {
                Assert.Equal(HttpStatusCode.OK, history.StatusCode);
                Assert.Contains(ProtectedBody, await history.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
            using (var thread = await memberClient.GetAsync($"/api/messages/{initial.Message}/thread"))
            {
                Assert.Equal(HttpStatusCode.OK, thread.StatusCode);
                Assert.Contains(ProtectedBody, await thread.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }

            var unchangedAuthority = await AuthoritySnapshotAsync(database, channel, workspace);
            // Only the synthetic account changes. No session, grant, subscription,
            // runtime authorizer or invalidation service is replaced or modified.
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                Assert.Equal(UserStatus.Active, await db.Users.Where(row => row.Id == SecurityCiFixtureSeed.TenantAMemberUserId)
                    .Select(row => row.Status).SingleAsync());
                Assert.Equal(1, await db.Users.Where(row => row.Id == SecurityCiFixtureSeed.TenantAMemberUserId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, UserStatus.Suspended)));
                Assert.Equal(UserStatus.Suspended, await db.Users.Where(row => row.Id == SecurityCiFixtureSeed.TenantAMemberUserId)
                    .Select(row => row.Status).SingleAsync());
            }
            Assert.Equal(unchangedAuthority, await AuthoritySnapshotAsync(database, channel, workspace));

            var denied = await PostAsync(ownerClient, database, channel);
            await DeliveredAsync(app, denied.Event, owner);
            // Observe passive exclusion before any new invocation can abort the
            // suspended client's original, previously subscribed connection.
            await established.AssertNotReceivedAsync(denied.Event);
            Assert.True(established.DeliveryCount(initial.Event) > 0);

            var ownerBeforeReplay = owner.DeliveryCount(denied.Event);
            Assert.True(ownerBeforeReplay > 0);
            var replayStarted = DateTimeOffset.UtcNow;
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                // Repository replay exercises the real dispatcher, not the
                // separately authorized operator/manual replay use case.
                Assert.True(await new OutboxEventRepository(db).ReplayAsync(denied.Event, replayStarted));
                await db.SaveChangesAsync();
            }
            await owner.WaitEventAsync(denied.Event, minimumCount: ownerBeforeReplay + 1);
            await app.WaitDeliveredAsync(denied.Event, replayStarted);
            await established.AssertNotReceivedAsync(denied.Event);
            Assert.True(owner.Received(denied.Event, EventType));
            Assert.Equal(unchangedAuthority, await AuthoritySnapshotAsync(database, channel, workspace));

            // Capture the retained authenticated cookie before HTTP rejection can
            // expire it. A fresh connection must recheck current account status.
            await using var reconnect = app.CreateSocket("member", Tenant);
            reconnect.Options.CollectHttpResponseDetails = true;
            var retainedCookie = reconnect.Options.Cookies!.GetCookieHeader(app.Address);
            Assert.False(string.IsNullOrWhiteSpace(retainedCookie));
            foreach (var route in new[] { $"/api/conversations/{channel}/messages", $"/api/messages/{initial.Message}/thread" })
            {
                // Each route independently submits the original credential.
                // A prior 401 must not turn the next request into an anonymous
                // control by expiring a shared client cookie jar.
                using var retainedHandler = new HttpClientHandler();
                retainedHandler.UseCookies = false;
                retainedHandler.AllowAutoRedirect = false;
                using var retainedClient = new HttpClient(retainedHandler, disposeHandler: false);
                retainedClient.BaseAddress = app.Address;
                retainedClient.Timeout = TimeSpan.FromSeconds(15);
                retainedClient.DefaultRequestHeaders.Add("Cookie", retainedCookie);
                retainedClient.DefaultRequestHeaders.Add("X-Tenant-Slug", Tenant);
                using var rejected = await retainedClient.GetAsync(route);
                Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
                Assert.DoesNotContain(ProtectedBody, await rejected.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
            var endpoint = new UriBuilder(app.Address) { Scheme = "ws", Path = "/hubs/app" }.Uri;
            await Assert.ThrowsAsync<WebSocketException>(() => reconnect.ConnectAsync(endpoint));
            Assert.Equal(HttpStatusCode.Unauthorized, reconnect.UpgradeStatusCode);
            Assert.Equal(unchangedAuthority, await AuthoritySnapshotAsync(database, channel, workspace));
            using var authorizedHistory = await ownerClient.GetAsync($"/api/conversations/{channel}/messages");
            Assert.Equal(HttpStatusCode.OK, authorizedHistory.StatusCode);
            Assert.Contains(ProtectedBody, await authorizedHistory.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        });

    private static async Task<Guid> CreatePrivateChannelAsync(HttpClient client, Guid workspace)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/workspaces/{workspace}/chat-channels");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        request.Content = JsonContent.Create(new { name = "Synthetic account scope " + Guid.NewGuid().ToString("N"), visibility = "Private" });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<(Guid Message, Guid Event)> PostAsync(HttpClient client, string database, Guid channel)
    {
        using var response = await client.PostAsJsonAsync($"/api/conversations/{channel}/messages",
            new { body = ProtectedBody + " " + Guid.NewGuid().ToString("N"), clientRequestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var message = body.RootElement.GetProperty("id").GetGuid();
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var eventId = await db.OutboxEvents.AsNoTracking().Where(row => row.AggregateId == message && row.EventType == EventType)
            .Select(row => row.Id).SingleAsync();
        return (message, eventId);
    }

    private static async Task DeliveredAsync(SecurityArchitectureSignalRFixture app, Guid eventId, params RealtimeSocket[] sockets)
    {
        foreach (var socket in sockets)
        {
            await socket.WaitEventAsync(eventId);
            Assert.True(socket.Received(eventId, EventType));
        }
        await app.WaitDeliveredAsync(eventId);
    }

    private static async Task<string> AuthoritySnapshotAsync(string database, Guid channel, Guid workspace)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var actor = SecurityCiFixtureSeed.TenantAMemberUserId;
        var sessions = await db.Sessions.AsNoTracking().Where(row => row.UserId == actor).OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.UserId, row.ExpiresAt, row.RevokedAt }).ToListAsync();
        Assert.NotEmpty(sessions);
        Assert.All(sessions, session => { Assert.Null(session.RevokedAt); Assert.True(session.ExpiresAt > DateTimeOffset.UtcNow); });
        var tenant = await db.TenantUsers.AsNoTracking().Where(row => row.UserId == actor)
            .Select(row => new { row.TenantId, row.Role, row.Status }).SingleAsync();
        Assert.Equal(TenantUserStatus.Active, tenant.Status);
        var membership = await db.WorkspaceMembers.AsNoTracking().Where(row => row.UserId == actor && row.WorkspaceId == workspace)
            .Select(row => new { row.TenantId, row.WorkspaceId, row.Role, row.Status }).SingleAsync();
        Assert.Equal(MembershipStatus.Active, membership.Status);
        var participant = await db.ConversationMembers.AsNoTracking().Where(row => row.UserId == actor && row.ConversationId == channel)
            .Select(row => new { row.TenantId, row.ConversationId, row.Role, row.CanRead, row.CanPost, row.LeftAt, row.RemovedAt }).SingleAsync();
        Assert.True(participant.CanRead);
        Assert.True(participant.CanPost);
        Assert.Null(participant.LeftAt);
        Assert.Null(participant.RemovedAt);
        return JsonSerializer.Serialize(new { sessions, tenant, membership, participant });
    }
}
