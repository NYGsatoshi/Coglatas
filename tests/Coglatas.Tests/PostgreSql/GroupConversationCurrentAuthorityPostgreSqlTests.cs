using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Communication;
using Coglatas.Application.Messaging;
using Coglatas.Application.Search;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;

namespace Coglatas.Tests.PostgreSql;

public sealed partial class GroupConversationHostedHttpTests
{
    [PostgreSqlFact]
    public Task HiddenForeignParentCannotBeLeftAndVisibleDirectSelfWithdrawalKeepsItsExistingBoundary() => WithScenarioAsync(async scenario =>
    {
        using var directResponse = await scenario.Host.PostAsync("/api/conversations", new CreateConversationRequest(
            ConversationType.DirectMessage, null, [scenario.Host.Recipient.Id], scenario.Host.Workspace.Id));
        Assert.Equal(HttpStatusCode.OK, directResponse.StatusCode);
        var direct = (await directResponse.Content.ReadFromJsonAsync<ConversationDetailResponse>())!;
        Guid foreignGroupId;
        await using (var scope = scenario.Host.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenantService>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var foreignTenant = await db.TenantUsers.Where(row => row.UserId == scenario.Host.ForeignRecipient.Id).Select(row => row.TenantId).SingleAsync();
            Assert.NotEqual(scenario.Host.Tenant.Id, foreignTenant);
            var workspace = new Workspace { TenantId = foreignTenant, Name = "Synthetic foreign legacy parent", Slug = "legacy-" + Guid.NewGuid().ToString("N"), CreatedByUserId = scenario.Host.ForeignRecipient.Id };
            var group = new Conversation { TenantId = foreignTenant, WorkspaceId = workspace.Id, Type = ConversationType.Group, CreatedByUserId = scenario.Host.ForeignRecipient.Id };
            db.Workspaces.Add(workspace);
            db.Conversations.Add(group);
            // Represent a malformed legacy participant foreign key. No ordinary
            // create/admission API is asserted to produce this synthetic shape.
            db.ConversationMembers.Add(new ConversationMember { TenantId = scenario.Host.Tenant.Id, ConversationId = group.Id, UserId = scenario.Host.Actor.Id,
                Role = ConversationMemberRole.Admin, CanRead = true, CanPost = true, CanManageMembers = true, JoinedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            foreignGroupId = group.Id;
        }
        using (var hidden = await scenario.Host.Client.GetAsync($"/api/conversations/{foreignGroupId}"))
            Assert.Equal(HttpStatusCode.BadRequest, hidden.StatusCode);
        using var leave = await AdmissionHost.PostAsync(scenario.Host.Client, $"/api/conversations/{foreignGroupId}/leave", new { });
        DateTimeOffset? leftAt = null;
        await scenario.WithDbAsync(async db => leftAt = await db.ConversationMembers.Where(row => row.ConversationId == foreignGroupId && row.UserId == scenario.Host.Actor.Id).Select(row => row.LeftAt).SingleAsync());
        Assert.True(leave.StatusCode == HttpStatusCode.BadRequest && !leftAt.HasValue,
            $"Expected a masked denial and unchanged malformed participation: HTTP={(int)leave.StatusCode}; participantLeftAtSet={leftAt.HasValue}.");
        await scenario.WithDbAsync(async db => await db.WorkspaceMembers.Where(row => row.WorkspaceId == direct.WorkspaceId && row.UserId == scenario.Host.Actor.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, MembershipStatus.Suspended)));
        using (var read = await scenario.Host.Client.GetAsync($"/api/conversations/{direct.Id}/messages"))
            Assert.Equal(HttpStatusCode.BadRequest, read.StatusCode);
        using var selfWithdrawal = await AdmissionHost.PostAsync(scenario.Host.Client, $"/api/conversations/{direct.Id}/leave", new { });
        Assert.Equal(HttpStatusCode.OK, selfWithdrawal.StatusCode);
        await scenario.WithDbAsync(async db => Assert.NotNull(await db.ConversationMembers.Where(row => row.ConversationId == direct.Id && row.UserId == scenario.Host.Actor.Id).Select(row => row.LeftAt).SingleAsync()));
    });

    [PostgreSqlFact]
    public Task SystemRoleDoesNotReplaceExactGroupWorkspaceMembershipOrParticipation() => WithScenarioAsync(async scenario =>
    {
        var group = await CreateGroupAsync(scenario);
        await scenario.WithDbAsync(async db => await db.Users.Where(row => row.Id == scenario.Host.Actor.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.SystemRole, SystemRole.SystemAdmin)));
        await WithActorAsync(scenario, async (services, _) =>
        {
            var authorization = services.GetRequiredService<IConversationAuthorizationService>();
            Assert.True(await authorization.CanViewConversation(scenario.Host.Actor.Id, group.Id));
            await scenario.WithDbAsync(async db => await db.WorkspaceMembers.Where(row => row.WorkspaceId == group.WorkspaceId && row.UserId == scenario.Host.Actor.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, MembershipStatus.Suspended)));
            Assert.True(await services.GetRequiredService<IWorkspaceAuthorizationService>().CanViewWorkspace(scenario.Host.Actor.Id, group.WorkspaceId));
            Assert.False(await authorization.CanViewConversation(scenario.Host.Actor.Id, group.Id));
            Assert.False(await authorization.CanSendMessage(scenario.Host.Actor.Id, group.Id));
            Assert.False(await authorization.CanManageConversation(scenario.Host.Actor.Id, group.Id));
        });
        await scenario.WithDbAsync(async db =>
        {
            await db.WorkspaceMembers.Where(row => row.WorkspaceId == group.WorkspaceId && row.UserId == scenario.Host.Actor.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, MembershipStatus.Active));
            await db.ConversationMembers.Where(row => row.ConversationId == group.Id && row.UserId == scenario.Host.Actor.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.RemovedAt, DateTimeOffset.UtcNow));
        });
        await WithActorAsync(scenario, async (services, _) =>
        {
            Assert.True(await services.GetRequiredService<IWorkspaceAuthorizationService>().CanViewWorkspace(scenario.Host.Actor.Id, group.WorkspaceId));
            Assert.False(await services.GetRequiredService<IConversationAuthorizationService>().CanViewConversation(scenario.Host.Actor.Id, group.Id));
        });
    });

    [PostgreSqlFact]
    public Task CurrentAccountTenantWorkspaceAndParticipationOverrideWarmGroupTrackingAcrossReadAndReplay() => WithScenarioAsync(async scenario =>
    {
        var group = await CreateGroupAsync(scenario);
        var original = await SendAsync(scenario.Host.Client, group.Id);
        foreach (var boundary in new[] { "account-suspended", "account-deleted", "tenant-suspended", "tenant-member-suspended", "workspace-deleted", "workspace-member-suspended", "participant-left", "participant-removed", "participant-cannot-read" })
        {
            await ResetActorAsync(scenario, group.Id);
            await WithActorAsync(scenario, async (services, db) =>
            {
                var repo = services.GetRequiredService<IMessagingRepository>();
                var authorization = services.GetRequiredService<IConversationAuthorizationService>();
                var messages = services.GetRequiredService<IConversationService>();
                Assert.True(await authorization.CanViewConversation(scenario.Host.Actor.Id, group.Id));
                Assert.True(await authorization.CanSendMessage(scenario.Host.Actor.Id, group.Id));
                Assert.True(await authorization.CanManageConversation(scenario.Host.Actor.Id, group.Id));
                Assert.NotNull(await repo.GetMemberAsync(group.Id, scenario.Host.Actor.Id));
                await db.WorkspaceMembers.Where(member => member.WorkspaceId == group.WorkspaceId && member.UserId == scenario.Host.Actor.Id).LoadAsync();
                var saved = services.GetRequiredService<IMessageFollowUpService>();
                var search = services.GetRequiredService<ISearchService>();
                var polling = services.GetRequiredService<ICommunicationPollingService>();
                Assert.True((await saved.SaveAsync(original.Id)).IsSuccess);
                Assert.Contains((await saved.ListAsync(new())).Value!.Items, item => item.MessageId == original.Id);
                Assert.Contains((await search.SearchAsync(new(Q: original.Body, Type: SearchResultType.Message))).Value!.Items, item => item.Id == original.Id);
                Assert.Contains((await search.SearchMessageAuthorsAsync(new(SelectedUserId: scenario.Host.Actor.Id))).Value!.Items, item => item.UserId == scenario.Host.Actor.Id);
                Assert.Contains((await polling.GetUnreadCountsAsync(new(WorkspaceId: group.WorkspaceId))).Value!.Items, item => item.ConversationId == group.Id);
                await scenario.WithDbAsync(other => ChangeActorAsync(other, scenario, group.Id, boundary));
                // These previously materialized rows remain warm. The authoritative
                // scalar/provider queries must not substitute their old values.
                Assert.True(db.ChangeTracker.Entries<ConversationMember>().Single(entry => entry.Entity.UserId == scenario.Host.Actor.Id && entry.Entity.ConversationId == group.Id).Entity.CanRead);
                Assert.False(await authorization.CanViewConversation(scenario.Host.Actor.Id, group.Id));
                Assert.False(await authorization.CanSendMessage(scenario.Host.Actor.Id, group.Id));
                Assert.False(await authorization.CanManageConversation(scenario.Host.Actor.Id, group.Id));
                Assert.DoesNotContain(group.Id, await repo.FilterReadableConversationIdsAsync(scenario.Host.Actor.Id, new[] { group.Id }));
                Assert.False(await repo.QueryReadableConversationIds(scenario.Host.Actor.Id)!.ContainsAsync(group.Id));
                var inbox = await messages.ListAsync(new());
                Assert.True(inbox.IsSuccess, inbox.Error);
                Assert.Empty(inbox.Value!.Items);
                Assert.Equal(0, inbox.Value.TotalCount);
                Assert.Equal(0, inbox.Value.Counts.All);
                Assert.Empty((await saved.ListAsync(new())).Value!.Items);
                Assert.Empty((await search.SearchAsync(new(Q: original.Body, Type: SearchResultType.Message))).Value!.Items);
                Assert.Empty((await search.SearchMessageAuthorsAsync(new(SelectedUserId: scenario.Host.Actor.Id))).Value!.Items);
                Assert.Empty((await polling.GetUnreadCountsAsync(new(WorkspaceId: group.WorkspaceId))).Value!.Items);
                if (!boundary.StartsWith("participant-", StringComparison.Ordinal))
                    Assert.False((await messages.CreateAsync(new(ConversationType.Group, "Denied current actor admission", [scenario.Host.Recipient.Id, scenario.Third.Id], group.WorkspaceId))).IsSuccess);
                if (boundary == "tenant-suspended")
                {
                    // Preserve the existing global persistence guard: even a
                    // denial audit cannot be saved into an inactive Tenant.
                    var denied = await Assert.ThrowsAsync<InvalidOperationException>(() => messages.SendMessageAsync(group.Id, new("Denied inactive Tenant")));
                    Assert.Equal("Current tenant is not active and cannot save tenant-owned data.", denied.Message);
                    return;
                }
                Assert.False((await messages.GetAsync(group.Id)).IsSuccess);
                Assert.False((await messages.ListMessagesAsync(group.Id, new())).IsSuccess);
                Assert.False((await messages.SendMessageAsync(group.Id, new(original.Body, ClientRequestId: original.ClientRequestId))).IsSuccess);
                Assert.False((await messages.SendMessageAsync(group.Id, new("Denied after committed authority change"))).IsSuccess);
                Assert.False((await messages.LeaveAsync(group.Id)).IsSuccess);
            });
            await scenario.WithDbAsync(async db =>
            {
                Assert.Single(await db.Messages.Where(message => message.ConversationId == group.Id).ToListAsync());
                Assert.Equal(3, await db.ConversationMembers.CountAsync(member => member.ConversationId == group.Id));
            });
        }
        await ResetActorAsync(scenario, group.Id);
        using var recovered = await scenario.Host.Client.GetAsync($"/api/conversations/{group.Id}/messages");
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Contains(original.Body, await recovered.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    });

    [PostgreSqlFact]
    public Task WarmPostingAndManagementFlagsDoNotAuthorizeAfterCommittedChanges() => WithScenarioAsync(async scenario =>
    {
        var group = await CreateGroupAsync(scenario);
        var original = await SendAsync(scenario.Host.Client, group.Id);
        foreach (var boundary in new[] { "cannot-post", "participant-readonly", "locked", "archived" })
        {
            await ResetActorAsync(scenario, group.Id);
            await scenario.WithDbAsync(async db => await db.Conversations.Where(row => row.Id == group.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsLocked, false).SetProperty(row => row.IsArchived, false)));
            await WithActorAsync(scenario, async (services, _) =>
            {
                var authorization = services.GetRequiredService<IConversationAuthorizationService>();
                var messages = services.GetRequiredService<IConversationService>();
                Assert.True(await authorization.CanSendMessage(scenario.Host.Actor.Id, group.Id));
                await scenario.WithDbAsync(async db =>
                {
                    if (boundary == "cannot-post") await db.ConversationMembers.Where(row => row.ConversationId == group.Id && row.UserId == scenario.Host.Actor.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.CanPost, false));
                    else if (boundary == "participant-readonly") await db.ConversationMembers.Where(row => row.ConversationId == group.Id && row.UserId == scenario.Host.Actor.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Role, ConversationMemberRole.ReadOnly));
                    else if (boundary == "locked") await db.Conversations.Where(row => row.Id == group.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.IsLocked, true));
                    else await db.Conversations.Where(row => row.Id == group.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.IsArchived, true));
                });
                Assert.True(await authorization.CanViewConversation(scenario.Host.Actor.Id, group.Id));
                Assert.False(await authorization.CanSendMessage(scenario.Host.Actor.Id, group.Id));
                Assert.False((await messages.SendMessageAsync(group.Id, new(original.Body, ClientRequestId: original.ClientRequestId))).IsSuccess);
                Assert.False((await messages.SendMessageAsync(group.Id, new("Denied posting-state change"))).IsSuccess);
            });
        }
        await ResetActorAsync(scenario, group.Id);
        await WithActorAsync(scenario, async (services, _) =>
        {
            var authorization = services.GetRequiredService<IConversationAuthorizationService>();
            Assert.True(await authorization.CanManageConversation(scenario.Host.Actor.Id, group.Id));
            await scenario.WithDbAsync(async db => await db.ConversationMembers.Where(row => row.ConversationId == group.Id && row.UserId == scenario.Host.Actor.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Role, ConversationMemberRole.Member).SetProperty(row => row.CanManageMembers, false)));
            Assert.False(await authorization.CanManageConversation(scenario.Host.Actor.Id, group.Id));
            Assert.False((await services.GetRequiredService<IConversationService>().RemoveMemberAsync(group.Id, scenario.Third.Id)).IsSuccess);
        });
        await scenario.WithDbAsync(async db => Assert.Single(await db.Messages.Where(message => message.ConversationId == group.Id).ToListAsync()));
    });

    [PostgreSqlFact]
    public Task MalformedLegacyGroupScopesFailClosedInBothProviderReadRelations() => WithScenarioAsync(async scenario =>
    {
        var group = await CreateGroupAsync(scenario);
        var original = await SendAsync(scenario.Host.Client, group.Id);
        Guid projectId = default;
        await scenario.WithDbAsync(async db =>
        {
            var project = new Project { WorkspaceId = group.WorkspaceId, Name = "Synthetic legacy scope", Slug = "legacy-" + Guid.NewGuid().ToString("N"), OwnerUserId = scenario.Host.Actor.Id, CreatedByUserId = scenario.Host.Actor.Id, Status = ProjectStatus.Active };
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            projectId = project.Id;
            var canonicalShape = await Assert.ThrowsAsync<PostgresException>(() => db.Conversations.Where(row => row.Id == group.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.DefaultKind, ConversationDefaultKind.WorkspaceGeneral)));
            Assert.Equal("23514", canonicalShape.SqlState);
            Assert.Equal("CK_conversations_workspace_general_shape", canonicalShape.ConstraintName);
            Assert.Null(await db.Conversations.Where(row => row.Id == group.Id).Select(row => row.DefaultKind).SingleAsync());
        });
        foreach (var shape in new[] { "project", "parent", "root", "default" })
        {
            await scenario.WithDbAsync(async db =>
            {
                await db.Conversations.Where(row => row.Id == group.Id).ExecuteUpdateAsync(update => update
                    .SetProperty(row => row.ProjectId, shape == "project" ? projectId : (Guid?)null)
                    .SetProperty(row => row.ParentConversationId, shape == "parent" ? group.Id : (Guid?)null)
                    .SetProperty(row => row.RootConversationId, shape == "root" ? group.Id : (Guid?)null)
                    .SetProperty(row => row.DefaultKind, shape == "default" ? (ConversationDefaultKind)99 : (ConversationDefaultKind?)null));
                var repo = new MessagingRepository(db);
                Assert.Empty(await repo.FilterReadableConversationIdsAsync(scenario.Host.Actor.Id, new[] { group.Id }));
                Assert.False(await repo.QueryReadableConversationIds(scenario.Host.Actor.Id)!.ContainsAsync(group.Id));
            });
            await AssertHiddenAsync(scenario.Host.Client, group.Id, original.Body);
            using var denied = await AdmissionHost.PostAsync(scenario.Host.Client, $"/api/conversations/{group.Id}/messages", new SendMessageRequest("Denied bad legacy shape"));
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        }
        await scenario.WithDbAsync(async db => Assert.Single(await db.Messages.Where(message => message.ConversationId == group.Id).ToListAsync()));
    });

    private static async Task WithActorAsync(Scenario scenario, Func<IServiceProvider, AppDbContext, Task> action)
    {
        await using var scope = scenario.Host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<CurrentTenantService>().SetTenant(scenario.Host.Tenant.Id, scenario.Host.Tenant.Slug);
        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        var prior = accessor.HttpContext;
        accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, scenario.Host.Actor.Id.ToString()), new Claim("system_role", nameof(SystemRole.User))
        }, "Synthetic service control")) };
        try { await action(services, services.GetRequiredService<AppDbContext>()); }
        finally { accessor.HttpContext = prior; }
    }

    private static Task ChangeActorAsync(AppDbContext db, Scenario scenario, Guid groupId, string state) => state switch
    {
        "account-suspended" => db.Users.Where(row => row.Id == scenario.Host.Actor.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, UserStatus.Suspended)),
        "account-deleted" => db.Users.Where(row => row.Id == scenario.Host.Actor.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.DeletedAt, DateTimeOffset.UtcNow)),
        "tenant-suspended" => db.Tenants.Where(row => row.Id == scenario.Host.Tenant.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, TenantStatus.Suspended)),
        "tenant-member-suspended" => db.TenantUsers.Where(row => row.UserId == scenario.Host.Actor.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, TenantUserStatus.Suspended)),
        "workspace-deleted" => db.Workspaces.Where(row => row.Id == scenario.Host.Workspace.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, WorkspaceStatus.Deleted)),
        "workspace-member-suspended" => db.WorkspaceMembers.Where(row => row.WorkspaceId == scenario.Host.Workspace.Id && row.UserId == scenario.Host.Actor.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, MembershipStatus.Suspended)),
        "participant-left" => db.ConversationMembers.Where(row => row.ConversationId == groupId && row.UserId == scenario.Host.Actor.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.LeftAt, DateTimeOffset.UtcNow)),
        "participant-removed" => db.ConversationMembers.Where(row => row.ConversationId == groupId && row.UserId == scenario.Host.Actor.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.RemovedAt, DateTimeOffset.UtcNow)),
        "participant-cannot-read" => db.ConversationMembers.Where(row => row.ConversationId == groupId && row.UserId == scenario.Host.Actor.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.CanRead, false)),
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    private static Task ResetActorAsync(Scenario scenario, Guid groupId) => scenario.WithDbAsync(async db =>
    {
        await db.Users.IgnoreQueryFilters().Where(row => row.Id == scenario.Host.Actor.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, UserStatus.Active).SetProperty(row => row.DeletedAt, (DateTimeOffset?)null));
        await db.Tenants.IgnoreQueryFilters().Where(row => row.Id == scenario.Host.Tenant.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, TenantStatus.Active));
        await db.TenantUsers.Where(row => row.UserId == scenario.Host.Actor.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, TenantUserStatus.Active));
        await db.Workspaces.Where(row => row.Id == scenario.Host.Workspace.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, WorkspaceStatus.Active));
        await db.WorkspaceMembers.Where(row => row.WorkspaceId == scenario.Host.Workspace.Id && row.UserId == scenario.Host.Actor.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, MembershipStatus.Active));
        await db.ConversationMembers.Where(row => row.ConversationId == groupId && row.UserId == scenario.Host.Actor.Id).ExecuteUpdateAsync(update => update
            .SetProperty(row => row.LeftAt, (DateTimeOffset?)null).SetProperty(row => row.RemovedAt, (DateTimeOffset?)null)
            .SetProperty(row => row.CanRead, true).SetProperty(row => row.CanPost, true).SetProperty(row => row.CanManageMembers, true).SetProperty(row => row.Role, ConversationMemberRole.Admin));
    });
}
