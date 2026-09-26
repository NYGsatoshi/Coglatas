using System.Data.Common;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coglatas.Tests.PostgreSql;

[Collection("PostgreSqlTaskV1")]
public sealed class TaskV1Pr05KanbanPostgreSqlTests
{
    private const string PreviousMigration = "20260729010000_AddMyTasksEffectiveWatchIndex";

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "TaskV1PR05")]
    public async Task DefaultSwimlaneMigrationAppliesCleanAndUpgradePreservesExistingBoardAndRollsBackAdditively()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();

        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async emptyDatabase =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(emptyDatabase);
            Assert.True(await ColumnExistsAsync(emptyDatabase));
            Assert.True(await IndexExistsAsync(emptyDatabase, "IX_task_items_ProjectId_WorkflowStageId_SortKey"));
            Assert.True(await IndexExistsAsync(emptyDatabase, "IX_task_workflow_stages_DefinitionId_SortKey"));
            await using var context = PostgreSqlMigrationTestDatabase.CreatePlatformContext(emptyDatabase);
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            Assert.False(context.Database.HasPendingModelChanges());
        });

        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async upgradeDatabase =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(upgradeDatabase, PreviousMigration);
            Assert.False(await ColumnExistsAsync(upgradeDatabase));
            var ids = await SeedExistingDefinitionAsync(upgradeDatabase);

            await PostgreSqlMigrationTestDatabase.MigrateAsync(upgradeDatabase);
            Assert.True(await ColumnExistsAsync(upgradeDatabase));
            Assert.Equal("None", await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(
                upgradeDatabase,
                """SELECT "KanbanDefaultSwimlane" FROM task_workflow_definitions WHERE "Id" = @id""",
                ("id", ids.DefinitionId)));

            await PostgreSqlMigrationTestDatabase.MigrateAsync(upgradeDatabase, PreviousMigration);
            Assert.False(await ColumnExistsAsync(upgradeDatabase));
            Assert.Equal(1L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(
                upgradeDatabase,
                """SELECT "VersionNo" FROM task_workflow_definitions WHERE "Id" = @id""",
                ("id", ids.DefinitionId)));
        });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "TaskV1PR05")]
    public async Task SnapshotQueryIsBoundedStableAndUsesFixedQueryCountWithoutPrivateColumns()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            var graph = await SeedBoardAsync(database);
            var interceptor = new CommandCaptureInterceptor();
            await using var context = CreateTenantContext(database, graph.Tenant, interceptor);
            var repository = new ProjectKanbanRepository(context);

            interceptor.Clear();
            var first = await repository.ReadAsync(
                graph.Project.Id,
                new DateTimeOffset(2026, 6, 29, 0, 0, 0, TimeSpan.Zero),
                includeOlderCompleted: false,
                primaryAssigneeUserId: null,
                targetGroupId: null,
                priority: null,
                parentTaskId: null,
                take: 2);

            Assert.NotNull(first);
            Assert.Equal(3, first.TotalCount);
            Assert.Equal(2, first.Tasks.Count);
            Assert.Equal(6, interceptor.Commands.Count);
            Assert.DoesNotContain(interceptor.Commands, sql => sql.Contains("\"Description\"", StringComparison.Ordinal));
            Assert.DoesNotContain(interceptor.Commands, sql => sql.Contains("\"BlockedReason\"", StringComparison.Ordinal));
            Assert.Contains(interceptor.Commands, sql => sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));

            context.ChangeTracker.Clear();
            var second = await repository.ReadAsync(
                graph.Project.Id,
                new DateTimeOffset(2026, 6, 29, 0, 0, 0, TimeSpan.Zero),
                false,
                null,
                null,
                null,
                null,
                2);
            Assert.Equal(first.Tasks.Select(task => task.Id), second!.Tasks.Select(task => task.Id));
            Assert.Equal(first.Tasks.Select(task => task.SortKey), second.Tasks.Select(task => task.SortKey));
        });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "TaskV1PR05")]
    public async Task ConcurrentMoveTokensConflictAndRollBackTaskAndBoardAtomically()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            var graph = await SeedBoardAsync(database);
            await using var first = CreateTenantContext(database, graph.Tenant);
            await using var second = CreateTenantContext(database, graph.Tenant);
            var taskId = await first.TaskItems.OrderBy(task => task.SortKey).Select(task => task.Id).FirstAsync();

            var firstTask = await first.TaskItems.SingleAsync(task => task.Id == taskId);
            var firstBoard = await first.TaskWorkflowDefinitions.SingleAsync(definition => definition.ProjectId == graph.Project.Id);
            var secondTask = await second.TaskItems.SingleAsync(task => task.Id == taskId);
            var secondBoard = await second.TaskWorkflowDefinitions.SingleAsync(definition => definition.ProjectId == graph.Project.Id);

            firstTask.SortKey = 1500;
            firstTask.VersionNo++;
            firstBoard.VersionNo++;
            await first.SaveChangesAsync();

            secondTask.SortKey = 1750;
            secondTask.VersionNo++;
            secondBoard.VersionNo++;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

            await using var verification = CreateTenantContext(database, graph.Tenant);
            var persistedTask = await verification.TaskItems.AsNoTracking().SingleAsync(task => task.Id == taskId);
            var persistedBoard = await verification.TaskWorkflowDefinitions.AsNoTracking()
                .SingleAsync(definition => definition.ProjectId == graph.Project.Id);
            Assert.Equal(1500, persistedTask.SortKey);
            Assert.Equal(2, persistedTask.VersionNo);
            Assert.Equal(2, persistedBoard.VersionNo);
        });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "TaskV1PR05")]
    public async Task ConcurrentConfigTokensConflictAndRollBackStageAndBoardAtomically()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            var graph = await SeedBoardAsync(database);
            await using var first = CreateTenantContext(database, graph.Tenant);
            await using var second = CreateTenantContext(database, graph.Tenant);
            var stageId = await first.TaskWorkflowStages
                .Where(stage => stage.ProjectId == graph.Project.Id && stage.InternalCategory == TaskStageCategory.Todo)
                .Select(stage => stage.Id)
                .SingleAsync();

            var firstStage = await first.TaskWorkflowStages.SingleAsync(stage => stage.Id == stageId);
            var firstBoard = await first.TaskWorkflowDefinitions.SingleAsync(definition => definition.ProjectId == graph.Project.Id);
            var secondStage = await second.TaskWorkflowStages.SingleAsync(stage => stage.Id == stageId);
            var secondBoard = await second.TaskWorkflowDefinitions.SingleAsync(definition => definition.ProjectId == graph.Project.Id);

            firstStage.WipWarningLimit = 2;
            firstStage.VersionNo++;
            firstBoard.KanbanDefaultSwimlane = ProjectKanbanSwimlane.Priority;
            firstBoard.VersionNo++;
            await first.SaveChangesAsync();

            secondStage.WipWarningLimit = 3;
            secondStage.VersionNo++;
            secondBoard.KanbanDefaultSwimlane = ProjectKanbanSwimlane.ParentTask;
            secondBoard.VersionNo++;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

            await using var verification = CreateTenantContext(database, graph.Tenant);
            var persistedStage = await verification.TaskWorkflowStages.AsNoTracking().SingleAsync(stage => stage.Id == stageId);
            var persistedBoard = await verification.TaskWorkflowDefinitions.AsNoTracking()
                .SingleAsync(definition => definition.ProjectId == graph.Project.Id);
            Assert.Equal(2, persistedStage.WipWarningLimit);
            Assert.Equal(2, persistedStage.VersionNo);
            Assert.Equal(ProjectKanbanSwimlane.Priority, persistedBoard.KanbanDefaultSwimlane);
            Assert.Equal(2, persistedBoard.VersionNo);
        });
    }

    private static async Task<bool> ColumnExistsAsync(string connectionString) =>
        await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(
            connectionString,
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'task_workflow_definitions'
                  AND column_name = 'KanbanDefaultSwimlane');
            """);

    private static Task<bool> IndexExistsAsync(string connectionString, string indexName) =>
        PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(
            connectionString,
            """
            SELECT EXISTS (
                SELECT 1
                FROM pg_indexes
                WHERE schemaname = 'public'
                  AND indexname = @indexName);
            """,
            ("indexName", indexName));

    private static async Task<(Guid DefinitionId, Guid ProjectId)> SeedExistingDefinitionAsync(string connectionString)
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        await PostgreSqlMigrationTestDatabase.ExecuteAsync(connectionString,
            """
            INSERT INTO tenants ("Id", "Name", "DisplayName", "Slug", "Status", "CreatedAt")
            VALUES (@tenant, 'PR05', 'PR05', @slug, 'Active', NOW());
            INSERT INTO users ("Id", "DisplayName", "Email", "NormalizedEmail", "PasswordHash", "Status", "CreatedAt")
            VALUES (@user, 'PR05', @email, @normalized, 'hash', 'Active', NOW());
            INSERT INTO workspaces ("Id", "TenantId", "Name", "Slug", "Status", "CreatedByUserId", "CreatedAt")
            VALUES (@workspace, @tenant, 'PR05 workspace', @workspaceSlug, 'Active', @user, NOW());
            INSERT INTO projects ("Id", "TenantId", "WorkspaceId", "OwnerUserId", "CreatedByUserId", "Name", "Slug", "Status", "CreatedAt")
            VALUES (@project, @tenant, @workspace, @user, @user, 'PR05 project', @projectSlug, 'Active', NOW());
            INSERT INTO task_workflow_definitions ("Id", "TenantId", "WorkspaceId", "ProjectId", "Name", "ReviewEnforcementEnabled", "VersionNo")
            VALUES (@definition, @tenant, @workspace, @project, 'Default', TRUE, 1);
            """,
            ("tenant", tenantId),
            ("slug", $"pr05-{tenantId:N}"),
            ("user", userId),
            ("email", $"pr05-{userId:N}@example.test"),
            ("normalized", $"PR05-{userId:N}@EXAMPLE.TEST"),
            ("workspace", workspaceId),
            ("workspaceSlug", $"workspace-{workspaceId:N}"),
            ("project", projectId),
            ("projectSlug", $"project-{projectId:N}"),
            ("definition", definitionId));
        return (definitionId, projectId);
    }

    private static async Task<(Tenant Tenant, Project Project)> SeedBoardAsync(string connectionString)
    {
        var tenantScope = new CurrentTenantService();
        tenantScope.SetPlatformScope();
        await using var platform = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options, tenantScope);
        var tenant = new Tenant { Name = "PR05 query", DisplayName = "PR05 query", Slug = $"pr05-query-{Guid.NewGuid():N}" };
        var user = new User { DisplayName = "PR05 actor", Email = $"pr05-{Guid.NewGuid():N}@example.test", NormalizedEmail = $"PR05-{Guid.NewGuid():N}@EXAMPLE.TEST", PasswordHash = "hash" };
        platform.AddRange(tenant, user);
        await platform.SaveChangesAsync();

        tenantScope.SetTenant(tenant.Id, tenant.Slug);
        platform.TenantUsers.Add(new TenantUser
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Role = TenantUserRole.Member,
            Status = TenantUserStatus.Active,
            JoinedAt = DateTimeOffset.UtcNow
        });
        var workspace = new Workspace { Name = "PR05 workspace", Slug = $"pr05-{Guid.NewGuid():N}", CreatedByUserId = user.Id, Status = WorkspaceStatus.Active };
        platform.Workspaces.Add(workspace);
        await platform.SaveChangesAsync();
        var project = new Project { WorkspaceId = workspace.Id, OwnerUserId = user.Id, CreatedByUserId = user.Id, Name = "PR05 board", Slug = $"pr05-{Guid.NewGuid():N}", Status = ProjectStatus.Active };
        platform.Projects.Add(project);
        await platform.SaveChangesAsync();
        var todo = await platform.TaskWorkflowStages.SingleAsync(stage => stage.ProjectId == project.Id && stage.InternalCategory == TaskStageCategory.Todo);
        platform.TaskItems.AddRange(
            Task(tenant, workspace, project, todo, user, "A", 1000),
            Task(tenant, workspace, project, todo, user, "B", 2000),
            Task(tenant, workspace, project, todo, user, "C", 3000));
        await platform.SaveChangesAsync();
        return (tenant, project);
    }

    private static TaskItem Task(Tenant tenant, Workspace workspace, Project project, TaskWorkflowStage stage, User user, string title, long sortKey) => new()
    {
        TenantId = tenant.Id,
        WorkspaceId = workspace.Id,
        ProjectId = project.Id,
        WorkflowStageId = stage.Id,
        Title = title,
        SortKey = sortKey,
        CreatedByUserId = user.Id,
        PrimaryAssigneeUserId = user.Id,
        Priority = TaskPriority.Medium,
        VersionNo = 1
    };

    private static AppDbContext CreateTenantContext(string connectionString, Tenant tenant, params IInterceptor[] interceptors)
    {
        var scope = new CurrentTenantService();
        scope.SetTenant(tenant.Id, tenant.Slug);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString);
        if (interceptors.Length > 0)
            options.AddInterceptors(interceptors);
        return new AppDbContext(
            options.Options,
            scope);
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public void Clear() => Commands.Clear();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
