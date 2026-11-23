using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class RecordedPredicatePremiseSnapshotTests
{
    public static IEnumerable<object[]> Cases() => RootSourceAdmissionTests.Cases();
    private static CoreCompilation Capture(CompilerInputManifest input, CompilerPredicateCapturePolicy? policy = null) =>
        PureCoreCompiler.CompileWithPredicateCapture(input, input.Compiler, policy ?? new(CompilerPredicateCaptureMode.OriginalRootArrays));
    private static ProjectRootSourceAdmission Admit(CoreCompilation compilation) =>
        Assert.IsType<ProjectRootSourceAdmission>(ProjectRootSourceAdmission.Read(compilation, compilation.Result, compilation.Result.ResultDigest));
    private static ProjectRecordedPredicatePremiseSnapshot Read(CoreCompilation compilation, CompilerDiagnostic diagnostic,
        DiagnosticPremiseSnapshotMode mode = DiagnosticPremiseSnapshotMode.AvailableBodies, int budget = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes) =>
        ProjectRecordedPredicatePremiseSnapshot.Read(Admit(compilation), compilation.PredicatePremises!, diagnostic, compilation.Result.ResultDigest, mode, budget);

    [Theory]
    [MemberData(nameof(Cases))]
    public void ExplicitReaderKeepsOriginalResultAndOldInputOnlySnapshotBytesWhileOpeningOnlySelectedArrays(string text)
    {
        var (control, _, _) = DiagnosticPremiseSnapshotTests.Prepare(text);
        var compilation = Capture(control.Input); var admission = Admit(compilation);
        Assert.Equal(control.Result.Data, compilation.Result.Data); Assert.Equal(control.LogicalOperationsConsumed, compilation.LogicalOperationsConsumed);
        using var json = JsonDocument.Parse(text); var key = json.RootElement.GetProperty("key").GetString();
        Assert.Equal(key is "fail" or "pass" ? 1 : 0, compilation.PredicatePremises!.Bodies.Count);
        Assert.Equal(control.Result.Diagnostics.Count, compilation.Result.Diagnostics.Count);
        for (var i = 0; i < compilation.Result.Diagnostics.Count; i++)
        {
            var diagnostic = compilation.Result.Diagnostics[i];
            var old = ProjectDiagnosticPremiseSnapshot.ReadRoot(admission, diagnostic, compilation.Result.ResultDigest, DiagnosticPremiseSnapshotMode.AvailableBodies);
            var oldControl = ProjectDiagnosticPremiseSnapshot.ReadRoot(Admit(control), control.Result.Diagnostics[i], control.Result.ResultDigest, DiagnosticPremiseSnapshotMode.AvailableBodies);
            Assert.Equal(oldControl.Data, old.Data); Assert.Equal(oldControl.Bodies.Keys, old.Bodies.Keys);
            var snapshot = Read(compilation, diagnostic);
            Assert.Equal(key == "fail" ? RecordedPredicateSnapshotState.CapturedOriginalArrays : diagnostic.Evidence.Premises.Count == 0
                ? RecordedPredicateSnapshotState.NoRecordedPremises : RecordedPredicateSnapshotState.NoAvailableOriginalArrays, snapshot.State);
            Assert.Equal(key == "fail" ? 1 : 0, snapshot.Bodies.Count);
            if (key == "fail") Assert.Equal(134, snapshot.RetainedBodyBytes);
            Assert.DoesNotContain(compilation.Input.InputDigest.Value, snapshot.Bodies.Keys);
            Assert.DoesNotContain(compilation.Input.Source.Digest.Value, snapshot.Bodies.Keys);
            Assert.Same(diagnostic, snapshot.Diagnostic); Assert.Same(compilation.PredicatePremises.Data, snapshot.OriginalRecordMetadata);
            Assert.Equal(compilation.PredicatePremises.Digest, snapshot.OriginalRecordDigest);
            Assert.Equal(compilation.PredicatePremises.State, snapshot.OriginalCaptureState);
            Assert.Equal(compilation.Result.ResultDigest, snapshot.ResultDigest); Assert.Equal(compilation.Input.InputDigest, snapshot.InputDigest);
            Assert.Equal(diagnostic.Evidence.Premises, snapshot.References.Select(r => r.Digest));
            foreach (var item in snapshot.Bodies)
            {
                Assert.Same(compilation.PredicatePremises.Bodies[item.Key], item.Value);
                Assert.Contains(diagnostic.Evidence.Premises, p => p.Value == item.Key);
                Assert.Equal(item.Key, ContentDigest.Compute(ProjectPredicatePremiseRecord.BodyDomain, item.Value.ToCanonicalBytes()).Value);
                Assert.DoesNotContain(item.Key, old.Bodies.Keys);
            }
            Assert.Equal(snapshot.Digest, ContentDigest.Compute(ProjectRecordedPredicatePremiseSnapshot.Format, snapshot.Data.ToCanonicalBytes()));
            Assert.Equal("SelectedDiagnosticOriginalRootPredicateArrays", snapshot.Data.Value.GetProperty("bodyScope").GetString());
            foreach (var field in new[] { "regionalEvaluation", "relationshipStructure", "earlierStates" }) Assert.Equal("NotRecorded", snapshot.Data.Value.GetProperty(field).GetString());
            foreach (var field in new[] { "runSelection", "authority", "producerTrust", "eligibility" }) Assert.Equal("NotEvaluated", snapshot.Data.Value.GetProperty(field).GetString());
            Assert.Equal("NotAdopted", snapshot.Data.Value.GetProperty("retentionPolicy").GetString());
        }
    }

    [Fact]
    public void DefaultManifestListsAvailabilityWithoutReturningBodiesOrChangingOriginalCapture()
    {
        var compilation = Capture(CompilerAnalysisFixtures.Constraint(false)); var diagnostic = Assert.Single(compilation.Result.Diagnostics);
        var manifest = ProjectRecordedPredicatePremiseSnapshot.Read(Admit(compilation), compilation.PredicatePremises!, diagnostic, compilation.Result.ResultDigest);
        Assert.Equal(RecordedPredicateSnapshotState.ManifestOnly, manifest.State); Assert.Empty(manifest.Bodies); Assert.Equal(0, manifest.RetainedBodyBytes);
        Assert.Single(manifest.References, r => r.AvailableInOriginalRecord);
        Assert.All(manifest.Data.Value.GetProperty("references").EnumerateArray(), r => Assert.False(r.GetProperty("bodyCaptured").GetBoolean()));
        Assert.Equal(PredicatePremiseRecordState.CapturedOriginalArrays, manifest.OriginalCaptureState); Assert.Single(compilation.PredicatePremises!.Bodies);
        Assert.Equal(Read(compilation, diagnostic, DiagnosticPremiseSnapshotMode.Manifest).Data, manifest.Data);
    }

    private static CompilerInputManifest TwoArrays()
    {
        var first = CompilerAnalysisFixtures.Constraint(false); var other = CompilerAnalysisFixtures.Constraint(true);
        var declarations = JsonNode.Parse(first.Source.Declarations.CanonicalText)!.AsArray();
        var declaration = JsonNode.Parse(other.Source.Declarations.CanonicalText)![0]!.DeepClone(); declaration["entityId"] = Id(8); declarations.Add(declaration);
        var relations = JsonNode.Parse(first.Source.Relations.CanonicalText)!.AsArray();
        var relation = JsonNode.Parse(other.Source.Relations.CanonicalText)![0]!.DeepClone(); relation["relationId"] = Id(21);
        relation["source"]!["reference"]!["entityId"] = Id(8); relation["payload"]!["predicateReference"]!["entityId"] = Id(8); relations.Add(relation);
        return new(ProjectSource.Create(first.Source.Context, first.Source.Documents, Json(declarations), Json(relations)), first.Settings);
    }

    [Fact]
    public void ReaderDoesNotDiscloseAnotherSuccessfulRulesArrayAndViewerBudgetCannotBecomeCaptureGap()
    {
        var compilation = Capture(TwoArrays()); Assert.Equal(2, compilation.PredicatePremises!.Bodies.Count);
        var diagnostic = Assert.Single(compilation.Result.Diagnostics); var full = Read(compilation, diagnostic);
        var selected = Assert.Single(full.Bodies); Assert.Equal(134, full.RetainedBodyBytes);
        var unrelated = Assert.Single(compilation.PredicatePremises.Bodies, b => b.Key != selected.Key);
        Assert.DoesNotContain(unrelated.Key, full.References.Select(r => r.Digest.Value));
        Assert.Equal(full.Data, Read(compilation, diagnostic).Data);
        var exact = Read(compilation, diagnostic, budget: 134); Assert.Single(exact.Bodies); Assert.Same(selected.Value, exact.Bodies[selected.Key]);
        var limited = Read(compilation, diagnostic, budget: 133);
        Assert.Equal(RecordedPredicateSnapshotState.BodyBudgetExceeded, limited.State); Assert.Empty(limited.Bodies); Assert.Equal(0, limited.RetainedBodyBytes);
        Assert.Equal(PredicatePremiseRecordState.CapturedOriginalArrays, limited.OriginalCaptureState);
        Assert.Equal(2, compilation.PredicatePremises.Bodies.Count); Assert.Equal(compilation.PredicatePremises.Digest, limited.OriginalRecordDigest);
        Assert.Equal(CompilerVerdict.Fail, compilation.Result.Verdict); Assert.Equal(full.References.Select(r => r.Digest), limited.References.Select(r => r.Digest));
        Assert.All(limited.Data.Value.GetProperty("references").EnumerateArray(), r => Assert.False(r.GetProperty("bodyCaptured").GetBoolean()));
    }

    [Theory]
    [InlineData(CompilerPredicateCaptureMode.Manifest, 1048576, 1024, PredicatePremiseRecordState.ManifestOnly)]
    [InlineData(CompilerPredicateCaptureMode.OriginalRootArrays, 1, 1024, PredicatePremiseRecordState.BodyBudgetExceeded)]
    [InlineData(CompilerPredicateCaptureMode.OriginalRootArrays, 1048576, 1, PredicatePremiseRecordState.BodyCountLimitReached)]
    public void OriginalCaptureGapsStayVisibleAndCannotBeReconstructedByALargerViewerBudget(CompilerPredicateCaptureMode mode, int bytes, int count, PredicatePremiseRecordState gap)
    {
        var compilation = Capture(TwoArrays(), new(mode, bytes, count)); var record = compilation.PredicatePremises!;
        var snapshot = Read(compilation, Assert.Single(compilation.Result.Diagnostics), budget: ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes);
        Assert.Equal(gap, record.State); Assert.Equal(gap, snapshot.OriginalCaptureState);
        Assert.Equal(RecordedPredicateSnapshotState.NoAvailableOriginalArrays, snapshot.State); Assert.Empty(snapshot.Bodies); Assert.Empty(record.Bodies);
        Assert.All(snapshot.References, r => Assert.False(r.AvailableInOriginalRecord)); Assert.Equal(CompilerVerdict.Fail, compilation.Result.Verdict);
        Assert.Equal(gap.ToString(), snapshot.Data.Value.GetProperty("originalRecord").GetProperty("metadata").GetProperty("state").GetString());
    }

    [Fact]
    public void SameInputEqualDigestForeignRecordAndForeignFindingCannotSubstituteForOwnedOriginalObjects()
    {
        var input = CompilerAnalysisFixtures.Constraint(false); var compilation = Capture(input); var foreign = Capture(input);
        var admission = Admit(compilation); var diagnostic = Assert.Single(compilation.Result.Diagnostics);
        Assert.Equal(compilation.Result.ResultDigest, foreign.Result.ResultDigest); Assert.Equal(compilation.PredicatePremises!.Digest, foreign.PredicatePremises!.Digest);
        var wrongRecord = ProjectRecordedPredicatePremiseSnapshot.Read(admission, foreign.PredicatePremises, diagnostic, compilation.Result.ResultDigest);
        var wrongDiagnostic = ProjectRecordedPredicatePremiseSnapshot.Read(admission, compilation.PredicatePremises, Assert.Single(foreign.Result.Diagnostics), compilation.Result.ResultDigest);
        var wrongResult = ProjectRecordedPredicatePremiseSnapshot.Read(admission, compilation.PredicatePremises, diagnostic, compilation.Input.InputDigest);
        Assert.Equal(RecordedPredicateSnapshotState.RefusedRecord, wrongRecord.State); Assert.Equal(RecordedPredicateSnapshotState.RefusedDiagnostic, wrongDiagnostic.State);
        Assert.Equal(RecordedPredicateSnapshotState.RefusedWrongResult, wrongResult.State);
        foreach (var refused in new[] { wrongRecord, wrongDiagnostic, wrongResult })
        {
            Assert.Null(refused.Diagnostic); Assert.Null(refused.InputDigest); Assert.Null(refused.ResultDigest); Assert.Null(refused.OriginalRecordDigest);
            Assert.Null(refused.OriginalRecordMetadata); Assert.Null(refused.OriginalCaptureState); Assert.Empty(refused.References); Assert.Empty(refused.Bodies);
            foreach (var field in new[] { "basis", "originalRecord", "result" }) Assert.Equal(JsonValueKind.Null, refused.Data.Value.GetProperty(field).ValueKind);
        }
        Assert.Null(ProjectRootSourceAdmission.Read(compilation, foreign.Result, compilation.Result.ResultDigest));
        var old = PureCoreCompiler.Compile(input, input.Compiler); var noRecord = ProjectRecordedPredicatePremiseSnapshot.Read(Admit(old), compilation.PredicatePremises, Assert.Single(old.Result.Diagnostics), old.Result.ResultDigest);
        Assert.Equal(RecordedPredicateSnapshotState.RefusedRecord, noRecord.State); Assert.Empty(noRecord.Bodies); Assert.Null(old.PredicatePremises);
    }

    [Fact]
    public void ImmutableSnapshotViewsAndReturnedByteCopiesCannotMutateOriginalFrames()
    {
        var compilation = Capture(CompilerAnalysisFixtures.Constraint(false)); var snapshot = Read(compilation, Assert.Single(compilation.Result.Diagnostics));
        var before = snapshot.Data; var body = Assert.Single(snapshot.Bodies); var bytes = body.Value.ToCanonicalBytes(); bytes[0] ^= 1;
        Assert.False(snapshot.Bodies is IDictionary); Assert.False(snapshot.References is IList);
        var map = Assert.IsAssignableFrom<IDictionary<string, SourceJson>>(snapshot.Bodies); var list = Assert.IsAssignableFrom<IList<RecordedPredicatePremiseReference>>(snapshot.References);
        Assert.Throws<NotSupportedException>(() => map.Clear()); Assert.Throws<NotSupportedException>(() => map[body.Key] = SourceJson.Parse("[]"));
        Assert.Throws<NotSupportedException>(() => map.Keys.Clear()); Assert.Throws<NotSupportedException>(() => map.Values.Clear()); Assert.Throws<NotSupportedException>(() => list.Clear());
        Assert.Same(before, snapshot.Data); Assert.Same(body.Value, compilation.PredicatePremises!.Bodies[body.Key]);
        Assert.Equal(body.Key, ContentDigest.Compute(ProjectPredicatePremiseRecord.BodyDomain, body.Value.ToCanonicalBytes()).Value);
    }

    [Fact]
    public void RootReadRefusesActualForeignContextFindingsFromTheSameCapturedCompilation()
    {
        var text = (string)DiagnosticPremiseSnapshotTests.ProofCases().First()[0];
        var (control, request, _) = DiagnosticPremiseSnapshotTests.Prepare(text);
        var compilation = PureCoreCompiler.CompileWithPredicateCapture(control.Input, control.Input.Compiler,
            new(CompilerPredicateCaptureMode.OriginalRootArrays), request.Snapshots, request.Units, request.Calendars);
        var admission = Admit(compilation); var record = compilation.PredicatePremises!;
        var foreign = compilation.Dependencies!.Regions.SelectMany(region => region.Diagnostics).First(d => !d.Location.Context.Equals(compilation.Input.Source.Context));
        var refused = ProjectRecordedPredicatePremiseSnapshot.Read(admission, record, foreign, compilation.Result.ResultDigest, DiagnosticPremiseSnapshotMode.AvailableBodies);
        Assert.Equal(RecordedPredicateSnapshotState.RefusedDiagnostic, refused.State); Assert.Empty(refused.Bodies); Assert.Empty(refused.References);
        Assert.Null(refused.OriginalRecordDigest); Assert.Null(refused.OriginalRecordMetadata); Assert.Null(refused.Diagnostic);
        Assert.Equal(JsonValueKind.Null, refused.Data.Value.GetProperty("basis").ValueKind);
        Assert.Equal(control.Result.Data, compilation.Result.Data);
    }

    [Fact]
    public void InterruptedCaptureKeepsItsCompilerResourceStateAndCannotSupplyAnOriginalArray()
    {
        var original = CompilerAnalysisFixtures.Constraint(false); var settings = JsonNode.Parse(original.Settings.CanonicalText)!.AsObject();
        settings["executionProfile"]!["maximumOperations"] = 1;
        var compilation = Capture(new(original.Source, Json(settings)));
        Assert.Equal(CompilerExecutionState.ResourceExhausted, compilation.Result.Execution);
        Assert.Equal(PredicatePremiseRecordState.CaptureInterrupted, compilation.PredicatePremises!.State); Assert.NotEmpty(compilation.Result.Diagnostics);
        foreach (var diagnostic in compilation.Result.Diagnostics)
        {
            var snapshot = Read(compilation, diagnostic);
            Assert.Equal(PredicatePremiseRecordState.CaptureInterrupted, snapshot.OriginalCaptureState); Assert.Empty(snapshot.Bodies);
            Assert.Equal(diagnostic.Evidence.Premises.Count == 0 ? RecordedPredicateSnapshotState.NoRecordedPremises
                : RecordedPredicateSnapshotState.NoAvailableOriginalArrays, snapshot.State);
            Assert.Equal("ResourceExhausted", snapshot.Data.Value.GetProperty("result").GetProperty("execution").GetString());
        }
    }

    [Fact]
    public void InvalidBoundsModeNullArgumentsAndCancellationCannotDiscloseOriginalRecordMetadata()
    {
        var compilation = Capture(CompilerAnalysisFixtures.Constraint(false)); var admission = Admit(compilation); var record = compilation.PredicatePremises!; var diagnostic = Assert.Single(compilation.Result.Diagnostics);
        foreach (var budget in new[] { -1, 0, ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes + 1 })
            Assert.Throws<ArgumentOutOfRangeException>(() => Read(compilation, diagnostic, budget: budget));
        Assert.Throws<ArgumentOutOfRangeException>(() => Read(compilation, diagnostic, (DiagnosticPremiseSnapshotMode)99));
        Assert.Throws<ArgumentNullException>(() => ProjectRecordedPredicatePremiseSnapshot.Read(null!, record, diagnostic, compilation.Result.ResultDigest));
        Assert.Throws<ArgumentNullException>(() => ProjectRecordedPredicatePremiseSnapshot.Read(admission, null!, diagnostic, compilation.Result.ResultDigest));
        Assert.Throws<ArgumentNullException>(() => ProjectRecordedPredicatePremiseSnapshot.Read(admission, record, null!, compilation.Result.ResultDigest));
        Assert.Throws<ArgumentNullException>(() => ProjectRecordedPredicatePremiseSnapshot.Read(admission, record, diagnostic, null!));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectRecordedPredicatePremiseSnapshot.Read(admission, record, diagnostic, compilation.Result.ResultDigest, cancellationToken: cancellation.Token));
        Assert.Single(record.Bodies); Assert.Equal(CompilerVerdict.Fail, compilation.Result.Verdict);
    }
}
