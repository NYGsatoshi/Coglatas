using Coglatas.Application.Projects;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Projects;

public sealed partial class ProjectServiceTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("  ", false)]
    [InlineData("\t", true)]
    [InlineData("Candidate", false)]
    public async Task InvalidMilestoneCreatePreservesTheExistingMilestoneAndAcceptsTheNextValidCommand(
        string? title,
        bool hasDueDate)
    {
        var fixture = ProjectFixture.Create();
        var manager = fixture.AddUser();
        fixture.Current.UserIdValue = manager.Id;
        fixture.AddProjectMember(manager.Id, ProjectRole.Manager);
        var existing = fixture.AddMilestone("Unchanged", 7);
        existing.Description = "Existing description";
        existing.DueDate = new DateOnly(2026, 6, 10);
        existing.VersionNo = 4;
        var before = (existing.Id, existing.ProjectId, existing.Name, existing.Description,
            existing.DueDate, existing.Status, existing.SortOrder, existing.VersionNo,
            existing.CreatedAt, existing.UpdatedAt, existing.DeletedAt);
        Assert.True(await fixture.ProjectAuthorization.CanManageProject(manager.Id, fixture.Project.Id));

        var rejected = await fixture.Service.CreateMilestoneAsync(
            fixture.Project.Id,
            new CreateMilestoneRequest(title!, "Rejected description",
                hasDueDate ? new DateOnly(2026, 6, 11) : null, 99));

        Assert.False(rejected.IsSuccess);
        Assert.Equal(string.IsNullOrWhiteSpace(title)
            ? "Milestone title is required."
            : "MILESTONE_DATE_REQUIRED|Milestone date is required.", rejected.Error);
        Assert.Equal(before, (existing.Id, existing.ProjectId, existing.Name, existing.Description,
            existing.DueDate, existing.Status, existing.SortOrder, existing.VersionNo,
            existing.CreatedAt, existing.UpdatedAt, existing.DeletedAt));
        Assert.Equal(existing.Id, Assert.Single(fixture.Projects.Milestones.Keys));
        var storedAfterRejection = fixture.Projects.Milestones[existing.Id];
        Assert.Equal(before, (storedAfterRejection.Id, storedAfterRejection.ProjectId,
            storedAfterRejection.Name, storedAfterRejection.Description, storedAfterRejection.DueDate,
            storedAfterRejection.Status, storedAfterRejection.SortOrder, storedAfterRejection.VersionNo,
            storedAfterRejection.CreatedAt, storedAfterRejection.UpdatedAt, storedAfterRejection.DeletedAt));
        Assert.Equal(0, fixture.CommandUnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.ClearCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.GenericSaveCount);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Empty(fixture.Audit.Entries.Select(entry => entry.EntityId));
        Assert.Empty(fixture.Invalidations.ProjectChanges.Select(change => change.ProjectId));
        Assert.Empty(fixture.AuthorizationChanges.Items.Select(change => change.UserId));

        var accepted = await fixture.Service.CreateMilestoneAsync(
            fixture.Project.Id,
            new CreateMilestoneRequest("  Accepted after rejection  ", "  Accepted description  ",
                new DateOnly(2026, 6, 12), 20));

        Assert.True(accepted.IsSuccess);
        Assert.NotNull(accepted.Value);
        Assert.NotEqual(existing.Id, accepted.Value.Id);
        Assert.Equal(fixture.Project.Id, accepted.Value.ProjectId);
        Assert.Equal("Accepted after rejection", accepted.Value.Title);
        Assert.Equal("Accepted description", accepted.Value.Description);
        Assert.Equal(new DateOnly(2026, 6, 12), accepted.Value.DueDate);
        Assert.Equal(20, accepted.Value.DisplayOrder);
        Assert.Equal(1L, accepted.Value.Version);
        Assert.Equal(new[] { existing.Id, accepted.Value.Id }.Order(), fixture.Projects.Milestones.Keys.Order());
        var storedCreate = fixture.Projects.Milestones[accepted.Value.Id];
        Assert.Equal((accepted.Value.Id, accepted.Value.ProjectId, accepted.Value.Title,
                accepted.Value.Description, accepted.Value.DueDate, accepted.Value.DisplayOrder, accepted.Value.Version),
            (storedCreate.Id, storedCreate.ProjectId, storedCreate.Name, storedCreate.Description,
                storedCreate.DueDate, storedCreate.SortOrder, storedCreate.VersionNo));
        var storedExistingAfterCreate = fixture.Projects.Milestones[existing.Id];
        Assert.Equal(before, (storedExistingAfterCreate.Id, storedExistingAfterCreate.ProjectId,
            storedExistingAfterCreate.Name, storedExistingAfterCreate.Description, storedExistingAfterCreate.DueDate,
            storedExistingAfterCreate.Status, storedExistingAfterCreate.SortOrder, storedExistingAfterCreate.VersionNo,
            storedExistingAfterCreate.CreatedAt, storedExistingAfterCreate.UpdatedAt, storedExistingAfterCreate.DeletedAt));
        Assert.Equal(before, (existing.Id, existing.ProjectId, existing.Name, existing.Description,
            existing.DueDate, existing.Status, existing.SortOrder, existing.VersionNo,
            existing.CreatedAt, existing.UpdatedAt, existing.DeletedAt));
        Assert.Equal(1, fixture.CommandUnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.ClearCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.GenericSaveCount);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Equal(accepted.Value.Id, Assert.Single(fixture.Audit.Entries.Select(entry => entry.EntityId)));
        Assert.Equal(fixture.Project.Id, Assert.Single(fixture.Invalidations.ProjectChanges.Select(change => change.ProjectId)));
        Assert.Empty(fixture.AuthorizationChanges.Items.Select(change => change.UserId));
    }

    [Theory]
    [InlineData(0L, "Changed", false)]
    [InlineData(-1L, "Changed", false)]
    [InlineData(4L, "", false)]
    [InlineData(4L, "  ", false)]
    [InlineData(4L, "\t", false)]
    [InlineData(4L, "Changed", true)]
    public async Task InvalidMilestoneUpdatePreservesSelectedStateAndDoesNotPoisonTheNextValidCommand(
        long expectedVersion,
        string title,
        bool missingDueDate)
    {
        var fixture = ProjectFixture.Create();
        var manager = fixture.AddUser();
        fixture.Current.UserIdValue = manager.Id;
        fixture.AddProjectMember(manager.Id, ProjectRole.Manager);
        var milestone = fixture.AddMilestone("Unchanged", 7);
        milestone.Description = "Existing description";
        milestone.DueDate = missingDueDate ? null : new DateOnly(2026, 6, 10);
        milestone.VersionNo = 4;
        var before = (milestone.Id, milestone.ProjectId, milestone.Name, milestone.Description,
            milestone.DueDate, milestone.Status, milestone.SortOrder, milestone.VersionNo,
            milestone.CreatedAt, milestone.UpdatedAt, milestone.DeletedAt);
        Assert.True(await fixture.ProjectAuthorization.CanManageProject(manager.Id, fixture.Project.Id));

        var rejected = await fixture.Service.UpdateMilestoneAsync(
            milestone.Id,
            new UpdateMilestoneRequest(title, "Rejected description", null,
                MilestoneStatus.InProgress, 99, expectedVersion));

        Assert.False(rejected.IsSuccess);
        if (expectedVersion <= 0)
            Assert.Equal("MILESTONE_INVALID_EXPECTED_VERSION", rejected.ErrorDetail?.Code);
        else if (missingDueDate)
            Assert.Equal("MILESTONE_DATE_REQUIRED", rejected.ErrorDetail?.Code);
        else
            Assert.Equal("Milestone title is required.", rejected.Error);
        Assert.Equal(before, (milestone.Id, milestone.ProjectId, milestone.Name, milestone.Description,
            milestone.DueDate, milestone.Status, milestone.SortOrder, milestone.VersionNo,
            milestone.CreatedAt, milestone.UpdatedAt, milestone.DeletedAt));
        Assert.Equal(milestone.Id, Assert.Single(fixture.Projects.Milestones.Keys));
        var storedAfterRejection = fixture.Projects.Milestones[milestone.Id];
        Assert.Equal(before, (storedAfterRejection.Id, storedAfterRejection.ProjectId,
            storedAfterRejection.Name, storedAfterRejection.Description, storedAfterRejection.DueDate,
            storedAfterRejection.Status, storedAfterRejection.SortOrder, storedAfterRejection.VersionNo,
            storedAfterRejection.CreatedAt, storedAfterRejection.UpdatedAt, storedAfterRejection.DeletedAt));
        Assert.Equal(0, fixture.CommandUnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.ClearCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.GenericSaveCount);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Empty(fixture.Audit.Entries.Select(entry => entry.EntityId));
        Assert.Empty(fixture.Invalidations.ProjectChanges.Select(change => change.ProjectId));
        Assert.Empty(fixture.AuthorizationChanges.Items.Select(change => change.UserId));

        var accepted = await fixture.Service.UpdateMilestoneAsync(
            milestone.Id,
            new UpdateMilestoneRequest("  Accepted after rejection  ", "  Accepted description  ",
                new DateOnly(2026, 6, 12), MilestoneStatus.InProgress, 20, 4));

        Assert.True(accepted.IsSuccess);
        Assert.NotNull(accepted.Value);
        Assert.Equal(milestone.Id, accepted.Value.Id);
        Assert.Equal("Accepted after rejection", milestone.Name);
        Assert.Equal("Accepted description", milestone.Description);
        Assert.Equal(new DateOnly(2026, 6, 12), milestone.DueDate);
        Assert.Equal(MilestoneStatus.InProgress, milestone.Status);
        Assert.Equal(20, milestone.SortOrder);
        Assert.Equal(5L, milestone.VersionNo);
        Assert.Equal(5L, accepted.Value.Version);
        Assert.Equal(milestone.Name, accepted.Value.Title);
        Assert.Equal(milestone.Status, accepted.Value.Status);
        Assert.Equal(milestone.DueDate, accepted.Value.DueDate);
        Assert.Equal(milestone.SortOrder, accepted.Value.DisplayOrder);
        Assert.Equal(milestone.Description, accepted.Value.Description);
        var storedUpdate = fixture.Projects.Milestones[milestone.Id];
        Assert.Equal((milestone.Id, milestone.ProjectId, milestone.Name, milestone.Description,
                milestone.DueDate, milestone.Status, milestone.SortOrder, milestone.VersionNo,
                milestone.CreatedAt, milestone.UpdatedAt, milestone.DeletedAt),
            (storedUpdate.Id, storedUpdate.ProjectId, storedUpdate.Name, storedUpdate.Description,
                storedUpdate.DueDate, storedUpdate.Status, storedUpdate.SortOrder, storedUpdate.VersionNo,
                storedUpdate.CreatedAt, storedUpdate.UpdatedAt, storedUpdate.DeletedAt));
        Assert.Equal(milestone.Id, Assert.Single(fixture.Projects.Milestones.Keys));
        Assert.Equal(1, fixture.CommandUnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.ClearCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.GenericSaveCount);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Equal(milestone.Id, Assert.Single(fixture.Audit.Entries.Select(entry => entry.EntityId)));
        Assert.Equal(fixture.Project.Id, Assert.Single(fixture.Invalidations.ProjectChanges.Select(change => change.ProjectId)));
        Assert.Empty(fixture.AuthorizationChanges.Items.Select(change => change.UserId));
    }
}
