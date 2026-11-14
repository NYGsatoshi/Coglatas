using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Preview;
using Coglatas.ProjectIde.Workbench.Synthetic;

// Offline synthetic export only. This tool exposes no HTTP, auth, database or job endpoint.
var testEditProfile = args is [_, "--test-edit"];
var dependencyProfile = args is [_, "--dependency-analysis"];
var graphProfile = args is [_, "--graph-closure"];
var debugProfile = args is [_, "--simulation-debug"];
var simulationProfile = args is [_, "--simulation"] || debugProfile;
var planProfile = args is [_, "--execution-plan"] || simulationProfile;
var publicationProfile = args is [_, "--scoped-publication"];
var scopedProfile = args is [_, "--scoped-sources"];
var scenarioProfile = args is [_, "--scenario"] || dependencyProfile || graphProfile || scopedProfile || publicationProfile || planProfile;
if (args.Length != 1 && !testEditProfile && !scenarioProfile) throw new ArgumentException("Supply the output JSON path and optional --test-edit, --scenario, --dependency-analysis, --graph-closure or --scoped-sources/--scoped-publication/--execution-plan/--simulation/--simulation-debug profile.");
var testEditFixture = testEditProfile ? TestEditFixture() : null;
var scenarioFixtures = scenarioProfile ? new Dictionary<string, PreparedScenarioFixture>(StringComparer.Ordinal)
{
    ["tests-scenario"] = ScenarioFixture(), ["tests-reference"] = ReferenceScenarioFixture(900),
    ["tests-nested-reference"] = ReferenceScenarioFixture(1000, nested: true),
    ["tests-missing-reference"] = ReferenceScenarioFixture(1100, admission: "missing"),
    ["tests-denied-reference"] = ReferenceScenarioFixture(1200, admission: "denied")
} : null;
if (dependencyProfile) scenarioFixtures = new(StringComparer.Ordinal) { ["tests-dependency"] = ReferenceScenarioFixture(1300, dependencyConstraint: true) };
if (scopedProfile) scenarioFixtures = new(StringComparer.Ordinal) { ["tests-scoped"] = ReferenceScenarioFixture(1500, dependencyConstraint: true, additionalVersion: true) };
if (graphProfile) scenarioFixtures = new(StringComparer.Ordinal) { ["tests-graph"] = ReferenceScenarioFixture(1400, graphCycle: true) };
if (publicationProfile) scenarioFixtures = new(StringComparer.Ordinal) { ["tests-publication-root"] = ScenarioFixture(publication: true), ["tests-publication-true"] = ReferenceScenarioFixture(1600, dependencyConstraint: true, publication: true, truth: true), ["tests-publication-false"] = ReferenceScenarioFixture(1700, dependencyConstraint: true, publication: true, truth: false), ["tests-publication-unknown"] = ReferenceScenarioFixture(1800, dependencyConstraint: true, publication: true, truth: null) };
if (planProfile) scenarioFixtures = new(StringComparer.Ordinal) {
    ["tests-plan-root"] = ScenarioFixture(publication: true, planTarget: true),
    ["tests-plan-external"] = ReferenceScenarioFixture(1900, dependencyConstraint: true, publication: true, truth: true, planTarget: true),
    ["tests-plan-unknown"] = ReferenceScenarioFixture(2000, dependencyConstraint: true, publication: true, truth: null, planTarget: true),
    ["tests-plan-budget"] = ReferenceScenarioFixture(2100, dependencyConstraint: true, publication: true, truth: true, planTarget: true, planBudget: 200000)
};
if (simulationProfile) scenarioFixtures = new(StringComparer.Ordinal) {
    ["tests-simulation-root"] = ScenarioFixture(publication: true, planTarget: true),
    ["tests-simulation-external"] = ReferenceScenarioFixture(2600, dependencyConstraint: true, publication: true, truth: true, planTarget: true),
    ["tests-simulation-unknown"] = ReferenceScenarioFixture(2300, dependencyConstraint: true, publication: true, truth: null, planTarget: true),
    ["tests-simulation-bounded"] = ReferenceScenarioFixture(2400, dependencyConstraint: true, publication: true, truth: true, planTarget: true)
};
var cases = new List<object>();
var declarationCache = new PlanDeclarationReadCache();
var renameFixture = RenameFixture();
var documentOriginal = Input("document-before", 130);
var documentDeclarations = Json(documentOriginal.Source.Declarations.Value.EnumerateArray()
    .Where(item => item.GetProperty("kind").GetString() is "coglatas.task" or "coglatas.milestone").ToArray());
var documentBefore = new CompilerInputManifest(ProjectSource.Create(documentOriginal.Source.Context, documentOriginal.Source.Documents,
    documentDeclarations, Json(Array.Empty<object>())), documentOriginal.Settings);
var documentBase = Input("document-after", 131);
var documentAfter = new CompilerInputManifest(ProjectSource.Create(documentBase.Source.Context,
    documentBase.Source.Documents.Select(document => SourceDocument.Create(document.DocumentId, document.LogicalName, document.Root.Data,
        Json(new { annotation = "Opaque document annotation; no inferred layout semantics" }))), documentDeclarations,
    Json(Array.Empty<object>())), documentBase.Settings);
var historyDelta = ProjectSourceDelta.Create(documentBefore.Source, documentAfter.Source);
var historyRecords = new[] { ProjectHistoryRevision.Checkpoint(documentBefore.Source, 0), ProjectHistoryRevision.ForwardDelta(historyDelta, 1) };
var history = ProjectSourceHistory.Read(historyDelta.AfterRevision, historyRecords);
var missingHistory = ProjectSourceHistory.Read(historyDelta.AfterRevision, [historyRecords[1]]);
if (history.State != HistoryReadState.Ready || !history.Source!.Data.Equals(documentAfter.Source.Data) || missingHistory.State != HistoryReadState.Unavailable)
    throw new InvalidOperationException("History reconstruction did not preserve the full-reference fixture or explicit missing anchor.");
