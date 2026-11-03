using Coglatas.Application.Projects;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Projects;

public sealed class ProjectMembershipPersistenceTests
{
    [Fact]
    public async Task MembershipLifecyclePersistsScopedRolesAndStagesExactAffectedUserChanges()
    {
        await using var fixture = await ProjectServicePersistenceContext.CreateAsync();
        var created = await fixture.Service.AddMemberAsync(fixture.ProjectId,
            new AddProjectMemberRequest(fixture.TargetId, ProjectRole.Contributor), fixture.Token);
        Assert.True(created.IsSuccess, created.Error);
        Assert.Equal(fixture.TargetId, created.Value!.UserId);
        Assert.Equal("Target", created.Value.DisplayName);
        Assert.Equal("target@example.test", created.Value.Email);
        Assert.Equal(ProjectRole.Contributor, created.Value.Role);
        Assert.Equal(fixture.Clock.UtcNow, created.Value.JoinedAt);
        var persisted = await fixture.Db.ProjectMembers.AsNoTracking().SingleAsync(member => member.UserId == fixture.TargetId);
        Assert.Equal(fixture.TenantId, persisted.TenantId);
        Assert.Equal(fixture.ProjectId, persisted.ProjectId);
        Assert.Equal(fixture.WorkspaceId, (await fixture.Db.Projects.AsNoTracking().SingleAsync()).WorkspaceId);
        Assert.Equal(ProjectRole.Contributor, persisted.Role);
        fixture.Db.ChangeTracker.Clear();

        var updated = await fixture.Service.UpdateMemberAsync(fixture.ProjectId, fixture.TargetId,
            new UpdateProjectMemberRequest(ProjectRole.Reviewer), fixture.Token);
        Assert.True(updated.IsSuccess, updated.Error);
        Assert.Equal(ProjectRole.Reviewer, updated.Value!.Role);
        Assert.Equal(fixture.Clock.UtcNow, updated.Value.JoinedAt);
        Assert.Equal(ProjectRole.Reviewer, (await fixture.Db.ProjectMembers.AsNoTracking().SingleAsync(member => member.UserId == fixture.TargetId)).Role);
        fixture.Db.ChangeTracker.Clear();

        var removed = await fixture.Service.RemoveMemberAsync(fixture.ProjectId, fixture.TargetId, fixture.Token);
        Assert.True(removed.IsSuccess, removed.Error);
        Assert.False(await fixture.Db.ProjectMembers.AsNoTracking().AnyAsync(member => member.UserId == fixture.TargetId));
        Assert.Equal(fixture.ActorId, (await fixture.Db.ProjectMembers.AsNoTracking().SingleAsync()).UserId);
        var audits = await fixture.Db.AuditLogs.AsNoTracking().OrderBy(audit => audit.CreatedAt).ThenBy(audit => audit.Id).ToListAsync();
        Assert.Equal(new[] { "ProjectMemberAdded", "ProjectMemberRemoved", "ProjectMemberUpdated" }, audits.Select(audit => audit.Action).Order().ToArray());
        Assert.All(audits, audit =>
        {
            Assert.Equal(fixture.TenantId, audit.TenantId);
            Assert.Equal(fixture.ActorId, audit.ActorUserId);
            Assert.Equal("Project", audit.EntityType);
            Assert.Equal(fixture.ProjectId, audit.EntityId);
        });
        Assert.Equal(3, fixture.Invalidations.Calls.Count);
        Assert.All(fixture.Invalidations.Calls, call =>
        {
            Assert.Equal(fixture.ProjectId, call.ProjectId);
            Assert.Equal(fixture.TenantId, call.TenantId);
            Assert.Equal(fixture.ActorId, call.ActorId);
            Assert.Equal("memberChanged", call.Change);
            Assert.Equal(fixture.Token, call.Token);
        });
        Assert.Equal(new[] { "membershipChanged", "membershipChanged", "revoked" }, fixture.AuthorizationChanges.Calls.Select(call => call.Change));
        Assert.All(fixture.AuthorizationChanges.Calls, call =>
        {
            Assert.Equal(fixture.TenantId, call.TenantId);
            Assert.Equal(fixture.TargetId, call.UserId);
            Assert.Equal("project", call.ScopeType);
            Assert.Equal(fixture.ProjectId, call.ScopeId);
            Assert.Equal(fixture.Token, call.Token);
        });
    }

