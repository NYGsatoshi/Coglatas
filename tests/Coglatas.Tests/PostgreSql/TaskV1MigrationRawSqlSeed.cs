namespace Coglatas.Tests.PostgreSql;

/// <summary>Raw-SQL seed helpers for schemas that intentionally predate the current EF model.</summary>
internal static class TaskV1MigrationRawSqlSeed
{
    public sealed record Graph(Guid TenantId, Guid UserId, Guid WorkspaceId, Guid ProjectId, Guid TaskId);

    public static async Task<Graph> CreateGraphAsync(string connectionString, string suffix, bool deletedTask = false)
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero);

        var hasActivationState = await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(
            connectionString,
            """
SELECT COUNT(*)
FROM information_schema.columns
WHERE table_schema = current_schema()
  AND table_name = 'projects'
  AND column_name = 'ActivationState';
""") > 0;

        await PostgreSqlMigrationTestDatabase.ExecuteAsync(connectionString, """
INSERT INTO tenants ("Id", "Name", "Slug", "DisplayName", "Status", "CreatedAt")
VALUES (@tenantId, @name, @slug, @name, 'Active', @now);
INSERT INTO users ("Id", "DisplayName", "Email", "NormalizedEmail", "PasswordHash", "Status", "SystemRole", "FailedLoginAttempts", "CreatedAt")
VALUES (@userId, 'Migration user', @email, @normalizedEmail, 'hash', 'Active', 'User', 0, @now);
INSERT INTO workspaces ("Id", "TenantId", "Name", "Slug", "Status", "CreatedByUserId", "CreatedAt")
VALUES (@workspaceId, @tenantId, 'Migration workspace', @workspaceSlug, 'Active', @userId, @now);
""",
            ("tenantId", tenantId), ("userId", userId), ("workspaceId", workspaceId),
            ("name", $"Migration tenant {suffix}"), ("slug", $"migration-tenant-{suffix}"),
            ("email", $"migration-{suffix}@example.test"), ("normalizedEmail", $"MIGRATION-{suffix}@EXAMPLE.TEST"),
            ("workspaceSlug", $"migration-workspace-{suffix}"), ("now", now));

        var projectInsertSql = hasActivationState
            ? """
INSERT INTO projects ("Id", "TenantId", "WorkspaceId", "Name", "Slug", "Status", "OwnerUserId", "CreatedByUserId", "CreatedAt", "ActivationState")
VALUES (@projectId, @tenantId, @workspaceId, 'Migration project', @projectSlug, 'Active', @userId, @userId, @now, 'LegacyUnknown');
"""
            : """
INSERT INTO projects ("Id", "TenantId", "WorkspaceId", "Name", "Slug", "Status", "OwnerUserId", "CreatedByUserId", "CreatedAt")
VALUES (@projectId, @tenantId, @workspaceId, 'Migration project', @projectSlug, 'Active', @userId, @userId, @now);
""";
        await PostgreSqlMigrationTestDatabase.ExecuteAsync(
            connectionString,
            projectInsertSql,
            ("projectId", projectId), ("tenantId", tenantId), ("workspaceId", workspaceId),
            ("projectSlug", $"migration-project-{suffix}"), ("userId", userId), ("now", now));

        await PostgreSqlMigrationTestDatabase.ExecuteAsync(connectionString, """
INSERT INTO task_items ("Id", "TenantId", "WorkspaceId", "ProjectId", "Title", "Status", "Priority", "ProgressPercent", "SortOrder", "CreatedByUserId", "CreatedAt", "Kind", "IsBlocked", "SortKey", "VersionNo", "DeletedAt")
VALUES (@taskId, @tenantId, @workspaceId, @projectId, 'Migration task', 'Todo', 'Medium', 0, 0, @userId, @now, 'Task', false, 1024, 1, @deletedAt);
""",
            ("taskId", taskId), ("tenantId", tenantId), ("workspaceId", workspaceId), ("projectId", projectId),
            ("userId", userId), ("now", now), ("deletedAt", deletedTask ? now : null));

        return new Graph(tenantId, userId, workspaceId, projectId, taskId);
    }

    public static Task AddUserAsync(string connectionString, Graph graph, Guid userId, string suffix)
        => PostgreSqlMigrationTestDatabase.ExecuteAsync(connectionString, """
INSERT INTO users ("Id", "DisplayName", "Email", "NormalizedEmail", "PasswordHash", "Status", "SystemRole", "FailedLoginAttempts", "CreatedAt")
VALUES (@id, @displayName, @email, @normalizedEmail, 'hash', 'Active', 'User', 0, @now);
INSERT INTO tenant_users ("Id", "TenantId", "UserId", "Role", "Status", "JoinedAt", "CreatedAt")
VALUES (@tenantUserId, @tenantId, @id, 'Member', 'Active', @now, @now);
""", ("id", userId), ("tenantUserId", Guid.NewGuid()), ("tenantId", graph.TenantId), ("displayName", suffix), ("email", $"{suffix}@example.test"), ("normalizedEmail", $"{suffix}@EXAMPLE.TEST"), ("now", new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero)));

    public static Task AddCollaboratorAsync(string connectionString, Graph graph, Guid userId)
        => PostgreSqlMigrationTestDatabase.ExecuteAsync(connectionString, """
INSERT INTO task_item_collaborators ("Id", "TenantId", "TaskItemId", "UserId", "AddedAt", "AddedByUserId")
VALUES (@id, @tenantId, @taskId, @userId, @now, @addedByUserId);
""", ("id", Guid.NewGuid()), ("tenantId", graph.TenantId), ("taskId", graph.TaskId), ("userId", userId), ("addedByUserId", graph.UserId), ("now", new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero)));
}
