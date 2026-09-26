using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

[Collection("PostgreSqlTaskV1")]
[Trait("Scope", "Issue368")]
public sealed class MessageFollowUpPostgreSqlTests
{
    private const string PreviousMigration = "20260829153230_AddConversationInboxLater";

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task MigrationCreatesPrivateUniqueSavedMessageStateAndSupportsDownAndReapply()
    {
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(),
            async database =>
            {
                await PostgreSqlMigrationTestDatabase.MigrateAsync(database, PreviousMigration);
                Assert.Equal(0L, await TableCountAsync(database));

                await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
                Assert.Equal(1L, await TableCountAsync(database));
                Assert.Equal(1L, await IndexCountAsync(database, "IX_message_follow_ups_TenantId_UserId_MessageId"));
                Assert.Equal(1L, await IndexCountAsync(database, "IX_message_follow_ups_TenantId_UserId_CreatedAt_Id"));
                await using (var current = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    Assert.Empty(await current.Database.GetPendingMigrationsAsync());
                }

                await PostgreSqlMigrationTestDatabase.MigrateAsync(database, PreviousMigration);
                Assert.Equal(0L, await TableCountAsync(database));
                await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
                Assert.Equal(1L, await TableCountAsync(database));
            });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task SavedMessagePagingComposesCurrentReadabilityAndExcludesRevokedRowsFromCounts()
    {
        var currentTenant = new CurrentTenantService();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(PostgreSqlTestEnvironment.RequireConnectionString())
            .Options;
        await using var dbContext = new AppDbContext(options, currentTenant);
        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());

        var runId = Guid.NewGuid().ToString("N");
        var now = new DateTimeOffset(2026, 8, 29, 17, 30, 0, TimeSpan.Zero);
        var tenant = new Tenant { Name = $"Follow-up {runId}", DisplayName = "Follow-up", Slug = $"follow-up-{runId}" };
        var actor = NewUser($"follow-up-actor-{runId}@example.test", "Follow-up Actor");
        var sender = NewUser($"follow-up-sender-{runId}@example.test", "Follow-up Sender");
        currentTenant.SetPlatformScope();
        dbContext.Tenants.Add(tenant);
        dbContext.Users.AddRange(actor, sender);
        await dbContext.SaveChangesAsync();

        currentTenant.SetTenant(tenant.Id, tenant.Slug);
        var workspace = new Workspace { Name = "Follow-up Workspace", Slug = $"follow-up-workspace-{runId}", CreatedByUserId = actor.Id };
        dbContext.Workspaces.Add(workspace);
        dbContext.TenantUsers.AddRange(
            new TenantUser { UserId = actor.Id, Status = TenantUserStatus.Active, JoinedAt = now },
            new TenantUser { UserId = sender.Id, Status = TenantUserStatus.Active, JoinedAt = now });

        var readable = NewConversation(workspace.Id, actor.Id, "Readable");
        var revoked = NewConversation(workspace.Id, sender.Id, "Revoked");
        dbContext.Conversations.AddRange(readable, revoked);
        dbContext.ConversationMembers.AddRange(
            NewMember(readable.Id, actor.Id, now),
            NewMember(readable.Id, sender.Id, now),
            NewMember(revoked.Id, actor.Id, now, canRead: false),
            NewMember(revoked.Id, sender.Id, now));
        var readableMessage = NewMessage(workspace.Id, readable.Id, sender.Id, "Readable saved body", now);
        var revokedMessage = NewMessage(workspace.Id, revoked.Id, sender.Id, "Revoked saved body", now.AddMinutes(1));
        dbContext.Messages.AddRange(readableMessage, revokedMessage);
        dbContext.MessageFollowUps.AddRange(
            new MessageFollowUp { UserId = actor.Id, MessageId = readableMessage.Id, CreatedAt = now.AddMinutes(2) },
            new MessageFollowUp { UserId = actor.Id, MessageId = revokedMessage.Id, CreatedAt = now.AddMinutes(3) });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var messaging = new MessagingRepository(dbContext);
        var repository = new MessageFollowUpRepository(dbContext, messaging);
        var page = await repository.ListVisibleAsync(actor.Id, 1, 1);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(readableMessage.Id, Assert.Single(page.Items).MessageId);
        Assert.DoesNotContain(page.Items, item => item.Message?.Body == revokedMessage.Body);

        var membership = await dbContext.ConversationMembers.SingleAsync(item =>
            item.ConversationId == readable.Id && item.UserId == actor.Id);
        membership.CanRead = false;
        membership.RemovedAt = now.AddMinutes(4);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var afterRevocation = await repository.ListVisibleAsync(actor.Id, 1, 20);
        Assert.Equal(0, afterRevocation.TotalCount);
        Assert.Empty(afterRevocation.Items);
        Assert.Equal(2, await dbContext.MessageFollowUps.CountAsync(item => item.UserId == actor.Id));
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task ConcurrentSaveAndRemoveReconcileTheExactPrivateIdentity()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        var seedTenant = new CurrentTenantService();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using var seed = new AppDbContext(options, seedTenant);
        Assert.Empty(await seed.Database.GetPendingMigrationsAsync());

