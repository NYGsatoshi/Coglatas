using Coglatas.Application.Projects;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Projects;

public sealed partial class ProjectServiceTests
{
    [Theory]
    [InlineData("missing-task", "Task not found.")]
    [InlineData("deleted-task", "Task not found.")]
    [InlineData("anonymous", "You are not allowed to assign this task.")]
    [InlineData("outsider", "You are not allowed to assign this task.")]
    [InlineData("negative-estimate", "Estimated hours cannot be negative.")]
    [InlineData("unsupported-role", "TASK_ASSIGNMENT_ROLE_UNSUPPORTED|Legacy Owner assignments are historical and cannot be created.")]
    [InlineData("duplicate-role", "User already has this assignment role.")]
    [InlineData("revoked-target", "TASK_FORBIDDEN|The assignment user is not available for this Task.")]
    public async Task AssignmentAddGuardPreservesExistingRelationshipsAndEffects(string scenario, string expectedError)
    {
        var fixture = ProjectFixture.Create();
        var (manager, target, task) = PrepareCompatibilityTarget(fixture);
        SeedAssignmentGuardSentinel(fixture, manager, target);
        var taskId = task.Id;
        var role = TaskAssignmentRole.Assignee;
        decimal? estimate = 2;
        switch (scenario)
        {
            case "missing-task": taskId = Guid.NewGuid(); break;
            case "deleted-task": task.MarkDeleted(fixture.Clock.UtcNow, manager.Id, "Synthetic retained deletion"); break;
            case "anonymous": fixture.Current.UserIdValue = null; break;
            case "outsider": fixture.Current.UserIdValue = fixture.AddUser().Id; break;
            case "negative-estimate": estimate = -1; break;
            case "unsupported-role": role = (TaskAssignmentRole)(-1); break;
            case "duplicate-role": fixture.AddLegacyAssignment(task, target, role, manager.Id); break;
            case "revoked-target": RevokeWorkspaceMember(fixture, target.Id); break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        var before = CaptureAssignmentGuardState(fixture);

        var result = await fixture.Service.AddAssignmentAsync(taskId, new AddTaskAssignmentRequest(target.Id, role, estimate));

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedError, result.Error);
        AssertAssignmentGuardStateUnchanged(fixture, before);
    }

    [Theory]
    [InlineData("missing-assignment", "Assignment not found.")]
    [InlineData("missing-parent", "Assignment not found.")]
    [InlineData("deleted-parent", "Assignment not found.")]
    [InlineData("anonymous", "You are not allowed to assign this task.")]
    [InlineData("outsider", "You are not allowed to assign this task.")]
    [InlineData("negative-estimate", "Hours cannot be negative.")]
    [InlineData("negative-actual", "Hours cannot be negative.")]
    [InlineData("duplicate-role", "User already has this assignment role.")]
    [InlineData("unsupported-owner", "TASK_ASSIGNMENT_ROLE_UNSUPPORTED|Legacy Owner assignments are historical and cannot be created.")]
    [InlineData("unsupported-enum", "TASK_ASSIGNMENT_ROLE_UNSUPPORTED|Legacy Owner assignments are historical and cannot be created.")]
    [InlineData("revoked-target", "TASK_FORBIDDEN|The assignment user is not available for this Task.")]
    public async Task AssignmentUpdateGuardPreservesExistingRelationshipsAndEffects(string scenario, string expectedError)
    {
        var fixture = ProjectFixture.Create();
        var (manager, target, task) = PrepareCompatibilityTarget(fixture);
        task.PrimaryAssigneeUserId = target.Id;
        var assignment = fixture.AddLegacyAssignment(task, target, TaskAssignmentRole.Assignee, manager.Id);
        SeedAssignmentGuardSentinel(fixture, manager, target);
        var assignmentId = assignment.Id;
        var role = TaskAssignmentRole.Reviewer;
        decimal? estimate = 2;
        decimal? actual = 1;
        switch (scenario)
        {
            case "missing-assignment": assignmentId = Guid.NewGuid(); break;
            case "missing-parent": fixture.Projects.Tasks.Remove(task.Id); assignment.TaskItem = null; break;
            case "deleted-parent": task.MarkDeleted(fixture.Clock.UtcNow, manager.Id, "Synthetic retained deletion"); break;
            case "anonymous": fixture.Current.UserIdValue = null; break;
            case "outsider": fixture.Current.UserIdValue = fixture.AddUser().Id; break;
            case "negative-estimate": estimate = -1; break;
            case "negative-actual": actual = -1; break;
            case "duplicate-role": fixture.AddLegacyAssignment(task, target, role, manager.Id); break;
            case "unsupported-owner": role = TaskAssignmentRole.Owner; break;
            case "unsupported-enum": role = (TaskAssignmentRole)(-1); break;
            case "revoked-target": RevokeWorkspaceMember(fixture, target.Id); break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        var before = CaptureAssignmentGuardState(fixture);

        var result = await fixture.Service.UpdateAssignmentAsync(assignmentId, new UpdateTaskAssignmentRequest(role, estimate, actual));

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedError, result.Error);
        AssertAssignmentGuardStateUnchanged(fixture, before);
    }

