using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Coglatas.Tests.PostgreSql;

[Collection("PostgreSqlTaskV1")]
[Trait("Scope", "WPC02A")]
public sealed class Wpc02AProjectGovernancePostgreSqlTests
{
    private const string Wpc01BaseMigration = "20260813100711_Wpc01WorkspaceCreateIdempotency";

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task ExistingProjectUpgradesToLegacyUnknownWithoutInventingVisibilityOrActivationMetadata()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async testConnectionString =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(testConnectionString, Wpc01BaseMigration);
            var graph = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(testConnectionString, "wpc02a-legacy");

            await PostgreSqlMigrationTestDatabase.MigrateAsync(testConnectionString);

            var rows = await PostgreSqlMigrationTestDatabase.QueryAsync(
                testConnectionString,
                """
                SELECT "Visibility", "ActivationState", "ActivatedAtUtc", "ActivationVersion",
                       "SuspendedFromStatus", "ArchivedFromStatus"
                FROM projects
                WHERE "Id" = @projectId;
                """,
                reader => new LegacyProjection(
                    reader.IsDBNull(0) ? null : reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
                    reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5)),
                ("projectId", graph.ProjectId));

            var project = Assert.Single(rows);
            Assert.Null(project.Visibility);
            Assert.Equal("LegacyUnknown", project.ActivationState);
            Assert.Null(project.ActivatedAtUtc);
            Assert.Null(project.ActivationVersion);
            Assert.Null(project.SuspendedFromStatus);
            Assert.Null(project.ArchivedFromStatus);

            await using var context = PostgreSqlMigrationTestDatabase.CreatePlatformContext(testConnectionString);
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            Assert.False(context.Database.HasPendingModelChanges());
        });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task DatabaseRejectsPublicLegacyUnknownVisibilityAndFabricatedActivatedState()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async testConnectionString =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(testConnectionString, Wpc01BaseMigration);
            var graph = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(testConnectionString, "wpc02a-constraints");
            await PostgreSqlMigrationTestDatabase.MigrateAsync(testConnectionString);

            var visibilityError = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgreSqlMigrationTestDatabase.ExecuteAsync(
                    testConnectionString,
                    "UPDATE projects SET \"Visibility\" = 'LegacyUnknown' WHERE \"Id\" = @projectId;",
                    ("projectId", graph.ProjectId)));
            Assert.Equal(PostgresErrorCodes.CheckViolation, visibilityError.SqlState);
            Assert.Equal("CK_projects_visibility", visibilityError.ConstraintName);

            var activationError = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgreSqlMigrationTestDatabase.ExecuteAsync(
                    testConnectionString,
                    """
                    UPDATE projects
                    SET "ActivationState" = 'Activated',
                        "ActivatedAtUtc" = NULL,
                        "ActivationVersion" = NULL
                    WHERE "Id" = @projectId;
                    """,
                    ("projectId", graph.ProjectId)));
            Assert.Equal(PostgresErrorCodes.CheckViolation, activationError.SqlState);
            Assert.Equal("CK_projects_activation_provenance", activationError.ConstraintName);
        });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task ArchivedWorkspaceReadScopeRequiresCurrentMembershipEvenForSystemAdmin()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async testConnectionString =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(testConnectionString, Wpc01BaseMigration);
            var graph = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(testConnectionString, "wpc02a-archived");
            var memberUserId = Guid.NewGuid();
            var systemAdminUserId = Guid.NewGuid();
            await TaskV1MigrationRawSqlSeed.AddUserAsync(testConnectionString, graph, memberUserId, "wpc02a-member");
            await TaskV1MigrationRawSqlSeed.AddUserAsync(testConnectionString, graph, systemAdminUserId, "wpc02a-system-admin");

            await PostgreSqlMigrationTestDatabase.MigrateAsync(testConnectionString);

            await using (var setup = PostgreSqlMigrationTestDatabase.CreatePlatformContext(testConnectionString))
            {
                setup.WorkspaceMembers.Add(new WorkspaceMember
                {
                    TenantId = graph.TenantId,
                    WorkspaceId = graph.WorkspaceId,
                    UserId = memberUserId,
                    Role = WorkspaceRole.Member,
                    Status = MembershipStatus.Active,
                    JoinedAt = new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero)
                });
                await setup.SaveChangesAsync();
            }

            await PostgreSqlMigrationTestDatabase.ExecuteAsync(
                testConnectionString,
                """
                UPDATE workspaces
                SET "Status" = 'Archived'
                WHERE "Id" = @workspaceId;

                UPDATE users
                SET "SystemRole" = 'SystemAdmin'
                WHERE "Id" = @systemAdminUserId;

                UPDATE projects
                SET "Visibility" = 'WorkspaceVisible',
                    "ActivationState" = 'Activated',
                    "ActivatedAtUtc" = TIMESTAMPTZ '2026-08-16T00:00:00Z',
                    "ActivationVersion" = 1
                WHERE "Id" = @projectId;
                """,
                ("workspaceId", graph.WorkspaceId),
                ("systemAdminUserId", systemAdminUserId),
                ("projectId", graph.ProjectId));

            await using var context = PostgreSqlMigrationTestDatabase.CreatePlatformContext(testConnectionString);
            var repository = new ProjectRepository(context);

            var memberVisible = await repository.ListVisibleAsync(memberUserId);
            var systemAdminVisible = await repository.ListVisibleAsync(systemAdminUserId);
            var readers = await repository.ListCurrentReaderUserIdsAsync(graph.ProjectId);

            Assert.Contains(memberVisible, project => project.Id == graph.ProjectId);
            Assert.DoesNotContain(systemAdminVisible, project => project.Id == graph.ProjectId);
            Assert.Contains(memberUserId, readers);
            Assert.DoesNotContain(systemAdminUserId, readers);
        });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task NeverActivatedPlanningProjectSuspendsAndResumesOnlyToRecordedPlanningState()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async testConnectionString =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(testConnectionString, Wpc01BaseMigration);
            var graph = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(testConnectionString, "wpc02a-planning-resume");
            await PostgreSqlMigrationTestDatabase.MigrateAsync(testConnectionString);
            await SetCanonicalProjectAsync(
                testConnectionString,
                graph.ProjectId,
                ProjectStatus.Planning,
                ProjectActivationState.NeverActivated);

            await using (var context = CreateGovernanceContext(testConnectionString))
            {
                var project = await context.Projects.SingleAsync(item => item.Id == graph.ProjectId);
                project.Status = ProjectStatus.Suspended;
                await context.SaveChangesAsync();

                Assert.Equal(ProjectStatus.Suspended, project.Status);
                Assert.Equal(ProjectStatus.Planning, project.SuspendedFromStatus);
            }

            await using (var rejected = CreateGovernanceContext(testConnectionString))
            {
                var project = await rejected.Projects.SingleAsync(item => item.Id == graph.ProjectId);
                project.Status = ProjectStatus.Active;

                await Assert.ThrowsAsync<InvalidOperationException>(() => rejected.SaveChangesAsync());
            }

            Assert.Equal(
                "Suspended",
                await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(
                    testConnectionString,
                    "SELECT \"Status\" FROM projects WHERE \"Id\" = @projectId;",
                    ("projectId", graph.ProjectId)));

            await using (var resumed = CreateGovernanceContext(testConnectionString))
            {
                var project = await resumed.Projects.SingleAsync(item => item.Id == graph.ProjectId);
                project.Status = ProjectStatus.Planning;
                await resumed.SaveChangesAsync();

                Assert.Equal(ProjectStatus.Planning, project.Status);
                Assert.Null(project.SuspendedFromStatus);
            }
        });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task ActivatedProjectPreservesNestedSuspendArchiveRecoveryProvenance()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async testConnectionString =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(testConnectionString, Wpc01BaseMigration);
            var graph = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(testConnectionString, "wpc02a-nested-recovery");
            await PostgreSqlMigrationTestDatabase.MigrateAsync(testConnectionString);
            await SetCanonicalProjectAsync(
                testConnectionString,
                graph.ProjectId,
                ProjectStatus.Active,
                ProjectActivationState.Activated);

            await using var context = CreateGovernanceContext(testConnectionString);
            var project = await context.Projects.SingleAsync(item => item.Id == graph.ProjectId);

            project.Status = ProjectStatus.Suspended;
            await context.SaveChangesAsync();
            Assert.Equal(ProjectStatus.Active, project.SuspendedFromStatus);

            project.Status = ProjectStatus.Archived;
            await context.SaveChangesAsync();
            Assert.Equal(ProjectStatus.Suspended, project.ArchivedFromStatus);
            Assert.Equal(ProjectStatus.Active, project.SuspendedFromStatus);

            project.Status = ProjectStatus.Suspended;
            await context.SaveChangesAsync();
            Assert.Null(project.ArchivedFromStatus);
            Assert.Equal(ProjectStatus.Active, project.SuspendedFromStatus);

            project.Status = ProjectStatus.Active;
            await context.SaveChangesAsync();
            Assert.Null(project.SuspendedFromStatus);
            Assert.Null(project.ArchivedFromStatus);
        });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task ArchivedProjectRejectsDifferentRecoveryStateAndRetainsRecordedSource()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async testConnectionString =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(testConnectionString, Wpc01BaseMigration);
            var graph = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(testConnectionString, "wpc02a-archive-exact");
            await PostgreSqlMigrationTestDatabase.MigrateAsync(testConnectionString);
            await SetCanonicalProjectAsync(
                testConnectionString,
                graph.ProjectId,
                ProjectStatus.Active,
                ProjectActivationState.Activated);

            await using (var archived = CreateGovernanceContext(testConnectionString))
            {
                var project = await archived.Projects.SingleAsync(item => item.Id == graph.ProjectId);
                project.Status = ProjectStatus.Archived;
                await archived.SaveChangesAsync();
                Assert.Equal(ProjectStatus.Active, project.ArchivedFromStatus);
            }

            await using (var rejected = CreateGovernanceContext(testConnectionString))
            {
                var project = await rejected.Projects.SingleAsync(item => item.Id == graph.ProjectId);
                project.Status = ProjectStatus.Review;
                await Assert.ThrowsAsync<InvalidOperationException>(() => rejected.SaveChangesAsync());
            }

            var state = Assert.Single(await PostgreSqlMigrationTestDatabase.QueryAsync(
                testConnectionString,
                """
                SELECT "Status", "ArchivedFromStatus"
                FROM projects
                WHERE "Id" = @projectId;
                """,
                reader => new RecoveryProjection(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)),
                ("projectId", graph.ProjectId)));
            Assert.Equal("Archived", state.Status);
            Assert.Equal("Active", state.ArchivedFromStatus);

            await using (var restored = CreateGovernanceContext(testConnectionString))
            {
                var project = await restored.Projects.SingleAsync(item => item.Id == graph.ProjectId);
                project.Status = ProjectStatus.Active;
                await restored.SaveChangesAsync();
                Assert.Null(project.ArchivedFromStatus);
            }
        });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task LegacyUnknownArchivedProjectCannotRecoverEvenWithFabricatedSourceStatus()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async testConnectionString =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(testConnectionString, Wpc01BaseMigration);
            var graph = await TaskV1MigrationRawSqlSeed.CreateGraphAsync(testConnectionString, "wpc02a-legacy-recovery");
            await PostgreSqlMigrationTestDatabase.MigrateAsync(testConnectionString);

            await PostgreSqlMigrationTestDatabase.ExecuteAsync(
                testConnectionString,
                """
                UPDATE projects
                SET "Status" = 'Archived',
                    "ArchivedFromStatus" = 'Active'
                WHERE "Id" = @projectId;
                """,
                ("projectId", graph.ProjectId));

            await using var context = CreateGovernanceContext(testConnectionString);
            var project = await context.Projects.SingleAsync(item => item.Id == graph.ProjectId);
            Assert.Equal(ProjectActivationState.LegacyUnknown, project.ActivationState);
            project.Status = ProjectStatus.Active;

            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());

            Assert.Equal(
                "Archived",
                await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(
                    testConnectionString,
                    "SELECT \"Status\" FROM projects WHERE \"Id\" = @projectId;",
                    ("projectId", graph.ProjectId)));
        });
    }

    private static AppDbContext CreateGovernanceContext(string connectionString)
    {
        var tenant = new CurrentTenantService();
        tenant.SetPlatformScope();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(new ProjectGovernanceSaveChangesInterceptor())
            .Options;
        return new AppDbContext(options, tenant);
    }

    private static Task SetCanonicalProjectAsync(
        string connectionString,
        Guid projectId,
        ProjectStatus status,
        ProjectActivationState activationState)
    {
        var activated = activationState == ProjectActivationState.Activated;
        return PostgreSqlMigrationTestDatabase.ExecuteAsync(
            connectionString,
            """
            UPDATE projects
            SET "Status" = @status,
                "Visibility" = 'MembersOnly',
                "ActivationState" = @activationState,
                "ActivatedAtUtc" = CASE WHEN @activated THEN TIMESTAMPTZ '2026-08-16T00:00:00Z' ELSE NULL END,
                "ActivationVersion" = CASE WHEN @activated THEN 1 ELSE NULL END,
                "SuspendedFromStatus" = NULL,
                "ArchivedFromStatus" = NULL
            WHERE "Id" = @projectId;
            """,
            ("status", status.ToString()),
            ("activationState", activationState.ToString()),
            ("activated", activated),
            ("projectId", projectId));
    }

    private sealed record LegacyProjection(
        string? Visibility,
        string ActivationState,
        DateTimeOffset? ActivatedAtUtc,
        int? ActivationVersion,
        string? SuspendedFromStatus,
        string? ArchivedFromStatus);

    private sealed record RecoveryProjection(string Status, string? ArchivedFromStatus);
}
