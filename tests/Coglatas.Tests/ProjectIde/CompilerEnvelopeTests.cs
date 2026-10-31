using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class CompilerEnvelopeTests
{
    public static IEnumerable<object[]> RequiredFields() => Settings().Select(item => new object[] { item.Key });

    [Theory]
    [MemberData(nameof(RequiredFields))]
    public void EveryApplicableManifestDimensionMustBeExplicit(string field)
    {
        var settings = Settings(); settings.Remove(field);
        Assert.Throws<FormatException>(() => Input(settings));
    }

    [Fact]
    public void ExactInputEnvelopeRoundTripsAndRejectsForgedIdentity()
    {
        var input = Input(); var roundTrip = CompilerInputManifest.ReadEnvelope(input.Source, input.ToEnvelope());
        Assert.Equal(input.InputId, roundTrip.InputId); Assert.Equal(input.InputDigest, roundTrip.InputDigest);
        Assert.Equal(8, input.InputId.Value.Version); Assert.Equal(0x80, input.InputId.Value.ToByteArray(true)[8] & 0xc0);
        foreach (var field in new[] { "compilerInputId", "inputDigest" })
        {
            var envelope = JsonNode.Parse(input.ToEnvelope().CanonicalText)!;
            if (field == "compilerInputId") envelope[field] = Id(12);
            else envelope[field]!["value"] = new string('b', 64);
            Assert.Throws<FormatException>(() => CompilerInputManifest.ReadEnvelope(input.Source, Json(envelope)));
        }
        Assert.Throws<FormatException>(() => CompilerInputManifest.ReadEnvelope(Source(9), input.ToEnvelope()));
    }

    [Theory]
    [InlineData("compiler")]
    [InlineData("schema")]
    [InlineData("ruleset")]
    [InlineData("semantics")]
    [InlineData("ir")]
    [InlineData("evaluator")]
    public void ImplementationAndContractVersionsChangeInputIdentity(string field)
    {
        var settings = Settings(); settings[field]!["version"] = "2";
        Assert.NotEqual(Input().InputDigest, Input(settings).InputDigest);
    }

    [Theory]
    [InlineData("maximumOperations")]
    [InlineData("maximumDeclarations")]
    [InlineData("maximumRelations")]
    [InlineData("maximumDigits")]
    [InlineData("maximumScale")]
    public void LogicalBudgetChangesInvalidateResultReuse(string field)
    {
        var settings = Settings(); settings["executionProfile"]![field] = 42;
        Assert.NotEqual(Input().InputId, Input(settings).InputId);
    }

    [Fact]
    public void UnorderedLocksNormalizeAndDuplicateIdentitiesFail()
    {
        var settings = Settings(); var rules = settings["rules"]!.AsArray().Select(item => item!.DeepClone()).Reverse().ToArray();
        settings["rules"] = new JsonArray(rules);
        Assert.Equal(Input().InputDigest, Input(settings).InputDigest);
        settings["rules"]!.AsArray().Add(settings["rules"]![0]!.DeepClone());
        Assert.Throws<FormatException>(() => Input(settings));
        settings = Settings(); settings["example.future"] = true;
        Assert.Throws<UnsupportedSourceException>(() => Input(settings));
    }

    [Fact]
    public void SnapshotScopeCoverageAndAsOfAreImmutableInputDimensions()
    {
        var settings = Settings();
        var snapshot = Obj(new { snapshotId = Id(20), version = "1", digest = Input().Source.Digest.Data.Value,
            context = Source().Context.Data.Value, coverage = "coglatas.partial", asOf = "2026-11-01T00:00:00.0000000Z" });
        settings["authorizedExternalSnapshots"] = new JsonArray(snapshot);
        var first = Input(settings); snapshot["coverage"] = "coglatas.complete";
        Assert.NotEqual(first.InputDigest, Input(settings).InputDigest);
        snapshot["context"]!["branch"]!["tenantId"] = Id(21);
        Assert.Throws<FormatException>(() => Input(settings));
    }

    [Fact]
    public void CompletePassCanBeStaleWithoutMutatingHistoricalResult()
    {
        var input = Input(); var result = Pass(input); var bytes = result.Data.ToCanonicalBytes();
        Assert.Equal(CompilerVerdict.Pass, result.Verdict); Assert.Equal(CompilerCoverage.Complete, result.Coverage);
        Assert.Equal(CompilerFreshness.Current, CompilerFreshnessAssessment.Assess(result, input).State);
        var stale = CompilerFreshnessAssessment.Assess(result, Input(source: Source(9)));
        Assert.Equal(CompilerFreshness.Stale, stale.State); Assert.Contains("sourceContext", stale.ChangedDimensions);
        Assert.Equal(CompilerFreshness.Undetermined, CompilerFreshnessAssessment.Assess(result, null).State);
        Assert.Equal(bytes, result.Data.ToCanonicalBytes()); Assert.Equal(result.ResultDigest, ContentDigest.Compute("coglatas.compiler-result/1", bytes));
    }

    [Fact]
    public void PartialFailCanBeCurrentAndRetainsUnknownRequiredEvaluations()
    {
        var input = Input();
        var rules = new[]
        {
            new CompilerRuleOutcome(SchemaRule, 1, CompilerExecutionState.Completed, CompilerCoverage.Complete, CompilerVerdict.Fail, Proof(input)),
            new CompilerRuleOutcome(RelationRule, 1, CompilerExecutionState.Completed, CompilerCoverage.Partial, CompilerVerdict.Inconclusive, Missing())
        };
        var result = new CompilerResult(input, CompilerExecutionState.Completed, CompilerCoverage.Partial, rules, [], [Stage(input)], false);
        Assert.Equal(CompilerVerdict.Fail, result.Verdict); Assert.Equal(CompilerCoverage.Partial, result.Coverage);
        Assert.Equal(CompilerFreshness.Current, CompilerFreshnessAssessment.Assess(result, input).State);
        Assert.False(result.EvidenceComplete);
    }

    [Theory]
    [InlineData(CompilerExecutionState.Cancelled)]
    [InlineData(CompilerExecutionState.TimedOut)]
    [InlineData(CompilerExecutionState.ResourceExhausted)]
    [InlineData(CompilerExecutionState.InternalError)]
    public void InterruptedExecutionCannotBePass(CompilerExecutionState execution)
    {
        var input = Input(); var result = new CompilerResult(input, execution, CompilerCoverage.Complete, Passes(input), [], [Stage(input)], true);
        Assert.NotEqual(CompilerVerdict.Pass, result.Verdict);
    }

    [Theory]
    [InlineData(CompilerEvidenceKind.Heuristic)]
    [InlineData(CompilerEvidenceKind.HumanApproval)]
    [InlineData(CompilerEvidenceKind.Missing)]
    public void IneligibleEvidenceCannotBecomeTechnicalPass(CompilerEvidenceKind kind)
    {
        var input = Input(); var evidence = kind == CompilerEvidenceKind.Missing ? Missing() : new CompilerEvidenceBasis(kind, [input.InputDigest]);
        Assert.Throws<ArgumentException>(() => new CompilerRuleOutcome(SchemaRule, 1, CompilerExecutionState.Completed, CompilerCoverage.Complete, CompilerVerdict.Pass, evidence));
    }

    [Fact]
    public void ZeroRulesMissingStagesAndCrossInputResultsCannotClaimComplete()
    {
        var input = Input();
        Assert.Throws<ArgumentException>(() => new CompilerResult(input, CompilerExecutionState.Completed, CompilerCoverage.Complete, [], [], [Stage(input)], true));
        Assert.Throws<ArgumentException>(() => new CompilerResult(input, CompilerExecutionState.Completed, CompilerCoverage.Complete, Passes(input), [], [], true));
        Assert.Throws<ArgumentException>(() => new CompilerResult(input, CompilerExecutionState.Completed, CompilerCoverage.Complete, Passes(input), [], [Stage(Input(source: Source(9)))], true));
        var empty = new CompilerResult(input, CompilerExecutionState.NotRun, CompilerCoverage.None, [], [], [], false);
        Assert.Equal(CompilerVerdict.NotRun, empty.Verdict);
    }

    [Fact]
    public void DiagnosticsBindInputAndStableTargetsWithDeterministicOrdering()
    {
        var input = Input(); var location = ProjectLocation.Create(input.Source.Documents[0].DocumentId, input.Source.Context);
        var first = new CompilerDiagnostic(input, SchemaRule, 1, 0, "coglatas.a", location, Proof(input), CompilerDiagnosticSeverity.Warning);
        var second = new CompilerDiagnostic(input, SchemaRule, 1, 0, "coglatas.b", location, Proof(input));
        var a = new CompilerResult(input, CompilerExecutionState.Completed, CompilerCoverage.Complete, Passes(input), [second, first], [Stage(input)], true);
        var b = new CompilerResult(input, CompilerExecutionState.Completed, CompilerCoverage.Complete, Passes(input).Reverse(), [first, second], [Stage(input)], true);
        Assert.Equal(a.ResultDigest, b.ResultDigest); Assert.Equal(first.DiagnosticId, a.Diagnostics[0].DiagnosticId);
        var next = Input(source: Source(9)); var nextLocation = ProjectLocation.Create(next.Source.Documents[0].DocumentId, next.Source.Context);
        var revised = new CompilerDiagnostic(next, SchemaRule, 1, 0, "coglatas.a", nextLocation, Proof(next));
        Assert.NotEqual(first.DiagnosticId, revised.DiagnosticId); Assert.Equal(first.FindingKey, revised.FindingKey);
        Assert.Throws<ArgumentException>(() => new CompilerDiagnostic(input, SchemaRule, 1, 0, "coglatas.a", nextLocation, Proof(input)));
        Assert.Throws<ArgumentException>(() => new CompilerResult(input, CompilerExecutionState.Completed, CompilerCoverage.Partial, [], [first, first], [], false));
    }
}
