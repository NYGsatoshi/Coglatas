using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Projects;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Coglatas.Tests.PostgreSql;

[Trait("Scope", "WPC02D")]
public sealed class Wpc02DConfiguredWorkflowSourcePostgreSqlTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task PersistedConfiguredWorkflowSourceUsesWorkspaceThenTenantThenCanonicalFallback()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            var graph = await SeedConfiguredGraphAsync(database, "precedence");

            await using var db = CreateTenantContext(database, graph.TenantId, graph.TenantSlug);
            var project = await db.Projects.SingleAsync(item => item.Id == graph.ProjectId);
            var resolver = new ProjectTaskWorkflowResolver(new ConfiguredProjectTaskWorkflowSource(db));

            var workspace = await resolver.ResolveAsync(project);
            Assert.True(workspace.IsSuccess, workspace.Error);
            Assert.Equal("Workspace Template", workspace.Value!.DisplayName);
            Assert.StartsWith($"TaskWorkflowTemplate/{graph.WorkspaceTemplateId:D}/v", workspace.Value.SourceIdentity);
            Assert.Equal(
                new[] { "Workspace Todo", "Workspace Done" },
                workspace.Value.Stages.Select(stage => stage.Name).ToArray());

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""DELETE FROM "workspace_task_workflow_defaults" WHERE "TenantId" = {graph.TenantId} AND "WorkspaceId" = {graph.WorkspaceId}""");

            var tenant = await resolver.ResolveAsync(project);
            Assert.True(tenant.IsSuccess, tenant.Error);
            Assert.Equal("Tenant Template", tenant.Value!.DisplayName);
            Assert.StartsWith($"TaskWorkflowTemplate/{graph.TenantTemplateId:D}/v", tenant.Value.SourceIdentity);
            Assert.Equal(
                new[] { "Tenant Ready", "Tenant Complete" },
                tenant.Value.Stages.Select(stage => stage.Name).ToArray());

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""DELETE FROM "tenant_task_workflow_defaults" WHERE "TenantId" = {graph.TenantId}""");

            var fallback = await resolver.ResolveAsync(project);
            Assert.True(fallback.IsSuccess, fallback.Error);
            Assert.Equal(ProjectTaskWorkflowResolver.CanonicalFallbackIdentity, fallback.Value!.SourceIdentity);
            Assert.Equal(6, fallback.Value.Stages.Count);
        });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task PersistedWorkspaceTemplateIsCopiedIntoProjectWorkflow()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            var graph = await SeedConfiguredGraphAsync(database, "copy");

            await using var db = CreateTenantContext(database, graph.TenantId, graph.TenantSlug);
            var project = await db.Projects.SingleAsync(item => item.Id == graph.ProjectId);
            var provisioner = new ProjectTaskWorkflowActivationProvisioner(
                new ProjectActivationWorkflowStore(db),
                new ProjectTaskWorkflowResolver(new ConfiguredProjectTaskWorkflowSource(db)));

            var staged = await provisioner.StageAsync(project);
            Assert.True(staged.IsSuccess, staged.Error);
            await db.SaveChangesAsync();

            var definition = await db.TaskWorkflowDefinitions
                .AsNoTracking()
                .SingleAsync(item => item.ProjectId == graph.ProjectId);
            Assert.Equal("Workspace Template", definition.Name);
            Assert.False(definition.ReviewEnforcementEnabled);

            var stages = await db.TaskWorkflowStages
                .AsNoTracking()
                .Where(item => item.ProjectId == graph.ProjectId)
                .OrderBy(item => item.SortKey)
                .ToListAsync();
            Assert.Collection(
                stages,
                stage =>
                {
                    Assert.Equal("Workspace Todo", stage.Name);
                    Assert.Equal(TaskStageCategory.Todo, stage.InternalCategory);
                    Assert.True(stage.IsInitialStage);
                    Assert.False(stage.IsTerminalStage);
                    Assert.Equal(3, stage.WipWarningLimit);
                },
                stage =>
                {
                    Assert.Equal("Workspace Done", stage.Name);
                    Assert.Equal(TaskStageCategory.Done, stage.InternalCategory);
                    Assert.False(stage.IsInitialStage);
                    Assert.True(stage.IsTerminalStage);
                    Assert.Null(stage.WipWarningLimit);
                });
        });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task ConfiguredWorkflowDefaultReferencesRejectCrossTenantTemplates()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(connectionString, async database =>
        {
            await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
            var platformTenant = new CurrentTenantService();
            platformTenant.SetPlatformScope();
            await using var platform = new AppDbContext(Options(database), platformTenant);

            var tenantA = NewTenant("cross-a");
            var tenantB = NewTenant("cross-b");
            var user = NewUser("cross-owner");
            platform.Tenants.AddRange(tenantA, tenantB);
            platform.Users.Add(user);
            await platform.SaveChangesAsync();

            var foreignTemplateId = Guid.NewGuid();
            await InsertTemplateAsync(
                platform,
                tenantB.Id,
                foreignTemplateId,
                "Foreign Template",
                reviewEnforcementEnabled: true,
                ("Ready", TaskStageCategory.Todo, 1000L, null, true, false),
                ("Done", TaskStageCategory.Done, 2000L, null, false, true));

            var currentTenantA = new CurrentTenantService();
            currentTenantA.SetTenant(tenantA.Id, tenantA.Slug);
            Guid workspaceId;
            await using (var seedA = new AppDbContext(Options(database), currentTenantA))
            {
                seedA.TenantUsers.Add(new TenantUser
                {
                    TenantId = tenantA.Id,
                    UserId = user.Id,
                    Role = TenantUserRole.Owner,
                    Status = TenantUserStatus.Active,
                    JoinedAt = DateTimeOffset.UtcNow
                });
                var workspace = new Workspace
                {
                    TenantId = tenantA.Id,
                    Name = "Cross Tenant Workspace",
                    Slug = $"cross-tenant-{Guid.NewGuid():N}",
                    CreatedByUserId = user.Id,
                    Status = WorkspaceStatus.Active
                };
                seedA.Workspaces.Add(workspace);
                await seedA.SaveChangesAsync();
                workspaceId = workspace.Id;
            }

            await using (var workspaceAttempt = new AppDbContext(Options(database), currentTenantA))
            {
                await Assert.ThrowsAsync<PostgresException>(async () =>
                    await workspaceAttempt.Database.ExecuteSqlInterpolatedAsync(
                        $"""
                        INSERT INTO "workspace_task_workflow_defaults" ("TenantId", "WorkspaceId", "TemplateId", "VersionNo")
                        VALUES ({tenantA.Id}, {workspaceId}, {foreignTemplateId}, {1L})
                        """));
            }

            await using (var tenantAttempt = new AppDbContext(Options(database), currentTenantA))
            {
                await Assert.ThrowsAsync<PostgresException>(async () =>
                    await tenantAttempt.Database.ExecuteSqlInterpolatedAsync(
                        $"""
                        INSERT INTO "tenant_task_workflow_defaults" ("TenantId", "TemplateId", "VersionNo")
                        VALUES ({tenantA.Id}, {foreignTemplateId}, {1L})
                        """));
            }
        });
    }

    private static async Task<ConfiguredGraph> SeedConfiguredGraphAsync(string connectionString, string suffix)
    {
        var currentTenant = new CurrentTenantService();
        currentTenant.SetPlatformScope();
        await using var db = new AppDbContext(Options(connectionString), currentTenant);

        var tenant = NewTenant(suffix);
        var owner = NewUser($"{suffix}-owner");
        db.Tenants.Add(tenant);
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        currentTenant.SetTenant(tenant.Id, tenant.Slug);
        db.TenantUsers.Add(new TenantUser
        {
            TenantId = tenant.Id,
            UserId = owner.Id,
            Role = TenantUserRole.Owner,
            Status = TenantUserStatus.Active,
            JoinedAt = DateTimeOffset.UtcNow
        });

        var workspace = new Workspace
        {
            TenantId = tenant.Id,
            Name = $"Configured Workspace {suffix}",
            Slug = $"configured-{suffix}-{Guid.NewGuid():N}",
            CreatedByUserId = owner.Id,
            Status = WorkspaceStatus.Active
        };
        var project = new Project
        {
            TenantId = tenant.Id,
            WorkspaceId = workspace.Id,
            OwnerUserId = owner.Id,
            CreatedByUserId = owner.Id,
            Name = $"Configured Project {suffix}",
            Slug = $"configured-project-{suffix}-{Guid.NewGuid():N}",
            Status = ProjectStatus.Planning,
            Visibility = ProjectVisibility.MembersOnly,
            ActivationState = ProjectActivationState.NeverActivated,
            VersionNo = 1
        };
        db.Workspaces.Add(workspace);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var workspaceTemplateId = Guid.NewGuid();
        var tenantTemplateId = Guid.NewGuid();
        await InsertTemplateAsync(
            db,
            tenant.Id,
            workspaceTemplateId,
            "Workspace Template",
            reviewEnforcementEnabled: false,
            ("Workspace Todo", TaskStageCategory.Todo, 1000L, 3, true, false),
            ("Workspace Done", TaskStageCategory.Done, 2000L, null, false, true));
        await InsertTemplateAsync(
            db,
            tenant.Id,
            tenantTemplateId,
            "Tenant Template",
            reviewEnforcementEnabled: true,
            ("Tenant Ready", TaskStageCategory.Backlog, 1000L, null, true, false),
            ("Tenant Complete", TaskStageCategory.Done, 2000L, null, false, true));

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "workspace_task_workflow_defaults" ("TenantId", "WorkspaceId", "TemplateId", "VersionNo")
            VALUES ({tenant.Id}, {workspace.Id}, {workspaceTemplateId}, {1L})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "tenant_task_workflow_defaults" ("TenantId", "TemplateId", "VersionNo")
            VALUES ({tenant.Id}, {tenantTemplateId}, {1L})
            """);

        return new ConfiguredGraph(
            tenant.Id,
            tenant.Slug,
            workspace.Id,
            project.Id,
            workspaceTemplateId,
            tenantTemplateId);
    }

    private static async Task InsertTemplateAsync(
        AppDbContext db,
        Guid tenantId,
        Guid templateId,
        string name,
        bool reviewEnforcementEnabled,
        params (string Name, TaskStageCategory Category, long SortKey, int? Wip, bool Initial, bool Terminal)[] stages)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "task_workflow_templates" ("Id", "TenantId", "Name", "ReviewEnforcementEnabled", "VersionNo")
            VALUES ({templateId}, {tenantId}, {name}, {reviewEnforcementEnabled}, {1L})
            """);

        foreach (var stage in stages)
        {
            var stageId = Guid.NewGuid();
            var category = stage.Category.ToString();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "task_workflow_template_stages"
                    ("Id", "TenantId", "TemplateId", "Name", "InternalCategory", "SortKey", "WipWarningLimit", "IsInitialStage", "IsTerminalStage", "VersionNo")
                VALUES
                    ({stageId}, {tenantId}, {templateId}, {stage.Name}, {category}, {stage.SortKey}, {stage.Wip}, {stage.Initial}, {stage.Terminal}, {1L})
                """);
        }
    }

    private static Tenant NewTenant(string suffix) => new()
    {
        Name = $"WPC02D Tenant {suffix} {Guid.NewGuid():N}",
        DisplayName = $"WPC02D Tenant {suffix}",
        Slug = $"wpc02d-template-{suffix}-{Guid.NewGuid():N}",
        Status = TenantStatus.Active
    };

    private static User NewUser(string suffix)
    {
        var email = $"{suffix}-{Guid.NewGuid():N}@example.test".ToLowerInvariant();
        return new User
        {
            DisplayName = $"WPC02D {suffix}",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Status = UserStatus.Active,
            SystemRole = SystemRole.NormalUser
        };
    }

    private static AppDbContext CreateTenantContext(
        string connectionString,
        Guid tenantId,
        string tenantSlug)
    {
        var currentTenant = new CurrentTenantService();
        currentTenant.SetTenant(tenantId, tenantSlug);
        return new AppDbContext(Options(connectionString), currentTenant);
    }

    private static DbContextOptions<AppDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(new ProjectGovernanceSaveChangesInterceptor())
            .Options;

    private sealed record ConfiguredGraph(
        Guid TenantId,
        string TenantSlug,
        Guid WorkspaceId,
        Guid ProjectId,
        Guid WorkspaceTemplateId,
        Guid TenantTemplateId);
}
