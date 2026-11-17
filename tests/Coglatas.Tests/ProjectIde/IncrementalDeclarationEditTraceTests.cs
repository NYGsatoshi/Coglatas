using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class IncrementalDeclarationEditTraceTests
{
    [Theory]
    [InlineData(1, false)] [InlineData(2, false)] [InlineData(256, false)]
    [InlineData(1, true)] [InlineData(2, true)] [InlineData(256, true)]
    public void TypedEditDeleteAndRestoreTraceKeepsEveryQueryEqualToFullCompilation(int capacity, bool advanceRevision)
    {
        var initial = ReferenceCompletionTests.CompletionInput();
        var original = JsonNode.Parse(initial.Source.Declarations.CanonicalText)!.AsArray();
        var declarations = original.DeepClone().AsArray();
        var tests = new JsonArray();
        for (var ordinal = 0; ordinal < 4; ordinal++) tests.Add(Obj(new
        {
            entityId = Id(20 + ordinal), documentId = Id(4), kind = "coglatas.project-test", schemaVersion = 1,
            payload = new { assertions = new[] { new { key = "example.trace", kind = "coglatas.assert-reference-resolved",
                target = CompilerAnalysisFixtures.Reference(ordinal < 3 ? 10 : 11, initial.Source.Context), portId = Id(ordinal < 3 ? 10000 : 10001) } } }
        }));
        var originalTests = tests.DeepClone().AsArray();
        var cache = new PlanDeclarationReadCache(capacity, 1_048_576);
        CompilerSemanticInspection? retained = null;
        ReferenceCompletionCursor? oldCompletion = null;
        ReferenceSearchCursor? oldReferences = null;
        SourceJson? retainedSource = null, retainedInspection = null;
        string[]? retainedQueries = null;
        ContentDigest? firstInput = null;
        TaskPlanDeclaration? retainedShape = null;
        var expectedCandidates = new[] { 3, 3, 0, 2, 2, 2, 3, 3, 3 };
        for (var step = 0; step < expectedCandidates.Length; step++)
        {
            if (step == 1) declarations[1]!["displayName"] = "Renamed 日本語";
            if (step == 2) declarations[0]!["payload"]!["ports"]![0]!["type"] = Obj(new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = true });
            if (step == 3) { declarations[0] = original[0]!.DeepClone(); declarations.RemoveAt(1); }
            if (step is 4 or 5 or 6) { if (step == 4) declarations.Insert(1, original[1]!.DeepClone()); else declarations[1] = original[1]!.DeepClone(); }
            if (step == 4) declarations[1]!["kind"] = "example.future-task";
            if (step == 5) declarations[1]!["payload"]!["duration"]!["value"] = "invalid-duration";
            if (step == 6)
            {
                declarations[0] = original[0]!.DeepClone();
                tests[3]!["payload"]!["assertions"]![0]!["portId"] = Id(10002);
            }
            if (step == 7) declarations[2]!["payload"]!["ports"] = new JsonArray();
            if (step == 8) { declarations = original.DeepClone().AsArray(); tests = originalTests.DeepClone().AsArray(); }
            var context = advanceRevision ? Source(200 + step).Context : initial.Source.Context;
            for (var ordinal = 0; ordinal < tests.Count; ordinal++)
                tests[ordinal]!["payload"]!["assertions"]![0]!["target"] = CompilerAnalysisFixtures.Reference(ordinal < 3 ? 10 : step is 6 or 7 ? 12 : 11, context);
            var combined = declarations.DeepClone().AsArray(); foreach (var item in tests) combined.Add(item!.DeepClone());
            var input = Input(source: ProjectSource.Create(context, initial.Source.Documents, CompilerContractFixtures.Json(combined)));
            firstInput ??= input.InputDigest;
            var sourceBefore = input.Source.Data;
            var full = PureCoreCompiler.Compile(input, input.Compiler);
            var actual = PureCoreCompiler.Compile(input, input.Compiler, declarationCache: cache);
            Assert.Equal(full.Result.Data, actual.Result.Data); Assert.Equal(full.Result.ResultDigest, actual.Result.ResultDigest);
            Assert.Equal(full.Validation?.Ir is null, actual.Validation?.Ir is null);
            if (full.Validation?.Ir is { } referenceIr)
            {
                var actualIr = actual.Validation!.Ir!;
                Assert.Equal(referenceIr.Data, actualIr.Data); Assert.Equal(referenceIr.SemanticModel, actualIr.SemanticModel); Assert.Equal(referenceIr.SourceMap, actualIr.SourceMap);
            }
            Assert.NotNull(full.Model); Assert.NotNull(actual.Model);
            var expected = new CompilerSemanticInspection(full.Model!); var current = new CompilerSemanticInspection(actual.Model!);
            Assert.Equal(expected.Data, current.Data); Assert.Equal(expected.Digest, current.Digest);
            Assert.Equal(QueryFacts(expected), QueryFacts(current)); Assert.Equal(sourceBefore, input.Source.Data);
            Assert.All(current.Symbols, item => Assert.Equal(context, item.Symbol.Context));
            var origin = Assert.Single(current.Symbols, item => item.Symbol.EntityId == EntityId.Parse(Id(10)) && item.Symbol.PortId == PortId.Parse(Id(10000))).Symbol.Location;
            var completion = current.CompleteEntityReference(origin, input.InputDigest);
            Assert.Equal(step == 2 ? ReferenceCompletionState.Unsupported : ReferenceCompletionState.Ready, completion.State);
            Assert.Equal(expectedCandidates[step], completion.Candidates.Count);
            if (step is 3 or 4 or 5 or 7) Assert.False(current.BindingComplete);
            if (step == 0)
            {
                Assert.True(current.BindingComplete);
                retainedShape = Assert.IsType<TaskPlanDeclaration>(actual.Model!.Declarations.Single(item => item.Declaration?.EntityId == EntityId.Parse(Id(12))).Declaration);
                retained = current; retainedSource = input.Source.Data; retainedInspection = current.Data; retainedQueries = QueryFacts(current);
                oldCompletion = current.CompleteEntityReference(origin, input.InputDigest, 1).NextCursor;
                oldReferences = current.FindReferencesPage(origin, input.InputDigest, 1).NextCursor;
                Assert.NotNull(oldCompletion); Assert.NotNull(oldReferences);
            }
            else
            {
                if (step != 2)
                    Assert.Equal(ReferenceCompletionState.InvalidCursor, current.CompleteEntityReference(origin, input.InputDigest, cursor: oldCompletion).State);
                Assert.Equal(ReferenceSearchState.InvalidCursor, current.FindReferencesPage(origin, input.InputDigest, cursor: oldReferences).State);
                Assert.Equal(retainedSource, retained!.Input.Source.Data); Assert.Equal(retainedInspection, retained.Data); Assert.Equal(retainedQueries, QueryFacts(retained));
            }
            if (step == 8) Assert.Equal(!advanceRevision, firstInput.Equals(input.InputDigest));
            if (capacity == 256 && step != 7)
                Assert.Same(retainedShape, actual.Model!.Declarations.Single(item => item.Declaration?.EntityId == EntityId.Parse(Id(12))).Declaration);
            Assert.InRange(cache.RetainedCount, 0, capacity); Assert.InRange(cache.RetainedCanonicalBytes, 0, 1_048_576);
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(256)]
    public void ActualDraftRenameScenarioAndBranchRoundTripNeverReuseAnotherContextsIndex(int capacity)
    {
        var basis = DraftRenameTests.Committed(); var basisSource = basis.Source.Data;
        var model = ProjectSemanticBinder.Bind(basis); Assert.True(model.BindingComplete);
        var draft = ProjectDraftRename.Fork(model, DraftRenameTests.Proposal(basis));
        var rename = ProjectDraftRename.Preview(ProjectSemanticBinder.Bind(draft), DraftRenameTests.Target(draft), draft.InputDigest,
            "Context trace rename", DraftRenameTests.Next(draft));
        Assert.Equal(DraftRenameState.Ready, rename.State); var renamed = rename.Plan!.ApplyToDraft(draft)!;
        SourceJson Duration(string value) => CompilerContractFixtures.Json(Obj(new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value }));
        ProjectScenarioOverride Change(string value) => new(DraftRenameTests.Target(basis), ProjectScenarioOverrideKind.TaskDuration, Duration(value));
        var prepared = ProjectScenarios.Prepare(model, basis.InputDigest, ScenarioId.Parse(Id(800)), ScenarioRevisionId.Parse(Id(801)), [Change("2")]);
        Assert.Equal(ProjectScenarioState.Ready, prepared.State); var scenario = prepared.Plan!;
        var revised = ProjectScenarios.Revise(scenario, model, basis.InputDigest, ScenarioRevisionId.Parse(Id(802)), [Change("3")]);
        Assert.Equal(ProjectScenarioState.Ready, revised.State);
        ProposalContext Proposal(int id) => new(ProposalId.Parse(Id(id)), CandidateRevisionId.Parse(Id(id + 1)), basis.Source.Context.CommittedRevision!, basis.Source.Context.CommittedRevision!);
        var converted = ProjectScenarios.ToDraft(scenario, model, basis.InputDigest, Proposal(810));
        var convertedRevision = ProjectScenarios.ToDraft(revised.Plan!, model, basis.InputDigest, Proposal(812));
        Assert.Equal(ProjectScenarioState.Ready, converted.State); Assert.Equal(ProjectScenarioState.Ready, convertedRevision.State);
        var foreignContext = SourceRevisionContext.Committed(new(new(basis.Source.TenantId, basis.Source.ProjectId, BranchId.Parse(Id(820))), RevisionId.Parse(Id(821))));
        // The carried reference targets retain their old Branch. This is a negative input, not a Branch migration.
        var foreignInput = Input(source: ProjectSource.Create(foreignContext, basis.Source.Documents, basis.Source.Declarations, basis.Source.Relations,
            packageLock: basis.Source.PackageLock, sourceReferences: basis.Source.SourceReferences));
        var inputs = new[] { basis, draft, renamed, scenario.Input, revised.Plan!.Input, converted.Plan!.After, convertedRevision.Plan!.After,
            basis, draft, scenario.Input, foreignInput, basis };
        var cache = new PlanDeclarationReadCache(capacity, 1_048_576);
        var held = new List<(CompilerSemanticInspection Index, SourceJson Source, SourceJson Inspection, string[] Queries)>();
        ReferenceCompletionCursor? originalCompletion = null; ReferenceSearchCursor? originalReferences = null;
        TaskPlanDeclaration? originalShape = null;
        for (var ordinal = 0; ordinal < inputs.Length; ordinal++)
        {
            var input = inputs[ordinal]; var full = PureCoreCompiler.Compile(input, input.Compiler);
            var actual = PureCoreCompiler.Compile(input, input.Compiler, declarationCache: cache);
            Assert.Equal(full.Result.Data, actual.Result.Data); Assert.Equal(full.Result.ResultDigest, actual.Result.ResultDigest);
            Assert.Equal(full.Validation?.Ir is null, actual.Validation?.Ir is null);
            if (full.Validation?.Ir is { } referenceIr)
            {
                var actualIr = actual.Validation!.Ir!;
                Assert.Equal(referenceIr.Data, actualIr.Data); Assert.Equal(referenceIr.SemanticModel, actualIr.SemanticModel); Assert.Equal(referenceIr.SourceMap, actualIr.SourceMap);
            }
            Assert.NotNull(full.Model); Assert.NotNull(actual.Model);
            var expected = new CompilerSemanticInspection(full.Model!); var current = new CompilerSemanticInspection(actual.Model!);
            Assert.Equal(expected.Data, current.Data); Assert.Equal(expected.Digest, current.Digest); Assert.Equal(QueryFacts(expected), QueryFacts(current));
            Assert.All(current.Symbols, item => Assert.Equal(input.Source.Context, item.Symbol.Context));
            Assert.All(current.References, item => Assert.Equal(input.Source.Context, item.Origin.Context));
            var declaration = Assert.Single(current.Symbols, item => item.Symbol.EntityId == EntityId.Parse(Id(10)) && item.Symbol.PortId is null).Symbol.Location;
            var port = Assert.Single(current.Symbols, item => item.Symbol.EntityId == EntityId.Parse(Id(10)) && item.Symbol.PortId == PortId.Parse(Id(10000))).Symbol.Location;
            var page = current.CompleteEntityReference(port, input.InputDigest, 1);
            Assert.Equal(ReferenceCompletionState.Ready, page.State);
            Assert.Equal(ordinal != 10, current.BindingComplete); Assert.Equal(ordinal != 10, page.CoverageComplete);
            if (ordinal == 0)
            {
                originalCompletion = page.NextCursor; originalReferences = current.FindReferencesPage(declaration, input.InputDigest, 1).NextCursor;
                Assert.NotNull(originalCompletion); Assert.NotNull(originalReferences);
                originalShape = Assert.IsType<TaskPlanDeclaration>(actual.Model!.Declarations.Single(item => item.Declaration?.EntityId == EntityId.Parse(Id(11))).Declaration);
            }
            else
            {
                Assert.Equal(ReferenceCompletionState.InvalidCursor, current.CompleteEntityReference(port, input.InputDigest, cursor: originalCompletion).State);
                Assert.Equal(ReferenceSearchState.InvalidCursor, current.FindReferencesPage(declaration, input.InputDigest, cursor: originalReferences).State);
                if (!input.InputDigest.Equals(basis.InputDigest))
                {
                    Assert.Equal(ReferenceCompletionState.StaleInput, current.CompleteEntityReference(port, basis.InputDigest).State);
                    Assert.Equal(ReferenceSearchState.StaleInput, current.FindReferencesPage(declaration, basis.InputDigest).State);
                }
            }
            if (capacity == 256)
            {
                var currentShape = actual.Model!.Declarations.Single(item => item.Declaration?.EntityId == EntityId.Parse(Id(11))).Declaration;
                if (ordinal < 10) Assert.Same(originalShape, currentShape); else Assert.NotSame(originalShape, currentShape);
            }
            foreach (var previous in held)
            {
                Assert.Equal(previous.Source, previous.Index.Input.Source.Data); Assert.Equal(previous.Inspection, previous.Index.Data);
                Assert.Equal(previous.Queries, QueryFacts(previous.Index));
            }
            held.Add((current, input.Source.Data, current.Data, QueryFacts(current)));
            Assert.Equal(basisSource, basis.Source.Data); Assert.InRange(cache.RetainedCount, 0, capacity);
        }
        Assert.Same(basis, inputs[7]); Assert.Same(draft, inputs[8]); Assert.Same(scenario.Input, inputs[9]); Assert.Same(basis, inputs[11]);
    }

    private static string[] QueryFacts(CompilerSemanticInspection inspection)
    {
        var facts = new List<string>();
        foreach (var symbol in inspection.Symbols)
        {
            var location = symbol.Symbol.Location;
            ReferenceSearchCursor? referenceCursor = null; var referencePages = 0;
            do
            {
                Assert.InRange(++referencePages, 1, 100);
                var page = inspection.FindReferencesPage(location, inspection.Input.InputDigest, 1, referenceCursor);
                facts.Add(SourceJson.Parse(System.Text.Json.JsonSerializer.Serialize(new { query = "references", location = location.Data.Value, state = page.State.ToString(),
                    page.IndexCoverageComplete, page.ScanComplete, page.ScannedReferences,
                    references = page.References.Select(item => new { origin = item.Origin.Data.Value, state = item.State.ToString(), item.SymbolKey }).ToArray() })).CanonicalText);
                referenceCursor = page.NextCursor;
            } while (referenceCursor is not null);
            if (symbol.Symbol.PortId is null) continue;
            ReferenceCompletionCursor? completionCursor = null; var completionPages = 0;
            do
            {
                Assert.InRange(++completionPages, 1, 100);
                var page = inspection.CompleteEntityReference(location, inspection.Input.InputDigest, 1, completionCursor);
                facts.Add(SourceJson.Parse(System.Text.Json.JsonSerializer.Serialize(new { query = "completion", location = location.Data.Value, state = page.State.ToString(),
                    page.ExpectedKind, page.CoverageComplete, page.ScannedSymbols, scanComplete = page.NextCursor is null,
                    candidates = page.Candidates.Select(item => new { item.Symbol.Symbol.Key, item.DisplayName, location = item.Symbol.Symbol.Location.Data.Value }).ToArray() })).CanonicalText);
                completionCursor = page.NextCursor;
            } while (completionCursor is not null);
        }
        return facts.ToArray();
    }
}
