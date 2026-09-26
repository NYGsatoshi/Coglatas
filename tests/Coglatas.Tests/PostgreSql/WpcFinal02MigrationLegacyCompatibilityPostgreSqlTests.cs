using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Tenancy;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Coglatas.Tests.PostgreSql;

[Collection("PostgreSqlTaskV1")]
[Trait("Scope", "WPCFinal02")]
public sealed class WpcFinal02MigrationLegacyCompatibilityPostgreSqlTests
{
    private const string Wpc01BaseMigration = "20260813100711_Wpc01WorkspaceCreateIdempotency";
    private const string Wpc02AMigration = "20260816041835_Wpc02AProjectVisibilityAndActivationProvenance";
    private const string Wpc02BMigration = "20260817023749_Wpc02BCapabilityGrantWorkspaceGeneral";

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task LegacyRowsUpgradeAcrossWpcChainWithoutInventedClassificationOrDefaults()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database, Wpc01BaseMigration);

            var alphaGraph = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, "wpc-final02-alpha");
            var betaGraph = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, "wpc-final02-beta");
            var alphaLegacy = await SeedLegacyCompatibilityArtifactsAsync(database, alphaGraph, "Alpha Legacy");
            var betaLegacy = await SeedLegacyCompatibilityArtifactsAsync(database, betaGraph, "Beta Legacy");

            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);

            await AssertLegacyUpgradedAsync(database, alphaGraph, alphaLegacy);
            await AssertLegacyUpgradedAsync(database, betaGraph, betaLegacy);
            await AssertNoSynthesizedCanonicalStateAsync(database);
            await AssertWpcStructuresAsync(database, expectA: true, expectB: true, expectD: true);

            await using var context = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            Assert.False(context.Database.HasPendingModelChanges());
        });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task WpcMigrationRollbackBoundariesPreserveLegacyRowsAndReapplyCleanly()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database, Wpc01BaseMigration);
            var graph = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, "wpc-final02-rollback");
            var legacy = await SeedLegacyCompatibilityArtifactsAsync(database, graph, "Rollback Legacy");

            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            await AssertLegacyUpgradedAsync(database, graph, legacy);
            await AssertNoSynthesizedCanonicalStateAsync(database);
            await AssertWpcStructuresAsync(database, expectA: true, expectB: true, expectD: true);

            await PostgreSqlMigrationTestDatabase.MigrateAsync(database, Wpc02BMigration);
            await AssertWpcStructuresAsync(database, expectA: true, expectB: true, expectD: false);
            await AssertLegacyUpgradedAsync(database, graph, legacy);

            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            await AssertWpcStructuresAsync(database, expectA: true, expectB: true, expectD: true);
            await AssertLegacyUpgradedAsync(database, graph, legacy);
            await AssertNoSynthesizedCanonicalStateAsync(database);

            await PostgreSqlMigrationTestDatabase.MigrateAsync(database, Wpc02AMigration);
            await AssertWpcStructuresAsync(database, expectA: true, expectB: false, expectD: false);
            await AssertLegacyCoreRowsAsync(database, graph, legacy);

            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            await AssertWpcStructuresAsync(database, expectA: true, expectB: true, expectD: true);
            await AssertLegacyUpgradedAsync(database, graph, legacy);
            await AssertNoSynthesizedCanonicalStateAsync(database);

            await PostgreSqlMigrationTestDatabase.MigrateAsync(database, Wpc01BaseMigration);
            await AssertWpcStructuresAsync(database, expectA: false, expectB: false, expectD: false);
            await AssertLegacyCoreRowsAsync(database, graph, legacy);

            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            await AssertWpcStructuresAsync(database, expectA: true, expectB: true, expectD: true);
            await AssertLegacyUpgradedAsync(database, graph, legacy);
            await AssertNoSynthesizedCanonicalStateAsync(database);

            await using var context = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            Assert.False(context.Database.HasPendingModelChanges());
        });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task MigrationConstraintsPreserveTenantIsolationAndCanonicalDefaultIdentity()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            var alpha = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, "wpc-final02-isolation-alpha");
            var beta = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(database, "wpc-final02-isolation-beta");
            await AddActiveTenantMembershipAsync(database, alpha);

            var templateId = Guid.NewGuid();
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(
                database,
                """
                INSERT INTO task_workflow_templates
                    ("Id", "TenantId", "Name", "ReviewEnforcementEnabled", "VersionNo")
                VALUES
                    (@id, @tenantId, 'Alpha template', TRUE, 1);
                """,
                ("id", templateId),
                ("tenantId", alpha.TenantId));

            var tenantDefaultError = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgreSqlMigrationTestDatabase.ExecuteAsync(
                    database,
                    """
                    INSERT INTO tenant_task_workflow_defaults
                        ("TenantId", "TemplateId", "VersionNo")
                    VALUES
                        (@tenantId, @templateId, 1);
                    """,
                    ("tenantId", beta.TenantId),
                    ("templateId", templateId)));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, tenantDefaultError.SqlState);
            Assert.Equal(
                "FK_tenant_task_workflow_defaults_templates_TenantId_TemplateId",
                tenantDefaultError.ConstraintName);

            var workspaceDefaultError = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgreSqlMigrationTestDatabase.ExecuteAsync(
                    database,
                    """
                    INSERT INTO workspace_task_workflow_defaults
                        ("TenantId", "WorkspaceId", "TemplateId", "VersionNo")
                    VALUES
                        (@tenantId, @workspaceId, @templateId, 1);
                    """,
                    ("tenantId", beta.TenantId),
                    ("workspaceId", beta.WorkspaceId),
                    ("templateId", templateId)));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, workspaceDefaultError.SqlState);
            Assert.Equal(
                "FK_workspace_task_workflow_defaults_templates_TenantId_Template",
                workspaceDefaultError.ConstraintName);

            await InsertCanonicalConversationAsync(
                database,
                alpha,
                Guid.NewGuid(),
                projectId: null,
                title: "general",
                defaultKind: "WorkspaceGeneral",
                type: "WorkspaceChannel");
            var duplicateWorkspaceGeneral = await Assert.ThrowsAsync<PostgresException>(() =>
                InsertCanonicalConversationAsync(
                    database,
                    alpha,
                    Guid.NewGuid(),
                    projectId: null,
                    title: "renamed-general",
                    defaultKind: "WorkspaceGeneral",
                    type: "WorkspaceChannel"));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicateWorkspaceGeneral.SqlState);
            Assert.Equal(
                "IX_conversations_TenantId_WorkspaceId_DefaultKind",
                duplicateWorkspaceGeneral.ConstraintName);

            await InsertCanonicalConversationAsync(
                database,
                alpha,
                Guid.NewGuid(),
                alpha.ProjectId,
                "general",
                "ProjectGeneral",
                "ProjectChannel");
            var duplicateProjectGeneral = await Assert.ThrowsAsync<PostgresException>(() =>
                InsertCanonicalConversationAsync(
                    database,
                    alpha,
                    Guid.NewGuid(),
                    alpha.ProjectId,
                    "renamed-general",
                    "ProjectGeneral",
                    "ProjectChannel"));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicateProjectGeneral.SqlState);
            Assert.Equal(
                "IX_conversations_TenantId_ProjectId_DefaultKind",
                duplicateProjectGeneral.ConstraintName);

            var now = new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(
                database,
                """
                INSERT INTO capability_grants
                    ("Id", "TenantId", "SubjectUserId", "CapabilityKey", "ScopeType", "ScopeId",
                     "GrantedByUserId", "GrantedAt", "ExpiresAt", "RevokedAt", "VersionNo", "CreatedAt")
                VALUES
                    (@id, @tenantId, @subjectUserId, @capabilityKey, 'Workspace', @scopeId,
                     @grantedByUserId, @grantedAt, @expiresAt, NULL, 1, @grantedAt);
                """,
                ("id", Guid.NewGuid()),
                ("tenantId", alpha.TenantId),
                ("subjectUserId", alpha.UserId),
                ("capabilityKey", CapabilityKeys.ProjectCreate),
                ("scopeId", beta.WorkspaceId),
                ("grantedByUserId", alpha.UserId),
                ("grantedAt", now.AddMinutes(-5)),
                ("expiresAt", now.AddHours(1)));

            Assert.Equal(
                1L,
                await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(
                    database,
                    """
                    SELECT COUNT(*)
                    FROM capability_grants
                    WHERE "TenantId" = @tenantId
                      AND "ScopeId" = @scopeId;
                    """,
                    ("tenantId", alpha.TenantId),
                    ("scopeId", beta.WorkspaceId)));

            var currentTenant = new CurrentTenantService();
            currentTenant.SetTenant(alpha.TenantId, "wpc-final02-isolation-alpha");
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(database)
                .Options;
            await using var context = new AppDbContext(options, currentTenant);
            var evaluator = new CapabilityGrantEvaluator(
                new CapabilityGrantRepository(context),
                new TenantRepository(context),
                new WorkspaceRepository(context),
                currentTenant,
                new FixedClock(now));

            Assert.False(await evaluator.HasActiveGrantAsync(
                alpha.UserId,
                alpha.TenantId,
                CapabilityKeys.ProjectCreate,
                CapabilityScopeType.Workspace,
                beta.WorkspaceId));

            await using var platform = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            Assert.Empty(await platform.Database.GetPendingMigrationsAsync());
            Assert.False(platform.Database.HasPendingModelChanges());
        });
    }

    private static async Task<LegacySeed> SeedLegacyCompatibilityArtifactsAsync(
        string database,
        TaskV1MigrationRawSqlSeed.Graph graph,
        string name)
    {
        var conversationId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        var stages = new[]
        {
            new LegacyStage(Guid.NewGuid(), "Backlog", "Backlog", 1000, IsInitial: true, IsTerminal: false),
            new LegacyStage(Guid.NewGuid(), "Todo", "Todo", 2000, IsInitial: false, IsTerminal: false),
            new LegacyStage(Guid.NewGuid(), "In Progress", "InProgress", 3000, IsInitial: false, IsTerminal: false),
            new LegacyStage(Guid.NewGuid(), "Review", "Review", 4000, IsInitial: false, IsTerminal: false),
            new LegacyStage(Guid.NewGuid(), "Done", "Done", 5000, IsInitial: false, IsTerminal: true),
            new LegacyStage(Guid.NewGuid(), "Cancelled", "Cancelled", 6000, IsInitial: false, IsTerminal: true)
        };
        var now = new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero);

        await PostgreSqlMigrationTestDatabase.ExecuteAsync(
            database,
            """
            INSERT INTO conversations
                ("Id", "TenantId", "WorkspaceId", "ProjectId", "Type", "Title",
                 "ParentConversationId", "RootConversationId", "IsArchived", "IsLocked",
                 "CreatedByUserId", "CreatedAt")
            VALUES
                (@id, @tenantId, @workspaceId, @projectId, 'ProjectChannel', 'general',
                 NULL, NULL, FALSE, FALSE, @createdByUserId, @createdAt);
            """,
            ("id", conversationId),
            ("tenantId", graph.TenantId),
            ("workspaceId", graph.WorkspaceId),
            ("projectId", graph.ProjectId),
            ("createdByUserId", graph.UserId),
            ("createdAt", now));

        await PostgreSqlMigrationTestDatabase.ExecuteAsync(
            database,
            """
            INSERT INTO task_workflow_definitions
                ("Id", "TenantId", "WorkspaceId", "ProjectId", "Name", "ReviewEnforcementEnabled",
                 "KanbanDefaultSwimlane", "VersionNo")
            VALUES
                (@id, @tenantId, @workspaceId, @projectId, @name, FALSE, 'None', 7);
            """,
            ("id", definitionId),
            ("tenantId", graph.TenantId),
            ("workspaceId", graph.WorkspaceId),
            ("projectId", graph.ProjectId),
            ("name", name));

        foreach (var stage in stages)
        {
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(
                database,
                """
                INSERT INTO task_workflow_stages
                    ("Id", "TenantId", "WorkspaceId", "ProjectId", "DefinitionId", "Name",
                     "InternalCategory", "SortKey", "WipWarningLimit", "IsInitialStage",
                     "IsTerminalStage", "VersionNo")
                VALUES
                    (@id, @tenantId, @workspaceId, @projectId, @definitionId, @name,
                     @category, @sortKey, NULL, @isInitial, @isTerminal, 3);
                """,
                ("id", stage.Id),
                ("tenantId", graph.TenantId),
                ("workspaceId", graph.WorkspaceId),
                ("projectId", graph.ProjectId),
                ("definitionId", definitionId),
                ("name", stage.Name),
                ("category", stage.Category),
                ("sortKey", stage.SortKey),
                ("isInitial", stage.IsInitial),
                ("isTerminal", stage.IsTerminal));
        }

        await PostgreSqlMigrationTestDatabase.ExecuteAsync(
            database,
            """
            UPDATE task_items
            SET "WorkflowStageId" = @workflowStageId
            WHERE "Id" = @taskId;
            """,
            ("workflowStageId", stages[1].Id),
            ("taskId", graph.TaskId));

        return new LegacySeed(conversationId, definitionId, stages, stages[1].Id, name);
    }

    private static async Task AssertLegacyUpgradedAsync(
        string database,
        TaskV1MigrationRawSqlSeed.Graph graph,
        LegacySeed legacy)
    {
        var project = Assert.Single(await PostgreSqlMigrationTestDatabase.QueryAsync(
            database,
            """
            SELECT "Visibility", "ActivationState", "ActivatedAtUtc", "ActivationVersion",
                   "SuspendedFromStatus", "ArchivedFromStatus"
            FROM projects
            WHERE "Id" = @projectId;
            """,
            reader => new LegacyProjectProjection(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)),
            ("projectId", graph.ProjectId)));
        Assert.Null(project.Visibility);
        Assert.Equal("LegacyUnknown", project.ActivationState);
        Assert.Null(project.ActivatedAtUtc);
        Assert.Null(project.ActivationVersion);
        Assert.Null(project.SuspendedFromStatus);
        Assert.Null(project.ArchivedFromStatus);

        var conversation = Assert.Single(await PostgreSqlMigrationTestDatabase.QueryAsync(
            database,
            """
            SELECT "TenantId", "WorkspaceId", "ProjectId", "Type", "Title", "Visibility", "DefaultKind"
            FROM conversations
            WHERE "Id" = @conversationId;
            """,
            reader => new LegacyConversationProjection(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)),
            ("conversationId", legacy.ConversationId)));
        Assert.Equal(graph.TenantId, conversation.TenantId);
        Assert.Equal(graph.WorkspaceId, conversation.WorkspaceId);
        Assert.Equal(graph.ProjectId, conversation.ProjectId);
        Assert.Equal("ProjectChannel", conversation.Type);
        Assert.Equal("general", conversation.Title);
        Assert.Null(conversation.Visibility);
        Assert.Null(conversation.DefaultKind);

        await AssertWorkflowPreservedAsync(database, graph, legacy);
    }

    private static async Task AssertLegacyCoreRowsAsync(
        string database,
        TaskV1MigrationRawSqlSeed.Graph graph,
        LegacySeed legacy)
    {
        Assert.Equal(
            1L,
            await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(
                database,
                "SELECT COUNT(*) FROM projects WHERE \"Id\" = @id;",
                ("id", graph.ProjectId)));
        Assert.Equal(
            1L,
            await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(
                database,
                "SELECT COUNT(*) FROM conversations WHERE \"Id\" = @id;",
                ("id", legacy.ConversationId)));
        await AssertWorkflowPreservedAsync(database, graph, legacy);
    }

    private static async Task AssertWorkflowPreservedAsync(
        string database,
        TaskV1MigrationRawSqlSeed.Graph graph,
        LegacySeed legacy)
    {
        var definition = Assert.Single(await PostgreSqlMigrationTestDatabase.QueryAsync(
            database,
            """
            SELECT "TenantId", "WorkspaceId", "ProjectId", "Name", "ReviewEnforcementEnabled",
                   "KanbanDefaultSwimlane", "VersionNo"
            FROM task_workflow_definitions
            WHERE "Id" = @definitionId;
            """,
            reader => new LegacyWorkflowProjection(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                reader.GetString(5),
                reader.GetInt64(6)),
            ("definitionId", legacy.DefinitionId)));
        Assert.Equal(graph.TenantId, definition.TenantId);
        Assert.Equal(graph.WorkspaceId, definition.WorkspaceId);
        Assert.Equal(graph.ProjectId, definition.ProjectId);
        Assert.Equal(legacy.WorkflowName, definition.Name);
        Assert.False(definition.ReviewEnforcementEnabled);
        Assert.Equal("None", definition.KanbanDefaultSwimlane);
        Assert.Equal(7L, definition.VersionNo);

        var stages = await PostgreSqlMigrationTestDatabase.QueryAsync(
            database,
            """
            SELECT "Id", "Name", "InternalCategory", "SortKey", "IsInitialStage", "IsTerminalStage", "VersionNo"
            FROM task_workflow_stages
            WHERE "DefinitionId" = @definitionId
            ORDER BY "SortKey";
            """,
            reader => new LegacyStageProjection(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                reader.GetBoolean(4),
                reader.GetBoolean(5),
                reader.GetInt64(6)),
            ("definitionId", legacy.DefinitionId));
        Assert.Equal(legacy.Stages.Length, stages.Count);
        for (var index = 0; index < legacy.Stages.Length; index++)
        {
            var expected = legacy.Stages[index];
            var actual = stages[index];
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.Category, actual.Category);
            Assert.Equal(expected.SortKey, actual.SortKey);
            Assert.Equal(expected.IsInitial, actual.IsInitial);
            Assert.Equal(expected.IsTerminal, actual.IsTerminal);
            Assert.Equal(3L, actual.VersionNo);
        }

        Assert.Equal(
            legacy.AssignedStageId,
            await PostgreSqlMigrationTestDatabase.ScalarAsync<Guid>(
                database,
                "SELECT \"WorkflowStageId\" FROM task_items WHERE \"Id\" = @taskId;",
                ("taskId", graph.TaskId)));
    }

    private static async Task AssertNoSynthesizedCanonicalStateAsync(string database)
    {
        Assert.Equal(
            0L,
            await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(
                database,
                "SELECT COUNT(*) FROM conversations WHERE \"DefaultKind\" IS NOT NULL;"));
        Assert.Equal(
            0L,
            await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(
                database,
                "SELECT COUNT(*) FROM capability_grants;"));
        Assert.Equal(
            0L,
            await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(
                database,
                "SELECT COUNT(*) FROM task_workflow_templates;"));
        Assert.Equal(
            0L,
            await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(
                database,
                "SELECT COUNT(*) FROM workspace_task_workflow_defaults;"));
        Assert.Equal(
            0L,
            await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(
                database,
                "SELECT COUNT(*) FROM tenant_task_workflow_defaults;"));
    }

    private static async Task AssertWpcStructuresAsync(
        string database,
        bool expectA,
        bool expectB,
        bool expectD)
    {
        Assert.Equal(expectA, await ColumnExistsAsync(database, "projects", "ActivationState"));
        Assert.Equal(expectA, await ColumnExistsAsync(database, "projects", "Visibility"));
        Assert.Equal(expectB, await TableExistsAsync(database, "capability_grants"));
        Assert.Equal(expectB, await ColumnExistsAsync(database, "conversations", "DefaultKind"));
        Assert.Equal(expectB, await ColumnExistsAsync(database, "conversations", "Visibility"));
        Assert.Equal(expectD, await TableExistsAsync(database, "task_workflow_templates"));
        Assert.Equal(expectD, await TableExistsAsync(database, "workspace_task_workflow_defaults"));
        Assert.Equal(expectD, await TableExistsAsync(database, "tenant_task_workflow_defaults"));
    }

    private static Task AddActiveTenantMembershipAsync(
        string database,
        TaskV1MigrationRawSqlSeed.Graph graph)
    {
        var now = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);
        return PostgreSqlMigrationTestDatabase.ExecuteAsync(
            database,
            """
            INSERT INTO tenant_users
                ("Id", "TenantId", "UserId", "Role", "Status", "JoinedAt", "CreatedAt")
            VALUES
                (@id, @tenantId, @userId, 'Member', 'Active', @now, @now);
            """,
            ("id", Guid.NewGuid()),
            ("tenantId", graph.TenantId),
            ("userId", graph.UserId),
            ("now", now));
    }

    private static Task InsertCanonicalConversationAsync(
        string database,
        TaskV1MigrationRawSqlSeed.Graph graph,
        Guid id,
        Guid? projectId,
        string title,
        string defaultKind,
        string type)
    {
        var now = new DateTimeOffset(2026, 8, 21, 9, 30, 0, TimeSpan.Zero);
        return PostgreSqlMigrationTestDatabase.ExecuteAsync(
            database,
            """
            INSERT INTO conversations
                ("Id", "TenantId", "WorkspaceId", "ProjectId", "Type", "Title", "Visibility", "DefaultKind",
                 "ParentConversationId", "RootConversationId", "IsArchived", "IsLocked",
                 "CreatedByUserId", "CreatedAt")
            VALUES
                (@id, @tenantId, @workspaceId, @projectId, @type, @title, 'PublicWithinScope', @defaultKind,
                 NULL, NULL, FALSE, FALSE, @createdByUserId, @createdAt);
            """,
            ("id", id),
            ("tenantId", graph.TenantId),
            ("workspaceId", graph.WorkspaceId),
            ("projectId", projectId),
            ("type", type),
            ("title", title),
            ("defaultKind", defaultKind),
            ("createdByUserId", graph.UserId),
            ("createdAt", now));
    }

    private static Task<bool> TableExistsAsync(string database, string tableName) =>
        PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(
            database,
            "SELECT to_regclass(current_schema() || '.' || @tableName) IS NOT NULL;",
            ("tableName", tableName));

    private static Task<bool> ColumnExistsAsync(string database, string tableName, string columnName) =>
        PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(
            database,
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = current_schema()
                  AND table_name = @tableName
                  AND column_name = @columnName);
            """,
            ("tableName", tableName),
            ("columnName", columnName));

    private sealed record LegacySeed(
        Guid ConversationId,
        Guid DefinitionId,
        LegacyStage[] Stages,
        Guid AssignedStageId,
        string WorkflowName);

    private sealed record LegacyStage(
        Guid Id,
        string Name,
        string Category,
        long SortKey,
        bool IsInitial,
        bool IsTerminal);

    private sealed record LegacyProjectProjection(
        string? Visibility,
        string ActivationState,
        DateTimeOffset? ActivatedAtUtc,
        int? ActivationVersion,
        string? SuspendedFromStatus,
        string? ArchivedFromStatus);

    private sealed record LegacyConversationProjection(
        Guid TenantId,
        Guid WorkspaceId,
        Guid? ProjectId,
        string Type,
        string? Title,
        string? Visibility,
        string? DefaultKind);

    private sealed record LegacyWorkflowProjection(
        Guid TenantId,
        Guid WorkspaceId,
        Guid ProjectId,
        string Name,
        bool ReviewEnforcementEnabled,
        string KanbanDefaultSwimlane,
        long VersionNo);

    private sealed record LegacyStageProjection(
        Guid Id,
        string Name,
        string Category,
        long SortKey,
        bool IsInitial,
        bool IsTerminal,
        long VersionNo);

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
