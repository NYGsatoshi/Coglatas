using System.Collections.ObjectModel;
using System.Globalization;

namespace Coglatas.Domain.ProjectIde;

public enum TimeEvaluationStatus { Known, Null, Unknown, Invalid, Unsupported, Ambiguous, ResourceExhausted }

/// <summary>A pure evaluation result, separate from compilation acceptance or snapshot authorization.</summary>
public sealed class TimeEvaluationResult<T>
{
    public TimeEvaluationStatus Status { get; }
    public T? Value { get; }
    public string Reason { get; }
    public SourceJson? Data { get; }
    public IReadOnlyList<T> Candidates { get; }

    internal TimeEvaluationResult(TimeEvaluationStatus status, T? value, string reason, SourceJson? data = null,
        IEnumerable<T>? candidates = null)
    {
        Status = status; Value = value; Reason = reason; Data = data;
        Candidates = new ReadOnlyCollection<T>((candidates ?? []).ToArray());
    }
}

/// <summary>Provisional deterministic processing bounds; these are not approved device or production capacities.</summary>
public sealed record TimeEvaluationLimits(int MaximumIntervals = 4096, int MaximumOperations = 100_000)
{
    internal void Validate()
    {
        if (MaximumIntervals < 1 || MaximumOperations < 1) throw new ArgumentOutOfRangeException(nameof(TimeEvaluationLimits));
    }
}

/// <summary>Exact zone rules identity. The digest pins a supplied finite snapshot, never an OS database.</summary>
public sealed class TimeZoneReference
{
    public string TimeZoneId { get; }
    public string Version { get; }
    public ContentDigest RulesDigest { get; }
    public SourceJson Data { get; }

    public TimeZoneReference(SourceJson data)
    {
        ArgumentNullException.ThrowIfNull(data);
        TimeContractFields.Require(data, "timeZoneId", "version", "rulesDigest");
        TimeZoneId = TimeValueFields.Nonempty(data, "timeZoneId");
        Version = TimeValueFields.Nonempty(data, "version");
        RulesDigest = ContentDigest.Parse(TimeContractFields.Child(data, "rulesDigest"));
        TimeContractFields.Require(RulesDigest.Data, "algorithm", "value");
        Data = data;
    }

    public bool Matches(TimeZoneReference other) => Data.Equals(other.Data);
}

/// <summary>Civil fields and pinned rules with an optional explicit offset; construction is not resolution.</summary>
public sealed class ZonedCivilTime
{
    public ProjectLocalDateTime Local { get; }
    public TimeZoneReference TimeZone { get; }
    public long? OffsetTicks { get; }
    public SourceJson Data { get; }

    public ZonedCivilTime(SourceJson data)
    {
        ArgumentNullException.ThrowIfNull(data);
        TimeContractFields.Require(data, "local", "timeZone", "offsetTicks");
        Local = new(SourceFields.String(data.Value, "local"));
        TimeZone = new(TimeContractFields.Child(data, "timeZone"));
        if (data.Value.TryGetProperty("offsetTicks", out var offset))
        {
            OffsetTicks = TimeValueFields.Ticks(ValueFields.String(offset));
            TimeContractFields.RequireOffset(OffsetTicks.Value);
        }
        Data = data;
    }
}

public sealed record TimeZoneSegment(InstantInterval Interval, long OffsetTicks);

public sealed class TimeZoneSnapshot
{
    public TimeZoneReference Reference { get; }
    public InstantInterval Validity { get; }
    public IReadOnlyList<TimeZoneSegment> Segments { get; }
    public SourceJson Data { get; }

