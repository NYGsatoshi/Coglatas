using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed class TaskStateListArtifactPostgreSqlTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task ArtifactTaskIdProjectionTranslatesAndRemainsProjectScoped()
    {
        var connectionString = PostgreSqlTestEnvironment.RequireConnectionString();
        var tenantScope = new CurrentTenantService();
        await using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connectionString)
                .Options,
            tenantScope);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());

        var suffix = Guid.NewGuid().ToString("N");
        var tenant = new Tenant
        {
            Name = $"Task state tenant {suffix}",
            DisplayName = "Task state tenant",
            Slug = $"task-state-{suffix}"
        };
        var user = new User
        {
            DisplayName = "Task state user",
            Email = $"task-state-{suffix}@example.test",
            NormalizedEmail = $"TASK-STATE-{suffix}@EXAMPLE.TEST",
            PasswordHash = "hash"
        };
        tenantScope.SetPlatformScope();
        context.AddRange(tenant, user);
        await context.SaveChangesAsync();

        tenantScope.SetTenant(tenant.Id, tenant.Slug);
        var workspace = new Workspace
        {
            Name = "Task state workspace",
            Slug = $"task-state-workspace-{suffix}",
            CreatedByUserId = user.Id
        };
        context.Workspaces.Add(workspace);
        await context.SaveChangesAsync();
        var project = Project(workspace.Id, user.Id, $"task-state-project-{suffix}");
        var otherProject = Project(workspace.Id, user.Id, $"task-state-other-{suffix}");
        context.Projects.AddRange(project, otherProject);
        await context.SaveChangesAsync();
        var liveTask = Task(project, user.Id, "Live artifact task");
        var deletedArtifactTask = Task(project, user.Id, "Deleted artifact task");
        context.TaskItems.AddRange(liveTask, deletedArtifactTask);
        await context.SaveChangesAsync();

        var deleted = Artifact(project.Id, deletedArtifactTask.Id, user.Id, "Deleted artifact");
        deleted.MarkDeleted(DateTimeOffset.UtcNow);
        context.Artifacts.AddRange(
            Artifact(project.Id, liveTask.Id, user.Id, "Live artifact one"),
            Artifact(project.Id, liveTask.Id, user.Id, "Live artifact duplicate"),
            deleted,
            Artifact(project.Id, null, user.Id, "Unlinked artifact"),
            Artifact(otherProject.Id, liveTask.Id, user.Id, "Other project artifact"));
        await context.SaveChangesAsync();

        var result = await new ProjectRepository(context)
            .ListTaskIdsWithArtifactsAsync(project.Id);

        Assert.Equal([liveTask.Id], result);
    }

    private static Project Project(Guid workspaceId, Guid userId, string slug) => new()
    {
        WorkspaceId = workspaceId,
        OwnerUserId = userId,
        CreatedByUserId = userId,
        Name = slug,
        Slug = slug,
        Status = ProjectStatus.Active
    };

    private static TaskItem Task(Project project, Guid userId, string title) => new()
    {
        WorkspaceId = project.WorkspaceId,
        ProjectId = project.Id,
        Title = title,
        CreatedByUserId = userId
    };

    private static Artifact Artifact(Guid projectId, Guid? taskItemId, Guid userId, string name) => new()
    {
        ProjectId = projectId,
        TaskItemId = taskItemId,
        Name = name,
        CreatedByUserId = userId
    };
}
