using Npgsql;

namespace Coglatas.Tests.PostgreSql;

[Collection("PostgreSqlTaskV1")]
[Trait("Scope", "Issue364")]
public sealed class ResearchPlanPostgreSqlTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task CurrentRevisionMustBelongToTheSamePlan()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            var first = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, $"research-plan-a-{Guid.NewGuid():N}");
            var second = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, $"research-plan-b-{Guid.NewGuid():N}");
            var third = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, $"research-plan-c-{Guid.NewGuid():N}");
            var firstPlanId = Guid.NewGuid();
            var secondPlanId = Guid.NewGuid();
            var firstRevisionId = Guid.NewGuid();
            var secondRevisionId = Guid.NewGuid();
            var deferredPlanId = Guid.NewGuid();
            var deferredRevisionId = Guid.NewGuid();
            var now = new DateTimeOffset(2026, 8, 30, 0, 0, 0, TimeSpan.Zero);

            await InsertPlanAsync(database, first, firstPlanId);
            await InsertPlanAsync(database, second, secondPlanId);
            await InsertRevisionAsync(database, first, firstPlanId, firstRevisionId, now);
            await InsertRevisionAsync(database, second, secondPlanId, secondRevisionId, now);

            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
UPDATE research_plans
SET "CurrentRevisionId" = @firstRevisionId
WHERE "Id" = @firstPlanId;
UPDATE research_plans
SET "CurrentRevisionId" = @secondRevisionId
WHERE "Id" = @secondPlanId;
""",
                ("firstPlanId", firstPlanId), ("firstRevisionId", firstRevisionId),
                ("secondPlanId", secondPlanId), ("secondRevisionId", secondRevisionId));

            var definition = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database, """
SELECT pg_get_constraintdef(oid)
FROM pg_constraint
WHERE conname = 'FK_research_plans_research_plan_revisions_CurrentRevisionId_Id';
""");
            Assert.Contains(
                "FOREIGN KEY (\"CurrentRevisionId\", \"Id\") REFERENCES research_plan_revisions(\"Id\", \"ResearchPlanId\")",
                definition,
                StringComparison.Ordinal);
            Assert.Equal(
                "true:true",
                await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database, """
