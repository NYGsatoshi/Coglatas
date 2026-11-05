using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

internal static class CompilerAnalysisFixtures
{
    internal static JsonObject AllRulesSettings()
    {
        var settings = Settings(); var rules = settings["rules"]!.AsArray();
        foreach (var ruleId in new[] { CoreCompilerRules.Constraints, CoreCompilerRules.Requirements, CoreCompilerRules.Resources })
            rules.Add(Obj(new { ruleId, version = 1, required = true, configuration = new { } }));
        return settings;
    }
    internal static JsonObject Reference(int entity, SourceRevisionContext context) => Obj(new
    { tenantId = context.Branch.TenantId.ToString(), projectId = context.Branch.ProjectId.ToString(), entityId = Id(entity), context = context.Data.Value });
    internal static JsonObject Predicate(bool? truth) => Obj(PlanDeclarationTests.Literal(truth));
    internal static CompilerInputManifest Constraint(bool? truth, bool advisory = false, bool attached = true, bool requirement = false, bool allRules = true)
    {
        var original = Source(); var context = original.Context; var predicate = Predicate(truth);
        var declaration = Obj(new { entityId = Id(7), documentId = Id(4), kind = "coglatas.constraint", schemaVersion = 1,
            payload = new { predicate, classification = advisory ? "coglatas.advisory" : "coglatas.required" } });
        var declarations = new JsonArray(declaration);
        if (requirement) declarations.Add(RequirementDeclaration(context));
        var edge = Obj(new
        {
            relationId = Id(20), documentId = Id(4), kind = RelationKinds.Constrains, schemaVersion = 1,
            descriptor = new { descriptorId = RelationKinds.Constrains, version = 1 },
            source = new { kind = "coglatas.entity", expectedKind = "coglatas.constraint", reference = Reference(7, context) },
            target = new { kind = "coglatas.entity", expectedKind = "coglatas.project", reference = Reference(6, context) },
            payload = new { predicateReference = Reference(7, context), version = context.CommittedRevision!.RevisionId.ToString(),
                predicateDigest = ContentDigest.Compute("coglatas.predicate/1", Json(predicate).ToCanonicalBytes()).Data.Value }
        });
        var source = ProjectSource.Create(context, original.Documents, Json(declarations), Json(attached ? new JsonArray(edge) : new JsonArray()));
        return Input(allRules ? AllRulesSettings() : Settings(), source);
    }
    internal static JsonObject RequirementDeclaration(SourceRevisionContext context, bool? truth = true) => Obj(new
    {
        entityId = Id(8), documentId = Id(4), kind = "coglatas.requirement", schemaVersion = 1,
        payload = new { target = Reference(6, context), condition = Predicate(truth), evidenceKind = "coglatas.model-proof" }
    });
    internal static CoreCompilation Compile(CompilerInputManifest input) => PureCoreCompiler.Compile(input, input.Compiler);
}