var compactedHistory = ProjectSourceHistory.Read(historyDelta.AfterRevision, [ProjectHistoryRevision.Checkpoint(history.Source, 1, [historyDelta.BeforeRevision])]);
if (compactedHistory.State != HistoryReadState.Ready || !compactedHistory.Source!.Data.Equals(history.Source.Data))
    throw new InvalidOperationException("History checkpoint compaction changed the reconstructed Source.");
documentAfter = new CompilerInputManifest(history.Source, documentAfter.Settings);
var revert = ProjectDraftRevert.Preview(ProjectSemanticBinder.Bind(documentBefore), ProjectSemanticBinder.Bind(documentAfter), ProjectSemanticBinder.Bind(documentAfter),
    documentBefore.InputDigest, documentAfter.InputDigest, documentAfter.InputDigest,
    new(ProposalId.Parse(Id(140)), CandidateRevisionId.Parse(Id(141)), historyDelta.AfterRevision, historyDelta.AfterRevision), historyRecords);
var revertPlan = revert.Plan ?? throw new InvalidOperationException("Synthetic inverse Draft preparation failed.");
var revertedDraft = revertPlan.ApplyToDraft(documentAfter) ?? throw new InvalidOperationException("Synthetic inverse Draft application failed.");
if (revertedDraft.Source.Context.Proposal?.BaseRevision != historyDelta.AfterRevision ||
    !revertedDraft.Source.Documents[0].Data.Equals(documentBefore.Source.Documents[0].Data))
    throw new InvalidOperationException("Synthetic inverse Draft lost its current base or original document.");
