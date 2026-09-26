using Coglatas.Infrastructure.Persistence.Migrations;

namespace Coglatas.Tests.PostgreSql;

[Collection("PostgreSqlTaskV1")]
[Trait("Scope", "TaskV1Prompt2C")]
public sealed class TaskV1WatchBackfillPostgreSqlTests
{
    private const string PreviousMigration = "20260722230000_MigrateLegacyTaskComments";
    private const string ManualIntentMigration = "20260726130000_AddManualWatchIntent";

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "TaskV1PR03C")]
    public async Task BackfillCombinesAllSourcesHonorsPreferencesAndIsIdempotent()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database, PreviousMigration);
            var graph = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, "watch-main");
            var otherTenant = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, "watch-other-tenant");
            var deleted = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, "watch-deleted", deletedTask: true);
            var primaryAndCollaborator = Guid.NewGuid();
            var reviewer = Guid.NewGuid();
            var manualOn = Guid.NewGuid();
            var manualOff = Guid.NewGuid();
            var manualOptOut = Guid.NewGuid();
            var stale = Guid.NewGuid();
            foreach (var user in new[] { primaryAndCollaborator, reviewer, manualOn, manualOff, manualOptOut, stale })
                await TaskV1MigrationRawSqlSeed.AddUserAsync(database, graph, user, $"watch-{user:N}");

            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
UPDATE task_items SET "PrimaryAssigneeUserId" = @primary, "ReviewerUserId" = @reviewer WHERE "Id" = @taskId;
""", ("primary", primaryAndCollaborator), ("reviewer", reviewer), ("taskId", graph.TaskId));
            await TaskV1MigrationRawSqlSeed.AddCollaboratorAsync(database, graph, primaryAndCollaborator);

            var old = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
            var optOutId = Guid.NewGuid();
            var multiId = Guid.NewGuid();
            var manualOnId = Guid.NewGuid();
            var manualOffId = Guid.NewGuid();
            var manualOptOutId = Guid.NewGuid();
            var staleId = Guid.NewGuid();
            var deletedId = Guid.NewGuid();
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
INSERT INTO work_item_watch_states ("Id", "TenantId", "TaskItemId", "UserId", "AutomaticSources", "IsExplicitOptOut", "IsWatching", "UpdatedAt", "VersionNo") VALUES
(@optOutId, @tenantId, @taskId, @creator, 1, true, true, @old, 7),
(@multiId, @tenantId, @taskId, @multiUser, 1, false, false, @old, 3),
(@manualOnId, @tenantId, @taskId, @manualOn, 0, false, true, @old, 5),
(@manualOffId, @tenantId, @taskId, @manualOff, 0, false, false, @old, 6),
(@manualOptOutId, @tenantId, @taskId, @manualOptOut, 0, true, true, @old, 4),
(@staleId, @tenantId, @taskId, @stale, 2, false, false, @old, 9),
(@deletedId, @deletedTenantId, @deletedTaskId, @deletedCreator, 1, false, true, @old, 11);
""", ("optOutId", optOutId), ("multiId", multiId), ("manualOnId", manualOnId), ("manualOffId", manualOffId), ("manualOptOutId", manualOptOutId), ("staleId", staleId), ("deletedId", deletedId),
                ("tenantId", graph.TenantId), ("taskId", graph.TaskId), ("creator", graph.UserId), ("multiUser", primaryAndCollaborator), ("manualOn", manualOn), ("manualOff", manualOff), ("manualOptOut", manualOptOut), ("stale", stale),
                ("deletedTenantId", deleted.TenantId), ("deletedTaskId", deleted.TaskId), ("deletedCreator", deleted.UserId), ("old", old));

            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            var rows = await ReadRowsAsync(database, graph.TaskId);

            Assert.Equal(7, rows.Count);
            Assert.Equal((1, true, false, 8L), rows.Single(row => row.Id == optOutId).State);
            Assert.Equal((6, false, true, 4L), rows.Single(row => row.Id == multiId).State);
            Assert.Equal((8, false, true, 1L), rows.Single(row => row.UserId == reviewer).State);
            Assert.Equal((0, false, true, 5L), rows.Single(row => row.Id == manualOnId).State);
            Assert.Equal((0, false, false, 6L), rows.Single(row => row.Id == manualOffId).State);
            Assert.Equal((0, true, false, 5L), rows.Single(row => row.Id == manualOptOutId).State);
            Assert.Equal((0, false, false, 10L), rows.Single(row => row.Id == staleId).State);
            Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database, "SELECT \"IsManualWatch\" FROM work_item_watch_states WHERE \"Id\" = @id;", ("id", manualOnId)));
            Assert.False(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database, "SELECT \"IsManualWatch\" FROM work_item_watch_states WHERE \"Id\" = @id;", ("id", manualOffId)));
            Assert.False(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database, "SELECT \"IsManualWatch\" FROM work_item_watch_states WHERE \"Id\" = @id;", ("id", multiId)));
            Assert.False(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database, "SELECT \"IsManualWatch\" FROM work_item_watch_states WHERE \"Id\" = @id;", ("id", manualOptOutId)));
            Assert.All(rows.Where(row => row.Id != manualOnId && row.Id != manualOffId), row => Assert.NotEqual(old, row.UpdatedAt));
            Assert.Equal(old, rows.Single(row => row.Id == manualOnId).UpdatedAt);
            Assert.Equal(old, rows.Single(row => row.Id == manualOffId).UpdatedAt);
            Assert.DoesNotContain(rows, row => row.UserId == otherTenant.UserId);
            Assert.Equal(1, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, "SELECT COUNT(*) FROM work_item_watch_states WHERE \"Id\" = @id;", ("id", deletedId)));
            Assert.Equal(11L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, "SELECT \"VersionNo\" FROM work_item_watch_states WHERE \"Id\" = @id;", ("id", deletedId)));
            Assert.Equal(0, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, "SELECT COUNT(*) FROM work_item_watch_states WHERE \"TaskItemId\" = @deletedTaskId AND \"UserId\" = @creator AND \"Id\" <> @existingId;", ("deletedTaskId", deleted.TaskId), ("creator", deleted.UserId), ("existingId", deletedId)));
            Assert.Equal(1, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, "SELECT COUNT(*) FROM work_item_watch_states WHERE \"TenantId\" = @tenantId AND \"TaskItemId\" = @taskId AND \"UserId\" = @userId;", ("tenantId", otherTenant.TenantId), ("taskId", otherTenant.TaskId), ("userId", otherTenant.UserId)));

            var beforeSecondRun = rows.OrderBy(row => row.Id).ToArray();
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, TaskV1WatchBackfillScript.Sql);
            var afterSecondRun = (await ReadRowsAsync(database, graph.TaskId)).OrderBy(row => row.Id).ToArray();
            Assert.Equal(beforeSecondRun, afterSecondRun);
        });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "TaskV1PR03C")]
    public async Task PostManualNormalizationUsesTheCanonicalFormulaWithoutTouchingDeletedTasks()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database, ManualIntentMigration);
            var active = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, "watch-normalization-active");
            var deleted = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, "watch-normalization-deleted", deletedTask: true);
            var automaticOnly = Guid.NewGuid();
            var manual = Guid.NewGuid();
            var optOut = Guid.NewGuid();
            var deletedState = Guid.NewGuid();
            await TaskV1MigrationRawSqlSeed.AddUserAsync(database, active, automaticOnly, "watch-normalization-auto");
            await TaskV1MigrationRawSqlSeed.AddUserAsync(database, active, manual, "watch-normalization-manual");
            await TaskV1MigrationRawSqlSeed.AddUserAsync(database, active, optOut, "watch-normalization-optout");

            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
