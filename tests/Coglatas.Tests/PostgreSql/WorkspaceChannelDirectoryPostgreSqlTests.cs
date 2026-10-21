using Coglatas.Application;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Messaging;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Web.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coglatas.Tests.PostgreSql;

[Collection("PostgreSqlTaskV1")]
[Trait("Category", "PostgreSQLIntegration")]
public sealed class WorkspaceChannelDirectoryPostgreSqlTests
{
    [PostgreSqlFact]
    public Task NamedCreatePersistsOnceAndReplaysOnlyTheSameRequest() => WithScenarioAsync(async scenario =>
    {
        var key = Guid.NewGuid();
        var request = new CreateWorkspaceChannelRequest("Council events", "PublicWithinScope");
        var created = await scenario.Directory.CreateAsync(scenario.Workspace.Id, request, key.ToString("N"));
        Assert.True(created.IsSuccess, created.Error);
        var item = created.Value!;
        Assert.Equal(scenario.Workspace.Id, item.WorkspaceId);
        Assert.Equal("Council events", item.Name);
        Assert.Equal("PublicWithinScope", item.Visibility);
        Assert.True(item.IsMember);
        Assert.True(item.CanManage);
        Assert.False(item.CanJoin);
        Assert.False(item.IsDefault);
        var outboxCount = await scenario.Db.OutboxEvents.CountAsync();
        var claimCount = await scenario.Db.IdempotencyRecords.CountAsync();
        scenario.Db.ChangeTracker.Clear();
        var replay = await scenario.Directory.CreateAsync(scenario.Workspace.Id, request, key.ToString("D"));
        Assert.True(replay.IsSuccess, replay.Error);
        Assert.Equal(item.Id, replay.Value!.Id);
        Assert.Equal(outboxCount, await scenario.Db.OutboxEvents.CountAsync());
        Assert.Equal(claimCount, await scenario.Db.IdempotencyRecords.CountAsync());
        Assert.False((await scenario.Directory.CreateAsync(scenario.Workspace.Id,
            request with { Name = "Different request" }, key.ToString("D"))).IsSuccess);
        Assert.Equal(2, await scenario.Db.Conversations.CountAsync());
        var stored = await scenario.Db.Conversations.AsNoTracking().SingleAsync(channel => channel.Id == item.Id);
        Assert.Equal(scenario.Tenant.Id, stored.TenantId);
        Assert.Equal(ConversationType.WorkspaceChannel, stored.Type);
        Assert.Equal(ConversationVisibility.PublicWithinScope, stored.Visibility);
        Assert.Null(stored.DefaultKind);
        Assert.Null(stored.ProjectId);
        Assert.Null(stored.ParentConversationId);
        Assert.Null(stored.RootConversationId);
        var member = await scenario.Db.ConversationMembers.AsNoTracking().SingleAsync(row => row.ConversationId == item.Id);
        Assert.Equal(scenario.Owner.Id, member.UserId);
        Assert.Equal(ConversationMemberRole.Admin, member.Role);
        Assert.True(member.CanRead && member.CanPost && member.CanCreateThread && member.CanManageMembers);
    });

    [PostgreSqlFact]
    public Task CreationRejectsInvalidInputAndDoesNotEnableGenericWorkspaceCreation() => WithScenarioAsync(async scenario =>
    {
        var count = await scenario.Db.Conversations.CountAsync();
        foreach (var name in new[] { "", " ", "general", "GENERAL", "with\u0000control", new string('x', 201) })
        {
            Assert.False((await scenario.Directory.CreateAsync(scenario.Workspace.Id,
                new(name, "PublicWithinScope"), Guid.NewGuid().ToString("D"))).IsSuccess);
        }
        foreach (var visibility in new[] { "", "0", "publicwithinscope", "ExternalShared" })
        {
            Assert.False((await scenario.Directory.CreateAsync(scenario.Workspace.Id,
                new("Valid name", visibility), Guid.NewGuid().ToString("D"))).IsSuccess);
        }
        foreach (var key in new[] { "", "not-a-request-identity", Guid.Empty.ToString("D") })
        {
            Assert.False((await scenario.Directory.CreateAsync(scenario.Workspace.Id,
                new("Valid name", "Private"), key)).IsSuccess);
        }
        Assert.False((await scenario.Messages.CreateAsync(new(ConversationType.WorkspaceChannel,
            "Generic bypass", [], scenario.Workspace.Id))).IsSuccess);
        Assert.Equal(count, await scenario.Db.Conversations.CountAsync());
        Assert.Single(await scenario.Db.Conversations.Where(channel => channel.DefaultKind == ConversationDefaultKind.WorkspaceGeneral).ToListAsync());
    });

