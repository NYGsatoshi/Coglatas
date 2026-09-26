namespace Coglatas.Domain.Enums;

/// <summary>
/// Identifies the server-authoritative policy that was selected for a Task.
/// A Task override always replaces the Project default in full; it is never a
/// partial merge.
/// </summary>
public enum TaskExecutionScopeOrigin
{
    ProjectDefault = 0,
    TaskOverride = 1
}

/// <summary>
/// The server-owned implementation selected for a Task execution run. It is
/// deliberately not browser-selectable and has no provider credentials.
/// </summary>
public enum TaskExecutionProvider
{
    FirstPartyProjectFilesRuntimeV1 = 0
}

/// <summary>
/// Durable lifecycle of a persisted execution request. It is independent from
/// Task workflow, activity, progress, and browser state. Stopped is a deliberate
/// destructive intervention. Redirected closes the current immutable snapshot so
/// a successor Run can restart from the latest saved Task state.
/// </summary>
public enum TaskExecutionRunStatus
{
    Accepted = 0,
    Queued = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    Stopped = 5,
    Redirected = 6
}

/// <summary>
/// Stable user-facing execution state. This is a projection of
/// <see cref="TaskExecutionRunStatus"/>, not a second mutable lifecycle.
/// </summary>
public enum TaskExecutionMajorState
{
    Accepted = 0,
    Queued = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    Stopped = 5,
    Redirected = 6
}

/// <summary>
/// Canonical runtime progression plus user intervention exits. A Stop or
/// Redirect may win from any non-terminal state. Once terminal, a Run cannot be
/// revived; direction correction creates a new immutable successor Run instead.
/// </summary>
public static class TaskExecutionRunLifecycle
{
    public static bool IsTerminal(TaskExecutionRunStatus status) =>
        status is TaskExecutionRunStatus.Succeeded or
            TaskExecutionRunStatus.Failed or
            TaskExecutionRunStatus.Stopped or
            TaskExecutionRunStatus.Redirected;

    public static bool CanIntervene(TaskExecutionRunStatus status) => !IsTerminal(status);

    public static bool CanTransition(TaskExecutionRunStatus from, TaskExecutionRunStatus to) =>
        (from, to) switch
        {
            (TaskExecutionRunStatus.Accepted, TaskExecutionRunStatus.Queued) => true,
            (TaskExecutionRunStatus.Queued, TaskExecutionRunStatus.Running) => true,
            (TaskExecutionRunStatus.Running, TaskExecutionRunStatus.Succeeded) => true,
            (TaskExecutionRunStatus.Running, TaskExecutionRunStatus.Failed) => true,
            (_, TaskExecutionRunStatus.Stopped) when CanIntervene(from) => true,
            (_, TaskExecutionRunStatus.Redirected) when CanIntervene(from) => true,
            _ => false
        };
}
