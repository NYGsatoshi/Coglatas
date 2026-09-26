using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed class TaskActivityPostgreSqlTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "Issue369")]
    public async Task TaskActivityProjectionTranslatesWithStableBoundedProjectTaskPaging()
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
        var tenant = new Tenant { Name = $"Task activity tenant {suffix}", DisplayName = "Task activity tenant", Slug = $"task-activity-{suffix}" };
        var author = new User
        {
            DisplayName = "Task activity author",
            Email = $"task-activity-{suffix}@example.test",
            NormalizedEmail = $"TASK-ACTIVITY-{suffix}@EXAMPLE.TEST",
            PasswordHash = "hash"
        };
        tenantScope.SetPlatformScope();
        context.AddRange(tenant, author);
        await context.SaveChangesAsync();

        tenantScope.SetTenant(tenant.Id, tenant.Slug);
        var workspace = new Workspace { Name = "Task activity workspace", Slug = $"task-activity-workspace-{suffix}", CreatedByUserId = author.Id };
        context.Workspaces.Add(workspace);
        await context.SaveChangesAsync();
        var project = Project(workspace.Id, author.Id, $"task-activity-project-{suffix}");
        var otherProject = Project(workspace.Id, author.Id, $"task-activity-other-{suffix}");
        context.Projects.AddRange(project, otherProject);
        await context.SaveChangesAsync();
        var task = Task(project, author.Id, "Activity task");
        var otherTask = Task(project, author.Id, "Other task");
        context.TaskItems.AddRange(task, otherTask);
        await context.SaveChangesAsync();

        var idTail = Guid.NewGuid().ToString("D")[8..];
        var firstId = Guid.Parse($"10000001{idTail}");
        var secondId = Guid.Parse($"10000002{idTail}");
        var thirdId = Guid.Parse($"10000003{idTail}");
        var occurredAt = new DateTimeOffset(2026, 8, 24, 4, 0, 0, TimeSpan.Zero);
        context.ActivityLogs.AddRange(
            Activity(firstId, project.Id, task.Id, author.Id, occurredAt, "first"),
            Activity(secondId, project.Id, task.Id, author.Id, occurredAt, "second"),
            Activity(thirdId, project.Id, task.Id, author.Id, occurredAt, "third"),
            Activity(Guid.NewGuid(), project.Id, otherTask.Id, author.Id, occurredAt.AddMinutes(1), "other task"),
            Activity(Guid.NewGuid(), otherProject.Id, task.Id, author.Id, occurredAt.AddMinutes(2), "mismatched project"));
        await context.SaveChangesAsync();

        var repository = new ProjectRepository(context);
        var pageOne = await repository.ListTaskActivityLogsPageAsync(project.Id, task.Id, 1, 2);
        var pageTwo = await repository.ListTaskActivityLogsPageAsync(project.Id, task.Id, 2, 2);

        Assert.Equal(3, pageOne.TotalCount);
        Assert.Equal([thirdId, secondId], pageOne.Items.Select(item => item.Id));
        Assert.Equal([firstId], pageTwo.Items.Select(item => item.Id));
        Assert.All(pageOne.Items.Concat(pageTwo.Items), item => Assert.Equal("Task activity author", item.AuthorDisplayName));
        Assert.DoesNotContain(pageOne.Items.Concat(pageTwo.Items), item => item.Body is "other task" or "mismatched project");
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

    private static ActivityLog Activity(Guid id, Guid projectId, Guid taskItemId, Guid authorUserId, DateTimeOffset occurredAt, string body) => new()
    {
        Id = id,
        ProjectId = projectId,
        TaskItemId = taskItemId,
        AuthorUserId = authorUserId,
        ActivityType = ActivityLogType.Note,
        Body = body,
        OccurredAt = occurredAt
    };
}
