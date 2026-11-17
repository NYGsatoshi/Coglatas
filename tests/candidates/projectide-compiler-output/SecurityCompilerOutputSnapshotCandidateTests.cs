using System.Collections;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

/// <summary>
/// Security-owned independent local Compiler output-snapshot tests for P0 #1966.
/// Neither a local snapshot nor its content digest confers read/export/Trust authority.
/// </summary>
public sealed class SecurityCompilerOutputSnapshotCandidateTests
{
    private static CoreCompilation ActualCompilation()
    {
        var input = ProjectSimulationTests.Plan().Input;
        return PureCoreCompiler.Compile(input, input.Compiler);
    }

    private static ProjectCompilerOutputSnapshot Capture(CoreCompilation compilation,
        int budget = ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes)
        => ProjectCompilerOutputSnapshot.Read(compilation, compilation.Result.ResultDigest,
            CompilerOutputSnapshotMode.AvailableOutputs, budget);

    [Fact]
    public void DefaultManifestRetainsRealInputAndResultAxesWithoutPrivateOutputBodies()
    {
        var compiled = ActualCompilation();
        var snapshot = ProjectCompilerOutputSnapshot.Read(compiled, compiled.Result.ResultDigest);
        Assert.Equal(CompilerOutputSnapshotState.ManifestOnly, snapshot.State);
        Assert.Equal(CompilerOutputSnapshotMode.Manifest, snapshot.Mode);
        Assert.Empty(snapshot.Bodies);
        Assert.Equal(0, snapshot.RetainedBodyBytes);
        Assert.Equal(compiled.Input.InputDigest, snapshot.InputDigest);
        Assert.Equal(compiled.Input.Source.Digest, snapshot.SourceDigest);
        Assert.Equal(compiled.Input.Source.Context, snapshot.Context);
        Assert.Equal(compiled.Result.ResultDigest, snapshot.ResultDigest);
        Assert.Equal(compiled.Result.Verdict.ToString(), snapshot.Data.Value.GetProperty("result").GetProperty("verdict").GetString());
        Assert.All(snapshot.Data.Value.GetProperty("outputs").EnumerateArray(),
            item => Assert.Equal("NotCaptured", item.GetProperty("body").GetString()));
        Assert.Equal(snapshot.Digest, ContentDigest.Compute(ProjectCompilerOutputSnapshot.Format, snapshot.Data.ToCanonicalBytes()));
    }

