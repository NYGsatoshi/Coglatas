using Coglatas.Application.Notifications;

namespace Coglatas.Tests.Notifications;

[Trait("Scope", "TaskV1PR07C")]
public sealed class TaskDeadlineDigestPolicyTests
{
    private static readonly TimeZoneInfo Tokyo = TimeZoneInfo.CreateCustomTimeZone(
        "TaskDeadlineDigestTests/Tokyo",
        TimeSpan.FromHours(9),
        "Task deadline digest Tokyo",
        "Task deadline digest Tokyo");

    private static readonly TimeZoneInfo Eastern = CreateEasternTimeZone();

    [Fact]
    public void ContractPinsPolicyAndAutomaticAttemptVersions()
    {
        Assert.Equal(1, TaskDeadlineDigestPolicy.PolicyVersion);
        Assert.Equal(3, TaskDeadlineDigestPolicy.MaximumAutomaticAttempts);
    }

    [Theory]
    [InlineData(0, 0, 0, true)]
    [InlineData(0, 15, 0, true)]
    [InlineData(23, 45, 0, true)]
    [InlineData(0, 1, 0, false)]
    [InlineData(23, 59, 0, false)]
    [InlineData(23, 45, 1, false)]
    public void LocalTimeValidationRequiresAnExactQuarterHour(
        int hour,
        int minute,
        int second,
        bool expected)
    {
        Assert.Equal(expected, TaskDeadlineDigestPolicy.IsValidLocalTime(new TimeOnly(hour, minute, second)));
    }

    [Fact]
    public void LocalTimeValidationRejectsSubSecondValues()
    {
        var value = new TimeOnly(new TimeOnly(8, 0).Ticks + 1);

        Assert.False(TaskDeadlineDigestPolicy.IsValidLocalTime(value));
    }

    [Fact]
    public void LocalDateAndMidnightScheduleUseWorkspaceTimezone()
    {
        var currentInstant = new DateTimeOffset(2026, 8, 3, 15, 30, 0, TimeSpan.Zero);

        var schedule = TaskDeadlineDigestPolicy.ResolveSchedule(
            currentInstant,
            TimeOnly.MinValue,
            Tokyo);

        Assert.Equal(new DateOnly(2026, 8, 4), schedule.LocalDate);
        Assert.Equal(new DateTimeOffset(2026, 8, 3, 15, 0, 0, TimeSpan.Zero), schedule.DueAtUtc);
    }

    [Fact]
    public void LatestAllowedLocalTimeResolvesWithoutRounding()
    {
        var dueAt = TaskDeadlineDigestPolicy.ResolveDueAtUtc(
            new DateOnly(2026, 8, 4),
            new TimeOnly(23, 45),
            Tokyo);

        Assert.Equal(new DateTimeOffset(2026, 8, 4, 14, 45, 0, TimeSpan.Zero), dueAt);
    }

