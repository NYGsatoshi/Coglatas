using System.Collections;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class CompilerPassObservationTests
{
    public static IEnumerable<object[]> Keys() => SyntheticOperatorRecordingTests.Keys();
    private static CompilerInputManifest Input(string key = "fail") => SyntheticOperatorRecordingTests.Input(key).Control.Input;
    private static ProjectCompilerPassObservationSnapshot Read(RecordedPassCompilation run,
        CompilerPassObservationSnapshotMode mode = CompilerPassObservationSnapshotMode.Manifest, int bytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes) =>
        ProjectCompilerPassObservationSnapshot.Read(run, run.Compilation, run.Observation, run.Compilation.Input.InputDigest, run.Compilation.Result.ResultDigest, mode, bytes);
    private static CompilerPassObservationSnapshotState Refused(ProjectCompilerPassObservationSnapshot snapshot)
    {
        Assert.Empty(snapshot.Bodies); Assert.Null(snapshot.OriginalRecordMetadata); Assert.Null(snapshot.OriginalRecordDigest);
        Assert.Null(snapshot.OriginalCaptureState); Assert.Equal(JsonValueKind.Null, snapshot.Data.Value.GetProperty("basis").ValueKind);
        Assert.Equal(JsonValueKind.Null, snapshot.Data.Value.GetProperty("originalRecord").ValueKind);
        return snapshot.State;
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void DefaultManifestObservesActualBoundariesWithoutChangingOriginalResultsOrOperations(string key)
    {
        var input = Input(key); var control = PureCoreCompiler.Compile(input, input.Compiler);
        var run = PureCoreCompiler.CompileWithPassObservation(input, input.Compiler, new()); var observation = run.Observation;
        Assert.Equal(control.Result.Data, run.Compilation.Result.Data); Assert.Equal(control.LogicalOperationsConsumed, run.Compilation.LogicalOperationsConsumed);
        Assert.True(run.IsFor(run.Compilation)); Assert.False(run.IsFor(control)); Assert.True(observation.IsOwnedBy(run));
        Assert.Equal(CompilerPassObservationState.ManifestOnly, observation.State); Assert.Equal(10, observation.VisitedEvents); Assert.Equal(10, observation.RetainedEvents);
        Assert.Equal(0, observation.RetainedBodyBytes); var metadata = observation.Data.Value;
        Assert.Equal("BeforeCompile", metadata.GetProperty("capture").GetProperty("declared").GetString());
        var events = metadata.GetProperty("events").EnumerateArray().ToArray();
        var expected = new[] { "coglatas.parse-normalize", "coglatas.binding", "coglatas.semantic-validation-ir", "coglatas.analysis", "coglatas.publication" }
            .SelectMany(pass => new[] { pass + ":Entered", pass + ":Returned" });
        Assert.Equal(expected, events.Select(e => e.GetProperty("passId").GetString() + ":" + e.GetProperty("phase").GetString()));
        Assert.Equal(Enumerable.Range(0, 10), events.Select(e => e.GetProperty("ordinal").GetInt32()));
        Assert.Equal(observation.Digest, ContentDigest.Compute(ProjectCompilerPassObservation.Format, observation.Data.ToCanonicalBytes()));
        Assert.Equal(CompilerPassObservationSnapshotState.ManifestOnly, Read(run).State); Assert.Empty(Read(run).Bodies);
        Assert.Equal(CompilerPassObservationSnapshotState.NoRecordedOriginalBodies, Read(run, CompilerPassObservationSnapshotMode.AvailableOriginalJson).State);
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void OriginalBodiesHaveTheirActualHashesWhileOwnerSerializationRemainsMetadataOnly(string key)
    {
        var input = Input(key); var run = PureCoreCompiler.CompileWithPassObservation(input, input.Compiler, new(CompilerPassObservationMode.AvailableOriginalJson));
        var manifest = Read(run); Assert.Empty(manifest.Bodies);
        var snapshot = Read(run, CompilerPassObservationSnapshotMode.AvailableOriginalJson);
        Assert.Equal(CompilerPassObservationState.AvailableOriginalJson, run.Observation.State); Assert.Equal(CompilerPassObservationSnapshotState.AvailableOriginalJson, snapshot.State);
        var unique = new HashSet<string>();
        foreach (var e in run.Observation.Data.Value.GetProperty("events").EnumerateArray()) foreach (var frame in e.GetProperty("originalBodies").EnumerateArray())
        {
            var domain = frame.GetProperty("domain").GetString()!; var digest = frame.GetProperty("digest"); var bodyKey = domain + ":" + digest.GetProperty("value").GetString();
            Assert.True(snapshot.Bodies.TryGetValue(bodyKey, out var body)); unique.Add(bodyKey);
            Assert.Equal(SourceJson.Parse(digest.GetRawText()), ContentDigest.Compute(domain, body.ToCanonicalBytes()).Data);
            Assert.Equal(frame.GetProperty("bytes").GetInt32(), Encoding.UTF8.GetByteCount(body.CanonicalText));
        }
        Assert.Equal(unique.Count, snapshot.Bodies.Count); Assert.Equal(run.Observation.RetainedBodyBytes, snapshot.RetainedBodyBytes);
        Assert.Same(input.Source.Data, snapshot.Bodies[ProjectSource.Format + ":" + input.Source.Digest.Value]);
        var title = input.Source.Data.Value.GetProperty("documents")[0].GetProperty("root").GetProperty("displayName").GetString()!;
        Assert.Contains(title, input.Source.Data.CanonicalText); Assert.DoesNotContain(title, JsonSerializer.Serialize(run));
        Assert.DoesNotContain(title, JsonSerializer.Serialize(run.Observation)); Assert.DoesNotContain(title, manifest.Data.CanonicalText);
        var limited = Read(run, CompilerPassObservationSnapshotMode.AvailableOriginalJson, 1);
        Assert.Equal(CompilerPassObservationSnapshotState.BodyBudgetExceeded, limited.State); Assert.Empty(limited.Bodies);
        Assert.Equal(CompilerPassObservationState.AvailableOriginalJson, limited.OriginalCaptureState);
        Assert.Equal(run.Observation.Data.CanonicalText, limited.OriginalRecordMetadata!.CanonicalText);
        Assert.Equal(CompilerPassObservationSnapshotState.AvailableOriginalJson, Read(run, CompilerPassObservationSnapshotMode.AvailableOriginalJson).State);
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void EqualGenuineInputResultAndObservationDigestsCannotCrossTheActualOwnedPair(string key)
    {
        var input = Input(key); var first = PureCoreCompiler.CompileWithPassObservation(input, input.Compiler, new(CompilerPassObservationMode.AvailableOriginalJson));
        var second = PureCoreCompiler.CompileWithPassObservation(input, input.Compiler, new(CompilerPassObservationMode.AvailableOriginalJson));
        Assert.Equal(first.Compilation.Result.ResultDigest, second.Compilation.Result.ResultDigest); Assert.Equal(first.Observation.Digest, second.Observation.Digest);
        Assert.False(first.IsFor(second.Compilation)); Assert.False(first.Observation.IsOwnedBy(second));
        Assert.Equal(CompilerPassObservationSnapshotState.RefusedInvocation, Refused(ProjectCompilerPassObservationSnapshot.Read(first, second.Compilation, first.Observation, input.InputDigest, first.Compilation.Result.ResultDigest)));
        Assert.Equal(CompilerPassObservationSnapshotState.RefusedInvocation, Refused(ProjectCompilerPassObservationSnapshot.Read(first, first.Compilation, second.Observation, input.InputDigest, first.Compilation.Result.ResultDigest)));
        Assert.Equal(CompilerPassObservationSnapshotState.RefusedInvocation, Refused(ProjectCompilerPassObservationSnapshot.Read(first, second.Compilation, second.Observation, input.InputDigest, first.Compilation.Result.ResultDigest)));
        Assert.Empty(typeof(RecordedPassCompilation).GetConstructors()); Assert.All(typeof(RecordedPassCompilation).GetProperties(), p => Assert.Null(p.SetMethod));
        Assert.Equal(CompilerPassObservationSnapshotState.AvailableOriginalJson, Read(first, CompilerPassObservationSnapshotMode.AvailableOriginalJson).State);
    }

    [Theory]
    [InlineData(CompilerPassObservationMode.Manifest, 1048576, 16, CompilerPassObservationState.ManifestOnly)]
    [InlineData(CompilerPassObservationMode.AvailableOriginalJson, 1, 16, CompilerPassObservationState.BodyBudgetExceeded)]
    [InlineData(CompilerPassObservationMode.AvailableOriginalJson, 1048576, 1, CompilerPassObservationState.EventLimitExceeded)]
    public void PredeclaredCaptureGapsCannotBeRecoveredByALaterLargeViewer(CompilerPassObservationMode mode, int bytes, int events, CompilerPassObservationState state)
    {
        var input = Input(); var run = PureCoreCompiler.CompileWithPassObservation(input, input.Compiler, new(mode, bytes, events));
        Assert.Equal(state, run.Observation.State); Assert.Equal(10, run.Observation.VisitedEvents); Assert.Equal(0, run.Observation.RetainedBodyBytes);
        Assert.Equal(state == CompilerPassObservationState.EventLimitExceeded ? 0 : 10, run.Observation.RetainedEvents);
        var snapshot = Read(run, CompilerPassObservationSnapshotMode.AvailableOriginalJson, ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes);
        Assert.Equal(CompilerPassObservationSnapshotState.NoRecordedOriginalBodies, snapshot.State); Assert.Empty(snapshot.Bodies);
        Assert.Equal(state, snapshot.OriginalCaptureState); Assert.Equal(run.Observation.Data, snapshot.OriginalRecordMetadata);
    }

    [Fact]
    public void SameSourceDifferentSettingsAndWrongExpectedResultRevealNoPreviousMetadata()
    {
        var input = Input(); var settings = JsonNode.Parse(input.Settings.CanonicalText)!;
        settings["executionProfile"]!["maximumOperations"] = input.ExecutionProfile.MaximumOperations + 1;
        var other = new CompilerInputManifest(input.Source, SourceJson.Parse(settings.ToJsonString())); Assert.NotEqual(input.InputDigest, other.InputDigest);
        var run = PureCoreCompiler.CompileWithPassObservation(input, input.Compiler, new(CompilerPassObservationMode.AvailableOriginalJson));
        Assert.Equal(CompilerPassObservationSnapshotState.RefusedInputBinding, Refused(ProjectCompilerPassObservationSnapshot.Read(run, run.Compilation, run.Observation, other.InputDigest, run.Compilation.Result.ResultDigest)));
        Assert.Equal(CompilerPassObservationSnapshotState.RefusedWrongResult, Refused(ProjectCompilerPassObservationSnapshot.Read(run, run.Compilation, run.Observation, input.InputDigest, input.Source.Digest)));
    }

    [Fact]
    public void PreCancelledCompileDoesNotInventAStartedPassOrOriginalBody()
    {
        var input = Input(); using var source = new CancellationTokenSource(); source.Cancel();
        var control = PureCoreCompiler.Compile(input, input.Compiler, cancellationToken: source.Token);
        var run = PureCoreCompiler.CompileWithPassObservation(input, input.Compiler, new(), cancellationToken: source.Token);
        Assert.Equal(control.Result.Data, run.Compilation.Result.Data); Assert.Equal(CompilerPassObservationState.NoActualPasses, run.Observation.State);
        Assert.Equal(0, run.Observation.VisitedEvents); Assert.Equal(0, run.Observation.RetainedEvents); Assert.Equal(0, run.Observation.RetainedBodyBytes);
        Assert.Equal(CompilerPassObservationSnapshotState.NoRecordedOriginalBodies, Read(run, CompilerPassObservationSnapshotMode.AvailableOriginalJson).State);
    }

    [Fact]
    public void CancellationDuringActualBindingRetainsEarlierBodiesWithoutInventingAReturn()
    {
        var input = Input();
        using var beforeToken = new CancellationTokenSource();
        using var observedToken = new CancellationTokenSource();

        var beforeSupply = new CancelWhenRead(beforeToken); var observedSupply = new CancelWhenRead(observedToken);
        var control = PureCoreCompiler.Compile(input, input.Compiler, beforeSupply, cancellationToken: beforeToken.Token);
        var run = PureCoreCompiler.CompileWithPassObservation(input, input.Compiler, new(CompilerPassObservationMode.AvailableOriginalJson), observedSupply, cancellationToken: observedToken.Token);
        Assert.Equal(control.Result.Data, run.Compilation.Result.Data); Assert.Equal(control.LogicalOperationsConsumed, run.Compilation.LogicalOperationsConsumed);
        foreach (var supply in new[] { beforeSupply, observedSupply }) { Assert.Equal(1, supply.Reads); Assert.Equal(0, supply.Moves); Assert.True(supply.Disposed); }
        var events = run.Observation.Data.Value.GetProperty("events"); Assert.Equal(4, events.GetArrayLength());
        Assert.Equal("coglatas.binding", events[3].GetProperty("passId").GetString()); Assert.Equal("CancelledBeforeReturn", events[3].GetProperty("phase").GetString());
        Assert.Empty(events[3].GetProperty("originalBodies").EnumerateArray());
        var snapshot = Read(run, CompilerPassObservationSnapshotMode.AvailableOriginalJson);
        Assert.Equal(CompilerExecutionState.Cancelled, run.Compilation.Result.Execution); Assert.Equal(CompilerPassObservationSnapshotState.AvailableOriginalJson, snapshot.State);
        Assert.Equal(2, snapshot.Bodies.Count); Assert.Same(input.Source.Data, snapshot.Bodies[ProjectSource.Format + ":" + input.Source.Digest.Value]);
    }

    [Fact]
    public void ActualPublicationOverflowKeepsOnlyTheActualFallbackOutputAndPreservesOriginalExecution()
    {
        var template = Input(); string Id(int n) => n.ToString("x8") + "-0000-4000-8000-000000000001";
        var context = SourceRevisionContext.Committed(new(template.Source.Context.Branch, RevisionId.Parse(Id(9040))));
        var declaration = new { entityId = Id(501), documentId = template.Source.Documents[0].DocumentId.ToString(), kind = "coglatas.task", schemaVersion = 1,
            payload = new { duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "1" },
                ports = Enumerable.Range(10000, 3000).Select(n => new { portId = Id(n), direction = "coglatas.input", minimumConnections = 0, maximumConnections = 1,
                    type = new { kind = "coglatas.quantity", schemaVersion = 1, nullable = false, acceptsUnknown = false, unitId = "unit.unavailable", dimension = new Dictionary<string, int> { ["dimension.mass"] = 1 } } }).ToArray() } };
        var source = ProjectSource.Create(context, template.Source.Documents, SourceJson.Parse(JsonSerializer.Serialize(new[] { declaration })));
        var settings = JsonNode.Parse(template.Settings.CanonicalText)!; settings["executionProfile"]!["maximumOperations"] = 10000000; settings["executionProfile"]!["maximumDeclarations"] = 10000;
        var input = new CompilerInputManifest(source, SourceJson.Parse(settings.ToJsonString())); var control = PureCoreCompiler.Compile(input, input.Compiler);
        var run = PureCoreCompiler.CompileWithPassObservation(input, input.Compiler, new(CompilerPassObservationMode.AvailableOriginalJson, ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes));
        Assert.Equal(control.Result.Data, run.Compilation.Result.Data); Assert.Equal(control.LogicalOperationsConsumed, run.Compilation.LogicalOperationsConsumed);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, control.Result.Execution); Assert.Contains(control.Result.Diagnostics, d => d.Code == "coglatas.analysis.publication-budget");
        Assert.True(control.LogicalOperationsConsumed < input.ExecutionProfile.MaximumOperations);
        var last = run.Observation.Data.Value.GetProperty("events")[9]; Assert.Equal("Returned", last.GetProperty("phase").GetString()); Assert.Contains("overflowed", last.GetProperty("gap").GetString()!);
        var snapshot = Read(run, CompilerPassObservationSnapshotMode.AvailableOriginalJson, ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes);
        Assert.Equal(CompilerPassObservationSnapshotState.AvailableOriginalJson, snapshot.State);
        Assert.Equal(control.Result.Data, snapshot.Bodies["coglatas.compiler-result/1:" + control.Result.ResultDigest.Value]);
        Assert.True(snapshot.RetainedBodyBytes > 1);
        Assert.Equal(CompilerPassObservationSnapshotState.AvailableOriginalJson, Read(run, CompilerPassObservationSnapshotMode.AvailableOriginalJson, snapshot.RetainedBodyBytes).State);
        Assert.Equal(CompilerPassObservationSnapshotState.BodyBudgetExceeded, Read(run, CompilerPassObservationSnapshotMode.AvailableOriginalJson, snapshot.RetainedBodyBytes - 1).State);
    }

    [Fact]
    public void ThrowingSupplyKeepsTheExistingExceptionContractInsteadOfClaimingFailureCapture()
    {
        var input = Input(); var control = Assert.Throws<InvalidOperationException>(() => PureCoreCompiler.Compile(input, input.Compiler, new ThrowingSupply()));
        var observed = Assert.Throws<InvalidOperationException>(() => PureCoreCompiler.CompileWithPassObservation(input, input.Compiler, new(), new ThrowingSupply()));
        Assert.Equal(control.Message, observed.Message); Assert.Equal("supply failed", observed.Message);
    }

    [Fact]
    public void InvalidPoliciesAndViewerBoundsAreRefusedAndReadCancellationLeavesOriginalBodiesIntact()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompilerPassObservationPolicy((CompilerPassObservationMode)999));
        foreach (var bytes in new[] { 0, ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes + 1 }) Assert.Throws<ArgumentOutOfRangeException>(() => new CompilerPassObservationPolicy(maximumBodyBytes: bytes));
        foreach (var events in new[] { 0, CompilerPassObservationPolicy.HardEventLimit + 1 }) Assert.Throws<ArgumentOutOfRangeException>(() => new CompilerPassObservationPolicy(maximumEvents: events));
        var input = Input(); var run = PureCoreCompiler.CompileWithPassObservation(input, input.Compiler, new(CompilerPassObservationMode.AvailableOriginalJson));
        Assert.Throws<ArgumentOutOfRangeException>(() => Read(run, (CompilerPassObservationSnapshotMode)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => Read(run, bytes: 0));
        using var source = new CancellationTokenSource(); source.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectCompilerPassObservationSnapshot.Read(run, run.Compilation, run.Observation, input.InputDigest, run.Compilation.Result.ResultDigest, cancellationToken: source.Token));
        Assert.Equal(CompilerPassObservationSnapshotState.AvailableOriginalJson, Read(run, CompilerPassObservationSnapshotMode.AvailableOriginalJson).State);
    }

    private sealed class ThrowingSupply : IEnumerable<CompilerMaterializedSnapshot?>
    {
        public IEnumerator<CompilerMaterializedSnapshot?> GetEnumerator() => throw new InvalidOperationException("supply failed");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private sealed class CancelWhenRead(CancellationTokenSource cancellation) : IEnumerable<CompilerMaterializedSnapshot?>
    {
        public int Reads { get; private set; } public int Moves { get; private set; } public bool Disposed { get; private set; }
        public IEnumerator<CompilerMaterializedSnapshot?> GetEnumerator() { Reads++; cancellation.Cancel(); return new Cursor(this); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        private sealed class Cursor(CancelWhenRead owner) : IEnumerator<CompilerMaterializedSnapshot?>
        {
            public CompilerMaterializedSnapshot? Current => null; object? IEnumerator.Current => Current;
            public bool MoveNext() { owner.Moves++; return false; } public void Reset() => throw new NotSupportedException(); public void Dispose() => owner.Disposed = true;
        }
    }
}
