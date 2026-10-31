using System.Collections.ObjectModel;

namespace Coglatas.Domain.ProjectIde;

public sealed record CalendarExceptionInterval(string Kind, InstantInterval Interval);

/// <summary>Finite, materialized calendar input. Recurrence expansion and snapshot authorization belong to input providers.</summary>
public sealed class CalendarSnapshot
{
    public CalendarReference Reference { get; }
    public TimeZoneReference TimeZone { get; }
    public InstantInterval Validity { get; }
    public IReadOnlyList<InstantInterval> WorkingIntervals { get; }
    public IReadOnlyList<InstantInterval> NonWorkingIntervals { get; }
    public IReadOnlyList<CalendarExceptionInterval> Exceptions { get; }
    public SourceJson Data { get; }

    private CalendarSnapshot(SourceJson data, CalendarReference reference, TimeZoneSnapshot zone, TimeEvaluationLimits limits)
    {
        TimeContractFields.Require(data, "schemaVersion", "calendarId", "version", "timeZone", "validFrom", "validTo",
            "workingIntervals", "nonWorkingIntervals", "exceptions");
        if (SourceFields.PositiveInteger(data.Value, "schemaVersion") != 1) throw new UnsupportedSourceException("Unsupported calendar snapshot schema.");
        if (SourceFields.Token(data.Value, "calendarId") != reference.CalendarId || SourceFields.String(data.Value, "version") != reference.Version)
            throw new FormatException("Calendar snapshot identity or version mismatch.");
        TimeContractFields.Require(reference.Data, "calendarId", "version", "timeZoneId", "rulesDigest");
        TimeContractFields.Require(reference.RulesDigest.Data, "algorithm", "value");
        if (ComputeDigest(data) != reference.RulesDigest) throw new FormatException("Calendar snapshot digest mismatch.");
        TimeZone = new(TimeContractFields.Child(data, "timeZone"));
        if (reference.TimeZoneId != TimeZone.TimeZoneId || !TimeZone.Matches(zone.Reference))
            throw new FormatException("Calendar and timezone snapshots disagree.");
        Validity = TimeContractFields.Interval(data, "validFrom", "validTo");
        if (Validity.IsEmpty || !zone.Validity.Contains(Validity)) throw new FormatException("Calendar validity exceeds supplied timezone coverage.");
        var working = SourceFields.Array(data.Value, "workingIntervals");
        var nonWorking = SourceFields.Array(data.Value, "nonWorkingIntervals");
        var exceptions = SourceFields.Array(data.Value, "exceptions");
        if ((long)working.GetArrayLength() + nonWorking.GetArrayLength() + exceptions.GetArrayLength() > limits.MaximumIntervals)
            throw new TimeBudgetException();
        WorkingIntervals = ReadIntervals(data, "workingIntervals");
        NonWorkingIntervals = ReadIntervals(data, "nonWorkingIntervals");
        var exceptionList = new List<CalendarExceptionInterval>();
        foreach (var value in exceptions.EnumerateArray())
        {
            var exception = SourceJson.FromElement(value, data.Limits);
            TimeContractFields.Require(exception, "kind", "start", "end");
            var kind = SourceFields.Token(value, "kind");
            if (kind is not ("coglatas.holiday" or "coglatas.non-working" or "coglatas.working"))
                throw new UnsupportedSourceException("Unsupported calendar exception rule.");
            var interval = TimeContractFields.Interval(exception);
            if (interval.IsEmpty || !Validity.Contains(interval)) throw new FormatException("Exception interval is empty or outside calendar validity.");
            exceptionList.Add(new(kind, interval));
        }
        Exceptions = new ReadOnlyCollection<CalendarExceptionInterval>(exceptionList);
        Reference = reference; Data = data;
    }

    private IReadOnlyList<InstantInterval> ReadIntervals(SourceJson data, string name)
    {
        var intervals = new List<InstantInterval>();
        foreach (var item in SourceFields.Array(data.Value, name).EnumerateArray())
        {
            var source = SourceJson.FromElement(item, data.Limits);
            TimeContractFields.Require(source, "start", "end");
            var interval = TimeContractFields.Interval(source);
            if (interval.IsEmpty || !Validity.Contains(interval) || intervals.Count > 0 && intervals[^1].End.CompareTo(interval.Start) > 0)
                throw new FormatException("Calendar intervals must be nonempty, ordered, disjoint and within validity.");
            intervals.Add(interval);
        }
        return new ReadOnlyCollection<InstantInterval>(intervals);
    }

