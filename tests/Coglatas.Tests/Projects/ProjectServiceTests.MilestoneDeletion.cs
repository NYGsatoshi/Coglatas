using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Projects;

public sealed partial class ProjectServiceTests
{
    [Theory]
    [InlineData(ProjectRole.Owner)]
    [InlineData(ProjectRole.Manager)]
    public async Task MilestoneDeletionStagesOneScopedMutationAndPreservesOtherRows(ProjectRole role)
    {
        var fixture = ProjectFixture.Create();
        var actor = fixture.AddUser();
        fixture.Current.UserIdValue = actor.Id;
        fixture.AddProjectMember(actor.Id, role);
        var milestone = fixture.AddMilestone("Delete candidate", 10);
        milestone.VersionNo = 7;
        var unrelated = fixture.AddMilestone("Retain sibling", 20);
        var task = fixture.AddTask("Retain linked task");
        task.MilestoneId = milestone.Id;

        var result = await fixture.Service.DeleteMilestoneAsync(milestone.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(fixture.Clock.UtcNow, milestone.DeletedAt);
        Assert.Equal(8, milestone.VersionNo);
        Assert.Same(milestone, fixture.Projects.Milestones[milestone.Id]);
        Assert.Same(unrelated, fixture.Projects.Milestones[unrelated.Id]);
        Assert.Null(unrelated.DeletedAt);
        Assert.Equal(1, unrelated.VersionNo);
        Assert.Equal(2, fixture.Projects.Milestones.Count);
        Assert.Same(task, Assert.Single(fixture.Projects.Tasks).Value);
        Assert.Equal(milestone.Id, task.MilestoneId);
        Assert.Equal(1, fixture.CommandUnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.ClearCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.GenericSaveCount);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        var audit = Assert.Single(fixture.Audit.Entries);
        Assert.Equal("MilestoneDeleted", audit.Action);
        Assert.Equal("Milestone", audit.EntityType);
        Assert.Equal(milestone.Id, audit.EntityId);
        Assert.Equal(actor.Id, audit.ActorUserId);
        Assert.Equal((fixture.Project.Id, actor.Id, "milestoneChanged"), Assert.Single(fixture.Invalidations.ProjectChanges));
        Assert.Equal(0, fixture.Invalidations.TaskChangedCount);
        Assert.Empty(fixture.AuthorizationChanges.Items);
        Assert.Empty(fixture.TaskNotifications.Requests);
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("empty-actor")]
    [InlineData("missing-milestone")]
    [InlineData("foreign-project")]
    [InlineData("project-viewer")]
    [InlineData("missing-project-member")]
    [InlineData("pending-workspace-member")]
    [InlineData("suspended-workspace-member")]
    [InlineData("archived-project")]
    [InlineData("deleted-project")]
    [InlineData("soft-deleted-project")]
    public async Task MilestoneDeletionDenialDoesNotMutateOrStageSideEffects(string scenario)
    {
        var fixture = ProjectFixture.Create();
        var actor = fixture.AddUser();
        fixture.Current.UserIdValue = actor.Id;
        fixture.AddProjectMember(actor.Id, ProjectRole.Manager);
        var milestone = fixture.AddMilestone("Retain candidate", 10);
        milestone.VersionNo = 7;
        var member = fixture.Workspaces.Members.Single(row => row.UserId == actor.Id);
        switch (scenario)
        {
            case "anonymous": fixture.Current.UserIdValue = null; break;
            case "empty-actor": fixture.Current.UserIdValue = Guid.Empty; break;
            case "missing-milestone": fixture.Projects.Milestones.Clear(); break;
            case "foreign-project": milestone.ProjectId = Guid.NewGuid(); break;
            case "project-viewer": fixture.Projects.Members.Single().Role = ProjectRole.Viewer; break;
            case "missing-project-member": fixture.Projects.Members.Clear(); break;
            case "pending-workspace-member": member.Status = MembershipStatus.Pending; break;
            case "suspended-workspace-member": member.Status = MembershipStatus.Suspended; break;
            case "archived-project": fixture.Project.Status = ProjectStatus.Archived; break;
            case "deleted-project": fixture.Project.Status = ProjectStatus.Deleted; break;
            case "soft-deleted-project": fixture.Project.MarkDeleted(fixture.Clock.UtcNow); break;
        }
        var expectedProjectId = milestone.ProjectId;

        var result = await fixture.Service.DeleteMilestoneAsync(milestone.Id);

        Assert.False(result.IsSuccess);
        Assert.Null(milestone.DeletedAt);
        Assert.Equal(7, milestone.VersionNo);
        Assert.Equal("Retain candidate", milestone.Name);
        Assert.Equal(10, milestone.SortOrder);
        Assert.Equal(expectedProjectId, milestone.ProjectId);
        Assert.Equal(0, fixture.CommandUnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.ClearCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.GenericSaveCount);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Empty(fixture.Audit.Entries);
        Assert.Empty(fixture.Invalidations.ProjectChanges);
        Assert.Equal(0, fixture.Invalidations.TaskChangedCount);
        Assert.Empty(fixture.AuthorizationChanges.Items);
        Assert.Empty(fixture.TaskNotifications.Requests);
    }

    [Theory]
    [InlineData(TaskCommandSaveResult.UniqueConflict)]
    [InlineData(TaskCommandSaveResult.ConcurrencyConflict)]
    public async Task MilestoneDeletionSaveConflictClearsTrackingAndReportsTheOwnedError(TaskCommandSaveResult outcome)
    {
        var fixture = ProjectFixture.Create();
        var actor = fixture.AddUser();
        fixture.Current.UserIdValue = actor.Id;
        fixture.AddProjectMember(actor.Id, ProjectRole.Manager);
        var milestone = fixture.AddMilestone("Conflicting candidate", 10);
        milestone.VersionNo = 7;
        fixture.CommandUnitOfWork.Outcome = new(outcome);

        var result = await fixture.Service.DeleteMilestoneAsync(milestone.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("MILESTONE_CONFLICT|Milestone has changed. Refetch and retry.", result.Error);
        Assert.Equal(1, fixture.CommandUnitOfWork.SaveCount);
        Assert.Equal(1, fixture.CommandUnitOfWork.ClearCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.GenericSaveCount);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Equal(fixture.Clock.UtcNow, milestone.DeletedAt);
        Assert.Equal(8, milestone.VersionNo);
        Assert.Equal("MilestoneDeleted", Assert.Single(fixture.Audit.Entries).Action);
        Assert.Equal((fixture.Project.Id, actor.Id, "milestoneChanged"), Assert.Single(fixture.Invalidations.ProjectChanges));
        Assert.Empty(fixture.TaskNotifications.Requests);
    }
}