    [PostgreSqlFact]
    public Task PublicDiscoveryDoesNotGrantHistoryAndPrivateChannelsDoNotAffectCounts() => WithScenarioAsync(async scenario =>
    {
        var channel = await CreateAsync(scenario, "Discoverable", "PublicWithinScope");
        var hidden = await CreateAsync(scenario, "Private council", "Private");
        var root = await scenario.Messages.SendMessageAsync(channel.Id, new("Synthetic prejoin history"));
        Assert.True(root.IsSuccess, root.Error);
        scenario.Identity.Use(scenario.Member.Id);
        var listed = await scenario.Directory.ListAsync(scenario.Workspace.Id, new());
        Assert.True(listed.IsSuccess, listed.Error);
        Assert.False(listed.Value!.CanCreate);
        Assert.Equal(2, listed.Value.TotalCount);
        Assert.DoesNotContain(listed.Value.Items, item => item.Id == hidden.Id);
        var discovered = Assert.Single(listed.Value.Items.Where(item => item.Id == channel.Id));
        Assert.False(discovered.IsMember);
        Assert.True(discovered.CanJoin);
        Assert.False((await scenario.Messages.ListMessagesAsync(channel.Id, new())).IsSuccess);
        Assert.False((await scenario.Messages.GetMessageThreadAsync(root.Value!.Id)).IsSuccess);
        Assert.False((await scenario.Directory.JoinAsync(scenario.Workspace.Id, hidden.Id)).IsSuccess);
        var joined = await scenario.Directory.JoinAsync(scenario.Workspace.Id, channel.Id);
        Assert.True(joined.IsSuccess, joined.Error);
        Assert.True(joined.Value!.IsMember);
        Assert.False(joined.Value.CanManage);
        scenario.Db.ChangeTracker.Clear();
        var history = await scenario.Messages.ListMessagesAsync(channel.Id, new());
        Assert.True(history.IsSuccess, history.Error);
        Assert.Equal(root.Value.Id, Assert.Single(history.Value!.Items).Id);
        var reply = await scenario.Messages.SendThreadMessageAsync(root.Value.Id, new("Synthetic joined reply", Guid.NewGuid()));
        Assert.True(reply.IsSuccess, reply.Error);
        Assert.Equal(channel.Id, reply.Value!.Message.ConversationId);
    });

