using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Projects;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Projects;

public sealed partial class TaskCommandServiceTests
{
    [Theory]
    [InlineData("missing", "TASK_NOT_FOUND|Task not found.")]
    [InlineData("anonymous", "TASK_NOT_FOUND|Task not found.")]
    [InlineData("hidden", "TASK_NOT_FOUND|Task not found.")]
    [InlineData("deleted", "TASK_NOT_FOUND|Task not found.")]
    [InlineData("stale", "TASK_STALE_VERSION|Task has changed. Refetch and retry.")]
    [InlineData("zero-version", "TASK_STALE_VERSION|Task has changed. Refetch and retry.")]
    [InlineData("assigned", "TASK_ALREADY_ASSIGNED|Task already has a primary assignee.")]
    [InlineData("no-group", "TASK_CLAIM_NOT_ELIGIBLE|Task is not eligible for claim.")]
    [InlineData("in-progress", "TASK_CLAIM_NOT_ELIGIBLE|Task is not eligible for claim.")]
    [InlineData("review", "TASK_CLAIM_NOT_ELIGIBLE|Task is not eligible for claim.")]
    [InlineData("done", "TASK_CLAIM_NOT_ELIGIBLE|Task is not eligible for claim.")]
    [InlineData("cancelled", "TASK_CLAIM_NOT_ELIGIBLE|Task is not eligible for claim.")]
    [InlineData("group-member-missing", "TASK_CLAIM_GROUP_MEMBERSHIP_REQUIRED|Current group and project membership are required.")]
    public async Task ClaimRefusalsPreserveAllStoredTaskWatchAndCollaboratorRows(string scenario, string error)
    {
        var fixture = Fixture.Create();
        var task = SeedClaimTask(fixture, TaskStageCategory.Backlog);
        var groupId = task.TargetGroupId!.Value;
        var groups = new ClaimGroups(groupId, fixture.Actor, scenario != "group-member-missing");
        var requestedId = task.Id;
        var expectedVersion = task.VersionNo;
        switch (scenario)
        {
            case "missing": fixture.Projects.Tasks.Remove(task.Id); break;
            case "deleted": task.MarkDeleted(new FixedClock().UtcNow.AddDays(-2), fixture.Actor, "Synthetic deletion"); break;
            case "stale": expectedVersion--; break;
            case "zero-version": expectedVersion = 0; break;
            case "assigned": task.PrimaryAssigneeUserId = Guid.NewGuid(); break;
            case "no-group": task.TargetGroupId = null; break;
            case "in-progress": task.WorkflowStage!.InternalCategory = TaskStageCategory.InProgress; break;
            case "review": task.WorkflowStage!.InternalCategory = TaskStageCategory.Review; break;
            case "done": task.WorkflowStage!.InternalCategory = TaskStageCategory.Done; break;
            case "cancelled": task.WorkflowStage!.InternalCategory = TaskStageCategory.Cancelled; break;
        }
        var before = CaptureClaimGraph(fixture);
        var service = CreateClaimService(fixture, groups, scenario);

        var result = await service.ClaimAsync(requestedId, new TaskClaimRequest(expectedVersion));

        Assert.False(result.IsSuccess);
        Assert.Equal(error, result.Error);
        Assert.Null(result.Value);
        AssertClaimGraph(before, fixture);
        Assert.Empty(fixture.Audit.Entries);
        Assert.Empty(fixture.Notifications.Requests);
        Assert.Empty(fixture.Invalidations.TaskChanges);
        Assert.Empty(fixture.Invalidations.TaskAssignmentChanges);
        Assert.Empty(fixture.Invalidations.ProjectChanges);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Equal(0, fixture.UnitOfWork.ClearCount);
        if (scenario == "group-member-missing")
            Assert.Equal((groupId, fixture.Actor), Assert.Single(groups.Reads));
        else
            Assert.Empty(groups.Reads);
    }

