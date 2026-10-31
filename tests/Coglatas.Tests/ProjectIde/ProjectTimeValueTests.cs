using System.Globalization;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class ProjectTimeValueTests
{
    private static CalendarReference Calendar() => new(SourceJson.Parse("""
        {"calendarId":"calendar.school","version":"2026-01","timeZoneId":"Asia/Tokyo",
        "rulesDigest":{"algorithm":"sha-256","value":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}
        """));

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("9223372036854775807")]
    [InlineData("-9223372036854775808")]
    public void ElapsedTicksRoundTripExactly(string text) => Assert.Equal(text, ElapsedDuration.Parse(text).CanonicalText);

    [Theory]
    [InlineData("01")]
    [InlineData("-0")]
    [InlineData("+1")]
    [InlineData("1.0")]
    [InlineData("9223372036854775808")]
    public void NoncanonicalOrOverflowingTicksAreRejected(string text) => Assert.Throws<FormatException>(() => ElapsedDuration.Parse(text));

    [Fact]
    public void DurationKindsAndCivilDatesCannotBecomeElapsedImplicitly()
    {
        var working = new WorkingDuration(10, Calendar());
        Assert.Equal("Asia/Tokyo", working.Calendar.TimeZoneId);
        Assert.Equal("2024-02-29", new ProjectLocalDate("2024-02-29", Calendar()).CanonicalText);
        Assert.Throws<FormatException>(() => new ProjectLocalDate("2026-02-29", Calendar()));
        Assert.Throws<OverflowException>(() => new ElapsedDuration(long.MaxValue).Add(new(1)));
        Assert.NotEqual(typeof(ElapsedDuration), working.GetType());
        Assert.DoesNotContain(typeof(CalendarPeriod).GetMethods(), method => method.Name.StartsWith("op_Implicit", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("2026-10-31T00:00:00Z")]
    [InlineData("2026-10-31T00:00:00.00000000Z")]
    [InlineData("2026-10-31T00:00:00.0000000+00:00")]
    [InlineData("2026-10-31T00:00:00.0000000+09:00")]
    [InlineData("2026-10-31T00:00:00.0000000")]
    [InlineData("2016-12-31T23:59:60.0000000Z")]
    [InlineData("2026-02-29T00:00:00.0000000Z")]
    public void InstantRejectsRoundingAndImplicitZones(string text) => Assert.Throws<FormatException>(() => ProjectInstant.Parse(text));

    [Fact]
    public void InstantUsesExactTicksAndHalfOpenIntervals()
    {
        var start = ProjectInstant.Parse("2026-10-31T00:00:00.0000000Z");
        var end = start.Add(new(1));
        Assert.Equal("2026-10-31T00:00:00.0000001Z", end.CanonicalText);
        var a = new InstantInterval(start, end);
        Assert.False(a.Overlaps(new(end, end.Add(new(1)))));
        Assert.True(a.Overlaps(new(start, end)));
        Assert.False(a.Overlaps(new(start, start)));
        Assert.Throws<ArgumentException>(() => new InstantInterval(end, start));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectInstant.Parse("9999-12-31T23:59:59.9999999Z").Add(new(1)));
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("ja-JP")]
    [InlineData("fr-FR")]
    public void TimeParsingIsCultureIndependent(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal("2026-10-31T00:00:00.0000000Z", ProjectInstant.Parse("2026-10-31T00:00:00.0000000Z").CanonicalText);
            Assert.Equal("2026-10-31", new ProjectLocalDate("2026-10-31", Calendar()).CanonicalText);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
}
