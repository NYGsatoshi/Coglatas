using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

internal static class CompilerContractFixtures
{
    internal const string SchemaRule = "coglatas.core.schema";
    internal const string DeclarationRule = "coglatas.core.declarations";
    internal const string RelationRule = "coglatas.core.relations";
    internal static string Id(int number) => new Guid(number, 0, 0x4000, 0x80, 0, 0, 0, 0, 0, 0, 1).ToString();
    internal static JsonObject Obj(object data) => JsonSerializer.SerializeToNode(data)!.AsObject();
    internal static SourceJson Json(JsonNode data) => SourceJson.Parse(data.ToJsonString());
    internal static ProjectSource Source(int revision = 5)
    {
        var context = SourceRevisionContext.Committed(new(new(TenantId.Parse(Id(1)), ProjectId.Parse(Id(2)), BranchId.Parse(Id(3))), RevisionId.Parse(Id(revision))));
        return ProjectSource.Create(context, [SourceDocument.Create(DocumentId.Parse(Id(4)), "project", Json(Obj(new { entityId = Id(6), kind = "coglatas.project", schemaVersion = 1, payload = new { } })))]);
    }
    internal static JsonObject Artifact(string id, string version = "1") => Obj(new { artifactId = id, version, digest = new { algorithm = "sha-256", value = new string('a', 64) } });
    internal static JsonObject Settings()
    {
        var settings = new JsonObject();
        foreach (var name in new[] { "schema", "compiler", "ruleset", "semantics", "ir", "evaluator" }) settings[name] = Artifact("coglatas." + name);
        foreach (var name in new[] { "scheduler", "solver", "stochastic", "importer", "scenario" }) settings[name] = Obj(new { kind = "coglatas.none" });
        foreach (var name in new[] { "authorizedExternalSnapshots", "resourceSnapshots", "allocationSnapshots", "observations", "evidence", "calendars", "timeZones" }) settings[name] = new JsonArray();
        settings["descriptors"] = JsonSerializer.SerializeToNode(RelationKindDescriptor.BuiltIns.Select(item => new { artifactId = item.DescriptorId, version = "1", digest = item.Digest.Data.Value }));
        settings["rules"] = JsonSerializer.SerializeToNode(new[] { SchemaRule, DeclarationRule, RelationRule }.Select(ruleId => new { ruleId, version = 1, required = true, configuration = new { } }));
        settings["featureSet"] = new JsonArray("coglatas.core.structural");
        settings["targets"] = new JsonArray("coglatas.semantic-model");
        settings["analysisScope"] = new JsonArray("coglatas.source-structure");
        settings["sourceCoverage"] = "coglatas.complete";
        settings["clock"] = Obj(new { kind = "coglatas.not-applicable" });
        settings["executionProfile"] = Obj(new { purpose = "coglatas.normal", maximumOperations = 200000, maximumDeclarations = 10000,
            maximumRelations = 20000, maximumDigits = 256, maximumScale = 256, rounding = "coglatas.ties-to-even" });
        return settings;
    }
    internal static CompilerInputManifest Input(JsonObject? settings = null, ProjectSource? source = null) => new(source ?? Source(), Json(settings ?? Settings()));
    internal static CompilerEvidenceBasis Proof(CompilerInputManifest input) => new(CompilerEvidenceKind.ModelProof, [input.InputDigest, input.Source.Digest]);
    internal static CompilerEvidenceBasis Missing() => new(CompilerEvidenceKind.Missing, [], "coglatas.not-evaluated");
    internal static CompilerRuleOutcome[] Passes(CompilerInputManifest input) => input.Rules.Select(rule => new CompilerRuleOutcome(rule.RuleId, rule.Version,
        CompilerExecutionState.Completed, CompilerCoverage.Complete, CompilerVerdict.Pass, Proof(input))).ToArray();
    internal static CompilerStageOutcome Stage(CompilerInputManifest input) => new("coglatas.validation", 1, CompilerExecutionState.Completed, CompilerCoverage.Complete, input.InputDigest, [input.Source.Digest]);
    internal static CompilerResult Pass(CompilerInputManifest input) => new(input, CompilerExecutionState.Completed, CompilerCoverage.Complete, Passes(input), [], [Stage(input)], true);
}