    [PostgreSqlFact]
    public Task PrivateAdmissionRequiresCurrentManagerAndPreservesReadOnlyAuthority() => WithScenarioAsync(async scenario =>
    {
        var channel = await CreateAsync(scenario, "Private operations", "Private");
        var root = await scenario.Messages.SendMessageAsync(channel.Id, new("Private synthetic history"));
        Assert.True(root.IsSuccess, root.Error);
        scenario.Identity.Use(scenario.Member.Id);
        Assert.False((await scenario.Directory.InviteAsync(scenario.Workspace.Id, channel.Id, scenario.Owner.Id)).IsSuccess);
        Assert.False((await scenario.Directory.JoinAsync(scenario.Workspace.Id, channel.Id)).IsSuccess);
        await scenario.Db.WorkspaceMembers.Where(member => member.WorkspaceId == scenario.Workspace.Id && member.UserId == scenario.Member.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(member => member.Role, WorkspaceRole.ReadOnly));
        scenario.Identity.Use(scenario.Owner.Id);
        Assert.True((await scenario.Directory.InviteAsync(scenario.Workspace.Id, channel.Id, scenario.Member.Id)).IsSuccess);
        Assert.True((await scenario.Directory.InviteAsync(scenario.Workspace.Id, channel.Id, scenario.Member.Id)).IsSuccess);
        scenario.Db.ChangeTracker.Clear();
        var stored = await scenario.Db.ConversationMembers.AsNoTracking().SingleAsync(member =>
            member.ConversationId == channel.Id && member.UserId == scenario.Member.Id);
        Assert.Equal(ConversationMemberRole.ReadOnly, stored.Role);
        Assert.True(stored.CanRead);
        Assert.False(stored.CanPost || stored.CanCreateThread || stored.CanManageMembers);
        scenario.Identity.Use(scenario.Member.Id);
        var listed = await scenario.Directory.ListAsync(scenario.Workspace.Id, new());
        Assert.True(listed.IsSuccess, listed.Error);
        Assert.Contains(listed.Value!.Items, item => item.Id == channel.Id && item.IsMember && !item.CanManage);
        Assert.True((await scenario.Messages.ListMessagesAsync(channel.Id, new())).IsSuccess);
        Assert.False((await scenario.Messages.SendMessageAsync(channel.Id, new("Denied write"))).IsSuccess);
        Assert.False((await scenario.Messages.SendThreadMessageAsync(root.Value!.Id, new("Denied reply"))).IsSuccess);
    });

    [PostgreSqlFact]
    public Task RemovedParticipantCannotSelfRestoreAndVoluntaryRejoinDropsManagement() => WithScenarioAsync(async scenario =>
    {
        var channel = await CreateAsync(scenario, "Council discussion", "PublicWithinScope");
        scenario.Identity.Use(scenario.Member.Id);
        Assert.True((await scenario.Directory.JoinAsync(scenario.Workspace.Id, channel.Id)).IsSuccess);
        var removedAt = DateTimeOffset.UtcNow;
        await scenario.Db.ConversationMembers.Where(member => member.ConversationId == channel.Id && member.UserId == scenario.Member.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(member => member.LeftAt, removedAt).SetProperty(member => member.RemovedAt, removedAt));
        Assert.False((await scenario.Directory.JoinAsync(scenario.Workspace.Id, channel.Id)).IsSuccess);
        Assert.False((await scenario.Messages.ListMessagesAsync(channel.Id, new())).IsSuccess);
        scenario.Identity.Use(scenario.Owner.Id);
        Assert.True((await scenario.Directory.InviteAsync(scenario.Workspace.Id, channel.Id, scenario.Member.Id)).IsSuccess);
        await scenario.Db.ConversationMembers.Where(member => member.ConversationId == channel.Id && member.UserId == scenario.Member.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(member => member.LeftAt, removedAt)
                .SetProperty(member => member.Role, ConversationMemberRole.Admin).SetProperty(member => member.CanManageMembers, true));
        scenario.Identity.Use(scenario.Member.Id);
        Assert.True((await scenario.Directory.JoinAsync(scenario.Workspace.Id, channel.Id)).IsSuccess);
        scenario.Db.ChangeTracker.Clear();
        var restored = await scenario.Db.ConversationMembers.AsNoTracking().SingleAsync(member =>
            member.ConversationId == channel.Id && member.UserId == scenario.Member.Id);
        Assert.Equal(ConversationMemberRole.Member, restored.Role);
        Assert.False(restored.CanManageMembers);
        Assert.Null(restored.LeftAt);
        Assert.Null(restored.RemovedAt);
        Assert.Equal(2, await scenario.Db.ConversationMembers.CountAsync(member => member.ConversationId == channel.Id));
    });

