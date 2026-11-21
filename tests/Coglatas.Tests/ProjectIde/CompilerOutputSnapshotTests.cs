using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class CompilerOutputSnapshotTests
{
    private static CoreCompilation Compilation() { var input = ProjectSimulationTests.Plan().Input; return PureCoreCompiler.Compile(input, input.Compiler); }
    private static ProjectCompilerOutputSnapshot Outputs(CoreCompilation compilation, int maximum = ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes) =>
        ProjectCompilerOutputSnapshot.Read(compilation, compilation.Result.ResultDigest, CompilerOutputSnapshotMode.AvailableOutputs, maximum);
    public static IEnumerable<object[]> ScopedCases() => WebSimulationTraceProfileContractTests.Cases();
    [Theory]
    [MemberData(nameof(ScopedCases))]
    public void ActualScopedCompilerOutputsRetainTheirOriginalBodiesStagesAndIndependentResultAxes(string fixture)
    {
        using var parsed = JsonDocument.Parse(fixture); var item = parsed.RootElement;
        var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(item.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(item.GetProperty("manifest").GetRawText()));
        var compilation = PureCoreCompiler.Compile(input, input.Compiler, WebPreviewContractTests.Snapshots(item, input));
        Assert.Equal(compilation.Result.Data, SourceJson.Parse(item.GetProperty("result").GetRawText()));
        var original = compilation.Result.Data; var manifest = ProjectCompilerOutputSnapshot.Read(compilation, compilation.Result.ResultDigest);
        Assert.Equal(CompilerOutputSnapshotState.ManifestOnly, manifest.State); Assert.Empty(manifest.Bodies); Assert.Equal(0, manifest.RetainedBodyBytes);
        Assert.Equal(CompilerFreshness.Undetermined, manifest.Freshness); Assert.Equal(input.InputDigest, manifest.InputDigest);
        Assert.Equal(input.Source.Digest, manifest.SourceDigest); Assert.Equal(input.Source.Context, manifest.Context); Assert.Equal(compilation.Result.ResultDigest, manifest.ResultDigest);
        Assert.Equal(compilation.Result.Execution.ToString(), manifest.Data.Value.GetProperty("result").GetProperty("execution").GetString());
        Assert.Equal(compilation.Result.Coverage.ToString(), manifest.Data.Value.GetProperty("result").GetProperty("coverage").GetString());
        Assert.Equal(compilation.Result.Verdict.ToString(), manifest.Data.Value.GetProperty("result").GetProperty("verdict").GetString());
        Assert.Equal(compilation.Result.EvidenceComplete, manifest.Data.Value.GetProperty("result").GetProperty("evidenceComplete").GetBoolean());
        Assert.Equal(compilation.Result.Stages.Select(stage => stage.Data), manifest.Data.Value.GetProperty("stages").EnumerateArray().Select(value => SourceJson.Parse(value.GetRawText())));
        Assert.All(manifest.Data.Value.GetProperty("outputs").EnumerateArray(), value => Assert.Equal("NotCaptured", value.GetProperty("body").GetString()));
        var captured = Outputs(compilation); Assert.Equal(CompilerOutputSnapshotState.CapturedAvailableOutputs, captured.State); Assert.Equal(3, captured.Bodies.Count);
        Assert.Same(compilation.Publication!.SemanticModel, captured.Bodies["coglatas.semantic-model"]); Assert.Same(compilation.Publication.Ir, captured.Bodies["coglatas.ir"]);
        Assert.Same(compilation.Publication.SourceMap, captured.Bodies["coglatas.source-map"]); Assert.Equal(captured.Bodies.Values.Sum(body => Encoding.UTF8.GetByteCount(body.CanonicalText)), captured.RetainedBodyBytes);
        Assert.Equal(manifest.Data.Value.GetProperty("result").GetRawText(), captured.Data.Value.GetProperty("result").GetRawText()); Assert.Same(original, compilation.Result.Data);
        Assert.Equal("NotRecorded", captured.Data.Value.GetProperty("intermediateStates").GetString()); Assert.Equal("NotAllocated", captured.Data.Value.GetProperty("runIdentity").GetString());
        foreach (var field in new[] { "authority", "producerTrust", "eligibility" }) Assert.Equal("NotEvaluated", captured.Data.Value.GetProperty(field).GetString());
        Assert.Equal("NotAdopted", captured.Data.Value.GetProperty("retentionPolicy").GetString()); Assert.Equal(4, captured.Data.Value.GetProperty("gaps").GetArrayLength());
    }
    [Fact]
    public void BodyBudgetIsAtomicEvenWhenOnlyTheLastOutputWouldExceedIt()
    {
        var compilation = Compilation(); var captured = Outputs(compilation); var exactBytes = captured.RetainedBodyBytes;
        var exact = Outputs(compilation, exactBytes); Assert.Equal(CompilerOutputSnapshotState.CapturedAvailableOutputs, exact.State); Assert.Equal(3, exact.Bodies.Count);
        var limited = Outputs(compilation, exactBytes - 1); Assert.Equal(CompilerOutputSnapshotState.BodyBudgetExceeded, limited.State);
        Assert.Empty(limited.Bodies); Assert.Equal(0, limited.RetainedBodyBytes); Assert.Equal(compilation.Result.ResultDigest, limited.ResultDigest);
        Assert.All(limited.Data.Value.GetProperty("outputs").EnumerateArray(), value => Assert.Equal("NotCaptured", value.GetProperty("body").GetString()));
        Assert.Equal(captured.Data.Value.GetProperty("result").GetRawText(), limited.Data.Value.GetProperty("result").GetRawText());
    }
    [Fact]
    public void WrongExpectedResultReturnsNoOriginalStampBodyOrResultSummary()
    {
        var compilation = Compilation(); var refused = ProjectCompilerOutputSnapshot.Read(compilation, compilation.Input.InputDigest, CompilerOutputSnapshotMode.AvailableOutputs);
        Assert.Equal(CompilerOutputSnapshotState.RefusedWrongResult, refused.State); Assert.Empty(refused.Bodies); Assert.Null(refused.InputDigest);
        Assert.Null(refused.ResultDigest); Assert.Null(refused.SourceDigest); Assert.Null(refused.Context); Assert.Empty(refused.ChangedDimensions);
        foreach (var field in new[] { "basis", "result", "desired", "freshness", "stages", "outputs" }) Assert.Equal(JsonValueKind.Null, refused.Data.Value.GetProperty(field).ValueKind);
    }
    [Fact]
    public void OriginalAndDesiredInputsHaveSeparateStampsAndFreshnessWithoutMutatingAnyResultAxes()
    {
        var compilation = Compilation(); var current = ProjectCompilerOutputSnapshot.Read(compilation, compilation.Result.ResultDigest, desiredInput: compilation.Input);
        var other = Input(source: Source(79)); var stale = ProjectCompilerOutputSnapshot.Read(compilation, compilation.Result.ResultDigest, desiredInput: other);
        Assert.Equal(CompilerFreshness.Current, current.Freshness); Assert.Empty(current.ChangedDimensions); Assert.Equal(CompilerFreshness.Stale, stale.Freshness); Assert.NotEmpty(stale.ChangedDimensions);
        Assert.Equal(compilation.Input.InputDigest, stale.InputDigest); Assert.Equal(compilation.Input.InputDigest.Data, SourceJson.Parse(stale.Data.Value.GetProperty("basis").GetProperty("inputDigest").GetRawText()));
        Assert.Equal(other.InputDigest.Data, SourceJson.Parse(stale.Data.Value.GetProperty("desired").GetProperty("inputDigest").GetRawText()));
        Assert.Equal(current.Data.Value.GetProperty("result").GetRawText(), stale.Data.Value.GetProperty("result").GetRawText()); Assert.NotEqual(current.Digest, stale.Digest);
    }
    [Theory]
    [InlineData("dictionary")] [InlineData("keys")] [InlineData("values")] [InlineData("entries-copy")] [InlineData("bytes-copy")]
    public void PublicOutputViewsExposeNoMutableStorageOrSynchronizationRoots(string surface)
    {
        var captured = Outputs(Compilation()); var data = captured.Data; var digest = captured.Digest; var body = captured.Bodies["coglatas.ir"]; var bytes = body.ToCanonicalBytes();
        Assert.False(captured.Bodies is ICollection); Assert.False(captured.Bodies.Keys is ICollection); Assert.False(captured.Bodies.Values is ICollection); Assert.False(captured.ChangedDimensions is ICollection);
        var dictionary = Assert.IsAssignableFrom<IDictionary<string, SourceJson>>(captured.Bodies);
        if (surface == "dictionary") Assert.Throws<NotSupportedException>(() => dictionary.Clear());
        if (surface == "keys") Assert.Throws<NotSupportedException>(() => dictionary.Keys.Clear());
        if (surface == "values") Assert.Throws<NotSupportedException>(() => dictionary.Values.Clear());
        if (surface == "entries-copy") { var copy = new KeyValuePair<string, SourceJson>[dictionary.Count]; dictionary.CopyTo(copy, 0); copy[0] = default; }
        if (surface == "bytes-copy") { var copy = body.ToCanonicalBytes(); copy[0] = 0; }
        Assert.Equal(3, captured.Bodies.Count); Assert.Same(body, captured.Bodies["coglatas.ir"]); Assert.Equal(bytes, body.ToCanonicalBytes()); Assert.Same(data, captured.Data); Assert.Same(digest, captured.Digest);
    }
    [Fact]
    public void LegacyArtifactReferencesStayAvailableWithoutFabricatedBodiesOrImplicitUpgrade()
    {
        var input = Input(); var compilation = PureCoreCompiler.Compile(input, input.Compiler); Assert.NotEmpty(compilation.Result.Artifacts);
        var captured = Outputs(compilation); Assert.Equal(CompilerOutputSnapshotState.UnavailableBodyProvider, captured.State); Assert.Empty(captured.Bodies);
        Assert.Equal(compilation.Result.Artifacts.Select(value => value.Data), captured.Data.Value.GetProperty("outputs").EnumerateArray().Select(value => SourceJson.Parse(value.GetProperty("reference").GetRawText())));
        Assert.Equal(compilation.Result.ResultDigest, captured.ResultDigest); Assert.Equal(compilation.Result.Stages.Count, captured.Data.Value.GetProperty("stages").GetArrayLength());
    }
    [Fact]
    public void CancelledCompilationHasNoUnrecordedOutputBodiesAndKeepsItsOriginalCancellationAxes()
    {
        var input = Compilation().Input; using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var compilation = PureCoreCompiler.Compile(input, input.Compiler, cancellationToken: cancellation.Token); var snapshot = Outputs(compilation);
        Assert.Equal(CompilerOutputSnapshotState.NoOutputsProduced, snapshot.State); Assert.Empty(snapshot.Bodies);
        Assert.Equal("Cancelled", snapshot.Data.Value.GetProperty("result").GetProperty("execution").GetString());
        Assert.Equal("NotRecorded", snapshot.Data.Value.GetProperty("intermediateStates").GetString()); Assert.Equal(compilation.Result.ResultDigest, snapshot.ResultDigest);
    }
    [Theory]
    [InlineData(0)] [InlineData(ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes + 1)]
    public void InvalidBodyBudgetsAreNeverClamped(int maximum)
    {
        var compilation = Compilation(); Assert.Throws<ArgumentOutOfRangeException>(() => Outputs(compilation, maximum));
    }
    [Fact]
    public void UnsupportedModesCannotBecomeAFullDumpOrAnImplicitManifest()
    {
        var compilation = Compilation(); Assert.Throws<ArgumentOutOfRangeException>(() => ProjectCompilerOutputSnapshot.Read(compilation, compilation.Result.ResultDigest, (CompilerOutputSnapshotMode)999));
    }
    [Fact]
    public void CancellationRefusesTheQueryWithoutChangingAnyOriginalCompilerOutput()
    {
        var compilation = Compilation(); var original = compilation.Result.Data; using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectCompilerOutputSnapshot.Read(compilation, compilation.Result.ResultDigest, cancellationToken: cancellation.Token));
        Assert.Same(original, compilation.Result.Data); Assert.NotNull(compilation.Publication);
    }
    [Theory]
    [InlineData("en-US")] [InlineData("fr-FR")] [InlineData("ar-SA")]
    public void OutputSnapshotBytesAreDeterministicAndCultureIndependent(string culture)
    {
        var compilation = Compilation(); var expected = Outputs(compilation); var original = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture); var actual = Outputs(compilation); Assert.Equal(expected.Data, actual.Data); Assert.Equal(expected.Digest, actual.Digest); }
        finally { CultureInfo.CurrentCulture = original; }
    }
}
