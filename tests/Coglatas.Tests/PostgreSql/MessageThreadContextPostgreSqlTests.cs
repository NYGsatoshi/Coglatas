using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Coglatas.Tests.PostgreSql;

[Collection("PostgreSqlTaskV1")]
[Trait("Scope", "Issue362")]
public sealed class MessageThreadContextPostgreSqlTests
{
    private const string PreviousMigration = "20260825081645_AddTaskExecutionScopeFoundation";

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task MigrationIsAdditiveIndexedConstrainedAndLeavesLegacyMessagesUnlinked()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database, PreviousMigration);
            var graph = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, $"message-thread-{Guid.NewGuid():N}");
            var conversationId = Guid.NewGuid();
            var legacyMessageId = Guid.NewGuid();
            var now = new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero);
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
INSERT INTO conversations ("Id", "TenantId", "WorkspaceId", "Type", "Title", "IsArchived", "IsLocked", "CreatedByUserId", "CreatedAt")
VALUES (@conversationId, @tenantId, @workspaceId, 'DirectMessage', 'Legacy conversation', false, false, @userId, @now);
INSERT INTO messages ("Id", "TenantId", "WorkspaceId", "ConversationId", "AuthorUserId", "Body", "Version", "CreatedAt")
VALUES (@messageId, @tenantId, @workspaceId, @conversationId, @userId, 'legacy root', 1, @now);
""",
                ("conversationId", conversationId), ("messageId", legacyMessageId),
                ("tenantId", graph.TenantId), ("workspaceId", graph.WorkspaceId),
                ("userId", graph.UserId), ("now", now));

            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);

            Assert.Equal(
                "uuid:YES",
                await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database, """
SELECT data_type || ':' || is_nullable
FROM information_schema.columns
WHERE table_schema = current_schema() AND table_name = 'messages' AND column_name = 'ThreadRootMessageId';
"""));
            Assert.Equal(
                1,
                await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, """
SELECT COUNT(*) FROM messages WHERE "Id" = @messageId AND "ThreadRootMessageId" IS NULL;
""", ("messageId", legacyMessageId)));

            var indexDefinition = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database, """
SELECT indexdef FROM pg_indexes
WHERE schemaname = current_schema() AND tablename = 'messages' AND indexname = 'IX_messages_thread_replies';
""");
            Assert.Contains(
                "(\"TenantId\", \"ConversationId\", \"ThreadRootMessageId\", \"CreatedAt\", \"Id\")",
                indexDefinition,
                StringComparison.Ordinal);

            var idempotencyIndexDefinition = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database, """
SELECT indexdef FROM pg_indexes
WHERE schemaname = current_schema() AND tablename = 'messages' AND indexname = @indexName;
""", ("indexName", EfMessageIdempotencyCommitCoordinator.ClientRequestIdentityConstraint));
            Assert.Contains("CREATE UNIQUE INDEX", idempotencyIndexDefinition, StringComparison.Ordinal);
            Assert.Contains(
                "(\"TenantId\", \"ConversationId\", \"AuthorUserId\", \"ClientRequestId\")",
                idempotencyIndexDefinition,
                StringComparison.Ordinal);
            Assert.Contains("ClientRequestId", idempotencyIndexDefinition, StringComparison.Ordinal);

            var checkDefinition = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database, """
SELECT pg_get_constraintdef(oid) FROM pg_constraint
WHERE conname = 'CK_messages_thread_root_not_self';
""");
            Assert.Contains("ThreadRootMessageId", checkDefinition, StringComparison.Ordinal);
            Assert.Contains("Id", checkDefinition, StringComparison.Ordinal);

            var foreignKeyDefinition = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database, """
