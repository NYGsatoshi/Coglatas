using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Projects;

public sealed partial class ProjectServiceTests
{
    [Theory]
    [InlineData(ProjectRole.Owner, WorkspaceRole.Member, true)]
    [InlineData(ProjectRole.Manager, WorkspaceRole.Member, true)]
    [InlineData(ProjectRole.Viewer, WorkspaceRole.Member, false)]
    [InlineData(ProjectRole.Manager, WorkspaceRole.ReadOnly, false)]
    public async Task DependencyListingProjectsCurrentVersionsAndReadOnlyWarningsWithoutMutation(
        ProjectRole projectRole, WorkspaceRole workspaceRole, bool editable)
    {
        var fixture = ProjectFixture.Create();
        var actor = fixture.AddUser(workspaceRole: workspaceRole);
        fixture.Current.UserIdValue = actor.Id;
        fixture.AddProjectMember(actor.Id, projectRole);
        var predecessor = fixture.AddTask("Predecessor");
        predecessor.VersionNo = 3;
        predecessor.PlannedStartDate = new DateOnly(2026, 8, 1);
        predecessor.PlannedEndDate = new DateOnly(2026, 8, 10);
        var successor = fixture.AddTask("Successor");
        successor.VersionNo = 7;
        successor.PlannedStartDate = new DateOnly(2026, 8, 5);
        successor.PlannedEndDate = new DateOnly(2026, 8, 8);
        foreach (var task in fixture.Projects.Tasks.Values)
            task.WorkspaceId = fixture.Workspace.Id;
        var canonical = new TaskDependency
        {
            ProjectId = fixture.Project.Id,
            PredecessorTaskItemId = predecessor.Id,
            SuccessorTaskItemId = successor.Id
        };
        var legacy = new TaskDependency
        {
            ProjectId = fixture.Project.Id,
            PredecessorTaskItemId = predecessor.Id,
            SuccessorTaskItemId = successor.Id,
            DependencyType = TaskDependencyType.StartToStart
        };
        fixture.Dependencies.AddRange([legacy, canonical]);
        var dates = (predecessor.PlannedStartDate, predecessor.PlannedEndDate, successor.PlannedStartDate, successor.PlannedEndDate);

        var result = await fixture.Service.ListDependenciesAsync(predecessor.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Value!.Count);
        var current = Assert.Single(result.Value, item => item.Id == canonical.Id);
        Assert.Equal(predecessor.Id, current.PredecessorTaskId);
        Assert.Equal(successor.Id, current.SuccessorTaskId);
        Assert.Equal(TaskDependencyType.FinishToStart, current.DependencyType);
        Assert.Equal(canonical.CreatedAt, current.CreatedAt);
        Assert.Equal(7, current.Version);
        Assert.Equal(editable, current.Editable);
        Assert.Contains(current.Warnings, warning => warning.Code == "DEPENDENCY_VIOLATION" && !warning.Blocking);
        var readOnly = Assert.Single(result.Value, item => item.Id == legacy.Id);
        Assert.Equal(TaskDependencyType.StartToStart, readOnly.DependencyType);
        Assert.Equal(7, readOnly.Version);
        Assert.False(readOnly.Editable);
        Assert.Contains(readOnly.Warnings, warning => warning.Code == "LEGACY_DEPENDENCY_TYPE" && !warning.Blocking);
        Assert.Equal(dates, (predecessor.PlannedStartDate, predecessor.PlannedEndDate, successor.PlannedStartDate, successor.PlannedEndDate));
        Assert.Equal(3, predecessor.VersionNo);
        Assert.Equal(7, successor.VersionNo);
        Assert.Same(legacy, fixture.Dependencies[0]);
        Assert.Same(canonical, fixture.Dependencies[1]);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.GenericSaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.ClearCount);
        Assert.Empty(fixture.Audit.Entries);
        Assert.Empty(fixture.Invalidations.ProjectChanges);
        Assert.Equal(0, fixture.Invalidations.TaskChangedCount);
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
    public async Task DependencyListingDeniesUnreadableTargetsWithoutReturningAPartialGraph(string scenario, string code)
    {
        var fixture = ProjectFixture.Create();
        var actor = fixture.AddUser();
        fixture.Current.UserIdValue = actor.Id;
        fixture.AddProjectMember(actor.Id, ProjectRole.Manager);
        var predecessor = fixture.AddTask("Retain predecessor");
        var successor = fixture.AddTask("Retain successor");
        successor.VersionNo = 7;
        foreach (var task in fixture.Projects.Tasks.Values)
            task.WorkspaceId = fixture.Workspace.Id;
        var edge = new TaskDependency
        {
            ProjectId = fixture.Project.Id,
            PredecessorTaskItemId = predecessor.Id,
            SuccessorTaskItemId = successor.Id
        };
        fixture.Dependencies.Add(edge);
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
        var identity = (successor.ProjectId, successor.Kind, successor.DeletedAt);
        var edgeFields = (edge.ProjectId, edge.PredecessorTaskItemId, edge.SuccessorTaskItemId, edge.DependencyType);

        var result = await fixture.Service.ListDependenciesAsync(targetId);

        Assert.False(result.IsSuccess);
        Assert.StartsWith(code + "|", result.Error);
        Assert.Null(result.Value);
        Assert.Equal(identity, (successor.ProjectId, successor.Kind, successor.DeletedAt));
        Assert.Equal(7, successor.VersionNo);
        Assert.Equal(edgeFields, (edge.ProjectId, edge.PredecessorTaskItemId, edge.SuccessorTaskItemId, edge.DependencyType));
        Assert.Same(edge, Assert.Single(fixture.Dependencies));
        Assert.Equal(2, fixture.Projects.Tasks.Count);
        Assert.Same(predecessor, fixture.Projects.Tasks[predecessor.Id]);
        Assert.Same(successor, fixture.Projects.Tasks[successor.Id]);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.GenericSaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.ClearCount);
        Assert.Empty(fixture.Audit.Entries);
        Assert.Empty(fixture.Invalidations.ProjectChanges);
        Assert.Equal(0, fixture.Invalidations.TaskChangedCount);
        Assert.Empty(fixture.AuthorizationChanges.Items);
        Assert.Empty(fixture.TaskNotifications.Requests);
    }

    [Theory]
    [InlineData(true, "deleted-neighbor")]
    [InlineData(false, "deleted-neighbor")]
    [InlineData(true, "missing-neighbor")]
    [InlineData(false, "missing-neighbor")]
    [InlineData(true, "foreign-project-neighbor")]
    [InlineData(false, "foreign-project-neighbor")]
    [InlineData(true, "foreign-project-edge")]
    [InlineData(false, "foreign-project-edge")]
    public async Task DependencyListingOmitsUnmappedEdgesAndPreservesTheStoredGraph(bool incoming, string scenario)
    {
        var fixture = ProjectFixture.Create();
        var actor = fixture.AddUser();
        fixture.Current.UserIdValue = actor.Id;
        fixture.AddProjectMember(actor.Id, ProjectRole.Viewer);
        var target = fixture.AddTask("Target");
        var neighbor = fixture.AddTask("Unmapped neighbor");
        var control = fixture.AddTask("Visible neighbor");
        foreach (var task in fixture.Projects.Tasks.Values)
            task.WorkspaceId = fixture.Workspace.Id;
        var visible = new TaskDependency
        {
            ProjectId = fixture.Project.Id,
            PredecessorTaskItemId = control.Id,
            SuccessorTaskItemId = target.Id
        };
        var excluded = new TaskDependency
        {
            ProjectId = fixture.Project.Id,
            PredecessorTaskItemId = incoming ? neighbor.Id : target.Id,
            SuccessorTaskItemId = incoming ? target.Id : neighbor.Id
        };
        fixture.Dependencies.AddRange([excluded, visible]);
        switch (scenario)
        {
            case "deleted-neighbor": neighbor.MarkDeleted(fixture.Clock.UtcNow); break;
            case "missing-neighbor": fixture.Projects.Tasks.Remove(neighbor.Id); break;
            case "foreign-project-neighbor": neighbor.ProjectId = Guid.NewGuid(); break;
            case "foreign-project-edge": excluded.ProjectId = Guid.NewGuid(); break;
        }
        var storedTasks = fixture.Projects.Tasks.ToDictionary(pair => pair.Key, pair => pair.Value);
        var excludedFields = (excluded.ProjectId, excluded.PredecessorTaskItemId, excluded.SuccessorTaskItemId, excluded.DependencyType);
        var visibleFields = (visible.ProjectId, visible.PredecessorTaskItemId, visible.SuccessorTaskItemId, visible.DependencyType);

        var result = await fixture.Service.ListDependenciesAsync(target.Id);

        Assert.True(result.IsSuccess, result.Error);
        var returned = Assert.Single(result.Value!);
        Assert.Equal(visible.Id, returned.Id);
        Assert.Equal(control.Id, returned.PredecessorTaskId);
        Assert.Equal(target.Id, returned.SuccessorTaskId);
        Assert.False(returned.Editable);
        Assert.Equal(2, fixture.Dependencies.Count);
        Assert.Same(excluded, fixture.Dependencies[0]);
        Assert.Same(visible, fixture.Dependencies[1]);
        Assert.Equal(excludedFields, (excluded.ProjectId, excluded.PredecessorTaskItemId, excluded.SuccessorTaskItemId, excluded.DependencyType));
        Assert.Equal(visibleFields, (visible.ProjectId, visible.PredecessorTaskItemId, visible.SuccessorTaskItemId, visible.DependencyType));
        Assert.Equal(storedTasks.Count, fixture.Projects.Tasks.Count);
        foreach (var row in storedTasks)
            Assert.Same(row.Value, fixture.Projects.Tasks[row.Key]);
        Assert.Equal(1, target.VersionNo);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.GenericSaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.ClearCount);
        Assert.Empty(fixture.Audit.Entries);
        Assert.Empty(fixture.Invalidations.ProjectChanges);
        Assert.Equal(0, fixture.Invalidations.TaskChangedCount);
        Assert.Empty(fixture.AuthorizationChanges.Items);
        Assert.Empty(fixture.TaskNotifications.Requests);
    }

    [Fact]
    public async Task DependencyListingRejectsTheExistingCombinedItemOverflowWithoutAPartialResult()
    {
        var fixture = ProjectFixture.Create();
        var actor = fixture.AddUser();
        fixture.Current.UserIdValue = actor.Id;
        fixture.AddProjectMember(actor.Id, ProjectRole.Viewer);
        var target = fixture.AddTask("Retain target");
        target.WorkspaceId = fixture.Workspace.Id;
        for (var index = 0; index < 500; index++) fixture.AddMilestone("Retain milestone", index);

        var result = await fixture.Service.ListDependenciesAsync(target.Id);

        Assert.False(result.IsSuccess);
        Assert.StartsWith("GANTT_ITEM_LIMIT_EXCEEDED|", result.Error);
        Assert.Null(result.Value);
        Assert.Same(target, Assert.Single(fixture.Projects.Tasks).Value);
        Assert.Equal(1, target.VersionNo);
        Assert.Equal(500, fixture.Projects.Milestones.Count);
        Assert.Empty(fixture.Dependencies);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.GenericSaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.ClearCount);
        Assert.Empty(fixture.Audit.Entries);
        Assert.Empty(fixture.Invalidations.ProjectChanges);
        Assert.Equal(0, fixture.Invalidations.TaskChangedCount);
        Assert.Empty(fixture.AuthorizationChanges.Items);
        Assert.Empty(fixture.TaskNotifications.Requests);
    }
}
