using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.SecurityArchitecture;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed class GroupConversationSessionChurnPostgreSqlTests
{
    private const string Tenant = SecurityCiFixtureSeed.TenantASlug;
    private const string BodyMarker = "Synthetic Group session churn body";
    private const string MessageEvent = "Messaging.MessageCreated.v1";
    private const string AuthorizationEvent = "Security.AuthorizationStateChanged.v1";

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task ThreeGroupLeaveAndReaddCyclesReauthorizeBothSameSessionSockets() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var ownerClient = await app.LoginAsync("owner", Tenant, SecurityCiFixtureSeed.TenantAOwnerEmail);
            using var memberClient = await app.LoginAsync("member", Tenant, SecurityCiFixtureSeed.TenantAMemberEmail);
            var group = await CreateGroupAsync(ownerClient, database);
            await using var owner = await app.ConnectAsync(ownerClient, "owner", Tenant);
            await using var first = await app.ConnectAsync(memberClient, "member", Tenant);
            await using var second = await app.ConnectAsync(memberClient, "member", Tenant);
            await SubscribeAsync(group, owner, first, second);
            await DeliverMessageAsync(app, ownerClient, database, group, owner, first, second);

            for (var cycle = 0; cycle < 3; cycle++)
            {
                var before = await AuthorizationIdsAsync(database, SecurityCiFixtureSeed.TenantAMemberUserId);
                using (var leave = await memberClient.PostAsync($"/api/conversations/{group}/leave", null))
                    Assert.Equal(HttpStatusCode.OK, leave.StatusCode);
                await SettleMembershipAsync(app, database, SecurityCiFixtureSeed.TenantAMemberUserId, before, first, second);
                await DeniedAsync(memberClient, group, first, second);
                await using (var freshDenied = await app.ConnectAsync(memberClient, "member", Tenant))
                {
                    await DeniedAsync(memberClient, group, freshDenied);
                    var denied = await DeliverMessageAsync(app, ownerClient, database, group, owner);
                    await ExcludedAsync(denied, first, second, freshDenied);
                }

                before = await AuthorizationIdsAsync(database, SecurityCiFixtureSeed.TenantAMemberUserId);
                using (var readd = await ownerClient.PostAsJsonAsync($"/api/conversations/{group}/members", new { userId = SecurityCiFixtureSeed.TenantAMemberUserId }))
                    Assert.Equal(HttpStatusCode.OK, readd.StatusCode);
                await SettleMembershipAsync(app, database, SecurityCiFixtureSeed.TenantAMemberUserId, before, first, second);
                await SubscribeAsync(group, first, second);
                using (var history = await memberClient.GetAsync($"/api/conversations/{group}/messages"))
                    Assert.Equal(HttpStatusCode.OK, history.StatusCode);
                await DeliverMessageAsync(app, memberClient, database, group, owner, first, second);
            }
        });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task ConcurrentIndependentGroupRemovalsPreserveOnlyCurrentOppositeScopeDelivery() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var ownerClient = await app.LoginAsync("owner", Tenant, SecurityCiFixtureSeed.TenantAOwnerEmail);
            using var memberClient = await app.LoginAsync("member", Tenant, SecurityCiFixtureSeed.TenantAMemberEmail);
            using var restrictedClient = await app.LoginAsync("restricted", Tenant, SecurityCiFixtureSeed.TenantARestrictedEmail);
            var firstGroup = await CreateGroupAsync(ownerClient, database);
            var secondGroup = await CreateGroupAsync(ownerClient, database);
            await using var owner = await app.ConnectAsync(ownerClient, "owner", Tenant);
            await using var firstMember = await app.ConnectAsync(memberClient, "member", Tenant);
            await using var secondMember = await app.ConnectAsync(memberClient, "member", Tenant);
            await using var firstRestricted = await app.ConnectAsync(restrictedClient, "restricted", Tenant);
            await using var secondRestricted = await app.ConnectAsync(restrictedClient, "restricted", Tenant);
            await SubscribeAsync(firstGroup, owner, firstMember, secondMember, firstRestricted, secondRestricted);
            await SubscribeAsync(secondGroup, owner, firstMember, secondMember, firstRestricted, secondRestricted);
            await DeliverMessageAsync(app, ownerClient, database, firstGroup, owner, firstMember, secondMember, firstRestricted, secondRestricted);
            await DeliverMessageAsync(app, ownerClient, database, secondGroup, owner, firstMember, secondMember, firstRestricted, secondRestricted);
            var memberBefore = await AuthorizationIdsAsync(database, SecurityCiFixtureSeed.TenantAMemberUserId);
            var restrictedBefore = await AuthorizationIdsAsync(database, SecurityCiFixtureSeed.TenantARestrictedUserId);

            // Both ordinary HTTP commands complete before any post-transition delivery assertion.
            var removeMember = ownerClient.DeleteAsync($"/api/conversations/{firstGroup}/members/{SecurityCiFixtureSeed.TenantAMemberUserId}");
            var removeRestricted = ownerClient.DeleteAsync($"/api/conversations/{secondGroup}/members/{SecurityCiFixtureSeed.TenantARestrictedUserId}");
            await Task.WhenAll(removeMember, removeRestricted);
            using (var response = await removeMember) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using (var response = await removeRestricted) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await SettleMembershipAsync(app, database, SecurityCiFixtureSeed.TenantAMemberUserId, memberBefore, firstMember, secondMember);
            await SettleMembershipAsync(app, database, SecurityCiFixtureSeed.TenantARestrictedUserId, restrictedBefore, firstRestricted, secondRestricted);
            await DeniedAsync(memberClient, firstGroup, firstMember, secondMember);
            await DeniedAsync(restrictedClient, secondGroup, firstRestricted, secondRestricted);
            // User-wide invalidation also removes valid subscriptions; restore only current scopes.
            await SubscribeAsync(secondGroup, firstMember, secondMember);
            await SubscribeAsync(firstGroup, firstRestricted, secondRestricted);
            var firstEvent = await DeliverMessageAsync(app, restrictedClient, database, firstGroup, owner, firstRestricted, secondRestricted);
            var secondEvent = await DeliverMessageAsync(app, memberClient, database, secondGroup, owner, firstMember, secondMember);
            await ExcludedAsync(firstEvent, firstMember, secondMember);
            await ExcludedAsync(secondEvent, firstRestricted, secondRestricted);
        });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task HttpLogoutStopsEstablishedSessionGroupDeliveryAndFreshLoginRestoresIt() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var ownerClient = await app.LoginAsync("owner", Tenant, SecurityCiFixtureSeed.TenantAOwnerEmail);
            using var memberClient = await app.LoginAsync("member", Tenant, SecurityCiFixtureSeed.TenantAMemberEmail);
            var group = await CreateGroupAsync(ownerClient, database);
            await using var owner = await app.ConnectAsync(ownerClient, "owner", Tenant);
            await using var first = await app.ConnectAsync(memberClient, "member", Tenant);
            await using var second = await app.ConnectAsync(memberClient, "member", Tenant);
            await SubscribeAsync(group, owner, first, second);
            await DeliverMessageAsync(app, ownerClient, database, group, owner, first, second);
            using (var logout = await memberClient.PostAsync("/api/auth/logout", null))
                Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
            using (var current = await memberClient.GetAsync("/api/auth/me"))
                Assert.Equal(HttpStatusCode.Unauthorized, current.StatusCode);
            using (var negotiate = await memberClient.PostAsync("/hubs/app/negotiate?negotiateVersion=1", null))
                Assert.Equal(HttpStatusCode.Unauthorized, negotiate.StatusCode);
            var denied = await DeliverMessageAsync(app, ownerClient, database, group, owner);
            // These sockets retain their pre-logout cookie snapshot. Dispatch checks the revoked session.
            Assert.False(first.Received(denied));
            Assert.False(second.Received(denied));
            using var freshClient = await app.LoginAsync("member", Tenant, SecurityCiFixtureSeed.TenantAMemberEmail);
            await using var fresh = await app.ConnectAsync(freshClient, "member", Tenant);
            await SubscribeAsync(group, fresh);
            var restored = await DeliverMessageAsync(app, freshClient, database, group, owner, fresh);
            Assert.False(first.Received(restored));
            Assert.False(second.Received(restored));
        });

    private static async Task<Guid> CreateGroupAsync(HttpClient owner, string database)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var workspace = await db.Workspaces.Where(row => row.Slug == SecurityCiFixtureSeed.TenantAWorkspaceSlug).Select(row => row.Id).SingleAsync();
        using var response = await owner.PostAsJsonAsync("/api/conversations", new
        {
            type = 7, workspaceId = workspace, title = "Synthetic Group session churn",
            memberUserIds = new[] { SecurityCiFixtureSeed.TenantAMemberUserId, SecurityCiFixtureSeed.TenantARestrictedUserId }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Group", json.RootElement.GetProperty("type").GetString());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task SubscribeAsync(Guid group, params RealtimeSocket[] sockets)
    {
        foreach (var socket in sockets)
        {
            Assert.True(await socket.SubscribeAsync("SubscribeUser"));
            Assert.True(await socket.SubscribeAsync("SubscribeConversation", group));
        }
    }

    private static async Task<Guid> DeliverMessageAsync(SecurityArchitectureSignalRFixture app, HttpClient sender, string database, Guid group, params RealtimeSocket[] receivers)
    {
        using var response = await sender.PostAsJsonAsync($"/api/conversations/{group}/messages", new
        { body = BodyMarker, clientRequestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var message = json.RootElement.GetProperty("id").GetGuid();
        Assert.True(json.RootElement.GetProperty("conversationId").GetGuid() == group);
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var id = await db.OutboxEvents.Where(row => row.EventType == MessageEvent && row.AggregateId == message).Select(row => row.Id).SingleAsync();
        Assert.NotEmpty(receivers);
        foreach (var receiver in receivers)
        {
            await receiver.WaitEventAsync(id);
            Assert.True(receiver.Received(id, MessageEvent));
        }
        await app.WaitDeliveredAsync(id);
        return id;
    }

    private static async Task<List<Guid>> AuthorizationIdsAsync(string database, Guid user)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        return await db.OutboxEvents.Where(row => row.EventType == AuthorizationEvent && row.AggregateId == user).Select(row => row.Id).ToListAsync();
    }

    private static async Task SettleMembershipAsync(SecurityArchitectureSignalRFixture app, string database, Guid user, List<Guid> before, params RealtimeSocket[] sockets)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var id = await db.OutboxEvents.Where(row => row.EventType == AuthorizationEvent && row.AggregateId == user && !before.Contains(row.Id)).Select(row => row.Id).SingleAsync();
        foreach (var socket in sockets)
        {
            await socket.WaitEventAsync(id);
            Assert.True(socket.Received(id, AuthorizationEvent));
        }
        await app.WaitDeliveredAsync(id);
        foreach (var socket in sockets) Assert.True(await socket.SubscribeAsync("SubscribeUser"));
    }

    private static async Task DeniedAsync(HttpClient client, Guid group, params RealtimeSocket[] sockets)
    {
        foreach (var socket in sockets)
        {
            var result = await socket.InvokeAsync("SubscribeConversation", group);
            Assert.False(result.GetProperty("allowed").GetBoolean());
            Assert.Equal("AccessDenied", result.GetProperty("code").GetString());
        }
        using var read = await client.GetAsync($"/api/conversations/{group}/messages");
        Assert.Equal(HttpStatusCode.BadRequest, read.StatusCode);
        Assert.False((await read.Content.ReadAsStringAsync()).Contains(BodyMarker, StringComparison.Ordinal));
        using var send = await client.PostAsJsonAsync($"/api/conversations/{group}/messages", new { body = BodyMarker, clientRequestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, send.StatusCode);
    }

    private static async Task ExcludedAsync(Guid id, params RealtimeSocket[] sockets)
    {
        foreach (var socket in sockets)
        {
            Assert.True(await socket.SubscribeAsync("SubscribeUser"));
            Assert.False(socket.Received(id));
        }
    }
}
