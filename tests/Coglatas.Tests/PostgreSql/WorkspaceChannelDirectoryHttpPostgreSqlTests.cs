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
        });

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
