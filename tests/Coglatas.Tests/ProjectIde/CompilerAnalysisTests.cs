using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;
using static Coglatas.Tests.ProjectIde.CompilerAnalysisFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class CompilerAnalysisTests
{
    [Fact]
    public void FullCompileProducesDeterministicArtifactsAndExecutedRuleEvidence()
    {
        var input = Input(); var first = Compile(input); var second = Compile(input);
        Assert.Equal(CompilerVerdict.Pass, first.Result.Verdict); Assert.Equal(CompilerCoverage.Complete, first.Result.Coverage);
        Assert.Equal(first.Result.ResultDigest, second.Result.ResultDigest); Assert.Equal(first.Validation!.Ir!.Digest, second.Validation!.Ir!.Digest);
        Assert.Equal(3, first.Result.Artifacts.Count); Assert.All(first.Result.RuleOutcomes, outcome => Assert.Equal(CompilerExecutionState.Completed, outcome.Execution));
        Assert.All(first.Result.RuleOutcomes, outcome => Assert.Equal(CompilerEvidenceKind.ModelProof, outcome.Evidence.Kind));
        Assert.Equal(4, first.Result.Stages.Count);
    }

    [Theory]
    [InlineData(true, CompilerVerdict.Pass, CompilerCoverage.Complete)]
    [InlineData(false, CompilerVerdict.Fail, CompilerCoverage.Complete)]
    [InlineData(null, CompilerVerdict.Inconclusive, CompilerCoverage.Partial)]
    public void RequiredConstraintTruthAndUnknownHaveDistinctResults(bool? truth, CompilerVerdict verdict, CompilerCoverage coverage)
    {
        var compiled = Compile(Constraint(truth));
        Assert.Equal(verdict, compiled.Result.Verdict); Assert.Equal(coverage, compiled.Result.Coverage);
        Assert.NotNull(compiled.Validation!.Ir);
        var outcome = Assert.Single(compiled.Result.RuleOutcomes, item => item.RuleId == CoreCompilerRules.Constraints);
        if (truth.HasValue) Assert.True(outcome.Evidence.Premises.Count >= 3);
        else Assert.Equal(CompilerEvidenceKind.Missing, outcome.Evidence.Kind);
    }

    [Fact]
    public void AdvisoryFailureIsRecordedWithoutChangingRequiredConstraintTruth()
    {
        var result = Compile(Constraint(false, advisory: true)).Result;
        Assert.Equal(CompilerVerdict.Pass, result.Verdict);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "coglatas.analysis.constraint-false" && diagnostic.Severity == CompilerDiagnosticSeverity.Warning);
    }

    [Fact]
    public void UnattachedRequiredPredicateAndMissingRuleFamilyCannotPass()
    {
        var unattached = Compile(Constraint(true, attached: false)).Result;
        Assert.Equal(CompilerVerdict.Inconclusive, unattached.Verdict);
        Assert.Contains(unattached.Diagnostics, item => item.Code == "coglatas.analysis.unattached-required-constraint");
        var omitted = Compile(Constraint(true, allRules: false)).Result;
        Assert.NotEqual(CompilerVerdict.Pass, omitted.Verdict);
        Assert.Contains(omitted.Diagnostics, item => item.Code == "coglatas.analysis.required-family-missing");
    }

    [Fact]
    public void KnownConstraintFailureSurvivesIncompleteRequirementEvidence()
    {
        var compiled = Compile(Constraint(false, requirement: true)); var result = compiled.Result;
        Assert.Equal(CompilerVerdict.Fail, result.Verdict); Assert.Equal(CompilerCoverage.Partial, result.Coverage); Assert.False(result.EvidenceComplete);
        Assert.Contains(result.Diagnostics, item => item.Code == "coglatas.analysis.constraint-false" && item.Evidence.Kind == CompilerEvidenceKind.ModelProof);
        Assert.Contains(result.Diagnostics, item => item.Code == "coglatas.analysis.requirement-evidence-unavailable");
        Assert.Equal(CompilerFreshness.Current, CompilerFreshnessAssessment.Assess(result, compiled.Input).State);
    }

    [Theory]
    [InlineData(true, CompilerVerdict.Inconclusive)]
    [InlineData(false, CompilerVerdict.Fail)]
    public void RequirementConditionsDoNotManufactureEligibleEvidence(bool truth, CompilerVerdict verdict)
    {
        var original = Source();
        var source = ProjectSource.Create(original.Context, original.Documents, Json(new JsonArray(RequirementDeclaration(original.Context, truth))));
        var result = Compile(Input(AllRulesSettings(), source)).Result;
        Assert.Equal(verdict, result.Verdict); Assert.Equal(CompilerCoverage.Partial, result.Coverage);
        Assert.Contains(result.Diagnostics, item => item.Code == "coglatas.analysis.requirement-evidence-unavailable");
    }

    [Theory]
    [InlineData("coglatas.human")]
    [InlineData("coglatas.material")]
    public void ResourceBindingsWithoutMaterializedStockOrCapacityStayUnknown(string kind)
    {
        var original = Source();
        var declaration = Obj(new { entityId = Id(7), documentId = Id(4), kind = "coglatas.resource-binding", schemaVersion = 1,
            payload = new { tenantId = original.TenantId.ToString(), resourceId = Id(20), snapshotVersion = "1", snapshotDigest = original.Digest.Data.Value, resourceKind = kind } });
        var source = ProjectSource.Create(original.Context, original.Documents, Json(new JsonArray(declaration)));
        var result = Compile(Input(AllRulesSettings(), source)).Result;
        Assert.Equal(CompilerVerdict.Inconclusive, result.Verdict); Assert.Equal(CompilerCoverage.Partial, result.Coverage);
        Assert.Contains(result.Diagnostics, item => item.Code == "coglatas.analysis.resource-snapshot-unavailable");
    }

    [Fact]
    public void UnknownRulesAreNotReportedAsExecutedChecks()
    {
        var settings = Settings(); settings["rules"]!.AsArray().Add(Obj(new { ruleId = "example.unknown", version = 1, required = true, configuration = new { } }));
        var result = Compile(Input(settings)).Result;
        var outcome = Assert.Single(result.RuleOutcomes, item => item.RuleId == "example.unknown");
        Assert.Equal(CompilerExecutionState.NotRun, outcome.Execution); Assert.Equal(CompilerVerdict.NotRun, outcome.Verdict);
        Assert.NotEqual(CompilerVerdict.Pass, result.Verdict);
    }

    [Fact]
    public void BudgetExhaustionAndCancellationAreExplicitAndNeverPass()
    {
        var settings = Settings(); settings["executionProfile"]!["maximumOperations"] = 1;
        var exhausted = Compile(Input(settings)).Result;
        Assert.Equal(CompilerExecutionState.ResourceExhausted, exhausted.Execution); Assert.NotEqual(CompilerVerdict.Pass, exhausted.Verdict);
        Assert.Contains(exhausted.RuleOutcomes, item => item.Execution == CompilerExecutionState.ResourceExhausted);
        var input = Input(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var cancelled = PureCoreCompiler.Compile(input, input.Compiler, cancellationToken: cancellation.Token);
        Assert.Equal(CompilerExecutionState.Cancelled, cancelled.Result.Execution); Assert.Null(cancelled.Validation); Assert.NotEqual(CompilerVerdict.Pass, cancelled.Result.Verdict);
    }

    [Fact]
    public void CompilerIdentitySubstitutionIsRejectedBeforeEvaluation()
    {
        var input = Input(); var different = new CompilerArtifactIdentity(Json(Artifact("coglatas.compiler", "2")));
        Assert.Throws<UnsupportedSourceException>(() => PureCoreCompiler.Compile(input, different));
    }

    [Fact]
    public void ScopedSuppressionRequestDoesNotAlterCompilerTruthOrHistoricalFinding()
    {
        var input = Constraint(false); var before = Compile(input).Result;
        var target = ProjectLocation.Create(input.Source.Documents[0].DocumentId, input.Source.Context, entityId: EntityId.Parse(Id(7)));
        var data = Obj(new { requestId = Id(50), ruleId = CoreCompilerRules.Constraints, ruleVersion = 1, target = target.Data.Value,
            reason = "Proposed policy review only", policyVersion = "1", policyDigest = input.InputDigest.Data.Value, expiresAt = (string?)null });
        _ = new CompilerSuppressionRequest(Json(data), input.Source.Context);
        var after = Compile(input).Result;
        Assert.Equal(CompilerVerdict.Fail, after.Verdict); Assert.Equal(before.ResultDigest, after.ResultDigest);
        Assert.Throws<FormatException>(() => new CompilerSuppressionRequest(Json(data), Source(9).Context));
    }
}
