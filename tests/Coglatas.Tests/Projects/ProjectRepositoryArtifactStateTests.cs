using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Projects;

public sealed class ProjectRepositoryArtifactStateTests
{
    [Fact]
    public async Task ArtifactProjectionReturnsDistinctLiveTaskLinksForRequestedProjectOnly()
    {
        var tenantScope = new CurrentTenantService();
        tenantScope.SetPlatformScope();
        var tenant = new Tenant
        {
            Name = "Task state tenant",
            DisplayName = "Task state tenant",
            Slug = $"task-state-{Guid.NewGuid():N}"
        };
        await using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options,
            tenantScope);
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        tenantScope.SetTenant(tenant.Id, tenant.Slug);

        var projectId = Guid.NewGuid();
        var otherProjectId = Guid.NewGuid();
        var liveTaskId = Guid.NewGuid();
        var deletedTaskId = Guid.NewGuid();
        var deleted = Artifact(projectId, deletedTaskId, "Deleted");
        deleted.MarkDeleted(DateTimeOffset.UtcNow);
        context.Artifacts.AddRange(
            Artifact(projectId, liveTaskId, "Live one"),
            Artifact(projectId, liveTaskId, "Live duplicate"),
            deleted,
            Artifact(projectId, null, "Unlinked"),
            Artifact(otherProjectId, Guid.NewGuid(), "Other project"));
        await context.SaveChangesAsync();

        var result = await new ProjectRepository(context)
            .ListTaskIdsWithArtifactsAsync(projectId);

        Assert.Equal([liveTaskId], result);
    }

    private static Artifact Artifact(Guid projectId, Guid? taskItemId, string name) => new()
    {
        ProjectId = projectId,
        TaskItemId = taskItemId,
        Name = name,
        CreatedByUserId = Guid.NewGuid()
    };
}
