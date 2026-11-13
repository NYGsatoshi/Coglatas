using Coglatas.Domain.ProjectIde;
namespace Coglatas.Tests.ProjectIde;
public sealed class TimeSnapshotImmutabilityTests
{
    private static SourceJson Encode(object value) => SourceJson.Parse(System.Text.Json.JsonSerializer.Serialize(value));
    private const string Start = "2026-01-01T00:00:00.0000000Z";
    private const string End = "2027-01-01T00:00:00.0000000Z";
    private static TimeZoneSnapshot Zone()
    {
        var data = Encode(new { schemaVersion=1, timeZoneId="Etc/UTC", version="fixture-1",validFrom=Start,validTo=End,
            segments=new[]{new{start=Start,end=End,offsetTicks="0"}} });
        var reference = new TimeZoneReference(Encode(new {timeZoneId="Etc/UTC",version="fixture-1",rulesDigest=TimeZoneSnapshot.ComputeDigest(data).Data.Value}));
        return TimeZoneSnapshot.Read(data,reference).Value!;
    }
    private static InstantInterval Interval(string start,string end) => new(ProjectInstant.Parse(start),ProjectInstant.Parse(end));
    [Theory]
    [InlineData("working")]
    [InlineData("nonworking")]
    [InlineData("exceptions")]
    public void CalendarMeasurementCannotChangeUnderItsRetainedRulesDigest(string surface)
    {
        var zone=Zone();
        const string begin="2026-07-01T13:00:00.0000000Z", end="2026-07-01T21:00:00.0000000Z";
        var data=Encode(new { schemaVersion=1,calendarId="coglatas.owned-calendar",version="fixture-1",timeZone=zone.Reference.Data.Value,
            validFrom=Start,validTo=End,workingIntervals=new[]{new{start=begin,end}},
            nonWorkingIntervals=new[]{new{start="2026-07-01T14:00:00.0000000Z",end="2026-07-01T15:00:00.0000000Z"}},
            exceptions=new[]{new{kind="coglatas.holiday",start="2026-07-01T16:00:00.0000000Z",end="2026-07-01T17:00:00.0000000Z"}} });
        var reference=new CalendarReference(Encode(new { calendarId="coglatas.owned-calendar",version="fixture-1",timeZoneId="Etc/UTC",rulesDigest=CalendarSnapshot.ComputeDigest(data).Data.Value }));
        var calendar=CalendarSnapshot.Read(data,reference,zone).Value!; var interval=Interval(begin,end);
        var expected=calendar.Measure(interval);Assert.Equal(TimeEvaluationStatus.Known,expected.Status);
        Assert.Equal(TimeSpan.FromHours(6).Ticks,expected.Value!.Ticks);
        object view=surface=="working" ? calendar.WorkingIntervals : surface=="nonworking" ? calendar.NonWorkingIntervals : calendar.Exceptions;
        if(view is System.Collections.ICollection legacy && legacy.SyncRoot is System.Collections.IList backing)
            try { backing.Clear(); } catch(NotSupportedException) { }
        Assert.Equal(reference.RulesDigest,CalendarSnapshot.ComputeDigest(calendar.Data));
        var after=calendar.Measure(interval);Assert.Equal(expected.Status,after.Status);Assert.Equal(expected.Value.Ticks,after.Value!.Ticks);
    }
    [Fact]
    public void ZoneResolutionCannotChangeUnderItsRetainedRulesDigest()
    {
        var zone=Zone();var instant=ProjectInstant.Parse("2026-07-01T13:00:00.0000000Z");var expected=zone.ToCivil(instant);
        Assert.Equal(TimeEvaluationStatus.Known,expected.Status);
        if(zone.Segments is System.Collections.ICollection legacy && legacy.SyncRoot is IList<TimeZoneSegment> backing)
            try { backing.Clear(); } catch(NotSupportedException) { }
        Assert.Equal(zone.Reference.RulesDigest,TimeZoneSnapshot.ComputeDigest(zone.Data));
        var after=zone.ToCivil(instant);Assert.Equal(expected.Status,after.Status);Assert.Equal(expected.Value!.Data,after.Value!.Data);
    }
    [Fact]
    public void ResolvedTimeCandidatesRetainTheKnownValueAfterCollectionAccess()
    {
        var zone=Zone();var civil=new ZonedCivilTime(Encode(new{local="2026-07-01T13:00:00.0000000",timeZone=zone.Reference.Data.Value}));
        var result=zone.Resolve(civil);Assert.Equal(TimeEvaluationStatus.Known,result.Status);
        if(result.Candidates is System.Collections.ICollection legacy && legacy.SyncRoot is ProjectInstant[] backing)backing[0]=null!;
        Assert.Equal(result.Value,Assert.Single(result.Candidates));
    }
}
