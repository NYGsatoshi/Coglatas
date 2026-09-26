using Npgsql;

namespace Coglatas.Tests.PostgreSql;

[Collection("PostgreSqlTaskV1")]
[Trait("Scope", "Issue461")]
public sealed class FirstPartyProjectFilesRuntimePostgreSqlTests
{
    private const string PreviousMigration = "20260829173340_AddMessageFollowUps";

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task RuntimeContractMigrationMapsLegacyRunsAndEnforcesTheCanonicalLifecycle()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database, PreviousMigration);
            var graph = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, "runtime-v1");
            var requestedAt = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);
            var legacyRunId = Guid.NewGuid();

            await InsertLegacyRunAsync(
                database,
                legacyRunId,
                graph,
                requestedAt,
                status: "RuntimeUnavailable",
                finishedAtUtc: requestedAt.AddSeconds(1),
                failureCode: "TASK_EXECUTION_RUNTIME_UNAVAILABLE");

            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);

            Assert.Equal(
                "Failed",
                await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(
                    database,
                    "SELECT \"Status\" FROM task_execution_runs WHERE \"Id\" = @id;",
                    ("id", legacyRunId)));
            Assert.Equal(
                "FirstPartyProjectFilesRuntimeV1",
                await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(
                    database,
                    "SELECT \"RuntimeProvider\" FROM task_execution_runs WHERE \"Id\" = @id;",
                    ("id", legacyRunId)));
            Assert.Equal(
                1,
                await PostgreSqlMigrationTestDatabase.ScalarAsync<int>(
                    database,
                    "SELECT \"RuntimeContractVersion\" FROM task_execution_runs WHERE \"Id\" = @id;",
                    ("id", legacyRunId)));
            Assert.Equal(
                "TASK_EXECUTION_LEGACY_PROVIDER_UNAVAILABLE",
                await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(
                    database,
                    "SELECT \"FailureCode\" FROM task_execution_runs WHERE \"Id\" = @id;",
                    ("id", legacyRunId)));

            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgreSqlMigrationTestDatabase.ExecuteAsync(
                    database,
                    "UPDATE task_execution_runs SET \"Status\" = 'Succeeded' WHERE \"Id\" = @id;",
                    ("id", legacyRunId)));
            await Assert.ThrowsAsync<PostgresException>(() =>
                PostgreSqlMigrationTestDatabase.ExecuteAsync(
                    database,
                    "UPDATE task_execution_runs SET \"RuntimeProvider\" = 'OtherProvider' WHERE \"Id\" = @id;",
                    ("id", legacyRunId)));

            var invalidRuntimeProvider = await Assert.ThrowsAsync<PostgresException>(() =>
                InsertAcceptedRunWithRuntimeContractAsync(
                    database,
                    Guid.NewGuid(),
                    graph,
                    requestedAt.AddMinutes(1),
                    runtimeProvider: "OtherProvider",
                    runtimeContractVersion: 1));
            Assert.Equal(PostgresErrorCodes.CheckViolation, invalidRuntimeProvider.SqlState);

            var invalidRuntimeVersion = await Assert.ThrowsAsync<PostgresException>(() =>
                InsertAcceptedRunWithRuntimeContractAsync(
                    database,
                    Guid.NewGuid(),
                    graph,
                    requestedAt.AddMinutes(1),
                    runtimeProvider: "FirstPartyProjectFilesRuntimeV1",
                    runtimeContractVersion: 2));
            Assert.Equal(PostgresErrorCodes.CheckViolation, invalidRuntimeVersion.SqlState);

            var acceptedRunId = Guid.NewGuid();
            await InsertAcceptedRunAsync(database, acceptedRunId, graph, requestedAt.AddMinutes(1));
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(
                database,
                "UPDATE task_execution_runs SET \"Status\" = 'Queued', \"QueuedAtUtc\" = @queuedAt WHERE \"Id\" = @id;",
                ("id", acceptedRunId), ("queuedAt", requestedAt.AddMinutes(1)));
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(
                database,
                "UPDATE task_execution_runs SET \"Status\" = 'Running', \"StartedAtUtc\" = @startedAt WHERE \"Id\" = @id;",
                ("id", acceptedRunId), ("startedAt", requestedAt.AddMinutes(2)));
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(
                database,
                "UPDATE task_execution_runs SET \"Status\" = 'Succeeded', \"FinishedAtUtc\" = @finishedAt WHERE \"Id\" = @id;",
                ("id", acceptedRunId), ("finishedAt", requestedAt.AddMinutes(3)));

            Assert.Equal(
                "Succeeded",
                await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(
                    database,
                    "SELECT \"Status\" FROM task_execution_runs WHERE \"Id\" = @id;",
                    ("id", acceptedRunId)));
        });
    }

    private static Task InsertLegacyRunAsync(
        string database,
        Guid runId,
        TaskV1MigrationRawSqlSeed.Graph graph,
        DateTimeOffset requestedAtUtc,
        string status,
        DateTimeOffset? finishedAtUtc,
        string? failureCode) =>
        PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
            INSERT INTO task_execution_runs (
                "Id", "TenantId", "WorkspaceId", "ProjectId", "TaskItemId",
                "RequestedByUserId", "RequestedAtUtc", "FinishedAtUtc", "Status",
                "FailureCode", "VersionNo", "SnapshotSchemaVersion",
                "SnapshotScopeOrigin", "SnapshotProjectScopeVersion",
                "SnapshotTaskOverrideVersion", "SnapshotWebEnabled",
                "SnapshotProjectFilesEnabled")
            VALUES (
                @id, @tenantId, @workspaceId, @projectId, @taskId,
                @userId, @requestedAt, @finishedAt, @status,
                @failureCode, 1, 1,
                'ProjectDefault', 1,
                NULL, FALSE, TRUE);
            """,
            ("id", runId),
            ("tenantId", graph.TenantId),
            ("workspaceId", graph.WorkspaceId),
            ("projectId", graph.ProjectId),
            ("taskId", graph.TaskId),
            ("userId", graph.UserId),
            ("requestedAt", requestedAtUtc),
            ("finishedAt", finishedAtUtc),
            ("status", status),
            ("failureCode", failureCode));

    private static Task InsertAcceptedRunAsync(
        string database,
        Guid runId,
        TaskV1MigrationRawSqlSeed.Graph graph,
        DateTimeOffset requestedAtUtc) =>
        PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
            INSERT INTO task_execution_runs (
                "Id", "TenantId", "WorkspaceId", "ProjectId", "TaskItemId",
                "RequestedByUserId", "RequestedAtUtc", "Status", "VersionNo",
                "SnapshotSchemaVersion", "SnapshotScopeOrigin",
                "SnapshotProjectScopeVersion", "SnapshotTaskOverrideVersion",
                "SnapshotWebEnabled", "SnapshotProjectFilesEnabled")
            VALUES (
                @id, @tenantId, @workspaceId, @projectId, @taskId,
                @userId, @requestedAt, 'Accepted', 1,
                1, 'ProjectDefault',
                1, NULL, FALSE, TRUE);
            """,
            ("id", runId),
            ("tenantId", graph.TenantId),
            ("workspaceId", graph.WorkspaceId),
            ("projectId", graph.ProjectId),
            ("taskId", graph.TaskId),
            ("userId", graph.UserId),
            ("requestedAt", requestedAtUtc));

    private static Task InsertAcceptedRunWithRuntimeContractAsync(
        string database,
        Guid runId,
        TaskV1MigrationRawSqlSeed.Graph graph,
        DateTimeOffset requestedAtUtc,
        string runtimeProvider,
        int runtimeContractVersion) =>
        PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
            INSERT INTO task_execution_runs (
                "Id", "TenantId", "WorkspaceId", "ProjectId", "TaskItemId",
                "RequestedByUserId", "RequestedAtUtc", "RuntimeProvider",
                "RuntimeContractVersion", "Status", "VersionNo",
                "SnapshotSchemaVersion", "SnapshotScopeOrigin",
                "SnapshotProjectScopeVersion", "SnapshotTaskOverrideVersion",
                "SnapshotWebEnabled", "SnapshotProjectFilesEnabled")
            VALUES (
                @id, @tenantId, @workspaceId, @projectId, @taskId,
                @userId, @requestedAt, @runtimeProvider,
                @runtimeContractVersion, 'Accepted', 1,
                1, 'ProjectDefault',
                1, NULL, FALSE, TRUE);
            """,
            ("id", runId),
            ("tenantId", graph.TenantId),
            ("workspaceId", graph.WorkspaceId),
            ("projectId", graph.ProjectId),
            ("taskId", graph.TaskId),
            ("userId", graph.UserId),
            ("requestedAt", requestedAtUtc),
            ("runtimeProvider", runtimeProvider),
            ("runtimeContractVersion", runtimeContractVersion));
}
