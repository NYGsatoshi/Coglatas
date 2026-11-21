using System.Text;
using System.Text.Json;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;

namespace Coglatas.Tests.ProjectIde;

public sealed class RootSourceAdmissionTests
{
    public static IEnumerable<object[]> Cases() => WebPreviewContractTests.Cases().Where(r => {
        using var json = JsonDocument.Parse((string)r[0]);
        return json.RootElement.GetProperty("key").GetString() is "fail" or "pass" or "unknown" or "test-assertion-navigation";
    });
    public static IEnumerable<object[]> ProofCases() => Cases().Where(r => {
        using var json = JsonDocument.Parse((string)r[0]);
        return json.RootElement.GetProperty("key").GetString() is "fail" or "test-assertion-navigation";
    });
    [Theory]
    [MemberData(nameof(Cases))]
    public void ExplicitRootAdmissionBindsActualSourceWithoutClaimingRegionalIr(string text)
    {
        var (c, _, _) = DiagnosticPremiseSnapshotTests.Prepare(text);
        var admission = ProjectRootSourceAdmission.Read(c, c.Result, c.Result.ResultDigest);
        Assert.NotNull(admission); Assert.True(admission.IsOwnedBy(c)); Assert.Same(c.Input.Source, admission.Source);
        Assert.Equal(c.Input.InputDigest, admission.InputDigest); Assert.Equal(c.Result.ResultDigest, admission.ResultDigest);
        Assert.Equal(c.Input.Source.Data, ProjectSourceCodec.Decode(admission.Source.Data.ToCanonicalBytes()).RequireSource().Data);
        foreach (var diagnostic in c.Result.Diagnostics)
        {
            var manifest = ProjectDiagnosticPremiseSnapshot.ReadRoot(admission, diagnostic, c.Result.ResultDigest);
            Assert.Equal("RootCompilerInputObjects", manifest.BodyScope); Assert.Equal(DiagnosticPremiseSnapshotState.ManifestOnly, manifest.State);
            Assert.Empty(manifest.Bodies); Assert.Equal(0, manifest.RetainedBodyBytes);
            Assert.Equal("NotEvaluated", manifest.Data.Value.GetProperty("authority").GetString());
            Assert.Equal("NotRecorded", manifest.Data.Value.GetProperty("relationshipStructure").GetString());
            Assert.Equal(c.Result.Verdict.ToString(), manifest.Data.Value.GetProperty("result").GetProperty("verdict").GetString());
        }
    }
    [Theory]
    [MemberData(nameof(ProofCases))]
    public void ActualRootSourceBytesResolveWhileUnrecordedPredicateBodiesRemainMissing(string text)
    {
        var (c, _, _) = DiagnosticPremiseSnapshotTests.Prepare(text);
        Assert.Null(SyntheticScopedSourceProjection.Export(c));
        var admission = ProjectRootSourceAdmission.Read(c, c.Result, c.Result.ResultDigest)!;
        foreach (var diagnostic in c.Result.Diagnostics)
        {
            var read = ProjectDiagnosticPremiseSnapshot.ReadRoot(admission, diagnostic, c.Result.ResultDigest, DiagnosticPremiseSnapshotMode.AvailableBodies);
            if (diagnostic.Evidence.Kind == CompilerEvidenceKind.Missing)
            {
                Assert.Equal(DiagnosticPremiseSnapshotState.NoRecordedPremises, read.State); Assert.Empty(read.Bodies); Assert.Empty(read.References);
                Assert.Equal(diagnostic.Evidence.MissingReason, read.Diagnostic!.Evidence.MissingReason); continue;
            }
            Assert.Equal(DiagnosticPremiseSnapshotState.CapturedKnownBodies, read.State); Assert.Equal(2, read.Bodies.Count);
            Assert.Equal(c.Input.Source.Data, read.Bodies[c.Input.Source.Digest.Value]); Assert.Equal(c.Input.Data, read.Bodies[c.Input.InputDigest.Value]);
            foreach (var reference in read.References.Where(r => r.Resolution == DiagnosticPremiseResolution.ResolvedRetainedObject))
                Assert.Equal(reference.Digest, ContentDigest.Compute(ProjectDiagnosticPremiseSnapshot.BodyDomain(reference.Kind!.Value), read.Bodies[reference.Digest.Value].ToCanonicalBytes()));
            Assert.Equal(diagnostic.Evidence.Premises.Count - 2, read.References.Count(r => r.Resolution == DiagnosticPremiseResolution.Unavailable));
            var exactBytes = read.Bodies.Values.Sum(b => Encoding.UTF8.GetByteCount(b.CanonicalText));
            Assert.Equal(read.Data, ProjectDiagnosticPremiseSnapshot.ReadRoot(admission, diagnostic, c.Result.ResultDigest, DiagnosticPremiseSnapshotMode.AvailableBodies).Data);
            Assert.Equal(2, ProjectDiagnosticPremiseSnapshot.ReadRoot(admission, diagnostic, c.Result.ResultDigest, DiagnosticPremiseSnapshotMode.AvailableBodies, exactBytes).Bodies.Count);
            var limited = ProjectDiagnosticPremiseSnapshot.ReadRoot(admission, diagnostic, c.Result.ResultDigest, DiagnosticPremiseSnapshotMode.AvailableBodies, exactBytes - 1);
            Assert.Equal(DiagnosticPremiseSnapshotState.BodyBudgetExceeded, limited.State); Assert.Empty(limited.Bodies); Assert.Equal(0, limited.RetainedBodyBytes);
            Assert.Equal(diagnostic.Evidence.Kind, read.Diagnostic!.Evidence.Kind);
        }
    }
    [Theory]
    [MemberData(nameof(ProofCases))]
    public void ForeignEqualIdentityResultsAndFindingsCannotSubstituteForOwnedObjects(string text)
    {
        var (c, _, _) = DiagnosticPremiseSnapshotTests.Prepare(text); var (foreign, _, _) = DiagnosticPremiseSnapshotTests.Prepare(text);
        Assert.Equal(c.Result.ResultDigest, foreign.Result.ResultDigest);
        Assert.Null(ProjectRootSourceAdmission.Read(c, foreign.Result, c.Result.ResultDigest));
        Assert.Null(ProjectRootSourceAdmission.Read(c, c.Result, c.Input.InputDigest));
        var admission = ProjectRootSourceAdmission.Read(c, c.Result, c.Result.ResultDigest)!;
        Assert.False(admission.IsOwnedBy(foreign));
        foreach (var diagnostic in foreign.Result.Diagnostics)
        {
            var refused = ProjectDiagnosticPremiseSnapshot.ReadRoot(admission, diagnostic, c.Result.ResultDigest);
            Assert.Equal(DiagnosticPremiseSnapshotState.RefusedDiagnostic, refused.State); Assert.Null(refused.Diagnostic); Assert.Empty(refused.Bodies);
            Assert.Equal(JsonValueKind.Null, refused.Data.Value.GetProperty("basis").ValueKind);
        }
    }
    [Fact]
    public void RootAdmissionCannotOpenAForeignRegionalFindingOrIgnoreCancellationAndInvalidBounds()
    {
        var row = DiagnosticPremiseSnapshotTests.ProofCases().Select(r => (string)r[0]).First();
        var (c, _, _) = DiagnosticPremiseSnapshotTests.Prepare(row);
        var admission = ProjectRootSourceAdmission.Read(c, c.Result, c.Result.ResultDigest)!;
        var foreign = c.Dependencies!.Regions.SelectMany(region => region.Diagnostics).First(d => !d.Location.Context.Equals(c.Input.Source.Context));
        var refused = ProjectDiagnosticPremiseSnapshot.ReadRoot(admission, foreign, c.Result.ResultDigest);
        Assert.Equal(DiagnosticPremiseSnapshotState.RefusedDiagnostic, refused.State); Assert.Null(refused.Diagnostic); Assert.Empty(refused.Bodies);
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectDiagnosticPremiseSnapshot.ReadRoot(admission, foreign, c.Result.ResultDigest, maximumBodyBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectDiagnosticPremiseSnapshot.ReadRoot(admission, foreign, c.Result.ResultDigest, mode: (DiagnosticPremiseSnapshotMode)99));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); var token = cancellation.Token;
        Assert.Throws<OperationCanceledException>(() => ProjectRootSourceAdmission.Read(c, c.Result, c.Result.ResultDigest, token));
        Assert.Throws<OperationCanceledException>(() => ProjectDiagnosticPremiseSnapshot.ReadRoot(admission, foreign, c.Result.ResultDigest, cancellationToken: token));
    }
}
