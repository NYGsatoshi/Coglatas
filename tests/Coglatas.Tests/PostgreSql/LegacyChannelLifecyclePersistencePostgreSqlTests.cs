using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coglatas.Application.Channels;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.SecurityArchitecture;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed class LegacyChannelLifecyclePersistencePostgreSqlTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task ChannelLifecyclePersistsScopedMembershipAndAuditsWhileDenialsPreserveRows() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
                using var owner = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug,
                    SecurityCiFixtureSeed.TenantAOwnerEmail);
                using var member = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug,
                    SecurityCiFixtureSeed.TenantAMemberEmail);
                using var otherTenant = await app.LoginAsync("beta", SecurityCiFixtureSeed.TenantBSlug,
                    SecurityCiFixtureSeed.TenantBOwnerEmail);
                Guid groupId;
                Guid workspaceId;
                Guid tenantId;
                Guid siblingId;
                await using (var seed = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    var workspace = await seed.Workspaces.AsNoTracking()
                        .SingleAsync(row => row.Slug == SecurityCiFixtureSeed.TenantAWorkspaceSlug);
                    tenantId = workspace.TenantId;
                    workspaceId = workspace.Id;
                    var group = new Group
                    {
                        TenantId = tenantId, WorkspaceId = workspaceId,
                        Name = "Synthetic channel lifecycle group", Slug = $"lifecycle-{Guid.NewGuid():N}",
                        CreatedByUserId = SecurityCiFixtureSeed.TenantAOwnerUserId
                    };
                    var sibling = new Channel
                    {
                        TenantId = tenantId, WorkspaceId = workspaceId, GroupId = group.Id,
                        Name = "Unchanged synthetic sibling", Slug = $"sibling-{Guid.NewGuid():N}",
                        CreatedByUserId = SecurityCiFixtureSeed.TenantAOwnerUserId
                    };
                    seed.Groups.Add(group);
                    seed.Channels.Add(sibling);
                    seed.GroupMembers.Add(new GroupMember
                    {
                        TenantId = tenantId, GroupId = group.Id,
                        UserId = SecurityCiFixtureSeed.TenantAMemberUserId, Role = GroupRole.Member,
                        JoinedAt = DateTimeOffset.UtcNow.AddDays(-1)
                    });
                    await seed.SaveChangesAsync();
                    groupId = group.Id;
                    siblingId = sibling.Id;
                }
                var siblingBefore = await SnapshotAsync(database, groupId, siblingId);
                var groupRoute = $"/api/groups/{groupId:D}/channels";
                await AssertDeniedWithoutChangesAsync(database, groupId, () =>
                    member.PostAsJsonAsync(groupRoute, new CreateChannelRequest("Denied", null, ChannelType.Public)));
                await AssertDeniedWithoutChangesAsync(database, groupId, () =>
                    owner.PostAsJsonAsync(groupRoute, new CreateChannelRequest("  ", null, ChannelType.Public)));

                ChannelResponse created;
                using (var response = await owner.PostAsJsonAsync(groupRoute,
                    new CreateChannelRequest("  Synthetic Council  ", "  original description  ", ChannelType.Public)))
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    created = (await response.Content.ReadFromJsonAsync<ChannelResponse>())!;
                    Assert.NotNull(created);
                    Assert.NotEqual(Guid.Empty, created.Id);
                    Assert.Equal(groupId, created.GroupId);
                    Assert.Equal("Synthetic Council", created.Name);
                    Assert.Equal("original description", created.Description);
                    Assert.Equal(ChannelType.Public, created.ChannelType);
                    Assert.Equal(SecurityCiFixtureSeed.TenantAOwnerUserId, created.CreatedByUserId);
                    Assert.Empty(created.PinnedPosts);
                }
                var route = $"/api/channels/{created.Id:D}";
                await using (var verify = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    var stored = await verify.Channels.AsNoTracking().SingleAsync(row => row.Id == created.Id);
                    Assert.Equal((tenantId, workspaceId, groupId), (stored.TenantId, stored.WorkspaceId, stored.GroupId));
                    Assert.Equal("synthetic-council", stored.Slug);
                    var creator = await verify.ChannelMembers.AsNoTracking().SingleAsync(row => row.ChannelId == created.Id);
                    Assert.Equal(tenantId, creator.TenantId);
                    Assert.Equal(SecurityCiFixtureSeed.TenantAOwnerUserId, creator.UserId);
                    Assert.Equal(ChannelRole.Admin, creator.Role);
                    Assert.NotEqual(default, creator.JoinedAt);
                }
                using (var response = await owner.PatchAsJsonAsync(route,
                    new UpdateChannelRequest("  Synthetic Private Council  ", "  updated description  ", ChannelType.Confidential, null)))
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    var updated = await response.Content.ReadFromJsonAsync<ChannelResponse>();
                    Assert.NotNull(updated);
                    Assert.Equal(created.Id, updated.Id);
                    Assert.Equal("Synthetic Private Council", updated.Name);
                    Assert.Equal("updated description", updated.Description);
                    Assert.Equal(ChannelType.Confidential, updated.ChannelType);
                }
                await AssertDeniedWithoutChangesAsync(database, groupId, () => member.GetAsync(route));
                using (var response = await owner.GetAsync(route))
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var target = new AddChannelMemberRequest(SecurityCiFixtureSeed.TenantAMemberUserId, ChannelRole.ReadOnly);
                ChannelMemberResponse added;
                using (var response = await owner.PostAsJsonAsync(route + "/members", target))
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    added = (await response.Content.ReadFromJsonAsync<ChannelMemberResponse>())!;
                    Assert.NotNull(added);
                    Assert.Equal(target.UserId, added.UserId);
                    Assert.Equal(ChannelRole.ReadOnly, added.Role);
                    Assert.Equal(SecurityCiFixtureSeed.TenantAMemberEmail, added.Email);
                    Assert.False(string.IsNullOrWhiteSpace(added.DisplayName));
                }
                // PostgreSQL stores microseconds; the immediate DTO can retain 100ns .NET ticks.
                Assert.Equal(TimeSpan.Zero, added.JoinedAt.Offset);
                var persistedJoinedAt = new DateTimeOffset(added.JoinedAt.Ticks - added.JoinedAt.Ticks % 10, TimeSpan.Zero);
                using (var response = await owner.GetAsync(route + "/members"))
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    var listed = await response.Content.ReadFromJsonAsync<List<ChannelMemberResponse>>();
                    Assert.NotNull(listed);
                    Assert.Equal(2, listed.Count);
                    Assert.Equal(added with { JoinedAt = persistedJoinedAt }, Assert.Single(listed, row => row.UserId == target.UserId));
                }
                await using (var verify = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    var stored = await verify.ChannelMembers.AsNoTracking()
                        .SingleAsync(row => row.ChannelId == created.Id && row.UserId == target.UserId);
                    Assert.Equal(tenantId, stored.TenantId);
                    Assert.Equal(added.Role, stored.Role);
                    Assert.Equal(persistedJoinedAt, stored.JoinedAt);
                }
                using (var response = await member.GetAsync(route))
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                await AssertDeniedWithoutChangesAsync(database, groupId, () => owner.PostAsJsonAsync(route + "/members", target));
                await AssertDeniedWithoutChangesAsync(database, groupId, () => owner.PostAsJsonAsync(route + "/members",
                    new AddChannelMemberRequest(Guid.NewGuid(), ChannelRole.Member)));
                await AssertDeniedWithoutChangesAsync(database, groupId, () => owner.PostAsJsonAsync(route + "/members",
                    new AddChannelMemberRequest(SecurityCiFixtureSeed.TenantBOwnerUserId, ChannelRole.Member)));
                await AssertDeniedWithoutChangesAsync(database, groupId, () => member.PatchAsJsonAsync(route,
                    new UpdateChannelRequest("Denied mutation", null, null, null)));
                await AssertDeniedWithoutChangesAsync(database, groupId, () => member.PostAsJsonAsync(route + "/posts",
                    new CreatePostRequest("Denied read-only post")));
                await AssertDeniedWithoutChangesAsync(database, groupId, () => otherTenant.DeleteAsync(route));
                await AssertDeniedWithoutChangesAsync(database, groupId, () => owner.PatchAsJsonAsync(route,
                    new UpdateChannelRequest("  ", null, null, null)));

                using (var response = await owner.DeleteAsync(route + $"/members/{target.UserId:D}"))
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                await AssertDeniedWithoutChangesAsync(database, groupId, () => member.GetAsync(route));
                await AssertDeniedWithoutChangesAsync(database, groupId, () => owner.DeleteAsync(route + $"/members/{target.UserId:D}"));
                using (var response = await owner.DeleteAsync(route))
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using (var response = await owner.GetAsync(groupRoute))
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    var listed = await response.Content.ReadFromJsonAsync<List<ChannelListItemResponse>>();
                    Assert.NotNull(listed);
                    Assert.Equal(siblingId, Assert.Single(listed).Id);
                }
                await AssertDeniedWithoutChangesAsync(database, groupId, () => owner.GetAsync(route));
                await using (var verify = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    var stored = await verify.Channels.AsNoTracking().SingleAsync(row => row.Id == created.Id);
                    Assert.Equal((tenantId, workspaceId, groupId), (stored.TenantId, stored.WorkspaceId, stored.GroupId));
                    Assert.Equal("Synthetic Private Council", stored.Name);
                    Assert.Equal("synthetic-private-council", stored.Slug);
                    Assert.Equal("updated description", stored.Description);
                    Assert.Equal(ChannelType.Confidential, stored.Type);
                    Assert.Equal(ChannelStatus.Archived, stored.Status);
                    Assert.NotNull(stored.DeletedAt);
                    var creator = await verify.ChannelMembers.AsNoTracking().SingleAsync(row => row.ChannelId == created.Id);
                    Assert.Equal(SecurityCiFixtureSeed.TenantAOwnerUserId, creator.UserId);
                    Assert.Equal(ChannelRole.Admin, creator.Role);
                    var audits = await verify.AuditLogs.AsNoTracking()
                        .Where(row => row.EntityType == "Channel" && row.EntityId == created.Id).ToListAsync();
                    Assert.Equal(7, audits.Count);
                    string[] mutationActions = ["ChannelCreated", "ChannelUpdated", "ChannelMemberAdded", "ChannelMemberRemoved", "ChannelArchived"];
                    foreach (var action in mutationActions)
                        Assert.Single(audits, row => row.Action == action && row.ActorUserId == SecurityCiFixtureSeed.TenantAOwnerUserId);
                    Assert.Equal(2, audits.Count(row => row.Action == "ConfidentialChannelViewed"));
                    Assert.Contains(audits, row => row.Action == "ConfidentialChannelViewed" && row.ActorUserId == target.UserId);
                    Assert.Contains(audits, row => row.Action == "ConfidentialChannelViewed" && row.ActorUserId == SecurityCiFixtureSeed.TenantAOwnerUserId);
                    foreach (var audit in audits) Assert.Equal(tenantId, audit.TenantId);
                }
                Assert.Equal(siblingBefore, await SnapshotAsync(database, groupId, siblingId));
            });

    private static async Task AssertDeniedWithoutChangesAsync(string database, Guid groupId, Func<Task<HttpResponseMessage>> send)
    {
        var before = await SnapshotAsync(database, groupId);
        using var response = await send();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync(database, groupId));
    }

    private static async Task<string> SnapshotAsync(string database, Guid groupId, Guid? channelId = null)
    {
        await using var verify = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var channels = await verify.Channels.AsNoTracking()
            .Where(row => row.GroupId == groupId && (!channelId.HasValue || row.Id == channelId.Value))
            .OrderBy(row => row.Id).Select(row => new
            {
                row.Id, row.TenantId, row.WorkspaceId, row.GroupId, row.Name, row.Slug, row.Description,
                row.Type, row.Status, row.CreatedByUserId, row.CreatedAt, row.UpdatedAt, row.DeletedAt
            }).ToListAsync();
        var ids = channels.Select(row => row.Id).ToArray();
        var members = await verify.ChannelMembers.AsNoTracking().Where(row => ids.Contains(row.ChannelId))
            .OrderBy(row => row.Id).Select(row => new
            { row.Id, row.TenantId, row.ChannelId, row.UserId, row.Role, row.JoinedAt, row.CreatedAt, row.UpdatedAt }).ToListAsync();
        var posts = await verify.Posts.AsNoTracking().Where(row => ids.Contains(row.ChannelId))
            .OrderBy(row => row.Id).Select(row => new
            { row.Id, row.TenantId, row.ChannelId, row.AuthorUserId, row.Body, row.EditedAt, row.PinnedAt, row.PinnedByUserId, row.CreatedAt, row.UpdatedAt, row.DeletedAt }).ToListAsync();
        var audits = await verify.AuditLogs.AsNoTracking()
            .Where(row => row.EntityType == "Channel" && row.EntityId.HasValue && ids.Contains(row.EntityId.Value))
            .OrderBy(row => row.Id).Select(row => new
            { row.Id, row.TenantId, row.ActorUserId, row.Action, row.EntityType, row.EntityId, row.Summary, row.MetadataJson, row.CreatedAt }).ToListAsync();
        return JsonSerializer.Serialize(new { channels, members, posts, audits });
    }
}