INSERT INTO work_item_watch_states ("Id", "TenantId", "TaskItemId", "UserId", "AutomaticSources", "IsManualWatch", "IsExplicitOptOut", "IsWatching", "UpdatedAt", "VersionNo") VALUES
(@automaticOnly, @tenantId, @taskId, @autoUser, 0, false, false, true, CURRENT_TIMESTAMP, 7),
(@manual, @tenantId, @taskId, @manualUser, 0, true, false, false, CURRENT_TIMESTAMP, 4),
(@optOut, @tenantId, @taskId, @optOutUser, 1, false, true, true, CURRENT_TIMESTAMP, 3),
(@deletedState, @deletedTenantId, @deletedTaskId, @deletedUser, 0, false, false, true, CURRENT_TIMESTAMP, 11);
""", ("automaticOnly", automaticOnly), ("manual", manual), ("optOut", optOut), ("deletedState", deletedState),
                ("tenantId", active.TenantId), ("taskId", active.TaskId), ("autoUser", automaticOnly), ("manualUser", manual), ("optOutUser", optOut),
                ("deletedTenantId", deleted.TenantId), ("deletedTaskId", deleted.TaskId), ("deletedUser", deleted.UserId));

            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            Assert.False(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database, "SELECT \"IsWatching\" FROM work_item_watch_states WHERE \"Id\" = @id;", ("id", automaticOnly)));
            Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database, "SELECT \"IsWatching\" FROM work_item_watch_states WHERE \"Id\" = @id;", ("id", manual)));
            Assert.False(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database, "SELECT \"IsWatching\" FROM work_item_watch_states WHERE \"Id\" = @id;", ("id", optOut)));
            Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database, "SELECT \"IsWatching\" FROM work_item_watch_states WHERE \"Id\" = @id;", ("id", deletedState)));

            var versions = await PostgreSqlMigrationTestDatabase.QueryAsync(database, """
