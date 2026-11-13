using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;

// Offline synthetic export only. This tool exposes no HTTP, auth, database or job endpoint.
if (args.Length != 1) throw new ArgumentException("Supply the output JSON path.");
var cases = new List<object>();
var declarationCache = new PlanDeclarationReadCache();
var renameFixture = RenameFixture();
var names = new[] { "fail", "pass", "unknown", "unsupported", "not-run", "cancelled", "timeout", "stale-pass", "branch-pass", "other-project", "draft-before", "draft-after" };
foreach (var (name, index) in names.Select((name, index) => (name, index)))
{
    // Displayed committed revisions must never identify two different Sources.
    var input = name switch { "draft-before" => renameFixture.Before, "draft-after" => renameFixture.After, _ => Input(name, 100 + index) };
    var compilation = PureCoreCompiler.Compile(input, input.Compiler, declarationCache: declarationCache);
    var reference = PureCoreCompiler.Compile(input, input.Compiler);
    if (!compilation.Result.Data.Equals(reference.Result.Data)) throw new InvalidOperationException("Incremental declaration decoding differs from full compilation.");
    var result = name switch
    {
        "not-run" => new CompilerResult(input, CompilerExecutionState.NotRun, CompilerCoverage.None, [], [], [], false),
        "cancelled" => new CompilerResult(input, CompilerExecutionState.Cancelled, CompilerCoverage.None, [], [], [], false),
        "timeout" => new CompilerResult(input, CompilerExecutionState.TimedOut, CompilerCoverage.None, [], [], [], false),
        _ => compilation.Result
    };
    var desired = name == "stale-pass" ? Input(name, 200 + index) : input;
    var ir = result.Artifacts.Any(artifact => artifact.Role == "coglatas.source-map") ? compilation.Validation?.Ir : null;
    var freshness = CompilerFreshnessAssessment.Assess(result, desired);
    var declarations = PlanDeclarationReader.Read(input.Source).Select(item => new
    {
        recordType = "entity", identity = item.Data.Value.GetProperty("entityId").GetString(),
        kind = item.Data.Value.GetProperty("kind").GetString(),
        status = item.Status.ToString(), data = item.Data.Value,
        location = ProjectLocation.Create(item.DocumentId, input.Source.Context,
            entityId: EntityId.Parse(item.Data.Value.GetProperty("entityId").GetString()!)).Data.Value
    }).ToArray();
    var relations = input.Source.Relations.Value.EnumerateArray().Select(item => new
    {
        recordType = "relation", identity = item.GetProperty("relationId").GetString(), kind = item.GetProperty("kind").GetString(),
        status = "Decoded", data = item,
        location = ProjectLocation.Create(DocumentId.Parse(Id(4)), input.Source.Context,
            relationId: RelationId.Parse(item.GetProperty("relationId").GetString()!)).Data.Value
    }).ToArray();
    var inspection = result.Execution == CompilerExecutionState.Completed && compilation.Model is { } model
        ? new CompilerSemanticInspection(model) : null;
    var completions = inspection?.Symbols.Where(symbol => symbol.Symbol.PortId is not null).Select(symbol =>
    {
        var page = inspection.CompleteEntityReference(symbol.Symbol.Location, input.InputDigest, 32);
        return new { originKey = symbol.Symbol.Key, inputDigest = page.InputDigest.Data.Value, state = page.State.ToString(),
            expectedKind = page.ExpectedKind, coverageComplete = page.CoverageComplete, truncated = page.NextCursor is not null,
            candidates = page.Candidates.Select(candidate => new { symbolKey = candidate.Symbol.Symbol.Key, displayName = candidate.DisplayName }).ToArray() };
    }).ToArray();
    cases.Add(new
    {
        key = name, projectName = name == "other-project" ? "Synthetic Satellite" : "Synthetic Observatory", workspaceName = "Synthetic Lab", workspaceId = Id(90), branchName = name == "branch-pass" ? "Alternate preview branch" : "Preview branch",
        source = input.Source.Data.Value, sourceText = input.Source.Data.CanonicalText, sourceDigest = input.Source.Digest.Data.Value,
        context = input.Source.Context.Data.Value, desiredContext = desired.Source.Context.Data.Value,
        manifest = input.ToEnvelope().Value, inputDigest = input.InputDigest.Data.Value, inputId = input.InputId.ToString(),
        result = result.Data.Value, resultId = result.ResultId.ToString(), resultDigest = result.ResultDigest.Data.Value,
        freshness = freshness.State.ToString(), changedDimensions = freshness.ChangedDimensions,
        declarations, relations,
        semanticInspection = inspection?.Data.Value,
        referenceCompletions = completions,
        preparedRename = name == "draft-before" ? new { afterCaseKey = "draft-after", plan = renameFixture.Plan.Data.Value } : null,
        sourceMap = ir?.SourceMap.Value,
        sourceMapDigest = ir?.SourceMapDigest.Data.Value
    });
}
var output = JsonSerializer.Serialize(new { format = "coglatas.web-preview/1", mode = "synthetic-fixture", coreProduct = "610f9911f9ce31739188437972f7eeb018f611da", cases }, new JsonSerializerOptions { WriteIndented = true });
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
File.WriteAllText(args[0], output.Replace("\r\n", "\n") + "\n", new System.Text.UTF8Encoding(false));
Console.WriteLine($"Exported {cases.Count} synthetic Core results; no browser/runtime compilation is enabled.");

