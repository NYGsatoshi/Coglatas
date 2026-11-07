using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Projects;

public sealed partial class ProjectServiceTests
{
    [Theory]
    [InlineData(ProjectRole.Owner)]
    [InlineData(ProjectRole.Manager)]
    public async Task MembershipManagerRoundTripKeepsIdentityAndRecordsScopedAuditAndSignals(ProjectRole actorRole)
    {
        var fixture = ProjectFixture.Create();
        fixture.Project.Visibility = ProjectVisibility.MembersOnly;
        var actor = fixture.AddUser();
        var target = fixture.AddUser();
        fixture.Current.UserIdValue = actor.Id;
        fixture.AddProjectMember(actor.Id, actorRole);
        SeedMembershipScope(fixture);
        var unrelated = new ProjectMember
        {
            ProjectId = Guid.NewGuid(), UserId = target.Id, User = target,
            TenantId = Guid.NewGuid(), Role = ProjectRole.Owner,
            JoinedAt = fixture.Clock.UtcNow.AddDays(-1),
            CreatedAt = fixture.Clock.UtcNow.AddDays(-3), UpdatedAt = fixture.Clock.UtcNow.AddDays(-2)
        };
        fixture.Projects.Members.Add(unrelated);
        var unrelatedBefore = MembershipRows(fixture).Single(row => row.ProjectId == unrelated.ProjectId);

        var added = await fixture.Service.AddMemberAsync(fixture.Project.Id, new(target.Id, ProjectRole.Viewer));
        Assert.True(added.IsSuccess);
        Assert.Equal(target.Id, added.Value!.UserId);
        Assert.Equal(target.DisplayName, added.Value.DisplayName);
        Assert.Equal(target.Email, added.Value.Email);
        Assert.Equal(ProjectRole.Viewer, added.Value.Role);
        Assert.Equal(fixture.Clock.UtcNow, added.Value.JoinedAt);
        var addedRow = MembershipRows(fixture).Single(row => row.ProjectId == fixture.Project.Id && row.UserId == target.Id);
        Assert.NotEqual(Guid.Empty, addedRow.Id);
        Assert.Equal(1, fixture.CommandUnitOfWork.SaveCount);
        Assert.Equal(1, fixture.Invalidations.ProjectChangedCount);
        var firstAudit = Assert.Single(fixture.Audit.Entries);
        Assert.Equal("ProjectMemberAdded", firstAudit.Action);
        Assert.Equal(fixture.Project.Id, firstAudit.EntityId);
        Assert.Equal(actor.Id, firstAudit.ActorUserId);
        Assert.Equal("Project", firstAudit.EntityType);
        Assert.Equal((fixture.Project.Id, actor.Id, "memberChanged"), Assert.Single(fixture.Invalidations.ProjectChanges));
        var firstSignal = Assert.Single(fixture.AuthorizationChanges.Items);
        Assert.Equal((fixture.Project.TenantId, target.Id, "project", (Guid?)fixture.Project.Id, "membershipChanged"), firstSignal);

        var listed = await fixture.Service.ListMembersAsync(fixture.Project.Id);
        Assert.True(listed.IsSuccess);
        Assert.Equal(2, listed.Value!.Count);
        Assert.Equal(added.Value, Assert.Single(listed.Value, member => member.UserId == target.Id));
        Assert.Equal(1, fixture.CommandUnitOfWork.SaveCount);
        Assert.Single(fixture.Invalidations.ProjectChanges);

        var updated = await fixture.Service.UpdateMemberAsync(fixture.Project.Id, target.Id, new(ProjectRole.Contributor));
        Assert.True(updated.IsSuccess);
        Assert.Equal(ProjectRole.Contributor, updated.Value!.Role);
        Assert.Equal(added.Value.JoinedAt, updated.Value.JoinedAt);
        Assert.Equal(target.Id, updated.Value.UserId);
        var expectedUpdated = addedRow;
        expectedUpdated.Role = ProjectRole.Contributor;
        Assert.Equal(expectedUpdated, MembershipRows(fixture).Single(row => row.ProjectId == fixture.Project.Id && row.UserId == target.Id));
        Assert.Equal(2, fixture.CommandUnitOfWork.SaveCount);
        Assert.Equal(2, fixture.Invalidations.ProjectChangedCount);
        Assert.Equal("ProjectMemberUpdated", fixture.Audit.Entries[1].Action);
        Assert.Equal(fixture.Project.Id, fixture.Audit.Entries[1].EntityId);
        Assert.Equal((fixture.Project.TenantId, target.Id, "project", (Guid?)fixture.Project.Id, "membershipChanged"), fixture.AuthorizationChanges.Items[1]);

        var removed = await fixture.Service.RemoveMemberAsync(fixture.Project.Id, target.Id);
        Assert.True(removed.IsSuccess);
        Assert.DoesNotContain(fixture.Projects.Members, member => member.ProjectId == fixture.Project.Id && member.UserId == target.Id);
        Assert.Contains(fixture.Projects.Members, member => member.ProjectId == fixture.Project.Id && member.UserId == actor.Id && member.Role == actorRole);
        Assert.Same(unrelated, Assert.Single(fixture.Projects.Members, member => member.ProjectId == unrelated.ProjectId));
        Assert.Equal(ProjectRole.Owner, unrelated.Role);
        Assert.Equal(fixture.Clock.UtcNow.AddDays(-1), unrelated.JoinedAt);
        Assert.Equal(unrelatedBefore, MembershipRows(fixture).Single(row => row.ProjectId == unrelated.ProjectId));
        Assert.Equal(3, fixture.CommandUnitOfWork.SaveCount);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.ClearCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.GenericSaveCount);
        Assert.Equal(3, fixture.Invalidations.ProjectChangedCount);
        Assert.Equal(["ProjectMemberAdded", "ProjectMemberUpdated", "ProjectMemberRemoved"], fixture.Audit.Entries.Select(entry => entry.Action));
        Assert.All(fixture.Audit.Entries, entry =>
        {
            Assert.Equal(actor.Id, entry.ActorUserId);
            Assert.Equal(fixture.Project.Id, entry.EntityId);
            Assert.Equal("Project", entry.EntityType);
        });
        Assert.Equal(3, fixture.Invalidations.ProjectChanges.Count);
        Assert.All(fixture.Invalidations.ProjectChanges, change => Assert.Equal((fixture.Project.Id, actor.Id, "memberChanged"), change));
        Assert.Equal(3, fixture.AuthorizationChanges.Items.Count);
        Assert.Equal((fixture.Project.TenantId, target.Id, "project", (Guid?)fixture.Project.Id, "revoked"), fixture.AuthorizationChanges.Items[2]);
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("empty-actor")]
    [InlineData("missing-workspace-member")]
    [InlineData("pending-workspace-member")]
    [InlineData("suspended-workspace-member")]
    [InlineData("readonly-workspace-member")]
    [InlineData("archived-workspace")]
    [InlineData("deleted-workspace")]
    [InlineData("deleted-project")]
    [InlineData("soft-deleted-project")]
    [InlineData("missing-project")]
    [InlineData("project-viewer")]
    [InlineData("project-contributor")]
    public async Task MembershipMutationsRejectUnavailableAuthorityBeforeAnySideEffect(string scenario)
    {
        var fixture = ProjectFixture.Create();
        fixture.Project.Visibility = ProjectVisibility.MembersOnly;
        var actor = fixture.AddUser();
        var target = fixture.AddUser();
        var candidate = fixture.AddUser();
        fixture.Current.UserIdValue = actor.Id;
        fixture.AddProjectMember(actor.Id, ProjectRole.Owner);
        fixture.AddProjectMember(target.Id, ProjectRole.Viewer);
        SeedMembershipScope(fixture);
        var workspaceMember = fixture.Workspaces.Members.Single(member => member.UserId == actor.Id);
        switch (scenario)
        {
            case "anonymous": fixture.Current.UserIdValue = null; break;
            case "empty-actor": fixture.Current.UserIdValue = Guid.Empty; break;
            case "missing-workspace-member": fixture.Workspaces.Members.Remove(workspaceMember); break;
            case "pending-workspace-member": workspaceMember.Status = MembershipStatus.Pending; break;
            case "suspended-workspace-member": workspaceMember.Status = MembershipStatus.Suspended; break;
            case "readonly-workspace-member": workspaceMember.Role = WorkspaceRole.ReadOnly; break;
            case "archived-workspace": fixture.Workspace.Status = WorkspaceStatus.Archived; break;
            case "deleted-workspace": fixture.Workspace.MarkDeleted(fixture.Clock.UtcNow); break;
            case "deleted-project": fixture.Project.Status = ProjectStatus.Deleted; break;
            case "soft-deleted-project": fixture.Project.MarkDeleted(fixture.Clock.UtcNow); break;
            case "missing-project": fixture.Projects.ProjectItems.Remove(fixture.Project.Id); break;
            case "project-viewer": fixture.Projects.Members.Single(member => member.UserId == actor.Id).Role = ProjectRole.Viewer; break;
            case "project-contributor": fixture.Projects.Members.Single(member => member.UserId == actor.Id).Role = ProjectRole.Contributor; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        var before = MembershipRows(fixture);
        Assert.False((await fixture.Service.AddMemberAsync(fixture.Project.Id, new(candidate.Id, ProjectRole.Manager))).IsSuccess);
        AssertMembershipDeniedWithoutSideEffects(fixture, before);
        Assert.False((await fixture.Service.UpdateMemberAsync(fixture.Project.Id, target.Id, new(ProjectRole.Manager))).IsSuccess);
        AssertMembershipDeniedWithoutSideEffects(fixture, before);
        Assert.False((await fixture.Service.RemoveMemberAsync(fixture.Project.Id, target.Id)).IsSuccess);
        AssertMembershipDeniedWithoutSideEffects(fixture, before);
    }

    [Theory]
    [InlineData("missing-user")]
    [InlineData("missing-workspace-member")]
    [InlineData("pending-workspace-member")]
    [InlineData("suspended-workspace-member")]
    [InlineData("missing-group-member")]
    [InlineData("duplicate-project-member")]
    public async Task MembershipAdditionRejectsMissingParentAccessOrDuplicateWithoutEffects(string scenario)
    {
        var fixture = ProjectFixture.Create();
        fixture.Project.Visibility = ProjectVisibility.MembersOnly;
        var actor = fixture.AddUser();
        var target = fixture.AddUser();
        fixture.Current.UserIdValue = actor.Id;
        fixture.AddProjectMember(actor.Id, ProjectRole.Owner);
        SeedMembershipScope(fixture);
        var targetId = target.Id;
        var workspaceMember = fixture.Workspaces.Members.Single(member => member.UserId == target.Id);
        switch (scenario)
        {
            case "missing-user": targetId = Guid.NewGuid(); break;
            case "missing-workspace-member": fixture.Workspaces.Members.Remove(workspaceMember); break;
            case "pending-workspace-member": workspaceMember.Status = MembershipStatus.Pending; break;
            case "suspended-workspace-member": workspaceMember.Status = MembershipStatus.Suspended; break;
            case "missing-group-member": fixture.Project.GroupId = fixture.AddGroup(actor.Id, GroupRole.Owner).Id; break;
            case "duplicate-project-member": fixture.AddProjectMember(target.Id, ProjectRole.Viewer); break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        var before = MembershipRows(fixture);
        Assert.False((await fixture.Service.AddMemberAsync(fixture.Project.Id, new(targetId, ProjectRole.Manager))).IsSuccess);
        AssertMembershipDeniedWithoutSideEffects(fixture, before);
    }

    [Fact]
    public async Task MembershipMissingUpdateAndRemovalKeepExistingMembersAndSignalsUnchanged()
    {
        var fixture = ProjectFixture.Create();
        fixture.Project.Visibility = ProjectVisibility.MembersOnly;
        var actor = fixture.AddUser();
        fixture.Current.UserIdValue = actor.Id;
        fixture.AddProjectMember(actor.Id, ProjectRole.Owner);
        SeedMembershipScope(fixture);
        var before = MembershipRows(fixture);
        var missing = Guid.NewGuid();
        Assert.False((await fixture.Service.UpdateMemberAsync(fixture.Project.Id, missing, new(ProjectRole.Manager))).IsSuccess);
        AssertMembershipDeniedWithoutSideEffects(fixture, before);
        Assert.False((await fixture.Service.RemoveMemberAsync(fixture.Project.Id, missing)).IsSuccess);
        AssertMembershipDeniedWithoutSideEffects(fixture, before);
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("missing-project")]
    [InlineData("deleted-project")]
    public async Task MembershipListDenialHasNoMutationOrSignals(string scenario)
    {
        var fixture = ProjectFixture.Create();
        var actor = fixture.AddUser();
        fixture.Current.UserIdValue = actor.Id;
        fixture.AddProjectMember(actor.Id, ProjectRole.Owner);
        SeedMembershipScope(fixture);
        switch (scenario)
        {
            case "anonymous": fixture.Current.UserIdValue = null; break;
            case "missing-project": fixture.Projects.ProjectItems.Remove(fixture.Project.Id); break;
            case "deleted-project": fixture.Project.Status = ProjectStatus.Deleted; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        var before = MembershipRows(fixture);
        Assert.False((await fixture.Service.ListMembersAsync(fixture.Project.Id)).IsSuccess);
        AssertMembershipDeniedWithoutSideEffects(fixture, before);
    }

    private static void SeedMembershipScope(ProjectFixture fixture)
    {
        var tenantId = Guid.NewGuid();
        fixture.Project.TenantId = tenantId;
        fixture.Workspace.TenantId = tenantId;
        foreach (var member in fixture.Workspaces.Members) member.TenantId = tenantId;
        foreach (var member in fixture.Projects.Members)
        {
            member.TenantId = tenantId;
            member.CreatedAt = fixture.Clock.UtcNow.AddDays(-3);
            member.UpdatedAt = fixture.Clock.UtcNow.AddDays(-2);
        }
        Assert.NotEqual(Guid.Empty, fixture.Project.TenantId);
    }

    private static (Guid Id, Guid TenantId, Guid ProjectId, Guid UserId, ProjectRole Role,
        DateTimeOffset JoinedAt, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt)[] MembershipRows(ProjectFixture fixture) =>
        fixture.Projects.Members.OrderBy(member => member.ProjectId).ThenBy(member => member.UserId)
            .Select(member => (member.Id, member.TenantId, member.ProjectId, member.UserId, member.Role,
                member.JoinedAt, member.CreatedAt, member.UpdatedAt)).ToArray();

    private static void AssertMembershipDeniedWithoutSideEffects(ProjectFixture fixture,
        (Guid Id, Guid TenantId, Guid ProjectId, Guid UserId, ProjectRole Role,
            DateTimeOffset JoinedAt, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt)[] before)
    {
        Assert.Equal(before, MembershipRows(fixture));
        Assert.Empty(fixture.Audit.Entries);
        Assert.Empty(fixture.AuthorizationChanges.Items);
        Assert.Empty(fixture.Invalidations.ProjectChanges);
        Assert.Equal(0, fixture.Invalidations.ProjectChangedCount);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.ClearCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.GenericSaveCount);
    }
}