var diffBefore = ProjectSemanticBinder.Bind(renameFixture.Before);
var diffAfter = ProjectSemanticBinder.Bind(renameFixture.After);
var envelopeBefore = EnvelopeInput(150, "original"); var envelopeAfter = EnvelopeInput(151, "changed");
var requirementCandidates = RequirementCandidateInput();
var names = new[] { "fail", "pass", "unknown", "unsupported", "not-run", "cancelled", "timeout", "stale-pass", "branch-pass", "other-project", "draft-before", "draft-after", "document-before", "document-after", "revert-draft", "envelope-before", "envelope-after", "requirement-candidates", "test-assertion-navigation", "tests-pass", "tests-fail", "tests-unknown", "tests-empty", "tests-unsupported", "tests-cancelled", "tests-stale", "tests-evaluator-mismatch", "tests-error", "tests-exhausted", "tests-unicode", "tests-many" };
if (testEditProfile) names = ["tests-edit-before", "tests-edit-after"];
if (scenarioProfile) names = scenarioFixtures!.Keys.SelectMany(key => new[] { key + "-base", key + "-hypothesis", key + "-draft" }).ToArray();
foreach (var (name, index) in names.Select((name, index) => (name, index)))
{
    var scenarioKey = scenarioProfile ? scenarioFixtures!.Keys.Single(key => name == key + "-base" || name == key + "-hypothesis" || name == key + "-draft") : null;
    var scenarioFixture = scenarioKey is null ? null : scenarioFixtures![scenarioKey];
    var snapshots = scenarioFixture?.Snapshots ?? [];
    // Displayed committed revisions must never identify two different Sources.
    var input = name switch { "tests-scenario-base" => scenarioFixture!.Scenario.Basis, "tests-scenario-hypothesis" => scenarioFixture!.Scenario.Input, "tests-scenario-draft" => scenarioFixture!.Draft.After, "tests-edit-before" => testEditFixture!.Before, "tests-edit-after" => testEditFixture!.After, "draft-before" => renameFixture.Before, "draft-after" => renameFixture.After,
        "document-before" => documentBefore, "document-after" => documentAfter, "revert-draft" => revertedDraft,
        "envelope-before" => envelopeBefore, "envelope-after" => envelopeAfter, "requirement-candidates" => requirementCandidates, "test-assertion-navigation" => TestAssertionNavigationInput(), _ when name.StartsWith("tests-", StringComparison.Ordinal) => LocalTestInput(name, 180 + index), _ => Input(name, 100 + index) };
    if (scenarioFixture is not null) input = name == scenarioKey + "-base" ? scenarioFixture.Scenario.Basis : name == scenarioKey + "-hypothesis" ? scenarioFixture.Scenario.Input : scenarioFixture.Draft.After;
    var compilation = PureCoreCompiler.Compile(input, input.Compiler, snapshots, declarationCache: declarationCache);
    var reference = PureCoreCompiler.Compile(input, input.Compiler, snapshots);
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
        preparedRevert = name == "document-after" ? new { parentCaseKey = "document-before", revertedCaseKey = "document-after", candidateCaseKey = "revert-draft", plan = revertPlan.Data.Value } : null,
        preparedDiff = name switch
        {
            "draft-before" => new { afterCaseKey = "draft-after", comparison = ProjectSemanticDiff.Compare(diffBefore, diffAfter, renameFixture.Before.InputDigest, renameFixture.After.InputDigest).Data.Value },
            "document-before" => new { afterCaseKey = "document-after", comparison = ProjectSemanticDiff.Compare(ProjectSemanticBinder.Bind(documentBefore),
                ProjectSemanticBinder.Bind(documentAfter), documentBefore.InputDigest, documentAfter.InputDigest).Data.Value },
            "envelope-before" => new { afterCaseKey = "envelope-after", comparison = ProjectSemanticDiff.Compare(ProjectSemanticBinder.Bind(envelopeBefore),
                ProjectSemanticBinder.Bind(envelopeAfter), envelopeBefore.InputDigest, envelopeAfter.InputDigest).Data.Value },
            "unsupported" => new { afterCaseKey = name, comparison = ProjectSemanticDiff.Compare(compilation.Model, compilation.Model, input.InputDigest, input.InputDigest).Data.Value },
            "fail" => new { afterCaseKey = name, comparison = ProjectSemanticDiff.Compare(compilation.Model, compilation.Model, input.InputDigest, input.InputDigest).Data.Value },
            _ => null
        },
        preparedHistory = name == "document-before" ? new
        {
            headCaseKey = "document-after", state = history.State.ToString(), sourceAdmissionStatus = history.SourceAdmissionStatus.ToString(),
            missingAnchorState = missingHistory.State.ToString(), compactedCheckpointVerified = true,
            reconstructionPath = history.ReconstructionPath.Select(revision => SourceRevisionContext.Committed(revision).Data.Value).ToArray(),
            revisions = historyRecords.Select(record => new
            {
                caseKey = record.Delta is null ? "document-before" : "document-after", context = SourceRevisionContext.Committed(record.Revision).Data.Value,
                branchSequence = record.BranchSequence, sourceDigest = record.ExpectedSourceDigest.Data.Value,
                storage = record.Delta is null ? "Checkpoint" : "TypedDelta",
                parents = record.Parents.Select(parent => SourceRevisionContext.Committed(parent).Data.Value).ToArray()
            }).ToArray(),
            deltaRecordCounts = new { documents = historyDelta.Documents.Count, declarations = historyDelta.Declarations.Count, relations = historyDelta.Relations.Count }
        } : null,
        sourceMap = ir?.SourceMap.Value,
        sourceMapDigest = ir?.SourceMapDigest.Data.Value
    });
    if (name.StartsWith("tests-", StringComparison.Ordinal))
    {
        var plan = ProjectTestPlan.Compile(compilation.Model!, SyntheticProjectTestConfiguration.Evaluator, name == "tests-exhausted" ? new(256, 1) : null);
        using var cancellation = new CancellationTokenSource();
        if (name == "tests-cancelled") cancellation.Cancel();
        var expected = name == "tests-stale" ? ContentDigest.Compute("coglatas.synthetic-stale-test-input/1", []) : input.InputDigest;
        var testRun = plan.Execute(expected, cancellation.Token);
        var testDeclarations = PlanDeclarationReader.Read(input.Source).Where(item => item.Data.Value.GetProperty("kind").GetString() == "coglatas.project-test")
            .OrderBy(item => item.Data.Value.GetProperty("entityId").GetString(), StringComparer.Ordinal).ToArray();
        var projection = new { format = "coglatas.local-test-projection/1", inputText = input.Data.CanonicalText,
            plan = new { text = plan.Data.CanonicalText, digest = plan.Digest.Data.Value },
            result = new { text = testRun.Data.CanonicalText, digest = testRun.Digest.Data.Value },
            declarations = testDeclarations.Select(item => new { testId = item.Data.Value.GetProperty("entityId").GetString(), text = item.Data.CanonicalText }).ToArray(),
            assertions = testDeclarations.Where(item => item.Declaration is ProjectTestPlanDeclaration)
                .SelectMany(item => ((ProjectTestPlanDeclaration)item.Declaration!).Assertions.Select(assertion => new
                { testId = item.Data.Value.GetProperty("entityId").GetString(), key = assertion.Key, text = assertion.Data.CanonicalText })).ToArray() };
        var fixture = JsonSerializer.SerializeToNode(cases[^1])!.AsObject();
        fixture["localProjectTests"] = JsonSerializer.SerializeToNode(projection); cases[^1] = fixture;
    }
    if (name == "tests-edit-before")
    {
        var fixture = JsonSerializer.SerializeToNode(cases[^1])!.AsObject();
        fixture["preparedTestEdit"] = JsonSerializer.SerializeToNode(new { afterCaseKey = "tests-edit-after", plan = testEditFixture!.Plan.Data.Value,
            definitionText = testEditFixture.Exported.CanonicalText, importedText = testEditFixture.Imported.CanonicalText });
        cases[^1] = fixture;
    }
    if (scenarioProfile)
    {
        var fixture = JsonSerializer.SerializeToNode(cases[^1])!.AsObject();
        fixture["preparedScenario"] = JsonSerializer.SerializeToNode(new { baselineCaseKey = scenarioKey + "-base", scenarioCaseKey = scenarioKey + "-hypothesis", draftCaseKey = scenarioKey + "-draft",
            overlay = new { text = scenarioFixture!.Scenario.Overlay.CanonicalText, digest = scenarioFixture.Scenario.OverlayDigest.Data.Value }, draftPlan = scenarioFixture.Draft.Data.Value,
            appliedValues = new { scenario = AppliedValues(ProjectScenarioAppliedValues.Read(scenarioFixture.Scenario, scenarioFixture.Scenario.Input)),
                draft = AppliedValues(ProjectScenarioAppliedValues.Read(scenarioFixture.Draft, scenarioFixture.Draft.After)) } });
        if (name == scenarioKey + "-base" || name == scenarioKey + "-hypothesis")
        {
            var after = name == scenarioKey + "-base" ? scenarioFixture.Scenario.Input : scenarioFixture.Draft.After;
            if (inspection is not null && PureCoreCompiler.Compile(after, after.Compiler, snapshots).Result.Execution == CompilerExecutionState.Completed)
                fixture["preparedDiff"] = JsonSerializer.SerializeToNode(new { afterCaseKey = name == scenarioKey + "-base" ? scenarioKey + "-hypothesis" : scenarioKey + "-draft",
                comparison = ProjectSemanticDiff.Compare(ProjectSemanticBinder.Bind(input, snapshots), ProjectSemanticBinder.Bind(after, snapshots), input.InputDigest, after.InputDigest).Data.Value });
        }
        if (snapshots.Count != 0 || scenarioFixture.Scenario.Basis.Settings.Value.GetProperty("authorizedExternalSnapshots").GetArrayLength() != 0)
            fixture["materializedSnapshots"] = JsonSerializer.SerializeToNode(SyntheticSnapshotMaterialization.Export(input, snapshots).Value);
        if (SyntheticScopedSourceProjection.Export(compilation) is { } scopedProjection)
            fixture["scopedSources"] = JsonSerializer.SerializeToNode(scopedProjection.Value);
        if (simulationProfile && compilation.Publication?.ExecutionPlan is { } simulationPlan)
        {
            var request = new SimulationRunManifest(simulationPlan, Guid.Parse(Id(2500 + index)), 100,
                new(MaximumEvents: scenarioKey == "tests-simulation-bounded" ? 2 : 4096));
            ProjectSimulationRun run;
            if (debugProfile) {
                var recorded = SyntheticSimulationDebugRecording.Capture(request); run = recorded.Run;
                fixture["simulationDebug"] = JsonSerializer.SerializeToNode(recorded.Recording.Value);
            } else run = ProjectSimulation.Execute(request, input.InputDigest);
            if (run.Execution != (scenarioKey == "tests-simulation-bounded" ? CompilerExecutionState.ResourceExhausted : CompilerExecutionState.Completed))
                throw new InvalidOperationException("Actual simulation fixture did not retain its expected execution boundary.");
            fixture["simulationRun"] = JsonSerializer.SerializeToNode(new { text = run.Data.CanonicalText, digest = run.Digest.Data.Value });
        }
        cases[^1] = fixture;
    }
    if (name == "requirement-candidates")
    {
        if (inspection is null || ir is null) throw new InvalidOperationException("Task candidates require actual Core inspection and source navigation.");
        var fixture = JsonSerializer.SerializeToNode(cases[^1])!.AsObject();
        fixture["requirementTaskSuggestions"] = JsonSerializer.SerializeToNode(inspection.Symbols.Where(symbol => symbol.Symbol.Kind == "coglatas.requirement").Select(symbol =>
        {
            var page = inspection.SuggestRequirementTasks(symbol.Symbol.Location, input.InputDigest, 32);
            if (page.State != RequirementTaskSuggestionState.Ready || !page.CandidateOnly || page.Candidates.Count == 0)
                throw new InvalidOperationException("Task candidate fixture did not contain bounded real Core candidates.");
            return new { originKey = symbol.Symbol.Key, inputDigest = page.InputDigest.Data.Value, state = page.State.ToString(), candidateOnly = page.CandidateOnly,
                coverageComplete = page.CoverageComplete, truncated = page.NextCursor is not null,
                candidates = page.Candidates.Select(candidate => new { symbolKey = candidate.Symbol.Symbol.Key, displayName = candidate.DisplayName, reason = candidate.Reason.ToString() }).ToArray() };
        }).ToArray());
        cases[^1] = fixture;
    }
}
var output = JsonSerializer.Serialize(new { format = "coglatas.web-preview/1", mode = "synthetic-fixture", coreProduct = "610f9911f9ce31739188437972f7eeb018f611da", cases }, new JsonSerializerOptions { WriteIndented = true });
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
File.WriteAllText(args[0], output.Replace("\r\n", "\n") + "\n", new System.Text.UTF8Encoding(false));
Console.WriteLine($"Exported {cases.Count} synthetic Core results; no browser/runtime compilation is enabled.");

