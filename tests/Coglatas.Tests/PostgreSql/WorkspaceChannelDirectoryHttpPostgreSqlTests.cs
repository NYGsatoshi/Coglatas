using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.SecurityArchitecture;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed class WorkspaceChannelDirectoryHttpPostgreSqlTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task RealHttpCreationDiscoveryAndAdmissionRespectCurrentScope() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var owner = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug,
                SecurityCiFixtureSeed.TenantAOwnerEmail);
            using var member = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug,
                SecurityCiFixtureSeed.TenantAMemberEmail);
            using var beta = await app.LoginAsync("beta", SecurityCiFixtureSeed.TenantBSlug,
                SecurityCiFixtureSeed.TenantBOwnerEmail);
            Guid workspace;
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                workspace = await db.Workspaces.Where(row => row.Slug == SecurityCiFixtureSeed.TenantAWorkspaceSlug)
                    .Select(row => row.Id).SingleAsync();
            var route = $"/api/workspaces/{workspace:D}/chat-channels";
            var key = Guid.NewGuid().ToString("D");
            var publicId = await CreateAsync(owner, route, "Council notice", "PublicWithinScope", key);
            Assert.Equal(publicId, await CreateAsync(owner, route, "Council notice", "PublicWithinScope", key));
            var privateId = await CreateAsync(owner, route, "Council private", "Private", Guid.NewGuid().ToString("D"));
            // Actual cookie-authenticated HTTP must reject all three unsafe
            // commands if a request supplies an invalid CSRF header, even when
            // the authenticated client has a valid default token.
            await AssertInvalidCsrfAsync(owner, route,
                new { name = "CSRF denied channel", visibility = "Private" }, Guid.NewGuid().ToString("D"));
            await AssertInvalidCsrfAsync(member, $"{route}/{publicId:D}/join", new { });
            await AssertInvalidCsrfAsync(owner, $"{route}/{privateId:D}/members",
                new { userId = SecurityCiFixtureSeed.TenantAMemberUserId });
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                Assert.False(await db.Conversations.AnyAsync(row => row.Title == "CSRF denied channel"));
                Assert.False(await db.ConversationMembers.AnyAsync(row =>
                    row.ConversationId == publicId && row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId));
                Assert.False(await db.ConversationMembers.AnyAsync(row =>
                    row.ConversationId == privateId && row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId));
            }
            using (var crossTenant = await beta.GetAsync(route))
                Assert.NotEqual(HttpStatusCode.OK, crossTenant.StatusCode);
            using (var listed = await member.GetAsync(route))
            {
                Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
                using var body = JsonDocument.Parse(await listed.Content.ReadAsStringAsync());
                Assert.False(body.RootElement.GetProperty("canCreate").GetBoolean());
                var items = body.RootElement.GetProperty("items").EnumerateArray().ToArray();
                var visible = Assert.Single(items, item => item.GetProperty("id").GetGuid() == publicId);
                Assert.False(visible.GetProperty("isMember").GetBoolean());
                Assert.True(visible.GetProperty("canJoin").GetBoolean());
                Assert.DoesNotContain(items, item => item.GetProperty("id").GetGuid() == privateId);
            }
            using (var unreadable = await member.GetAsync($"/api/conversations/{publicId:D}/messages"))
                Assert.NotEqual(HttpStatusCode.OK, unreadable.StatusCode);
            using (var uninvited = await member.PostAsJsonAsync($"{route}/{privateId:D}/join", new { }))
                Assert.NotEqual(HttpStatusCode.OK, uninvited.StatusCode);
            using (var joined = await member.PostAsJsonAsync($"{route}/{publicId:D}/join", new { }))
            {
                Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
                using var body = JsonDocument.Parse(await joined.Content.ReadAsStringAsync());
                Assert.True(body.RootElement.GetProperty("isMember").GetBoolean());
            }
            using (var added = await owner.PostAsJsonAsync($"{route}/{privateId:D}/members",
                new { userId = SecurityCiFixtureSeed.TenantAMemberUserId }))
                Assert.Equal(HttpStatusCode.OK, added.StatusCode);
            using (var listed = await member.GetAsync(route))
            {
                Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
                using var body = JsonDocument.Parse(await listed.Content.ReadAsStringAsync());
                var privateRow = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray(),
                    item => item.GetProperty("id").GetGuid() == privateId);
                Assert.True(privateRow.GetProperty("isMember").GetBoolean());
            }
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                Assert.Equal(1, await db.WorkspaceMembers.Where(row =>
                    row.WorkspaceId == workspace && row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, MembershipStatus.Suspended)));
                Assert.Equal(1, await db.Conversations.CountAsync(row => row.Id == publicId));
            }
            using (var revoked = await member.GetAsync(route))
                Assert.NotEqual(HttpStatusCode.OK, revoked.StatusCode);
            using (var revokedHistory = await member.GetAsync($"/api/conversations/{privateId:D}/messages"))
                Assert.NotEqual(HttpStatusCode.OK, revokedHistory.StatusCode);
            // An archived committed resource must preserve the original
            // idempotency identity, rather than prompting a second create.
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                await db.Conversations.Where(row => row.Id == publicId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsArchived, true));
            using (var request = new HttpRequestMessage(HttpMethod.Post, route))
            {
                request.Headers.Add("Idempotency-Key", key);
                request.Content = JsonContent.Create(new { name = "Council notice", visibility = "PublicWithinScope" });
                using var replay = await owner.SendAsync(request);
                Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
            }
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                Assert.Equal(1, await db.Conversations.CountAsync(row => row.Id == publicId));
        });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task ConcurrentWorkspaceSuspensionPreventsLatePublicSelfJoin() =>
        WithRaceFixtureAsync(async (database, owner, member, workspace, route) =>
        {
            var channel = await CreateAsync(owner, route, "Workspace race join",
                "PublicWithinScope", Guid.NewGuid().ToString("D"));
            await using var admin = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            await using var transaction = await admin.Database.BeginTransactionAsync();
            Assert.Equal(1, await admin.WorkspaceMembers.Where(row =>
                row.WorkspaceId == workspace && row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, MembershipStatus.Suspended)));

            var joining = member.PostAsJsonAsync($"{route}/{channel:D}/join", new { });
            Assert.NotSame(joining, await Task.WhenAny(joining, Task.Delay(TimeSpan.FromMilliseconds(500))));
            await transaction.CommitAsync();

            using var denied = await joining.WaitAsync(TimeSpan.FromSeconds(12));
            Assert.NotEqual(HttpStatusCode.OK, denied.StatusCode);
            await using var verify = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            Assert.False(await verify.ConversationMembers.AnyAsync(row =>
                row.ConversationId == channel && row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId));
            Assert.False(await verify.AuditLogs.AnyAsync(row =>
                row.Action == "WorkspaceChannelJoined" && row.EntityId == channel));
        });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task ConcurrentTenantSuspensionPreventsLatePrivateMemberAdmission() =>
        WithRaceFixtureAsync(async (database, owner, _, _, route) =>
        {
            var channel = await CreateAsync(owner, route, "Tenant race admission",
                "Private", Guid.NewGuid().ToString("D"));
            await using var admin = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            await using var transaction = await admin.Database.BeginTransactionAsync();
            Assert.Equal(1, await admin.TenantUsers.Where(row =>
                row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, TenantUserStatus.Suspended)));

            var admitting = owner.PostAsJsonAsync($"{route}/{channel:D}/members",
                new { userId = SecurityCiFixtureSeed.TenantAMemberUserId });
            Assert.NotSame(admitting, await Task.WhenAny(admitting, Task.Delay(TimeSpan.FromMilliseconds(500))));
            await transaction.CommitAsync();

            using var denied = await admitting.WaitAsync(TimeSpan.FromSeconds(12));
            Assert.NotEqual(HttpStatusCode.OK, denied.StatusCode);
            await using var verify = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            Assert.False(await verify.ConversationMembers.AnyAsync(row =>
                row.ConversationId == channel && row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId));
            Assert.False(await verify.AuditLogs.AnyAsync(row =>
                row.Action == "WorkspaceChannelMemberAdded" && row.EntityId == channel));
        });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task TwoParallelPublicJoinsCreateExactlyOneParticipantAndAudit() =>
        WithRaceFixtureAsync(async (database, owner, member, _, route) =>
        {
            var channel = await CreateAsync(owner, route, "Parallel council join",
                "PublicWithinScope", Guid.NewGuid().ToString("D"));
            var first = member.PostAsJsonAsync($"{route}/{channel:D}/join", new { });
            var second = member.PostAsJsonAsync($"{route}/{channel:D}/join", new { });
            using var firstResponse = await first.WaitAsync(TimeSpan.FromSeconds(12));
            using var secondResponse = await second.WaitAsync(TimeSpan.FromSeconds(12));
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
            await using var verify = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            Assert.Equal(1, await verify.ConversationMembers.CountAsync(row =>
                row.ConversationId == channel && row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId));
            Assert.Equal(1, await verify.AuditLogs.CountAsync(row =>
                row.Action == "WorkspaceChannelJoined" && row.EntityId == channel));
        });

    private static Task WithRaceFixtureAsync(
        Func<string, HttpClient, HttpClient, Guid, string, Task> run) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
                using var owner = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug,
                    SecurityCiFixtureSeed.TenantAOwnerEmail);
                using var member = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug,
                    SecurityCiFixtureSeed.TenantAMemberEmail);
                Guid workspace;
                await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                    workspace = await db.Workspaces.Where(row => row.Slug == SecurityCiFixtureSeed.TenantAWorkspaceSlug)
                        .Select(row => row.Id).SingleAsync();
                await run(database, owner, member, workspace,
                    $"/api/workspaces/{workspace:D}/chat-channels");
            });

    private static async Task AssertInvalidCsrfAsync(HttpClient client, string path, object body,
        string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-CSRF-Token", "synthetic-invalid-token");
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Content = JsonContent.Create(body);
        using var denied = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    private static async Task<Guid> CreateAsync(HttpClient owner, string route, string name, string visibility, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route);
        request.Headers.Add("Idempotency-Key", key);
        request.Content = JsonContent.Create(new { name, visibility });
        using var response = await owner.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }
}