    [Theory]
    [InlineData(TaskStageCategory.Backlog, false, false, true)]
    [InlineData(TaskStageCategory.Backlog, false, true, false)]
    [InlineData(TaskStageCategory.Backlog, true, true, true)]
    [InlineData(TaskStageCategory.Todo, false, false, true)]
    [InlineData(TaskStageCategory.Todo, false, true, false)]
    [InlineData(TaskStageCategory.Todo, true, true, true)]
    public async Task ClaimRetainsExistingWatchIdentityAndExplicitIntent(
        TaskStageCategory category, bool manual, bool optedOut, bool watching)
    {
        var fixture = Fixture.Create();
        var task = SeedClaimTask(fixture, category);
        var watch = new WorkItemWatchState
        {
            TenantId = task.TenantId, TaskItemId = task.Id, UserId = fixture.Actor,
            AutomaticSources = WorkItemWatchAutomaticSource.Creator, IsManualWatch = manual,
            IsExplicitOptOut = optedOut, IsWatching = watching,
            UpdatedAt = new FixedClock().UtcNow.AddDays(-3), VersionNo = 5
        };
        fixture.Projects.Watches.Add(watch);
        var before = CaptureClaimGraph(fixture);
        var taskId = task.Id;
        var version = task.VersionNo;
        var watchId = watch.Id;
        var groupId = task.TargetGroupId!.Value;
        var groups = new ClaimGroups(groupId, fixture.Actor, true);

        var result = await CreateClaimService(fixture, groups).ClaimAsync(taskId, new TaskClaimRequest(version));

        before.Tasks[taskId].Values[nameof(TaskItem.PrimaryAssigneeUserId)] = fixture.Actor;
        before.Tasks[taskId].Values[nameof(TaskItem.VersionNo)] = version + 1;
        before.Watches[watchId].Values[nameof(WorkItemWatchState.AutomaticSources)] =
            WorkItemWatchAutomaticSource.Creator | WorkItemWatchAutomaticSource.PrimaryAssignee;
        before.Watches[watchId].Values[nameof(WorkItemWatchState.UpdatedAt)] = new FixedClock().UtcNow;
        before.Watches[watchId].Values[nameof(WorkItemWatchState.VersionNo)] = 6L;
        AssertClaimGraph(before, fixture);
        AssertClaimEffects(fixture, task, taskId, version, result);
        Assert.Equal((groupId, fixture.Actor), Assert.Single(groups.Reads));
    }