    public static ContentDigest ComputeDigest(SourceJson data) => ContentDigest.Compute("coglatas.calendar-snapshot/1", data.ToCanonicalBytes());
    public bool Matches(CalendarReference reference) => Reference.Data.Equals(reference.Data);

    public static TimeEvaluationResult<CalendarSnapshot> Read(SourceJson data, CalendarReference reference, TimeZoneSnapshot? zone,
        TimeEvaluationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(data); ArgumentNullException.ThrowIfNull(reference);
        limits ??= new(); limits.Validate();
        if (zone is null) return new(TimeEvaluationStatus.Unknown, null, "coglatas.timezone-snapshot-missing", data);
        try { return new(TimeEvaluationStatus.Known, new(data, reference, zone, limits), "coglatas.snapshot-validated", data); }
        catch (UnsupportedSourceException) { return new(TimeEvaluationStatus.Unsupported, null, "coglatas.unsupported-calendar-contract", data); }
        catch (TimeBudgetException) { return new(TimeEvaluationStatus.ResourceExhausted, null, "coglatas.time-budget-exhausted", data); }
        catch (Exception error) when (error is FormatException or ArgumentException or OverflowException)
        { return new(TimeEvaluationStatus.Invalid, null, "coglatas.invalid-calendar-snapshot", data); }
    }

    /// <summary>Union of declared work minus all explicit holiday/nonworking intervals. No recurrence or precedence is inferred.</summary>
    public TimeEvaluationResult<WorkingDuration> Measure(InstantInterval interval, TimeEvaluationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(interval);
        limits ??= new(); limits.Validate();
        if (!Validity.Contains(interval)) return new(TimeEvaluationStatus.Unknown, null, "coglatas.calendar-coverage-missing");
        if ((long)WorkingIntervals.Count + NonWorkingIntervals.Count + Exceptions.Count > limits.MaximumIntervals)
            return new(TimeEvaluationStatus.ResourceExhausted, null, "coglatas.time-budget-exhausted");
        try
        {
            var budget = limits.MaximumOperations;
            var work = Merge(WorkingIntervals.Concat(Exceptions.Where(item => item.Kind == "coglatas.working").Select(item => item.Interval)), ref budget);
            var excluded = Merge(NonWorkingIntervals.Concat(Exceptions.Where(item => item.Kind != "coglatas.working").Select(item => item.Interval)), ref budget);
            // Conflicting positive/negative exceptions require a declared precedence policy, rather than an inferred winner.
            foreach (var positive in Exceptions.Where(item => item.Kind == "coglatas.working"))
                foreach (var negative in excluded)
                {
                    Spend(ref budget);
                    if (positive.Interval.Overlaps(negative)) return new(TimeEvaluationStatus.Unsupported, null, "coglatas.calendar-exception-precedence-required");
                }
            long ticks = 0;
            foreach (var item in work)
            {
                Spend(ref budget);
                var start = Math.Max(item.Start.UtcTicks, interval.Start.UtcTicks);
                var end = Math.Min(item.End.UtcTicks, interval.End.UtcTicks);
                if (start >= end) continue;
                var available = end - start;
                foreach (var blocked in excluded)
                {
                    Spend(ref budget);
                    var overlapStart = Math.Max(start, blocked.Start.UtcTicks);
                    var overlapEnd = Math.Min(end, blocked.End.UtcTicks);
                    if (overlapStart < overlapEnd) available = checked(available - (overlapEnd - overlapStart));
                }
                ticks = checked(ticks + available);
            }
            return new(TimeEvaluationStatus.Known, new(ticks, Reference), "coglatas.working-duration-measured");
        }
        catch (TimeBudgetException) { return new(TimeEvaluationStatus.ResourceExhausted, null, "coglatas.time-budget-exhausted"); }
        catch (OverflowException) { return new(TimeEvaluationStatus.Invalid, null, "coglatas.duration-overflow"); }
    }

