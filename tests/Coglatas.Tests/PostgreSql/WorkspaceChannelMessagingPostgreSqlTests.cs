using Coglatas.Application;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Communication;
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
public sealed class WorkspaceChannelMessagingPostgreSqlTests
{
    [PostgreSqlFact]
    public Task CanonicalWorkspaceGeneralPersistsMessagesAndSameConversationReplies() => WithScenarioAsync(async scenario =>
    {
        Assert.Equal(ConversationDefaultKind.WorkspaceGeneral, scenario.Conversation.DefaultKind);
        Assert.Equal(ConversationType.WorkspaceChannel, scenario.Conversation.Type);
        var key = Guid.NewGuid();
        var sent = await scenario.Messages.SendMessageAsync(scenario.Conversation.Id, new("Canonical synthetic message", ClientRequestId: key, MentionedUserIds: [scenario.Other.Id]));
        Assert.True(sent.IsSuccess, sent.Error);
        var replay = await scenario.Messages.SendMessageAsync(scenario.Conversation.Id, new("Canonical synthetic message", ClientRequestId: key));
        Assert.True(replay.IsSuccess, replay.Error);
        Assert.Equal(sent.Value!.Id, replay.Value!.Id);
        var reply = await scenario.Messages.SendThreadMessageAsync(sent.Value.Id, new("Canonical synthetic reply", Guid.NewGuid()));
        Assert.True(reply.IsSuccess, reply.Error);
        Assert.Equal(scenario.Conversation.Id, reply.Value!.Message.ConversationId);
        scenario.Db.ChangeTracker.Clear();
        var timeline = await scenario.Messages.ListMessagesAsync(scenario.Conversation.Id, new());
        Assert.True(timeline.IsSuccess, timeline.Error);
        Assert.Equal(sent.Value.Id, Assert.Single(timeline.Value!.Items).Id);
        var thread = await scenario.Messages.GetMessageThreadAsync(sent.Value.Id);
        Assert.True(thread.IsSuccess, thread.Error);
        Assert.Equal(reply.Value.Message.Id, Assert.Single(thread.Value!.Replies).Id);
        Assert.Equal(sent.Value.Id, await scenario.Db.Messages.Where(message => message.Id == reply.Value.Message.Id)
            .Select(message => message.ThreadRootMessageId).SingleAsync());
        Assert.Equal(2, await scenario.Db.Messages.CountAsync());
        Assert.Single(await scenario.Db.Notifications.Where(notification => notification.UserId == scenario.Other.Id && notification.NotificationType == NotificationType.Mention).ToListAsync());
        Assert.Equal(2, await scenario.Db.OutboxEvents.CountAsync(item => item.EventType == "Messaging.MessageCreated.v1"));
        var invalidation = await scenario.Db.OutboxEvents.SingleAsync(item => item.EventType == "Messaging.ThreadChanged.v1");
        Assert.DoesNotContain("Canonical synthetic reply", invalidation.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain(scenario.Actor.DisplayName, invalidation.PayloadJson, StringComparison.Ordinal);
    });

    [PostgreSqlFact]
    public Task PollingIncludesAuthorizedWorkspaceChannelAndRetainsArchivedHistory() => WithScenarioAsync(async scenario =>
    {
        scenario.Db.Messages.Add(new Message
        {
            WorkspaceId = scenario.Workspace.Id, ConversationId = scenario.Conversation.Id,
            AuthorUserId = scenario.Other.Id, Body = "Unread synthetic channel message"
        });
        await scenario.Db.SaveChangesAsync();
        await AssertPollingAsync(scenario, expected: true);
        await scenario.Db.Workspaces.Where(workspace => workspace.Id == scenario.Workspace.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(workspace => workspace.Status, WorkspaceStatus.Archived));
        await AssertPollingAsync(scenario, expected: true);
        await scenario.Db.WorkspaceMembers.Where(member => member.WorkspaceId == scenario.Workspace.Id && member.UserId == scenario.Actor.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(member => member.Status, MembershipStatus.Suspended));
        await AssertPollingAsync(scenario, expected: false);
    });

    [PostgreSqlFact]
    public Task FirstReplyRequiresCreateThreadButLaterRepliesRequirePostingOnly() => WithScenarioAsync(async scenario =>
    {
        var root = await scenario.Messages.SendMessageAsync(scenario.Conversation.Id, new("Thread permission root"));
        Assert.True(root.IsSuccess, root.Error);
        var member = await scenario.Db.ConversationMembers.SingleAsync(member => member.UserId == scenario.Actor.Id);
        member.CanCreateThread = false;
        await scenario.Db.SaveChangesAsync();
        Assert.False((await scenario.Messages.SendThreadMessageAsync(root.Value!.Id, new("Denied first reply"))).IsSuccess);
        Assert.Single(await scenario.Db.Messages.ToListAsync());
        member.CanCreateThread = true;
        await scenario.Db.SaveChangesAsync();
        var first = await scenario.Messages.SendThreadMessageAsync(root.Value.Id, new("Permitted first reply"));
        Assert.True(first.IsSuccess, first.Error);
        member.CanCreateThread = false;
        await scenario.Db.SaveChangesAsync();
        var later = await scenario.Messages.SendThreadMessageAsync(root.Value.Id, new("Permitted later reply"));
        Assert.True(later.IsSuccess, later.Error);
        Assert.Equal(3, await scenario.Db.Messages.CountAsync());
    });

    [PostgreSqlFact]
    public Task ArchivedWorkspaceDeniesSendReplyAndReplay() => AssertCurrentStateDenialAsync("workspace-archived");

    [PostgreSqlFact]
    public Task DeletedWorkspaceDeniesSendReplyAndReplay() => AssertCurrentStateDenialAsync("workspace-deleted");

    [PostgreSqlFact]
    public Task SoftDeletedWorkspaceDeniesSendReplyAndReplay() => AssertCurrentStateDenialAsync("workspace-soft-deleted");

    [PostgreSqlFact]
    public Task ReadOnlyWorkspaceRoleDeniesRetainedConversationAdminSendReplyAndReplay() => AssertCurrentStateDenialAsync("workspace-readonly");

    [PostgreSqlFact]
    public Task SuspendedWorkspaceMembershipDeniesSendReplyAndReplay() => AssertCurrentStateDenialAsync("workspace-member-suspended");

    [PostgreSqlFact]
    public Task SuspendedTenantPreservesExistingWriteGuardWithoutPersistingMessageOrReply() => WithScenarioAsync(async scenario =>
    {
        var key = Guid.NewGuid();
        var root = await scenario.Messages.SendMessageAsync(scenario.Conversation.Id, new("Before Tenant suspension", ClientRequestId: key));
        Assert.True(root.IsSuccess, root.Error);
        await ChangePersistedStateAsync(scenario, "tenant-suspended");
        // The established global Tenant write guard also rejects the denial audit.
        // Service calls therefore throw; authenticated HTTP has its separate session gate.
        var fresh = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scenario.Messages.SendMessageAsync(scenario.Conversation.Id, new("Denied Tenant write")));
        Assert.Equal("Current tenant is not active and cannot save tenant-owned data.", fresh.Message);
        var replay = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scenario.Messages.SendMessageAsync(scenario.Conversation.Id, new("Before Tenant suspension", ClientRequestId: key)));
        Assert.Equal(fresh.Message, replay.Message);
        var reply = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scenario.Messages.SendThreadMessageAsync(root.Value!.Id, new("Denied Tenant reply")));
        Assert.Equal(fresh.Message, reply.Message);
        Assert.Single(await scenario.Db.Messages.AsNoTracking().ToListAsync());
    });

    [PostgreSqlFact]
    public Task SuspendedTenantMembershipDeniesSendReplyAndReplay() => AssertCurrentStateDenialAsync("tenant-member-suspended");

    [PostgreSqlFact]
    public Task SuspendedUserDeniesSendReplyAndReplay() => AssertCurrentStateDenialAsync("user-suspended");

    [PostgreSqlFact]
    public Task DeletedUserDeniesSendReplyAndReplay() => AssertCurrentStateDenialAsync("user-deleted");

    private static Task AssertCurrentStateDenialAsync(string state) => WithScenarioAsync(async scenario =>
    {
        var key = Guid.NewGuid();
        var root = await scenario.Messages.SendMessageAsync(scenario.Conversation.Id, new("Before lifecycle change", ClientRequestId: key));
        Assert.True(root.IsSuccess, root.Error);
        // Warm the exact tracked objects that must not hide committed lifecycle changes.
        Assert.True(await scenario.Get<IWorkspaceAuthorizationService>().CanContributeWorkspace(scenario.Actor.Id, scenario.Workspace.Id));
        await scenario.Db.TenantUsers.Include(member => member.Tenant).Include(member => member.User).ToListAsync();
        await ChangePersistedStateAsync(scenario, state);
        Assert.Equal(WorkspaceStatus.Active, scenario.Workspace.Status);
        var originalCount = await scenario.Db.Messages.CountAsync();
        Assert.False((await scenario.Messages.SendMessageAsync(scenario.Conversation.Id, new("After lifecycle change"))).IsSuccess);
        Assert.False((await scenario.Messages.SendMessageAsync(scenario.Conversation.Id, new("Before lifecycle change", ClientRequestId: key))).IsSuccess);
        Assert.False((await scenario.Messages.SendThreadMessageAsync(root.Value!.Id, new("Denied lifecycle reply"))).IsSuccess);
        Assert.Equal(originalCount, await scenario.Db.Messages.CountAsync());
    });

    [PostgreSqlFact]
    public Task CurrentSystemAdminContributionCompatibilityStillRequiresWorkspaceMembership() => WithScenarioAsync(async scenario =>
    {
        scenario.Actor.SystemRole = SystemRole.SystemAdmin;
        var member = await scenario.Db.WorkspaceMembers.SingleAsync(member => member.UserId == scenario.Actor.Id);
        member.Role = WorkspaceRole.ReadOnly;
        await scenario.Db.SaveChangesAsync();
        var sent = await scenario.Messages.SendMessageAsync(scenario.Conversation.Id, new("Scoped SystemAdmin compatibility"));
        Assert.True(sent.IsSuccess, sent.Error);
        await scenario.Db.Users.Where(user => user.Id == scenario.Actor.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.SystemRole, SystemRole.User));
        Assert.False((await scenario.Messages.SendMessageAsync(scenario.Conversation.Id, new("No stale SystemAdmin bypass"))).IsSuccess);
        await scenario.Db.Users.Where(user => user.Id == scenario.Actor.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.SystemRole, SystemRole.SystemAdmin));
        await scenario.Db.WorkspaceMembers.Where(item => item.Id == member.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.Status, MembershipStatus.Suspended));
        Assert.False((await scenario.Messages.SendMessageAsync(scenario.Conversation.Id, new("No membership bypass"))).IsSuccess);
        Assert.Single(await scenario.Db.Messages.ToListAsync());
    });

    [PostgreSqlFact]
    public Task ExistingConversationPermissionLockAndArchiveGuardsStillDenyChannelSends() => WithScenarioAsync(async scenario =>
    {
        var member = await scenario.Db.ConversationMembers.SingleAsync(item => item.UserId == scenario.Actor.Id);
        member.CanPost = false;
        await scenario.Db.SaveChangesAsync();
        Assert.False((await scenario.Messages.SendMessageAsync(scenario.Conversation.Id, new("Denied CanPost control"))).IsSuccess);
        member.CanPost = true;
        member.Role = ConversationMemberRole.ReadOnly;
        await scenario.Db.SaveChangesAsync();
        Assert.False((await scenario.Messages.SendMessageAsync(scenario.Conversation.Id, new("Denied participant role control"))).IsSuccess);
        member.Role = ConversationMemberRole.Admin;
        scenario.Conversation.IsLocked = true;
        await scenario.Db.SaveChangesAsync();
        Assert.False((await scenario.Messages.SendMessageAsync(scenario.Conversation.Id, new("Denied lock control"))).IsSuccess);
        scenario.Conversation.IsLocked = false;
        scenario.Conversation.IsArchived = true;
        await scenario.Db.SaveChangesAsync();
        Assert.False((await scenario.Messages.SendMessageAsync(scenario.Conversation.Id, new("Denied archive control"))).IsSuccess);
        Assert.Empty(await scenario.Db.Messages.ToListAsync());
        scenario.Conversation.IsArchived = false;
        await scenario.Db.SaveChangesAsync();
        Assert.True((await scenario.Messages.SendMessageAsync(scenario.Conversation.Id, new("Restored current participant control"))).IsSuccess);
    });

    [PostgreSqlFact]
    public Task ExplicitTenantGuestWithContributingScopedMembershipCanPost() => WithScenarioAsync(async scenario =>
    {
        await scenario.Db.TenantUsers.Where(member => member.UserId == scenario.Actor.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(member => member.Role, TenantUserRole.Guest));
        var member = await scenario.Db.WorkspaceMembers.SingleAsync(member => member.UserId == scenario.Actor.Id);
        member.Role = WorkspaceRole.Member;
        await scenario.Db.SaveChangesAsync();
        var sent = await scenario.Messages.SendMessageAsync(scenario.Conversation.Id, new("Explicitly scoped Guest contribution"));
        Assert.True(sent.IsSuccess, sent.Error);
    });

    [PostgreSqlFact]
    public Task RepairDoesNotEnableGenericWorkspaceChannelCreationOrLockArchiveOrGroup() => WithScenarioAsync(async scenario =>
    {
        var count = await scenario.Db.Conversations.CountAsync();
        Assert.False((await scenario.Messages.CreateAsync(new(ConversationType.WorkspaceChannel, "Denied generic channel", [], scenario.Workspace.Id))).IsSuccess);
        Assert.False((await scenario.Messages.CreateAsync(new(ConversationType.Group, "Denied Group", [scenario.Other.Id], scenario.Workspace.Id))).IsSuccess);
        Assert.False((await scenario.Messages.LockAsync(scenario.Conversation.Id, new())).IsSuccess);
        Assert.False((await scenario.Messages.ArchiveAsync(scenario.Conversation.Id)).IsSuccess);
        Assert.Equal(count, await scenario.Db.Conversations.CountAsync());
        Assert.False((await scenario.Db.Conversations.AsNoTracking().SingleAsync()).IsLocked);
        Assert.False((await scenario.Db.Conversations.AsNoTracking().SingleAsync()).IsArchived);
        // The new parent contribution guard must not alter approved Direct archived-history posting semantics.
        var direct = new Conversation { WorkspaceId = scenario.Workspace.Id, Type = ConversationType.DirectMessage, CreatedByUserId = scenario.Actor.Id };
        scenario.Db.Conversations.Add(direct);
        scenario.Db.ConversationMembers.Add(new ConversationMember
        {
            ConversationId = direct.Id, UserId = scenario.Actor.Id, Role = ConversationMemberRole.Member,
            CanRead = true, CanPost = true, CanCreateThread = true
        });
        await scenario.Db.SaveChangesAsync();
        await ChangePersistedStateAsync(scenario, "workspace-archived");
        var sent = await scenario.Messages.SendMessageAsync(direct.Id, new("Unchanged Direct control"));
        Assert.True(sent.IsSuccess, sent.Error);
    });

    private static async Task AssertPollingAsync(Scenario scenario, bool expected)
    {
        var polling = scenario.Get<ICommunicationPollingService>();
        var unread = await polling.GetUnreadCountsAsync(new(WorkspaceId: scenario.Workspace.Id));
        Assert.True(unread.IsSuccess, unread.Error);
        Assert.Equal(expected ? 1 : 0, unread.Value!.Items.Count);
        if (expected)
        {
            var item = Assert.Single(unread.Value.Items);
            Assert.Equal(scenario.Conversation.Id, item.ConversationId);
            Assert.Equal(1, item.UnreadCount);
        }
        var updates = await polling.GetUpdatesAsync(new(Since: DateTimeOffset.UtcNow.AddMinutes(-1), WorkspaceId: scenario.Workspace.Id));
        Assert.True(updates.IsSuccess, updates.Error);
        Assert.Equal(expected ? 1 : 0, updates.Value!.Items.Count(item => item.UpdateType == "conversation"));
    }

    private static Task<int> ChangePersistedStateAsync(Scenario scenario, string state) => state switch
    {
        "workspace-archived" => scenario.Db.Workspaces.Where(item => item.Id == scenario.Workspace.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.Status, WorkspaceStatus.Archived)),
        "workspace-deleted" => scenario.Db.Workspaces.Where(item => item.Id == scenario.Workspace.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.Status, WorkspaceStatus.Deleted)),
        "workspace-soft-deleted" => scenario.Db.Workspaces.Where(item => item.Id == scenario.Workspace.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.DeletedAt, DateTimeOffset.UtcNow)),
        "workspace-readonly" => scenario.Db.WorkspaceMembers.Where(item => item.UserId == scenario.Actor.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.Role, WorkspaceRole.ReadOnly)),
        "workspace-member-suspended" => scenario.Db.WorkspaceMembers.Where(item => item.UserId == scenario.Actor.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.Status, MembershipStatus.Suspended)),
        "tenant-suspended" => scenario.Db.Tenants.Where(item => item.Id == scenario.Tenant.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.Status, TenantStatus.Suspended)),
        "tenant-member-suspended" => scenario.Db.TenantUsers.Where(item => item.UserId == scenario.Actor.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.Status, TenantUserStatus.Suspended)),
        "user-suspended" => scenario.Db.Users.Where(item => item.Id == scenario.Actor.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.Status, UserStatus.Suspended)),
        "user-deleted" => scenario.Db.Users.Where(item => item.Id == scenario.Actor.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.DeletedAt, DateTimeOffset.UtcNow)),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown lifecycle control.")
    };

    private static Task WithScenarioAsync(Func<Scenario, Task> test) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            var actor = NewUser("Writer");
            var other = NewUser("Reader");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database,
                ["FileStorage:Provider"] = "LocalFileSystem",
                ["FileStorage:RootPath"] = Path.Combine(Path.GetTempPath(), "coglatas-channel", Guid.NewGuid().ToString("N"))
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddApplication().AddInfrastructure(configuration).AddWebServices(configuration);
            services.AddSingleton<ICurrentUser>(new WriterIdentity(actor.Id));
            await using var provider = services.BuildServiceProvider(validateScopes: true);
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenantScope = scope.ServiceProvider.GetRequiredService<CurrentTenantService>();
            tenantScope.SetPlatformScope();
            var tenant = new Tenant { Name = "Channel control", DisplayName = "Channel control", Slug = $"channel-{Guid.NewGuid():N}" };
            db.Tenants.Add(tenant);
            db.Users.AddRange(actor, other);
            await db.SaveChangesAsync();
            tenantScope.SetTenant(tenant.Id, tenant.Slug);
            db.TenantUsers.Add(new TenantUser { UserId = actor.Id, Role = TenantUserRole.Owner, Status = TenantUserStatus.Active });
            db.TenantUsers.Add(new TenantUser { UserId = other.Id, Role = TenantUserRole.Member, Status = TenantUserStatus.Active });
            await db.SaveChangesAsync();
            var created = await scope.ServiceProvider.GetRequiredService<IWorkspaceService>().CreateAsync(new("Canonical channel control", null, null), Guid.NewGuid().ToString("N"));
            Assert.True(created.IsSuccess, created.Error);
            var workspace = await db.Workspaces.SingleAsync(item => item.Id == created.Value!.Id);
            var conversation = await db.Conversations.SingleAsync(item => item.WorkspaceId == workspace.Id);
            db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspace.Id, UserId = other.Id, Role = WorkspaceRole.Member, Status = MembershipStatus.Active });
            db.ConversationMembers.Add(new ConversationMember
            {
                ConversationId = conversation.Id, UserId = other.Id, Role = ConversationMemberRole.Member,
                CanRead = true, CanPost = true, CanCreateThread = true, JoinedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            await test(new(scope.ServiceProvider, db, tenant, workspace, actor, other, conversation));
        });

    private static User NewUser(string name)
    {
        var email = $"channel-{Guid.NewGuid():N}@example.test";
        return new() { Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayName = name, Status = UserStatus.Active, PasswordHash = "unused-synthetic-hash" };
    }

    private sealed record Scenario(IServiceProvider Services, AppDbContext Db, Tenant Tenant, Workspace Workspace,
        User Actor, User Other, Conversation Conversation)
    {
        public T Get<T>() where T : notnull => Services.GetRequiredService<T>();
        public IConversationService Messages => Get<IConversationService>();
    }

    private sealed class WriterIdentity(Guid id) : ICurrentUser
    {
        public Guid? UserId => id;
        public Guid? SessionId => null;
        public string? Email => null;
        public SystemRole? SystemRole => Coglatas.Domain.Enums.SystemRole.User;
        public bool IsAuthenticated => true;
    }
}
