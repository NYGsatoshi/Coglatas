using Coglatas.Application.Artifacts;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Groups;
using Coglatas.Application.Projects;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Artifacts;

// Synthetic InMemory histories; these do not establish PostgreSQL concurrency or activation.
public sealed class WorkspaceArtifactFinalProjectionTests
{
    [Theory]
    [InlineData("visibility", "unchanged")]
    [InlineData("visibility", "revoked")]
    [InlineData("visibility", "restored")]
    [InlineData("membership", "unchanged")]
    [InlineData("membership", "revoked")]
    [InlineData("membership", "restored")]
    public async Task CommittedProjectChangeDuringFinalWorkspaceAuthorizationInvalidatesRetainedMetadata(string kind, string history)
    {
        var fixture = new WorkspaceArtifactReviewFixture();
        var tenantId = fixture.Tenant.TenantId;
        var actorId = fixture.User.UserId!.Value;
        fixture.Tenant.SetPlatformScope();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("artifact-final-projection-" + Guid.NewGuid().ToString("N")).Options;
        await using var reader = new AppDbContext(options, fixture.Tenant);
        var workspace = new Workspace { TenantId = tenantId, Name = "Synthetic boundary", Slug = "synthetic-boundary", CreatedByUserId = actorId };
        reader.AddRange(new Tenant { Id = tenantId, Name = "Synthetic", DisplayName = "Synthetic", Slug = "synthetic" },
            new User { Id = actorId, Email = "final-projection@example.test", NormalizedEmail = "FINAL-PROJECTION@EXAMPLE.TEST", DisplayName = "Synthetic reader" },
            workspace, new WorkspaceMember { TenantId = tenantId, WorkspaceId = workspace.Id, UserId = actorId, Status = MembershipStatus.Active });
        await reader.SaveChangesAsync();
        fixture.Tenant.SetTenant(tenantId, "synthetic");
        var project = new Project { TenantId = tenantId, WorkspaceId = workspace.Id, Name = "Synthetic project", Slug = "synthetic-project",
            OwnerUserId = actorId, CreatedByUserId = actorId, Status = ProjectStatus.Active, ActivationState = ProjectActivationState.Activated,
            Visibility = kind == "visibility" ? ProjectVisibility.WorkspaceVisible : ProjectVisibility.Restricted };
        reader.Projects.Add(project);
        if (kind == "membership") reader.ProjectMembers.Add(new ProjectMember { TenantId = tenantId, ProjectId = project.Id, UserId = actorId, Role = ProjectRole.Viewer });
        var artifact = new Artifact { TenantId = tenantId, ProjectId = project.Id, Name = "FINAL-PROJECTION-PRIVATE-SENTINEL", CreatedByUserId = actorId };
        reader.Artifacts.Add(artifact);
        await reader.SaveChangesAsync();
        var workspaceRepository = new WorkspaceRepository(reader);
        var workspaceAuthority = new WorkspaceAuthorizationService(new UserRepository(reader), workspaceRepository);
        var authorization = new CommitOnFinalAuthorization(workspaceAuthority, options, fixture.Tenant, project.Id, actorId, tenantId, kind, history);
        var repository = new CountingRepository(new WorkspaceArtifactRepository(reader));
        var service = new WorkspaceArtifactService(repository, fixture.Codec, authorization, fixture.Tenant, fixture.User, fixture.Clock);

        var result = await service.ListAsync(workspace.Id, new());

        Assert.True(authorization.CommitCompleted);
        Assert.Equal(2, authorization.Calls);
        Assert.Equal(2, repository.Calls); // one initial projection and one bounded final projection
        await using var fresh = new AppDbContext(options, fixture.Tenant);
        var freshWorkspaceRepository = new WorkspaceRepository(fresh);
        var freshWorkspace = new WorkspaceAuthorizationService(new UserRepository(fresh), freshWorkspaceRepository);
        var groups = new GroupRepository(fresh);
        var freshProject = new ProjectAuthorizationService(new ProjectRepository(fresh), freshWorkspace,
            new GroupAuthorizationService(groups, freshWorkspaceRepository, freshWorkspace), groups);
        Assert.True(await freshWorkspace.CanViewWorkspace(actorId, workspace.Id));
        Assert.Equal(history != "revoked", await freshProject.CanViewProject(actorId, project.Id));
        var current = await new WorkspaceArtifactRepository(fresh).ReadAsync(new(tenantId, actorId, workspace.Id, "", 20, null));
        Assert.Equal(history == "revoked" ? 0 : 1, current.Count);
        if (history == "revoked")
        {
            Assert.False(result.IsSuccess);
            Assert.Null(result.Value);
            Assert.Equal("WorkspaceNotFound", result.ErrorDetail!.Code);
        }
        else
        {
            Assert.True(result.IsSuccess);
            Assert.Equal(artifact.Id, Assert.Single(result.Value!.Items).Id);
        }
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("tenant")]
    [InlineData("anonymous")]
    [InlineData("platform")]
    public async Task ContextChangeDuringFinalProjectionCannotReturnRowsOrCursor(string change)
    {
        var fixture = new WorkspaceArtifactReviewFixture();
        fixture.Repository.Rows = [fixture.Item(1), fixture.Item(2)];
        fixture.Repository.DuringRead = () =>
        {
            if (fixture.Repository.Calls != 2) return;
            if (change == "actor") fixture.User.UserId = Guid.NewGuid();
            if (change == "tenant") fixture.Tenant.SetTenant(Guid.NewGuid(), "changed-synthetic");
            if (change == "anonymous") fixture.User.IsAuthenticated = false;
            if (change == "platform") fixture.Tenant.SetPlatformScope();
        };
        var result = await fixture.ListAsync(new(PageSize: 1));
        Assert.Equal(2, fixture.Repository.Calls);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal("WorkspaceNotFound", result.ErrorDetail!.Code);
    }

