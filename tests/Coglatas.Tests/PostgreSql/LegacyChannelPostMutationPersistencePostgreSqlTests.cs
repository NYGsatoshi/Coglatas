using System.Net;
using System.Net.Http.Json;
using Coglatas.Application.Channels;
using Coglatas.Application.Common;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.SecurityArchitecture;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed class LegacyChannelPostMutationPersistencePostgreSqlTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task EditDeletePinAndUnpinPersistWithCommittedAuditsAndCurrentDenials() =>
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

                Guid channelId;
                Guid postId;
                var originalBody = "Synthetic council legacy post";
                var editedBody = "Synthetic council edited post";
                await using (var seed = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    var workspace = await seed.Workspaces.AsNoTracking()
                        .SingleAsync(row => row.Slug == SecurityCiFixtureSeed.TenantAWorkspaceSlug);
                    var group = new Group
                    {
                        TenantId = workspace.TenantId, WorkspaceId = workspace.Id,
                        Name = "Synthetic legacy group", Slug = $"legacy-{Guid.NewGuid():N}",
                        CreatedByUserId = SecurityCiFixtureSeed.TenantAOwnerUserId
                    };
                    var channel = new Channel
                    {
                        TenantId = workspace.TenantId, WorkspaceId = workspace.Id,
                        GroupId = group.Id, Name = "Synthetic legacy channel",
                        Slug = $"legacy-{Guid.NewGuid():N}", Type = ChannelType.Public,
                        CreatedByUserId = SecurityCiFixtureSeed.TenantAOwnerUserId
                    };
                    var post = new Post
                    {
                        TenantId = workspace.TenantId, ChannelId = channel.Id,
                        AuthorUserId = SecurityCiFixtureSeed.TenantAOwnerUserId, Body = originalBody
                    };
                    seed.Groups.Add(group);
                    seed.Channels.Add(channel);
                    seed.Posts.Add(post);
                    await seed.SaveChangesAsync();
                    channelId = channel.Id;
                    postId = post.Id;
                }

                var postRoute = $"/api/posts/{postId:D}";
                // An authenticated non-manager cannot mutate another author's Post.
                using (var denied = await member.PatchAsJsonAsync(postRoute, new UpdatePostRequest("denied")))
                    Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
                using (var denied = await member.PostAsync(postRoute + "/pin", null))
                    Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
                using (var denied = await otherTenant.DeleteAsync(postRoute))
                    Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
                await AssertPersistedAsync(database, postId, originalBody, pinned: false, deleted: false,
                    edited: false, editedAudits: 0, pinnedAudits: 0, unpinnedAudits: 0, deletedAudits: 0);

                using (var edited = await owner.PatchAsJsonAsync(postRoute,
                    new UpdatePostRequest($"  {editedBody}  ")))
                {
                    Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
                    var returned = await edited.Content.ReadFromJsonAsync<PostResponse>();
                    Assert.NotNull(returned);
                    Assert.Equal(postId, returned.Id);
                    Assert.Equal(editedBody, returned.Body);
                }
                await AssertPersistedAsync(database, postId, editedBody, pinned: false, deleted: false,
                    edited: true, editedAudits: 1, pinnedAudits: 0, unpinnedAudits: 0, deletedAudits: 0);

                using (var pinned = await owner.PostAsync(postRoute + "/pin", null))
                    Assert.Equal(HttpStatusCode.OK, pinned.StatusCode);
                await AssertPersistedAsync(database, postId, editedBody, pinned: true, deleted: false,
                    edited: true, editedAudits: 1, pinnedAudits: 1, unpinnedAudits: 0, deletedAudits: 0);

                using (var pinnedPosts = await owner.GetAsync($"/api/channels/{channelId:D}/pinned-posts"))
                {
                    Assert.Equal(HttpStatusCode.OK, pinnedPosts.StatusCode);
                    var items = await pinnedPosts.Content.ReadFromJsonAsync<List<PostResponse>>();
                    Assert.NotNull(items);
                    Assert.Equal(postId, Assert.Single(items).Id);
                }

                using (var unpinned = await owner.DeleteAsync(postRoute + "/pin"))
                    Assert.Equal(HttpStatusCode.OK, unpinned.StatusCode);
                await AssertPersistedAsync(database, postId, editedBody, pinned: false, deleted: false,
                    edited: true, editedAudits: 1, pinnedAudits: 1, unpinnedAudits: 1, deletedAudits: 0);

                using (var deleted = await owner.DeleteAsync(postRoute))
                    Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
                await AssertPersistedAsync(database, postId, editedBody, pinned: false, deleted: true,
                    edited: true, editedAudits: 1, pinnedAudits: 1, unpinnedAudits: 1, deletedAudits: 1);

                using (var listed = await owner.GetAsync($"/api/channels/{channelId:D}/posts"))
                {
                    Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
                    var response = await listed.Content.ReadFromJsonAsync<PagedResponse<PostResponse>>();
                    Assert.NotNull(response);
                    Assert.DoesNotContain(response.Items, row => row.Id == postId);
                }
            });

    private static async Task AssertPersistedAsync(string database, Guid postId, string expectedBody,
        bool pinned, bool deleted, bool edited,
        int editedAudits, int pinnedAudits, int unpinnedAudits, int deletedAudits)
    {
        // Deliberately create an entirely new context after each HTTP command.
        await using var verify = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var post = await verify.Posts.AsNoTracking().SingleAsync(row => row.Id == postId);
        Assert.Equal(expectedBody, post.Body);
        Assert.Equal(edited, post.EditedAt.HasValue);
        Assert.Equal(deleted, post.DeletedAt.HasValue);
        Assert.Equal(pinned, post.PinnedAt.HasValue);
        Assert.Equal(pinned ? SecurityCiFixtureSeed.TenantAOwnerUserId : null, post.PinnedByUserId);

        var actions = await verify.AuditLogs.AsNoTracking()
            .Where(row => row.EntityId == postId && row.EntityType == "Post")
            .Select(row => row.Action).ToListAsync();
        Assert.Equal(editedAudits, actions.Count(action => action == "PostEdited"));
        Assert.Equal(pinnedAudits, actions.Count(action => action == "PostPinned"));
        Assert.Equal(unpinnedAudits, actions.Count(action => action == "PostUnpinned"));
        Assert.Equal(deletedAudits, actions.Count(action => action == "PostDeleted"));
    }
}