    [Theory]
    [InlineData("missing-assignment", "Assignment not found.")]
    [InlineData("anonymous", "You are not allowed to assign this task.")]
    [InlineData("outsider", "You are not allowed to assign this task.")]
    [InlineData("deleted-parent", "You are not allowed to assign this task.")]
    public async Task AssignmentDeleteGuardPreservesExistingRelationshipsAndEffects(string scenario, string expectedError)
    {
        var fixture = ProjectFixture.Create();
        var (manager, target, task) = PrepareCompatibilityTarget(fixture);
        task.PrimaryAssigneeUserId = target.Id;
        var assignment = fixture.AddLegacyAssignment(task, target, TaskAssignmentRole.Assignee, manager.Id);
        SeedAssignmentGuardSentinel(fixture, manager, target);
        var assignmentId = assignment.Id;
        switch (scenario)
        {
            case "missing-assignment": assignmentId = Guid.NewGuid(); break;
            case "anonymous": fixture.Current.UserIdValue = null; break;
            case "outsider": fixture.Current.UserIdValue = fixture.AddUser().Id; break;
            case "deleted-parent": task.MarkDeleted(fixture.Clock.UtcNow, manager.Id, "Synthetic retained deletion"); break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        var before = CaptureAssignmentGuardState(fixture);

        var result = await fixture.Service.DeleteAssignmentAsync(assignmentId);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedError, result.Error);
        AssertAssignmentGuardStateUnchanged(fixture, before);
    }

    [Theory]
    [InlineData(TaskAssignmentRole.Owner)]
    [InlineData(TaskAssignmentRole.Assignee)]
    [InlineData(TaskAssignmentRole.Reviewer)]
    [InlineData(TaskAssignmentRole.Support)]
    public async Task UnchangedAssignmentHoursReturnOriginalIdentityWithoutEffects(TaskAssignmentRole role)
    {
        var fixture = ProjectFixture.Create();
        var (manager, target, task) = PrepareCompatibilityTarget(fixture);
        var assignment = fixture.AddLegacyAssignment(task, target, role, manager.Id);
        assignment.EstimatedHours = 2;
        assignment.ActualHours = 1;
        SetAssignmentGuardCanonicalRelationship(fixture, task, target, manager, role);
        SeedAssignmentGuardSentinel(fixture, manager, target);
        var assignmentId = assignment.Id;
        var taskId = task.Id;
        var targetId = target.Id;
        var managerId = manager.Id;
        var displayName = target.DisplayName;
        var assignedAt = fixture.Clock.UtcNow;
        var before = CaptureAssignmentGuardState(fixture);

        var result = await fixture.Service.UpdateAssignmentAsync(assignmentId, new UpdateTaskAssignmentRequest(role, 2, 1));

        Assert.True(result.IsSuccess, result.Error);
        var dto = Assert.IsType<TaskAssignmentResponse>(result.Value);
        Assert.Equal(assignmentId, dto.Id);
        Assert.Equal(taskId, dto.TaskItemId);
        Assert.Equal(targetId, dto.UserId);
        Assert.Equal(managerId, dto.AssignedByUserId);
        Assert.Equal(displayName, dto.DisplayName);
        Assert.Equal(assignedAt, dto.AssignedAt);
        Assert.Equal(role, dto.Role);
        Assert.Equal(2m, dto.EstimatedHours);
        Assert.Equal(1m, dto.ActualHours);
        AssertAssignmentGuardStateUnchanged(fixture, before);
    }