static object AppliedValues(ProjectScenarioAppliedValuesPreview preview)
{
    var projection = preview.Projection ?? throw new InvalidOperationException($"Scenario applied values unavailable: {preview.State}");
    return new { text = projection.Data.CanonicalText, digest = projection.Digest.Data.Value };
}

static CompilerInputManifest PlanTarget(CompilerInputManifest input, int operations = 400000) {
    var settings = JsonNode.Parse(input.Settings.CanonicalText)!.AsObject(); settings["targets"]!.AsArray().Add(CompilerExecutionPlan.Target);
    settings["scheduler"] = JsonSerializer.SerializeToNode(new { kind = "coglatas.plan-lowering", artifact = CompilerExecutionPlan.Engine.Data.Value });
    settings["executionProfile"]!["maximumOperations"] = operations;
    var declarations = input.Source.Declarations.Value.EnumerateArray().Select(row => (object)row).Concat(new[] { PlanTask(7), PlanTask(9) }).ToArray();
    object Edge(int id, int dependent, int prerequisite, SourceRevisionContext target) {
        object Endpoint(int entity, SourceRevisionContext context) => new { kind = "coglatas.entity", expectedKind = "coglatas.task", reference = new {
            tenantId = context.Branch.TenantId.ToString(), projectId = context.Branch.ProjectId.ToString(), entityId = Id(entity), context = context.Data.Value } };
        return new { relationId = Id(id), documentId = Id(4), kind = RelationKinds.DependsOn, schemaVersion = 1, descriptor = new { descriptorId = RelationKinds.DependsOn, version = 1 },
            source = Endpoint(dependent, input.Source.Context), target = Endpoint(prerequisite, target), payload = new { mode = "coglatas.finish-to-start", lag = new {
                type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "0" } } };
    }
    var relations = input.Source.Relations.Value.EnumerateArray().Select(row => (object)row).ToList();
    relations.AddRange(new[] { Edge(44, 8, 7, input.Source.Context), Edge(45, 9, 8, input.Source.Context) });
    var pins = input.Settings.Value.GetProperty("authorizedExternalSnapshots");
    if (pins.GetArrayLength() != 0) relations.Add(Edge(46, 8, 19, SourceRevisionContext.Parse(SourceJson.Parse(pins[0].GetProperty("context").GetRawText()))));
    return new(ProjectSource.Create(input.Source.Context, input.Source.Documents, Json(declarations), Json(relations)), Json(settings));
}
static object PlanTask(int id) => new { entityId = Id(id), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1, payload = new {
    duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "1" }, ports = Array.Empty<object>() } };
