namespace Coglatas.Tests.PostgreSql;

[Collection("PostgreSqlTaskV1")]
public sealed class TaskV1LegacyCommentMigrationPostgreSqlTests
{
    private const string PreviousMigration = "20260719071017_MyTasksProjectionIndexes";

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task LegacyTaskCommentsAreCopiedFromTheActualPreMigrationSchemaWithoutMovingOtherCommentTypes()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database, PreviousMigration);
            var graph = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, "legacy-comment");
            var activeId = Guid.NewGuid();
            var deletedId = Guid.NewGuid();
            var projectCommentId = Guid.NewGuid();
            var milestoneCommentId = Guid.NewGuid();
            var createdAt = new DateTimeOffset(2026, 7, 20, 1, 2, 3, TimeSpan.Zero);
            var updatedAt = createdAt.AddHours(1);
            var deletedAt = updatedAt.AddHours(1);

            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
INSERT INTO comments ("Id", "TenantId", "WorkspaceId", "AuthorUserId", "TargetType", "TargetId", "Body", "CreatedAt", "UpdatedAt", "DeletedAt", "DeletedByUserId", "DeleteReason")
VALUES
(@activeId, @tenantId, @workspaceId, @userId, 'TaskItem', @taskId, 'active legacy task comment', @createdAt, @updatedAt, NULL, NULL, NULL),
(@deletedId, @tenantId, @workspaceId, @userId, 'TaskItem', @taskId, 'deleted legacy task comment', @createdAt, @updatedAt, @deletedAt, @userId, 'legacy deletion'),
(@projectCommentId, @tenantId, @workspaceId, @userId, 'Project', @projectId, 'project remains generic', @createdAt, @updatedAt, NULL, NULL, NULL),
(@milestoneCommentId, @tenantId, @workspaceId, @userId, 'Milestone', @taskId, 'milestone remains generic', @createdAt, @updatedAt, NULL, NULL, NULL);
""", ("activeId", activeId), ("deletedId", deletedId), ("projectCommentId", projectCommentId), ("milestoneCommentId", milestoneCommentId),
                ("tenantId", graph.TenantId), ("workspaceId", graph.WorkspaceId), ("userId", graph.UserId), ("taskId", graph.TaskId), ("projectId", graph.ProjectId),
                ("createdAt", createdAt), ("updatedAt", updatedAt), ("deletedAt", deletedAt));

            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);

            var rows = await PostgreSqlMigrationTestDatabase.QueryAsync(database, """
SELECT "Id", "TenantId", "WorkspaceId", "ProjectId", "TaskItemId", "AuthorUserId", "BodyPlainText", "IsImportant", "VersionNo", "CreatedAt", "UpdatedAt", "DeletedAt", "DeletedByUserId", "DeleteReason"
FROM task_comments WHERE "Id" IN (@activeId, @deletedId) ORDER BY "Id";
""", row => new { Id = row.GetGuid(0), TenantId = row.GetGuid(1), WorkspaceId = row.GetGuid(2), ProjectId = row.GetGuid(3), TaskId = row.GetGuid(4), UserId = row.GetGuid(5), Body = row.GetString(6), Important = row.GetBoolean(7), Version = row.GetInt64(8), Created = row.GetFieldValue<DateTimeOffset>(9), Updated = row.GetFieldValue<DateTimeOffset>(10), Deleted = row.IsDBNull(11) ? (DateTimeOffset?)null : row.GetFieldValue<DateTimeOffset>(11), DeletedBy = row.IsDBNull(12) ? (Guid?)null : row.GetGuid(12), Reason = row.IsDBNull(13) ? null : row.GetString(13) }, ("activeId", activeId), ("deletedId", deletedId));

            Assert.Equal(2, rows.Count);
            Assert.All(rows, row => { Assert.Equal(graph.TenantId, row.TenantId); Assert.Equal(graph.WorkspaceId, row.WorkspaceId); Assert.Equal(graph.ProjectId, row.ProjectId); Assert.Equal(graph.TaskId, row.TaskId); Assert.Equal(graph.UserId, row.UserId); Assert.False(row.Important); Assert.Equal(1, row.Version); Assert.Equal(createdAt, row.Created); Assert.Equal(updatedAt, row.Updated); });
            Assert.Equal("active legacy task comment", rows.Single(row => row.Id == activeId).Body);
            var deleted = rows.Single(row => row.Id == deletedId);
            Assert.Equal(deletedAt, deleted.Deleted); Assert.Equal(graph.UserId, deleted.DeletedBy); Assert.Equal("legacy deletion", deleted.Reason);
            Assert.Equal(0, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, "SELECT COUNT(*) FROM task_comments WHERE \"Id\" IN (@projectId, @milestoneId);", ("projectId", projectCommentId), ("milestoneId", milestoneCommentId)));
            Assert.Equal(4, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, "SELECT COUNT(*) FROM comments;"));
        });
    }
}