    private static IReadOnlyList<InstantInterval> Merge(IEnumerable<InstantInterval> intervals, ref int budget)
    {
        var result = new List<InstantInterval>();
        foreach (var interval in intervals.OrderBy(item => item.Start.UtcTicks).ThenBy(item => item.End.UtcTicks))
        {
            Spend(ref budget);
            if (result.Count == 0 || result[^1].End.CompareTo(interval.Start) < 0) result.Add(interval);
            else if (result[^1].End.CompareTo(interval.End) < 0) result[^1] = new(result[^1].Start, interval.End);
        }
        return result;
    }

    private static void Spend(ref int budget)
    {
        if (--budget < 0) throw new TimeBudgetException();
    }
}

/// <summary>Typed endpoints, explicit state and identical civil context are required for interval relations.</summary>
public sealed class ProjectTimeInterval
{
    public TypedProjectValue Start { get; }
    public TypedProjectValue End { get; }

    public ProjectTimeInterval(TypedProjectValue start, TypedProjectValue end)
    {
        ArgumentNullException.ThrowIfNull(start); ArgumentNullException.ThrowIfNull(end);
        if (start.Type.Kind != end.Type.Kind || start.Type.Kind is not ("coglatas.instant" or "coglatas.local-date" or "coglatas.local-date-time"))
            throw new ArgumentException("Interval endpoints require the same comparable time domain.");
        if (start.State == ValueState.Known && end.State == ValueState.Known && Compare(start, end) > 0)
            throw new ArgumentException("Interval end precedes start.");
        Start = start; End = end;
    }

    public TimeEvaluationResult<bool> Overlaps(ProjectTimeInterval other) => Relate(other, "coglatas.overlaps");
    public TimeEvaluationResult<bool> Contains(ProjectTimeInterval other) => Relate(other, "coglatas.contains");
    public TimeEvaluationResult<bool> IsAdjacentTo(ProjectTimeInterval other) => Relate(other, "coglatas.adjacent");

    private TimeEvaluationResult<bool> Relate(ProjectTimeInterval other, string relation)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Start.Type.Kind != other.Start.Type.Kind) return new(TimeEvaluationStatus.Invalid, false, "coglatas.time-domain-mismatch");
        var endpoints = new[] { Start, End, other.Start, other.End };
        if (endpoints.Any(item => item.State == ValueState.Unknown)) return new(TimeEvaluationStatus.Unknown, false, "coglatas.unknown-endpoint");
        if (endpoints.Any(item => item.State == ValueState.Null)) return new(TimeEvaluationStatus.Null, false, "coglatas.null-endpoint");
        try
        {
            _ = Compare(Start, other.Start);
            var empty = Compare(Start, End) == 0 || Compare(other.Start, other.End) == 0;
            var value = relation switch
            {
                "coglatas.overlaps" => !empty && Compare(Start, other.End) < 0 && Compare(other.Start, End) < 0,
                "coglatas.contains" => Compare(Start, other.Start) <= 0 && Compare(other.End, End) <= 0,
                "coglatas.adjacent" => !empty && (Compare(End, other.Start) == 0 || Compare(other.End, Start) == 0),
                _ => throw new UnsupportedSourceException("Unsupported interval relation.")
            };
            return new(TimeEvaluationStatus.Known, value, relation);
        }
        catch (ArgumentException) { return new(TimeEvaluationStatus.Invalid, false, "coglatas.calendar-context-mismatch"); }
    }

    private static int Compare(TypedProjectValue left, TypedProjectValue right) => (left.KnownValue, right.KnownValue) switch
    {
        (ProjectInstant first, ProjectInstant second) => first.CompareTo(second),
        (ProjectLocalDate first, ProjectLocalDate second) when first.Calendar.Data.Equals(second.Calendar.Data) =>
            StringComparer.Ordinal.Compare(first.CanonicalText, second.CanonicalText),
        (ProjectLocalDateTime first, ProjectLocalDateTime second) => first.CivilTicks.CompareTo(second.CivilTicks),
        _ => throw new ArgumentException("Incomparable time domain or calendar context.")
    };
}
