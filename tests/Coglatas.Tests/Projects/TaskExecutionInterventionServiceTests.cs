using System.Text.Json;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Projects;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Projects;

public sealed class TaskExecutionInterventionServiceTests
{
    [Theory]
    [InlineData(false, "anonymous")]
    [InlineData(true, "anonymous")]
    [InlineData(false, "missing-actor")]
    [InlineData(true, "missing-actor")]
    [InlineData(false, "empty-task")]
    [InlineData(true, "empty-task")]
    [InlineData(false, "missing-task")]
    [InlineData(true, "missing-task")]
    [InlineData(false, "denied")]
    [InlineData(true, "denied")]
    [InlineData(false, "deleted")]
    [InlineData(true, "deleted")]
    [InlineData(false, "foreign-tenant")]
    [InlineData(true, "foreign-tenant")]
    [InlineData(false, "empty-run")]
    [InlineData(true, "empty-run")]
    public async Task Unavailable_task_authority_returns_the_same_safe_not_found_without_writes(bool correct, string boundary)
    {
        await using var fixture = await Fixture.CreateAsync();
        var taskId = fixture.Task.Id;
        var runId = fixture.Run.Id;
        if (boundary == "anonymous") fixture.Actor.IsAuthenticated = false;
        if (boundary == "missing-actor") fixture.Actor.UserId = null;
        if (boundary == "empty-task") taskId = Guid.Empty;
        if (boundary == "missing-task") taskId = Guid.NewGuid();
        if (boundary == "denied") fixture.Authorization.CanManage = false;
        if (boundary == "empty-run") runId = Guid.Empty;
        if (boundary == "deleted")
        {
            fixture.Task.MarkDeleted(fixture.Clock.UtcNow);
            await fixture.Db.SaveChangesAsync();
        }
        if (boundary == "foreign-tenant") fixture.TenantContext.SetTenant(Guid.NewGuid(), "other-tenant");
        var taskVersion = fixture.Task.VersionNo;

        var result = await fixture.InvokeAsync(correct, taskId, runId);

        Assert.False(result.IsSuccess);
        Assert.Equal("TASK_EXECUTION_NOT_FOUND", result.ErrorDetail?.Code);
        Assert.Equal(TaskExecutionRunStatus.Running, fixture.Run.Status);
        Assert.Equal(7, fixture.Run.VersionNo);
        Assert.Equal(taskVersion, fixture.Task.VersionNo);
        Assert.Empty(fixture.Audit.Entries);
        Assert.Equal(0, fixture.Save.Calls);
        Assert.Empty(await fixture.Db.OutboxEvents.ToListAsync());
    }

    [Theory]
    [InlineData(false, "missing")]
    [InlineData(true, "missing")]
    [InlineData(false, "task")]
    [InlineData(true, "task")]
    [InlineData(false, "workspace")]
    [InlineData(true, "workspace")]
    [InlineData(false, "project")]
    [InlineData(true, "project")]
    [InlineData(false, "tenant")]
    [InlineData(true, "tenant")]
    public async Task A_run_cannot_be_rebound_to_a_different_task_scope(bool correct, string mismatch)
    {
        await using var fixture = await Fixture.CreateAsync();
        var runId = fixture.Run.Id;
        if (mismatch == "missing") runId = Guid.NewGuid();
        if (mismatch == "task") fixture.Run.TaskItemId = Guid.NewGuid();
        if (mismatch == "workspace") fixture.Run.WorkspaceId = Guid.NewGuid();
        if (mismatch == "project") fixture.Run.ProjectId = Guid.NewGuid();
        if (mismatch == "tenant") fixture.Run.TenantId = Guid.NewGuid();

        var result = await fixture.InvokeAsync(correct, fixture.Task.Id, runId);

        Assert.False(result.IsSuccess);
        Assert.Equal("TASK_EXECUTION_NOT_FOUND", result.ErrorDetail?.Code);
        Assert.Equal(TaskExecutionRunStatus.Running, fixture.Run.Status);
        Assert.Empty(fixture.Audit.Entries);
        Assert.Equal(0, fixture.Save.Calls);
        Assert.Empty(await fixture.Db.OutboxEvents.ToListAsync());
    }

