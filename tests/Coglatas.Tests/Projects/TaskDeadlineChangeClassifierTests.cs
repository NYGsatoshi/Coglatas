using Coglatas.Application.Projects;

namespace Coglatas.Tests.Projects;

public sealed class TaskDeadlineChangeClassifierTests
{
    private static readonly TimeZoneInfo Tokyo = TimeZoneInfo.CreateCustomTimeZone(
        "TaskDeadlineTests/Tokyo",
        TimeSpan.FromHours(9),
        "Task deadline test timezone",
        "Task deadline test timezone");

    private static readonly DateTimeOffset Now = new(2026, 8, 2, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NullToValueIsAdded()
    {
        var result = TaskDeadlineChangeClassifier.Classify(null, Now.AddDays(2), Tokyo, Now);

        Assert.Equal(TaskDeadlineChangeClassification.Added, result);
    }

    [Fact]
    public void ValueToNullIsRemoved()
    {
        var result = TaskDeadlineChangeClassifier.Classify(Now.AddDays(2), null, Tokyo, Now);

        Assert.Equal(TaskDeadlineChangeClassification.Removed, result);
    }

    [Fact]
    public void ShiftOf23Hours59MinutesIsNotMajor()
    {
        var oldDeadline = Now.AddDays(3);

        var result = TaskDeadlineChangeClassifier.Classify(
            oldDeadline,
            oldDeadline.AddHours(23).AddMinutes(59),
            TimeZoneInfo.Utc,
            Now);

        Assert.Equal(TaskDeadlineChangeClassification.None, result);
    }

    [Fact]
    public void ShiftOfExactly24HoursIsMajor()
    {
        var oldDeadline = Now.AddDays(3);

        var result = TaskDeadlineChangeClassifier.Classify(
            oldDeadline,
            oldDeadline.AddHours(24),
            TimeZoneInfo.Utc,
            Now);

        Assert.Equal(TaskDeadlineChangeClassification.ShiftAtLeast24Hours, result);
    }

    [Fact]
    public void BackwardShiftOf23Hours59MinutesIsNotMajor()
    {
        var oldDeadline = Now.AddDays(3);

        var result = TaskDeadlineChangeClassifier.Classify(
            oldDeadline,
            oldDeadline.AddHours(-23).AddMinutes(-59),
            TimeZoneInfo.Utc,
            Now);

        Assert.Equal(TaskDeadlineChangeClassification.None, result);
    }

    [Fact]
    public void BackwardShiftOfExactly24HoursIsMajor()
    {
        var oldDeadline = Now.AddDays(3);

        var result = TaskDeadlineChangeClassifier.Classify(
            oldDeadline,
            oldDeadline.AddHours(-24),
            TimeZoneInfo.Utc,
            Now);

        Assert.Equal(TaskDeadlineChangeClassification.ShiftAtLeast24Hours, result);
    }

    [Fact]
    public void CrossingWorkspaceLocalTodayBoundaryIsMajor()
    {
        var now = new DateTimeOffset(2026, 8, 2, 14, 30, 0, TimeSpan.Zero); // 23:30 in Tokyo
        var today = now.AddMinutes(15);
        var tomorrow = now.AddMinutes(45);

        var result = TaskDeadlineChangeClassifier.Classify(today, tomorrow, Tokyo, now);

        Assert.Equal(TaskDeadlineChangeClassification.CrossedUrgencyBoundary, result);
    }

    [Fact]
    public void CrossingOverdueBoundaryIsMajor()
    {
        var oldDeadline = Now.AddMinutes(-1);
        var newDeadline = Now.AddMinutes(1);

        var result = TaskDeadlineChangeClassifier.Classify(oldDeadline, newDeadline, Tokyo, Now);

        Assert.Equal(TaskDeadlineChangeClassification.CrossedUrgencyBoundary, result);
    }

    [Fact]
    public void WorkspaceTimezoneControlsTodayBoundary()
    {
        var now = new DateTimeOffset(2026, 8, 2, 14, 30, 0, TimeSpan.Zero);
        var oldDeadline = now.AddMinutes(15);
        var newDeadline = now.AddMinutes(45);

        var tokyoResult = TaskDeadlineChangeClassifier.Classify(oldDeadline, newDeadline, Tokyo, now);
        var utcResult = TaskDeadlineChangeClassifier.Classify(oldDeadline, newDeadline, TimeZoneInfo.Utc, now);

        Assert.Equal(TaskDeadlineChangeClassification.CrossedUrgencyBoundary, tokyoResult);
        Assert.Equal(TaskDeadlineChangeClassification.None, utcResult);
    }

    [Fact]
    public void TwentyFourHourShiftTakesPrecedenceOverBoundaryCrossing()
    {
        var oldDeadline = Now.AddMinutes(1);
        var newDeadline = oldDeadline.AddHours(24);

        var result = TaskDeadlineChangeClassifier.Classify(oldDeadline, newDeadline, TimeZoneInfo.Utc, Now);

        Assert.Equal(TaskDeadlineChangeClassification.ShiftAtLeast24Hours, result);
    }

    [Fact]
    public void UnchangedDeadlineIsNoneWhenOnlyPlanningDatesWouldChange()
    {
        var persistedDeadline = Now.AddDays(2);

        var result = TaskDeadlineChangeClassifier.Classify(
            persistedDeadline,
            persistedDeadline,
            Tokyo,
            Now);

        Assert.Equal(TaskDeadlineChangeClassification.None, result);
    }
}