    [Fact]
    public void InvalidLocalTimeIsRejectedInsteadOfRounded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TaskDeadlineDigestPolicy.ResolveDueAtUtc(
            new DateOnly(2026, 8, 4),
            new TimeOnly(8, 1),
            Tokyo));
    }

    [Fact]
    public void DstGapUsesFirstValidInstantAfterGap()
    {
        var localDate = new DateOnly(2026, 3, 8);
        var selected = new TimeOnly(2, 30);
        Assert.True(Eastern.IsInvalidTime(localDate.ToDateTime(selected, DateTimeKind.Unspecified)));

        var dueAt = TaskDeadlineDigestPolicy.ResolveDueAtUtc(localDate, selected, Eastern);

        Assert.Equal(new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero), dueAt);
    }

    [Fact]
    public void DstFoldUsesFirstChronologicalOccurrence()
    {
        var localDate = new DateOnly(2026, 11, 1);
        var selected = new TimeOnly(1, 30);
        Assert.True(Eastern.IsAmbiguousTime(localDate.ToDateTime(selected, DateTimeKind.Unspecified)));

        var dueAt = TaskDeadlineDigestPolicy.ResolveDueAtUtc(localDate, selected, Eastern);

        Assert.Equal(new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero), dueAt);
    }

    [Fact]
    public void RepeatedFoldEvaluationKeepsOneLogicalIdentity()
    {
        var workspaceId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var localDate = new DateOnly(2026, 11, 1);

        var first = TaskDeadlineDigestPolicy.BuildNotificationLogicalKey(workspaceId, localDate);
        var repeated = TaskDeadlineDigestPolicy.BuildNotificationLogicalKey(workspaceId, localDate);

        Assert.Equal(first, repeated);
    }

    public static TheoryData<DateTimeOffset?, TaskDeadlineDigestCategory?> CategoryCases => new()
    {
        {
            new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero),
            TaskDeadlineDigestCategory.DeadlineInThreeLocalDays
        },
        {
            new DateTimeOffset(2026, 8, 4, 15, 0, 0, TimeSpan.Zero),
            TaskDeadlineDigestCategory.DeadlineInOneLocalDay
        },
        {
            new DateTimeOffset(2026, 8, 4, 14, 45, 0, TimeSpan.Zero),
            TaskDeadlineDigestCategory.DueToday
        },
        {
            new DateTimeOffset(2026, 8, 3, 15, 29, 59, TimeSpan.Zero),
            TaskDeadlineDigestCategory.Overdue
        },
        {
            new DateTimeOffset(2026, 8, 5, 15, 0, 0, TimeSpan.Zero),
            null
        },
        { null, null }
    };

    [Theory]
    [MemberData(nameof(CategoryCases))]
    public void ClassificationUsesWorkspaceLocalDatesAndCurrentInstantPrecedence(
        DateTimeOffset? deadlineAt,
        TaskDeadlineDigestCategory? expected)
    {
        var currentInstant = new DateTimeOffset(2026, 8, 3, 15, 30, 0, TimeSpan.Zero);

        var result = TaskDeadlineDigestPolicy.Classify(deadlineAt, currentInstant, Tokyo);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void DeadlineExactlyAtCurrentInstantIsDueTodayNotOverdue()
    {
        var currentInstant = new DateTimeOffset(2026, 8, 3, 15, 30, 0, TimeSpan.Zero);

        var result = TaskDeadlineDigestPolicy.Classify(currentInstant, currentInstant, Tokyo);

        Assert.Equal(TaskDeadlineDigestCategory.DueToday, result);
    }

    [Fact]
    public void EarlierDeadlineOnCurrentLocalDateIsOverdue()
    {
        var currentInstant = new DateTimeOffset(2026, 8, 3, 15, 30, 0, TimeSpan.Zero);
        var earlierToday = currentInstant.AddMinutes(-1);

        var result = TaskDeadlineDigestPolicy.Classify(earlierToday, currentInstant, Tokyo);

        Assert.Equal(TaskDeadlineDigestCategory.Overdue, result);
    }

    [Fact]
    public void NotificationLogicalKeyIsStableAndContainsDailyPolicyIdentity()
    {
        var workspaceId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

        var key = TaskDeadlineDigestPolicy.BuildNotificationLogicalKey(
            workspaceId,
            new DateOnly(2026, 8, 4));

        Assert.Equal(
            "task-deadline-digest:workspace:00112233445566778899aabbccddeeff:date:2026-08-04:policy:1",
            key);
    }

    [Fact]
    public void LogicalKeyRejectsInvalidIdentityParts()
    {
        Assert.Throws<ArgumentException>(() => TaskDeadlineDigestPolicy.BuildNotificationLogicalKey(
            Guid.Empty,
            new DateOnly(2026, 8, 4)));
        Assert.Throws<ArgumentOutOfRangeException>(() => TaskDeadlineDigestPolicy.BuildNotificationLogicalKey(
            Guid.NewGuid(),
            new DateOnly(2026, 8, 4),
            0));
    }

    private static TimeZoneInfo CreateEasternTimeZone()
    {
        var daylightStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0, DateTimeKind.Unspecified),
            3,
            2,
            DayOfWeek.Sunday);
        var daylightEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0, DateTimeKind.Unspecified),
            11,
            1,
            DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2020, 1, 1),
            new DateTime(2030, 12, 31),
            TimeSpan.FromHours(1),
            daylightStart,
            daylightEnd);
        return TimeZoneInfo.CreateCustomTimeZone(
            "TaskDeadlineDigestTests/Eastern",
            TimeSpan.FromHours(-5),
            "Task deadline digest Eastern",
            "Task deadline digest Eastern standard",
            "Task deadline digest Eastern daylight",
            [rule]);
    }
}