SELECT pg_get_constraintdef(oid) FROM pg_constraint
WHERE conname = 'FK_messages_messages_ThreadRootMessageId';
""");
            Assert.Contains("FOREIGN KEY (\"ThreadRootMessageId\") REFERENCES messages(\"Id\")", foreignKeyDefinition, StringComparison.Ordinal);
            Assert.Contains("ON DELETE RESTRICT", foreignKeyDefinition, StringComparison.Ordinal);

            var selfReference = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
INSERT INTO messages ("Id", "TenantId", "WorkspaceId", "ConversationId", "AuthorUserId", "Body", "Version", "CreatedAt", "ThreadRootMessageId")
VALUES (@id, @tenantId, @workspaceId, @conversationId, @userId, 'invalid self reply', 1, @now, @id);
""", ("id", Guid.NewGuid()), ("tenantId", graph.TenantId), ("workspaceId", graph.WorkspaceId),
                    ("conversationId", conversationId), ("userId", graph.UserId), ("now", now)));
            Assert.Equal(PostgresErrorCodes.CheckViolation, selfReference.SqlState);

            var missingRoot = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
INSERT INTO messages ("Id", "TenantId", "WorkspaceId", "ConversationId", "AuthorUserId", "Body", "Version", "CreatedAt", "ThreadRootMessageId")
VALUES (@id, @tenantId, @workspaceId, @conversationId, @userId, 'missing root reply', 1, @now, @missingRootId);
""", ("id", Guid.NewGuid()), ("tenantId", graph.TenantId), ("workspaceId", graph.WorkspaceId),
                    ("conversationId", conversationId), ("userId", graph.UserId), ("now", now),
                    ("missingRootId", Guid.NewGuid())));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, missingRoot.SqlState);

            await PostgreSqlMigrationTestDatabase.MigrateAsync(database, PreviousMigration);
            Assert.Equal(
                0,
                await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, """
SELECT COUNT(*) FROM information_schema.columns
WHERE table_schema = current_schema() AND table_name = 'messages' AND column_name = 'ThreadRootMessageId';
"""));
            Assert.Equal(
                1,
                await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, "SELECT COUNT(*) FROM messages WHERE \"Id\" = @id;", ("id", legacyMessageId)));

            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            Assert.Equal(
                1,
                await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, """