SELECT "Id", "VersionNo" FROM work_item_watch_states
WHERE "Id" = @automaticOnly OR "Id" = @manual OR "Id" = @optOut OR "Id" = @deletedState ORDER BY "Id";
""", row => (row.GetGuid(0), row.GetInt64(1)), ("automaticOnly", automaticOnly), ("manual", manual), ("optOut", optOut), ("deletedState", deletedState));
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            var repeatedVersions = await PostgreSqlMigrationTestDatabase.QueryAsync(database, """
SELECT "Id", "VersionNo" FROM work_item_watch_states
WHERE "Id" = @automaticOnly OR "Id" = @manual OR "Id" = @optOut OR "Id" = @deletedState ORDER BY "Id";
""", row => (row.GetGuid(0), row.GetInt64(1)), ("automaticOnly", automaticOnly), ("manual", manual), ("optOut", optOut), ("deletedState", deletedState));
            Assert.Equal(versions, repeatedVersions);
        });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "TaskV1PR03C")]
    public async Task ExclusivityMigrationRepairsContradictionsAndDatabaseRejectsNewContradictions()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database, ManualIntentMigration);
            var active = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, "watch-exclusive-active");
            var deleted = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, "watch-exclusive-deleted", deletedTask: true);
            var contradictory = Guid.NewGuid();
            var manual = Guid.NewGuid();
            var automatic = Guid.NewGuid();
            var optOut = Guid.NewGuid();
            var constraintUser = Guid.NewGuid();
            var deletedContradiction = Guid.NewGuid();
            foreach (var user in new[] { contradictory, manual, automatic, optOut, constraintUser })
                await TaskV1MigrationRawSqlSeed.AddUserAsync(database, active, user, $"watch-exclusive-{user:N}");

            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
INSERT INTO work_item_watch_states ("Id", "TenantId", "TaskItemId", "UserId", "AutomaticSources", "IsManualWatch", "IsExplicitOptOut", "IsWatching", "UpdatedAt", "VersionNo") VALUES
(@contradictory, @tenantId, @taskId, @contradictory, 0, true, true, true, CURRENT_TIMESTAMP, 1),
(@manual, @tenantId, @taskId, @manual, 0, true, false, false, CURRENT_TIMESTAMP, 1),
(@automatic, @tenantId, @taskId, @automatic, 1, false, false, false, CURRENT_TIMESTAMP, 1),
(@optOut, @tenantId, @taskId, @optOut, 1, false, true, true, CURRENT_TIMESTAMP, 1),
(@deleted, @deletedTenantId, @deletedTaskId, @deletedUser, 0, true, true, true, CURRENT_TIMESTAMP, 1);
""", ("contradictory", contradictory), ("manual", manual), ("automatic", automatic), ("optOut", optOut), ("deleted", deletedContradiction),
                ("tenantId", active.TenantId), ("taskId", active.TaskId), ("deletedTenantId", deleted.TenantId), ("deletedTaskId", deleted.TaskId), ("deletedUser", deleted.UserId));

            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            Assert.Equal((false, true, false), await ReadIntentAsync(database, contradictory));
            Assert.Equal((true, false, true), await ReadIntentAsync(database, manual));
            Assert.Equal((false, false, true), await ReadIntentAsync(database, automatic));
            Assert.Equal((false, true, false), await ReadIntentAsync(database, optOut));
            // A deleted Task is excluded from normal reconciliation, but a
            // contradictory row must still be repaired before the global DB
            // invariant can be installed.
            Assert.Equal((false, true, false), await ReadIntentAsync(database, deletedContradiction));

            var exception = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
INSERT INTO work_item_watch_states ("Id", "TenantId", "TaskItemId", "UserId", "AutomaticSources", "IsManualWatch", "IsExplicitOptOut", "IsWatching", "UpdatedAt", "VersionNo")
VALUES (@id, @tenantId, @taskId, @userId, 0, true, true, true, CURRENT_TIMESTAMP, 1);
""", ("id", Guid.NewGuid()), ("tenantId", active.TenantId), ("taskId", active.TaskId), ("userId", constraintUser)));
            Assert.Equal("CK_work_item_watch_states_manual_opt_out_exclusive", exception.ConstraintName);
        });
    }

    private static Task<List<WatchRow>> ReadRowsAsync(string connectionString, Guid taskId) => PostgreSqlMigrationTestDatabase.QueryAsync(connectionString, """
    SELECT "Id", "UserId", "AutomaticSources", "IsExplicitOptOut", "IsWatching", "VersionNo", "UpdatedAt"
    FROM work_item_watch_states WHERE "TaskItemId" = @taskId;
    """, row => new WatchRow(row.GetGuid(0), row.GetGuid(1), (row.GetInt32(2), row.GetBoolean(3), row.GetBoolean(4), row.GetInt64(5)), row.GetFieldValue<DateTimeOffset>(6)), ("taskId", taskId));

    private static async Task<(bool Manual, bool OptOut, bool Watching)> ReadIntentAsync(string connectionString, Guid id)
    {
        var rows = await PostgreSqlMigrationTestDatabase.QueryAsync(connectionString, """
SELECT "IsManualWatch", "IsExplicitOptOut", "IsWatching"
FROM work_item_watch_states WHERE "Id" = @id;
""", row => (row.GetBoolean(0), row.GetBoolean(1), row.GetBoolean(2)), ("id", id));
        return Assert.Single(rows);
    }

    private sealed record WatchRow(Guid Id, Guid UserId, (int AutomaticSources, bool OptOut, bool Watching, long Version) State, DateTimeOffset UpdatedAt);
}
