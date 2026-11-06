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
}