SELECT COUNT(*) FROM messages WHERE "Id" = @messageId AND "ThreadRootMessageId" IS NULL;
""", ("messageId", legacyMessageId)));
        });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task RepositoryScopesRepliesByTenantConversationAndRootAndKeepsTombstones()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            var graphA = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, $"message-thread-a-{Guid.NewGuid():N}");
            var graphB = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, $"message-thread-b-{Guid.NewGuid():N}");
            var conversationA = Guid.NewGuid();
            var conversationOther = Guid.NewGuid();
            var conversationB = Guid.NewGuid();
            var rootId = Guid.NewGuid();
            var ordinaryDeletedId = Guid.NewGuid();
            var canonicalReplyId = Guid.NewGuid();
            var now = new DateTimeOffset(2026, 8, 28, 1, 0, 0, TimeSpan.Zero);
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
INSERT INTO tenant_users ("Id", "TenantId", "UserId", "Role", "Status", "JoinedAt", "CreatedAt")
VALUES (@tenantUserAId, @tenantA, @userA, 'Member', 'Active', @now, @now);
INSERT INTO conversations ("Id", "TenantId", "WorkspaceId", "Type", "Title", "IsArchived", "IsLocked", "CreatedByUserId", "CreatedAt") VALUES
(@conversationA, @tenantA, @workspaceA, 'DirectMessage', 'A', false, false, @userA, @now),
(@conversationOther, @tenantA, @workspaceA, 'DirectMessage', 'A other', false, false, @userA, @now),
(@conversationB, @tenantB, @workspaceB, 'DirectMessage', 'B', false, false, @userB, @now);
INSERT INTO conversation_members ("Id", "TenantId", "ConversationId", "UserId", "Role", "CanRead", "CanPost", "CanManageMembers", "CanCreateThread", "JoinedAt", "IsMuted", "IsArchived", "CreatedAt")
VALUES (@conversationMemberAId, @tenantA, @conversationA, @userA, 'Member', true, true, false, true, @now, false, false, @now);
INSERT INTO messages ("Id", "TenantId", "WorkspaceId", "ConversationId", "AuthorUserId", "Body", "Version", "CreatedAt", "DeletedAt", "DeletedByUserId", "DeleteReason") VALUES
(@rootId, @tenantA, @workspaceA, @conversationA, @userA, 'deleted root secret', 1, @now, @deletedAt, @userA, 'author_delete'),
(@ordinaryDeletedId, @tenantA, @workspaceA, @conversationA, @userA, 'ordinary deleted secret', 1, @ordinaryAt, @deletedAt, @userA, 'author_delete');
INSERT INTO messages ("Id", "TenantId", "WorkspaceId", "ConversationId", "AuthorUserId", "Body", "Version", "CreatedAt", "ThreadRootMessageId", "DeletedAt", "DeletedByUserId", "DeleteReason") VALUES
(@canonicalReplyId, @tenantA, @workspaceA, @conversationA, @userA, '', 1, @replyAt, @rootId, @deletedAt, @userA, 'author_delete'),
(@crossConversationReplyId, @tenantA, @workspaceA, @conversationOther, @userA, 'cross conversation secret', 1, @replyAt, @rootId, NULL, NULL, NULL),
(@crossTenantReplyId, @tenantB, @workspaceB, @conversationB, @userB, 'cross tenant secret', 1, @replyAt, @rootId, NULL, NULL, NULL);
""",
                ("conversationA", conversationA), ("conversationOther", conversationOther), ("conversationB", conversationB),
                ("tenantA", graphA.TenantId), ("workspaceA", graphA.WorkspaceId), ("userA", graphA.UserId),
                ("tenantB", graphB.TenantId), ("workspaceB", graphB.WorkspaceId), ("userB", graphB.UserId),
                ("tenantUserAId", Guid.NewGuid()), ("conversationMemberAId", Guid.NewGuid()),
                ("rootId", rootId), ("ordinaryDeletedId", ordinaryDeletedId), ("canonicalReplyId", canonicalReplyId),
                ("crossConversationReplyId", Guid.NewGuid()), ("crossTenantReplyId", Guid.NewGuid()),
                ("now", now), ("ordinaryAt", now.AddMinutes(-1)),
                ("replyAt", now.AddMinutes(1)), ("deletedAt", now.AddMinutes(2)));

            var currentTenant = new CurrentTenantService();
            currentTenant.SetTenant(graphA.TenantId, $"message-thread-a-{Guid.NewGuid():N}");
            await using var context = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).Options,
                currentTenant);
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            var repository = new MessagingRepository(context);

            var replies = await repository.ListThreadRepliesAsync(conversationA, rootId, 100, before: null);
            var summary = await repository.GetThreadSummaryAsync(conversationA, rootId, participantLimit: 3);
            var timeline = await repository.ListMessagesAsync(conversationA, 100, before: null);

            var reply = Assert.Single(replies.Items);
            Assert.Equal(canonicalReplyId, reply.Id);
            Assert.NotNull(reply.DeletedAt);
            Assert.Equal(1, replies.TotalCount);
            Assert.Equal(1, summary.ReplyCount);
            Assert.Equal(now.AddMinutes(1), summary.LatestReplyAt);
            Assert.Equal(new[] { "Migration user" }, summary.ParticipantDisplayNames);
            var timelineRoot = Assert.Single(timeline.Items);
            Assert.Equal(rootId, timelineRoot.Id);
            Assert.NotNull(timelineRoot.DeletedAt);
            Assert.DoesNotContain(timeline.Items, message => message.Id == ordinaryDeletedId);
            Assert.Equal(1, timeline.TotalCount);
        });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "Issue368")]
    public async Task ExactReplyContextIncludesAnAnchorOutsideTheLatestHundredWithoutIncreasingTheBound()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            var graph = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, $"thread-anchor-{Guid.NewGuid():N}");
            var conversationId = Guid.NewGuid();
            var rootId = Guid.NewGuid();
            var oldestReplyId = Guid.NewGuid();
            var now = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