    [Theory]
    [InlineData(TaskStageCategory.Backlog)]
    [InlineData(TaskStageCategory.Todo)]
    public async Task ClaimCreatesOneCombinedAutomaticWatchAndRejectsARepeatedClaim(TaskStageCategory category)
    {
        var fixture = Fixture.Create();
        var task = SeedClaimTask(fixture, category);
        var taskId = task.Id;
        var version = task.VersionNo;
        var before = CaptureClaimGraph(fixture);
        var groupId = task.TargetGroupId!.Value;
        var groups = new ClaimGroups(groupId, fixture.Actor, true);
        var service = CreateClaimService(fixture, groups);

        var result = await service.ClaimAsync(taskId, new TaskClaimRequest(version));

        var watch = Assert.Single(fixture.Projects.Watches, row => row.TaskItemId == taskId);
        Assert.NotEqual(Guid.Empty, watch.Id);
        Assert.Equal(Guid.Empty, watch.TenantId);
        Assert.Equal(fixture.Actor, watch.UserId);
        Assert.Equal(WorkItemWatchAutomaticSource.Creator | WorkItemWatchAutomaticSource.PrimaryAssignee, watch.AutomaticSources);
        Assert.False(watch.IsManualWatch);
        Assert.False(watch.IsExplicitOptOut);
        Assert.True(watch.IsWatching);
        Assert.Equal(new FixedClock().UtcNow, watch.UpdatedAt);
        Assert.Equal(1, watch.VersionNo);
        before.Tasks[taskId].Values[nameof(TaskItem.PrimaryAssigneeUserId)] = fixture.Actor;
        before.Tasks[taskId].Values[nameof(TaskItem.VersionNo)] = version + 1;
        before.Watches.Add(watch.Id, (watch, ClaimWatchScalars(watch)));
        AssertClaimGraph(before, fixture);
        AssertClaimEffects(fixture, task, taskId, version, result);
        Assert.Equal((groupId, fixture.Actor), Assert.Single(groups.Reads));
        var afterFirstClaim = CaptureClaimGraph(fixture);
        var auditBeforeRepeat = Assert.Single(fixture.Audit.Entries);
        var notificationBeforeRepeat = Assert.Single(fixture.Notifications.Requests);

        var repeated = await service.ClaimAsync(taskId, new TaskClaimRequest(version + 1));

        Assert.False(repeated.IsSuccess);
        Assert.Equal("TASK_ALREADY_ASSIGNED|Task already has a primary assignee.", repeated.Error);
        AssertClaimGraph(afterFirstClaim, fixture);
        Assert.Single(fixture.Audit.Entries);
        Assert.Single(fixture.Notifications.Requests);
        Assert.Single(fixture.Invalidations.TaskChanges);
        Assert.Single(fixture.Invalidations.TaskAssignmentChanges);
        Assert.Empty(fixture.Invalidations.ProjectChanges);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
        Assert.Equal(0, fixture.UnitOfWork.ClearCount);
        Assert.Same(auditBeforeRepeat, Assert.Single(fixture.Audit.Entries));
        Assert.Same(notificationBeforeRepeat, Assert.Single(fixture.Notifications.Requests));
        AssertClaimEffects(fixture, task, taskId, version, result);
        Assert.Equal((groupId, fixture.Actor), Assert.Single(groups.Reads));
    }