    [Theory]
    [InlineData(TaskAssignmentRole.Assignee)]
    [InlineData(TaskAssignmentRole.Reviewer)]
    [InlineData(TaskAssignmentRole.Support)]
    public async Task UnmappedSameRoleHoursChangePreservesRelationshipsBeforeRefusal(TaskAssignmentRole role)
    {
        var fixture = ProjectFixture.Create();
        var (manager, target, task) = PrepareCompatibilityTarget(fixture);
        var assignment = fixture.AddLegacyAssignment(task, target, role, manager.Id);
        SeedAssignmentGuardSentinel(fixture, manager, target);
        var before = CaptureAssignmentGuardState(fixture);

        var result = await fixture.Service.UpdateAssignmentAsync(assignment.Id, new UpdateTaskAssignmentRequest(role, 2, 1));

        Assert.False(result.IsSuccess);
        Assert.StartsWith("TASK_ASSIGNMENT_AMBIGUOUS|", result.Error);
        AssertAssignmentGuardStateUnchanged(fixture, before);
    }

    [Theory]
    [InlineData(TaskAssignmentRole.Assignee, TaskAssignmentRole.Support)]
    [InlineData(TaskAssignmentRole.Reviewer, TaskAssignmentRole.Support)]
    [InlineData(TaskAssignmentRole.Support, TaskAssignmentRole.Assignee)]
    public async Task UnmappedRoleChangePreservesRelationshipsBeforeRefusal(TaskAssignmentRole previousRole, TaskAssignmentRole nextRole)
    {
        var fixture = ProjectFixture.Create();
        var (manager, target, task) = PrepareCompatibilityTarget(fixture);
        var assignment = fixture.AddLegacyAssignment(task, target, previousRole, manager.Id);
        SeedAssignmentGuardSentinel(fixture, manager, target);
        var before = CaptureAssignmentGuardState(fixture);

        var result = await fixture.Service.UpdateAssignmentAsync(assignment.Id, new UpdateTaskAssignmentRequest(nextRole, 2, 1));

        Assert.False(result.IsSuccess);
        Assert.StartsWith("TASK_ASSIGNMENT_AMBIGUOUS|", result.Error);
        AssertAssignmentGuardStateUnchanged(fixture, before);
    }

    [Theory]
    [InlineData(TaskAssignmentRole.Assignee)]
    [InlineData(TaskAssignmentRole.Reviewer)]
    public async Task OccupiedCanonicalAssignmentRejectsAnotherTargetWithoutEffects(TaskAssignmentRole role)
    {
        var fixture = ProjectFixture.Create();
        var (manager, target, task) = PrepareCompatibilityTarget(fixture);
        var other = fixture.AddUser();
        fixture.AddProjectMember(other.Id, ProjectRole.Contributor);
        SetAssignmentGuardCanonicalRelationship(fixture, task, other, manager, role);
        SeedAssignmentGuardSentinel(fixture, manager, target);
        var before = CaptureAssignmentGuardState(fixture);

        var result = await fixture.Service.AddAssignmentAsync(task.Id, new AddTaskAssignmentRequest(target.Id, role, 2));

        Assert.False(result.IsSuccess);
        Assert.StartsWith("TASK_ALREADY_ASSIGNED|", result.Error);
        AssertAssignmentGuardStateUnchanged(fixture, before);
    }

