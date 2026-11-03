using System.Collections;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class CompilerDiagnosticIntegrationTests
{
    [Theory]
    [InlineData("units")]
    [InlineData("calendars")]
    public void CancellationDuringSemanticSupplyStopsBeforeTheNextItemAndDisposes(string family)
    {
        var input = Input(); var model = ProjectSemanticBinder.Bind(input);
        using var cancellation = new CancellationTokenSource();
        var units = new ObservedSupply<UnitDefinition>(() => cancellation.Cancel(), Unit());
        var calendars = new ObservedSupply<CalendarSnapshot>(() => cancellation.Cancel(), Calendar());
        Assert.Throws<OperationCanceledException>(() => CompilerSemanticValidation.Validate(model,
            family == "units" ? units : null, family == "calendars" ? calendars : null, cancellation.Token));
        Assert.Equal(1, family == "units" ? units.MoveNextCalls : calendars.MoveNextCalls);
        Assert.True(family == "units" ? units.Disposed : calendars.Disposed);
    }

    [Fact]
    public void CancellationAtTheEndOfOneSupplyPreventsOpeningTheNextSupply()
    {
        var input = Input(); using var cancellation = new CancellationTokenSource();
        var units = new ObservedSupply<UnitDefinition>(() => cancellation.Cancel(), Unit(), count: 0);
        var calendars = new ObservedSupply<CalendarSnapshot>(() => { }, Calendar(), throwOnOpen: true);
        var result = PureCoreCompiler.Compile(input, input.Compiler, units: units, calendars: calendars, cancellationToken: cancellation.Token);
        Assert.Equal(CompilerExecutionState.Cancelled, result.Result.Execution);
        Assert.Equal(1, units.MoveNextCalls); Assert.True(units.Disposed); Assert.Equal(0, calendars.OpenCalls);
        Assert.Null(result.Validation); Assert.Empty(result.Result.Artifacts); Assert.False(result.Result.EvidenceComplete);
    }

    [Fact]
    public void ExhaustedBindingDoesNotOpenLaterMaterializationSupplies()
    {
        var settings = Settings(); settings["executionProfile"]!["maximumOperations"] = 1;
        var input = Input(settings);
        var units = new ObservedSupply<UnitDefinition>(() => { }, Unit());
        var calendars = new ObservedSupply<CalendarSnapshot>(() => { }, Calendar());
        var result = PureCoreCompiler.Compile(input, input.Compiler, units: units, calendars: calendars).Result;
        Assert.Equal(CompilerExecutionState.ResourceExhausted, result.Execution);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, Assert.Single(result.Stages, stage => stage.StageId == "coglatas.binding").Execution);
        Assert.Equal(0, units.OpenCalls); Assert.Equal(0, calendars.OpenCalls);
        Assert.Empty(result.Artifacts); Assert.Equal(CompilerCoverage.Partial, result.Coverage); Assert.False(result.EvidenceComplete);
    }

    [Theory]
    [InlineData("units")]
    [InlineData("calendars")]
    public void MaterializationLimitsRemainExhaustionInTheSemanticStageAndFinalResult(string family)
    {
        var input = Input(); var units = new ObservedSupply<UnitDefinition>(() => { }, Unit(), count: 1025);
        var calendars = new ObservedSupply<CalendarSnapshot>(() => { }, Calendar(), count: 129);
        var compilation = PureCoreCompiler.Compile(input, input.Compiler,
            units: family == "units" ? units : null, calendars: family == "calendars" ? calendars : null);
        var result = compilation.Result;
        Assert.Equal(CompilerExecutionState.ResourceExhausted, result.Execution);
        Assert.Equal(CompilerExecutionState.ResourceExhausted,
            Assert.Single(result.Stages, stage => stage.StageId == "coglatas.semantic-validation-ir").Execution);
        Assert.Equal(CompilerCoverage.Partial, result.Coverage); Assert.False(result.EvidenceComplete);
        Assert.Null(compilation.Validation!.Ir); Assert.Empty(result.Artifacts);
        Assert.True(family == "units" ? units.Disposed : calendars.Disposed);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(input.Source.Context, diagnostic.Location.Context));
    }

    [Fact]
    public void KnownConstraintFailureIsCompletedAndDistinctFromResourceExhaustion()
    {
        var input = CompilerAnalysisFixtures.Constraint(false); var result = PureCoreCompiler.Compile(input, input.Compiler).Result;
        Assert.Equal(CompilerExecutionState.Completed, result.Execution); Assert.Equal(CompilerVerdict.Fail, result.Verdict);
        Assert.Equal(CompilerCoverage.Complete, result.Coverage);
        Assert.All(result.Stages, stage => Assert.Equal(CompilerExecutionState.Completed, stage.Execution));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.Contains("budget", StringComparison.Ordinal));
    }

    [Fact]
    public void AdmissionDiagnosticsKeepTheirExactIdentityLocationAndOrderingThroughCompilation()
    {
        var source = Source();
        var context = SourceRevisionContext.Committed(new(new(source.TenantId, ProjectId.Parse(Id(22)), BranchId.Parse(Id(23))), RevisionId.Parse(Id(24))));
        var body = ProjectSource.Create(context, source.Documents); var settings = Settings();
        settings["authorizedExternalSnapshots"] = new JsonArray(Obj(new { snapshotId = Id(30), version = "1", digest = body.Digest.Data.Value,
            context = context.Data.Value, coverage = "coglatas.complete", asOf = (string?)null }));
        var input = Input(settings); var first = new CompilerMaterializedSnapshot(ExternalSnapshotId.Parse(Id(30)), "1", new(body, true, true, true));
        var wrong = new CompilerMaterializedSnapshot(first.SnapshotId, "2", first.Snapshot);
        var a = PureCoreCompiler.Compile(input, input.Compiler, [first, wrong]);
        var b = PureCoreCompiler.Compile(input, input.Compiler, [wrong, first]);
        Assert.Equal(a.Result.ResultDigest, b.Result.ResultDigest);
        Assert.Equal(a.Result.Diagnostics.Select(item => item.Data.CanonicalText), b.Result.Diagnostics.Select(item => item.Data.CanonicalText));
        var binding = Assert.Single(a.Model!.Findings, item => item.Diagnostic.Code == "coglatas.binding.snapshot-conflict").Diagnostic;
        var published = Assert.Single(a.Result.Diagnostics, item => item.DiagnosticId == binding.DiagnosticId);
        Assert.Equal(binding.Data, published.Data); Assert.Equal(input.Source.Context, published.Location.Context);
        Assert.Equal(CompilerVerdict.Fail, a.Result.Verdict); Assert.Empty(a.Result.Artifacts);
        Assert.Equal(CompilerFreshness.Stale, CompilerFreshnessAssessment.Assess(a.Result, Input(source: Source(99))).State);
    }

    private static UnitDefinition Unit()
    {
        var data = Json(Obj(new { unitId = "unit.kg", version = 1, dimension = new Dictionary<string, int> { ["dimension.mass"] = 1 } }));
        return new(data, ContentDigest.Compute("coglatas.unit-definition/1", data.ToCanonicalBytes()));
    }

    private static CalendarSnapshot Calendar()
    {
        const string start = "2026-01-01T00:00:00.0000000Z"; const string end = "2027-01-01T00:00:00.0000000Z";
        var zoneData = Json(Obj(new { schemaVersion = 1, timeZoneId = "Etc/UTC", version = "fixture-1", validFrom = start, validTo = end,
            segments = new[] { new { start, end, offsetTicks = "0" } } }));
        var zoneReference = new TimeZoneReference(Json(Obj(new { timeZoneId = "Etc/UTC", version = "fixture-1", rulesDigest = TimeZoneSnapshot.ComputeDigest(zoneData).Data.Value })));
        var zone = TimeZoneSnapshot.Read(zoneData, zoneReference).Value!;
        var data = Json(Obj(new { schemaVersion = 1, calendarId = "coglatas.integration-fixture", version = "fixture-1", timeZone = zoneReference.Data.Value,
            validFrom = start, validTo = end, workingIntervals = Array.Empty<object>(), nonWorkingIntervals = Array.Empty<object>(), exceptions = Array.Empty<object>() }));
        var reference = new CalendarReference(Json(Obj(new { calendarId = "coglatas.integration-fixture", version = "fixture-1", timeZoneId = "Etc/UTC", rulesDigest = CalendarSnapshot.ComputeDigest(data).Data.Value })));
        return Assert.IsType<CalendarSnapshot>(CalendarSnapshot.Read(data, reference, zone).Value);
    }

    private sealed class ObservedSupply<T>(Action beforeFirst, T item, int count = 2, bool throwOnOpen = false) : IEnumerable<T?> where T : class
    {
        private readonly Action _beforeFirst = beforeFirst;
        private readonly T _item = item;
        private readonly int _count = count;
        public int OpenCalls { get; private set; }
        public int MoveNextCalls { get; private set; }
        public bool Disposed { get; private set; }
        public IEnumerator<T?> GetEnumerator()
        {
            OpenCalls++;
            if (throwOnOpen) throw new InvalidOperationException("Later materialization supply must remain unopened.");
            return new Enumerator(this);
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        private sealed class Enumerator(ObservedSupply<T> owner) : IEnumerator<T?>
        {
            public T Current => owner._item;
            object IEnumerator.Current => Current;
            public bool MoveNext() { owner.MoveNextCalls++; if (owner.MoveNextCalls == 1) owner._beforeFirst(); return owner.MoveNextCalls <= owner._count; }
            public void Reset() => throw new NotSupportedException();
            public void Dispose() => owner.Disposed = true;
        }
    }
}