    private TimeZoneSnapshot(SourceJson data, TimeZoneReference reference, TimeEvaluationLimits limits)
    {
        TimeContractFields.Require(data, "schemaVersion", "timeZoneId", "version", "validFrom", "validTo", "segments");
        if (SourceFields.PositiveInteger(data.Value, "schemaVersion") != 1) throw new UnsupportedSourceException("Unsupported timezone snapshot schema.");
        if (SourceFields.String(data.Value, "timeZoneId") != reference.TimeZoneId || SourceFields.String(data.Value, "version") != reference.Version)
            throw new FormatException("Timezone snapshot identity or version mismatch.");
        if (ComputeDigest(data) != reference.RulesDigest) throw new FormatException("Timezone snapshot digest mismatch.");
        Validity = TimeContractFields.Interval(data, "validFrom", "validTo");
        if (Validity.IsEmpty) throw new FormatException("Timezone validity must be nonempty.");
        var items = SourceFields.Array(data.Value, "segments");
        if (items.GetArrayLength() > limits.MaximumIntervals) throw new TimeBudgetException();
        var segments = new List<TimeZoneSegment>();
        var previous = Validity.Start;
        foreach (var item in items.EnumerateArray())
        {
            var segment = SourceJson.FromElement(item, data.Limits);
            TimeContractFields.Require(segment, "start", "end", "offsetTicks");
            var interval = TimeContractFields.Interval(segment);
            var offset = TimeValueFields.Ticks(SourceFields.String(item, "offsetTicks"));
            TimeContractFields.RequireOffset(offset);
            if (interval.IsEmpty || interval.Start != previous || !Validity.Contains(interval))
                throw new FormatException("Timezone segments must form ordered contiguous validity coverage.");
            _ = ProjectLocalDateTime.FromTicks(checked(interval.Start.UtcTicks + offset));
            _ = ProjectLocalDateTime.FromTicks(checked(interval.End.UtcTicks + offset));
            segments.Add(new(interval, offset)); previous = interval.End;
        }
        if (previous != Validity.End) throw new FormatException("Timezone snapshot has incomplete coverage.");
        Data = data; Reference = reference; Segments = new ReadOnlyCollection<TimeZoneSegment>(segments);
    }

    public static ContentDigest ComputeDigest(SourceJson data) => ContentDigest.Compute("coglatas.timezone-snapshot/1", data.ToCanonicalBytes());

    public static TimeEvaluationResult<TimeZoneSnapshot> Read(SourceJson data, TimeZoneReference reference, TimeEvaluationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(data); ArgumentNullException.ThrowIfNull(reference);
        limits ??= new(); limits.Validate();
        try { return new(TimeEvaluationStatus.Known, new(data, reference, limits), "coglatas.snapshot-validated", data); }
        catch (UnsupportedSourceException) { return new(TimeEvaluationStatus.Unsupported, null, "coglatas.unsupported-timezone-contract", data); }
        catch (TimeBudgetException) { return new(TimeEvaluationStatus.ResourceExhausted, null, "coglatas.time-budget-exhausted", data); }
        catch (Exception error) when (error is FormatException or ArgumentException or OverflowException)
        { return new(TimeEvaluationStatus.Invalid, null, "coglatas.invalid-timezone-snapshot", data); }
    }

    public TimeEvaluationResult<ProjectInstant> Resolve(ZonedCivilTime civil, TimeEvaluationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(civil);
        limits ??= new(); limits.Validate();
        if (!Reference.Matches(civil.TimeZone)) return new(TimeEvaluationStatus.Invalid, null, "coglatas.timezone-context-mismatch", civil.Data);
        if (Segments.Count > limits.MaximumOperations) return new(TimeEvaluationStatus.ResourceExhausted, null, "coglatas.time-budget-exhausted", civil.Data);
        var candidates = new List<ProjectInstant>();
        var minimum = long.MaxValue; var maximum = long.MinValue;
        foreach (var segment in Segments)
        {
            minimum = Math.Min(minimum, checked(segment.Interval.Start.UtcTicks + segment.OffsetTicks));
            maximum = Math.Max(maximum, checked(segment.Interval.End.UtcTicks + segment.OffsetTicks));
            var utcTicks = checked(civil.Local.CivilTicks - segment.OffsetTicks);
            if (utcTicks >= segment.Interval.Start.UtcTicks && utcTicks < segment.Interval.End.UtcTicks &&
                (!civil.OffsetTicks.HasValue || civil.OffsetTicks.Value == segment.OffsetTicks)) candidates.Add(ProjectInstant.FromTicks(utcTicks));
        }
        candidates.Sort();
        if (candidates.Count == 1) return new(TimeEvaluationStatus.Known, candidates[0], "coglatas.local-time-resolved", civil.Data, candidates);
        if (candidates.Count > 1) return new(TimeEvaluationStatus.Ambiguous, null, "coglatas.dst-overlap", civil.Data, candidates);
        return civil.Local.CivilTicks < minimum || civil.Local.CivilTicks >= maximum
            ? new(TimeEvaluationStatus.Unknown, null, "coglatas.timezone-coverage-missing", civil.Data)
            : new(TimeEvaluationStatus.Invalid, null, "coglatas.local-time-gap-or-offset-mismatch", civil.Data);
    }