    [Theory]
    [InlineData(TaskStageCategory.Backlog)]
    [InlineData(TaskStageCategory.Todo)]
    public async Task ClaimAssignsTheCurrentActorWhenTheTaskCreatorIsAnotherUser(TaskStageCategory category)
    {
        var fixture = Fixture.Create();
        var task = SeedClaimTask(fixture, category);
        var creatorId = Guid.NewGuid();
        task.CreatedByUserId = creatorId;
        Assert.NotEqual(creatorId, fixture.Actor);
        var taskId = task.Id;
        var groupId = task.TargetGroupId!.Value;
        var version = task.VersionNo;
        var before = CaptureClaimGraph(fixture);
        var groups = new ClaimGroups(groupId, fixture.Actor, true);

        var result = await CreateClaimService(fixture, groups).ClaimAsync(taskId, new TaskClaimRequest(version));

        Assert.True(result.IsSuccess);
        Assert.Equal(taskId, result.Value!.Task.Id);
        Assert.Equal(version + 1, result.Value.Task.Version);
        Assert.Equal(fixture.Actor, result.Value.Task.PrimaryAssignee!.UserId);
        Assert.NotEqual(creatorId, result.Value.Task.PrimaryAssignee.UserId);
        before.Tasks[taskId].Values[nameof(TaskItem.PrimaryAssigneeUserId)] = fixture.Actor;
        before.Tasks[taskId].Values[nameof(TaskItem.VersionNo)] = version + 1;
        var watches = fixture.Projects.Watches.Where(row => row.TaskItemId == taskId).ToArray();
        Assert.Equal(new[] { creatorId, fixture.Actor }.Order(), watches.Select(row => row.UserId).Order());
        foreach (var (userId, source) in new[]
        {
            (creatorId, WorkItemWatchAutomaticSource.Creator),
            (fixture.Actor, WorkItemWatchAutomaticSource.PrimaryAssignee)
        })
        {
            var watch = Assert.Single(watches, row => row.UserId == userId);
            Assert.NotEqual(Guid.Empty, watch.Id);
            Assert.Equal(Guid.Empty, watch.TenantId);
            Assert.Equal(taskId, watch.TaskItemId);
            Assert.Equal(source, watch.AutomaticSources);
            Assert.False(watch.IsManualWatch);
            Assert.False(watch.IsExplicitOptOut);
            Assert.True(watch.IsWatching);
            Assert.Equal(new FixedClock().UtcNow, watch.UpdatedAt);
            Assert.Equal(1, watch.VersionNo);
            before.Watches.Add(watch.Id, (watch, ClaimWatchScalars(watch)));
        }
        AssertClaimGraph(before, fixture);
        var audit = Assert.Single(fixture.Audit.Entries);
        Assert.Equal(fixture.Actor, audit.ActorUserId);
        Assert.Equal(taskId, audit.EntityId);
        Assert.Equal("TaskClaimed", audit.Action);
        Assert.Equal(version, Assert.IsType<long>(audit.Metadata!["versionBefore"]));
        var changed = Assert.Single(fixture.Invalidations.TaskChanges);
        Assert.Equal(taskId, changed.TaskId);
        Assert.Equal("claimed", changed.Change);
        Assert.Equal(new[] { "primaryAssigneeUserId" }, changed.ChangedFields);
        Assert.Equal(new[] { creatorId, fixture.Actor }, changed.AffectedUserIds);
        var assigned = Assert.Single(fixture.Invalidations.TaskAssignmentChanges);
        Assert.Equal(taskId, assigned.TaskId);
        Assert.Equal("assigneeChanged", assigned.Change);
        Assert.Equal(new[] { creatorId, fixture.Actor }, assigned.AffectedUserIds);
        var notification = Assert.Single(fixture.Notifications.Requests);
        Assert.Same(task, notification.Task);
        Assert.Equal(TaskNotificationEventKind.PrimaryAssigneeChanged, notification.EventKind);
        Assert.Equal(fixture.Actor, notification.ActorUserId);
        Assert.Equal(fixture.Actor, notification.NewPrimaryAssigneeUserId);
        Assert.Null(notification.PreviousPrimaryAssigneeUserId);
        Assert.Empty(fixture.Invalidations.ProjectChanges);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
        Assert.Equal(0, fixture.UnitOfWork.ClearCount);
        Assert.Equal((groupId, fixture.Actor), Assert.Single(groups.Reads));
    }

    private static void AssertClaimEffects(Fixture fixture, TaskItem task, Guid taskId, long previousVersion,
        Coglatas.Application.Common.Result<TaskCommandResponse> result)
    {
        Assert.True(result.IsSuccess);
        Assert.Equal(taskId, result.Value!.Task.Id);
        Assert.Equal(previousVersion + 1, result.Value.Task.Version);
        Assert.Equal(fixture.Actor, result.Value.Task.PrimaryAssignee!.UserId);
        Assert.Equal(task.TargetGroupId, result.Value.Task.TargetGroupId);
        Assert.Empty(result.Value.Warnings);
        Assert.False(result.Value.OverrideApplied);
        var audit = Assert.Single(fixture.Audit.Entries);
        Assert.Equal(fixture.Actor, audit.ActorUserId);
        Assert.Equal("TaskClaimed", audit.Action);
        Assert.Equal("TaskItem", audit.EntityType);
        Assert.Equal(taskId, audit.EntityId);
        Assert.Equal(task.WorkspaceId, audit.WorkspaceId);
        Assert.Equal(task.ProjectId, audit.ProjectId);
        Assert.Equal(previousVersion, Assert.IsType<long>(audit.Metadata!["versionBefore"]));
        Assert.False(Assert.IsType<bool>(audit.Metadata["reasonProvided"]));
        Assert.Equal(2, audit.Metadata.Count);
        var changed = Assert.Single(fixture.Invalidations.TaskChanges);
        Assert.Equal(taskId, changed.TaskId);
        Assert.Equal("claimed", changed.Change);
        Assert.Equal(new[] { "primaryAssigneeUserId" }, changed.ChangedFields);
        Assert.Equal(new[] { fixture.Actor }, changed.AffectedUserIds);
        var assigned = Assert.Single(fixture.Invalidations.TaskAssignmentChanges);
        Assert.Equal(taskId, assigned.TaskId);
        Assert.Equal("assigneeChanged", assigned.Change);
        Assert.Equal(new[] { fixture.Actor }, assigned.AffectedUserIds);
        var notification = Assert.Single(fixture.Notifications.Requests);
        Assert.Same(task, notification.Task);
        Assert.Equal(TaskNotificationEventKind.PrimaryAssigneeChanged, notification.EventKind);
        Assert.Equal(fixture.Actor, notification.ActorUserId);
        Assert.Equal(fixture.Actor, notification.NewPrimaryAssigneeUserId);
        Assert.Null(notification.PreviousPrimaryAssigneeUserId);
        Assert.Null(notification.NewReviewerUserId);
        Assert.Empty(fixture.Invalidations.ProjectChanges);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
        Assert.Equal(0, fixture.UnitOfWork.ClearCount);
    }

