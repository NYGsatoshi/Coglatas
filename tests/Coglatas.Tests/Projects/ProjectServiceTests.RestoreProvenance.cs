using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Projects;

public sealed partial class ProjectServiceTests
{
    [Theory]
    [InlineData(null, ProjectActivationState.Activated, null, true, 1)]
    [InlineData(ProjectStatus.Archived, ProjectActivationState.Activated, null, true, 1)]
    [InlineData(ProjectStatus.Deleted, ProjectActivationState.Activated, null, true, 1)]
    [InlineData((ProjectStatus)999, ProjectActivationState.Activated, null, true, 1)]
    [InlineData(ProjectStatus.Active, ProjectActivationState.LegacyUnknown, null, false, null)]
    [InlineData(ProjectStatus.Active, ProjectActivationState.NeverActivated, null, false, null)]
    [InlineData(ProjectStatus.Planning, ProjectActivationState.Activated, null, true, 1)]
    [InlineData(ProjectStatus.Planning, ProjectActivationState.NeverActivated, null, true, null)]
    [InlineData(ProjectStatus.Planning, ProjectActivationState.NeverActivated, null, false, 1)]
    [InlineData(ProjectStatus.Active, ProjectActivationState.Activated, null, false, 1)]
    [InlineData(ProjectStatus.Active, ProjectActivationState.Activated, null, true, null)]
    [InlineData(ProjectStatus.Review, ProjectActivationState.Activated, null, true, 0)]
    [InlineData(ProjectStatus.Completed, ProjectActivationState.Activated, null, true, -1)]
    [InlineData(ProjectStatus.Suspended, ProjectActivationState.Activated, null, true, 1)]
    [InlineData(ProjectStatus.Suspended, ProjectActivationState.Activated, ProjectStatus.Archived, true, 1)]
    [InlineData(ProjectStatus.Suspended, ProjectActivationState.Activated, ProjectStatus.Suspended, true, 1)]
    [InlineData(ProjectStatus.Suspended, ProjectActivationState.NeverActivated, ProjectStatus.Active, false, null)]
    public async Task InconsistentRestoreProvenanceCannotMutateOrPublishTheArchivedProject(
        ProjectStatus? archivedFromStatus,
        ProjectActivationState activationState,
        ProjectStatus? suspendedFromStatus,
        bool hasActivatedAt,
        int? activationVersion)
    {
        var fixture = ProjectFixture.Create();
        var manager = fixture.AddUser();
        fixture.Current.UserIdValue = manager.Id;
        fixture.AddProjectMember(manager.Id, ProjectRole.Manager);
        fixture.Project.Visibility = ProjectVisibility.MembersOnly;
        fixture.Project.Status = ProjectStatus.Archived;
        fixture.Project.ArchivedFromStatus = archivedFromStatus;
        fixture.Project.SuspendedFromStatus = suspendedFromStatus;
        fixture.Project.ActivationState = activationState;
        fixture.Project.ActivatedAtUtc = hasActivatedAt ? fixture.Clock.UtcNow : null;
        fixture.Project.ActivationVersion = activationVersion;
        fixture.Project.VersionNo = 4;
        fixture.Projects.CurrentReaderUserIds[fixture.Project.Id] = [manager.Id];
        var before = (fixture.Project.Id, fixture.Project.Name, fixture.Project.Status,
            fixture.Project.Visibility, fixture.Project.ActivationState, fixture.Project.ActivatedAtUtc,
            fixture.Project.ActivationVersion, fixture.Project.ArchivedFromStatus,
            fixture.Project.SuspendedFromStatus, fixture.Project.VersionNo,
            fixture.Project.CreatedAt, fixture.Project.UpdatedAt, fixture.Project.DeletedAt);
        Assert.True(await fixture.ProjectAuthorization.CanManageProject(manager.Id, fixture.Project.Id));

        var result = await fixture.Service.RestoreAsync(fixture.Project.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("InvalidStateTransition", result.ErrorDetail?.Code);
        Assert.Equal("project", result.ErrorDetail?.Target);
        Assert.Equal("The Project cannot be restored because its prior lifecycle state is unavailable or inconsistent.", result.ErrorDetail?.Message);
        var stored = fixture.Projects.ProjectItems[fixture.Project.Id];
        Assert.Equal(before, (stored.Id, stored.Name, stored.Status, stored.Visibility,
            stored.ActivationState, stored.ActivatedAtUtc, stored.ActivationVersion,
            stored.ArchivedFromStatus, stored.SuspendedFromStatus, stored.VersionNo,
            stored.CreatedAt, stored.UpdatedAt, stored.DeletedAt));
        Assert.Equal(fixture.Project.Id, Assert.Single(fixture.Projects.ProjectItems.Keys));
        Assert.Equal(0, fixture.CommandUnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.GenericSaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.ClearCount);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Empty(fixture.Audit.Entries.Select(entry => entry.EntityId));
        Assert.Empty(fixture.Invalidations.ProjectChanges.Select(change => change.ProjectId));
        Assert.Empty(fixture.AuthorizationChanges.Items.Select(change => change.UserId));
        Assert.Empty(fixture.TaskNotifications.Requests);
    }

    [Theory]
    [InlineData(ProjectStatus.Planning, ProjectActivationState.NeverActivated, null, false, null)]
    [InlineData(ProjectStatus.Active, ProjectActivationState.Activated, null, true, 1)]
    [InlineData(ProjectStatus.Review, ProjectActivationState.Activated, null, true, 2)]
    [InlineData(ProjectStatus.Completed, ProjectActivationState.Activated, null, true, 3)]
    [InlineData(ProjectStatus.Suspended, ProjectActivationState.Activated, ProjectStatus.Active, true, 1)]
    [InlineData(ProjectStatus.Suspended, ProjectActivationState.Activated, ProjectStatus.Review, true, 2)]
    [InlineData(ProjectStatus.Suspended, ProjectActivationState.Activated, ProjectStatus.Completed, true, 3)]
    [InlineData(ProjectStatus.Suspended, ProjectActivationState.NeverActivated, ProjectStatus.Planning, false, null)]
    public async Task ConsistentRestoreUsesTheStoredStateAndEmitsItsScopedEffectsOnlyOnce(
        ProjectStatus archivedFromStatus,
        ProjectActivationState activationState,
        ProjectStatus? suspendedFromStatus,
        bool hasActivatedAt,
        int? activationVersion)
    {
        var fixture = ProjectFixture.Create();
        var manager = fixture.AddUser();
        fixture.Current.UserIdValue = manager.Id;
        fixture.AddProjectMember(manager.Id, ProjectRole.Manager);
        fixture.Project.Visibility = ProjectVisibility.MembersOnly;
        fixture.Project.Status = ProjectStatus.Archived;
        fixture.Project.ArchivedFromStatus = archivedFromStatus;
        fixture.Project.SuspendedFromStatus = suspendedFromStatus;
        fixture.Project.ActivationState = activationState;
        fixture.Project.ActivatedAtUtc = hasActivatedAt ? fixture.Clock.UtcNow : null;
        fixture.Project.ActivationVersion = activationVersion;
        fixture.Project.VersionNo = 4;
        fixture.Projects.CurrentReaderUserIds[fixture.Project.Id] = [manager.Id];
        var activationBefore = (fixture.Project.ActivationState, fixture.Project.ActivatedAtUtc,
            fixture.Project.ActivationVersion, fixture.Project.Visibility);
        Assert.True(await fixture.ProjectAuthorization.CanManageProject(manager.Id, fixture.Project.Id));

        var restored = await fixture.Service.RestoreAsync(fixture.Project.Id);

        Assert.True(restored.IsSuccess, restored.Error);
        var stored = fixture.Projects.ProjectItems[fixture.Project.Id];
        Assert.Equal(archivedFromStatus, stored.Status);
        Assert.Equal(activationBefore, (stored.ActivationState, stored.ActivatedAtUtc,
            stored.ActivationVersion, stored.Visibility));
        var audit = Assert.Single(fixture.Audit.Entries);
        Assert.Equal("ProjectRestored", audit.Action);
        Assert.Equal("Project", audit.EntityType);
        Assert.Equal(fixture.Project.Id, audit.EntityId);
        Assert.Equal(fixture.Project.Id, Assert.Single(fixture.Invalidations.ProjectChanges.Select(change => change.ProjectId)));
        var authorization = Assert.Single(fixture.AuthorizationChanges.Items);
        Assert.Equal(manager.Id, authorization.UserId);
        Assert.Equal(fixture.Project.Id, authorization.ScopeId);
        Assert.Equal("restored", authorization.Change);
        Assert.Equal(1, fixture.CommandUnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.GenericSaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.ClearCount);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Empty(fixture.TaskNotifications.Requests);

        var repeated = await fixture.Service.RestoreAsync(fixture.Project.Id);

        Assert.False(repeated.IsSuccess);
        Assert.Equal("InvalidStateTransition", repeated.ErrorDetail?.Code);
        Assert.Equal("project", repeated.ErrorDetail?.Target);
        Assert.Equal(archivedFromStatus, fixture.Projects.ProjectItems[fixture.Project.Id].Status);
        Assert.Equal(fixture.Project.Id, Assert.Single(fixture.Audit.Entries.Select(entry => entry.EntityId)));
        Assert.Equal(fixture.Project.Id, Assert.Single(fixture.Invalidations.ProjectChanges.Select(change => change.ProjectId)));
        Assert.Equal(manager.Id, Assert.Single(fixture.AuthorizationChanges.Items.Select(change => change.UserId)));
        Assert.Equal(1, fixture.CommandUnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.GenericSaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.ClearCount);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }
}
