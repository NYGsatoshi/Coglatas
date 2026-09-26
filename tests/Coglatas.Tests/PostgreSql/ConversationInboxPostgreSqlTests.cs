using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Messaging;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed class ConversationInboxPostgreSqlTests
{
    private const string PreviousMigration = "20260827154230_AddMessageThreadRootContext";

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "Issue355")]
    public async Task MigrationAddsPrivateLaterStateAndSupportsDownAndReapply()
    {
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(),
            async database =>
            {
                await PostgreSqlMigrationTestDatabase.MigrateAsync(database, PreviousMigration);
                Assert.Equal(0L, await ColumnCountAsync(database));

                await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
                Assert.Equal(1L, await ColumnCountAsync(database));
                Assert.Equal(1L, await IndexCountAsync(database));
                await using (var current = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    Assert.Empty(await current.Database.GetPendingMigrationsAsync());
                }

                await PostgreSqlMigrationTestDatabase.MigrateAsync(database, PreviousMigration);
                Assert.Equal(0L, await ColumnCountAsync(database));

                await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
                Assert.Equal(1L, await ColumnCountAsync(database));
            });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "Issue355")]
    public async Task InboxQueriesComposeRecursiveReadabilityWithUnreadMentionAndLaterPredicates()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(PostgreSqlTestEnvironment.RequireConnectionString())
            .Options;
        var currentTenant = new CurrentTenantService();
        var runId = Guid.NewGuid().ToString("N");
        var now = new DateTimeOffset(2026, 8, 29, 15, 30, 0, TimeSpan.Zero);

        await using var dbContext = new AppDbContext(options, currentTenant);
        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());

        var tenant = new Tenant
        {
            Name = $"Inbox Tenant {runId}",
            DisplayName = "Inbox Tenant",
            Slug = $"inbox-{runId}"
        };
        var actor = NewUser($"inbox-actor-{runId}@example.test", "Inbox Actor");
        var sender = NewUser($"inbox-sender-{runId}@example.test", "Inbox Sender");
        currentTenant.SetPlatformScope();
        dbContext.Tenants.Add(tenant);
        dbContext.Users.AddRange(actor, sender);
        await dbContext.SaveChangesAsync();

        currentTenant.SetTenant(tenant.Id, tenant.Slug);
        var workspace = new Workspace
        {
            Name = "Inbox Workspace",
            Slug = $"inbox-workspace-{runId}",
            CreatedByUserId = actor.Id
        };
        dbContext.TenantUsers.AddRange(
            new TenantUser { UserId = actor.Id, Status = TenantUserStatus.Active, JoinedAt = now },
            new TenantUser { UserId = sender.Id, Status = TenantUserStatus.Active, JoinedAt = now });
        dbContext.Workspaces.Add(workspace);
        dbContext.WorkspaceMembers.AddRange(
            new WorkspaceMember { WorkspaceId = workspace.Id, UserId = actor.Id, Role = WorkspaceRole.Member, Status = MembershipStatus.Active, JoinedAt = now },
            new WorkspaceMember { WorkspaceId = workspace.Id, UserId = sender.Id, Role = WorkspaceRole.Member, Status = MembershipStatus.Active, JoinedAt = now });

        var attention = NewConversation(workspace.Id, actor.Id, "Needs attention");
        var ordinary = NewConversation(workspace.Id, actor.Id, "Ordinary");
        var inaccessible = NewConversation(workspace.Id, sender.Id, "Inaccessible");
        dbContext.Conversations.AddRange(attention, ordinary, inaccessible);
        dbContext.ConversationMembers.AddRange(
            NewMember(attention.Id, actor.Id, isLater: true, now),
            NewMember(attention.Id, sender.Id, isLater: false, now),
            NewMember(ordinary.Id, actor.Id, isLater: false, now),
            NewMember(ordinary.Id, sender.Id, isLater: false, now),
            NewMember(inaccessible.Id, sender.Id, isLater: true, now));

        var mentionMessage = new Message
        {
            WorkspaceId = workspace.Id,
            ConversationId = attention.Id,
            AuthorUserId = sender.Id,
            Body = "Authorized mention",
            CreatedAt = now.AddMinutes(1)
        };
        dbContext.Messages.AddRange(
            mentionMessage,
            new Message
            {
                WorkspaceId = workspace.Id,
                ConversationId = ordinary.Id,
                AuthorUserId = actor.Id,
                Body = "Own message",
                CreatedAt = now.AddMinutes(2)
            },
            new Message
            {
                WorkspaceId = workspace.Id,
                ConversationId = inaccessible.Id,
                AuthorUserId = sender.Id,
                Body = "Restricted message",
                CreatedAt = now.AddMinutes(3)
            });
        dbContext.Notifications.Add(new Notification
        {
            UserId = actor.Id,
            NotificationType = NotificationType.Mention,
            Title = "Mention",
            RelatedEntityType = "Message",
            RelatedEntityId = mentionMessage.Id,
            CreatedAt = now.AddMinutes(1)
        });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var repository = new MessagingRepository(dbContext);
        var all = await repository.ListInboxForUserAsync(actor.Id, ConversationInboxView.All, 1, 20);
        Assert.Equal(new ConversationInboxCountsResponse(2, 1, 1, 1), all.Counts);
        Assert.Equal(2, all.Page.TotalCount);
        Assert.DoesNotContain(all.Page.Items, item => item.Id == inaccessible.Id);

        var unread = await repository.ListInboxForUserAsync(actor.Id, ConversationInboxView.Unread, 1, 20);
        Assert.Equal(attention.Id, Assert.Single(unread.Page.Items).Id);
        var mentions = await repository.ListInboxForUserAsync(actor.Id, ConversationInboxView.Mentions, 1, 20);
        Assert.Equal(attention.Id, Assert.Single(mentions.Page.Items).Id);
        var later = await repository.ListInboxForUserAsync(actor.Id, ConversationInboxView.Later, 1, 20);
        Assert.Equal(attention.Id, Assert.Single(later.Page.Items).Id);
    }

    private static Task<long> ColumnCountAsync(string database) =>
        PostgreSqlMigrationTestDatabase.ScalarAsync<long>(
            database,
            """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'conversation_members'
              AND column_name = 'IsLater'
            """);

    private static Task<long> IndexCountAsync(string database) =>
        PostgreSqlMigrationTestDatabase.ScalarAsync<long>(
            database,
            """
            SELECT COUNT(*)
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename = 'conversation_members'
              AND indexname = 'IX_conversation_members_TenantId_UserId_IsLater'
            """);

    private static User NewUser(string email, string displayName) => new()
    {
        DisplayName = displayName,
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        Status = UserStatus.Active
    };

    private static Conversation NewConversation(Guid workspaceId, Guid creatorUserId, string title) => new()
    {
        WorkspaceId = workspaceId,
        Type = ConversationType.DirectMessage,
        Title = title,
        CreatedByUserId = creatorUserId
    };

    private static ConversationMember NewMember(
        Guid conversationId,
        Guid userId,
        bool isLater,
        DateTimeOffset joinedAt) => new()
    {
        ConversationId = conversationId,
        UserId = userId,
        JoinedAt = joinedAt,
        IsLater = isLater
    };
}
