using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Messaging;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;

namespace Coglatas.Tests.PostgreSql;

public sealed partial class GroupConversationHostedHttpTests
{
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
                Assert.True((await saved.SaveAsync(original.Id)).IsSuccess);
                Assert.Contains((await saved.ListAsync(new())).Value!.Items, item => item.MessageId == original.Id);
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
        });
        foreach (var shape in new[] { "project", "parent", "root", "default" })
        {
            await scenario.WithDbAsync(async db =>
            {
                await db.Conversations.Where(row => row.Id == group.Id).ExecuteUpdateAsync(update => update
                    .SetProperty(row => row.ProjectId, shape == "project" ? projectId : (Guid?)null)
                    .SetProperty(row => row.ParentConversationId, shape == "parent" ? group.Id : (Guid?)null)
                    .SetProperty(row => row.RootConversationId, shape == "root" ? group.Id : (Guid?)null)
                    .SetProperty(row => row.DefaultKind, shape == "default" ? ConversationDefaultKind.WorkspaceGeneral : (ConversationDefaultKind?)null));
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
