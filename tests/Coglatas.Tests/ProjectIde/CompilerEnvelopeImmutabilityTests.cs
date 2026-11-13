using Coglatas.Domain.ProjectIde;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;
namespace Coglatas.Tests.ProjectIde;
public sealed class CompilerEnvelopeImmutabilityTests
{
    [Fact]
    public void LockedManifestRecompilesIdenticallyAfterPublicCollectionAccess()
    {
        var input = Input(); var expected = PureCoreCompiler.Compile(input, input.Compiler).Result.Data;
        var settings = Settings(); foreach (var rule in settings["rules"]!.AsArray()) rule!["required"] = false;
        var alternate = Input(settings);
        if (input.Rules is System.Collections.ICollection view && view.SyncRoot is CompilerRuleContract[] backing)
            backing[0] = alternate.Rules[0];
        Assert.Equal(expected, PureCoreCompiler.Compile(input, input.Compiler).Result.Data);
    }
    [Fact]
    public void EvidencePremisesAlwaysReproduceTheirRetainedCanonicalBasis()
    {
        var input = Input(); var proof = Proof(input);
        if (proof.Premises is System.Collections.ICollection view && view.SyncRoot is ContentDigest[] backing)
            backing[0] = ContentDigest.Compute("example.replacement", new byte[]{1});
        Assert.Equal(proof.Data, new CompilerEvidenceBasis(proof.Kind, proof.Premises).Data);
    }
    [Fact]
    public void RetainedPassCannotExposeAChangedTypedRuleVerdictUnderItsOriginalResultDigest()
    {
        var input = Input(); var result = Pass(input); var original = result.RuleOutcomes[0];
        if (result.RuleOutcomes is System.Collections.ICollection view && view.SyncRoot is CompilerRuleOutcome[] backing)
            backing[0] = new(original.RuleId, original.Version, CompilerExecutionState.Completed, CompilerCoverage.Complete, CompilerVerdict.Fail, original.Evidence);
        var reconstructed = new CompilerResult(input,result.Execution,result.Coverage,result.RuleOutcomes,result.Diagnostics,result.Stages,result.EvidenceComplete,result.Artifacts);
        Assert.Equal(result.Verdict, reconstructed.Verdict); Assert.Equal(result.Data, reconstructed.Data);
    }

    [Theory]
    [InlineData("input-rules")]
    [InlineData("evidence-premises")]
    [InlineData("result-rules")]
    [InlineData("result-diagnostics")]
    [InlineData("result-stages")]
    [InlineData("result-artifacts")]
    [InlineData("ir-nodes")]
    [InlineData("fresh-current")]
    [InlineData("fresh-stale")]
    [InlineData("fresh-absent")]
    public void EveryCompilerEnvelopeViewPreservesItsCanonicalSnapshotUnderPublicMutationAttempts(string surface)
    {
        var input = Input(); var compilation = PureCoreCompiler.Compile(input, input.Compiler); var result = compilation.Result;
        switch (surface)
        {
            case "input-rules": Preserve(input.Rules); break;
            case "evidence-premises": Preserve(Proof(input).Premises); break;
            case "result-rules": Preserve(result.RuleOutcomes); break;
            case "result-diagnostics":
                var failing = CompilerAnalysisFixtures.Constraint(false);
                var diagnosticResult = PureCoreCompiler.Compile(failing, failing.Compiler).Result;
                Assert.NotEmpty(diagnosticResult.Diagnostics); Preserve(diagnosticResult.Diagnostics); break;
            case "result-stages": Preserve(result.Stages); break;
            case "result-artifacts": Assert.NotEmpty(result.Artifacts); Preserve(result.Artifacts); break;
            case "ir-nodes": Assert.NotNull(compilation.Validation!.Ir); Preserve(compilation.Validation.Ir.Nodes); break;
            case "fresh-current":
                var current = CompilerFreshnessAssessment.Assess(result, input); Assert.Empty(current.ChangedDimensions);
                Assert.Equal(CompilerFreshness.Current, current.State); Preserve(current.ChangedDimensions); break;
            case "fresh-stale":
                var stale = CompilerFreshnessAssessment.Assess(result, Input(source: Source(9)));
                Assert.Equal(CompilerFreshness.Stale, stale.State); Assert.NotEmpty(stale.ChangedDimensions); Preserve(stale.ChangedDimensions); break;
            case "fresh-absent":
                var absent = CompilerFreshnessAssessment.Assess(result, null);
                Assert.Equal(CompilerFreshness.Undetermined, absent.State); Preserve(absent.ChangedDimensions); break;
            default: throw new ArgumentOutOfRangeException(nameof(surface));
        }
    }