    [Fact]
    public void ActualCorePublicationOnlyCapturesThreeDeclaredBoundOutputObjects()
    {
        var compiled = ActualCompilation();
        var capture = Capture(compiled);
        Assert.Equal(CompilerOutputSnapshotState.CapturedAvailableOutputs, capture.State);
        Assert.Equal(3, capture.Bodies.Count);
        Assert.NotNull(compiled.Publication);
        Assert.Same(compiled.Publication!.SemanticModel, capture.Bodies["coglatas.semantic-model"]);
        Assert.Same(compiled.Publication.Ir, capture.Bodies["coglatas.ir"]);
        Assert.Same(compiled.Publication.SourceMap, capture.Bodies["coglatas.source-map"]);
        Assert.Equal(compiled.Result.ResultDigest, capture.ResultDigest);
        Assert.Equal(compiled.Result.Artifacts.Select(item => item.Role).Order(StringComparer.Ordinal),
            capture.Bodies.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(capture.Bodies.Values.Sum(body => Encoding.UTF8.GetByteCount(body.CanonicalText)),
            capture.RetainedBodyBytes);
        Assert.All(capture.Data.Value.GetProperty("outputs").EnumerateArray(),
            item => Assert.Equal("Captured", item.GetProperty("body").GetString()));
    }

    [Fact]
    public void WrongExpectedResultRefusesAllSourceResultAndOutputExistenceDetails()
    {
        var compiled = ActualCompilation();
        var refused = ProjectCompilerOutputSnapshot.Read(compiled, compiled.Input.InputDigest,
            CompilerOutputSnapshotMode.AvailableOutputs);
        Assert.Equal(CompilerOutputSnapshotState.RefusedWrongResult, refused.State);
        Assert.Null(refused.InputDigest);
        Assert.Null(refused.ResultDigest);
        Assert.Null(refused.SourceDigest);
        Assert.Null(refused.Context);
        Assert.Empty(refused.Bodies);
        Assert.Empty(refused.ChangedDimensions);
        foreach (var field in new[] { "basis", "result", "desired", "freshness", "stages", "outputs" })
            Assert.Equal(JsonValueKind.Null, refused.Data.Value.GetProperty(field).ValueKind);
        Assert.NotEqual(Capture(compiled).Digest, refused.Digest);
    }

    [Fact]
    public void ExactByteBudgetSucceedsButLastMissingByteAtomicallyRefusesAllBodies()
    {
        var compiled = ActualCompilation();
        var entire = Capture(compiled);
        Assert.True(entire.RetainedBodyBytes > 1);
        var exact = Capture(compiled, entire.RetainedBodyBytes);
        Assert.Equal(CompilerOutputSnapshotState.CapturedAvailableOutputs, exact.State);
        Assert.Equal(3, exact.Bodies.Count);
        var shortByOne = Capture(compiled, entire.RetainedBodyBytes - 1);
        Assert.Equal(CompilerOutputSnapshotState.BodyBudgetExceeded, shortByOne.State);
        Assert.Empty(shortByOne.Bodies);
        Assert.Equal(0, shortByOne.RetainedBodyBytes);
        Assert.Equal(compiled.Result.ResultDigest, shortByOne.ResultDigest);
        Assert.Equal(entire.Data.Value.GetProperty("result").GetRawText(),
            shortByOne.Data.Value.GetProperty("result").GetRawText());
        Assert.All(shortByOne.Data.Value.GetProperty("outputs").EnumerateArray(),
            item => Assert.Equal("NotCaptured", item.GetProperty("body").GetString()));
    }

    [Fact]
    public void PublicBodyMapKeysAndValuesDoNotOfferGenericOrLegacyMutation()
    {
        var snapshot = Capture(ActualCompilation());
        Assert.False(snapshot.Bodies is ICollection);
        Assert.False(snapshot.Bodies.Keys is ICollection);
        Assert.False(snapshot.Bodies.Values is ICollection);
        Assert.False(snapshot.ChangedDimensions is ICollection);
        var dictionary = Assert.IsAssignableFrom<IDictionary<string, SourceJson>>(snapshot.Bodies);
        var resultBytes = snapshot.Data.ToCanonicalBytes();
        var digest = snapshot.Digest;
        Assert.Throws<NotSupportedException>(() => dictionary.Clear());
        Assert.Throws<NotSupportedException>(() => dictionary.Remove("coglatas.ir"));
        Assert.Throws<NotSupportedException>(() => dictionary.Keys.Clear());
        Assert.Throws<NotSupportedException>(() => dictionary.Values.Clear());
        var copy = new KeyValuePair<string, SourceJson>[dictionary.Count];
        dictionary.CopyTo(copy, 0);
        copy[0] = default;
        var mutable = dictionary.ToDictionary(pair => pair.Key, pair => pair.Value);
        mutable.Clear();
        Assert.Equal(3, snapshot.Bodies.Count);
        Assert.Equal(resultBytes, snapshot.Data.ToCanonicalBytes());
        Assert.Equal(digest, snapshot.Digest);
    }

    [Fact]
    public void NestedOutputJsonAndCopiedCanonicalBytesCannotRewriteOwnedEvidence()
    {
        var snapshot = Capture(ActualCompilation());
        var ir = snapshot.Bodies["coglatas.ir"];
        var original = ir.ToCanonicalBytes();
        var copy = ir.ToCanonicalBytes();
        Assert.NotEmpty(copy);
        copy[0] = (byte)(copy[0] == 123 ? 91 : 123);
        var parsed = JsonNode.Parse(ir.CanonicalText)!;
        if (parsed is JsonObject map) map["example.synthetic"] = true;
        Assert.Equal(original, ir.ToCanonicalBytes());
        Assert.Equal(ir.CanonicalText, snapshot.Bodies["coglatas.ir"].CanonicalText);
        Assert.Equal(snapshot.Digest, ContentDigest.Compute(ProjectCompilerOutputSnapshot.Format, snapshot.Data.ToCanonicalBytes()));
    }

    [Fact]
    public void DesiredFreshnessNeverOverwritesActualCompilerAxesOrSourceScope()
    {
        var compiled = ActualCompilation();
        var current = ProjectCompilerOutputSnapshot.Read(compiled, compiled.Result.ResultDigest,
            desiredInput: compiled.Input);
        var changed = Input(source: Source(79));
        var stale = ProjectCompilerOutputSnapshot.Read(compiled, compiled.Result.ResultDigest,
            desiredInput: changed);
        Assert.Equal(CompilerFreshness.Current, current.Freshness);
        Assert.Empty(current.ChangedDimensions);
        Assert.Equal(CompilerFreshness.Stale, stale.Freshness);
        Assert.NotEmpty(stale.ChangedDimensions);
        Assert.Equal(compiled.Input.InputDigest, stale.InputDigest);
        Assert.Equal(compiled.Result.ResultDigest, stale.ResultDigest);
        Assert.Equal(compiled.Input.Source.Context, stale.Context);
        Assert.Equal(current.Data.Value.GetProperty("result").GetRawText(),
            stale.Data.Value.GetProperty("result").GetRawText());
        Assert.NotEqual(current.Digest, stale.Digest);
    }

    [Fact]
    public void LegacyCompilerArtifactsNeverBecomeInventedCapturedBodies()
    {
        var input = Input();
        var compiled = PureCoreCompiler.Compile(input, input.Compiler);
        Assert.NotEmpty(compiled.Result.Artifacts);
        var result = Capture(compiled);
        Assert.Equal(CompilerOutputSnapshotState.UnavailableBodyProvider, result.State);
        Assert.Empty(result.Bodies);
        Assert.Equal(0, result.RetainedBodyBytes);
        Assert.Equal(compiled.Result.ResultDigest, result.ResultDigest);
        Assert.All(result.Data.Value.GetProperty("outputs").EnumerateArray(),
            item => Assert.Equal("NotCaptured", item.GetProperty("body").GetString()));
    }

    [Fact]
    public void CancellationRejectsCaptureWithoutChangingTheRealResult()
    {
        var compiled = ActualCompilation();
        var unchanged = compiled.Result.Data;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            ProjectCompilerOutputSnapshot.Read(compiled, compiled.Result.ResultDigest,
                CompilerOutputSnapshotMode.AvailableOutputs, cancellationToken: cancellation.Token));
        Assert.Same(unchanged, compiled.Result.Data);
        Assert.Equal(compiled.Result.ResultDigest,
            ContentDigest.Compute("coglatas.compiler-result/1", compiled.Result.Data.ToCanonicalBytes()));
    }