INSERT INTO tenant_users ("Id", "TenantId", "UserId", "Role", "Status", "JoinedAt", "CreatedAt")
VALUES (@tenantUserId, @tenantId, @userId, 'Member', 'Active', @now, @now);
INSERT INTO conversations ("Id", "TenantId", "WorkspaceId", "Type", "Title", "IsArchived", "IsLocked", "CreatedByUserId", "CreatedAt")
VALUES (@conversationId, @tenantId, @workspaceId, 'DirectMessage', 'Exact reply context', false, false, @userId, @now);
INSERT INTO conversation_members ("Id", "TenantId", "ConversationId", "UserId", "Role", "CanRead", "CanPost", "CanManageMembers", "CanCreateThread", "JoinedAt", "IsMuted", "IsArchived", "CreatedAt")
VALUES (@memberId, @tenantId, @conversationId, @userId, 'Member', true, true, false, true, @now, false, false, @now);
INSERT INTO messages ("Id", "TenantId", "WorkspaceId", "ConversationId", "AuthorUserId", "Body", "Version", "CreatedAt")
VALUES (@rootId, @tenantId, @workspaceId, @conversationId, @userId, 'root', 1, @now);
INSERT INTO messages ("Id", "TenantId", "WorkspaceId", "ConversationId", "AuthorUserId", "Body", "Version", "CreatedAt", "ThreadRootMessageId")
VALUES (@oldestReplyId, @tenantId, @workspaceId, @conversationId, @userId, 'anchored oldest reply', 1, @now + INTERVAL '1 second', @rootId);
INSERT INTO messages ("Id", "TenantId", "WorkspaceId", "ConversationId", "AuthorUserId", "Body", "Version", "CreatedAt", "ThreadRootMessageId")
SELECT gen_random_uuid(), @tenantId, @workspaceId, @conversationId, @userId,
       'bounded reply ' || series.value, 1,
       @now + (series.value * INTERVAL '1 second'), @rootId
