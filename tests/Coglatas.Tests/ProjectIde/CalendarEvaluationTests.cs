using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class CalendarEvaluationTests
{
    private static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private static ProjectInstant Instant(string value) => ProjectInstant.Parse(value + ".0000000Z");
    private static object Range(string start, string end) => new { start = start + ".0000000Z", end = end + ".0000000Z" };

    private static (SourceJson Data, TimeZoneReference Reference, TimeZoneSnapshot Snapshot) Zone()
    {
        var data = Json(new
        {
            schemaVersion = 1, timeZoneId = "America/New_York", version = "fixture-2026a",
            validFrom = "2026-01-01T00:00:00.0000000Z", validTo = "2027-01-02T00:00:00.0000000Z",
            segments = new[]
            {
                new { start = "2026-01-01T00:00:00.0000000Z", end = "2026-03-08T07:00:00.0000000Z", offsetTicks = "-180000000000" },
                new { start = "2026-03-08T07:00:00.0000000Z", end = "2026-11-01T06:00:00.0000000Z", offsetTicks = "-144000000000" },
                new { start = "2026-11-01T06:00:00.0000000Z", end = "2027-01-02T00:00:00.0000000Z", offsetTicks = "-180000000000" }
            }
        });
        var reference = new TimeZoneReference(Json(new { timeZoneId = "America/New_York", version = "fixture-2026a", rulesDigest = TimeZoneSnapshot.ComputeDigest(data).Data.Value }));
        var result = TimeZoneSnapshot.Read(data, reference);
        Assert.Equal(TimeEvaluationStatus.Known, result.Status);
        return (data, reference, Assert.IsType<TimeZoneSnapshot>(result.Value));
    }

    private static ZonedCivilTime Civil(TimeZoneReference zone, string local, string? offset = null)
    {
        var value = new Dictionary<string, object?> { ["local"] = local + ".0000000", ["timeZone"] = zone.Data.Value };
        if (offset is not null) value["offsetTicks"] = offset;
        return new(Json(value));
    }

    private static (SourceJson Data, CalendarReference Reference, CalendarSnapshot Snapshot) Calendar(object[]? exceptions = null)
    {
        var zone = Zone();
        var data = Json(new
        {
            schemaVersion = 1, calendarId = "coglatas.fixture-calendar", version = "fixture-1", timeZone = zone.Reference.Data.Value,
            validFrom = "2026-01-01T00:00:00.0000000Z", validTo = "2027-01-01T00:00:00.0000000Z",
            workingIntervals = new[] { Range("2026-07-01T13:00:00", "2026-07-01T17:00:00"), Range("2026-07-01T18:00:00", "2026-07-01T22:00:00") },
            nonWorkingIntervals = new[] { Range("2026-07-01T14:00:00", "2026-07-01T15:00:00") }, exceptions = exceptions ?? []
        });
        var reference = CalendarRef(data);
        var result = CalendarSnapshot.Read(data, reference, zone.Snapshot);
        Assert.Equal(TimeEvaluationStatus.Known, result.Status);
        return (data, reference, Assert.IsType<CalendarSnapshot>(result.Value));
    }

    private static CalendarReference CalendarRef(SourceJson data) => new(Json(new
    {
        calendarId = "coglatas.fixture-calendar", version = "fixture-1", timeZoneId = "America/New_York",
        rulesDigest = CalendarSnapshot.ComputeDigest(data).Data.Value
    }));

    private static TypedProjectValue Value(string kind, string state, object? value = null)
    {
        var data = new Dictionary<string, object?> { ["type"] = new { kind, schemaVersion = 1, nullable = true, acceptsUnknown = true }, ["state"] = state };
        if (state == "coglatas.known") data["value"] = value;
        if (state == "coglatas.unknown") { data["reason"] = "coglatas.fixture-missing"; data["basis"] = new { fixture = true }; }
        return Assert.IsType<TypedProjectValue>(TypedProjectValueCodec.Decode(Json(data)).Value);
    }

    [Theory]
    [InlineData("2026-07-01T12:00:00", "2026-07-01T16:00:00.0000000Z")]
    [InlineData("2026-01-01T12:00:00", "2026-01-01T17:00:00.0000000Z")]
    [InlineData("2026-12-31T23:59:59", "2027-01-01T04:59:59.0000000Z")]
    public void SuppliedZoneConvertsCivilTimeExactly(string local, string expected)
    {
        var zone = Zone();
        var result = zone.Snapshot.Resolve(Civil(zone.Reference, local));
        Assert.Equal(TimeEvaluationStatus.Known, result.Status);
        Assert.Equal(expected, result.Value!.CanonicalText);
        var inverse = zone.Snapshot.ToCivil(result.Value);
        Assert.Equal(TimeEvaluationStatus.Known, inverse.Status);
        Assert.Equal(local + ".0000000", inverse.Value!.Local.CanonicalText);
    }

    [Fact]
    public void GapIsInvalidAndOverlapRetainsBothCandidatesUntilExplicitOffset()
    {
        var zone = Zone();
        Assert.Equal(TimeEvaluationStatus.Invalid, zone.Snapshot.Resolve(Civil(zone.Reference, "2026-03-08T02:30:00")).Status);
        var overlap = zone.Snapshot.Resolve(Civil(zone.Reference, "2026-11-01T01:30:00"));
        Assert.Equal(TimeEvaluationStatus.Ambiguous, overlap.Status); Assert.Null(overlap.Value);
        Assert.Equal(new[] { "2026-11-01T05:30:00.0000000Z", "2026-11-01T06:30:00.0000000Z" }, overlap.Candidates.Select(value => value.CanonicalText));
        Assert.Equal("2026-11-01T05:30:00.0000000Z", zone.Snapshot.Resolve(Civil(zone.Reference, "2026-11-01T01:30:00", "-144000000000")).Value!.CanonicalText);
        Assert.Equal("2026-11-01T06:30:00.0000000Z", zone.Snapshot.Resolve(Civil(zone.Reference, "2026-11-01T01:30:00", "-180000000000")).Value!.CanonicalText);
        Assert.Equal(TimeEvaluationStatus.Invalid, zone.Snapshot.Resolve(Civil(zone.Reference, "2026-11-01T01:30:00", "0")).Status);
    }

    [Fact]
    public void MissingCoverageIsUnknownAndBudgetExhaustionIsExplicit()
    {
        var zone = Zone();
        Assert.Equal(TimeEvaluationStatus.Unknown, zone.Snapshot.Resolve(Civil(zone.Reference, "2028-01-01T00:00:00")).Status);
        Assert.Equal(TimeEvaluationStatus.Unknown, zone.Snapshot.ToCivil(Instant("2028-01-01T00:00:00")).Status);
        Assert.Equal(TimeEvaluationStatus.ResourceExhausted, zone.Snapshot.Resolve(Civil(zone.Reference, "2026-07-01T12:00:00"), new(MaximumOperations: 1)).Status);
        Assert.Equal(TimeEvaluationStatus.ResourceExhausted, TimeZoneSnapshot.Read(zone.Data, zone.Reference, new(MaximumIntervals: 1)).Status);
    }

    [Theory]
    [InlineData("version", "different-version")]
    [InlineData("timeZoneId", "Etc/UTC")]
    public void ZoneSnapshotRejectsIdentityOrVersionDrift(string field, string replacement)
    {
        var zone = Zone(); var node = JsonNode.Parse(zone.Reference.Data.CanonicalText)!; node[field] = replacement;
        var reference = new TimeZoneReference(SourceJson.Parse(node.ToJsonString()));
        Assert.Equal(TimeEvaluationStatus.Invalid, TimeZoneSnapshot.Read(zone.Data, reference).Status);
        Assert.Equal(TimeEvaluationStatus.Invalid, zone.Snapshot.Resolve(Civil(reference, "2026-07-01T12:00:00")).Status);
    }

    [Theory]
    [InlineData("offsetTicks", "864000000000")]
    [InlineData("start", "2026-01-01T01:00:00.0000000Z")]
    [InlineData("end", "2026-03-09T07:00:00.0000000Z")]
    public void ZoneSnapshotRejectsInvalidOffsetsGapsAndOverlaps(string field, string replacement)
    {
        var zone = Zone(); var node = JsonNode.Parse(zone.Data.CanonicalText)!; node["segments"]![0]![field] = replacement;
        var data = SourceJson.Parse(node.ToJsonString());
        var reference = new TimeZoneReference(Json(new { timeZoneId = "America/New_York", version = "fixture-2026a", rulesDigest = TimeZoneSnapshot.ComputeDigest(data).Data.Value }));
        Assert.Equal(TimeEvaluationStatus.Invalid, TimeZoneSnapshot.Read(data, reference).Status);
    }

    [Fact]
    public void SnapshotDigestMismatchDoesNotObtainKnownValue()
    {
        var zone = Zone(); var node = JsonNode.Parse(zone.Data.CanonicalText)!; node["version"] = "tampered";
        var result = TimeZoneSnapshot.Read(SourceJson.Parse(node.ToJsonString()), zone.Reference);
        Assert.Equal(TimeEvaluationStatus.Invalid, result.Status); Assert.Null(result.Value); Assert.NotNull(result.Data);
        var calendar = Calendar(); var calendarNode = JsonNode.Parse(calendar.Data.CanonicalText)!;
        calendarNode["workingIntervals"]![0]!["end"] = "2026-07-01T16:00:00.0000000Z";
        Assert.Equal(TimeEvaluationStatus.Invalid, CalendarSnapshot.Read(SourceJson.Parse(calendarNode.ToJsonString()), calendar.Reference, zone.Snapshot).Status);
    }

    [Fact]
    public void CalendarMeasuresMultipleWorkingIntervalsAndExcludesHolidayAndNonworking()
    {
        var calendar = Calendar([new { kind = "coglatas.holiday", start = "2026-07-01T19:00:00.0000000Z", end = "2026-07-01T20:00:00.0000000Z" }]);
        var interval = new InstantInterval(Instant("2026-07-01T12:00:00"), Instant("2026-07-01T23:00:00"));
        var result = calendar.Snapshot.Measure(interval);
        Assert.Equal(TimeEvaluationStatus.Known, result.Status); Assert.Equal(6 * TimeSpan.TicksPerHour, result.Value!.Ticks);
        Assert.Same(calendar.Reference, result.Value.Calendar);
        Assert.Equal(TimeEvaluationStatus.ResourceExhausted, calendar.Snapshot.Measure(interval, new(MaximumOperations: 1)).Status);
        Assert.Equal(TimeEvaluationStatus.Unknown, calendar.Snapshot.Measure(new(Instant("2025-01-01T00:00:00"), Instant("2026-07-01T23:00:00"))).Status);
        Assert.Equal(0, calendar.Snapshot.Measure(new(interval.Start, interval.Start)).Value!.Ticks);
    }

    [Fact]
    public void ConflictingExceptionPrecedenceRemainsUnsupported()
    {
        var calendar = Calendar([new { kind = "coglatas.working", start = "2026-07-01T14:00:00.0000000Z", end = "2026-07-01T15:00:00.0000000Z" }]);
        Assert.Equal(TimeEvaluationStatus.Unsupported, calendar.Snapshot.Measure(new(Instant("2026-07-01T12:00:00"), Instant("2026-07-01T23:00:00"))).Status);
    }

    [Theory]
    [InlineData("recurrence", "future.weekly-rule")]
    [InlineData("futureExtension", "preserve-this")]
    public void UnknownCalendarMembersArePreservedUnsupported(string field, string value)
    {
        var calendar = Calendar(); var node = JsonNode.Parse(calendar.Data.CanonicalText)!; node[field] = value;
        var data = SourceJson.Parse(node.ToJsonString());
        var result = CalendarSnapshot.Read(data, CalendarRef(data), Zone().Snapshot);
        Assert.Equal(TimeEvaluationStatus.Unsupported, result.Status); Assert.Null(result.Value);
        Assert.Equal(data.CanonicalText, result.Data!.CanonicalText);
    }

    [Fact]
    public void CalendarRequiresMatchingZoneSnapshotAndReference()
    {
        var calendar = Calendar(); var zone = Zone();
        Assert.Equal(TimeEvaluationStatus.Unknown, CalendarSnapshot.Read(calendar.Data, calendar.Reference, null).Status);
        var node = JsonNode.Parse(calendar.Data.CanonicalText)!; node["timeZone"]!["version"] = "future";
        var data = SourceJson.Parse(node.ToJsonString());
        Assert.Equal(TimeEvaluationStatus.Invalid, CalendarSnapshot.Read(data, CalendarRef(data), zone.Snapshot).Status);
        Assert.Equal(TimeEvaluationStatus.ResourceExhausted, CalendarSnapshot.Read(calendar.Data, calendar.Reference, zone.Snapshot, new(MaximumIntervals: 1)).Status);
    }

    [Theory]
    [InlineData("2026-02-29T01:00:00.0000000")]
    [InlineData("2026-07-01T01:00:00.0000000Z")]
    [InlineData("2026-07-01T01:00:00")]
    [InlineData("2026-07-01T01:00:60.0000000")]
    [InlineData("2026-07-01T01:00:00.00000001")]
    public void CivilTimeRejectsInvalidDatesOffsetsAndResolution(string value) => Assert.Throws<FormatException>(() => new ProjectLocalDateTime(value));

    [Theory]
    [InlineData("2026-12-31", 0, 0, 1, "2027-01-01")]
    [InlineData("2024-02-28", 0, 0, 1, "2024-02-29")]
    [InlineData("2026-01-31", 0, 1, 0, null)]
    [InlineData("2024-02-29", 1, 0, 0, null)]
    [InlineData("9999-12-31", 0, 0, 1, null)]
    [InlineData("2026-07-01", 2147483647, 0, 0, null)]
    public void AnchoredPeriodUsesExplicitRejectPolicyWithoutMonthClamping(string anchor, int years, int months, int days, string? expected)
    {
        var calendar = Calendar(); var date = new ProjectLocalDate(anchor, calendar.Reference);
        var result = ProjectTimeArithmetic.ApplyPeriod(date, new(years, months, days), calendar.Snapshot, "coglatas.reject-invalid-day");
        Assert.Equal(expected is null ? TimeEvaluationStatus.Invalid : TimeEvaluationStatus.Known, result.Status);
        Assert.Equal(expected, result.Value?.CanonicalText);
        Assert.Equal(TimeEvaluationStatus.Unknown, ProjectTimeArithmetic.ApplyPeriod(date, new(0, 1, 0), null, "coglatas.reject-invalid-day").Status);
        Assert.Equal(TimeEvaluationStatus.Unsupported, ProjectTimeArithmetic.ApplyPeriod(date, new(0, 1, 0), calendar.Snapshot, "future.clamp").Status);
    }

    [Fact]
    public void IntervalsAreHalfOpenAndUnknownIsNotInfinity()
    {
        TypedProjectValue Known(string instant) => Value("coglatas.instant", "coglatas.known", instant + ".0000000Z");
        var first = new ProjectTimeInterval(Known("2026-07-01T00:00:00"), Known("2026-07-02T00:00:00"));
        var next = new ProjectTimeInterval(first.End, Known("2026-07-03T00:00:00"));
        var empty = new ProjectTimeInterval(first.Start, first.Start);
        Assert.False(first.Overlaps(next).Value); Assert.True(first.IsAdjacentTo(next).Value);
        Assert.False(first.Overlaps(empty).Value); Assert.True(first.Contains(empty).Value);
        var unknown = new ProjectTimeInterval(first.Start, Value("coglatas.instant", "coglatas.unknown"));
        Assert.Equal(TimeEvaluationStatus.Unknown, unknown.Contains(next).Status);
        var nullEnd = new ProjectTimeInterval(first.Start, Value("coglatas.instant", "coglatas.null"));
        Assert.Equal(TimeEvaluationStatus.Null, nullEnd.Contains(next).Status);
        Assert.Throws<ArgumentException>(() => new ProjectTimeInterval(first.End, first.Start));
        Assert.Equal(TimeSpan.TicksPerDay, ProjectTimeArithmetic.ElapsedBetween(first.Start, first.End).Value!.Ticks);
        Assert.Equal(TimeEvaluationStatus.Unknown, ProjectTimeArithmetic.ElapsedBetween(first.Start, unknown.End).Status);
        Assert.Equal(TimeEvaluationStatus.Null, ProjectTimeArithmetic.ElapsedBetween(first.Start, nullEnd.End).Status);
        Assert.Equal(first.Data.CanonicalText, new ProjectTimeInterval(SourceJson.Parse(first.Data.ToCanonicalBytes())).Data.CanonicalText);
    }

    [Fact]
    public void CivilIntervalsRejectCalendarAndDomainMismatch()
    {
        var calendar = Calendar();
        TypedProjectValue Date(string text, CalendarReference reference) => Value("coglatas.local-date", "coglatas.known", new { date = text, calendar = reference.Data.Value });
        var start = Date("2026-07-01", calendar.Reference); var end = Date("2026-07-02", calendar.Reference);
        var node = JsonNode.Parse(calendar.Reference.Data.CanonicalText)!; node["version"] = "different";
        var different = new CalendarReference(SourceJson.Parse(node.ToJsonString()));
        Assert.Throws<ArgumentException>(() => new ProjectTimeInterval(start, Date("2026-07-02", different)));
        var first = new ProjectTimeInterval(start, end);
        var second = new ProjectTimeInterval(Date("2026-07-01", different), Date("2026-07-02", different));
        Assert.Equal(TimeEvaluationStatus.Invalid, first.Overlaps(second).Status);
        var instant = Value("coglatas.instant", "coglatas.known", "2026-07-01T00:00:00.0000000Z");
        Assert.Throws<ArgumentException>(() => new ProjectTimeInterval(start, instant));
        Assert.Equal(TimeEvaluationStatus.Invalid, ProjectTimeArithmetic.ElapsedBetween(start, end).Status);
    }

    [Theory]
    [InlineData("workingIntervals", "2026-07-01T12:00:00.0000000Z", "2026-07-01T11:00:00.0000000Z")]
    [InlineData("workingIntervals", "2026-07-01T12:00:00.0000000Z", "2026-07-01T12:00:00.0000000Z")]
    [InlineData("nonWorkingIntervals", "2025-07-01T12:00:00.0000000Z", "2025-07-01T13:00:00.0000000Z")]
    public void InvalidCalendarIntervalsCannotBeEvaluated(string field, string start, string end)
    {
        var calendar = Calendar(); var node = JsonNode.Parse(calendar.Data.CanonicalText)!;
        node[field]![0]!["start"] = start; node[field]![0]!["end"] = end;
        var data = SourceJson.Parse(node.ToJsonString());
        var result = CalendarSnapshot.Read(data, CalendarRef(data), Zone().Snapshot);
        Assert.Equal(TimeEvaluationStatus.Invalid, result.Status); Assert.Null(result.Value);
    }

    [Fact]
    public void UnsupportedZoneRulesAndUnknownIntervalsRetainSource()
    {
        var zone = Zone(); var node = JsonNode.Parse(zone.Data.CanonicalText)!; node["futureRule"] = "future.unknown";
        var data = SourceJson.Parse(node.ToJsonString());
        var result = TimeZoneSnapshot.Read(data, zone.Reference);
        Assert.Equal(TimeEvaluationStatus.Unsupported, result.Status); Assert.Equal(data.CanonicalText, result.Data!.CanonicalText);
        var endpoint = Value("coglatas.instant", "coglatas.unknown");
        var interval = new ProjectTimeInterval(endpoint, endpoint);
        Assert.Equal(TimeEvaluationStatus.Unknown, new ProjectTimeInterval(interval.Data).Overlaps(interval).Status);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [InlineData("ar-SA")]
    public void EvaluationAndCanonicalBytesAreCultureIndependent(string culture)
    {
        var prior = CultureInfo.CurrentCulture;
        try
        {
            var baseline = Calendar(); var zone = Zone(); CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var repeated = Calendar();
            Assert.Equal(baseline.Data.ToCanonicalBytes(), repeated.Data.ToCanonicalBytes());
            Assert.Equal(baseline.Reference.RulesDigest, repeated.Reference.RulesDigest);
            var civil = Civil(zone.Reference, "2026-11-01T01:30:00", "-180000000000");
            Assert.Equal("2026-11-01T06:30:00.0000000Z", zone.Snapshot.Resolve(civil).Value!.CanonicalText);
            Assert.Equal(civil.Data.CanonicalText, new ZonedCivilTime(SourceJson.Parse(civil.Data.ToCanonicalBytes())).Data.CanonicalText);
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }
}
