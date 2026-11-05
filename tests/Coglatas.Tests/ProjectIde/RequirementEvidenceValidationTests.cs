using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;
using static Coglatas.Tests.ProjectIde.CompilerAnalysisFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class RequirementEvidenceValidationTests
{
    private static CompilerInputManifest EvidenceInput(JsonObject? settings = null, ProjectSource? original = null, string kind = "coglatas.model-proof")
    {
        original ??= Source();
        var declaration = RequirementDeclaration(original.Context); declaration["payload"]!["evidenceKind"] = kind;
        return Input(settings ?? AllRulesSettings(), ProjectSource.Create(original.Context, original.Documents, Json(new JsonArray(declaration))));
    }
    private static ProjectLocation Location(CompilerInputManifest input, int entity = 8) =>
        ProjectLocation.Create(input.Source.Documents[0].DocumentId, input.Source.Context, entityId: EntityId.Parse(Id(entity)));
    private static PlanEntityReference Target(CompilerInputManifest input, int entity = 6) => new(Json(Reference(entity, input.Source.Context)));
    private static RequirementEvidenceClaim Claim(CompilerInputManifest input, CompilerResult? result = null) => new(Location(input), Target(input), result ?? Pass(input));
    private static void Has(RequirementEvidenceAssessment result, string code) => Assert.Contains(result.Findings, item => item.Diagnostic.Code == code);
    private static void Incomplete(RequirementEvidenceAssessment result)
    {
        Assert.NotEqual(CompilerVerdict.Pass, result.Outcome.Verdict); Assert.Equal(CompilerCoverage.Partial, result.Outcome.Coverage);
        Assert.Equal(CompilerEvidenceKind.Missing, result.Outcome.Evidence.Kind);
        Has(result, "coglatas.analysis.requirement-evidence-unavailable");
    }

    [Fact]
    public void MissingEvidenceHasNoSuccessfulVerdictOrFreshness()
    {
        var input = EvidenceInput(); var result = RequirementEvidenceValidator.Assess(input, Location(input));
        Incomplete(result); Has(result, "coglatas.evidence.missing"); Assert.Empty(result.Claims);
        Assert.Equal(CompilerFreshness.Undetermined, result.Freshness);
    }

    [Fact]
    public void FabricatedCompleteResultIsOnlyAClaimWithoutMaterialization()
    {
        var input = EvidenceInput(); var claim = Claim(input); var before = claim.Result.Data.CanonicalText;
        var result = RequirementEvidenceValidator.Assess(input, Location(input), [claim]);
        Incomplete(result); Assert.Same(claim, Assert.Single(result.Claims));
        Assert.Equal(CompilerFreshness.Current, result.Freshness); Assert.Single(result.Findings);
        Assert.Equal(before, claim.Result.Data.CanonicalText);
        Assert.Contains(Compile(input).Result.Diagnostics, item => item.Code == "coglatas.analysis.requirement-evidence-unavailable");
    }

    [Theory]
    [InlineData("target", "coglatas.evidence.target-mismatch")]
    [InlineData("requirement", "coglatas.evidence.requirement-identity-mismatch")]
    [InlineData("revision", "coglatas.evidence.input-mismatch")]
    [InlineData("settings", "coglatas.evidence.input-mismatch")]
    [InlineData("manifest", "coglatas.evidence.input-mismatch")]
    [InlineData("tenant", "coglatas.evidence.input-mismatch")]
    [InlineData("project", "coglatas.evidence.input-mismatch")]
    [InlineData("branch", "coglatas.evidence.input-mismatch")]
    public void ExactTargetScopeRevisionAndManifestCannotBeSubstituted(string dimension, string code)
    {
        var input = EvidenceInput(); var other = input;
        if (dimension == "revision") other = EvidenceInput(original: Source(9));
        if (dimension is "settings" or "manifest")
        {
            var settings = AllRulesSettings();
            if (dimension == "settings") settings["executionProfile"]!["maximumOperations"] = 199999;
            else settings["compiler"]!["digest"]!["value"] = new string('b', 64);
            other = EvidenceInput(settings);
        }
        if (dimension is "tenant" or "project" or "branch")
        {
            var context = SourceRevisionContext.Committed(new(new(TenantId.Parse(Id(dimension == "tenant" ? 11 : 1)),
                ProjectId.Parse(Id(dimension == "project" ? 12 : 2)), BranchId.Parse(Id(dimension == "branch" ? 13 : 3))), RevisionId.Parse(Id(5))));
            other = EvidenceInput(original: ProjectSource.Create(context, input.Source.Documents));
        }
        var claim = new RequirementEvidenceClaim(dimension == "requirement" ? Location(input, 6) : Location(input),
            Target(input, dimension == "target" ? 9 : 6), Pass(other));
        var result = RequirementEvidenceValidator.Assess(input, Location(input), [claim]);
        Has(result, code); Incomplete(result);
        if (dimension is not ("target" or "requirement")) Assert.Equal(CompilerFreshness.Stale, result.Freshness);
    }

    [Theory]
    [InlineData("document")]
    [InlineData("entity")]
    [InlineData("path")]
    [InlineData("span")]
    [InlineData("context")]
    public void RequestMustIdentifyTheExactRequirementDeclaration(string dimension)
    {
        var input = EvidenceInput();
        var location = ProjectLocation.Create(DocumentId.Parse(Id(dimension == "document" ? 99 : 4)),
            dimension == "context" ? Source(9).Context : input.Source.Context, dimension == "path" ? "/payload/condition" : "",
            entityId: EntityId.Parse(Id(dimension == "entity" ? 6 : 8)), textSpan: dimension == "span" ? new(0, 1) : null);
        var result = RequirementEvidenceValidator.Assess(input, location, [Claim(input)]);
        Incomplete(result); Assert.Empty(result.Claims);
        Assert.Contains(result.Findings, item => item.State == RelationFindingState.Invalid);
        Assert.All(result.Findings, item => Assert.Equal(input.Source.Context, item.Diagnostic.Location.Context));
    }

    [Fact]
    public void DuplicateAndCompetingClaimsAreDiagnosedWithoutChoosingTheFirst()
    {
        var input = EvidenceInput(); var claim = Claim(input);
        var duplicate = RequirementEvidenceValidator.Assess(input, Location(input), [claim, claim]);
        Has(duplicate, "coglatas.evidence.duplicate"); Incomplete(duplicate);
        var competing = Claim(input, Compile(input).Result);
        var first = RequirementEvidenceValidator.Assess(input, Location(input), [claim, competing]);
        var second = RequirementEvidenceValidator.Assess(input, Location(input), [competing, claim]);
        Has(first, "coglatas.evidence.ambiguous"); Incomplete(first);
        Assert.Equal(first.Findings.Select(item => item.Diagnostic.Data.CanonicalText), second.Findings.Select(item => item.Diagnostic.Data.CanonicalText));
        Assert.Equal(first.Claims.Select(item => item.Result.ResultId), second.Claims.Select(item => item.Result.ResultId));
    }

    [Theory]
    [InlineData("coglatas.human-approval", CompilerEvidenceKind.HumanApproval)]
    [InlineData("coglatas.heuristic", CompilerEvidenceKind.Heuristic)]
    public void ApprovalAndHeuristicCannotBecomeTechnicalProof(string kind, CompilerEvidenceKind basisKind)
    {
        var input = EvidenceInput(kind: kind);
        var rules = input.Rules.Select(rule => new CompilerRuleOutcome(rule.RuleId, rule.Version, CompilerExecutionState.Completed,
            CompilerCoverage.Complete, CompilerVerdict.Inconclusive, new(basisKind, [input.InputDigest, input.Source.Digest])));
        var candidate = new CompilerResult(input, CompilerExecutionState.Completed, CompilerCoverage.Complete, rules, [], [Stage(input)], true);
        var result = RequirementEvidenceValidator.Assess(input, Location(input), [Claim(input, candidate)]);
        Has(result, "coglatas.evidence.technical-proof-unavailable"); Incomplete(result);
    }

    [Fact]
    public void EvidenceKindAndPremiseDigestsMustMatchTheDeclaredClaim()
    {
        var input = EvidenceInput(kind: "coglatas.observation");
        var result = RequirementEvidenceValidator.Assess(input, Location(input), [Claim(input)]);
        Has(result, "coglatas.evidence.kind-mismatch"); Incomplete(result);
        var outcomes = input.Rules.Select(rule => new CompilerRuleOutcome(rule.RuleId, rule.Version, CompilerExecutionState.Completed,
            CompilerCoverage.Complete, CompilerVerdict.Pass, new(CompilerEvidenceKind.ModelProof, [Source(9).Digest])));
        var wrong = new CompilerResult(input, CompilerExecutionState.Completed, CompilerCoverage.Complete, outcomes, [], [Stage(input)], true);
        Has(RequirementEvidenceValidator.Assess(input, Location(input), [Claim(input, wrong)]), "coglatas.evidence.premise-mismatch");
    }

    [Fact]
    public void UnknownEvidenceKindRemainsPreservedUnsupportedSource()
    {
        var input = EvidenceInput(kind: "example.future-evidence"); var bytes = input.Source.Data.CanonicalText;
        var result = RequirementEvidenceValidator.Assess(input, Location(input), [Claim(input)]);
        Has(result, "coglatas.evidence.requirement-unsupported"); Incomplete(result); Assert.Empty(result.Claims);
        Assert.Equal(bytes, result.Input.Source.Data.CanonicalText);
    }

    [Theory]
    [InlineData(CompilerExecutionState.Completed)]
    [InlineData(CompilerExecutionState.Cancelled)]
    [InlineData(CompilerExecutionState.TimedOut)]
    [InlineData(CompilerExecutionState.ResourceExhausted)]
    public void PartialOrUnexecutedEvidenceCannotDischargeARequirement(CompilerExecutionState execution)
    {
        var input = EvidenceInput();
        var outcomes = input.Rules.Select(rule => new CompilerRuleOutcome(rule.RuleId, rule.Version, execution,
            CompilerCoverage.Partial, CompilerVerdict.Inconclusive, Missing()));
        var candidate = new CompilerResult(input, execution, CompilerCoverage.Partial, outcomes, [], [], false);
        var result = RequirementEvidenceValidator.Assess(input, Location(input), [Claim(input, candidate)]);
        Has(result, "coglatas.evidence.coverage-incomplete"); Has(result, "coglatas.evidence.basis-unavailable"); Incomplete(result);
        if (execution != CompilerExecutionState.Completed) Has(result, "coglatas.evidence.execution-incomplete");
    }

    [Fact]
    public void CancellationAndLogicalExhaustionAreExplicit()
    {
        var input = EvidenceInput(); using var cts = new CancellationTokenSource(); cts.Cancel();
        var cancelled = RequirementEvidenceValidator.Assess(input, Location(input), [Claim(input)], cts.Token);
        Assert.Equal(CompilerExecutionState.Cancelled, cancelled.Outcome.Execution); Incomplete(cancelled);
        var settings = AllRulesSettings(); settings["executionProfile"]!["maximumOperations"] = 1; input = EvidenceInput(settings);
        var exhausted = RequirementEvidenceValidator.Assess(input, Location(input), [Claim(input)]);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, exhausted.Outcome.Execution); Incomplete(exhausted);
    }

    [Fact]
    public void EnumerationIsBoundedAndChecksCancellationBetweenClaims()
    {
        var input = EvidenceInput();
        using var cts = new CancellationTokenSource();
        static IEnumerable<RequirementEvidenceClaim?> CancelBetweenClaims(CompilerInputManifest input, Action cancel)
        {
            yield return Claim(input);
            cancel();
            yield return Claim(input);
        }

        var cancelled = RequirementEvidenceValidator.Assess(input, Location(input), CancelBetweenClaims(input, cts.Cancel), cts.Token);
        Assert.Equal(CompilerExecutionState.Cancelled, cancelled.Outcome.Execution); Assert.Single(cancelled.Claims); Incomplete(cancelled);
        var count = 0;
        IEnumerable<RequirementEvidenceClaim?> Nulls()
        { while (true) { if (++count > 1025) throw new InvalidOperationException("Unbounded evidence enumeration."); yield return null; } }
        var exhausted = RequirementEvidenceValidator.Assess(input, Location(input), Nulls());
        Assert.Equal(CompilerExecutionState.ResourceExhausted, exhausted.Outcome.Execution); Assert.Equal(1025, count); Incomplete(exhausted);
    }

    [Fact]
    public void PinnedEvidenceAndReceiptShapedMetadataCannotImpersonateActualMaterialization()
    {
        var settings = AllRulesSettings(); var original = Source();
        settings["evidence"]!.AsArray().Add(Obj(new { snapshotId = Id(50), version = "1", digest = original.Digest.Data.Value,
            context = original.Context.Data.Value, coverage = "coglatas.complete", asOf = (string?)null,
            receipt = new { verified = true, authorized = true } }));
        Assert.Throws<UnsupportedSourceException>(() => EvidenceInput(settings));
        settings["evidence"]![0]!.AsObject().Remove("receipt");
        var input = EvidenceInput(settings);
        Incomplete(RequirementEvidenceValidator.Assess(input, Location(input), [Claim(input)]));
        Assert.Contains(Compile(input).Result.Diagnostics, item => item.Code == "coglatas.analysis.requirement-evidence-unavailable");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CandidateAndScenarioStampsAreExactEvidenceInputs(bool scenario)
    {
        var original = Source();
        CompilerInputManifest ContextInput(int stamp)
        {
            var settings = AllRulesSettings();
            var context = scenario ? SourceRevisionContext.Hypothetical(new(ScenarioId.Parse(Id(30)), ScenarioRevisionId.Parse(Id(stamp)),
                original.Context, original.Digest)) : SourceRevisionContext.Candidate(new(ProposalId.Parse(Id(30)), CandidateRevisionId.Parse(Id(stamp)),
                original.Context.CommittedRevision!, original.Context.CommittedRevision!));
            if (scenario) settings["scenario"] = Obj(new { baseContext = original.Context.Data.Value, overlayDigest = original.Digest.Data.Value, assumptions = Array.Empty<object>() });
            return EvidenceInput(settings, ProjectSource.Create(context, original.Documents));
        }
        var desired = ContextInput(31); var old = ContextInput(32);
        var claim = new RequirementEvidenceClaim(Location(desired), Target(desired), Pass(old));
        var result = RequirementEvidenceValidator.Assess(desired, Location(desired), [claim]);
        Has(result, "coglatas.evidence.input-mismatch"); Assert.Equal(CompilerFreshness.Stale, result.Freshness); Incomplete(result);
    }

    [Theory]
    [InlineData("missing", "coglatas.evidence.target-unavailable")]
    [InlineData("unsupported", "coglatas.evidence.target-unavailable")]
    [InlineData("resource", "coglatas.evidence.resource-materialization-unavailable")]
    public void ExactClaimCannotHideMissingUnsupportedOrResourceTargetBodies(string kind, string code)
    {
        var original = Source();
        var target = kind == "resource" ? Obj(new { entityId = Id(7), documentId = Id(4), kind = "coglatas.resource-binding", schemaVersion = 1,
            payload = new { tenantId = Id(1), resourceId = Id(20), snapshotVersion = "1", snapshotDigest = original.Digest.Data.Value, resourceKind = "coglatas.material" } })
            : Obj(new { entityId = Id(7), documentId = Id(4), kind = "example.future", schemaVersion = 1, payload = new { } });
        var requirement = RequirementDeclaration(original.Context); requirement["payload"]!["target"] = Reference(7, original.Context);
        var declarations = new JsonArray(requirement);
        if (kind != "missing") declarations.Add(target);
        var input = Input(AllRulesSettings(), ProjectSource.Create(original.Context, original.Documents, Json(declarations)));
        var claim = new RequirementEvidenceClaim(Location(input), Target(input, 7), Pass(input));
        var result = RequirementEvidenceValidator.Assess(input, Location(input), [claim]);
        Has(result, code); Incomplete(result);
    }

    [Fact]
    public void ClaimLocatorPayloadConsumesBudgetBeforeSortingOrPublication()
    {
        var settings = AllRulesSettings(); settings["executionProfile"]!["maximumOperations"] = 1000;
        var input = EvidenceInput(settings); var data = Obj(Location(input).Data.Value);
        data["example.untrusted"] = new JsonArray(Enumerable.Range(0, 2000).Select(value => JsonValue.Create(value)).ToArray<JsonNode?>());
        var claim = new RequirementEvidenceClaim(ProjectLocation.Parse(Json(data)), Target(input), Pass(input));
        var result = RequirementEvidenceValidator.Assess(input, Location(input), [claim]);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, result.Outcome.Execution);
        Assert.Equal(CompilerFreshness.Undetermined, result.Freshness); Incomplete(result);
        Assert.Empty(result.Claims);
    }
}
