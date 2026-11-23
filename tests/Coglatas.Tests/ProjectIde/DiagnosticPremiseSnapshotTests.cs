using System.Collections;
using System.Text;
using System.Text.Json;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class DiagnosticPremiseSnapshotTests
{
    public static IEnumerable<object[]> Cases() => SyntheticDiagnosticBasisTests.Cases();
    public static IEnumerable<object[]> ProofCases() => Cases().Where(row => {
        using var json = JsonDocument.Parse((string)row[0]);
        return json.RootElement.GetProperty("result").GetProperty("diagnostics")[0].GetProperty("evidence").GetProperty("premises").GetArrayLength() > 0;
    });
    internal static (CoreCompilation Compilation, CompilerHostRequest Request, LocalNetCompilerHost Host) Prepare(string text)
    {
        using var json = JsonDocument.Parse(text); var row = json.RootElement;
        var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(row.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(row.GetProperty("manifest").GetRawText()));
        var snapshots = WebPreviewContractTests.Snapshots(row, input);
        CompilerArtifactIdentity Artifact(string key) => new(SourceJson.Parse(input.Settings.Value.GetProperty(key).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(value => value.GetString()), input.PlanLowerer);
        return (PureCoreCompiler.Compile(input, input.Compiler, snapshots), new(new(input), 7, snapshots), new(profile));
    }
    private static ProjectDiagnosticPremiseSnapshot Read(CoreCompilation c, CompilerDiagnostic d, int budget = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes) =>
        ProjectDiagnosticPremiseSnapshot.Read(c, d, c.Result.ResultDigest, DiagnosticPremiseSnapshotMode.AvailableBodies, budget);

    [Theory]
    [MemberData(nameof(Cases))]
    public void ActualRetainedObjectsPreserveExactBasisMissingReasonAndOriginalResultAxes(string text)
    {
        var (c, _, _) = Prepare(text); var before = c.Result.Data;
        using var json = JsonDocument.Parse(text); Assert.Equal(SourceJson.Parse(json.RootElement.GetProperty("result").GetRawText()), before);
        foreach (var diagnostic in c.Result.Diagnostics)
        {
            var manifest = ProjectDiagnosticPremiseSnapshot.Read(c, diagnostic, c.Result.ResultDigest);
            Assert.Equal(DiagnosticPremiseSnapshotState.ManifestOnly, manifest.State); Assert.Empty(manifest.Bodies); Assert.Equal(0, manifest.RetainedBodyBytes);
            var snapshot = Read(c, diagnostic); Assert.Same(diagnostic, snapshot.Diagnostic); Assert.True(snapshot.LookupComplete);
            Assert.Equal(c.Input.InputDigest, snapshot.InputDigest); Assert.Equal(c.Result.ResultDigest, snapshot.ResultDigest);
            Assert.Equal(diagnostic.Evidence.Premises, snapshot.References.Select(r => r.Digest));
            var basis = snapshot.Data.Value.GetProperty("basis");
            Assert.Equal(diagnostic.Evidence.Data, SourceJson.Parse(basis.GetProperty("evidence").GetRawText()));
            Assert.Equal(diagnostic.Location.Data, SourceJson.Parse(basis.GetProperty("location").GetRawText()));
            Assert.Equal(ContentDigest.Compute("coglatas.compiler-diagnostic/1", diagnostic.Data.ToCanonicalBytes()).Data,
                SourceJson.Parse(basis.GetProperty("diagnosticDigest").GetRawText()));
            foreach (var field in new[] { "authority", "producerTrust", "eligibility" }) Assert.Equal("NotEvaluated", snapshot.Data.Value.GetProperty(field).GetString());
            Assert.Equal("NotAdopted", snapshot.Data.Value.GetProperty("retentionPolicy").GetString());
            Assert.Equal("NotRecorded", snapshot.Data.Value.GetProperty("relationshipStructure").GetString());
            Assert.Equal("NotRecorded", snapshot.Data.Value.GetProperty("earlierStates").GetString());
            Assert.Equal(manifest.Data.Value.GetProperty("result").GetRawText(), snapshot.Data.Value.GetProperty("result").GetRawText());
            if (diagnostic.Evidence.Kind == CompilerEvidenceKind.Missing)
            {
                Assert.Equal(DiagnosticPremiseSnapshotState.NoRecordedPremises, snapshot.State); Assert.Empty(snapshot.References); Assert.Empty(snapshot.Bodies);
                Assert.NotNull(diagnostic.Evidence.MissingReason); Assert.False(c.Result.EvidenceComplete);
            }
            else
            {
                Assert.Equal(DiagnosticPremiseSnapshotState.CapturedKnownBodies, snapshot.State); Assert.Equal(3, snapshot.Bodies.Count);
                Assert.All(snapshot.References, r => Assert.Equal(DiagnosticPremiseResolution.ResolvedRetainedObject, r.Resolution));
                foreach (var reference in snapshot.References)
                {
                    var body = snapshot.Bodies[reference.Digest.Value];
                    Assert.Equal(reference.Digest, ContentDigest.Compute(ProjectDiagnosticPremiseSnapshot.BodyDomain(reference.Kind!.Value), body.ToCanonicalBytes()));
                    if (reference.Kind == DiagnosticPremiseBodyKind.RootInput) Assert.Same(c.Input.Data, body);
                    else if (reference.Kind == DiagnosticPremiseBodyKind.BindingRegion) Assert.Same(c.Dependencies!.Binding.Regions.Single(r => r.Digest.Equals(reference.Digest)).Data, body);
                    else Assert.Same(c.Dependencies!.Regions.Single(r => r.Digest.Equals(reference.Digest)).Data, body);
                }
                Assert.Equal(snapshot.Bodies.Values.Sum(b => b.ToCanonicalBytes().Length), snapshot.RetainedBodyBytes);
            }
        }
        Assert.Same(before, c.Result.Data); Assert.NotEqual(CompilerVerdict.Pass, c.Result.Verdict);
    }

    [Theory]
    [MemberData(nameof(ProofCases))]
    public void LastByteBudgetRefusalIsAtomicAndDoesNotReplaceTheRecordedBasis(string text)
    {
        var (c, _, _) = Prepare(text); var d = c.Result.Diagnostics.Single(); var all = Read(c, d);
        var exact = Read(c, d, all.RetainedBodyBytes);
        Assert.Equal(DiagnosticPremiseSnapshotState.CapturedKnownBodies, exact.State); Assert.Equal(3, exact.Bodies.Count);
        Assert.Equal(all.RetainedBodyBytes, exact.RetainedBodyBytes);
        foreach (var budget in new[] { 1, all.RetainedBodyBytes - 1 })
        {
            var limited = Read(c, d, budget); Assert.Equal(DiagnosticPremiseSnapshotState.BodyBudgetExceeded, limited.State);
            Assert.Empty(limited.Bodies); Assert.Equal(0, limited.RetainedBodyBytes); Assert.Same(d, limited.Diagnostic);
            Assert.Equal(d.Evidence.Premises, limited.References.Select(r => r.Digest));
            Assert.All(limited.Data.Value.GetProperty("references").EnumerateArray(), r => Assert.False(r.GetProperty("bodyCaptured").GetBoolean()));
            Assert.Equal(all.Data.Value.GetProperty("result").GetRawText(), limited.Data.Value.GetProperty("result").GetRawText());
        }
    }

    [Fact]
    public void UnavailableActualPredicatePremisesRemainVisibleBesideTheRetainedInput()
    {
        var text = WebPreviewContractTests.Cases().Select(r => (string)r[0]).Single(text => {
            using var json = JsonDocument.Parse(text); return json.RootElement.GetProperty("key").GetString() == "fail";
        });
        var (c, _, _) = Prepare(text); var d = c.Result.Diagnostics.Single(); var snapshot = Read(c, d);
        Assert.Equal(DiagnosticPremiseSnapshotState.CapturedKnownBodies, snapshot.State); Assert.Equal(3, snapshot.References.Count); Assert.Equal(2, snapshot.Bodies.Count);
        Assert.Equal(c.Input.Source.Data, snapshot.Bodies[c.Input.Source.Digest.Value]);
        Assert.Equal(DiagnosticPremiseBodyKind.RootSource, snapshot.References.Single(r => r.Digest.Equals(c.Input.Source.Digest)).Kind);
        Assert.Single(snapshot.References, r => r.Resolution == DiagnosticPremiseResolution.Unavailable);
        Assert.All(snapshot.References.Where(r => r.Resolution == DiagnosticPremiseResolution.Unavailable), r => Assert.Null(r.Kind));
        Assert.Equal(CompilerEvidenceKind.ModelProof, d.Evidence.Kind); Assert.Equal(CompilerVerdict.Fail, c.Result.Verdict);
    }

    [Fact]
    public void WrongResultAndSameIdentityForeignDiagnosticExposeNoOriginalMetadata()
    {
        var text = (string)ProofCases().First()[0]; var (c, _, _) = Prepare(text); var (copy, _, _) = Prepare(text);
        var original = c.Result.Diagnostics.Single(); var foreign = copy.Result.Diagnostics.Single(); Assert.Equal(original.DiagnosticId, foreign.DiagnosticId); Assert.NotSame(original, foreign);
        var refused = new[] { ProjectDiagnosticPremiseSnapshot.Read(c, original, c.Input.InputDigest), ProjectDiagnosticPremiseSnapshot.Read(c, foreign, c.Result.ResultDigest) };
        Assert.Equal(DiagnosticPremiseSnapshotState.RefusedWrongResult, refused[0].State); Assert.Equal(DiagnosticPremiseSnapshotState.RefusedDiagnostic, refused[1].State);
        foreach (var snapshot in refused)
        {
            Assert.Null(snapshot.Diagnostic); Assert.Null(snapshot.InputDigest); Assert.Null(snapshot.ResultDigest); Assert.Empty(snapshot.References); Assert.Empty(snapshot.Bodies);
            Assert.Equal(JsonValueKind.Null, snapshot.Data.Value.GetProperty("basis").ValueKind); Assert.Equal(JsonValueKind.Null, snapshot.Data.Value.GetProperty("result").ValueKind);
        }
    }

    [Fact]
    public void ReturnedMapsListsAndByteCopiesCannotAlterOriginalBodiesOrDigests()
    {
        var (c, _, _) = Prepare((string)ProofCases().First()[0]); var snapshot = Read(c, c.Result.Diagnostics.Single()); var before = snapshot.Data;
        Assert.False(snapshot.Bodies is IDictionary); Assert.False(snapshot.References is IList);
        var dictionary = Assert.IsAssignableFrom<IDictionary<string, SourceJson>>(snapshot.Bodies);
        var first = dictionary.First(); Assert.True(dictionary.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => dictionary.Add("unrecorded", first.Value));
        Assert.Throws<NotSupportedException>(() => dictionary[first.Key] = SourceJson.Parse("{}"));
        Assert.Throws<NotSupportedException>(() => dictionary.Remove(first.Key));
        Assert.Throws<NotSupportedException>(() => dictionary.Clear());
        Assert.Throws<NotSupportedException>(() => dictionary.Keys.Clear());
        Assert.Throws<NotSupportedException>(() => dictionary.Values.Clear());
        var references = Assert.IsAssignableFrom<IList<DiagnosticPremiseReference>>(snapshot.References);
        Assert.True(references.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => references[0] = references[0]);
        Assert.Throws<NotSupportedException>(() => references.Add(references[0]));
        Assert.Throws<NotSupportedException>(() => references.RemoveAt(0));
        Assert.Throws<NotSupportedException>(() => references.Clear());
        foreach (var body in snapshot.Bodies.Values)
        {
            var text = body.CanonicalText; var bytes = body.ToCanonicalBytes(); bytes[0] ^= 1; Assert.Equal(text, body.CanonicalText);
        }
        Assert.Same(before, snapshot.Data); Assert.Equal(snapshot.Digest, ContentDigest.Compute(ProjectDiagnosticPremiseSnapshot.Format, snapshot.Data.ToCanonicalBytes()));
    }

    [Fact]
    public void InvalidModesBudgetsAndPreCancelledReadsDoNotProduceSnapshots()
    {
        var (c, _, _) = Prepare((string)ProofCases().First()[0]); var d = c.Result.Diagnostics.Single();
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectDiagnosticPremiseSnapshot.Read(c, d, c.Result.ResultDigest, (DiagnosticPremiseSnapshotMode)99));
        foreach (var budget in new[] { -1, 0, ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes + 1 }) Assert.Throws<ArgumentOutOfRangeException>(() => Read(c, d, budget));
        using var token = new CancellationTokenSource(); token.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectDiagnosticPremiseSnapshot.Read(c, d, c.Result.ResultDigest, cancellationToken: token.Token));
    }
}