SELECT condeferrable::text || ':' || condeferred::text
FROM pg_constraint
WHERE conname = 'FK_research_plans_research_plan_revisions_CurrentRevisionId_Id';
"""));

            await InsertDeferredFirstRevisionAsync(
                database,
                third,
                deferredPlanId,
                deferredRevisionId,
                now);

            var invalidPointer = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
UPDATE research_plans
SET "CurrentRevisionId" = @secondRevisionId
WHERE "Id" = @firstPlanId;
""", ("firstPlanId", firstPlanId), ("secondRevisionId", secondRevisionId)));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, invalidPointer.SqlState);
        });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task ExecutionRunPlanSnapshotRequiresACompleteSameScopeRevisionReference()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            var first = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, $"research-run-a-{Guid.NewGuid():N}");
            var second = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, $"research-run-b-{Guid.NewGuid():N}");
            var firstPlanId = Guid.NewGuid();
            var secondPlanId = Guid.NewGuid();
            var firstRevisionId = Guid.NewGuid();
            var secondRevisionId = Guid.NewGuid();
            var now = new DateTimeOffset(2026, 8, 30, 0, 0, 0, TimeSpan.Zero);

            await InsertPlanAsync(database, first, firstPlanId);
            await InsertPlanAsync(database, second, secondPlanId);
            await InsertRevisionAsync(database, first, firstPlanId, firstRevisionId, now);
            await InsertRevisionAsync(database, second, secondPlanId, secondRevisionId, now);

            var validRunId = Guid.NewGuid();
            await InsertRunWithPlanSnapshotAsync(database, validRunId, first, now, firstRevisionId, 1);
            Assert.Equal(
                firstRevisionId,
                await PostgreSqlMigrationTestDatabase.ScalarAsync<Guid>(
                    database,
                    "SELECT \"SnapshotResearchPlanRevisionId\" FROM task_execution_runs WHERE \"Id\" = @id;",
                    ("id", validRunId)));
            Assert.Equal(
                1L,
                await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(
                    database,
                    "SELECT \"SnapshotResearchPlanRevisionNo\" FROM task_execution_runs WHERE \"Id\" = @id;",
                    ("id", validRunId)));

            var incompleteSnapshot = await Assert.ThrowsAsync<PostgresException>(() =>
                InsertRunWithPlanSnapshotAsync(database, Guid.NewGuid(), first, now, null, 1));
            Assert.Equal(PostgresErrorCodes.CheckViolation, incompleteSnapshot.SqlState);

            var crossScopeSnapshot = await Assert.ThrowsAsync<PostgresException>(() =>
                InsertRunWithPlanSnapshotAsync(database, Guid.NewGuid(), first, now, secondRevisionId, 1));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, crossScopeSnapshot.SqlState);

            var mismatchedRevisionNumber = await Assert.ThrowsAsync<PostgresException>(() =>
                InsertRunWithPlanSnapshotAsync(database, Guid.NewGuid(), first, now, firstRevisionId, 2));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, mismatchedRevisionNumber.SqlState);
        });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task AcceptedExecutionRunPlanSnapshotCannotBeSwappedAfterAcceptance()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            var graph = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, $"research-run-immutable-{Guid.NewGuid():N}");
            var planId = Guid.NewGuid();
            var firstRevisionId = Guid.NewGuid();
            var secondRevisionId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            var now = new DateTimeOffset(2026, 8, 30, 0, 0, 0, TimeSpan.Zero);

            await InsertPlanAsync(database, graph, planId);
            await InsertRevisionAsync(database, graph, planId, firstRevisionId, now, revisionNo: 1);
            await InsertRevisionAsync(database, graph, planId, secondRevisionId, now, revisionNo: 2);
            await InsertRunWithPlanSnapshotAsync(database, runId, graph, now, firstRevisionId, 1);

            var idOnlySwap = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
UPDATE task_execution_runs
SET "SnapshotResearchPlanRevisionId" = @secondRevisionId
WHERE "Id" = @runId;
""", ("secondRevisionId", secondRevisionId), ("runId", runId)));
            Assert.Equal("P0001", idOnlySwap.SqlState);
            Assert.Contains("Task execution run snapshot is immutable", idOnlySwap.MessageText, StringComparison.Ordinal);

            var revisionNumberOnlySwap = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
UPDATE task_execution_runs
SET "SnapshotResearchPlanRevisionNo" = 2
WHERE "Id" = @runId;
""", ("runId", runId)));
            Assert.Equal("P0001", revisionNumberOnlySwap.SqlState);
            Assert.Contains("Task execution run snapshot is immutable", revisionNumberOnlySwap.MessageText, StringComparison.Ordinal);

            // This is a complete, valid same-Task revision identity. Only the
            // accepted-run immutability trigger—not the composite FK—can stop it.
            var validSameScopeSwap = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
UPDATE task_execution_runs
SET
    "SnapshotResearchPlanRevisionId" = @secondRevisionId,
    "SnapshotResearchPlanRevisionNo" = 2
