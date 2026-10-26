using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.SecurityArchitecture;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed class WorkspaceChannelCapabilityProjectionPostgreSqlTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task PrivateChannelDetailTracksCurrentWorkspaceContributionWithoutRewritingParticipantGrants() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var owner = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug,
                SecurityCiFixtureSeed.TenantAOwnerEmail);
            using var member = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug,
                SecurityCiFixtureSeed.TenantAMemberEmail);
            var workspace = await CreateWorkspaceAsync(owner);
            using (var admitted = await owner.PostAsJsonAsync($"/api/workspaces/{workspace}/members", new
            {
                userId = SecurityCiFixtureSeed.TenantAMemberUserId,
                role = WorkspaceRole.Member
            })) Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);

            var channelRoute = $"/api/workspaces/{workspace}/chat-channels";
            Guid channel;
            using (var request = new HttpRequestMessage(HttpMethod.Post, channelRoute))
            {
                request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
                request.Content = JsonContent.Create(new { name = "Private capability projection", visibility = "Private" });
                using var created = await owner.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, created.StatusCode);
                using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
                channel = body.RootElement.GetProperty("id").GetGuid();
            }
            using (var invited = await owner.PostAsJsonAsync($"{channelRoute}/{channel}/members", new
            {
                userId = SecurityCiFixtureSeed.TenantAMemberUserId
            })) Assert.Equal(HttpStatusCode.OK, invited.StatusCode);

            var direct = await CreateConversationAsync(member, workspace, ConversationType.DirectMessage,
                SecurityCiFixtureSeed.TenantAOwnerUserId);
            var group = await CreateConversationAsync(owner, workspace, ConversationType.Group,
                SecurityCiFixtureSeed.TenantAMemberUserId);
            await AssertViewerAsync(member, channel, canPost: true, canCreateThread: true);
            var root = await SendAsync(member, channel, "Before Workspace downgrade");
            await SetWorkspaceRoleAsync(owner, workspace, WorkspaceRole.ReadOnly);

            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                var stored = await db.ConversationMembers.AsNoTracking().SingleAsync(row =>
                    row.ConversationId == channel && row.UserId == SecurityCiFixtureSeed.TenantAMemberUserId);
                Assert.Equal(ConversationMemberRole.Member, stored.Role);
                Assert.True(stored.CanRead);
                Assert.True(stored.CanPost);
                Assert.True(stored.CanCreateThread);
                Assert.Equal(WorkspaceRole.ReadOnly, await db.WorkspaceMembers.Where(row =>
                        row.WorkspaceId == workspace && row.UserId == stored.UserId)
                    .Select(row => row.Role).SingleAsync());

                var messages = await db.Messages.CountAsync(row => row.ConversationId == channel);
                var outbox = await db.OutboxEvents.CountAsync(row => row.EventType.StartsWith("Messaging."));
                var denied = await db.AuditLogs.CountAsync(row => row.EntityId == channel &&
                    row.Action == "communication.message_post_denied");
                var successfulPosts = await db.AuditLogs.CountAsync(row =>
                    row.ActorUserId == SecurityCiFixtureSeed.TenantAMemberUserId &&
                    (row.Action == "communication.message_posted" || row.Action == "communication.thread_reply_posted"));
                using var send = await member.PostAsJsonAsync($"/api/conversations/{channel}/messages", new
                {
                    body = "Denied Workspace contribution", clientRequestId = Guid.NewGuid()
                });
                using var reply = await member.PostAsJsonAsync($"/api/messages/{root}/thread/messages", new
                {
                    body = "Denied Workspace reply", clientRequestId = Guid.NewGuid()
                });
                await AssertContributionDeniedAsync(send);
                await AssertContributionDeniedAsync(reply);
                Assert.Equal(messages, await db.Messages.CountAsync(row => row.ConversationId == channel));
                Assert.Equal(outbox, await db.OutboxEvents.CountAsync(row => row.EventType.StartsWith("Messaging.")));
                Assert.Equal(denied + 2, await db.AuditLogs.CountAsync(row => row.EntityId == channel &&
                    row.Action == "communication.message_post_denied"));
                Assert.Equal(successfulPosts, await db.AuditLogs.CountAsync(row =>
                    row.ActorUserId == SecurityCiFixtureSeed.TenantAMemberUserId &&
                    (row.Action == "communication.message_posted" || row.Action == "communication.thread_reply_posted")));
            }

            // Detail drives the current viewer's composer. Membership-management
            // reads and stored grants retain their original, editable values.
            await AssertViewerAsync(member, channel, canPost: false, canCreateThread: false);
            await AssertViewerAsync(owner, channel, canPost: true, canCreateThread: true,
                viewerUserId: SecurityCiFixtureSeed.TenantAOwnerUserId);
            using (var listed = await member.GetAsync($"/api/conversations/{channel}/members"))
            {
                Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
                using var body = JsonDocument.Parse(await listed.Content.ReadAsStringAsync());
                var stored = Assert.Single(body.RootElement.EnumerateArray(), row =>
                    row.GetProperty("userId").GetGuid() == SecurityCiFixtureSeed.TenantAMemberUserId);
                Assert.True(stored.GetProperty("canPost").GetBoolean());
                Assert.True(stored.GetProperty("canCreateThread").GetBoolean());
            }

            // Global Direct and Group participation retain their existing contract.
            await AssertViewerAsync(member, direct, canPost: true, canCreateThread: true);
            await AssertViewerAsync(member, group, canPost: true, canCreateThread: false);
            await SendAsync(member, direct, "Direct participation remains independent");
            await SendAsync(member, group, "Group participation remains independent");

            await SetWorkspaceRoleAsync(owner, workspace, WorkspaceRole.Member);
            await AssertViewerAsync(member, channel, canPost: true, canCreateThread: true);
            await SendAsync(member, channel, "Restored Workspace contribution");
        });

    private static async Task<Guid> CreateWorkspaceAsync(HttpClient owner)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspaces");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        request.Content = JsonContent.Create(new { name = "Synthetic capability projection " + Guid.NewGuid().ToString("N") });
        using var response = await owner.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("data").GetProperty("id").GetGuid();
    }

    private static async Task SetWorkspaceRoleAsync(HttpClient owner, Guid workspace, WorkspaceRole role)
    {
        using var response = await owner.PatchAsJsonAsync(
            $"/api/workspaces/{workspace}/members/{SecurityCiFixtureSeed.TenantAMemberUserId}",
            new { role, status = MembershipStatus.Active });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<Guid> CreateConversationAsync(HttpClient client, Guid workspace,
        ConversationType type, Guid peer)
    {
        using var response = await client.PostAsJsonAsync("/api/conversations", new
        {
            workspaceId = workspace, type, title = "Synthetic independent participation", memberUserIds = new[] { peer }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(workspace, body.RootElement.GetProperty("workspaceId").GetGuid());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> SendAsync(HttpClient client, Guid channel, string body)
    {
        using var response = await client.PostAsJsonAsync($"/api/conversations/{channel}/messages", new
        {
            body, clientRequestId = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task AssertContributionDeniedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("You are not allowed to send messages.", body.RootElement.GetProperty("error").GetString());
    }

    private static async Task AssertViewerAsync(HttpClient client, Guid channel, bool canPost, bool canCreateThread,
        Guid? viewerUserId = null)
    {
        using var response = await client.GetAsync($"/api/conversations/{channel}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var viewer = Assert.Single(body.RootElement.GetProperty("members").EnumerateArray(), row =>
            row.GetProperty("userId").GetGuid() == (viewerUserId ?? SecurityCiFixtureSeed.TenantAMemberUserId));
        Assert.True(viewer.GetProperty("canRead").GetBoolean());
        Assert.Equal(canPost, viewer.GetProperty("canPost").GetBoolean());
        Assert.Equal(canCreateThread, viewer.GetProperty("canCreateThread").GetBoolean());
    }
}
