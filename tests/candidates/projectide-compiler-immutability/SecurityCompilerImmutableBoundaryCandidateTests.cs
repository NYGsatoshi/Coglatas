using System.Collections;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

/// <summary>
/// Independent Security-branch acceptance candidates for #1938.
/// In-process Core and pure ViewModel checks only; not authorization or Native UI admission.
/// </summary>
public sealed class SecurityCompilerImmutableBoundaryCandidateTests
{
    private static void AssertSealed<T>(IReadOnlyList<T> values)
    {
        var snapshot = values.ToArray();
        Assert.False(values is T[], "A public collection must not expose its owned array.");
        if (values is ICollection legacy)
            Assert.False(legacy.SyncRoot is Array, "A legacy synchronization root must not expose mutable storage.");
        if (values is IList<T> editable)
        {
            Assert.Throws<NotSupportedException>(() => editable.Add(default!));
            Assert.Throws<NotSupportedException>(() => editable.Clear());
            if (editable.Count != 0)
                Assert.Throws<NotSupportedException>(() => editable[0] = default!);
        }
        Assert.Equal(snapshot, values.ToArray());
    }

    [Fact]
    public void LockedManifestRejectsPublicBackingMutationAndRetainsCompilerIdentity()
    {
        var settings = Settings();
        var input = Input(settings);
        var digest = input.InputDigest;
        var original = PureCoreCompiler.Compile(input, input.Compiler).Result.Data;
        AssertSealed(input.Rules);

        // The caller still owns the JSON used to construct the admitted input.
        foreach (var item in settings["rules"]!.AsArray())
            item!["required"] = false;
        Assert.Equal(digest, input.InputDigest);
        Assert.Equal(original, PureCoreCompiler.Compile(input, input.Compiler).Result.Data);
        Assert.Equal(input.Rules.Count, input.Rules.Count(rule => rule.Required));
    }

    [Fact]
    public void EvidencePremisesPreserveCanonicalBasisAgainstSyncRootAndCallerArrayMutation()
    {
        var input = Input();
        ContentDigest[] caller = [input.InputDigest, input.Source.Digest];
        var basis = new CompilerEvidenceBasis(CompilerEvidenceKind.ModelProof, caller);
        var saved = basis.Data.ToCanonicalBytes();
        AssertSealed(basis.Premises);
        caller[0] = ContentDigest.Compute("example.independent-replacement", [1, 2, 3]);
        Assert.Equal(saved, basis.Data.ToCanonicalBytes());
        Assert.Equal(basis.Data, new CompilerEvidenceBasis(basis.Kind, basis.Premises).Data);
        AssertSealed(Missing().Premises);
    }

    [Fact]
    public void TypedRuleOutcomeCannotChangeRetainedVerdictOrResultDigest()
    {
        var input = Input();
        var caller = Passes(input);
        var stages = new[] { Stage(input) };
        var result = new CompilerResult(input, CompilerExecutionState.Completed,
            CompilerCoverage.Complete, caller, [], stages, true);
        var before = result.Data;
        var digest = result.ResultDigest;
        Assert.Equal(CompilerVerdict.Pass, result.Verdict);
        AssertSealed(result.RuleOutcomes);

        var rule = caller[0];
        caller[0] = new CompilerRuleOutcome(rule.RuleId, rule.Version,
            CompilerExecutionState.Completed, CompilerCoverage.Complete, CompilerVerdict.Fail, rule.Evidence);
        stages[0] = new CompilerStageOutcome("example.changed-stage", 1,
            CompilerExecutionState.Completed, CompilerCoverage.Complete, input.InputDigest, []);
        var rebuilt = new CompilerResult(input, result.Execution, result.Coverage,
            result.RuleOutcomes, result.Diagnostics, result.Stages, result.EvidenceComplete, result.Artifacts);
        Assert.Equal(CompilerVerdict.Pass, rebuilt.Verdict);
        Assert.Equal(CompilerVerdict.Pass, result.Verdict);
        Assert.Equal(digest, result.ResultDigest);
        Assert.Equal(digest, rebuilt.ResultDigest);
        Assert.Equal(before, rebuilt.Data);
    }

    [Fact]
    public void ActualDiagnosticAndArtifactViewsCannotExposeWritableStorage()
    {
        var input = CompilerAnalysisFixtures.Constraint(false);
        var failing = PureCoreCompiler.Compile(input, input.Compiler).Result;
        Assert.NotEmpty(failing.Diagnostics);
        var diagnosticBytes = failing.Data.ToCanonicalBytes();
        AssertSealed(failing.Diagnostics);
        Assert.Equal(diagnosticBytes, failing.Data.ToCanonicalBytes());

        var ordinary = Input();
        var result = PureCoreCompiler.Compile(ordinary, ordinary.Compiler).Result;
        Assert.NotEmpty(result.Artifacts);
        AssertSealed(result.Artifacts);
        Assert.Equal(result.ResultDigest,
            ContentDigest.Compute("coglatas.compiler-result/1", result.Data.ToCanonicalBytes()));
    }