    public TimeEvaluationResult<ZonedCivilTime> ToCivil(ProjectInstant instant)
    {
        ArgumentNullException.ThrowIfNull(instant);
        var segment = Segments.FirstOrDefault(item => item.Interval.Contains(instant));
        if (segment is null) return new(TimeEvaluationStatus.Unknown, null, "coglatas.timezone-coverage-missing");
        var local = ProjectLocalDateTime.FromTicks(checked(instant.UtcTicks + segment.OffsetTicks));
        var data = SourceJson.FromObject(new { local = local.CanonicalText, timeZone = Reference.Data.Value,
            offsetTicks = segment.OffsetTicks.ToString(CultureInfo.InvariantCulture) });
        return new(TimeEvaluationStatus.Known, new(data), "coglatas.instant-converted", data);
    }
}

public static class ProjectTimeArithmetic
{
    public static TimeEvaluationResult<ElapsedDuration> ElapsedBetween(TypedProjectValue start, TypedProjectValue end)
    {
        ArgumentNullException.ThrowIfNull(start); ArgumentNullException.ThrowIfNull(end);
        if (start.Type.Kind != "coglatas.instant" || end.Type.Kind != "coglatas.instant")
            return new(TimeEvaluationStatus.Invalid, null, "coglatas.time-domain-mismatch");
        if (start.State == ValueState.Unknown || end.State == ValueState.Unknown)
            return new(TimeEvaluationStatus.Unknown, null, "coglatas.unknown-endpoint");
        if (start.State == ValueState.Null || end.State == ValueState.Null)
            return new(TimeEvaluationStatus.Null, null, "coglatas.null-endpoint");
        return new(TimeEvaluationStatus.Known, ((ProjectInstant)end.KnownValue!).Since((ProjectInstant)start.KnownValue!), "coglatas.elapsed-difference");
    }

    public static TimeEvaluationResult<ProjectLocalDate> ApplyPeriod(ProjectLocalDate anchor, CalendarPeriod period,
        CalendarSnapshot? snapshot, string resolutionPolicy)
    {
        ArgumentNullException.ThrowIfNull(anchor); ArgumentNullException.ThrowIfNull(period);
        if (snapshot is null) return new(TimeEvaluationStatus.Unknown, null, "coglatas.calendar-snapshot-missing");
        if (!snapshot.Matches(anchor.Calendar)) return new(TimeEvaluationStatus.Invalid, null, "coglatas.calendar-context-mismatch");
        if (resolutionPolicy != "coglatas.reject-invalid-day") return new(TimeEvaluationStatus.Unsupported, null, "coglatas.unsupported-period-resolution");
        try
        {
            var date = DateOnly.ParseExact(anchor.CanonicalText, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var totalMonths = checked((long)(date.Year - 1) * 12 + date.Month - 1 + (long)period.Years * 12 + period.Months);
            if (totalMonths is < 0 or >= 9999 * 12) throw new ArgumentOutOfRangeException(nameof(period));
            var year = (int)(totalMonths / 12) + 1; var month = (int)(totalMonths % 12) + 1;
            var result = new DateOnly(year, month, date.Day).AddDays(period.Days);
            if (!snapshot.CoversDate(date) || !snapshot.CoversDate(result))
                return new(TimeEvaluationStatus.Unknown, null, "coglatas.calendar-date-coverage-missing");
            return new(TimeEvaluationStatus.Known, new(result.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), anchor.Calendar), "coglatas.anchored-period-applied");
        }
        catch (Exception error) when (error is ArgumentException or OverflowException)
        { return new(TimeEvaluationStatus.Invalid, null, "coglatas.invalid-calendar-period"); }
    }
}

internal sealed class TimeBudgetException : Exception;

internal static class TimeContractFields
{
    internal static void Require(SourceJson data, params string[] fields)
    {
        if (data.Value.ValueKind != System.Text.Json.JsonValueKind.Object) throw new FormatException("Expected a time contract object.");
        if (ValueFields.HasUnknown(data.Value, fields)) throw new UnsupportedSourceException("Unknown time contract members are preserved without evaluation.");
    }

    internal static SourceJson Child(SourceJson data, string name) => SourceJson.FromElement(SourceFields.Object(data.Value, name), data.Limits);

    internal static InstantInterval Interval(SourceJson data, string start = "start", string end = "end") =>
        new(ProjectInstant.Parse(SourceFields.String(data.Value, start)), ProjectInstant.Parse(SourceFields.String(data.Value, end)));

    internal static void RequireOffset(long ticks)
    {
        if (ticks <= -TimeSpan.TicksPerDay || ticks >= TimeSpan.TicksPerDay)
            throw new FormatException("Timezone offset must have magnitude below one day.");
    }
}
