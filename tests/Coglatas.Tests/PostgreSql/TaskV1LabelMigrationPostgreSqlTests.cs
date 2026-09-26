using Npgsql;

namespace Coglatas.Tests.PostgreSql;

[Collection("PostgreSqlTaskV1")]
[Trait("Scope", "TaskV1Prompt2C")]
public sealed class TaskV1LabelMigrationPostgreSqlTests
{
    private const string PreviousMigration = "20260725070000_EnforceUniqueActiveTaskFileAssociations";

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "TaskV1PR03C")]
    public async Task NormalizedNameMigrationDeterministicallyConsolidatesDefinitionsAndAssociations()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database, PreviousMigration);
            var graph = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, "labels-main");
            var otherTenantGraph = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, "labels-other-tenant");
            var taskB = Guid.NewGuid();
            var otherProject = Guid.NewGuid();
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
INSERT INTO task_items ("Id", "TenantId", "WorkspaceId", "ProjectId", "Title", "Status", "Priority", "ProgressPercent", "SortOrder", "CreatedByUserId", "CreatedAt", "Kind", "IsBlocked", "SortKey", "VersionNo")
VALUES (@taskB, @tenantId, @workspaceId, @projectId, 'Task B', 'Todo', 'Medium', 0, 1, @userId, @now, 'Task', false, 2048, 1);
INSERT INTO projects ("Id", "TenantId", "WorkspaceId", "Name", "Slug", "Status", "OwnerUserId", "CreatedByUserId", "CreatedAt")
VALUES (@otherProject, @tenantId, @workspaceId, 'Other project', @otherProjectSlug, 'Active', @userId, @userId, @now);
""", ("taskB", taskB), ("tenantId", graph.TenantId), ("workspaceId", graph.WorkspaceId), ("projectId", graph.ProjectId), ("userId", graph.UserId), ("now", new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero)), ("otherProject", otherProject), ("otherProjectSlug", "other-project-labels-main"));

            var release = Guid.NewGuid();
            var archivedRelease = Guid.NewGuid();
            var upperRelease = Guid.NewGuid();
            var other = Guid.NewGuid();
            var otherProjectRelease = Guid.NewGuid();
            var otherTenantRelease = Guid.NewGuid();
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
INSERT INTO project_task_labels ("Id", "TenantId", "WorkspaceId", "ProjectId", "Name", "SortKey", "IsArchived", "VersionNo") VALUES
(@release, @tenantId, @workspaceId, @projectId, 'Release', 2048, false, 1),
(@archivedRelease, @tenantId, @workspaceId, @projectId, 'release', 1024, true, 1),
(@upperRelease, @tenantId, @workspaceId, @projectId, 'RELEASE', 4096, false, 1),
(@other, @tenantId, @workspaceId, @projectId, 'Other', 1024, false, 1),
(@otherProjectRelease, @tenantId, @workspaceId, @otherProject, 'release', 1024, false, 1),
(@otherTenantRelease, @otherTenantId, @otherTenantWorkspaceId, @otherTenantProjectId, 'release', 1024, false, 1);
INSERT INTO work_item_labels ("Id", "TenantId", "TaskItemId", "LabelId", "AddedAt", "AddedByUserId") VALUES
(@linkARelease, @tenantId, @taskA, @release, @now, @userId),
(@linkAArchived, @tenantId, @taskA, @archivedRelease, @now, @userId),
(@linkBUpper, @tenantId, @taskB, @upperRelease, @now, @userId);
""", ("release", release), ("archivedRelease", archivedRelease), ("upperRelease", upperRelease), ("other", other), ("otherProjectRelease", otherProjectRelease), ("otherTenantRelease", otherTenantRelease),
                ("tenantId", graph.TenantId), ("workspaceId", graph.WorkspaceId), ("projectId", graph.ProjectId), ("otherProject", otherProject), ("taskA", graph.TaskId), ("taskB", taskB), ("userId", graph.UserId), ("now", new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero)),
                ("otherTenantId", otherTenantGraph.TenantId), ("otherTenantWorkspaceId", otherTenantGraph.WorkspaceId), ("otherTenantProjectId", otherTenantGraph.ProjectId),
                ("linkARelease", Guid.NewGuid()), ("linkAArchived", Guid.NewGuid()), ("linkBUpper", Guid.NewGuid()));

            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);

            Assert.Equal(1, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, "SELECT COUNT(*) FROM project_task_labels WHERE \"TenantId\" = @tenantId AND \"ProjectId\" = @projectId AND \"NormalizedName\" = 'release';", ("tenantId", graph.TenantId), ("projectId", graph.ProjectId)));
            Assert.Equal(release, await PostgreSqlMigrationTestDatabase.ScalarAsync<Guid>(database, "SELECT \"Id\" FROM project_task_labels WHERE \"TenantId\" = @tenantId AND \"ProjectId\" = @projectId AND \"NormalizedName\" = 'release';", ("tenantId", graph.TenantId), ("projectId", graph.ProjectId)));
            Assert.Equal(1, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, "SELECT COUNT(*) FROM work_item_labels WHERE \"TaskItemId\" = @taskId AND \"LabelId\" = @release;", ("taskId", graph.TaskId), ("release", release)));
            Assert.Equal(1, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, "SELECT COUNT(*) FROM work_item_labels WHERE \"TaskItemId\" = @taskId;", ("taskId", graph.TaskId)));
            Assert.Equal(release, await PostgreSqlMigrationTestDatabase.ScalarAsync<Guid>(database, "SELECT \"LabelId\" FROM work_item_labels WHERE \"TaskItemId\" = @taskId;", ("taskId", taskB)));
            Assert.Equal(3, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, "SELECT COUNT(*) FROM project_task_labels WHERE \"Id\" IN (@other, @otherProjectRelease, @otherTenantRelease);", ("other", other), ("otherProjectRelease", otherProjectRelease), ("otherTenantRelease", otherTenantRelease)));
            Assert.Equal(0, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, "SELECT COUNT(*) FROM project_task_labels WHERE \"Id\" IN (@archived, @upper);", ("archived", archivedRelease), ("upper", upperRelease)));
            Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database, "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE tablename = 'project_task_labels' AND indexname = 'IX_project_task_labels_TenantId_ProjectId_NormalizedName');"));

            var exception = await Assert.ThrowsAsync<PostgresException>(() => PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
INSERT INTO project_task_labels ("Id", "TenantId", "WorkspaceId", "ProjectId", "Name", "SortKey", "IsArchived", "VersionNo")
VALUES (@id, @tenantId, @workspaceId, @projectId, ' release ', 8192, false, 1);
""", ("id", Guid.NewGuid()), ("tenantId", graph.TenantId), ("workspaceId", graph.WorkspaceId), ("projectId", graph.ProjectId)));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        });
    }
}