    [Theory]
    [InlineData(TaskAssignmentRole.Assignee)]
    [InlineData(TaskAssignmentRole.Reviewer)]
    public async Task AnotherLegacySingletonPreventsAmbiguousAssignmentAdd(TaskAssignmentRole role)
    {
        var fixture = ProjectFixture.Create();
        var (manager, target, task) = PrepareCompatibilityTarget(fixture);
        var other = fixture.AddUser();
        fixture.AddProjectMember(other.Id, ProjectRole.Contributor);
        fixture.AddLegacyAssignment(task, other, role, manager.Id);
        SeedAssignmentGuardSentinel(fixture, manager, target);
        var before = CaptureAssignmentGuardState(fixture);

        var result = await fixture.Service.AddAssignmentAsync(task.Id, new AddTaskAssignmentRequest(target.Id, role, 2));

        Assert.False(result.IsSuccess);
        Assert.StartsWith("TASK_ASSIGNMENT_AMBIGUOUS|", result.Error);
        AssertAssignmentGuardStateUnchanged(fixture, before);
    }

    [Theory]
    [InlineData(TaskAssignmentRole.Assignee)]
    [InlineData(TaskAssignmentRole.Reviewer)]
    public async Task MultipleLegacySingletonsPreventAmbiguousAssignmentDelete(TaskAssignmentRole role)
    {
        var fixture = ProjectFixture.Create();
        var (manager, target, task) = PrepareCompatibilityTarget(fixture);
        var other = fixture.AddUser();
        fixture.AddProjectMember(other.Id, ProjectRole.Contributor);
        var assignment = fixture.AddLegacyAssignment(task, target, role, manager.Id);
        fixture.AddLegacyAssignment(task, other, role, manager.Id);
        SetAssignmentGuardCanonicalRelationship(fixture, task, target, manager, role);
        SeedAssignmentGuardSentinel(fixture, manager, target);
        var before = CaptureAssignmentGuardState(fixture);

        var result = await fixture.Service.DeleteAssignmentAsync(assignment.Id);

        Assert.False(result.IsSuccess);
        Assert.StartsWith("TASK_ASSIGNMENT_AMBIGUOUS|", result.Error);
        AssertAssignmentGuardStateUnchanged(fixture, before);
    }

    private static void SeedAssignmentGuardSentinel(ProjectFixture fixture, User manager, User target)
    {
        var unrelated = fixture.AddTask("Unrelated retained assignment graph");
        fixture.AddLegacyAssignment(unrelated, target, TaskAssignmentRole.Support, manager.Id);
        SetAssignmentGuardCanonicalRelationship(fixture, unrelated, target, manager, TaskAssignmentRole.Support);
        fixture.Projects.Watches.Add(new WorkItemWatchState
        {
            TaskItemId = unrelated.Id, UserId = target.Id,
            AutomaticSources = WorkItemWatchAutomaticSource.Collaborator,
            IsManualWatch = true, IsExplicitOptOut = false, IsWatching = true,
            UpdatedAt = fixture.Clock.UtcNow, VersionNo = 3
        });
    }

    private static void SetAssignmentGuardCanonicalRelationship(ProjectFixture fixture, TaskItem task, User target, User manager, TaskAssignmentRole role)
    {
        if (role == TaskAssignmentRole.Assignee) task.PrimaryAssigneeUserId = target.Id;
        if (role == TaskAssignmentRole.Reviewer) task.ReviewerUserId = target.Id;
        if (role == TaskAssignmentRole.Support)
            fixture.Projects.Collaborators.Add(new WorkItemCollaborator
            {
                TaskItemId = task.Id, UserId = target.Id,
                AddedAt = fixture.Clock.UtcNow, AddedByUserId = manager.Id
            });
    }