    [PostgreSqlFact]
    public Task CurrentWorkspaceRoleControlsCreationDespiteGlobalRoleOrTrackedMembership() => WithScenarioAsync(async scenario =>
    {
        scenario.Identity.Use(scenario.Member.Id, SystemRole.PlatformAdmin);
        Assert.False((await scenario.Directory.CreateAsync(scenario.Workspace.Id,
            new("Global role is insufficient", "Private"), Guid.NewGuid().ToString("D"))).IsSuccess);
        await scenario.Db.WorkspaceMembers.Where(member => member.WorkspaceId == scenario.Workspace.Id && member.UserId == scenario.Member.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(member => member.Role, WorkspaceRole.Admin));
        var key = Guid.NewGuid().ToString("D");
        var request = new CreateWorkspaceChannelRequest("Scoped administration", "Private");
        Assert.True((await scenario.Directory.CreateAsync(scenario.Workspace.Id, request, key)).IsSuccess);
        await scenario.Db.WorkspaceMembers.SingleAsync(member => member.WorkspaceId == scenario.Workspace.Id && member.UserId == scenario.Member.Id);
        await scenario.Db.WorkspaceMembers.Where(member => member.WorkspaceId == scenario.Workspace.Id && member.UserId == scenario.Member.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(member => member.Status, MembershipStatus.Suspended));
        var count = await scenario.Db.Conversations.CountAsync();
        Assert.False((await scenario.Directory.ListAsync(scenario.Workspace.Id, new())).IsSuccess);
        Assert.False((await scenario.Directory.CreateAsync(scenario.Workspace.Id, request, key)).IsSuccess);
        Assert.False((await scenario.Directory.CreateAsync(scenario.Workspace.Id, request, Guid.NewGuid().ToString("D"))).IsSuccess);
        Assert.Equal(count, await scenario.Db.Conversations.CountAsync());
    });

    [PostgreSqlFact]
    public Task SearchPagingAndUnknownScopeNeverIncludePrivateRowsOrOverflowOffsets() => WithScenarioAsync(async scenario =>
    {
        await CreateAsync(scenario, "Council A", "PublicWithinScope");
        await CreateAsync(scenario, "Council B", "PublicWithinScope");
        var hidden = await CreateAsync(scenario, "Council hidden", "Private");
        scenario.Identity.Use(scenario.Member.Id);
        var first = await scenario.Directory.ListAsync(scenario.Workspace.Id, new(1, 1, "Council"));
        var second = await scenario.Directory.ListAsync(scenario.Workspace.Id, new(2, 1, "Council"));
        Assert.True(first.IsSuccess, first.Error);
        Assert.True(second.IsSuccess, second.Error);
        Assert.Equal(2, first.Value!.TotalCount);
        Assert.Equal(2, second.Value!.TotalCount);
        Assert.NotEqual(Assert.Single(first.Value.Items).Id, Assert.Single(second.Value.Items).Id);
        Assert.DoesNotContain(first.Value.Items.Concat(second.Value.Items), item => item.Id == hidden.Id);
        var distant = await scenario.Directory.ListAsync(scenario.Workspace.Id, new(int.MaxValue, 100, "Council"));
        Assert.True(distant.IsSuccess, distant.Error);
        Assert.Empty(distant.Value!.Items);
        Assert.Equal(2, distant.Value.TotalCount);
        Assert.False((await scenario.Directory.ListAsync(Guid.NewGuid(), new())).IsSuccess);
        Assert.False((await scenario.Directory.JoinAsync(Guid.NewGuid(), hidden.Id)).IsSuccess);
        scenario.Services.GetRequiredService<CurrentTenantService>().SetTenant(Guid.NewGuid(), "unrelated-synthetic-tenant");
        Assert.False((await scenario.Directory.ListAsync(scenario.Workspace.Id, new())).IsSuccess);
    });

    [PostgreSqlFact]
    public Task RevokedTargetCannotBeAdmittedAndDefaultChannelIsNotSelfJoinable() => WithScenarioAsync(async scenario =>
    {
        var channel = await CreateAsync(scenario, "Target admission", "Private");
        await scenario.Db.WorkspaceMembers.SingleAsync(member => member.WorkspaceId == scenario.Workspace.Id && member.UserId == scenario.Member.Id);
        await scenario.Db.TenantUsers.Where(member => member.UserId == scenario.Member.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(member => member.Status, TenantUserStatus.Suspended));
        Assert.False((await scenario.Directory.InviteAsync(scenario.Workspace.Id, channel.Id, scenario.Member.Id)).IsSuccess);
        Assert.False(await scenario.Db.ConversationMembers.AnyAsync(member => member.ConversationId == channel.Id && member.UserId == scenario.Member.Id));
        var general = await scenario.Db.Conversations.AsNoTracking().SingleAsync(item => item.DefaultKind == ConversationDefaultKind.WorkspaceGeneral);
        Assert.False((await scenario.Directory.JoinAsync(scenario.Workspace.Id, general.Id)).IsSuccess);
        Assert.Equal("general", general.Title);
        Assert.Equal(ConversationVisibility.PublicWithinScope, general.Visibility);
    });