    private static TaskCommandService CreateClaimService(Fixture fixture, ClaimGroups groups, string scenario = "") =>
        new(fixture.Projects, groups, new ClaimUsers(fixture.Actor),
            scenario == "hidden" ? new DeniedProjectAuthorization() : new AllowedProjectAuthorization(),
            new AllowedTaskAuthorization(), new ClaimCurrentUser(fixture.Actor, scenario != "anonymous"),
            new FixedClock(), fixture.Audit, fixture.Invalidations, fixture.UnitOfWork,
            new UtcTimeZoneResolver(), taskNotifications: fixture.Notifications, relationshipTargets: fixture.RelationshipTargets);

    private static TaskItem SeedClaimTask(Fixture fixture, TaskStageCategory category)
    {
        var task = fixture.AddTask("Synthetic claim target", progress: 17);
        task.TenantId = Guid.NewGuid();
        task.TargetGroupId = Guid.NewGuid();
        task.VersionNo = 8;
        task.Description = "Retained claim description";
        task.BriefGoal = "Retained goal";
        task.Priority = TaskPriority.High;
        task.CreatedAt = new FixedClock().UtcNow.AddDays(-10);
        task.UpdatedAt = new FixedClock().UtcNow.AddDays(-5);
        task.WorkflowStage = new() { ProjectId = task.ProjectId, WorkspaceId = task.WorkspaceId, Name = "Synthetic claim stage", InternalCategory = category };
        task.WorkflowStageId = task.WorkflowStage.Id;
        fixture.Projects.Stages.Add(task.WorkflowStage.Id, task.WorkflowStage);
        var unrelated = fixture.AddTask("Unrelated retained task");
        unrelated.MarkDeleted(new FixedClock().UtcNow.AddDays(-4), Guid.NewGuid(), "Unrelated synthetic deletion");
        fixture.Projects.Watches.Add(new() { TenantId = task.TenantId, TaskItemId = unrelated.Id, UserId = Guid.NewGuid(), AutomaticSources = WorkItemWatchAutomaticSource.Reviewer, IsWatching = true, UpdatedAt = new FixedClock().UtcNow.AddDays(-8), VersionNo = 11 });
        fixture.Projects.Collaborators.Add(new() { TenantId = task.TenantId, TaskItemId = unrelated.Id, UserId = Guid.NewGuid(), AddedByUserId = fixture.Actor, AddedAt = new FixedClock().UtcNow.AddDays(-9) });
        return task;
    }

    private sealed record ClaimGraph(
        Dictionary<Guid, (TaskItem Entity, Dictionary<string, object?> Values)> Tasks,
        Dictionary<Guid, (WorkItemWatchState Entity, Dictionary<string, object?> Values)> Watches,
        Dictionary<Guid, (WorkItemCollaborator Entity, Dictionary<string, object?> Values)> Collaborators);