        var runId = Guid.NewGuid().ToString("N");
        var now = new DateTimeOffset(2026, 8, 29, 18, 0, 0, TimeSpan.Zero);
        var tenant = new Tenant { Name = $"Follow-up race {runId}", DisplayName = "Follow-up race", Slug = $"follow-up-race-{runId}" };
        var actor = NewUser($"follow-up-race-actor-{runId}@example.test", "Follow-up Actor");
        seedTenant.SetPlatformScope();
        seed.Tenants.Add(tenant);
        seed.Users.Add(actor);
        await seed.SaveChangesAsync();

        seedTenant.SetTenant(tenant.Id, tenant.Slug);
        var workspace = new Workspace { Name = "Follow-up Race Workspace", Slug = $"follow-up-race-workspace-{runId}", CreatedByUserId = actor.Id };
        seed.Workspaces.Add(workspace);
        seed.TenantUsers.Add(new TenantUser { UserId = actor.Id, Status = TenantUserStatus.Active, JoinedAt = now });
        var conversation = NewConversation(workspace.Id, actor.Id, "Follow-up race");
        seed.Conversations.Add(conversation);
        seed.ConversationMembers.Add(NewMember(conversation.Id, actor.Id, now));
        var message = NewMessage(workspace.Id, conversation.Id, actor.Id, "Concurrent private marker", now);
        seed.Messages.Add(message);
        await seed.SaveChangesAsync();

        var tenantOne = new CurrentTenantService();
        var tenantTwo = new CurrentTenantService();
        tenantOne.SetTenant(tenant.Id, tenant.Slug);
        tenantTwo.SetTenant(tenant.Id, tenant.Slug);
        await using var contextOne = new AppDbContext(options, tenantOne);
        await using var contextTwo = new AppDbContext(options, tenantTwo);
        var pendingOne = new MessageFollowUp { UserId = actor.Id, MessageId = message.Id, CreatedAt = now.AddMinutes(1) };
        var pendingTwo = new MessageFollowUp { UserId = actor.Id, MessageId = message.Id, CreatedAt = now.AddMinutes(2) };
        contextOne.MessageFollowUps.Add(pendingOne);
        contextTwo.MessageFollowUps.Add(pendingTwo);

        var saved = await Task.WhenAll(
            new EfMessageFollowUpCommitCoordinator(contextOne).SaveAsync(pendingOne),
            new EfMessageFollowUpCommitCoordinator(contextTwo).SaveAsync(pendingTwo));
        Assert.Single(saved, result => result.WasReconciled);
        Assert.All(saved, result => Assert.Equal(message.Id, result.FollowUp.MessageId));

        contextOne.ChangeTracker.Clear();
        contextTwo.ChangeTracker.Clear();
        var removeOne = await contextOne.MessageFollowUps.SingleAsync(item =>
            item.UserId == actor.Id && item.MessageId == message.Id);
        var removeTwo = await contextTwo.MessageFollowUps.SingleAsync(item =>
            item.UserId == actor.Id && item.MessageId == message.Id);
        contextOne.MessageFollowUps.Remove(removeOne);
        contextTwo.MessageFollowUps.Remove(removeTwo);

        var removed = await Task.WhenAll(
            new EfMessageFollowUpCommitCoordinator(contextOne).RemoveAsync(removeOne),
            new EfMessageFollowUpCommitCoordinator(contextTwo).RemoveAsync(removeTwo));
        Assert.Single(removed, value => value);
        Assert.Single(removed, value => !value);

        await using var verification = new AppDbContext(options, tenantOne);
        Assert.False(await verification.MessageFollowUps.AnyAsync(item =>
            item.UserId == actor.Id && item.MessageId == message.Id));
    }

    private static Task<long> TableCountAsync(string database) =>
        PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, """
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = current_schema() AND table_name = 'message_follow_ups';
            """);

    private static Task<long> IndexCountAsync(string database, string indexName) =>
        PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, """
            SELECT COUNT(*) FROM pg_indexes
            WHERE schemaname = current_schema() AND tablename = 'message_follow_ups' AND indexname = @indexName;
            """, ("indexName", indexName));

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

    private static ConversationMember NewMember(Guid conversationId, Guid userId, DateTimeOffset joinedAt, bool canRead = true) => new()
    {
        ConversationId = conversationId,
        UserId = userId,
        JoinedAt = joinedAt,
        CanRead = canRead,
        CanPost = canRead
    };

    private static Message NewMessage(Guid workspaceId, Guid conversationId, Guid authorUserId, string body, DateTimeOffset createdAt) => new()
    {
        WorkspaceId = workspaceId,
        ConversationId = conversationId,
        AuthorUserId = authorUserId,
        Body = body,
        CreatedAt = createdAt
    };
}
