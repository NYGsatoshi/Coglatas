using Coglatas.Domain.Entities;

namespace Coglatas.Tests.Aq;

[Trait("Feature", "AQ")]
public sealed class AqReceptionWindowTests
{
    private static readonly DateTimeOffset EventStart = new(2026, 11, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EventEnd = EventStart.AddHours(2);

    [Fact]
    public void TargetWindowIncludesItsOpeningAndExcludesItsClosing()
    {
        var targetStart = EventStart.AddMinutes(30);
        var targetEnd = EventStart.AddHours(1);
        var window = SurveyReceptionWindow.Intersect(EventStart, EventEnd, targetStart, targetEnd);

        Assert.False(window.Contains(targetStart.AddTicks(-1)));
        Assert.True(window.Contains(targetStart));
        Assert.True(window.Contains(targetEnd.AddTicks(-1)));
        Assert.False(window.Contains(targetEnd));
    }

    [Fact]
    public void TargetLimitsCannotExtendTheParentAndAbsentLimitsAreInherited()
    {
        var expanded = SurveyReceptionWindow.Intersect(
            EventStart, EventEnd, EventStart.AddHours(-1), EventEnd.AddHours(1));
        var inherited = SurveyReceptionWindow.Intersect(EventStart, EventEnd, null, null);

        Assert.Equal(EventStart, expanded.StartsAtUtc);
        Assert.Equal(EventEnd, expanded.EndsAtUtc);
        Assert.Equal(expanded.StartsAtUtc, inherited.StartsAtUtc);
        Assert.Equal(expanded.EndsAtUtc, inherited.EndsAtUtc);
        Assert.Throws<ArgumentException>(() =>
            SurveyReceptionWindow.Intersect(EventStart, EventEnd, EventEnd, EventEnd.AddHours(1)));
    }

    [Fact]
    public void LocalClockOverlapNeedsAnOffsetAndTheSpringGapIsInvalid()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var overlap = new DateTime(2026, 11, 1, 1, 30, 0, DateTimeKind.Unspecified);
        var gap = new DateTime(2026, 3, 8, 2, 30, 0, DateTimeKind.Unspecified);

        Assert.Throws<ArgumentException>(() => EventFeedbackSettings.ResolveLocalTime(overlap, zone));
        Assert.Throws<ArgumentException>(() => EventFeedbackSettings.ResolveLocalTime(gap, zone));
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero),
            EventFeedbackSettings.ResolveLocalTime(overlap, zone, TimeSpan.FromHours(-4)));
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero),
            EventFeedbackSettings.ResolveLocalTime(overlap, zone, TimeSpan.FromHours(-5)));
    }
}