    [Fact]
    public async Task GroupAdmissionRequiresMembershipInTheExactOwningGroup()
    {
        await using var fixture = await ProjectServicePersistenceContext.CreateAsync(grouped: true);
        var before = await fixture.ReadPersistedMembershipsAsync();

        var denied = await fixture.MutateMembershipAsync("add");

        Assert.False(denied.IsSuccess);
        Assert.Equal("User must belong to the group before joining the project.", denied.Error);
        Assert.Equal(before, await fixture.ReadPersistedMembershipsAsync());
        Assert.Empty(await fixture.Db.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Empty(fixture.Invalidations.Calls);
        Assert.Empty(fixture.AuthorizationChanges.Calls);
        fixture.Db.GroupMembers.Add(new GroupMember { GroupId = fixture.GroupId!.Value, UserId = fixture.TargetId, Role = GroupRole.Member });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var accepted = await fixture.MutateMembershipAsync("add");

        Assert.True(accepted.IsSuccess, accepted.Error);
        Assert.Equal(fixture.ProjectId, (await fixture.Db.ProjectMembers.AsNoTracking().SingleAsync(member => member.UserId == fixture.TargetId)).ProjectId);
        Assert.Equal("ProjectMemberAdded", (await fixture.Db.AuditLogs.AsNoTracking().SingleAsync()).Action);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(MembershipStatus.Pending)]
    [InlineData(MembershipStatus.Suspended)]
    public async Task AddRejectsMissingOrInactiveTargetWorkspaceMembershipWithoutWrites(MembershipStatus? status)
    {
        await using var fixture = await ProjectServicePersistenceContext.CreateAsync();
        var member = await fixture.Db.WorkspaceMembers.SingleAsync(item => item.UserId == fixture.TargetId);
        if (status.HasValue) member.Status = status.Value;
        else fixture.Db.WorkspaceMembers.Remove(member);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var before = await fixture.ReadPersistedMembershipsAsync();

        var result = await fixture.MutateMembershipAsync("add");

        Assert.False(result.IsSuccess);
        Assert.Equal("User must belong to the workspace before joining the project.", result.Error);
        Assert.Equal(before, await fixture.ReadPersistedMembershipsAsync());
        Assert.Empty(await fixture.Db.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Empty(fixture.Invalidations.Calls);
        Assert.Empty(fixture.AuthorizationChanges.Calls);
    }

    [Fact]
    public async Task DuplicateMembershipCannotChangeTheExistingRoleOrEmitSuccessEffects()
    {
        await using var fixture = await ProjectServicePersistenceContext.CreateAsync();
        await fixture.AddTargetMemberAsync(ProjectRole.Viewer);
        var before = await fixture.ReadPersistedMembershipsAsync();

        var result = await fixture.MutateMembershipAsync("add");

        Assert.False(result.IsSuccess);
        Assert.Equal("User is already a project member.", result.Error);
        Assert.Equal(before, await fixture.ReadPersistedMembershipsAsync());
        Assert.Empty(await fixture.Db.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Empty(fixture.Invalidations.Calls);
        Assert.Empty(fixture.AuthorizationChanges.Calls);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("update")]
    [InlineData("remove")]
    public async Task CurrentProjectViewerCanReadMembershipButCannotMutateIt(string operation)
    {
        await using var fixture = await ProjectServicePersistenceContext.CreateAsync();
        await fixture.AddTargetMemberAsync(ProjectRole.Viewer);
        fixture.Actor.UserId = fixture.TargetId;
        var before = await fixture.ReadPersistedMembershipsAsync();
        var visible = await fixture.Service.ListMembersAsync(fixture.ProjectId, fixture.Token);
        Assert.True(visible.IsSuccess, visible.Error);
        Assert.Equal(new[] { fixture.ActorId, fixture.TargetId }, visible.Value!.Select(member => member.UserId));

        var result = await fixture.MutateMembershipAsync(operation);

        Assert.False(result.IsSuccess);
        Assert.Equal("You are not allowed to manage project members.", result.Error);
        Assert.Equal(before, await fixture.ReadPersistedMembershipsAsync());
        Assert.Empty(await fixture.Db.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Empty(fixture.Invalidations.Calls);
        Assert.Empty(fixture.AuthorizationChanges.Calls);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("update")]
    [InlineData("remove")]
    public async Task WorkspaceRevocationOverridesRetainedProjectOwnerForMembershipMutation(string operation)
    {
        await using var fixture = await ProjectServicePersistenceContext.CreateAsync();
        await fixture.AddTargetMemberAsync();
        Assert.True(await fixture.Authorization.CanManageProject(fixture.ActorId, fixture.ProjectId, fixture.Token));
        var actorMembership = await fixture.Db.WorkspaceMembers.SingleAsync(member => member.UserId == fixture.ActorId);
        actorMembership.Status = MembershipStatus.Suspended;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var before = await fixture.ReadPersistedMembershipsAsync();

        var result = await fixture.MutateMembershipAsync(operation);

        Assert.False(result.IsSuccess);
        Assert.Equal("You are not allowed to manage project members.", result.Error);
        Assert.Equal(before, await fixture.ReadPersistedMembershipsAsync());
        Assert.Equal(ProjectRole.Owner, (await fixture.Db.ProjectMembers.AsNoTracking().SingleAsync(member => member.UserId == fixture.ActorId)).Role);
        Assert.Empty(await fixture.Db.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Empty(fixture.Invalidations.Calls);
        Assert.Empty(fixture.AuthorizationChanges.Calls);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("update")]
    [InlineData("remove")]
    public async Task ForeignTenantProjectIsIndistinguishableFromUnknownProjectEvenForSharedOwner(string operation)
    {
        await using var fixture = await ProjectServicePersistenceContext.CreateAsync();
        var foreignProject = await fixture.SeedForeignProjectAsync();
        var before = await fixture.ReadPersistedMembershipsAsync();

        var foreign = await fixture.MutateMembershipAsync(operation, foreignProject);
        var unknown = await fixture.MutateMembershipAsync(operation, Guid.NewGuid());
        var members = await fixture.Service.ListMembersAsync(foreignProject, fixture.Token);

        Assert.False(foreign.IsSuccess);
        Assert.False(unknown.IsSuccess);
        Assert.Equal(unknown.Error, foreign.Error);
        Assert.False(members.IsSuccess);
        Assert.Null(members.Value);
        Assert.Equal("Project not found.", members.Error);
        Assert.Equal(before, await fixture.ReadPersistedMembershipsAsync());
        Assert.Empty(await fixture.Db.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Empty(fixture.Invalidations.Calls);
        Assert.Empty(fixture.AuthorizationChanges.Calls);
    }

    [Theory]
    [InlineData("update")]
    [InlineData("remove")]
    public async Task UnknownMembershipDoesNotProduceMutationEffects(string operation)
    {
        await using var fixture = await ProjectServicePersistenceContext.CreateAsync();
        var before = await fixture.ReadPersistedMembershipsAsync();

        var result = await fixture.MutateMembershipAsync(operation);

        Assert.False(result.IsSuccess);
        Assert.Equal("Project member not found.", result.Error);
        Assert.Equal(before, await fixture.ReadPersistedMembershipsAsync());
        Assert.Empty(await fixture.Db.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Empty(fixture.Invalidations.Calls);
        Assert.Empty(fixture.AuthorizationChanges.Calls);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("update")]
    [InlineData("remove")]
    public async Task ConcurrencyFailureDiscardsPendingMembershipAndAuditThenAllowsFreshRetry(string operation)
    {
        await using var fixture = await ProjectServicePersistenceContext.CreateAsync();
        if (operation != "add") await fixture.AddTargetMemberAsync();
        var before = await fixture.ReadPersistedMembershipsAsync();
        fixture.Fault.FailNextSave = true;

        var failed = await fixture.MutateMembershipAsync(operation);

        Assert.False(failed.IsSuccess);
        Assert.Equal("PROJECT_CONFLICT", failed.ErrorDetail!.Code);
        Assert.Empty(fixture.Db.ChangeTracker.Entries());
        Assert.Equal(before, await fixture.ReadPersistedMembershipsAsync());
        Assert.Empty(await fixture.Db.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Equal(fixture.Token, Assert.Single(fixture.Fault.FailedTokens));
        // Publishers record staging attempts; they do not simulate dispatch.
        Assert.Single(fixture.Invalidations.Calls);
        Assert.Single(fixture.AuthorizationChanges.Calls);

        var retry = await fixture.MutateMembershipAsync(operation);

        Assert.True(retry.IsSuccess, retry.Error);
        Assert.NotEqual(before, await fixture.ReadPersistedMembershipsAsync());
        Assert.Single(await fixture.Db.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Equal(2, fixture.Invalidations.Calls.Count);
        Assert.Equal(2, fixture.AuthorizationChanges.Calls.Count);
    }

    [Fact]
    public async Task CancellationStopsRealRepositoryLookupBeforeMutationAndAudit()
    {
        await using var fixture = await ProjectServicePersistenceContext.CreateAsync();
        var before = await fixture.ReadPersistedMembershipsAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.AddMemberAsync(
            fixture.ProjectId, new AddProjectMemberRequest(fixture.TargetId, ProjectRole.Contributor), cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(before, await fixture.ReadPersistedMembershipsAsync());
        Assert.Empty(await fixture.Db.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Empty(fixture.Invalidations.Calls);
        Assert.Empty(fixture.AuthorizationChanges.Calls);
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("missing-id")]
    [InlineData("empty-id")]
    public async Task InvalidActorCannotReadOrMutateMemberships(string context)
    {
        await using var fixture = await ProjectServicePersistenceContext.CreateAsync();
        await fixture.AddTargetMemberAsync();
        var before = await fixture.ReadPersistedMembershipsAsync();
        if (context == "anonymous") fixture.Actor.IsAuthenticated = false;
        if (context == "missing-id") fixture.Actor.UserId = null;
        if (context == "empty-id") fixture.Actor.UserId = Guid.Empty;

        foreach (var operation in new[] { "add", "update", "remove" })
        {
            var denied = await fixture.MutateMembershipAsync(operation);
            Assert.False(denied.IsSuccess);
            Assert.Equal("You are not allowed to manage project members.", denied.Error);
        }
        var list = await fixture.Service.ListMembersAsync(fixture.ProjectId, fixture.Token);
        Assert.False(list.IsSuccess);
        Assert.Null(list.Value);
        Assert.Equal(before, await fixture.ReadPersistedMembershipsAsync());
        Assert.Empty(await fixture.Db.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Empty(fixture.Invalidations.Calls);
        Assert.Empty(fixture.AuthorizationChanges.Calls);
    }

    [Fact]
    public async Task MissingTenantContextCannotUseRetainedGlobalActorIdentity()
    {
        await using var fixture = await ProjectServicePersistenceContext.CreateAsync();
        await fixture.AddTargetMemberAsync();
        var before = await fixture.ReadPersistedMembershipsAsync();
        fixture.Tenant.Clear();

        foreach (var operation in new[] { "add", "update", "remove" })
        {
            var denied = await fixture.MutateMembershipAsync(operation);
            Assert.False(denied.IsSuccess);
            Assert.Equal("You are not allowed to manage project members.", denied.Error);
        }
        var list = await fixture.Service.ListMembersAsync(fixture.ProjectId, fixture.Token);
        Assert.False(list.IsSuccess);
        Assert.Null(list.Value);
        Assert.Equal(before, await fixture.ReadPersistedMembershipsAsync());
        fixture.Tenant.SetTenant(fixture.TenantId, "project-service");
        Assert.Empty(await fixture.Db.AuditLogs.AsNoTracking().ToListAsync());
        Assert.Empty(fixture.Invalidations.Calls);
        Assert.Empty(fixture.AuthorizationChanges.Calls);
    }
}