static string Id(int n) => new Guid(n, 0, 0x4000, 0x80, 0, 0, 0, 0, 0, 0, 1).ToString();
static (CompilerInputManifest Before, CompilerInputManifest After, ProjectDraftRenamePlan Plan) RenameFixture()
{
    var original = Input("draft-base", 110);
    var declarations = original.Source.Declarations.Value.EnumerateArray().Where(item => item.GetProperty("entityId").GetString() is var id && (id == Id(8) || id == Id(9))).ToArray();
    var source = ProjectSource.Create(original.Source.Context, original.Source.Documents, Json(declarations),
        sourceReferences: Json(new[] { new { kind = "coglatas.entity-reference", documentId = Id(4), entityId = Id(8) } }));
    var basis = new CompilerInputManifest(source, original.Settings);
    var proposal = new ProposalContext(ProposalId.Parse(Id(50)), CandidateRevisionId.Parse(Id(51)), source.Context.CommittedRevision!, source.Context.CommittedRevision!);
    var before = ProjectDraftRename.Fork(ProjectSemanticBinder.Bind(basis), proposal);
    var next = SourceRevisionContext.Candidate(new(proposal.ProposalId, CandidateRevisionId.Parse(Id(52)), proposal.BaseRevision, proposal.CapturedTargetHead));
    var target = ProjectLocation.Create(DocumentId.Parse(Id(4)), before.Source.Context, entityId: EntityId.Parse(Id(8)));
    var preview = ProjectDraftRename.Preview(ProjectSemanticBinder.Bind(before), target, before.InputDigest, "Observe renamed 🌌", next);
    var plan = preview.Plan ?? throw new InvalidOperationException("Synthetic Draft Rename preparation failed.");
    return (before, plan.ApplyToDraft(before) ?? throw new InvalidOperationException("Synthetic Draft Rename application failed."), plan);
}
static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
static object Literal(bool? value) => new
{
    kind = "coglatas.literal",
    value = value.HasValue ? (object)new { type = new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = true }, state = "coglatas.known", value = value.Value }
        : new { type = new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = true }, state = "coglatas.unknown", reason = "coglatas.missing-input", basis = new { coverage = "coglatas.partial" } }
};
static CompilerInputManifest Input(string name, int revision)
{
    var project = name == "other-project" ? 22 : 2;
    var context = SourceRevisionContext.Committed(new(new(TenantId.Parse(Id(1)), ProjectId.Parse(Id(project)), BranchId.Parse(Id(name == "branch-pass" ? 31 : 3))), RevisionId.Parse(Id(revision))));
    object Reference(int entity) => new { tenantId = Id(1), projectId = Id(project), entityId = Id(entity), context = context.Data.Value };
    var predicate = Literal(name == "unknown" ? null : name != "fail");
    var declarations = new List<object>
    {
        new { entityId = Id(7), documentId = Id(4), kind = "coglatas.constraint", schemaVersion = 1, displayName = "Launch readiness", payload = new { predicate, classification = "coglatas.required" } },
        new { entityId = Id(8), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1, displayName = "Observe 🌌", payload = new { duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = true }, state = "coglatas.known", value = "10" }, ports = name == "pass" ? new object[] { new { portId = Id(80), direction = "coglatas.input", type = new { kind = "coglatas.entity-reference", schemaVersion = 1, nullable = false, acceptsUnknown = true, expectedKind = "coglatas.task" }, minimumConnections = 0, maximumConnections = 1 } } : Array.Empty<object>() } },
        new { entityId = Id(9), documentId = Id(4), kind = "coglatas.milestone", schemaVersion = 1, displayName = "First light", payload = new { target = new { type = new { kind = "coglatas.instant", schemaVersion = 1, nullable = false, acceptsUnknown = true }, state = "coglatas.known", value = "2026-11-01T00:00:00.0000000Z" } } },
        new { entityId = Id(10), documentId = Id(4), kind = "coglatas.workflow", schemaVersion = 1, displayName = "Observation workflow", payload = new { states = new[] { Id(30) }, initialState = Id(30), maximumTransitions = 2, transitions = Array.Empty<object>() } }
    };
    if (name == "unsupported")
    {
        declarations.Add(new { entityId = Id(11), documentId = Id(4), kind = "coglatas.requirement", schemaVersion = 1, displayName = "Evidence required", payload = new { target = Reference(6), condition = Literal(true), evidenceKind = "coglatas.model-proof" } });
        declarations.Add(new { entityId = Id(12), documentId = Id(4), kind = "coglatas.resource-binding", schemaVersion = 1, displayName = "Synthetic telescope", payload = new { tenantId = Id(1), resourceId = Id(40), snapshotVersion = "1", snapshotDigest = new { algorithm = "sha-256", value = new string('a', 64) }, resourceKind = "coglatas.equipment" } });
        declarations.Add(new { entityId = Id(13), documentId = Id(4), kind = "example.future", schemaVersion = 1, displayName = "Opaque declaration", payload = new { opaque = "Preserved without inferred semantics" } });
    }
    var relation = new
    {
        relationId = Id(20), documentId = Id(4), kind = RelationKinds.Constrains, schemaVersion = 1,
        descriptor = new { descriptorId = RelationKinds.Constrains, version = 1 },
        source = new { kind = "coglatas.entity", expectedKind = "coglatas.constraint", reference = Reference(7) },
        target = new { kind = "coglatas.entity", expectedKind = "coglatas.project", reference = Reference(6) },
        payload = new { predicateReference = Reference(7), version = context.CommittedRevision!.RevisionId.ToString(), predicateDigest = ContentDigest.Compute("coglatas.predicate/1", Json(predicate).ToCanonicalBytes()).Data.Value }
    };
    var document = SourceDocument.Create(DocumentId.Parse(Id(4)), name == "stale-pass" ? "renamed-observatory" : "observatory", Json(new { entityId = Id(6), kind = "coglatas.project", schemaVersion = 1, displayName = "Synthetic Observatory", payload = new { } }));
    var source = ProjectSource.Create(context, [document], Json(declarations), Json(new[] { relation }));
    var settings = new JsonObject();
    foreach (var field in new[] { "schema", "compiler", "ruleset", "semantics", "ir", "evaluator" })
        settings[field] = JsonNode.Parse(Json(new { artifactId = field switch { "schema" => ProjectSource.SchemaId, "semantics" => "coglatas.semantic-model", "ir" => "coglatas.project-ir", _ => "coglatas." + field }, version = "1", digest = new { algorithm = "sha-256", value = new string('a', 64) } }).CanonicalText);
    foreach (var field in new[] { "scheduler", "solver", "stochastic", "importer", "scenario" }) settings[field] = JsonNode.Parse("{\"kind\":\"coglatas.none\"}");
    foreach (var field in new[] { "authorizedExternalSnapshots", "resourceSnapshots", "allocationSnapshots", "observations", "evidence", "units", "calendars", "timeZones" }) settings[field] = new JsonArray();
    settings["descriptors"] = JsonSerializer.SerializeToNode(RelationKindDescriptor.BuiltIns.Select(item => new { artifactId = item.DescriptorId, version = "1", digest = item.Digest.Data.Value }));
    settings["rules"] = JsonSerializer.SerializeToNode(new[] { CoreCompilerRules.Schema, CoreCompilerRules.Declarations, CoreCompilerRules.Relations, CoreCompilerRules.Constraints, CoreCompilerRules.Requirements, CoreCompilerRules.Resources }.Select(ruleId => new { ruleId, version = 1, required = true, configuration = new { } }));
    settings["featureSet"] = JsonSerializer.SerializeToNode(RelationKindDescriptor.BuiltIns.SelectMany(item => item.RequiredCapabilities).Prepend("coglatas.core.structural"));
    settings["targets"] = new JsonArray("coglatas.semantic-model"); settings["analysisScope"] = new JsonArray("coglatas.source-structure");
    settings["sourceCoverage"] = "coglatas.complete"; settings["clock"] = JsonNode.Parse("{\"kind\":\"coglatas.not-applicable\"}");
    settings["executionProfile"] = JsonSerializer.SerializeToNode(new { purpose = "coglatas.normal", maximumOperations = 200000, maximumDeclarations = 10000, maximumRelations = 20000, maximumDigits = 256, maximumScale = 256, rounding = "coglatas.ties-to-even" });
    return new(source, SourceJson.Parse(settings.ToJsonString()));
}