FROM generate_series(2, 101) AS series(value);
""",
                ("tenantUserId", Guid.NewGuid()), ("memberId", Guid.NewGuid()),
                ("tenantId", graph.TenantId), ("workspaceId", graph.WorkspaceId),
                ("userId", graph.UserId), ("conversationId", conversationId),
                ("rootId", rootId), ("oldestReplyId", oldestReplyId), ("now", now));

            var currentTenant = new CurrentTenantService();
            currentTenant.SetTenant(graph.TenantId, "thread-anchor-tenant");
            await using var context = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).Options,
                currentTenant);
            var page = await new MessagingRepository(context).ListThreadReplyContextAsync(
                conversationId,
                rootId,
                oldestReplyId,
                100);

            Assert.Equal(101, page.TotalCount);
            Assert.Equal(100, page.Items.Count);
            Assert.Equal(oldestReplyId, page.Items[0].Id);
            Assert.Equal("anchored oldest reply", page.Items[0].Body);
            Assert.Contains(page.Items, message => message.Body == "bounded reply 101");
            Assert.DoesNotContain(page.Items, message => message.Body == "bounded reply 2");
            Assert.Equal(
                page.Items.Select(message => message.Id),
                page.Items
                    .OrderBy(message => message.CreatedAt)
                    .ThenBy(message => message.Id)
                    .Select(message => message.Id));
        });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task ThreadParticipantProjectionBoundsRowsPerRootInsidePostgreSql()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            var graph = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, $"thread-participants-{Guid.NewGuid():N}");
            var authorIds = new[] { graph.UserId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            for (var index = 1; index < authorIds.Length; index++)
            {
                await TaskV1MigrationRawSqlSeed.AddUserAsync(database, graph, authorIds[index], $"Participant {index}");
            }

            var conversationId = Guid.NewGuid();
            var rootIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
            var now = new DateTimeOffset(2026, 8, 28, 2, 0, 0, TimeSpan.Zero);
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
INSERT INTO conversations ("Id", "TenantId", "WorkspaceId", "Type", "Title", "IsArchived", "IsLocked", "CreatedByUserId", "CreatedAt")
VALUES (@conversationId, @tenantId, @workspaceId, 'DirectMessage', 'Bounded participants', false, false, @userId, @now);
INSERT INTO tenant_users ("Id", "TenantId", "UserId", "Role", "Status", "JoinedAt", "CreatedAt")
VALUES (@tenantUserId, @tenantId, @userId, 'Member', 'Active', @now, @now);
INSERT INTO messages ("Id", "TenantId", "WorkspaceId", "ConversationId", "AuthorUserId", "Body", "Version", "CreatedAt") VALUES
(@rootA, @tenantId, @workspaceId, @conversationId, @userId, 'root A', 1, @now),
(@rootB, @tenantId, @workspaceId, @conversationId, @userId, 'root B', 1, @now);
""", ("conversationId", conversationId), ("tenantId", graph.TenantId),
                ("workspaceId", graph.WorkspaceId), ("userId", graph.UserId),
                ("tenantUserId", Guid.NewGuid()), ("rootA", rootIds[0]), ("rootB", rootIds[1]), ("now", now));

            foreach (var authorId in authorIds)
            {
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
INSERT INTO conversation_members ("Id", "TenantId", "ConversationId", "UserId", "Role", "CanRead", "CanPost", "CanManageMembers", "CanCreateThread", "JoinedAt", "IsMuted", "IsArchived", "CreatedAt")
VALUES (@id, @tenantId, @conversationId, @userId, 'Member', true, true, false, true, @now, false, false, @now);
""", ("id", Guid.NewGuid()), ("tenantId", graph.TenantId), ("conversationId", conversationId),
                    ("userId", authorId), ("now", now));
            }

            foreach (var rootId in rootIds)
            {
                for (var index = 0; index < authorIds.Length; index++)
                {
                    await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
INSERT INTO messages ("Id", "TenantId", "WorkspaceId", "ConversationId", "AuthorUserId", "Body", "Version", "CreatedAt", "ThreadRootMessageId")
VALUES (@id, @tenantId, @workspaceId, @conversationId, @authorUserId, @body, 1, @createdAt, @rootId);
""", ("id", Guid.NewGuid()), ("tenantId", graph.TenantId), ("workspaceId", graph.WorkspaceId),
                        ("conversationId", conversationId), ("authorUserId", authorIds[index]),
                        ("body", $"reply {index}"), ("createdAt", now.AddMinutes(index + 1)), ("rootId", rootId));
                }
            }

            var currentTenant = new CurrentTenantService();
            currentTenant.SetTenant(graph.TenantId, "thread-participant-tenant");
            await using var context = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).Options,
                currentTenant);
            var summaries = await new MessagingRepository(context)
                .GetThreadSummariesAsync(conversationId, rootIds, participantLimit: 3);

            Assert.Equal(rootIds, summaries.Keys.OrderBy(id => Array.IndexOf(rootIds, id)));
            Assert.All(summaries.Values, summary => Assert.Equal(3, summary.ParticipantDisplayNames.Count));
            Assert.Equal(rootIds.Length * 3, summaries.Values.Sum(summary => summary.ParticipantDisplayNames.Count));
        });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task ThreadAuthorNamesRequireHistoricalTenantAndConversationProof()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            var graphA = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, $"thread-author-a-{Guid.NewGuid():N}");
            var graphB = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, $"thread-author-b-{Guid.NewGuid():N}");
            var conversationId = Guid.NewGuid();
            var rootId = Guid.NewGuid();
            var historicalReplyId = Guid.NewGuid();
            var corruptReplyId = Guid.NewGuid();
            var now = new DateTimeOffset(2026, 8, 28, 2, 30, 0, TimeSpan.Zero);
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
UPDATE users SET "DisplayName" = 'Historical tenant A author', "Status" = 'Archived', "DeletedAt" = @now
WHERE "Id" = @userA;
UPDATE users SET "DisplayName" = 'TENANT B SECRET AUTHOR'
WHERE "Id" = @userB;
INSERT INTO tenant_users ("Id", "TenantId", "UserId", "Role", "Status", "JoinedAt", "CreatedAt") VALUES
(@tenantUserAId, @tenantA, @userA, 'Member', 'Left', @now, @now),
(@tenantUserBId, @tenantB, @userB, 'Member', 'Active', @now, @now);
INSERT INTO conversations ("Id", "TenantId", "WorkspaceId", "Type", "Title", "IsArchived", "IsLocked", "CreatedByUserId", "CreatedAt")
VALUES (@conversationId, @tenantA, @workspaceA, 'DirectMessage', 'Historical author proof', false, false, @userA, @now);
INSERT INTO conversation_members ("Id", "TenantId", "ConversationId", "UserId", "Role", "CanRead", "CanPost", "CanManageMembers", "CanCreateThread", "JoinedAt", "LeftAt", "IsMuted", "IsArchived", "CreatedAt")
VALUES (@memberAId, @tenantA, @conversationId, @userA, 'Member', true, false, false, false, @joinedAt, @now, false, false, @joinedAt);
INSERT INTO messages ("Id", "TenantId", "WorkspaceId", "ConversationId", "AuthorUserId", "Body", "Version", "CreatedAt")
VALUES (@rootId, @tenantA, @workspaceA, @conversationId, @userB, 'corrupt-author root', 1, @now);
INSERT INTO messages ("Id", "TenantId", "WorkspaceId", "ConversationId", "AuthorUserId", "Body", "Version", "CreatedAt", "ThreadRootMessageId") VALUES
(@historicalReplyId, @tenantA, @workspaceA, @conversationId, @userA, 'historical reply', 1, @historicalAt, @rootId),
(@corruptReplyId, @tenantA, @workspaceA, @conversationId, @userB, 'corrupt-author reply', 1, @corruptAt, @rootId);
""",
                ("tenantA", graphA.TenantId), ("workspaceA", graphA.WorkspaceId), ("userA", graphA.UserId),
                ("tenantB", graphB.TenantId), ("userB", graphB.UserId),
                ("tenantUserAId", Guid.NewGuid()), ("tenantUserBId", Guid.NewGuid()),
                ("conversationId", conversationId), ("memberAId", Guid.NewGuid()),
                ("rootId", rootId), ("historicalReplyId", historicalReplyId), ("corruptReplyId", corruptReplyId),
                ("joinedAt", now.AddDays(-10)), ("historicalAt", now.AddMinutes(1)),
                ("corruptAt", now.AddMinutes(2)), ("now", now));

            var currentTenant = new CurrentTenantService();
            currentTenant.SetTenant(graphA.TenantId, "thread-author-proof-tenant");
            await using var context = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).Options,
                currentTenant);
            var repository = new MessagingRepository(context);

            var root = await repository.GetMessageAsync(rootId);
            var replies = await repository.ListThreadRepliesAsync(conversationId, rootId, 100, before: null);
            var summary = await repository.GetThreadSummaryAsync(conversationId, rootId, participantLimit: 3);
            var summaries = await repository.GetThreadSummariesAsync(conversationId, [rootId], participantLimit: 3);

            Assert.NotNull(root);
            Assert.Null(root.AuthorUser);
            Assert.Equal("Historical tenant A author", Assert.Single(replies.Items, item => item.Id == historicalReplyId).AuthorUser?.DisplayName);
            Assert.Null(Assert.Single(replies.Items, item => item.Id == corruptReplyId).AuthorUser);
            Assert.Equal(2, summary.ReplyCount);
            Assert.Equal(new[] { "Historical tenant A author" }, summary.ParticipantDisplayNames);
            Assert.Equal(summary.ParticipantDisplayNames, summaries[rootId].ParticipantDisplayNames);
            Assert.DoesNotContain("TENANT B SECRET AUTHOR", summary.ParticipantDisplayNames);
        });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task ConcurrentClientRequestCommitReturnsOneWinnerAndRollsBackLosingSideEffects()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            var graph = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, $"thread-race-{Guid.NewGuid():N}");
            var conversationId = Guid.NewGuid();
            var rootId = Guid.NewGuid();
            var otherRootId = Guid.NewGuid();
            var clientRequestId = Guid.NewGuid();
            var now = new DateTimeOffset(2026, 8, 28, 3, 0, 0, TimeSpan.Zero);
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
INSERT INTO conversations ("Id", "TenantId", "WorkspaceId", "Type", "Title", "IsArchived", "IsLocked", "CreatedByUserId", "CreatedAt")
VALUES (@conversationId, @tenantId, @workspaceId, 'DirectMessage', 'Idempotency race', false, false, @userId, @now);
INSERT INTO messages ("Id", "TenantId", "WorkspaceId", "ConversationId", "AuthorUserId", "Body", "Version", "CreatedAt") VALUES
(@rootId, @tenantId, @workspaceId, @conversationId, @userId, 'root', 1, @now),
(@otherRootId, @tenantId, @workspaceId, @conversationId, @userId, 'other root', 1, @now);
""", ("conversationId", conversationId), ("tenantId", graph.TenantId),
                ("workspaceId", graph.WorkspaceId), ("userId", graph.UserId),
                ("rootId", rootId), ("otherRootId", otherRootId), ("now", now));

            async Task<MessageIdempotencyCommitResult> CommitAsync(string body)
            {
                var tenant = new CurrentTenantService();
                tenant.SetTenant(graph.TenantId, "thread-race-tenant");
                await using var context = new AppDbContext(
                    new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).Options,
                    tenant);
                var message = NewReply(body, rootId);
                context.Messages.Add(message);
                context.AuditLogs.Add(new AuditLog
                {
                    TenantId = graph.TenantId,
                    ActorUserId = graph.UserId,
                    Action = "communication.thread_reply_posted",
                    EntityType = "Message",
                    EntityId = message.Id,
                    CorrelationId = clientRequestId.ToString("D"),
                    CreatedAt = now
                });
                context.Notifications.Add(new Notification
                {
                    TenantId = graph.TenantId,
                    UserId = graph.UserId,
                    NotificationType = NotificationType.Message,
                    Title = $"thread-race-{clientRequestId:D}",
                    RelatedEntityType = "Message",
                    RelatedEntityId = message.Id,
                    CreatedAt = now,
                    StateVersion = 1
                });
                context.OutboxEvents.Add(new OutboxEvent(Guid.NewGuid())
                {
                    TenantId = graph.TenantId,
                    EventType = "Messaging.MessageCreated.v1",
                    PayloadSchemaVersion = 1,
                    AggregateType = "Message",
                    AggregateId = message.Id,
                    AggregateVersion = 1,
                    OccurredAt = now,
                    PayloadJson = "{}",
                    RoutingJson = "[]",
                    CausationId = clientRequestId.ToString("D"),
                    Status = OutboxEventStatus.Pending,
                    CreatedAt = now
                });
                return await new EfMessageIdempotencyCommitCoordinator(context).CommitAsync(message);
            }

            Message NewReply(string body, Guid targetRootId) => new()
            {
                TenantId = graph.TenantId,
                WorkspaceId = graph.WorkspaceId,
                ConversationId = conversationId,
                AuthorUserId = graph.UserId,
                Body = body,
                ClientRequestId = clientRequestId,
                ThreadRootMessageId = targetRootId,
                Version = 1,
                CreatedAt = now.AddMinutes(1)
            };

            var results = await Task.WhenAll(CommitAsync("racing body A"), CommitAsync("racing body B"));
            Assert.Equal(results[0].Message.Id, results[1].Message.Id);
            Assert.Single(results, result => result.WasReconciled);
            Assert.Single(results, result => !result.WasReconciled);

            Assert.Equal(1, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, """
SELECT COUNT(*) FROM messages WHERE "TenantId" = @tenantId AND "ConversationId" = @conversationId
AND "AuthorUserId" = @userId AND "ClientRequestId" = @clientRequestId;
""", ("tenantId", graph.TenantId), ("conversationId", conversationId),
                ("userId", graph.UserId), ("clientRequestId", clientRequestId)));
            Assert.Equal(1, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database,
                "SELECT COUNT(*) FROM audit_logs WHERE \"CorrelationId\" = @key;", ("key", clientRequestId.ToString("D"))));
            Assert.Equal(1, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database,
                "SELECT COUNT(*) FROM notifications WHERE \"Title\" = @title;", ("title", $"thread-race-{clientRequestId:D}")));
            Assert.Equal(1, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database,
                "SELECT COUNT(*) FROM outbox_events WHERE \"CausationId\" = @key;", ("key", clientRequestId.ToString("D"))));

            var mismatchTenant = new CurrentTenantService();
            mismatchTenant.SetTenant(graph.TenantId, "thread-race-tenant");
            await using var mismatchContext = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).Options,
                mismatchTenant);
            var mismatched = NewReply("must not change target", otherRootId);
            mismatchContext.Messages.Add(mismatched);
            var mismatchResult = await new EfMessageIdempotencyCommitCoordinator(mismatchContext).CommitAsync(mismatched);
            Assert.True(mismatchResult.WasReconciled);
            Assert.Equal(rootId, mismatchResult.Message.ThreadRootMessageId);
            Assert.NotEqual(mismatched.ThreadRootMessageId, mismatchResult.Message.ThreadRootMessageId);

            var unrelatedTenant = new CurrentTenantService();
            unrelatedTenant.SetTenant(graph.TenantId, "thread-race-tenant");
            await using var unrelatedContext = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).Options,
                unrelatedTenant);
            var unrelatedRequestId = Guid.NewGuid();
            var unrelatedMessage = new Message
            {
                TenantId = graph.TenantId,
                WorkspaceId = graph.WorkspaceId,
                ConversationId = conversationId,
                AuthorUserId = graph.UserId,
                Body = "unrelated database failure",
                ClientRequestId = unrelatedRequestId,
                ThreadRootMessageId = rootId,
                Version = 1,
                CreatedAt = now.AddMinutes(2)
            };
            unrelatedContext.Messages.Add(unrelatedMessage);
            unrelatedContext.Users.Add(new User
            {
                Id = graph.UserId,
                DisplayName = "duplicate primary key",
                Email = "duplicate-primary-key@example.test",
                NormalizedEmail = "DUPLICATE-PRIMARY-KEY@EXAMPLE.TEST",
                PasswordHash = "hash",
                Status = UserStatus.Active,
                SystemRole = SystemRole.User,
                CreatedAt = now
            });
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                new EfMessageIdempotencyCommitCoordinator(unrelatedContext).CommitAsync(unrelatedMessage));
            Assert.Equal(0, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database,
                "SELECT COUNT(*) FROM messages WHERE \"ClientRequestId\" = @key;", ("key", unrelatedRequestId)));
        });
    }
}
