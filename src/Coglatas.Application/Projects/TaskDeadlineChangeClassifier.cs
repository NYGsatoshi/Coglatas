namespace Coglatas.Application.Projects;

public enum TaskDeadlineChangeClassification
{
    None = 0,
    Added = 1,
    Removed = 2,
    ShiftAtLeast24Hours = 3,
    CrossedUrgencyBoundary = 4
}

/// <summary>
/// Server-authoritative classification for a persisted DeadlineAt mutation.
/// Urgency boundaries use the Workspace timezone and one caller-supplied
/// instant so the result cannot change midway through a command.
/// </summary>
public static class TaskDeadlineChangeClassifier
{
    private static readonly TimeSpan MajorShiftThreshold = TimeSpan.FromHours(24);

    public static TaskDeadlineChangeClassification Classify(
        DateTimeOffset? persistedDeadlineAt,
        DateTimeOffset? newDeadlineAt,
        TimeZoneInfo workspaceTimeZone,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(workspaceTimeZone);

        if (!persistedDeadlineAt.HasValue && newDeadlineAt.HasValue)
            return TaskDeadlineChangeClassification.Added;
        if (persistedDeadlineAt.HasValue && !newDeadlineAt.HasValue)
            return TaskDeadlineChangeClassification.Removed;
        if (!persistedDeadlineAt.HasValue)
            return TaskDeadlineChangeClassification.None;

        var oldValue = persistedDeadlineAt.Value;
        var newValue = newDeadlineAt!.Value;
        if (oldValue == newValue)
            return TaskDeadlineChangeClassification.None;

        var shift = newValue - oldValue;
        if (shift >= MajorShiftThreshold || shift <= -MajorShiftThreshold)
            return TaskDeadlineChangeClassification.ShiftAtLeast24Hours;

        return Bucket(oldValue, workspaceTimeZone, now) != Bucket(newValue, workspaceTimeZone, now)
            ? TaskDeadlineChangeClassification.CrossedUrgencyBoundary
            : TaskDeadlineChangeClassification.None;
    }

    private static DeadlineUrgencyBucket Bucket(
        DateTimeOffset deadlineAt,
        TimeZoneInfo workspaceTimeZone,
        DateTimeOffset now)
    {
        if (deadlineAt < now)
            return DeadlineUrgencyBucket.Overdue;

        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, workspaceTimeZone).DateTime);
        var localDeadlineDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(deadlineAt, workspaceTimeZone).DateTime);
        return localDeadlineDate == localToday
            ? DeadlineUrgencyBucket.Today
            : DeadlineUrgencyBucket.Future;
    }

    private enum DeadlineUrgencyBucket
    {
        Overdue,
        Today,
        Future
    }
}