    private static ClaimGraph CaptureClaimGraph(Fixture fixture) => new(
        fixture.Projects.Tasks.ToDictionary(pair => pair.Key, pair => (pair.Value, ClaimTaskScalars(pair.Value))),
        fixture.Projects.Watches.ToDictionary(row => row.Id, row => (row, ClaimWatchScalars(row))),
        fixture.Projects.Collaborators.ToDictionary(row => row.Id, row => (row, ClaimCollaboratorScalars(row))));

    private static void AssertClaimGraph(ClaimGraph expected, Fixture fixture)
    {
        Assert.Equal(expected.Tasks.Keys.Order(), fixture.Projects.Tasks.Keys.Order());
        foreach (var (id, row) in expected.Tasks)
        {
            Assert.Same(row.Entity, fixture.Projects.Tasks[id]);
            AssertClaimScalars(row.Values, ClaimTaskScalars(fixture.Projects.Tasks[id]));
        }
        Assert.Equal(expected.Watches.Keys.Order(), fixture.Projects.Watches.Select(row => row.Id).Order());
        foreach (var (id, row) in expected.Watches)
        {
            var actual = Assert.Single(fixture.Projects.Watches, value => value.Id == id);
            Assert.Same(row.Entity, actual);
            AssertClaimScalars(row.Values, ClaimWatchScalars(actual));
        }
        Assert.Equal(expected.Collaborators.Keys.Order(), fixture.Projects.Collaborators.Select(row => row.Id).Order());
        foreach (var (id, row) in expected.Collaborators)
        {
            var actual = Assert.Single(fixture.Projects.Collaborators, value => value.Id == id);
            Assert.Same(row.Entity, actual);
            AssertClaimScalars(row.Values, ClaimCollaboratorScalars(actual));
        }
    }

    private static void AssertClaimScalars(Dictionary<string, object?> expected, Dictionary<string, object?> actual)
    {
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach (var (field, value) in expected) Assert.Equal(value, actual[field]);
    }

    private static Dictionary<string, object?> ClaimTaskScalars(TaskItem row) => new()
    {
        [nameof(row.Id)] = row.Id, [nameof(row.TenantId)] = row.TenantId,
        [nameof(row.WorkspaceId)] = row.WorkspaceId, [nameof(row.ProjectId)] = row.ProjectId,
        [nameof(row.MilestoneId)] = row.MilestoneId, [nameof(row.ParentTaskItemId)] = row.ParentTaskItemId,
        [nameof(row.WorkflowStageId)] = row.WorkflowStageId, [nameof(row.Kind)] = row.Kind,
        [nameof(row.Title)] = row.Title, [nameof(row.Description)] = row.Description,
        [nameof(row.BriefGoal)] = row.BriefGoal, [nameof(row.BriefDeliverable)] = row.BriefDeliverable,
        [nameof(row.BriefConstraints)] = row.BriefConstraints, [nameof(row.Status)] = row.Status,
        [nameof(row.Priority)] = row.Priority, [nameof(row.StartDate)] = row.StartDate,
        [nameof(row.DueDate)] = row.DueDate, [nameof(row.IsBlocked)] = row.IsBlocked,
        [nameof(row.BlockedReason)] = row.BlockedReason, [nameof(row.TargetGroupId)] = row.TargetGroupId,
        [nameof(row.PrimaryAssigneeUserId)] = row.PrimaryAssigneeUserId, [nameof(row.ReviewerUserId)] = row.ReviewerUserId,
        [nameof(row.PlannedStartDate)] = row.PlannedStartDate, [nameof(row.PlannedEndDate)] = row.PlannedEndDate,
        [nameof(row.DeadlineAt)] = row.DeadlineAt, [nameof(row.ActualStartAt)] = row.ActualStartAt,
        [nameof(row.CompletedAt)] = row.CompletedAt, [nameof(row.EstimatedEffortMinutes)] = row.EstimatedEffortMinutes,
        [nameof(row.CancelledAt)] = row.CancelledAt, [nameof(row.CancellationReason)] = row.CancellationReason,
        [nameof(row.ReviewStatus)] = row.ReviewStatus, [nameof(row.ReviewSubmittedAt)] = row.ReviewSubmittedAt,
        [nameof(row.ReviewResolvedAt)] = row.ReviewResolvedAt, [nameof(row.ReviewResolvedByUserId)] = row.ReviewResolvedByUserId,
        [nameof(row.ReviewReturnReason)] = row.ReviewReturnReason, [nameof(row.SortKey)] = row.SortKey,
        [nameof(row.VersionNo)] = row.VersionNo, [nameof(row.ProgressPercent)] = row.ProgressPercent,
        [nameof(row.SortOrder)] = row.SortOrder, [nameof(row.CreatedByUserId)] = row.CreatedByUserId,
        [nameof(row.CreatedAt)] = row.CreatedAt, [nameof(row.UpdatedAt)] = row.UpdatedAt,
        [nameof(row.DeletedAt)] = row.DeletedAt, [nameof(row.DeletedByUserId)] = row.DeletedByUserId,
        [nameof(row.DeleteReason)] = row.DeleteReason, [nameof(row.IsDeleted)] = row.IsDeleted
    };

