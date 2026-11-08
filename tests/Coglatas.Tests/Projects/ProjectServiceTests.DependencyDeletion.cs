using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Projects;

public sealed partial class ProjectServiceTests
{
    [Theory]
    [InlineData(ProjectRole.Owner)]
    [InlineData(ProjectRole.Manager)]
    public async Task DependencyDeletionRemovesOnlyTheSelectedEdgeAndPreservesTaskDates(ProjectRole role)
    {
        var fixture = ProjectFixture.Create();
        var actor = fixture.AddUser();
        fixture.Current.UserIdValue = actor.Id;
        fixture.AddProjectMember(actor.Id, role);
        var predecessor = fixture.AddTask("Predecessor");
        predecessor.VersionNo = 3;
        predecessor.PlannedStartDate = new DateOnly(2026, 8, 1);
        predecessor.PlannedEndDate = new DateOnly(2026, 8, 10);
        var successor = fixture.AddTask("Successor");
        successor.WorkspaceId = fixture.Workspace.Id;
        successor.VersionNo = 7;
        successor.PlannedStartDate = new DateOnly(2026, 8, 11);
        successor.PlannedEndDate = new DateOnly(2026, 8, 20);
        var other = fixture.AddTask("Other successor");
        other.VersionNo = 11;
        var dependency = new TaskDependency
        {
            ProjectId = fixture.Project.Id,
            PredecessorTaskItemId = predecessor.Id,
            SuccessorTaskItemId = successor.Id
        };
        var retained = new TaskDependency
        {
            ProjectId = fixture.Project.Id,
            PredecessorTaskItemId = predecessor.Id,
            SuccessorTaskItemId = other.Id
        };
        fixture.Dependencies.AddRange([dependency, retained]);
        var predecessorDates = (predecessor.PlannedStartDate, predecessor.PlannedEndDate);
        var successorDates = (successor.PlannedStartDate, successor.PlannedEndDate);

        var result = await fixture.Service.DeleteDependencyAsync(successor.Id, dependency.Id, 7);

        Assert.True(result.IsSuccess, result.Error);
        Assert.DoesNotContain(dependency, fixture.Dependencies);
        Assert.Same(retained, Assert.Single(fixture.Dependencies));
        Assert.Equal(3, fixture.Projects.Tasks.Count);
        Assert.Same(predecessor, fixture.Projects.Tasks[predecessor.Id]);
        Assert.Same(successor, fixture.Projects.Tasks[successor.Id]);
        Assert.Same(other, fixture.Projects.Tasks[other.Id]);
        Assert.Equal(3, predecessor.VersionNo);
        Assert.Equal(8, successor.VersionNo);
        Assert.Equal(11, other.VersionNo);
        Assert.Null(predecessor.DeletedAt);
        Assert.Null(successor.DeletedAt);
        Assert.Null(other.DeletedAt);
        Assert.Equal(predecessorDates, (predecessor.PlannedStartDate, predecessor.PlannedEndDate));
        Assert.Equal(successorDates, (successor.PlannedStartDate, successor.PlannedEndDate));
        Assert.Equal(1, fixture.CommandUnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.ClearCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.GenericSaveCount);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        var audit = Assert.Single(fixture.Audit.Entries);
        Assert.Equal("TaskDependencyRemoved", audit.Action);
        Assert.Equal("TaskDependency", audit.EntityType);
        Assert.Equal(dependency.Id, audit.EntityId);
        Assert.Equal(actor.Id, audit.ActorUserId);
        Assert.Equal(fixture.Workspace.Id, audit.WorkspaceId);
        Assert.Equal(fixture.Project.Id, audit.ProjectId);
        var metadata = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(audit.Metadata);
        Assert.Equal(3, metadata.Count);
        Assert.Equal(predecessor.Id, metadata["predecessorTaskId"]);
        Assert.Equal(successor.Id, metadata["successorTaskId"]);
        Assert.Equal(7L, metadata["versionBefore"]);
        Assert.Equal((fixture.Project.Id, actor.Id, "dependencyChanged"), Assert.Single(fixture.Invalidations.ProjectChanges));
        Assert.Equal(1, fixture.Invalidations.TaskChangedCount);
        Assert.Empty(fixture.AuthorizationChanges.Items);
        Assert.Empty(fixture.TaskNotifications.Requests);
    }

