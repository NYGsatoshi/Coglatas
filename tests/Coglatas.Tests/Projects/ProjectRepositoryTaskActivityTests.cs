using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Projects;

public sealed class ProjectRepositoryTaskActivityTests
{
    [Fact]
    [Trait("Scope", "Issue369")]
    public async Task TaskActivityUsesStableOccurredAtAndIdPagingWithinExactProjectTaskScope()
    {
        var tenantScope = new CurrentTenantService();
        tenantScope.SetPlatformScope();
        var tenant = new Tenant
        {
            Name = "Task activity tenant",
            DisplayName = "Task activity tenant",
            Slug = $"task-activity-{Guid.NewGuid():N}"
        };
        var author = new User
        {
            DisplayName = "Activity author",
            Email = $"activity-{Guid.NewGuid():N}@example.test",
            NormalizedEmail = $"ACTIVITY-{Guid.NewGuid():N}@EXAMPLE.TEST"
        };
        await using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options,
            tenantScope);
        context.Tenants.Add(tenant);
        context.Users.Add(author);
        await context.SaveChangesAsync();
        tenantScope.SetTenant(tenant.Id, tenant.Slug);

        var workspace = new Workspace
        {
            TenantId = tenant.Id,
            Name = "Task activity workspace",
            Slug = $"task-activity-workspace-{Guid.NewGuid():N}",
            CreatedByUserId = author.Id
        };
        var project = Project(tenant.Id, workspace.Id, author.Id, "activity-project");
        var otherProject = Project(tenant.Id, workspace.Id, author.Id, "other-project");
        var task = Task(tenant.Id, workspace.Id, project.Id, author.Id, "Target task");
        var otherTask = Task(tenant.Id, workspace.Id, project.Id, author.Id, "Other task");
        context.Workspaces.Add(workspace);
        context.Projects.AddRange(project, otherProject);
        context.TaskItems.AddRange(task, otherTask);

        var occurredAt = new DateTimeOffset(2026, 8, 24, 2, 0, 0, TimeSpan.Zero);
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var thirdId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        context.ActivityLogs.AddRange(
            Activity(firstId, tenant.Id, project.Id, task.Id, author, occurredAt, "first"),
            Activity(secondId, tenant.Id, project.Id, task.Id, author, occurredAt, "second"),
            Activity(thirdId, tenant.Id, project.Id, task.Id, author, occurredAt, "third"),
            Activity(Guid.NewGuid(), tenant.Id, project.Id, otherTask.Id, author, occurredAt.AddMinutes(1), "other task"),
            Activity(Guid.NewGuid(), tenant.Id, otherProject.Id, task.Id, author, occurredAt.AddMinutes(2), "mismatched project"));
        await context.SaveChangesAsync();

        var repository = new ProjectRepository(context);
        var pageOne = await repository.ListTaskActivityLogsPageAsync(project.Id, task.Id, 1, 2);
        var pageTwo = await repository.ListTaskActivityLogsPageAsync(project.Id, task.Id, 2, 2);

        Assert.Equal(3, pageOne.TotalCount);
        Assert.Equal([thirdId, secondId], pageOne.Items.Select(item => item.Id));
        Assert.Equal([firstId], pageTwo.Items.Select(item => item.Id));
        Assert.All(pageOne.Items.Concat(pageTwo.Items), item => Assert.Equal("Activity author", item.AuthorDisplayName));
        Assert.DoesNotContain(pageOne.Items.Concat(pageTwo.Items), item => item.Body is "other task" or "mismatched project");
    }

    [Fact]
    [Trait("Scope", "Issue369")]
    public async Task TaskActivityReturnsNoRowsForAProjectTaskMismatch()
    {
        var tenantScope = new CurrentTenantService();
        tenantScope.SetPlatformScope();
        var tenant = new Tenant { Name = "Mismatch tenant", DisplayName = "Mismatch tenant", Slug = $"mismatch-{Guid.NewGuid():N}" };
        await using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options,
            tenantScope);
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        tenantScope.SetTenant(tenant.Id, tenant.Slug);

        var repository = new ProjectRepository(context);
        var result = await repository.ListTaskActivityLogsPageAsync(Guid.NewGuid(), Guid.NewGuid(), 1, 20);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    private static Project Project(Guid tenantId, Guid workspaceId, Guid ownerUserId, string slug) => new()
    {
        TenantId = tenantId,
        WorkspaceId = workspaceId,
        OwnerUserId = ownerUserId,
        CreatedByUserId = ownerUserId,
        Name = slug,
        Slug = $"{slug}-{Guid.NewGuid():N}"
    };

    private static TaskItem Task(Guid tenantId, Guid workspaceId, Guid projectId, Guid createdByUserId, string title) => new()
    {
        TenantId = tenantId,
        WorkspaceId = workspaceId,
        ProjectId = projectId,
        CreatedByUserId = createdByUserId,
        Title = title
    };

    private static ActivityLog Activity(Guid id, Guid tenantId, Guid projectId, Guid taskItemId, User author, DateTimeOffset occurredAt, string body) => new()
    {
        Id = id,
        TenantId = tenantId,
        ProjectId = projectId,
        TaskItemId = taskItemId,
        AuthorUserId = author.Id,
        AuthorUser = author,
        ActivityType = ActivityLogType.Note,
        Body = body,
        OccurredAt = occurredAt
    };
}
