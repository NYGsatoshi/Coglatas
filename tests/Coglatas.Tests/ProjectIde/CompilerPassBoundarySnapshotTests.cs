using System.Text;
using System.Text.Json;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class CompilerPassBoundarySnapshotTests
{
    public static IEnumerable<object[]> Keys() => SyntheticOperatorRecordingTests.Keys();
    private static CompilerInputManifest Input() => SyntheticOperatorRecordingTests.Input().Control.Input;
    private static ProjectCompilerPassBoundarySnapshot Read(RecordedPassCompilation run, int ordinal,
        CompilerPassObservationSnapshotMode mode = CompilerPassObservationSnapshotMode.Manifest,
        int maximumBodyBytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes) =>
        ProjectCompilerPassBoundarySnapshot.Read(run, run.Compilation, run.Observation, run.Compilation.Input.InputDigest,
            run.Compilation.Result.ResultDigest, ordinal, mode, maximumBodyBytes);

    [Theory]
    [MemberData(nameof(Keys))]
    public void SelectedBoundaryPreservesOriginalOrderGapsAndOnlyItsExactOriginalBodyObjects(string key)
    {
        var input = SyntheticOperatorRecordingTests.Input(key).Control.Input;
        var run = PureCoreCompiler.CompileWithPassObservation(input, input.Compiler, new(CompilerPassObservationMode.AvailableOriginalJson));
        var whole = ProjectCompilerPassObservationSnapshot.Read(run, run.Compilation, run.Observation, input.InputDigest,
            run.Compilation.Result.ResultDigest, CompilerPassObservationSnapshotMode.AvailableOriginalJson);
        var originalText = run.Observation.Data.CanonicalText; var originalDigest = run.Observation.Digest;
        var events = run.Observation.Data.Value.GetProperty("events");
        Assert.Equal(10, events.GetArrayLength());
        for (var ordinal = 0; ordinal < events.GetArrayLength(); ordinal++)
        {
            var manifest = Read(run, ordinal);
            Assert.Equal(CompilerPassBoundarySnapshotState.ManifestOnly, manifest.State); Assert.Empty(manifest.Bodies);
            Assert.Equal(events[ordinal].GetRawText(), manifest.OriginalBoundaryMetadata!.CanonicalText);
            var snapshot = Read(run, ordinal, CompilerPassObservationSnapshotMode.AvailableOriginalJson);
            Assert.Same(manifest.OriginalBoundaryMetadata, snapshot.OriginalBoundaryMetadata);
            Assert.Equal(ordinal, snapshot.Ordinal); Assert.Equal(originalDigest, snapshot.OriginalRecordDigest);
            var expectedKeys = events[ordinal].GetProperty("originalBodies").EnumerateArray().Select(r =>
                r.GetProperty("domain").GetString() + ":" + r.GetProperty("digest").GetProperty("value").GetString()).Distinct().Order().ToArray();
            Assert.Equal(expectedKeys, snapshot.Bodies.Keys.Order());
            Assert.Equal(expectedKeys.Length == 0 ? CompilerPassBoundarySnapshotState.NoRecordedOriginalBodies :
                CompilerPassBoundarySnapshotState.AvailableOriginalJson, snapshot.State);
            foreach (var body in snapshot.Bodies) Assert.Same(whole.Bodies[body.Key], body.Value);
            Assert.Equal(snapshot.Bodies.Values.Sum(b => Encoding.UTF8.GetByteCount(b.CanonicalText)), snapshot.RetainedBodyBytes);
            Assert.Equal(snapshot.Digest, ContentDigest.Compute(ProjectCompilerPassBoundarySnapshot.Format, snapshot.Data.ToCanonicalBytes()));
        }
        Assert.Equal(originalText, run.Observation.Data.CanonicalText); Assert.Equal(originalDigest, run.Observation.Digest);
    }

    [Fact]
    public void SelectedViewerBudgetCountsOnlyDistinctSelectedBodiesAndDoesNotRewriteWholeCapture()
    {
        var input = Input();
        var run = PureCoreCompiler.CompileWithPassObservation(input, input.Compiler, new(CompilerPassObservationMode.AvailableOriginalJson));
        var bytes = Encoding.UTF8.GetByteCount(input.Source.Data.CanonicalText);
        Assert.True(run.Observation.RetainedBodyBytes > bytes);
        Assert.Equal(CompilerPassObservationSnapshotState.BodyBudgetExceeded, ProjectCompilerPassObservationSnapshot.Read(run, run.Compilation,
            run.Observation, input.InputDigest, run.Compilation.Result.ResultDigest, CompilerPassObservationSnapshotMode.AvailableOriginalJson, bytes).State);
        var selected = Read(run, 0, CompilerPassObservationSnapshotMode.AvailableOriginalJson, bytes);
        Assert.Equal(CompilerPassBoundarySnapshotState.AvailableOriginalJson, selected.State); Assert.Single(selected.Bodies);
        Assert.Same(input.Source.Data, selected.Bodies.Single().Value); Assert.Equal(bytes, selected.RetainedBodyBytes);
        var small = Read(run, 0, CompilerPassObservationSnapshotMode.AvailableOriginalJson, bytes - 1);
        Assert.Equal(CompilerPassBoundarySnapshotState.BodyBudgetExceeded, small.State); Assert.Empty(small.Bodies); Assert.Equal(0, small.RetainedBodyBytes);
        Assert.Same(selected.OriginalBoundaryMetadata, small.OriginalBoundaryMetadata);
        Assert.Equal(CompilerPassObservationState.AvailableOriginalJson, small.OriginalCaptureState);
        Assert.Equal(CompilerPassBoundarySnapshotState.AvailableOriginalJson, Read(run, 0, CompilerPassObservationSnapshotMode.AvailableOriginalJson, bytes).State);
    }

    [Theory]
    [InlineData(CompilerPassObservationMode.Manifest, 1048576, 16)]
    [InlineData(CompilerPassObservationMode.AvailableOriginalJson, 1, 16)]
    [InlineData(CompilerPassObservationMode.AvailableOriginalJson, 1048576, 1)]
    public void LargerSelectedReadCannotRecoverOriginalCaptureGaps(CompilerPassObservationMode mode, int bytes, int events)
    {
        var input = Input(); var run = PureCoreCompiler.CompileWithPassObservation(input, input.Compiler, new(mode, bytes, events));
        var snapshot = Read(run, 0, CompilerPassObservationSnapshotMode.AvailableOriginalJson, ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes);
        Assert.Equal(events == 1 ? CompilerPassBoundarySnapshotState.BoundaryNotRecorded :
            CompilerPassBoundarySnapshotState.NoRecordedOriginalBodies, snapshot.State);
        Assert.Empty(snapshot.Bodies); Assert.Equal(0, snapshot.RetainedBodyBytes);
        Assert.Equal(run.Observation.State, snapshot.OriginalCaptureState); Assert.Equal(run.Observation.Digest, snapshot.OriginalRecordDigest);
        Assert.Equal(events == 1, snapshot.OriginalBoundaryMetadata is null);
    }

    [Fact]
    public void EqualGenuineOlderPairsAndWrongBindingsRevealNoPreviousBoundaryOrBodies()
    {
        var input = Input(); var first = PureCoreCompiler.CompileWithPassObservation(input, input.Compiler, new());
        var second = PureCoreCompiler.CompileWithPassObservation(input, input.Compiler, new());
        Assert.Equal(first.Observation.Digest, second.Observation.Digest);
        var refused = new[] {
            ProjectCompilerPassBoundarySnapshot.Read(first, second.Compilation, first.Observation, input.InputDigest, first.Compilation.Result.ResultDigest, 0),
            ProjectCompilerPassBoundarySnapshot.Read(first, first.Compilation, second.Observation, input.InputDigest, first.Compilation.Result.ResultDigest, 0),
            ProjectCompilerPassBoundarySnapshot.Read(first, first.Compilation, first.Observation, input.Source.Digest, first.Compilation.Result.ResultDigest, 0),
            ProjectCompilerPassBoundarySnapshot.Read(first, first.Compilation, first.Observation, input.InputDigest, input.Source.Digest, 0)
        };
        Assert.Equal(new[] { CompilerPassBoundarySnapshotState.RefusedInvocation, CompilerPassBoundarySnapshotState.RefusedInvocation,
            CompilerPassBoundarySnapshotState.RefusedInputBinding, CompilerPassBoundarySnapshotState.RefusedWrongResult }, refused.Select(r => r.State));
        foreach (var snapshot in refused)
        {
            Assert.Null(snapshot.OriginalBoundaryMetadata); Assert.Null(snapshot.OriginalRecordDigest); Assert.Null(snapshot.OriginalCaptureState);
            Assert.Empty(snapshot.Bodies); Assert.Equal(JsonValueKind.Null, snapshot.Data.Value.GetProperty("basis").ValueKind);
        }
    }

    [Fact]
    public void UnrecordedOrdinalsAndPreCancelledExecutionDoNotInventAnOriginalBoundary()
    {
        var input = Input(); var run = PureCoreCompiler.CompileWithPassObservation(input, input.Compiler, new());
        var beyond = Read(run, int.MaxValue); Assert.Equal(CompilerPassBoundarySnapshotState.BoundaryNotRecorded, beyond.State);
        Assert.Null(beyond.OriginalBoundaryMetadata); Assert.Empty(beyond.Bodies);
        using var source = new CancellationTokenSource(); source.Cancel();
        var cancelled = PureCoreCompiler.CompileWithPassObservation(input, input.Compiler, new(), cancellationToken: source.Token);
        var missing = Read(cancelled, 0);
        Assert.Equal(CompilerExecutionState.Cancelled, cancelled.Compilation.Result.Execution);
        Assert.Equal(CompilerPassObservationState.NoActualPasses, missing.OriginalCaptureState);
        Assert.Equal(CompilerPassBoundarySnapshotState.BoundaryNotRecorded, missing.State);
        Assert.Null(missing.OriginalBoundaryMetadata); Assert.Empty(missing.Bodies);
    }

    [Fact]
    public void InvalidSelectorsBoundsAndCancellationPreserveOwnedRecording()
    {
        var input = Input(); var run = PureCoreCompiler.CompileWithPassObservation(input, input.Compiler, new(CompilerPassObservationMode.AvailableOriginalJson));
        Assert.Throws<ArgumentOutOfRangeException>(() => Read(run, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Read(run, 0, (CompilerPassObservationSnapshotMode)999));
        foreach (var bytes in new[] { 0, ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes + 1 })
            Assert.Throws<ArgumentOutOfRangeException>(() => Read(run, 0, maximumBodyBytes: bytes));
        using var source = new CancellationTokenSource(); source.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectCompilerPassBoundarySnapshot.Read(run, run.Compilation, run.Observation,
            input.InputDigest, run.Compilation.Result.ResultDigest, 0, cancellationToken: source.Token));
        Assert.Equal(CompilerPassBoundarySnapshotState.AvailableOriginalJson, Read(run, 0, CompilerPassObservationSnapshotMode.AvailableOriginalJson).State);
    }
}