    [Theory]
    [InlineData("anonymous", "TASK_DEPENDENCY_AUTHENTICATION_REQUIRED")]
    [InlineData("empty-actor", "TASK_DEPENDENCY_NOT_FOUND")]
    [InlineData("unknown-task", "TASK_DEPENDENCY_NOT_FOUND")]
    [InlineData("deleted-task", "TASK_DEPENDENCY_NOT_FOUND")]
    [InlineData("legacy-task-kind", "TASK_DEPENDENCY_NOT_FOUND")]
    [InlineData("unknown-project", "TASK_DEPENDENCY_NOT_FOUND")]
    [InlineData("archived-project", "TASK_DEPENDENCY_NOT_FOUND")]
    [InlineData("deleted-project", "TASK_DEPENDENCY_NOT_FOUND")]
    [InlineData("soft-deleted-project", "TASK_DEPENDENCY_NOT_FOUND")]
    [InlineData("pending-workspace", "TASK_DEPENDENCY_NOT_FOUND")]
    [InlineData("suspended-workspace", "TASK_DEPENDENCY_NOT_FOUND")]
    public async Task DependencyDeletionDeniesUnreadableTargetsWithoutMutationOrAudit(string scenario, string code)
    {
        var fixture = ProjectFixture.Create();
        var actor = fixture.AddUser();
        fixture.Current.UserIdValue = actor.Id;
        fixture.AddProjectMember(actor.Id, ProjectRole.Manager);
        var predecessor = fixture.AddTask("Retain predecessor");
        var successor = fixture.AddTask("Retain successor");
        successor.VersionNo = 7;
        var dependency = new TaskDependency
        {
            ProjectId = fixture.Project.Id,
            PredecessorTaskItemId = predecessor.Id,
            SuccessorTaskItemId = successor.Id
        };
        fixture.Dependencies.Add(dependency);
        var targetId = successor.Id;
        switch (scenario)
        {
            case "anonymous": fixture.Current.UserIdValue = null; break;
            case "empty-actor": fixture.Current.UserIdValue = Guid.Empty; break;
            case "unknown-task": targetId = Guid.NewGuid(); break;
            case "deleted-task": successor.MarkDeleted(fixture.Clock.UtcNow); break;
            case "legacy-task-kind": successor.Kind = WorkItemKind.Milestone; break;
            case "unknown-project": successor.ProjectId = Guid.NewGuid(); break;
            case "archived-project": fixture.Project.Status = ProjectStatus.Archived; break;
            case "deleted-project": fixture.Project.Status = ProjectStatus.Deleted; break;
            case "soft-deleted-project": fixture.Project.MarkDeleted(fixture.Clock.UtcNow); break;
            case "pending-workspace": fixture.Workspaces.Members.Single().Status = MembershipStatus.Pending; break;
            case "suspended-workspace": fixture.Workspaces.Members.Single().Status = MembershipStatus.Suspended; break;
        }
        var expectedRows = fixture.Projects.Tasks.ToDictionary(pair => pair.Key, pair => pair.Value);
        var expectedTask = (successor.ProjectId, successor.Kind, successor.DeletedAt);

        var result = await fixture.Service.DeleteDependencyAsync(targetId, dependency.Id, 7);

        Assert.False(result.IsSuccess);
        Assert.StartsWith(code + "|", result.Error);
        Assert.Same(dependency, Assert.Single(fixture.Dependencies));
        Assert.Equal(expectedRows.Count, fixture.Projects.Tasks.Count);
        foreach (var row in expectedRows)
            Assert.Same(row.Value, fixture.Projects.Tasks[row.Key]);
        Assert.Equal(expectedTask, (successor.ProjectId, successor.Kind, successor.DeletedAt));
        Assert.Equal(7, successor.VersionNo);
        Assert.Equal("Retain successor", successor.Title);
        Assert.Equal(1, predecessor.VersionNo);
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
    [InlineData("viewer", "TASK_DEPENDENCY_FORBIDDEN")]
    [InlineData("workspace-readonly", "TASK_DEPENDENCY_FORBIDDEN")]
    [InlineData("zero-version", "TASK_DEPENDENCY_INVALID_EXPECTED_VERSION")]
    [InlineData("negative-version", "TASK_DEPENDENCY_INVALID_EXPECTED_VERSION")]
    [InlineData("stale-version", "TASK_STALE_VERSION")]
    [InlineData("missing-edge", "TASK_DEPENDENCY_NOT_FOUND")]
    [InlineData("foreign-project-edge", "TASK_DEPENDENCY_NOT_FOUND")]
    [InlineData("other-successor-edge", "TASK_DEPENDENCY_NOT_FOUND")]
    [InlineData("legacy-edge-type", "TASK_DEPENDENCY_LEGACY_READ_ONLY")]
    [InlineData("combined-item-overflow", "GANTT_ITEM_LIMIT_EXCEEDED")]
    public async Task DependencyDeletionRefusalKeepsTheGraphAndStagesOnlyRedactedAudit(string scenario, string code)
    {
        var fixture = ProjectFixture.Create();
        var actor = fixture.AddUser();
        fixture.Current.UserIdValue = actor.Id;
        fixture.AddProjectMember(actor.Id, ProjectRole.Manager);
        var predecessor = fixture.AddTask("Retain predecessor");
        var successor = fixture.AddTask("Retain successor");
        successor.WorkspaceId = fixture.Workspace.Id;
        successor.VersionNo = 7;
        var other = fixture.AddTask("Other successor");
        var dependency = new TaskDependency
        {
            ProjectId = fixture.Project.Id,
            PredecessorTaskItemId = predecessor.Id,
            SuccessorTaskItemId = successor.Id
        };
        fixture.Dependencies.Add(dependency);
        var dependencyId = dependency.Id;
        long version = 7;
        switch (scenario)
        {
            case "viewer": fixture.Projects.Members.Single().Role = ProjectRole.Viewer; break;
            case "workspace-readonly": fixture.Workspaces.Members.Single().Role = WorkspaceRole.ReadOnly; break;
            case "zero-version": version = 0; break;
            case "negative-version": version = -1; break;
            case "stale-version": version = 6; break;
            case "missing-edge": dependencyId = Guid.NewGuid(); break;
            case "foreign-project-edge": dependency.ProjectId = Guid.NewGuid(); break;
            case "other-successor-edge": dependency.SuccessorTaskItemId = other.Id; break;
            case "legacy-edge-type": dependency.DependencyType = TaskDependencyType.StartToStart; break;
            case "combined-item-overflow":
                for (var index = 0; index < 500; index++) fixture.AddMilestone("Retain milestone", index);
                break;
        }
        var expectedEdge = (dependency.ProjectId, dependency.PredecessorTaskItemId, dependency.SuccessorTaskItemId, dependency.DependencyType);
        var expectedMilestones = fixture.Projects.Milestones.ToDictionary(pair => pair.Key, pair => pair.Value);

        var result = await fixture.Service.DeleteDependencyAsync(successor.Id, dependencyId, version);

        Assert.False(result.IsSuccess);
        Assert.StartsWith(code + "|", result.Error);
        Assert.Same(dependency, Assert.Single(fixture.Dependencies));
        Assert.Equal(expectedEdge, (dependency.ProjectId, dependency.PredecessorTaskItemId, dependency.SuccessorTaskItemId, dependency.DependencyType));
        Assert.Equal(3, fixture.Projects.Tasks.Count);
        Assert.Same(predecessor, fixture.Projects.Tasks[predecessor.Id]);
        Assert.Same(successor, fixture.Projects.Tasks[successor.Id]);
        Assert.Same(other, fixture.Projects.Tasks[other.Id]);
        Assert.Equal(7, successor.VersionNo);
        Assert.Null(successor.DeletedAt);
        Assert.Equal(expectedMilestones.Count, fixture.Projects.Milestones.Count);
        foreach (var row in expectedMilestones)
            Assert.Same(row.Value, fixture.Projects.Milestones[row.Key]);
        Assert.Equal(0, fixture.CommandUnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.ClearCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.GenericSaveCount);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
        var audit = Assert.Single(fixture.Audit.Entries);
        Assert.Equal("TaskDependencyMutationRejected", audit.Action);
        Assert.Equal("TaskDependency", audit.EntityType);
        Assert.Null(audit.EntityId);
        Assert.Equal(actor.Id, audit.ActorUserId);
        Assert.Equal(fixture.Workspace.Id, audit.WorkspaceId);
        Assert.Equal(fixture.Project.Id, audit.ProjectId);
        var metadata = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(audit.Metadata);
        Assert.Equal(code, Assert.Single(metadata).Value);
        Assert.Equal("reasonCode", Assert.Single(metadata).Key);
        Assert.Empty(fixture.Invalidations.ProjectChanges);
        Assert.Equal(0, fixture.Invalidations.TaskChangedCount);
        Assert.Empty(fixture.AuthorizationChanges.Items);
        Assert.Empty(fixture.TaskNotifications.Requests);
    }

    [Theory]
    [InlineData(TaskCommandSaveResult.UniqueConflict, "TASK_DEPENDENCY_CONFLICT")]
    [InlineData(TaskCommandSaveResult.ConcurrencyConflict, "TASK_STALE_VERSION")]
    public async Task DependencyDeletionSaveConflictReportsTheOwnedErrorAndRedactedRejection(TaskCommandSaveResult outcome, string code)
    {
        var fixture = ProjectFixture.Create();
        var actor = fixture.AddUser();
        fixture.Current.UserIdValue = actor.Id;
        fixture.AddProjectMember(actor.Id, ProjectRole.Manager);
        var predecessor = fixture.AddTask("Predecessor");
        var successor = fixture.AddTask("Successor");
        successor.WorkspaceId = fixture.Workspace.Id;
        successor.VersionNo = 7;
        var dependency = new TaskDependency
        {
            ProjectId = fixture.Project.Id,
            PredecessorTaskItemId = predecessor.Id,
            SuccessorTaskItemId = successor.Id
        };
        fixture.Dependencies.Add(dependency);
        fixture.CommandUnitOfWork.Outcome = new(outcome);

        var result = await fixture.Service.DeleteDependencyAsync(successor.Id, dependency.Id, 7);

        Assert.False(result.IsSuccess);
        Assert.StartsWith(code + "|", result.Error);
        Assert.Equal(1, fixture.CommandUnitOfWork.SaveCount);
        Assert.Equal(1, fixture.CommandUnitOfWork.ClearCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.GenericSaveCount);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
        Assert.Empty(fixture.Dependencies);
        Assert.Equal(8, successor.VersionNo);
        Assert.Equal(2, fixture.Projects.Tasks.Count);
        Assert.Same(predecessor, fixture.Projects.Tasks[predecessor.Id]);
        Assert.Same(successor, fixture.Projects.Tasks[successor.Id]);
        Assert.Equal(2, fixture.Audit.Entries.Count);
        Assert.Equal("TaskDependencyRemoved", fixture.Audit.Entries[0].Action);
        var rejection = fixture.Audit.Entries[1];
        Assert.Equal("TaskDependencyMutationRejected", rejection.Action);
        Assert.Null(rejection.EntityId);
        var metadata = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(rejection.Metadata);
        Assert.Equal("reasonCode", Assert.Single(metadata).Key);
        Assert.Equal(code, Assert.Single(metadata).Value);
        Assert.Equal((fixture.Project.Id, actor.Id, "dependencyChanged"), Assert.Single(fixture.Invalidations.ProjectChanges));
        Assert.Equal(1, fixture.Invalidations.TaskChangedCount);
        Assert.Empty(fixture.AuthorizationChanges.Items);
        Assert.Empty(fixture.TaskNotifications.Requests);
    }
}