    [Fact]
    public void CancelledCompilationDoesNotFabricateEarlierBodiesOrCompletedVerdict()
    {
        var input = ProjectSimulationTests.Plan().Input;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var compiled = PureCoreCompiler.Compile(input, input.Compiler, cancellationToken: cancellation.Token);
        var snapshot = Capture(compiled);
        Assert.Equal(CompilerOutputSnapshotState.NoOutputsProduced, snapshot.State);
        Assert.Empty(snapshot.Bodies);
        Assert.Equal("Cancelled", snapshot.Data.Value.GetProperty("result").GetProperty("execution").GetString());
        Assert.Equal("NotRecorded", snapshot.Data.Value.GetProperty("intermediateStates").GetString());
        Assert.Equal(compiled.Result.ResultDigest, snapshot.ResultDigest);
    }

    [Fact]
    public void InvalidLimitsUnsupportedModeAndUnallocatedAuthorityNeverQualify()
    {
        var compiled = ActualCompilation();
        foreach (var budget in new[] { 0, ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes + 1 })
            Assert.Throws<ArgumentOutOfRangeException>(() => Capture(compiled, budget));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProjectCompilerOutputSnapshot.Read(compiled, compiled.Result.ResultDigest,
                (CompilerOutputSnapshotMode)999));
        var capture = Capture(compiled);
        Assert.Equal("NotAllocated", capture.Data.Value.GetProperty("runIdentity").GetString());
        Assert.Equal("NotRecorded", capture.Data.Value.GetProperty("intermediateStates").GetString());
        foreach (var name in new[] { "authority", "producerTrust", "eligibility" })
            Assert.Equal("NotEvaluated", capture.Data.Value.GetProperty(name).GetString());
        Assert.Equal("NotAdopted", capture.Data.Value.GetProperty("retentionPolicy").GetString());
        Assert.Equal(4, capture.Data.Value.GetProperty("gaps").GetArrayLength());
    }
}