    [Theory]
    [InlineData(false, TaskExecutionRunStatus.Succeeded)]
    [InlineData(true, TaskExecutionRunStatus.Succeeded)]
    [InlineData(false, TaskExecutionRunStatus.Failed)]
    [InlineData(true, TaskExecutionRunStatus.Failed)]
    [InlineData(false, TaskExecutionRunStatus.Stopped)]
    [InlineData(true, TaskExecutionRunStatus.Stopped)]
    [InlineData(false, TaskExecutionRunStatus.Redirected)]
    [InlineData(true, TaskExecutionRunStatus.Redirected)]
    public async Task Terminal_runs_are_not_stopped_redirected_or_revived(bool correct, TaskExecutionRunStatus status)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Run.Status = status;
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.InvokeAsync(correct, fixture.Task.Id, fixture.Run.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("TASK_EXECUTION_INTERVENTION_NOT_AVAILABLE", result.ErrorDetail?.Code);
        Assert.Equal(status, fixture.Run.Status);
        Assert.Equal(7, fixture.Run.VersionNo);
        Assert.Equal(4, fixture.Task.VersionNo);
        Assert.Empty(fixture.Audit.Entries);
        Assert.Equal(0, fixture.Save.Calls);
    }

    [Theory]
    [InlineData(TaskExecutionRunStatus.Accepted)]
    [InlineData(TaskExecutionRunStatus.Queued)]
    [InlineData(TaskExecutionRunStatus.Running)]
    public async Task Stop_persists_the_closed_run_activity_and_invalidation_without_replacing_its_snapshot(TaskExecutionRunStatus status)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Run.Status = status;
        await fixture.Db.SaveChangesAsync();
        var originalSnapshot = Snapshot(fixture.Run);
        var originalDocument = await fixture.Scopes.GetSourcePolicyDocumentAsync(TaskExecutionSourcePolicyOwnerType.Run, fixture.Run.Id);
        var originalDocumentJson = JsonSerializer.Serialize(originalDocument);