    private static AssignmentGuardSnapshot CaptureAssignmentGuardState(ProjectFixture fixture) => new(
        fixture.Projects.Tasks.Values.OrderBy(row => row.Id).Select(row => (
            row.Id, row.TenantId, row.WorkspaceId, row.ProjectId, row.ParentTaskItemId,
            row.Title, row.PrimaryAssigneeUserId, row.ReviewerUserId, row.VersionNo,
            row.Status, row.ProgressPercent, row.CreatedAt, row.UpdatedAt,
            row.DeletedAt, row.DeletedByUserId, row.DeleteReason)).ToArray(),
        fixture.Projects.Assignments.OrderBy(row => row.Id).Select(row => (
            row.Id, row.TenantId, row.TaskItemId, row.UserId, row.Role,
            row.AssignedByUserId, row.EstimatedHours, row.ActualHours, row.AssignedAt)).ToArray(),
        fixture.Projects.Collaborators.OrderBy(row => row.Id).Select(row => (
            row.Id, row.TenantId, row.TaskItemId, row.UserId, row.AddedAt, row.AddedByUserId)).ToArray(),
        fixture.Projects.Watches.OrderBy(row => row.Id).Select(row => (
            row.Id, row.TenantId, row.TaskItemId, row.UserId, row.AutomaticSources,
            row.IsManualWatch, row.IsExplicitOptOut, row.IsWatching, row.UpdatedAt, row.VersionNo)).ToArray());

    private static void AssertAssignmentGuardStateUnchanged(ProjectFixture fixture, AssignmentGuardSnapshot before)
    {
        var after = CaptureAssignmentGuardState(fixture);
        Assert.Equal(before.Tasks, after.Tasks);
        Assert.Equal(before.Assignments, after.Assignments);
        Assert.Equal(before.Collaborators, after.Collaborators);
        Assert.Equal(before.Watches, after.Watches);
        Assert.Empty(fixture.Audit.Entries);
        Assert.Empty(fixture.TaskNotifications.Requests);
        Assert.Empty(fixture.AuthorizationChanges.Items);
        Assert.Empty(fixture.Invalidations.ProjectChanges);
        Assert.Empty(fixture.Invalidations.TaskAssignmentChanges);
        Assert.Equal(0, fixture.Invalidations.TaskChangedCount);
        Assert.Equal(0, fixture.Invalidations.TaskAssignmentChangedCount);
        Assert.Equal(0, fixture.Invalidations.ProjectChangedCount);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.SaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.GenericSaveCount);
        Assert.Equal(0, fixture.CommandUnitOfWork.ClearCount);
    }

    private sealed record AssignmentGuardSnapshot(
        (Guid Id, Guid TenantId, Guid WorkspaceId, Guid ProjectId, Guid? ParentTaskItemId,
        string Title, Guid? PrimaryAssigneeUserId, Guid? ReviewerUserId, long VersionNo, TaskItemStatus Status, int ProgressPercent,
        DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, DateTimeOffset? DeletedAt, Guid? DeletedByUserId, string? DeleteReason)[] Tasks,
        (Guid Id, Guid TenantId, Guid TaskItemId, Guid UserId, TaskAssignmentRole Role,
        Guid AssignedByUserId, decimal? EstimatedHours, decimal? ActualHours, DateTimeOffset AssignedAt)[] Assignments,
        (Guid Id, Guid TenantId, Guid TaskItemId, Guid UserId, DateTimeOffset AddedAt, Guid AddedByUserId)[] Collaborators,
        (Guid Id, Guid TenantId, Guid TaskItemId, Guid UserId, WorkItemWatchAutomaticSource AutomaticSources,
        bool IsManualWatch, bool IsExplicitOptOut, bool IsWatching, DateTimeOffset UpdatedAt, long VersionNo)[] Watches);
}