WHERE "Id" = @runId;
""", ("secondRevisionId", secondRevisionId), ("runId", runId)));
            Assert.Equal("P0001", validSameScopeSwap.SqlState);
            Assert.Contains("Task execution run snapshot is immutable", validSameScopeSwap.MessageText, StringComparison.Ordinal);

            Assert.Equal(
                firstRevisionId,
                await PostgreSqlMigrationTestDatabase.ScalarAsync<Guid>(
                    database,
                    "SELECT \"SnapshotResearchPlanRevisionId\" FROM task_execution_runs WHERE \"Id\" = @id;",
                    ("id", runId)));
            Assert.Equal(
                1L,
                await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(
                    database,
                    "SELECT \"SnapshotResearchPlanRevisionNo\" FROM task_execution_runs WHERE \"Id\" = @id;",
                    ("id", runId)));
        });
    }

    private static Task InsertDeferredFirstRevisionAsync(
        string database,
        TaskV1MigrationRawSqlSeed.Graph graph,
        Guid planId,
        Guid revisionId,
        DateTimeOffset now) =>
        PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
BEGIN;
INSERT INTO research_plans
    ("Id", "TenantId", "WorkspaceId", "ProjectId", "TaskItemId", "CurrentRevisionId", "VersionNo")
VALUES
    (@planId, @tenantId, @workspaceId, @projectId, @taskId, @revisionId, 1);
INSERT INTO research_plan_revisions
    ("Id", "TenantId", "WorkspaceId", "ProjectId", "TaskItemId", "ResearchPlanId", "RevisionNo", "CreatedByUserId", "CreatedAtUtc")
VALUES
    (@revisionId, @tenantId, @workspaceId, @projectId, @taskId, @planId, 1, @userId, @now);
COMMIT;
""",
            ("planId", planId), ("revisionId", revisionId), ("tenantId", graph.TenantId),
            ("workspaceId", graph.WorkspaceId), ("projectId", graph.ProjectId),
            ("taskId", graph.TaskId), ("userId", graph.UserId), ("now", now));

    private static Task InsertPlanAsync(
        string database,
        TaskV1MigrationRawSqlSeed.Graph graph,
        Guid planId) =>
        PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
INSERT INTO research_plans
    ("Id", "TenantId", "WorkspaceId", "ProjectId", "TaskItemId", "CurrentRevisionId", "VersionNo")
VALUES
    (@planId, @tenantId, @workspaceId, @projectId, @taskId, NULL, 1);
""",
            ("planId", planId), ("tenantId", graph.TenantId), ("workspaceId", graph.WorkspaceId),
            ("projectId", graph.ProjectId), ("taskId", graph.TaskId));

    private static Task InsertRevisionAsync(
        string database,
        TaskV1MigrationRawSqlSeed.Graph graph,
        Guid planId,
        Guid revisionId,
        DateTimeOffset now,
        long revisionNo = 1) =>
        PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
INSERT INTO research_plan_revisions
    ("Id", "TenantId", "WorkspaceId", "ProjectId", "TaskItemId", "ResearchPlanId", "RevisionNo", "CreatedByUserId", "CreatedAtUtc")
VALUES
    (@revisionId, @tenantId, @workspaceId, @projectId, @taskId, @planId, @revisionNo, @userId, @now);
""",
            ("revisionId", revisionId), ("tenantId", graph.TenantId), ("workspaceId", graph.WorkspaceId),
            ("projectId", graph.ProjectId), ("taskId", graph.TaskId), ("planId", planId),
            ("revisionNo", revisionNo), ("userId", graph.UserId), ("now", now));

    private static Task InsertRunWithPlanSnapshotAsync(
        string database,
        Guid runId,
        TaskV1MigrationRawSqlSeed.Graph graph,
        DateTimeOffset requestedAtUtc,
        Guid? revisionId,
        long? revisionNo) =>
        PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
INSERT INTO task_execution_runs (
    "Id", "TenantId", "WorkspaceId", "ProjectId", "TaskItemId",
    "RequestedByUserId", "RequestedAtUtc", "Status", "VersionNo",
    "SnapshotSchemaVersion", "SnapshotScopeOrigin",
    "SnapshotProjectScopeVersion", "SnapshotTaskOverrideVersion",
    "SnapshotWebEnabled", "SnapshotProjectFilesEnabled",
    "SnapshotResearchPlanRevisionId", "SnapshotResearchPlanRevisionNo")
VALUES (
    @id, @tenantId, @workspaceId, @projectId, @taskId,
    @userId, @requestedAt, 'Accepted', 1,
    2, 'ProjectDefault',
    1, NULL, FALSE, TRUE,
    @revisionId, @revisionNo);
""",
            ("id", runId),
            ("tenantId", graph.TenantId),
            ("workspaceId", graph.WorkspaceId),
            ("projectId", graph.ProjectId),
            ("taskId", graph.TaskId),
            ("userId", graph.UserId),
            ("requestedAt", requestedAtUtc),
            ("revisionId", revisionId),
            ("revisionNo", revisionNo));
}