    [Fact]
    public void RealCompilationStagesAndDerivedIrNodesAreIndependentOfPublicCollections()
    {
        var input = Input();
        var compilation = PureCoreCompiler.Compile(input, input.Compiler);
        var before = compilation.Result.Data;
        Assert.NotEmpty(compilation.Result.Stages);
        AssertSealed(compilation.Result.Stages);
        Assert.NotNull(compilation.Validation);
        Assert.NotNull(compilation.Validation!.Ir);
        AssertSealed(compilation.Validation.Ir!.Nodes);
        Assert.Equal(before, PureCoreCompiler.Compile(input, input.Compiler).Result.Data);
    }

    [Fact]
    public void CurrentStaleAndAbsentFreshnessViewsRemainImmutable()
    {
        var input = Input();
        var result = Pass(input);
        var current = CompilerFreshnessAssessment.Assess(result, input);
        Assert.Equal(CompilerFreshness.Current, current.State);
        Assert.Empty(current.ChangedDimensions);
        AssertSealed(current.ChangedDimensions);

        var stale = CompilerFreshnessAssessment.Assess(result, Input(source: Source(9)));
        Assert.Equal(CompilerFreshness.Stale, stale.State);
        Assert.NotEmpty(stale.ChangedDimensions);
        AssertSealed(stale.ChangedDimensions);

        var unavailable = CompilerFreshnessAssessment.Assess(result, null);
        Assert.Equal(CompilerFreshness.Undetermined, unavailable.State);
        AssertSealed(unavailable.ChangedDimensions);
    }

    [Fact]
    public void RealHostProfileCannotLoseCompatibilityThroughPublicCollectionAccess()
    {
        var input = Input();
        CompilerArtifactIdentity Artifact(string field) =>
            new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Artifact("schema"),
            Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString()));
        Assert.True(profile.Supports(input));
        AssertSealed(profile.Capabilities);
        var contracts = profile.Contracts.ToDictionary(item => item.Key, item => item.Value);
        if (profile.Contracts is IDictionary<string, CompilerArtifactIdentity> map)
            Assert.Throws<NotSupportedException>(() => map.Clear());
        Assert.Equal(contracts.Count, profile.Contracts.Count);
        foreach (var pair in contracts)
            Assert.Equal(pair.Value, profile.Contracts[pair.Key]);
        Assert.True(profile.Supports(input));
    }

    [Fact]
    public async Task WindowsPurePreparationRetainsRealCompiledResultAfterMutationProbes()
    {
        var input = Input();
        CompilerArtifactIdentity Artifact(string field) =>
            new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Artifact("schema"),
            Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString()));
        var host = new LocalNetCompilerHost(profile);
        var request = new CompilerHostRequest(new(input), 1);
        var response = await host.CompileAsync(request);
        using var view = new SyntheticWorkbenchViewModel();
        view.BeginSynthetic(request);
        Assert.True(view.TryAccept(response));
        Assert.True(view.HasCompleteLocalPass);
        var result = view.Result!;
        var before = result.Data;
        AssertSealed(result.Input.Rules);
        AssertSealed(result.RuleOutcomes);
        AssertSealed(result.Stages);
        AssertSealed(result.Artifacts);
        foreach (var rule in result.RuleOutcomes)
            AssertSealed(rule.Evidence.Premises);
        Assert.True(view.HasCompleteLocalPass);
        Assert.Equal(before, view.Result!.Data);
        Assert.Equal(PureCoreCompiler.Compile(input, input.Compiler).Result.Data, result.Data);
    }

    [Fact]
    public void EmptyResultAndMissingEvidenceCannotExposeMutableStorage()
    {
        var input = Input();
        var result = new CompilerResult(input, CompilerExecutionState.NotRun,
            CompilerCoverage.None, [], [], [], false);
        Assert.Equal(CompilerVerdict.NotRun, result.Verdict);
        AssertSealed(result.RuleOutcomes);
        AssertSealed(result.Diagnostics);
        AssertSealed(result.Stages);
        AssertSealed(result.Artifacts);
        AssertSealed(Missing().Premises);
        Assert.Equal(result.ResultDigest,
            ContentDigest.Compute("coglatas.compiler-result/1", result.Data.ToCanonicalBytes()));
    }
}