        var result = await fixture.InvokeAsync(false, fixture.Task.Id, fixture.Run.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Stop", result.Value!.Action);
        Assert.Null(result.Value.ResumedRun);
        Assert.Equal("None", result.Value.ResumePoint);
        Assert.Empty(result.Value.EditableSurfaces);
        fixture.Db.ChangeTracker.Clear();
        var stored = await fixture.Db.TaskExecutionRuns.SingleAsync();
        Assert.Equal(TaskExecutionRunStatus.Stopped, stored.Status);
        Assert.Null(stored.FailureCode);
        Assert.Equal(fixture.Clock.UtcNow, stored.FinishedAtUtc);
        Assert.Equal(8, stored.VersionNo);
        Assert.Equal(originalSnapshot, Snapshot(stored));
        var observedDocument = Assert.Single(fixture.Save.StagedRunDocuments);
        Assert.Same(originalDocument, observedDocument);
        Assert.Equal(originalDocumentJson, JsonSerializer.Serialize(observedDocument));
        Assert.Equal(5, (await fixture.Db.TaskItems.SingleAsync()).VersionNo);
        Assert.Equal("TaskExecutionRunStopped", Assert.Single(fixture.Audit.Entries).Action);
        Assert.Equal(fixture.Actor.UserId, fixture.Audit.Entries[0].ActorUserId);
        Assert.Equal(fixture.Run.Id, fixture.Audit.Entries[0].EntityId);
        Assert.Equal(fixture.Task.Id, (await fixture.Db.Set<ActivityLog>().SingleAsync()).TaskItemId);
        await AssertInvalidationAsync(fixture, "executionRunStopped");
        Assert.Equal(1, fixture.Save.Calls);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task Correction_preserves_the_closed_snapshot_and_captures_current_policy_and_research_provenance(
        bool taskOverride, bool matchingDocument, bool researchPlan)
    {
        await using var fixture = await Fixture.CreateAsync();
        var originalSnapshot = Snapshot(fixture.Run);
        var originalDocument = await fixture.Scopes.GetSourcePolicyDocumentAsync(TaskExecutionSourcePolicyOwnerType.Run, fixture.Run.Id);
        var originalDocumentJson = JsonSerializer.Serialize(originalDocument);
        var currentPolicy = TaskExecutionSourcePolicyV2.FromLegacy(true, false) with
        {
            Web = TaskExecutionSourceState.Prioritize,
            Items = [new(TaskExecutionSourceKind.ProjectFile, TaskExecutionSourcePolicyV2.ProjectFileSourceId(Guid.NewGuid()), TaskExecutionSourceState.Exclude)]
        };
        var projectScope = await fixture.Db.ProjectExecutionScopes.SingleAsync();
        projectScope.WebEnabled = false;
        projectScope.ProjectFilesEnabled = true;
        projectScope.VersionNo = 3;
        projectScope.UpdatedByUserId = fixture.Actor.UserId!.Value;
        if (taskOverride)
            fixture.Db.TaskExecutionScopeOverrides.Add(new TaskExecutionScopeOverride
            {
                TenantId = fixture.Task.TenantId, WorkspaceId = fixture.Task.WorkspaceId,
                ProjectId = fixture.Task.ProjectId, TaskItemId = fixture.Task.Id,
                WebEnabled = false, ProjectFilesEnabled = true, VersionNo = 5,
                UpdatedByUserId = fixture.Actor.UserId.Value
            });
        var revision = researchPlan ? await fixture.AddResearchPlanAsync() : null;
        await fixture.Db.SaveChangesAsync();
        fixture.Scopes.StageSourcePolicyDocument(new TaskExecutionSourcePolicyDocument(
            taskOverride ? TaskExecutionSourcePolicyOwnerType.Task : TaskExecutionSourcePolicyOwnerType.Project,
            taskOverride ? fixture.Task.Id : fixture.Task.ProjectId,
            fixture.Task.TenantId, fixture.Task.WorkspaceId, fixture.Task.ProjectId,
            taskOverride ? fixture.Task.Id : null,
            taskOverride || matchingDocument ? 3 : 2,
            taskOverride ? matchingDocument ? 5 : 4 : null,
            currentPolicy));

        var result = await fixture.InvokeAsync(true, fixture.Task.Id, fixture.Run.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("CorrectDirection", result.Value!.Action);
        Assert.Equal("NewRunFromLatestTaskState", result.Value.ResumePoint);
        Assert.Equal(new[] { "Task brief", "Research plan", "Active source scope" }, result.Value.EditableSurfaces);
        Assert.NotNull(result.Value.ResumedRun);
        fixture.Db.ChangeTracker.Clear();
        var closed = await fixture.Db.TaskExecutionRuns.SingleAsync(run => run.Id == fixture.Run.Id);
        var successor = await fixture.Db.TaskExecutionRuns.SingleAsync(run => run.Id != fixture.Run.Id);
        Assert.Equal(TaskExecutionRunStatus.Redirected, closed.Status);
        Assert.Equal(originalSnapshot, Snapshot(closed));
        var closedDocument = Assert.Single(fixture.Save.StagedRunDocuments, item => item.OwnerId == closed.Id);
        Assert.Same(originalDocument, closedDocument);
        Assert.Equal(originalDocumentJson, JsonSerializer.Serialize(closedDocument));
        Assert.Equal(8, closed.VersionNo);
        Assert.Equal(fixture.Clock.UtcNow, closed.FinishedAtUtc);
        Assert.Equal(successor.Id, result.Value.ResumedRun!.Id);
        Assert.Equal(TaskExecutionRunStatus.Accepted, successor.Status);
        Assert.Equal(fixture.Actor.UserId, successor.RequestedByUserId);
        Assert.Equal(fixture.Task.Id, successor.TaskItemId);
        Assert.Equal(fixture.Task.TenantId, successor.TenantId);
        Assert.Equal(fixture.Task.WorkspaceId, successor.WorkspaceId);
        Assert.Equal(fixture.Task.ProjectId, successor.ProjectId);
        Assert.Equal(TaskExecutionRun.CurrentSnapshotSchemaVersion, successor.SnapshotSchemaVersion);
        Assert.Equal(taskOverride ? TaskExecutionScopeOrigin.TaskOverride : TaskExecutionScopeOrigin.ProjectDefault, successor.SnapshotScopeOrigin);
        Assert.Equal(3, successor.SnapshotProjectScopeVersion);
        Assert.Equal(taskOverride ? 5L : null, successor.SnapshotTaskOverrideVersion);
        Assert.Equal(matchingDocument, successor.SnapshotWebEnabled);
        Assert.Equal(!matchingDocument, successor.SnapshotProjectFilesEnabled);
        Assert.Equal(revision?.Id, successor.SnapshotResearchPlanRevisionId);
        Assert.Equal(revision?.RevisionNo, successor.SnapshotResearchPlanRevisionNo);
        var successorDocument = Assert.Single(fixture.Save.StagedRunDocuments, item => item.OwnerId == successor.Id);
        Assert.Equal(TaskExecutionSourcePolicyOwnerType.Run, successorDocument.OwnerType);
        Assert.Equal(successor.TenantId, successorDocument.TenantId);
        Assert.Equal(successor.WorkspaceId, successorDocument.WorkspaceId);
        Assert.Equal(successor.ProjectId, successorDocument.ProjectId);
        Assert.Equal(successor.TaskItemId, successorDocument.TaskItemId);
        Assert.Equal(3, successorDocument.ProjectScopeVersion);
        Assert.Equal(taskOverride ? 5L : null, successorDocument.TaskOverrideVersion);
        Assert.Equal(JsonSerializer.Serialize(matchingDocument ? currentPolicy : TaskExecutionSourcePolicyV2.FromLegacy(false, true)),
            JsonSerializer.Serialize(successorDocument.Policy));
        Assert.Null(successor.QueuedAtUtc);
        Assert.Null(successor.StartedAtUtc);
        Assert.Null(successor.FinishedAtUtc);
        Assert.Equal(5, (await fixture.Db.TaskItems.SingleAsync()).VersionNo);
        var audit = Assert.Single(fixture.Audit.Entries);
        Assert.Equal("TaskExecutionRunRedirected", audit.Action);
        Assert.Equal(fixture.Actor.UserId, audit.ActorUserId);
        Assert.Equal(closed.Id, audit.EntityId);
        Assert.Equal(fixture.Task.WorkspaceId, audit.WorkspaceId);
        Assert.Equal(fixture.Task.ProjectId, audit.ProjectId);
        Assert.Equal(successor.Id, audit.Metadata!["successorRunId"]);
        var activity = await fixture.Db.Set<ActivityLog>().SingleAsync();
        Assert.Equal(fixture.Task.TenantId, activity.TenantId);
        Assert.Equal(fixture.Task.ProjectId, activity.ProjectId);
        Assert.Equal(fixture.Task.Id, activity.TaskItemId);
        Assert.Equal(fixture.Actor.UserId, activity.AuthorUserId);
        Assert.Equal(fixture.Clock.UtcNow, activity.OccurredAt);
        await AssertInvalidationAsync(fixture, "executionRunRedirected");
        Assert.Equal(1, fixture.Save.Calls);
    }

    [Theory]
    [InlineData(false, TaskCommandSaveResult.ConcurrencyConflict)]
    [InlineData(true, TaskCommandSaveResult.ConcurrencyConflict)]
    [InlineData(false, TaskCommandSaveResult.UniqueConflict)]
    [InlineData(true, TaskCommandSaveResult.UniqueConflict)]
    public async Task Rejected_save_never_returns_a_successful_intervention(bool correct, TaskCommandSaveResult outcome)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Save.Outcome = outcome;

        var result = await fixture.InvokeAsync(correct, fixture.Task.Id, fixture.Run.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("TASK_EXECUTION_STALE_VERSION", result.ErrorDetail?.Code);
        Assert.Equal(1, fixture.Save.Calls);
        Assert.Equal(TaskExecutionRunStatus.Running, (await fixture.Db.TaskExecutionRuns.SingleAsync()).Status);
        Assert.Equal(4, (await fixture.Db.TaskItems.SingleAsync()).VersionNo);
        Assert.Empty(await fixture.Db.OutboxEvents.ToListAsync());
        Assert.Empty(await fixture.Db.Set<ActivityLog>().ToListAsync());
    }

    private static object Snapshot(TaskExecutionRun run) => new
    {
        run.SnapshotSchemaVersion, run.SnapshotScopeOrigin, run.SnapshotProjectScopeVersion,
        run.SnapshotTaskOverrideVersion, run.SnapshotWebEnabled, run.SnapshotProjectFilesEnabled,
        run.SnapshotResearchPlanRevisionId, run.SnapshotResearchPlanRevisionNo
    };

    private static async Task AssertInvalidationAsync(Fixture fixture, string change)
    {
        var item = await fixture.Db.OutboxEvents.SingleAsync();
        Assert.Equal("Projects.TaskChanged.v1", item.EventType);
        Assert.Equal(fixture.Task.TenantId, item.TenantId);
        Assert.Equal("Task", item.AggregateType);
        Assert.Equal(fixture.Task.Id, item.AggregateId);
        Assert.Equal(5, item.AggregateVersion);
        Assert.Equal(OutboxEventStatus.Pending, item.Status);
        Assert.Equal(fixture.Clock.UtcNow, item.OccurredAt);
        using var envelope = JsonDocument.Parse(item.PayloadJson);
        var payload = envelope.RootElement.GetProperty("payload");
        Assert.Equal(fixture.Task.Id, payload.GetProperty("taskId").GetGuid());
        Assert.Equal(fixture.Task.ProjectId, payload.GetProperty("projectId").GetGuid());
        Assert.Equal(5, payload.GetProperty("taskVersion").GetInt64());
        Assert.Equal(change, payload.GetProperty("change").GetString());
        Assert.True(payload.GetProperty("requiresRefetch").GetBoolean());
        using var routing = JsonDocument.Parse(item.RoutingJson);
        var route = Assert.Single(routing.RootElement.EnumerateArray());
        Assert.Equal((int)RealtimeSubscriptionType.Project, route.GetProperty("subscriptionType").GetInt32());
        Assert.Equal(fixture.Task.ProjectId, route.GetProperty("resourceId").GetGuid());
    }

    private sealed class Fixture(AppDbContext db, CurrentTenantService tenantContext, TaskItem task, TaskExecutionRun run,
        CurrentActor actor, Authorization authorization, AuditLogger audit, FixedClock clock) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public CurrentTenantService TenantContext { get; } = tenantContext;
        public TaskItem Task { get; } = task;
        public TaskExecutionRun Run { get; } = run;
        public CurrentActor Actor { get; } = actor;
        public Authorization Authorization { get; } = authorization;
        public AuditLogger Audit { get; } = audit;
        public FixedClock Clock { get; } = clock;
        public TaskExecutionScopeRepository Scopes { get; } = new(db);
        public Saving Save { get; } = new(db);
        public TaskExecutionSourcePolicyV2 ClosedPolicy { get; } = TaskExecutionSourcePolicyV2.FromLegacy(false, true) with
        {
            ProjectFile = TaskExecutionSourceState.Prioritize,
            Items = [new(TaskExecutionSourceKind.ProjectFile, TaskExecutionSourcePolicyV2.ProjectFileSourceId(Guid.NewGuid()), TaskExecutionSourceState.Exclude)]
        };

        public Task<Result<TaskExecutionInterventionResponse>> InvokeAsync(bool correct, Guid taskId, Guid runId)
        {
            Save.Inner = new EfUnitOfWork(Db, Scopes);
            Save.Scopes = Scopes;
            var outbox = new TransactionalOutbox(new OutboxEventRepository(Db), TenantContext, Clock);
            var service = new TaskExecutionInterventionService(new ProjectRepository(Db), Authorization,
                new TaskExecutionInterventionRepository(Db), Scopes, new ResearchPlanRepository(Db), Actor,
                Clock, Audit, new BusinessInvalidationPublisher(outbox, TenantContext, Clock), Save);
            return correct ? service.CorrectDirectionAsync(taskId, runId) : service.StopAsync(taskId, runId);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var tenantContext = new CurrentTenantService();
            tenantContext.SetPlatformScope();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"intervention-{Guid.NewGuid():N}").Options, tenantContext);
            var tenant = new Tenant { Name = "Intervention tenant", DisplayName = "Intervention tenant", Slug = $"intervention-{Guid.NewGuid():N}" };
            var user = new User { DisplayName = "Intervention manager", Email = $"{Guid.NewGuid():N}@example.test", NormalizedEmail = $"{Guid.NewGuid():N}@EXAMPLE.TEST" };
            db.AddRange(tenant, user); await db.SaveChangesAsync();
            tenantContext.SetTenant(tenant.Id, tenant.Slug);
            var workspace = new Workspace { Name = "Intervention workspace", Slug = $"intervention-{Guid.NewGuid():N}", CreatedByUserId = user.Id };
            db.Add(workspace); await db.SaveChangesAsync();
            var project = new Project { WorkspaceId = workspace.Id, OwnerUserId = user.Id, CreatedByUserId = user.Id,
                Name = "Intervention project", Slug = $"intervention-{Guid.NewGuid():N}", Status = ProjectStatus.Active };
            db.Add(project); await db.SaveChangesAsync();
            var task = new TaskItem { WorkspaceId = workspace.Id, ProjectId = project.Id, CreatedByUserId = user.Id,
                Title = "Intervention task", VersionNo = 4 };
            db.Add(task); await db.SaveChangesAsync();
            var run = new TaskExecutionRun { TenantId = tenant.Id, WorkspaceId = workspace.Id, ProjectId = project.Id,
                TaskItemId = task.Id, RequestedByUserId = user.Id, Status = TaskExecutionRunStatus.Running,
                RequestedAtUtc = new DateTimeOffset(2026, 8, 25, 8, 0, 0, TimeSpan.Zero), VersionNo = 7,
                SnapshotScopeOrigin = TaskExecutionScopeOrigin.ProjectDefault, SnapshotProjectScopeVersion = 2,
                SnapshotProjectFilesEnabled = true };
            db.Add(run); await db.SaveChangesAsync();
            var fixture = new Fixture(db, tenantContext, task, run, new CurrentActor(user.Id), new Authorization(), new AuditLogger(), new FixedClock());
            fixture.Scopes.StageSourcePolicyDocument(new TaskExecutionSourcePolicyDocument(
                TaskExecutionSourcePolicyOwnerType.Run, run.Id, task.TenantId, task.WorkspaceId,
                task.ProjectId, task.Id, run.SnapshotProjectScopeVersion,
                run.SnapshotTaskOverrideVersion, fixture.ClosedPolicy));
            return fixture;
        }