static string Id(int n) => new Guid(n, 0, 0x4000, 0x80, 0, 0, 0, 0, 0, 0, 1).ToString();
static PreparedScenarioFixture ScenarioFixture(bool publication = false, bool planTarget = false)
{
    var fixtureRevision = planTarget ? 2200 : 750;
    var basis = LocalTestInput("tests-fail", fixtureRevision);
    if (publication) { var settings = JsonNode.Parse(basis.Settings.CanonicalText)!.AsObject(); settings["semantics"]!["version"] = "2"; settings["ir"]!["version"] = "2"; basis = new(basis.Source, Json(settings)); }
    if (planTarget) basis = PlanTarget(basis);
    var model = ProjectSemanticBinder.Bind(basis);
    var task = model.Declarations.Select(item => item.Declaration).OfType<TaskPlanDeclaration>().Single(item => item.EntityId == EntityId.Parse(Id(8)));
    var port = task.Ports.Single(); var value = JsonNode.Parse(port.DefaultValue!.Data.CanonicalText)!.AsObject(); value["value"] = true;
    var change = new ProjectScenarioOverride(ProjectLocation.Create(task.DocumentId, basis.Source.Context, entityId: task.EntityId),
        ProjectScenarioOverrideKind.TaskInputDefault, Json(value), port.PortId);
    var plan = ProjectScenarios.Prepare(model, basis.InputDigest, ScenarioId.Parse(Id(fixtureRevision + 10)), ScenarioRevisionId.Parse(Id(fixtureRevision + 11)), [change]).Plan
        ?? throw new InvalidOperationException("Scenario preparation unavailable.");
    var revision = basis.Source.Context.CommittedRevision!;
    var draft = ProjectScenarios.ToDraft(plan, model, basis.InputDigest, new(ProposalId.Parse(Id(fixtureRevision + 20)), CandidateRevisionId.Parse(Id(fixtureRevision + 21)), revision, revision)).Plan
        ?? throw new InvalidOperationException("Scenario Draft conversion unavailable.");
    if (!plan.Materialize(basis)!.Data.Equals(plan.Input.Data) || !draft.ApplyToDraft(basis)!.Data.Equals(draft.After.Data))
        throw new InvalidOperationException("Scenario application lost its exact base.");
    var verdicts = new[] { basis, plan.Input, draft.After }.Select(input => ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(input), SyntheticProjectTestConfiguration.Evaluator).Execute(input.InputDigest).Verdict).ToArray();
    if (!verdicts.SequenceEqual([CompilerVerdict.Fail, CompilerVerdict.Pass, CompilerVerdict.Pass]))
        throw new InvalidOperationException("Actual Scenario Test results did not preserve baseline and fresh input verdicts.");
    return new(plan, draft);
}
static PreparedScenarioFixture ReferenceScenarioFixture(int revision, bool nested = false, string admission = "available", bool dependencyConstraint = false, bool graphCycle = false, bool additionalVersion = false, bool publication = false, bool? truth = false, bool planTarget = false, int planBudget = 400000)
{
    var template = Input("reference-scenario", revision); var context = template.Source.Context;
    var suppliedSnapshot = planTarget ? revision + 300 : 230;
    var externalContext = SourceRevisionContext.Committed(new(new(TenantId.Parse(Id(1)), ProjectId.Parse(Id(202)), BranchId.Parse(Id(203))), RevisionId.Parse(Id(planTarget ? revision + 100 : 204))));
    var external = ProjectSource.Create(externalContext, template.Source.Documents, Json(new[] { new { entityId = Id(16), documentId = Id(4), kind = "coglatas.project", schemaVersion = 1, payload = new { } } }));
    if (dependencyConstraint)
    {
        var predicate = Literal(truth);
        var constraint = new { entityId = Id(17), documentId = Id(4), kind = "coglatas.constraint", schemaVersion = 1,
            payload = new { predicate, classification = "coglatas.required" } };
        object Ref(int entity) => new { tenantId = Id(1), projectId = Id(202), entityId = Id(entity), context = externalContext.Data.Value };
        var relation = new { relationId = Id(18), documentId = Id(4), kind = RelationKinds.Constrains, schemaVersion = 1,
            descriptor = new { descriptorId = RelationKinds.Constrains, version = 1 },
            source = new { kind = "coglatas.entity", expectedKind = "coglatas.constraint", reference = Ref(17) },
            target = new { kind = "coglatas.entity", expectedKind = "coglatas.project", reference = Ref(16) },
            payload = new { predicateReference = Ref(17), version = externalContext.CommittedRevision!.RevisionId.ToString(),
                predicateDigest = ContentDigest.Compute("coglatas.predicate/1", Json(predicate).ToCanonicalBytes()).Data.Value } };
        external = ProjectSource.Create(externalContext, external.Documents,
            Json(external.Declarations.Value.EnumerateArray().Select(item => (object)item).Append(constraint).ToArray()), Json(new[] { relation }));
    }
    if (planTarget) external = ProjectSource.Create(external.Context, external.Documents, Json(external.Declarations.Value.EnumerateArray().Select(row => (object)row).Append(PlanTask(19)).ToArray()), external.Relations);
    ProjectSource? versionedDependency = null;
    if (additionalVersion)
    {
        var nextContext = SourceRevisionContext.Committed(new(externalContext.Branch, RevisionId.Parse(Id(205))));
        var documents = external.Documents.Select(document =>
        {
            var root = JsonNode.Parse(document.Root.Data.CanonicalText)!.AsObject(); root["displayName"] = "Second captured revision";
            return SourceDocument.Create(document.DocumentId, document.LogicalName + "-version-2", Json(root));
        });
        versionedDependency = ProjectSource.Create(nextContext, documents, external.Declarations);
    }
    ProjectSource? graphDependency = null;
    if (graphCycle)
    {
        var bContext = SourceRevisionContext.Committed(new(new(TenantId.Parse(Id(1)), ProjectId.Parse(Id(302)), BranchId.Parse(Id(303))), RevisionId.Parse(Id(304))));
        var graphTask = new { entityId = Id(77), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1, payload = new
        {
            duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "1" },
            ports = Array.Empty<object>()
        } };
        object Endpoint(SourceRevisionContext ctx) => new { kind = "coglatas.entity", expectedKind = "coglatas.task", reference = new
            { tenantId = ctx.Branch.TenantId.ToString(), projectId = ctx.Branch.ProjectId.ToString(), entityId = Id(77), context = ctx.Data.Value } };
        object Edge(SourceRevisionContext from, SourceRevisionContext to) => new { relationId = Id(78), documentId = Id(4), kind = RelationKinds.DependsOn,
            schemaVersion = 1, descriptor = new { descriptorId = RelationKinds.DependsOn, version = 1 }, source = Endpoint(from), target = Endpoint(to),
            payload = new { mode = "coglatas.finish-to-start", lag = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false },
                state = "coglatas.known", value = "0" } } };
        graphDependency = ProjectSource.Create(bContext, template.Source.Documents, Json(new[] { graphTask }), Json(new[] { Edge(bContext, externalContext) }));
        external = ProjectSource.Create(externalContext, external.Documents,
            Json(external.Declarations.Value.EnumerateArray().Select(item => (object)item).Append(graphTask).ToArray()), Json(new[] { Edge(externalContext, bContext) }));
    }
    var referenceType = JsonSerializer.SerializeToNode(new { kind = "coglatas.entity-reference", schemaVersion = 1, nullable = false, acceptsUnknown = true, expectedKind = "coglatas.project" })!.AsObject();
    JsonObject ReferenceValue(int target) => JsonSerializer.SerializeToNode(new { type = referenceType, state = "coglatas.known", value = new {
        tenantId = Id(1), projectId = Id(202), entityId = Id(target), context = externalContext.Data.Value,
        origin = ProjectLocation.Create(DocumentId.Parse(Id(4)), context, entityId: EntityId.Parse(Id(8))).Data.Value,
        manifest = new { version = "1", digest = external.Digest.Data.Value } } })!.AsObject();
    JsonObject NestedValue(bool after)
    {
        var childType = new JsonObject { ["kind"] = "coglatas.structure", ["schemaVersion"] = 1, ["nullable"] = false, ["acceptsUnknown"] = true,
            ["fields"] = new JsonArray(new JsonObject { ["name"] = "ref/~field", ["required"] = true, ["type"] = referenceType.DeepClone() }) };
        JsonObject Child(int target) => new() { ["type"] = childType.DeepClone(), ["state"] = "coglatas.known", ["value"] = new JsonObject { ["ref/~field"] = ReferenceValue(target) } };
        var type = new JsonObject { ["kind"] = "coglatas.list", ["schemaVersion"] = 1, ["nullable"] = false, ["acceptsUnknown"] = true, ["elementType"] = childType.DeepClone() };
        var items = new JsonArray(Child(6)); if (after) items.Add(Child(16));
        return new() { ["type"] = type, ["state"] = "coglatas.known", ["value"] = items };
    }
    var beforeValue = nested ? NestedValue(false) : ReferenceValue(6); var afterValue = nested ? NestedValue(true) : ReferenceValue(16);
    var booleanType = new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = false };
    object Boolean(bool value) => new { type = booleanType, state = "coglatas.known", value };
    var ports = new JsonArray(JsonSerializer.SerializeToNode(new { portId = Id(88), direction = "coglatas.input", type = beforeValue["type"]!.DeepClone(), minimumConnections = 0, maximumConnections = 1, defaultValue = beforeValue }));
    if (!nested) ports.Add(JsonSerializer.SerializeToNode(new { portId = Id(89), direction = "coglatas.input", type = booleanType, minimumConnections = 0, maximumConnections = 1, defaultValue = Boolean(false) }));
    var task = new { entityId = Id(8), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1, displayName = "Pinned reference hypothesis",
        payload = new { duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "1" }, ports } };
    object Target() => new { tenantId = Id(1), projectId = Id(2), entityId = Id(8), context = context.Data.Value };
    var assertions = nested ? new object[] { new { key = "example.collection", kind = "coglatas.assert-collection-count", target = Target(), portId = Id(88), expectedCount = 2 } } :
        new object[] { new { key = "example.reference", kind = "coglatas.assert-reference-resolved", target = Target(), portId = Id(88) },
            new { key = "example.ready", kind = "coglatas.assert-boolean", target = Target(), portId = Id(89), expected = Boolean(true) } };
    var test = new { entityId = Id(10), documentId = Id(4), kind = "coglatas.project-test", schemaVersion = 1, displayName = "Pinned reference assertions", payload = new { assertions } };
    var settings = JsonNode.Parse(template.Settings.CanonicalText)!.AsObject(); settings["evaluator"] = JsonNode.Parse(SyntheticProjectTestConfiguration.Evaluator.Data.CanonicalText);
    if (publication) { settings["semantics"]!["version"] = "2"; settings["ir"]!["version"] = "2"; }
    settings["authorizedExternalSnapshots"] = JsonSerializer.SerializeToNode(new[] { new { snapshotId = Id(suppliedSnapshot), version = "1", digest = external.Digest.Data.Value,
        context = externalContext.Data.Value, coverage = "coglatas.complete", asOf = (string?)null } });
    if (graphDependency is not null) settings["authorizedExternalSnapshots"]!.AsArray().Add(JsonSerializer.SerializeToNode(new
        { snapshotId = Id(231), version = "1", digest = graphDependency.Digest.Data.Value, context = graphDependency.Context.Data.Value, coverage = "coglatas.complete", asOf = (string?)null }));
    if (versionedDependency is not null) settings["authorizedExternalSnapshots"]!.AsArray().Add(JsonSerializer.SerializeToNode(new
        { snapshotId = Id(232), version = "2", digest = versionedDependency.Digest.Data.Value, context = versionedDependency.Context.Data.Value, coverage = "coglatas.complete", asOf = (string?)null }));
    if (additionalVersion) { var alias = settings["authorizedExternalSnapshots"]![0]!.DeepClone(); alias["snapshotId"] = Id(233); settings["authorizedExternalSnapshots"]!.AsArray().Add(alias); }
    var basis = new CompilerInputManifest(ProjectSource.Create(context, template.Source.Documents, Json(new object[] { task, test })), Json(settings));
    if (planTarget) basis = PlanTarget(basis, planBudget);
    IReadOnlyList<CompilerMaterializedSnapshot> snapshots = admission == "missing" ? [] : [new(ExternalSnapshotId.Parse(Id(suppliedSnapshot)), "1", new(external, admission == "available", true, true))];
    if (graphDependency is not null) snapshots = [..snapshots, new(ExternalSnapshotId.Parse(Id(231)), "1", new(graphDependency, true, true, true))];
    if (versionedDependency is not null) snapshots = [..snapshots, new(ExternalSnapshotId.Parse(Id(232)), "2", new(versionedDependency, true, true, true))];
    var model = ProjectSemanticBinder.Bind(basis, snapshots);
    var target = ProjectLocation.Create(DocumentId.Parse(Id(4)), context, entityId: EntityId.Parse(Id(8)));
    var changes = new List<ProjectScenarioOverride> { new(target, ProjectScenarioOverrideKind.TaskInputDefault, Json(afterValue), PortId.Parse(Id(88))) };
    if (!nested) changes.Add(new(target, ProjectScenarioOverrideKind.TaskInputDefault, Json(Boolean(true)), PortId.Parse(Id(89))));
    var plan = ProjectScenarios.Prepare(model, basis.InputDigest, ScenarioId.Parse(Id(revision + 10)), ScenarioRevisionId.Parse(Id(revision + 11)), changes).Plan
        ?? throw new InvalidOperationException("Reference Scenario preparation unavailable.");
    var committed = context.CommittedRevision!;
    var draft = ProjectScenarios.ToDraft(plan, model, basis.InputDigest, new(ProposalId.Parse(Id(revision + 20)), CandidateRevisionId.Parse(Id(revision + 21)), committed, committed)).Plan
        ?? throw new InvalidOperationException("Reference Scenario Draft unavailable.");
    var runs = new[] { basis, plan.Input, draft.After }.Select(input => ProjectTestPlan.Compile(ProjectSemanticBinder.Bind(input, snapshots), SyntheticProjectTestConfiguration.Evaluator).Execute(input.InputDigest).Verdict).ToArray();
    if (admission == "available" ? !runs.SequenceEqual([CompilerVerdict.Fail, CompilerVerdict.Pass, CompilerVerdict.Pass]) : runs.Any(result => result == CompilerVerdict.Pass))
        throw new InvalidOperationException("Reference Scenario verdicts did not preserve actual snapshot admission.");
    if (dependencyConstraint && !(planTarget && planBudget == 200000) && new[] { basis, plan.Input, draft.After }.Any(input =>
        PureCoreCompiler.Compile(input, input.Compiler, snapshots).Result.Verdict != (truth == true ? CompilerVerdict.Pass : truth == false ? CompilerVerdict.Fail : CompilerVerdict.Inconclusive)))
        throw new InvalidOperationException("The actual Compiler predicate/plan result did not match the fixture: " + string.Join(";", new[] { basis, plan.Input, draft.After }.Select(input => { var result = PureCoreCompiler.Compile(input, input.Compiler, snapshots); return result.Result.Verdict + ":ops=" + result.LogicalOperationsConsumed + ":max=" + input.ExecutionProfile.MaximumOperations + ":scope=" + (result.Dependencies?.ScopedIr is not null) + ":" + result.Publication?.PlanNotProducedReason + ":" + string.Join(",", result.Result.Diagnostics.Select(d => d.Code)); })));
    if (graphCycle)
        foreach (var input in new[] { basis, plan.Input, draft.After })
        {
            var compiled = PureCoreCompiler.Compile(input, input.Compiler, snapshots);
            if (compiled.Result.Verdict != CompilerVerdict.Fail ||
                compiled.Dependencies?.Graph?.Findings.Count(finding => finding.Code == "coglatas.relation.dependency-cycle") != 2)
                throw new InvalidOperationException("Both independently owned dependency-cycle edges must fail every actual Compiler result.");
        }
    return new(plan, draft, snapshots);
}