    private static Dictionary<string, object?> ClaimWatchScalars(WorkItemWatchState row) => new()
    {
        [nameof(row.Id)] = row.Id, [nameof(row.TenantId)] = row.TenantId,
        [nameof(row.TaskItemId)] = row.TaskItemId, [nameof(row.UserId)] = row.UserId,
        [nameof(row.AutomaticSources)] = row.AutomaticSources, [nameof(row.IsManualWatch)] = row.IsManualWatch,
        [nameof(row.IsExplicitOptOut)] = row.IsExplicitOptOut, [nameof(row.IsWatching)] = row.IsWatching,
        [nameof(row.UpdatedAt)] = row.UpdatedAt, [nameof(row.VersionNo)] = row.VersionNo
    };

    private static Dictionary<string, object?> ClaimCollaboratorScalars(WorkItemCollaborator row) => new()
    {
        [nameof(row.Id)] = row.Id, [nameof(row.TenantId)] = row.TenantId,
        [nameof(row.TaskItemId)] = row.TaskItemId, [nameof(row.UserId)] = row.UserId,
        [nameof(row.AddedAt)] = row.AddedAt, [nameof(row.AddedByUserId)] = row.AddedByUserId
    };

    private sealed class ClaimGroups(Guid groupId, Guid actor, bool member) : IGroupRepository
    {
        public List<(Guid Group, Guid Actor)> Reads { get; } = [];
        public Task<GroupMember?> GetMemberAsync(Guid group, Guid user, CancellationToken ct = default)
        {
            Reads.Add((group, user));
            return Task.FromResult<GroupMember?>(member && group == groupId && user == actor ? new GroupMember { GroupId = group, UserId = user } : null);
        }
        public Task<IReadOnlyList<Group>> ListByWorkspaceAsync(Guid workspace, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Group>>([]);
        public Task<IReadOnlyList<Group>> ListManagedByUserAsync(Guid workspace, Guid user, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Group>>([]);
        public Task<Group?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Group?>(null);
        public Task<IReadOnlyList<GroupMember>> ListMembersAsync(Guid id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GroupMember>>([]);
        public Task AddAsync(Group group, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddMemberAsync(GroupMember row, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ClaimUsers(Guid actor) : IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<User?>(id == actor ? new User { Id = actor, DisplayName = "Synthetic claimer", Email = "claimer@example.test" } : null);
        public Task<User?> GetByNormalizedEmailAsync(string email, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task AddAsync(User user, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ClaimCurrentUser(Guid actor, bool authenticated) : ICurrentUser
    {
        public Guid? UserId => actor;
        public Guid? SessionId => null;
        public string? Email => null;
        public SystemRole? SystemRole => null;
        public bool IsAuthenticated => authenticated;
    }
}
