using Coglatas.Application.Artifacts;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Groups;
using Coglatas.Application.Projects;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Artifacts;

// InMemory controls are logical query checks, not PostgreSQL execution evidence.
public sealed class WorkspaceArtifactQueryTests
{
    [Fact]
    public async Task BoundedProjectionMatchesCurrentProjectAuthorizationAndExcludesArchivedHistory()
    {
        await using var graph = await Graph.CreateAsync();
        var visible = await graph.AddProjectAsync(ProjectVisibility.WorkspaceVisible, ProjectStatus.Active, true, false);
        var restricted = await graph.AddProjectAsync(ProjectVisibility.Restricted, ProjectStatus.Active, true, false);
        var explicitMember = await graph.AddProjectAsync(ProjectVisibility.MembersOnly, ProjectStatus.Planning, false, true);
        var inactive = await graph.AddProjectAsync(ProjectVisibility.WorkspaceVisible, ProjectStatus.Planning, false, false);
        var archived = await graph.AddProjectAsync(ProjectVisibility.Restricted, ProjectStatus.Archived, true, true);
        var deleted = await graph.AddProjectAsync(ProjectVisibility.WorkspaceVisible, ProjectStatus.Active, true, true);
        deleted.MarkDeleted(DateTimeOffset.UtcNow);
        await graph.Db.SaveChangesAsync();
        graph.Db.ChangeTracker.Clear();

        var rows = await new WorkspaceArtifactRepository(graph.Db).ReadAsync(graph.Request());
        Assert.Equal(new[] { visible.Id, explicitMember.Id }.Order(), rows.Select(row => row.ProjectId).Order());
        foreach (var project in new[] { visible, restricted, explicitMember, inactive, archived, deleted })
        {
            Assert.Equal(await graph.Authorization.CanViewProject(graph.Actor.Id, project.Id), rows.Any(row => row.ProjectId == project.Id));
        }
        Assert.True(await graph.Db.ListableProjectsFor(graph.Actor.Id).AnyAsync(project => project.Id == archived.Id));
        Assert.DoesNotContain(rows, row => row.ProjectId == archived.Id);
    }

    [Fact]
    public async Task WorkspaceTenantAndRevocationAreAppliedBeforePageLimit()
    {
        await using var graph = await Graph.CreateAsync();
        var allowed = await graph.AddProjectAsync(ProjectVisibility.WorkspaceVisible, ProjectStatus.Active, true, false);
        var hidden = await graph.AddProjectAsync(ProjectVisibility.Restricted, ProjectStatus.Active, true, false);
        var tenantId = graph.TenantId;
        var actorId = graph.Actor.Id;
        var hiddenProjectId = hidden.Id;
        var extraHidden = Enumerable.Range(1, 120).Select(index => new Artifact
        {
            TenantId = tenantId, ProjectId = hiddenProjectId, Name = $"Hidden synthetic {index}", CreatedByUserId = actorId,
            UpdatedAt = DateTimeOffset.UtcNow.AddYears(10)
        });
        graph.Db.Artifacts.AddRange(extraHidden);
        await graph.Db.SaveChangesAsync();
        var first = Assert.Single(await new WorkspaceArtifactRepository(graph.Db).ReadAsync(graph.Request() with { PageSize = 1 }));
        Assert.Equal(allowed.Id, first.ProjectId);
        Assert.Empty(await new WorkspaceArtifactRepository(graph.Db).ReadAsync(graph.Request() with { TenantId = Guid.NewGuid() }));
        Assert.Empty(await new WorkspaceArtifactRepository(graph.Db).ReadAsync(graph.Request() with { WorkspaceId = Guid.NewGuid() }));
        graph.Db.WorkspaceMembers.RemoveRange(graph.Db.WorkspaceMembers);
        await graph.Db.SaveChangesAsync();
        Assert.Empty(await new WorkspaceArtifactRepository(graph.Db).ReadAsync(graph.Request()));
    }