    private static void Preserve<T>(IReadOnlyList<T> values)
    {
        var before = values.ToArray();
        if (values is System.Collections.ICollection legacy && legacy.SyncRoot is T[] backing && backing.Length > 0)
            backing[0] = default!;
        Assert.Equal(before, values);
        if (values is ICollection<T> generic) Assert.Throws<NotSupportedException>(() => generic.Clear());
        if (values is IList<T> indexed && indexed.Count > 0) Assert.Throws<NotSupportedException>(() => indexed[0] = default!);
        Assert.Equal(before, values);
    }

    [Fact]
    public void CallerOwnedArraysAndListsCannotChangeTheRetainedResultOrEvidence()
    {
        var input = Input(); var premises = new[] { input.InputDigest, input.Source.Digest };
        var proof = new CompilerEvidenceBasis(CompilerEvidenceKind.ModelProof, premises); var originalProof = proof.Data;
        premises[0] = ContentDigest.Compute("example.changed", new byte[] { 2 });
        Assert.Equal(originalProof, new CompilerEvidenceBasis(proof.Kind, proof.Premises).Data);
        var rules = Passes(input); var stages = new[] { Stage(input) }; var diagnostics = new List<CompilerDiagnostic>();
        var result = new CompilerResult(input, CompilerExecutionState.Completed, CompilerCoverage.Complete, rules, diagnostics, stages, true);
        var expected = result.Data; var digest = result.ResultDigest;
        rules[0] = new(rules[0].RuleId, rules[0].Version, CompilerExecutionState.Completed, CompilerCoverage.Complete, CompilerVerdict.Fail, proof);
        stages[0] = new("example.other-stage", 1, CompilerExecutionState.Completed, CompilerCoverage.Complete, input.InputDigest, []);
        diagnostics.Add(new(input, SchemaRule, 1, 0, "example.added", ProjectLocation.Create(input.Source.Documents[0].DocumentId, input.Source.Context), proof));
        var reconstructed = new CompilerResult(input, result.Execution, result.Coverage, result.RuleOutcomes, result.Diagnostics, result.Stages, result.EvidenceComplete);
        Assert.Equal(CompilerVerdict.Pass, result.Verdict); Assert.Equal(expected, reconstructed.Data); Assert.Equal(digest, reconstructed.ResultDigest);
        Preserve(Missing().Premises); Preserve(result.Diagnostics); Preserve(result.Artifacts);
    }

    [Fact]
    public async Task WindowsPreparationRetainsTheActualCompiledResultAndEvidenceAfterCollectionAccess()
    {
        var input = Input();
        CompilerArtifactIdentity Artifact(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        var host = new LocalNetCompilerHost(new(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString())));
        var request = new CompilerHostRequest(new(input), 1); var response = await host.CompileAsync(request);
        using var view = new SyntheticWorkbenchViewModel(); view.BeginSynthetic(request); Assert.True(view.TryAccept(response));
        Assert.True(view.HasCompleteLocalPass); var result = view.Result!; var before = result.Data;
        Preserve(result.RuleOutcomes); Preserve(result.Stages); Preserve(result.Artifacts); Preserve(result.Input.Rules);
        foreach (var rule in result.RuleOutcomes) Preserve(rule.Evidence.Premises);
        Assert.True(view.HasCompleteLocalPass); Assert.Equal(before, result.Data);
        Assert.Equal(PureCoreCompiler.Compile(input, input.Compiler).Result.Data, result.Data);
    }
}