    private static async Task<WorkspaceChannelDirectoryItem> CreateAsync(Scenario scenario, string name, string visibility)
    {
        var created = await scenario.Directory.CreateAsync(scenario.Workspace.Id, new(name, visibility), Guid.NewGuid().ToString("D"));
        Assert.True(created.IsSuccess, created.Error);
        return created.Value!;
    }

    private static Task WithScenarioAsync(Func<Scenario, Task> test) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            var owner = NewUser("Channel owner");
            var member = NewUser("Channel member");
            var identity = new ActorIdentity(owner.Id);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database,
                ["FileStorage:Provider"] = "LocalFileSystem",
                ["FileStorage:RootPath"] = Path.Combine(Path.GetTempPath(), "coglatas-channel-directory", Guid.NewGuid().ToString("N"))
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddApplication().AddInfrastructure(configuration).AddWebServices(configuration);
            services.AddSingleton<ICurrentUser>(identity);
            await using var provider = services.BuildServiceProvider(validateScopes: true);
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenantScope = scope.ServiceProvider.GetRequiredService<CurrentTenantService>();
            tenantScope.SetPlatformScope();
            var tenant = new Tenant { Name = "Channel directory", DisplayName = "Channel directory", Slug = $"directory-{Guid.NewGuid():N}" };
            db.Tenants.Add(tenant);
            db.Users.AddRange(owner, member);
            await db.SaveChangesAsync();
            tenantScope.SetTenant(tenant.Id, tenant.Slug);
            db.TenantUsers.AddRange(
                new TenantUser { UserId = owner.Id, Role = TenantUserRole.Owner, Status = TenantUserStatus.Active },
                new TenantUser { UserId = member.Id, Role = TenantUserRole.Member, Status = TenantUserStatus.Active });
            await db.SaveChangesAsync();
            var created = await scope.ServiceProvider.GetRequiredService<IWorkspaceService>().CreateAsync(
                new("Channel directory control", null, null), Guid.NewGuid().ToString("N"));
            Assert.True(created.IsSuccess, created.Error);
            var workspace = await db.Workspaces.SingleAsync(item => item.Id == created.Value!.Id);
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = workspace.Id, UserId = member.Id, Role = WorkspaceRole.Member, Status = MembershipStatus.Active
            });
            await db.SaveChangesAsync();
            await test(new(scope.ServiceProvider, db, tenant, workspace, owner, member, identity));
        });

    private static User NewUser(string name)
    {
        var email = $"directory-{Guid.NewGuid():N}@example.test";
        return new() { Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayName = name,
            Status = UserStatus.Active, PasswordHash = "unused-synthetic-hash" };
    }

    private sealed record Scenario(IServiceProvider Services, AppDbContext Db, Tenant Tenant, Workspace Workspace,
        User Owner, User Member, ActorIdentity Identity)
    {
        public IWorkspaceChannelDirectoryService Directory => Services.GetRequiredService<IWorkspaceChannelDirectoryService>();
        public IConversationService Messages => Services.GetRequiredService<IConversationService>();
    }

    private sealed class ActorIdentity(Guid initialUserId) : ICurrentUser
    {
        public Guid? UserId { get; private set; } = initialUserId;
        public Guid? SessionId => null;
        public string? Email => null;
        public SystemRole? SystemRole { get; private set; } = Coglatas.Domain.Enums.SystemRole.User;
        public bool IsAuthenticated => true;
        public void Use(Guid userId, SystemRole role = Coglatas.Domain.Enums.SystemRole.User)
        {
            UserId = userId;
            SystemRole = role;
        }
    }
}
