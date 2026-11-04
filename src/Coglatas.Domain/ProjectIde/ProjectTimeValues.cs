using System.Globalization;

namespace Coglatas.Domain.ProjectIde;

public sealed record ElapsedDuration(long Ticks)
{
    public static ElapsedDuration Parse(string text) => new(TimeValueFields.Ticks(text));
    public ElapsedDuration Add(ElapsedDuration other) => new(checked(Ticks + other.Ticks));
    public string CanonicalText => Ticks.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Identity of pinned calendar rules, never an ambient machine calendar/timezone.</summary>
public sealed class CalendarReference
{
    public string CalendarId { get; }
    public string Version { get; }
    public string TimeZoneId { get; }
    public ContentDigest RulesDigest { get; }
    public SourceJson Data { get; }

    public CalendarReference(SourceJson data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
        CalendarId = SourceFields.Token(data.Value, "calendarId");
        Version = TimeValueFields.Nonempty(data, "version");
        TimeZoneId = TimeValueFields.Nonempty(data, "timeZoneId");
        RulesDigest = ContentDigest.Parse(SourceJson.FromElement(SourceFields.Object(data.Value, "rulesDigest"), data.Limits));
    }
}

public sealed record WorkingDuration
{
    public long Ticks { get; }
    public CalendarReference Calendar { get; }
    public WorkingDuration(long ticks, CalendarReference calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        Ticks = ticks; Calendar = calendar;
    }
}

/// <summary>Civil components; no conversion to elapsed work without an explicit anchor and calendar policy.</summary>
public sealed record CalendarPeriod(int Years, int Months, int Days);

public sealed record ProjectInstant : IComparable<ProjectInstant>
{
    private const string Pattern = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
    public string CanonicalText { get; }
    public long UtcTicks { get; }

    private ProjectInstant(string text, long ticks) { CanonicalText = text; UtcTicks = ticks; }
    public static ProjectInstant Parse(string text)
    {
        if (!DateTimeOffset.TryParseExact(text, Pattern, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value) ||
            value.ToString(Pattern, CultureInfo.InvariantCulture) != text)
            throw new FormatException("Instant requires canonical UTC with exactly seven fractional digits.");
        return new(text, value.UtcTicks);
    }

    public ProjectInstant Add(ElapsedDuration duration)
    {
        ArgumentNullException.ThrowIfNull(duration);
        var ticks = checked(UtcTicks + duration.Ticks);
        var value = new DateTimeOffset(ticks, TimeSpan.Zero);
        return new(value.ToString(Pattern, CultureInfo.InvariantCulture), ticks);
    }

    public int CompareTo(ProjectInstant? other) => other is null ? 1 : UtcTicks.CompareTo(other.UtcTicks);
}

public sealed record ProjectLocalDate
{
    public string CanonicalText { get; }
    public CalendarReference Calendar { get; }

    public ProjectLocalDate(string text, CalendarReference calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ||
            value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) != text)
            throw new FormatException("LocalDate requires a valid Gregorian YYYY-MM-DD date.");
        CanonicalText = text; Calendar = calendar;
    }
}

/// <summary>Half-open [start,end); equal endpoints represent an empty interval.</summary>
public sealed record InstantInterval
{
    public ProjectInstant Start { get; }
    public ProjectInstant End { get; }

    public InstantInterval(ProjectInstant start, ProjectInstant end)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);
        if (start.CompareTo(end) > 0) throw new ArgumentException("Interval end precedes start.");
        Start = start; End = end;
    }

    public bool Overlaps(InstantInterval other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Start.CompareTo(End) < 0 && other.Start.CompareTo(other.End) < 0 &&
               Start.CompareTo(other.End) < 0 && other.Start.CompareTo(End) < 0;
    }
}

internal static class TimeValueFields
{
    internal static string Nonempty(SourceJson data, string field)
    {
        var value = SourceFields.String(data.Value, field);
        if (string.IsNullOrWhiteSpace(value)) throw new FormatException("A pinned time reference requires nonempty fields.");
        return value;
    }

    internal static long Ticks(string text)
    {
        if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ||
            value.ToString(CultureInfo.InvariantCulture) != text)
            throw new FormatException("Duration ticks require a canonical signed 64-bit integer string.");
        return value;
    }
}