    [Fact]
    public async Task KeysetUsesEffectiveUpdatedAtThenDescendingIdentityAndFetchesOneExtra()
    {
        await using var graph = await Graph.CreateAsync();
        var project = await graph.AddProjectAsync(ProjectVisibility.WorkspaceVisible, ProjectStatus.Active, true, false);
        var baseline = await graph.Db.Artifacts.SingleAsync();
        baseline.MarkDeleted(DateTimeOffset.UtcNow);
        var future = new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
        graph.Db.Artifacts.AddRange(Enumerable.Range(1, 105).Select(index => new Artifact
        {
            Id = Guid.Parse($"00000000-0000-0000-0000-{index:x12}"),
            TenantId = graph.TenantId, ProjectId = project.Id, Name = $"Synthetic tie {index}",
            CreatedByUserId = graph.Actor.Id, UpdatedAt = future
        }));
        graph.Db.Artifacts.Add(new Artifact
        {
            TenantId = graph.TenantId, ProjectId = project.Id, Name = "CreatedAt fallback", CreatedByUserId = graph.Actor.Id
        });
        await graph.Db.SaveChangesAsync();
        var repository = new WorkspaceArtifactRepository(graph.Db);
        var first = await repository.ReadAsync(graph.Request() with { PageSize = 500 });
        Assert.Equal(101, first.Count);
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000069"), first[0].Id);
        var lastVisible = first[99];
        var second = await repository.ReadAsync(graph.Request() with
        { After = new(lastVisible.UpdatedAt!.Value, lastVisible.Id) });
        Assert.Equal(6, second.Count);
        Assert.DoesNotContain(second, row => first.Take(100).Any(previous => previous.Id == row.Id));
        Assert.Equal("CreatedAt fallback", second[^1].Title);
    }

    [Fact]
    public void NpgsqlTranslatesAuthorizationEscapedSearchKeysetAndBoundedLimitWithoutConnecting()
    {
        var tenant = new CurrentTenantService();
        tenant.SetTenant(Guid.NewGuid(), "translation-only");
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only").Options, tenant);
        var query = new WorkspaceArtifactReadQuery(tenant.TenantId, Guid.NewGuid(), Guid.NewGuid(), @"literal%_\", 20,
            new(new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero), Guid.NewGuid()));
        var sql = WorkspaceArtifactReadScope.Query(db, query).ToQueryString();
        Assert.Contains("ILIKE", sql, StringComparison.Ordinal);
        Assert.Contains("ESCAPE", sql, StringComparison.Ordinal);
        Assert.Contains("COALESCE", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT", sql, StringComparison.Ordinal);
        Assert.Contains("workspace_members", sql, StringComparison.Ordinal);
        Assert.Contains("project_members", sql, StringComparison.Ordinal);
        Assert.Contains(@"literal\%\_\\", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("COUNT(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OFFSET", sql, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Graph : IAsyncDisposable
    {
        internal Guid TenantId { get; } = Guid.NewGuid();
        internal User Actor { get; } = new() { Email = "reader@example.test", NormalizedEmail = "READER@EXAMPLE.TEST", DisplayName = "Synthetic reader" };
        internal Workspace Workspace { get; }
        internal AppDbContext Db { get; }
        internal ProjectAuthorizationService Authorization { get; }
        private readonly CurrentTenantService _tenant = new();

        private Graph()
        {
            _tenant.SetPlatformScope();
            Db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options, _tenant);
            Workspace = new() { TenantId = TenantId, Name = "Synthetic artifacts", Slug = "artifact-review", CreatedByUserId = Actor.Id };
            var workspaceRepository = new WorkspaceRepository(Db);
            var workspaceAuthorization = new WorkspaceAuthorizationService(new UserRepository(Db), workspaceRepository);
            var groups = new GroupRepository(Db);
            Authorization = new(new ProjectRepository(Db), workspaceAuthorization,
                new GroupAuthorizationService(groups, workspaceRepository, workspaceAuthorization), groups);
        }

        internal static async Task<Graph> CreateAsync()
        {
            var graph = new Graph();
            graph.Db.AddRange(new Tenant { Id = graph.TenantId, Name = "Synthetic tenant", DisplayName = "Synthetic tenant", Slug = "artifact-review" }, graph.Actor, graph.Workspace,
                new WorkspaceMember { TenantId = graph.TenantId, WorkspaceId = graph.Workspace.Id, UserId = graph.Actor.Id, Status = MembershipStatus.Active });
            await graph.Db.SaveChangesAsync();
            graph._tenant.SetTenant(graph.TenantId, "artifact-review");
            return graph;
        }

        internal async Task<Project> AddProjectAsync(ProjectVisibility visibility, ProjectStatus status, bool activated, bool member)
        {
            var project = new Project
            {
                TenantId = TenantId, WorkspaceId = Workspace.Id, Name = $"Synthetic {visibility} {status}", Slug = Guid.NewGuid().ToString("N"),
                OwnerUserId = Actor.Id, CreatedByUserId = Actor.Id, Visibility = visibility, Status = status,
                ActivationState = activated ? ProjectActivationState.Activated : ProjectActivationState.NeverActivated
            };
            Db.Projects.Add(project);
            if (member) Db.ProjectMembers.Add(new() { TenantId = TenantId, ProjectId = project.Id, UserId = Actor.Id });
            Db.Artifacts.Add(new() { TenantId = TenantId, ProjectId = project.Id, Name = project.Name, CreatedByUserId = Actor.Id });
            await Db.SaveChangesAsync();
            return project;
        }

        internal WorkspaceArtifactReadQuery Request() => new(TenantId, Actor.Id, Workspace.Id, "", 20, null);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
