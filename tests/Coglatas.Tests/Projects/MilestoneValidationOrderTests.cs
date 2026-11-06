using Coglatas.Application.Projects;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Projects;

public sealed partial class ProjectServiceTests
{
    [Theory]
    [InlineData((MilestoneStatus)999, null)]
    [InlineData(MilestoneStatus.InProgress, "MILESTONE_DATE_REQUIRED")]
    [InlineData(MilestoneStatus.Completed, "MILESTONE_DATE_REQUIRED")]
    public async Task RejectedMilestoneStatusPreservesTrackedTitleAndEffects(
        MilestoneStatus status,
        string? expectedCode)
    {
        var fixture = ProjectFixture.Create();
        var manager = fixture.AddUser();
        fixture.Current.UserIdValue = manager.Id;
        fixture.AddProjectMember(manager.Id, ProjectRole.Manager);
        var milestone = fixture.AddMilestone("Original title", 7);
        milestone.Description = "Original description";
        milestone.VersionNo = 4;

        var result = await fixture.Service.UpdateMilestoneAsync(
            milestone.Id,
            new UpdateMilestoneRequest(
                "  Rejected title  ",
                "Rejected description",
                null,
                status,
                99,
                milestone.VersionNo));

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedCode, result.ErrorDetail?.Code);
        Assert.Equal(
            expectedCode is null
                ? "Milestone status must be NotStarted, InProgress, or Completed."
                : "MILESTONE_DATE_REQUIRED|Milestone date is required before progress can become active or completed.",
            result.Error);
        Assert.Equal("Original title", milestone.Name);
        Assert.Equal("Original description", milestone.Description);
        Assert.Null(milestone.DueDate);
        Assert.Equal(MilestoneStatus.NotStarted, milestone.Status);
        Assert.Equal(7, milestone.SortOrder);
        Assert.Equal(4, milestone.VersionNo);
        Assert.Null(milestone.DeletedAt);
        Assert.Empty(fixture.Audit.Entries);
        Assert.Equal(0, fixture.Invalidations.ProjectChangedCount);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.ClearCount);
    }

    [Theory]
    [InlineData(MilestoneStatus.InProgress)]
    [InlineData(MilestoneStatus.Completed)]
    public async Task UndatedCurrentMilestoneStatusRejectsTitleOnlyUpdateWithoutMutation(
        MilestoneStatus currentStatus)
    {
        var fixture = ProjectFixture.Create();
        var manager = fixture.AddUser();
        fixture.Current.UserIdValue = manager.Id;
        fixture.AddProjectMember(manager.Id, ProjectRole.Manager);
        var milestone = fixture.AddMilestone("Original title", 7);
        milestone.Status = currentStatus;

        var result = await fixture.Service.UpdateMilestoneAsync(
            milestone.Id,
            new UpdateMilestoneRequest("Rejected title", null, null, null, null, milestone.VersionNo));

        Assert.False(result.IsSuccess);
        Assert.Equal("MILESTONE_DATE_REQUIRED", result.ErrorDetail?.Code);
        Assert.Equal("Original title", milestone.Name);
        Assert.Equal(currentStatus, milestone.Status);
        Assert.Null(milestone.DueDate);
        Assert.Equal(1, milestone.VersionNo);
        Assert.Empty(fixture.Audit.Entries);
        Assert.Equal(0, fixture.Invalidations.ProjectChangedCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.ClearCount);
    }

    [Theory]
    [InlineData(MilestoneStatus.NotStarted, true)]
    [InlineData(MilestoneStatus.NotStarted, false)]
    [InlineData(MilestoneStatus.InProgress, true)]
    [InlineData(MilestoneStatus.InProgress, false)]
    [InlineData(MilestoneStatus.Completed, true)]
    [InlineData(MilestoneStatus.Completed, false)]
    public async Task ValidMilestoneUpdatePreservesTitleDateAndEffectContracts(
        MilestoneStatus status,
        bool replaceTitleAndDate)
    {
        var fixture = ProjectFixture.Create();
        var manager = fixture.AddUser();
        fixture.Current.UserIdValue = manager.Id;
        fixture.AddProjectMember(manager.Id, ProjectRole.Manager);
        var milestone = fixture.AddMilestone("Original title", 7);
        milestone.DueDate = new DateOnly(2026, 11, 7);
        milestone.VersionNo = 4;
        var expectedDate = replaceTitleAndDate ? new DateOnly(2026, 11, 8) : milestone.DueDate;

        var result = await fixture.Service.UpdateMilestoneAsync(
            milestone.Id,
            new UpdateMilestoneRequest(
                replaceTitleAndDate ? "  Updated title  " : null,
                "  Updated description  ",
                replaceTitleAndDate ? expectedDate : null,
                status,
                11,
                milestone.VersionNo));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Equal(replaceTitleAndDate ? "Updated title" : "Original title", milestone.Name);
        Assert.Equal("Updated description", milestone.Description);
        Assert.Equal(expectedDate, milestone.DueDate);
        Assert.Equal(status, milestone.Status);
        Assert.Equal(11, milestone.SortOrder);
        Assert.Equal(5, milestone.VersionNo);
        Assert.NotNull(result.Value);
        Assert.Equal(milestone.Id, result.Value.Id);
        Assert.Equal(milestone.Name, result.Value.Title);
        Assert.Equal(milestone.Description, result.Value.Description);
        Assert.Equal(milestone.DueDate, result.Value.DueDate);
        Assert.Equal(milestone.Status, result.Value.Status);
        Assert.Equal(milestone.SortOrder, result.Value.DisplayOrder);
        Assert.Equal(milestone.VersionNo, result.Value.Version);
        var entry = Assert.Single(fixture.Audit.Entries);
        Assert.Equal(manager.Id, entry.ActorUserId);
        Assert.Equal("MilestoneUpdated", entry.Action);
        Assert.Equal("Milestone", entry.EntityType);
        Assert.Equal(milestone.Id, entry.EntityId);
        Assert.Equal(1, fixture.Invalidations.ProjectChangedCount);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Equal(1, fixture.CommandUnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.ClearCount);
    }
}
