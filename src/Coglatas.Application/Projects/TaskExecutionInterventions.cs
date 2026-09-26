using System.Text.Json.Serialization;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Projects;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StopTaskExecutionRunRequest;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CorrectTaskExecutionDirectionRequest;

public sealed record TaskExecutionInterventionResponse(
    string Action,
    TaskExecutionRunResponse ClosedRun,
    TaskExecutionRunResponse? ResumedRun,
    string ResumePoint,
    IReadOnlyList<string> EditableSurfaces);

public interface ITaskExecutionInterventionService
{
    Task<Result<TaskExecutionInterventionResponse>> StopAsync(
        Guid taskItemId,
        Guid runId,
        CancellationToken cancellationToken = default);

    Task<Result<TaskExecutionInterventionResponse>> CorrectDirectionAsync(
        Guid taskItemId,
        Guid runId,
        CancellationToken cancellationToken = default);
}

public interface ITaskExecutionInterventionRepository
{
    Task<TaskExecutionRun?> GetRunForUpdateAsync(
        Guid runId,
        CancellationToken cancellationToken = default);

    Task AddActivityAsync(
        ActivityLog activity,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Owns explicit user intervention in a durable Task execution Run. Stop and
/// direction correction are intentionally different commands. A correction
/// never mutates the immutable snapshot of the current Run: it closes that Run
/// as Redirected and creates a successor from the latest saved Research Plan and
/// source policy. The current V1 runtime has no durable intra-run checkpoint, so
/// the truthful resume point is the beginning of a new Run.
/// </summary>
public sealed class TaskExecutionInterventionService(
    IProjectRepository projects,
    IProjectAuthorizationService projectAuthorization,
    ITaskExecutionInterventionRepository interventions,
    ITaskExecutionScopeRepository executionScopes,
    IResearchPlanRepository researchPlans,
    ICurrentUser currentUser,
    IClock clock,
    IAuditLogger audit,
    IBusinessInvalidationPublisher invalidations,
    ITaskCommandUnitOfWork unitOfWork) : ITaskExecutionInterventionService
{
    private static readonly IReadOnlyList<string> CorrectionSurfaces =
        ["Task brief", "Research plan", "Active source scope"];

    public async Task<Result<TaskExecutionInterventionResponse>> StopAsync(
        Guid taskItemId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var task = await ManagedTaskAsync(taskItemId, cancellationToken);
        if (task is null || runId == Guid.Empty)
            return NotFound();

        var run = await interventions.GetRunForUpdateAsync(runId, cancellationToken);
        if (!MatchesTask(run, task))
            return NotFound();
        if (!TaskExecutionRunLifecycle.CanIntervene(run!.Status))
            return Unavailable("This execution has already reached a terminal state and cannot be stopped.");

        var actor = Actor();
        var occurredAt = clock.UtcNow;
        run.Status = TaskExecutionRunStatus.Stopped;
        run.FailureCode = null;
        run.FinishedAtUtc = occurredAt;
        run.VersionNo = NextVersion(run.VersionNo);
        AdvanceTaskVersion(task);

        await interventions.AddActivityAsync(NewActivity(
            task,
            actor,
            occurredAt,
            "Execution stopped by user."), cancellationToken);
        await audit.LogAsync(new AuditLogEntry(
            actor,
            "TaskExecutionRunStopped",
            "TaskExecutionRun",
            run.Id,
            WorkspaceId: task.WorkspaceId,
            ProjectId: task.ProjectId,
            Metadata: new Dictionary<string, object?>
            {
                ["status"] = run.Status.ToString(),
                ["resumePoint"] = "None"
            }), cancellationToken);
        await invalidations.TaskChangedAsync(task, actor, "executionRunStopped", cancellationToken: cancellationToken);

        var save = await unitOfWork.SaveTaskCommandAsync(cancellationToken);
        if (!save.IsSaved)
            return Stale();

        var closed = await ToRunResponseAsync(run, cancellationToken);
        return Result<TaskExecutionInterventionResponse>.Success(new(
            "Stop",
            closed,
            null,
            "None",
            []));
    }

    public async Task<Result<TaskExecutionInterventionResponse>> CorrectDirectionAsync(
        Guid taskItemId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var task = await ManagedTaskAsync(taskItemId, cancellationToken);
        if (task is null || runId == Guid.Empty)
            return NotFound();

        var currentRun = await interventions.GetRunForUpdateAsync(runId, cancellationToken);
        if (!MatchesTask(currentRun, task))
            return NotFound();
        if (!TaskExecutionRunLifecycle.CanIntervene(currentRun!.Status))
            return Unavailable("This execution has already reached a terminal state and cannot be redirected.");

        var actor = Actor();
        var occurredAt = clock.UtcNow;
        var snapshot = await BuildCurrentSnapshotAsync(task, cancellationToken);
        var successor = NewRun(task, actor, occurredAt, snapshot);

        currentRun.Status = TaskExecutionRunStatus.Redirected;
        currentRun.FailureCode = null;
        currentRun.FinishedAtUtc = occurredAt;
        currentRun.VersionNo = NextVersion(currentRun.VersionNo);
        await executionScopes.AddRunAsync(successor, cancellationToken);
        executionScopes.StageSourcePolicyDocument(new TaskExecutionSourcePolicyDocument(
            TaskExecutionSourcePolicyOwnerType.Run,
            successor.Id,
            task.TenantId,
            task.WorkspaceId,
            task.ProjectId,
            task.Id,
            snapshot.ProjectVersion,
            snapshot.OverrideVersion,
            snapshot.Policy));
        AdvanceTaskVersion(task);

        await interventions.AddActivityAsync(NewActivity(
            task,
            actor,
            occurredAt,
            "Execution direction corrected. The previous Run was redirected and a new Run restarts from the latest saved Task state."), cancellationToken);
        await audit.LogAsync(new AuditLogEntry(
            actor,
            "TaskExecutionRunRedirected",
            "TaskExecutionRun",
            currentRun.Id,
            WorkspaceId: task.WorkspaceId,
            ProjectId: task.ProjectId,
            Metadata: new Dictionary<string, object?>
            {
                ["status"] = currentRun.Status.ToString(),
                ["successorRunId"] = successor.Id,
                ["resumePoint"] = "NewRunFromLatestTaskState",
                ["researchPlanRevisionNo"] = successor.SnapshotResearchPlanRevisionNo,
                ["projectScopeVersion"] = successor.SnapshotProjectScopeVersion,
                ["taskOverrideVersion"] = successor.SnapshotTaskOverrideVersion
            }), cancellationToken);
        await invalidations.TaskChangedAsync(task, actor, "executionRunRedirected", cancellationToken: cancellationToken);

        var save = await unitOfWork.SaveTaskCommandAsync(cancellationToken);
        if (!save.IsSaved)
            return Stale();

        return Result<TaskExecutionInterventionResponse>.Success(new(
            "CorrectDirection",
            await ToRunResponseAsync(currentRun, cancellationToken),
            await ToRunResponseAsync(successor, cancellationToken),
            "NewRunFromLatestTaskState",
            CorrectionSurfaces));
    }

    private async Task<CurrentSnapshot> BuildCurrentSnapshotAsync(
        TaskItem task,
        CancellationToken cancellationToken)
    {
        var projectScope = await executionScopes.GetProjectScopeAsync(task.ProjectId, cancellationToken);
        var taskOverride = await executionScopes.GetTaskOverrideAsync(task.Id, cancellationToken);
        var researchPlan = await researchPlans.GetCurrentExecutionSnapshotForTaskAsync(task.Id, cancellationToken);

        if (taskOverride is not null)
        {
            var document = await executionScopes.GetSourcePolicyDocumentAsync(
                TaskExecutionSourcePolicyOwnerType.Task,
                task.Id,
                cancellationToken);
            var policy = document is { TaskOverrideVersion: var version } && version == taskOverride.VersionNo
                ? document.Policy
                : TaskExecutionSourcePolicyV2.FromLegacy(taskOverride.WebEnabled, taskOverride.ProjectFilesEnabled);
            return new(
                TaskExecutionScopeOrigin.TaskOverride,
                projectScope?.VersionNo ?? 0,
                taskOverride.VersionNo,
                policy,
                researchPlan?.RevisionId,
                researchPlan?.RevisionNo);
        }

        var projectDocument = await executionScopes.GetSourcePolicyDocumentAsync(
            TaskExecutionSourcePolicyOwnerType.Project,
            task.ProjectId,
            cancellationToken);
        var projectPolicy = projectScope is not null &&
                            projectDocument is { ProjectScopeVersion: var projectVersion } &&
                            projectVersion == projectScope.VersionNo
            ? projectDocument.Policy
            : TaskExecutionSourcePolicyV2.FromLegacy(
                projectScope?.WebEnabled ?? false,
                projectScope?.ProjectFilesEnabled ?? false);
        return new(
            TaskExecutionScopeOrigin.ProjectDefault,
            projectScope?.VersionNo ?? 0,
            null,
            projectPolicy,
            researchPlan?.RevisionId,
            researchPlan?.RevisionNo);
    }

    private static TaskExecutionRun NewRun(
        TaskItem task,
        Guid actor,
        DateTimeOffset requestedAt,
        CurrentSnapshot snapshot) => new()
    {
        TenantId = task.TenantId,
        WorkspaceId = task.WorkspaceId,
        ProjectId = task.ProjectId,
        TaskItemId = task.Id,
        RequestedByUserId = actor,
        RequestedAtUtc = requestedAt,
        SnapshotSchemaVersion = TaskExecutionRun.CurrentSnapshotSchemaVersion,
        SnapshotScopeOrigin = snapshot.Origin,
        SnapshotProjectScopeVersion = snapshot.ProjectVersion,
        SnapshotTaskOverrideVersion = snapshot.OverrideVersion,
        SnapshotWebEnabled = snapshot.Policy.WebEnabled,
        SnapshotProjectFilesEnabled = snapshot.Policy.ProjectFilesEnabled,
        SnapshotResearchPlanRevisionId = snapshot.ResearchPlanRevisionId,
        SnapshotResearchPlanRevisionNo = snapshot.ResearchPlanRevisionNo,
        RuntimeProvider = FirstPartyProjectFilesRuntimeV1.Provider,
        RuntimeContractVersion = FirstPartyProjectFilesRuntimeV1.ContractVersion,
        Status = TaskExecutionRunStatus.Accepted,
        FailureCode = null,
        QueuedAtUtc = null,
        StartedAtUtc = null,
        FinishedAtUtc = null
    };

    private async Task<TaskExecutionRunResponse> ToRunResponseAsync(
        TaskExecutionRun run,
        CancellationToken cancellationToken)
    {
        var document = await executionScopes.GetSourcePolicyDocumentAsync(
            TaskExecutionSourcePolicyOwnerType.Run,
            run.Id,
            cancellationToken);
        var policy = document?.Policy ?? TaskExecutionSourcePolicyV2.FromLegacy(
            run.SnapshotWebEnabled,
            run.SnapshotProjectFilesEnabled);
        return new TaskExecutionRunResponse(
            run.Id,
            run.Status,
            run.FailureCode,
            run.RequestedAtUtc,
            run.FinishedAtUtc,
            run.SnapshotSchemaVersion,
            run.SnapshotScopeOrigin,
            run.SnapshotProjectScopeVersion,
            run.SnapshotTaskOverrideVersion,
            run.SnapshotWebEnabled,
            run.SnapshotProjectFilesEnabled,
            run.RuntimeProvider,
            run.RuntimeContractVersion,
            run.QueuedAtUtc,
            run.StartedAtUtc,
            run.SnapshotResearchPlanRevisionId,
            run.SnapshotResearchPlanRevisionNo,
            policy);
    }

    private async Task<TaskItem?> ManagedTaskAsync(
        Guid taskItemId,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        if (!currentUser.IsAuthenticated || actor == Guid.Empty || taskItemId == Guid.Empty)
            return null;

        var task = await projects.GetTaskAsync(taskItemId, cancellationToken);
        return task is { DeletedAt: null } &&
               await projectAuthorization.CanManageProject(actor, task.ProjectId, cancellationToken)
            ? task
            : null;
    }

    private Guid Actor() => currentUser.UserId ?? Guid.Empty;

    private static bool MatchesTask(TaskExecutionRun? run, TaskItem task) =>
        run is not null &&
        run.TaskItemId == task.Id &&
        run.TenantId == task.TenantId &&
        run.WorkspaceId == task.WorkspaceId &&
        run.ProjectId == task.ProjectId;

    private static ActivityLog NewActivity(
        TaskItem task,
        Guid actor,
        DateTimeOffset occurredAt,
        string body) => new()
    {
        TenantId = task.TenantId,
        ProjectId = task.ProjectId,
        TaskItemId = task.Id,
        AuthorUserId = actor,
        ActivityType = ActivityLogType.StatusUpdate,
        Body = body,
        OccurredAt = occurredAt,
        CreatedAt = occurredAt
    };

    private static long NextVersion(long value) => Math.Max(1L, checked(value + 1L));
    private static void AdvanceTaskVersion(TaskItem task) => task.VersionNo = NextVersion(task.VersionNo);

    private static Result<TaskExecutionInterventionResponse> NotFound() =>
        Result<TaskExecutionInterventionResponse>.Failure(new ApplicationErrorDetail(
            "TASK_EXECUTION_NOT_FOUND",
            "The requested resource was not found."));

    private static Result<TaskExecutionInterventionResponse> Unavailable(string message) =>
        Result<TaskExecutionInterventionResponse>.Failure(new ApplicationErrorDetail(
            "TASK_EXECUTION_INTERVENTION_NOT_AVAILABLE",
            message));

    private static Result<TaskExecutionInterventionResponse> Stale() =>
        Result<TaskExecutionInterventionResponse>.Failure(new ApplicationErrorDetail(
            "TASK_EXECUTION_STALE_VERSION",
            "The execution changed before the intervention could be saved. Refetch and retry."));

    private sealed record CurrentSnapshot(
        TaskExecutionScopeOrigin Origin,
        long ProjectVersion,
        long? OverrideVersion,
        TaskExecutionSourcePolicyV2 Policy,
        Guid? ResearchPlanRevisionId,
        long? ResearchPlanRevisionNo);
}