        public async Task<ResearchPlanRevision> AddResearchPlanAsync()
        {
            var plan = new ResearchPlan { TenantId = Task.TenantId, WorkspaceId = Task.WorkspaceId,
                ProjectId = Task.ProjectId, TaskItemId = Task.Id, VersionNo = 9 };
            var older = new ResearchPlanRevision { TenantId = Task.TenantId, WorkspaceId = Task.WorkspaceId,
                ProjectId = Task.ProjectId, TaskItemId = Task.Id, ResearchPlanId = plan.Id,
                RevisionNo = 8, CreatedByUserId = Actor.UserId!.Value, CreatedAtUtc = Clock.UtcNow.AddMinutes(-1) };
            var revision = new ResearchPlanRevision { TenantId = Task.TenantId, WorkspaceId = Task.WorkspaceId,
                ProjectId = Task.ProjectId, TaskItemId = Task.Id, ResearchPlanId = plan.Id,
                RevisionNo = 9, CreatedByUserId = Actor.UserId!.Value, CreatedAtUtc = Clock.UtcNow };
            plan.CurrentRevisionId = revision.Id; Db.AddRange(plan, older, revision); await Db.SaveChangesAsync();
            return revision;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class CurrentActor(Guid actorId) : ICurrentUser
    {
        public Guid? UserId { get; set; } = actorId;
        public bool IsAuthenticated { get; set; } = true;
        public Guid? SessionId => null;
        public string? Email => null;
        public SystemRole? SystemRole => Coglatas.Domain.Enums.SystemRole.NormalUser;
    }

    private sealed class Authorization : IProjectAuthorizationService
    {
        public bool CanManage { get; set; } = true;
        public Task<bool> CanViewProject(Guid userId, Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(CanManage);
        public Task<bool> CanManageProject(Guid userId, Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(CanManage);
        public Task<bool> CanCreateProject(Guid userId, Guid workspaceId, Guid groupId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 25, 9, 0, 0, TimeSpan.Zero);
    }

    private sealed class AuditLogger : IAuditLogger
    {
        public List<AuditLogEntry> Entries { get; } = [];
        public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry); return Task.CompletedTask;
        }
    }

    private sealed class Saving(AppDbContext db) : ITaskCommandUnitOfWork
    {
        public ITaskCommandUnitOfWork Inner { private get; set; } = new EfUnitOfWork(db);
        public TaskExecutionScopeRepository? Scopes { private get; set; }
        public List<TaskExecutionSourcePolicyDocument> StagedRunDocuments { get; } = [];
        public TaskCommandSaveResult Outcome { get; set; } = TaskCommandSaveResult.Saved;
        public int Calls { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);
        public void ClearTaskCommandTracking() => Inner.ClearTaskCommandTracking();
        public async Task<TaskCommandSaveOutcome> SaveTaskCommandAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Scopes is not null)
            {
                foreach (var entry in db.ChangeTracker.Entries<TaskExecutionRun>().ToArray())
                {
                    var document = await Scopes.GetSourcePolicyDocumentAsync(
                        TaskExecutionSourcePolicyOwnerType.Run, entry.Entity.Id, cancellationToken);
                    if (document is not null) StagedRunDocuments.Add(document);
                }
            }
            if (Outcome == TaskCommandSaveResult.Saved) return await Inner.SaveTaskCommandAsync(cancellationToken);
            ClearTaskCommandTracking(); return new TaskCommandSaveOutcome(Outcome);
        }
    }
}