static PreparedTestEditFixture TestEditFixture()
{
    var original = LocalTestInput("tests-fail", 250); var basis = original.Source.Context.CommittedRevision!;
    var proposal = new ProposalContext(ProposalId.Parse(Id(430)), CandidateRevisionId.Parse(Id(431)), basis, basis);
    var before = ProjectDraftRename.Fork(ProjectSemanticBinder.Bind(original), proposal); var model = ProjectSemanticBinder.Bind(before);
    var target = ProjectLocation.Create(DocumentId.Parse(Id(4)), before.Source.Context, entityId: EntityId.Parse(Id(10)));
    var exported = ProjectTestEditing.Export(model, target, before.InputDigest).Definition ?? throw new InvalidOperationException("Test definition export unavailable.");
    var edited = JsonNode.Parse(exported.CanonicalText)!.AsObject(); edited["payload"]!["assertions"]![0]!["expected"]!["value"] = false;
    var imported = Json(edited); var next = SourceRevisionContext.Candidate(new(proposal.ProposalId, CandidateRevisionId.Parse(Id(432)), basis, basis));
    var plan = ProjectTestEditing.Preview(model, target, before.InputDigest, imported, next).Plan ?? throw new InvalidOperationException("Test edit preview unavailable.");
    return new(before, plan.ApplyToDraft(before) ?? throw new InvalidOperationException("Test edit application unavailable."), plan, exported, imported);
}
static CompilerInputManifest LocalTestInput(string name, int revision)
{
    var input = Input(name, revision);
    var booleanType = new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = true };
    var type = booleanType with { kind = name == "tests-error" ? "coglatas.decimal" : "coglatas.boolean" };
    object value = name == "tests-unknown" ? new { type, state = "coglatas.unknown", reason = "example.missing-input", basis = new { } }
        : name == "tests-error" ? new { type, state = "coglatas.known", value = "2" } : new { type, state = "coglatas.known", value = name != "tests-fail" };
    var task = new { entityId = Id(8), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1, displayName = "Local input fixture",
        payload = new { duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "1" },
            ports = new[] { new { portId = Id(88), direction = "coglatas.input", type, minimumConnections = 0, maximumConnections = 1, defaultValue = value } } } };
    var test = new { entityId = Id(10), documentId = Id(4), kind = "coglatas.project-test", schemaVersion = name == "tests-unsupported" ? 2 : 1,
        displayName = "Input assertion", payload = new { assertions = new[] { new { key = "example.ready", kind = "coglatas.assert-boolean", portId = Id(88),
            target = new { tenantId = Id(1), projectId = Id(2), entityId = Id(8), context = input.Source.Context.Data.Value },
            expected = new { type = booleanType, state = "coglatas.known", value = true } } } } };
    var settings = JsonNode.Parse(input.Settings.CanonicalText)!.AsObject();
    if (name != "tests-evaluator-mismatch") settings["evaluator"] = JsonNode.Parse(SyntheticProjectTestConfiguration.Evaluator.Data.CanonicalText);
    var testNode = JsonSerializer.SerializeToNode(test)!.AsObject();
    var assertions = testNode["payload"]!["assertions"]!.AsArray();
    if (name == "tests-unicode") assertions[0]!["key"] = "example.テスト/α~β"; // Intentional Unicode key fixture.
    if (name == "tests-exhausted") { var second = assertions[0]!.DeepClone(); second["key"] = "example.second"; assertions.Add(second); }
    object[] declarations = name == "tests-empty" ? [task] : [task, testNode];
    if (name == "tests-many") declarations = new object[] { task }.Concat(Enumerable.Range(300, 33).Select(number =>
    { var copy = testNode.DeepClone(); copy["entityId"] = Id(number); copy["displayName"] = "Input assertion " + number; return (object)copy; })).ToArray();
    return new(ProjectSource.Create(input.Source.Context, input.Source.Documents, Json(declarations), Json(Array.Empty<object>())), Json(settings));
}
static CompilerInputManifest TestAssertionNavigationInput()
{
    var input = Input("test-assertion-navigation", 170);
    object Test(int identity, string[] keys) => new
    {
        entityId = Id(identity), documentId = Id(4), kind = "coglatas.project-test", schemaVersion = 1,
        displayName = "Local assertion navigation " + identity,
        payload = new { assertions = keys.Select(key => new
        {
            key, kind = "coglatas.assert-boolean",
            target = new { tenantId = Id(1), projectId = Id(2), entityId = Id(99), context = input.Source.Context.Data.Value },
            portId = Id(88),
            expected = new { type = new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = true }
        }).ToArray() }
    };
    // Unicode is intentional assertion-key test data, including RFC 6901 escape characters.
    var declarations = new[] { Test(10, ["example.check", "example.a/b~c", "example.テスト/α~β"]), Test(11, ["example.foreign"]) };
    return new(ProjectSource.Create(input.Source.Context, input.Source.Documents, Json(declarations), Json(Array.Empty<object>())), input.Settings);
}
static CompilerInputManifest RequirementCandidateInput()
{
    var input = Input("requirement-candidates", 160);
    var declarations = new JsonArray(input.Source.Declarations.Value.EnumerateArray()
        .Where(item => item.GetProperty("kind").GetString() is "coglatas.task" or "coglatas.milestone")
        .Select(item => JsonNode.Parse(item.GetRawText())).ToArray());
    foreach (var number in new[] { 11, 12 })
        declarations.Add(JsonSerializer.SerializeToNode(new { entityId = Id(number), documentId = Id(4), kind = "coglatas.requirement", schemaVersion = 1,
            displayName = "Candidate observation tasks " + number, payload = new { target = new { tenantId = Id(1), projectId = Id(2), entityId = Id(6), context = input.Source.Context.Data.Value },
                condition = Literal(true), evidenceKind = "coglatas.model-proof" } }));
    return new(ProjectSource.Create(input.Source.Context, input.Source.Documents, Json(declarations), Json(Array.Empty<object>())), input.Settings);
}
static CompilerInputManifest EnvelopeInput(int revision, string value)
{
    var input = Input("envelope", revision);
    var declarations = Json(input.Source.Declarations.Value.EnumerateArray()
        .Where(item => item.GetProperty("kind").GetString() is "coglatas.task" or "coglatas.milestone").ToArray());
    var source = ProjectSource.Create(input.Source.Context, input.Source.Documents, declarations, Json(Array.Empty<object>()));
    var root = JsonNode.Parse(source.Data.CanonicalText)!.AsObject(); var document = root["documents"]![0]!.AsObject();
    root["future/field~"] = "Source " + value; root["documentId"] = "Opaque Source hint " + value; document["future/field~"] = "Document " + value;
    var digest = document["digest"]!.AsObject(); digest["futureEnvelope"] = "Document Digest " + value;
    document.Remove("digest"); digest["value"] = ContentDigest.Compute(SourceDocument.DigestDomain, Json(document).ToCanonicalBytes()).Value;
    document["digest"] = digest;
    var manifest = root["manifest"]!.AsObject(); manifest["futureEnvelope"] = "Manifest " + value;
    var entry = manifest["documents"]![0]!.AsObject(); entry["futureEnvelope"] = "Manifest entry " + value;
    entry["digest"]!["futureEnvelope"] = "Manifest Digest " + value; entry["digest"]!["value"] = digest["value"]!.DeepClone();
    return new(ProjectSourceCodec.Decode(Json(root).ToCanonicalBytes()).RequireSource(), input.Settings);
}
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

internal sealed record PreparedTestEditFixture(CompilerInputManifest Before, CompilerInputManifest After, ProjectTestEditPlan Plan, SourceJson Exported, SourceJson Imported);

internal sealed record PreparedScenarioFixture(ProjectScenarioPlan Scenario, ProjectScenarioDraftPlan Draft, IReadOnlyList<CompilerMaterializedSnapshot>? Snapshots = null);