    private sealed class CountingRepository(IWorkspaceArtifactRepository inner) : IWorkspaceArtifactRepository
    {
        public int Calls { get; private set; }
        public Task<IReadOnlyList<ArtifactListItemResponse>> ReadAsync(WorkspaceArtifactReadQuery query, CancellationToken cancellationToken = default)
        {
            Calls++;
            return inner.ReadAsync(query, cancellationToken);
        }
    }

    private sealed class CommitOnFinalAuthorization(IWorkspaceAuthorizationService inner, DbContextOptions<AppDbContext> options,
        ICurrentTenant tenant, Guid projectId, Guid actorId, Guid tenantId, string kind, string history) : IWorkspaceAuthorizationService
    {
        public int Calls { get; private set; }
        public bool CommitCompleted { get; private set; }
        public async Task<bool> CanViewWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Calls == 2)
            {
                await Task.Yield();
                if (history != "unchanged")
                {
                    await using var writer = new AppDbContext(options, tenant);
                    if (kind == "visibility")
                    {
                        var project = await writer.Projects.SingleAsync(p => p.Id == projectId, cancellationToken);
                        project.Visibility = ProjectVisibility.Restricted;
                        await writer.SaveChangesAsync(cancellationToken);
                        if (history == "restored") { project.Visibility = ProjectVisibility.WorkspaceVisible; await writer.SaveChangesAsync(cancellationToken); }
                    }
                    else
                    {
                        var member = await writer.ProjectMembers.SingleAsync(m => m.ProjectId == projectId && m.UserId == actorId, cancellationToken);
                        writer.ProjectMembers.Remove(member);
                        await writer.SaveChangesAsync(cancellationToken);
                        if (history == "restored")
                        {
                            writer.ProjectMembers.Add(new ProjectMember { TenantId = tenantId, ProjectId = projectId, UserId = actorId, Role = ProjectRole.Viewer });
                            await writer.SaveChangesAsync(cancellationToken);
                        }
                    }
                }
                CommitCompleted = true;
            }
            return await inner.CanViewWorkspace(userId, workspaceId, cancellationToken);
        }
        public Task<bool> CanContributeWorkspace(Guid u, Guid w, CancellationToken c = default) => inner.CanContributeWorkspace(u, w, c);
        public Task<bool> CanManageWorkspace(Guid u, Guid w, CancellationToken c = default) => inner.CanManageWorkspace(u, w, c);
        public Task<bool> CanCreateWorkspace(Guid u, Guid t, CancellationToken c = default) => inner.CanCreateWorkspace(u, t, c);
    }
}
