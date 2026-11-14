using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using Coglatas.Application.ProjectIde.Core;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class CompilerGraphClosureTests
{
    private sealed record Fixture(CompilerInputManifest Input, CompilerMaterializedSnapshot[] Snapshots, SourceRevisionContext A, SourceRevisionContext B);
    private static object BooleanType() => new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = false };
    private static object Port(int id, bool output, int minimum = 0, int maximum = 1) => new { portId = Id(id),
        direction = output ? "coglatas.output" : "coglatas.input", type = BooleanType(), minimumConnections = minimum, maximumConnections = maximum };
    private static JsonObject Task(params object[] ports) => Obj(new { entityId = Id(7), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1,
        payload = new { duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false },
            state = "coglatas.known", value = "1" }, ports } });
    private static JsonObject Edge(int id, string kind, SourceRevisionContext from, SourceRevisionContext to)
    {
        object Endpoint(SourceRevisionContext context, bool output) => kind == RelationKinds.FlowsTo
            ? new { kind = "coglatas.port", expectedKind = "coglatas.task", reference = CompilerAnalysisFixtures.Reference(7, context), portId = Id(output ? 70 : 71) }
            : new { kind = "coglatas.entity", expectedKind = "coglatas.task", reference = CompilerAnalysisFixtures.Reference(7, context) };
        return Obj(new { relationId = Id(id), documentId = Id(4), kind, schemaVersion = 1,
            descriptor = new { descriptorId = kind, version = 1 }, source = Endpoint(from, true), target = Endpoint(to, false),
            payload = kind == RelationKinds.DependsOn ? Obj(new { mode = "coglatas.finish-to-start",
                lag = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "0" } }) : new JsonObject() });
    }
    private static Fixture Make(string mode, string admission = "available")
    {
        var basis = Source(); var root = basis.Context;
        var a = SourceRevisionContext.Committed(new(new(basis.TenantId, ProjectId.Parse(Id(22)), BranchId.Parse(Id(23))), RevisionId.Parse(Id(24))));
        var b = mode == "versions" ? SourceRevisionContext.Committed(new(a.Branch, RevisionId.Parse(Id(25))))
            : SourceRevisionContext.Committed(new(new(basis.TenantId, ProjectId.Parse(Id(42)), BranchId.Parse(Id(43))), RevisionId.Parse(Id(44))));
        var contexts = new[] { root, a, b };
        var declarations = contexts.Select(_ => new JsonArray(Task())).ToArray();
        var relations = contexts.Select(_ => new JsonArray()).ToArray();
        if (mode is "cycle" or "acyclic" or "versions")
        {
            relations[0].Add(Edge(20, RelationKinds.DependsOn, root, a));
            if (mode == "cycle") { relations[1].Add(Edge(20, RelationKinds.DependsOn, a, root)); relations[1].Add(Edge(21, RelationKinds.DependsOn, a, b)); }
            if (mode == "acyclic") relations[1].Add(Edge(20, RelationKinds.DependsOn, a, b));
            if (mode == "versions") relations[2].Add(Edge(20, RelationKinds.DependsOn, b, root));
        }
        if (mode is "flow-cycle" or "flow-max")
        {
            declarations[0] = new(Task(Port(70, true), Port(71, false)));
            declarations[1] = new(Task(Port(70, true), Port(71, false)));
            relations[0].Add(Edge(20, RelationKinds.FlowsTo, root, a));
            relations[1].Add(Edge(20, RelationKinds.FlowsTo, mode == "flow-cycle" ? a : root, mode == "flow-cycle" ? root : a));
        }
        if (mode is "inbound" or "missing-inbound" or "scoped-port")
        {
            declarations[0] = new(Task(Port(71, false, minimum: 1)));
            declarations[1] = new(Task(mode == "scoped-port" ? Port(71, false, minimum: 1) : Port(70, true)));
            if (mode == "inbound") relations[1].Add(Edge(20, RelationKinds.FlowsTo, a, root));
            if (mode == "scoped-port")
            {
                declarations[2] = new(Task(Port(70, true)));
                relations[2].Add(Edge(20, RelationKinds.FlowsTo, b, root));
            }
        }
        if (mode == "duplicate")
        {
            relations[0].Add(Edge(20, RelationKinds.RelatedTo, root, a));
            relations[1].Add(Edge(20, RelationKinds.RelatedTo, root, a));
        }
        if (mode == "governs")
        {
            object Workflow() => new { entityId = Id(99), documentId = Id(4), kind = "coglatas.workflow", schemaVersion = 1,
                payload = new { states = new[] { Id(84) }, initialState = Id(84), transitions = Array.Empty<object>(), maximumTransitions = 1 } };
            for (var i = 1; i <= 2; i++)
            {
                declarations[i].Add(Obj(Workflow()));
                relations[i].Add(Obj(new { relationId = Id(20), documentId = Id(4), kind = RelationKinds.Governs, schemaVersion = 1,
                    descriptor = new { descriptorId = RelationKinds.Governs, version = 1 },
                    source = new { kind = "coglatas.entity", expectedKind = "coglatas.workflow", reference = CompilerAnalysisFixtures.Reference(99, contexts[i]) },
                    target = new { kind = "coglatas.entity", expectedKind = "coglatas.task", reference = CompilerAnalysisFixtures.Reference(7, root) },
                    payload = new { stateId = Id(84), transitionId = (string?)null, guard = CompilerAnalysisFixtures.Predicate(true) } }));
            }
        }
        if (mode == "relation-owner")
        {
            declarations[1] = new JsonArray();
            var relation = Edge(20, RelationKinds.DependsOn, root, b);
            relation["payload"]!["lag"] = Obj(new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = true },
                state = "coglatas.unknown", reason = "example.pending-lag", basis = new { } });
            relations[1].Add(relation);
        }
        var sources = contexts.Select((context, index) => ProjectSource.Create(context, basis.Documents, Json(declarations[index]), Json(relations[index]))).ToArray();
        var settings = CompilerAnalysisFixtures.AllRulesSettings();
        settings["authorizedExternalSnapshots"] = new JsonArray(sources.Skip(1).Select((source, i) => Obj(new { snapshotId = Id(30 + i), version = i == 0 ? "1" : "2",
            digest = source.Digest.Data.Value, context = source.Context.Data.Value, coverage = "coglatas.complete", asOf = (string?)null })).ToArray());
        var supplied = sources.Skip(1).Select((source, i) => new CompilerMaterializedSnapshot(ExternalSnapshotId.Parse(Id(30 + i)), i == 0 ? "1" : "2",
            new(source, i != 0 || admission != "denied", i != 0 || admission != "partial-declarations", i != 0 || admission != "partial-relations")))
            .Where((_, i) => i != 0 || admission != "missing").ToArray();
        return new(new(sources[0], Json(settings)), supplied, a, b);
    }
    private static CoreCompilation Compile(Fixture fixture, PlanDeclarationReadCache? cache = null) =>
        PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots, declarationCache: cache);

    [Theory]
    [InlineData("cycle", "coglatas.relation.dependency-cycle")]
    [InlineData("flow-cycle", "coglatas.relation.dataflow-cycle")]
    public void CyclesAcrossSourceOwnersFailTheActualCompilerWithoutMarkingADownstreamTail(string mode, string code)
    {
        var fixture = Make(mode); var compiled = Compile(fixture);
        Assert.Equal(CompilerVerdict.Fail, compiled.Result.Verdict);
        var graph = compiled.Dependencies!.Graph!; Assert.Equal(CompilerGraphClosureState.Evaluated, graph.State); Assert.False(graph.StructuralValidated);
        var cycles = graph.Findings.Where(finding => finding.Code == code).ToArray(); Assert.Equal(2, cycles.Length);
        Assert.All(cycles, finding => Assert.Equal(RelationId.Parse(Id(20)), finding.Location.RelationId));
        Assert.Contains(cycles, finding => finding.Location.Context.Equals(fixture.Input.Source.Context));
        Assert.Contains(cycles, finding => finding.Location.Context.Equals(fixture.A));
        Assert.DoesNotContain(cycles, finding => finding.Location.RelationId == RelationId.Parse(Id(21)));
        Assert.Contains(compiled.Dependencies.KnownFailures, failure => failure.Diagnostic?.Code == code);
        Assert.Null(compiled.Validation!.Ir); Assert.Empty(compiled.Result.Artifacts);
    }
    [Theory]
    [InlineData("acyclic")] [InlineData("versions")]
    public void ExactContextsKeepAcyclicGraphsAndTwoVersionsSeparate(string mode)
    {
        var fixture = Make(mode); var compiled = Compile(fixture); var graph = compiled.Dependencies!.Graph!;
        Assert.True(graph.StructuralValidated); Assert.DoesNotContain(graph.Findings, finding => finding.Code.Contains("cycle", StringComparison.Ordinal));
        Assert.Equal(3, compiled.Dependencies.Binding.Regions.Count);
        if (mode == "versions")
        {
            var versions = compiled.Dependencies.Regions.Where(region => region.Binding.Source.ProjectId == fixture.A.Branch.ProjectId).ToArray();
            Assert.Equal(2, versions.Length); Assert.Equal(versions[0].Validation!.Ir!.Nodes.Select(node => node.NodeId), versions[1].Validation!.Ir!.Nodes.Select(node => node.NodeId));
            Assert.NotEqual(versions[0].Validation!.Ir!.SourceMapDigest, versions[1].Validation!.Ir!.SourceMapDigest);
        }
        Assert.Equal(CompilerVerdict.Inconclusive, compiled.Result.Verdict); Assert.Null(compiled.Validation!.Ir);
    }
    [Fact]
    public void ADeclaredIncomingEdgeSatisfiesTheRootPortEvenWithoutAnOutgoingRootReference()
    {
        var fixture = Make("inbound"); var compiled = Compile(fixture);
        Assert.Empty(ProjectSemanticBinder.Bind(fixture.Input, fixture.Snapshots).References);
        Assert.Equal(CompilerVerdict.Inconclusive, compiled.Result.Verdict); var analysis = compiled.Dependencies!;
        Assert.True(analysis.Binding.IncludesDeclaredSnapshots); Assert.True(analysis.Graph!.StructuralValidated);
        Assert.DoesNotContain(analysis.Graph.Findings, finding => finding.Code == "coglatas.relation.required-port");
        Assert.DoesNotContain(compiled.Result.Diagnostics, diagnostic => diagnostic.Code == "coglatas.relation.required-port");
        Assert.NotNull(analysis.Regions.Single(region => region.Binding.IsRoot).Validation!.Ir);
        Assert.Null(compiled.Validation!.Ir);
    }
    [Fact]
    public void ARequiredPortIsInvalidOnlyAfterTheWholeDeclaredScopeHasBeenChecked()
    {
        var fixture = Make("missing-inbound"); var compiled = Compile(fixture);
        Assert.Equal(CompilerVerdict.Fail, compiled.Result.Verdict);
        var finding = Assert.Single(compiled.Dependencies!.Graph!.Findings, finding => finding.Code == "coglatas.relation.required-port");
        Assert.Equal(RelationFindingState.Invalid, finding.State); Assert.Equal(fixture.Input.Source.Context, finding.Location.Context);
        Assert.Equal("/payload/ports/" + Id(71), finding.Location.PropertyPath);
    }
    [Theory]
    [InlineData("missing")] [InlineData("denied")] [InlineData("partial-declarations")] [InlineData("partial-relations")]
    public void AnUnavailableDeclaredOwnerCannotTurnUnobservedDemandIntoZero(string admission)
    {
        var fixture = Make("inbound", admission); var compiled = Compile(fixture);
        Assert.Equal(CompilerVerdict.Inconclusive, compiled.Result.Verdict);
        Assert.Null(compiled.Dependencies?.Graph);
        Assert.DoesNotContain(compiled.Result.Diagnostics, diagnostic => diagnostic.Code == "coglatas.relation.required-port" && diagnostic.Evidence.Kind == CompilerEvidenceKind.ModelProof);
    }
    [Fact]
    public void RepeatedPortAndEntityIdsDoNotBorrowAnotherScopesIncomingConnection()
    {
        var fixture = Make("scoped-port"); var compiled = Compile(fixture); Assert.Equal(CompilerVerdict.Fail, compiled.Result.Verdict);
        var missing = Assert.Single(compiled.Dependencies!.Graph!.Findings, finding => finding.Code == "coglatas.relation.required-port");
        Assert.Equal(fixture.A, missing.Location.Context); Assert.NotEqual(fixture.Input.Source.Context, missing.Location.Context);
    }
    [Theory]
    [InlineData("duplicate")] [InlineData("flow-max")]
    public void CrossOwnerDuplicatesRetainBothOwnersAndPortMaximumsUseTheUnion(string mode)
    {
        var fixture = Make(mode); var compiled = Compile(fixture); Assert.Equal(CompilerVerdict.Fail, compiled.Result.Verdict);
        var graph = compiled.Dependencies!.Graph!;
        Assert.Equal(2, graph.Findings.Count(finding => finding.Code == "coglatas.relation.duplicate"));
        if (mode == "flow-max")
        {
            var cardinalities = graph.Findings.Where(finding => finding.Code == "coglatas.relation.port-cardinality").ToArray();
            Assert.Equal(2, cardinalities.Length); Assert.Equal(2, cardinalities.Select(finding => finding.Location.Context).Distinct().Count());
        }
    }
    [Fact]
    public void GovernsCardinalityCountsEdgesOwnedByDifferentSources()
    {
        var fixture = Make("governs"); var compiled = Compile(fixture);
        Assert.Equal(CompilerVerdict.Fail, compiled.Result.Verdict);
        var findings = compiled.Dependencies!.Graph!.Findings.Where(finding => finding.Code == "coglatas.relation.workflow-cardinality").ToArray();
        Assert.Equal(2, findings.Length); Assert.Equal(2, findings.Select(finding => finding.Location.Context).Distinct().Count());
        Assert.All(findings, finding => Assert.Equal(RelationFindingState.Invalid, finding.State));
    }
    [Fact]
    public void CacheSupplyOrderAndOwnedCollectionsCannotChangeTheGraphReceipt()
    {
        var fixture = Make("cycle"); var direct = Compile(fixture); var cache = new PlanDeclarationReadCache();
        var cached = Compile(fixture with { Snapshots = fixture.Snapshots.Reverse().ToArray() }, cache);
        Assert.Equal(direct.Result.Data, cached.Result.Data); Assert.Equal(direct.Dependencies!.Graph!.Data, cached.Dependencies!.Graph!.Data);
        var copy = direct.Dependencies.Graph.Findings.ToArray(); Array.Clear(copy); Assert.True(direct.Dependencies.Graph.Findings.Count >= 2);
        Assert.False(direct.Dependencies.Graph.Findings is RelationFinding[]);
        Assert.Equal(ContentDigest.Compute(CompilerGraphClosure.Format, direct.Dependencies.Graph.Data.ToCanonicalBytes()), direct.Dependencies.Graph.Digest);
    }
    [Fact]
    public void WholeGraphWorkConsumesTheSameRootBudgetAndCancellationDropsTheReceipt()
    {
        var fixture = Make("cycle"); var full = Compile(fixture); var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject();
        settings["executionProfile"]!["maximumOperations"] = full.Dependencies!.Binding.LogicalOperationsConsumed + full.Dependencies.Graph!.LogicalOperationsConsumed - 1;
        var limited = Compile(fixture with { Input = new(fixture.Input.Source, Json(settings)) });
        Assert.Equal(CompilerExecutionState.ResourceExhausted, limited.Result.Execution);
        Assert.Equal(CompilerGraphClosureState.ResourceLimit, limited.Dependencies!.Graph!.State); Assert.False(limited.Dependencies.Graph.StructuralValidated);
        Assert.True(limited.LogicalOperationsConsumed <= limited.Input.ExecutionProfile.MaximumOperations);
        Assert.NotEqual(CompilerVerdict.Pass, limited.Result.Verdict);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var cancelled = PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots, cancellationToken: cancellation.Token);
        Assert.Equal(CompilerExecutionState.Cancelled, cancelled.Result.Execution); Assert.Null(cancelled.Dependencies);
    }
    [Fact]
    public async Task ActualHostUsesTheSameWholeGraphFailureAndExactOwnerLocations()
    {
        var fixture = Make("cycle");
        CompilerArtifactIdentity Artifact(string key) => new(SourceJson.Parse(fixture.Input.Settings.Value.GetProperty(key).GetRawText()));
        var host = new LocalNetCompilerHost(new(fixture.Input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            fixture.Input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(value => value.GetString())));
        var response = await host.CompileAsync(new(new(fixture.Input), 1, fixture.Snapshots)); Assert.Equal(CompilerHostStatus.Completed, response.Status);
        var direct = Compile(fixture); Assert.Equal(direct.Result.Data, response.Compilation!.Result.Data);
        Assert.Equal(direct.Dependencies!.Graph!.Data, response.Compilation.Dependencies!.Graph!.Data);
    }

    [Fact]
    public void ADeclaredOwnerCannotBeSkippedMerelyBecauseNoOutgoingReferenceReachesIt()
    {
        var fixture = Make("inbound");
        var missing = fixture with { Snapshots = fixture.Snapshots.Where(snapshot => !snapshot.Snapshot.Source.Context.Equals(fixture.B)).ToArray() };
        var root = ProjectSemanticBinder.Bind(missing.Input, missing.Snapshots); Assert.True(root.BindingComplete);
        Assert.Equal(CompilerBindingClosureState.Complete, CompilerBindingClosure.Build(root, missing.Input.InputDigest).State);
        var compiled = Compile(missing); Assert.Equal(CompilerVerdict.Inconclusive, compiled.Result.Verdict);
        Assert.Equal(CompilerBindingClosureState.Incomplete, compiled.Dependencies!.Binding.State);
        Assert.True(compiled.Dependencies.Binding.IncludesDeclaredSnapshots); Assert.Null(compiled.Dependencies.Graph);
    }

    public static IEnumerable<object[]> BrowserGraphCases()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "frontend", "public", "assets", "projectide", "graph-closure-v1.json"))) directory = directory.Parent;
        using var data = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory!.FullName, "frontend", "public", "assets", "projectide", "graph-closure-v1.json")));
        Assert.Equal(3, data.RootElement.GetProperty("cases").GetArrayLength());
        foreach (var row in data.RootElement.GetProperty("cases").EnumerateArray()) yield return [row.GetRawText()];
    }
    [Theory]
    [MemberData(nameof(BrowserGraphCases))]
    public async Task ActualCoreGraphFixtureAndWindowsViewPreserveGraphFailureApartFromPassingLocalTests(string fixture)
    {
        await new CompilerDependencyAnalysisTests().ActualCoreFixtureAndWindowsPureViewPreserveDependencyFailureApartFromLocalTestPass(fixture);
        using var document = System.Text.Json.JsonDocument.Parse(fixture); var row = document.RootElement;
        var source = ProjectSourceCodec.Decode(System.Text.Encoding.UTF8.GetBytes(row.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(row.GetProperty("manifest").GetRawText()));
        var compiled = PureCoreCompiler.Compile(input, input.Compiler, WebPreviewContractTests.Snapshots(row, input));
        Assert.Equal(CompilerGraphClosureState.Evaluated, compiled.Dependencies!.Graph!.State);
        var cycle = compiled.Dependencies.Graph.Findings.Where(finding => finding.Code == "coglatas.relation.dependency-cycle").ToArray();
        Assert.Equal(2, cycle.Length); Assert.Equal(2, cycle.Select(finding => finding.Location.Context).Distinct().Count());
        Assert.All(cycle, finding => Assert.NotEqual(input.Source.Context, finding.Location.Context));
    }

    [Fact]
    public void PartialRootCoverageCannotReachWholeGraphValidationOrProveAnAbsentPortInvalid()
    {
        var fixture = Make("missing-inbound"); var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject();
        settings["sourceCoverage"] = "coglatas.partial";
        var partial = fixture with { Input = new(fixture.Input.Source, Json(settings)) };
        var model = ProjectSemanticBinder.Bind(partial.Input, partial.Snapshots);
        Assert.False(model.BindingComplete); Assert.False(model.Input.SourceCoverageComplete);
        Assert.Contains(model.Findings, finding => finding.Diagnostic.Code == "coglatas.binding.incomplete-source");
        Assert.Equal(CompilerBindingClosureState.Incomplete, CompilerBindingClosure.Build(model, partial.Input.InputDigest).State);
        var compiled = Compile(partial); Assert.Null(compiled.Dependencies);
        Assert.Equal(CompilerVerdict.Inconclusive, compiled.Result.Verdict);
        Assert.DoesNotContain(compiled.Result.Diagnostics, diagnostic => diagnostic.Code == "coglatas.relation.required-port" && diagnostic.Evidence.Kind == CompilerEvidenceKind.ModelProof);
        Assert.Equal(CompilerVerdict.Fail, Compile(fixture).Result.Verdict);
    }
    [Fact]
    public void ARelationOwnerWithoutExplicitDeclarationsStillHasItsProjectRootAndImportsOwnedFindings()
    {
        var fixture = Make("relation-owner");
        var owner = fixture.Snapshots.Single(snapshot => snapshot.Snapshot.Source.Context.Equals(fixture.A)).Snapshot.Source;
        Assert.Equal(0, owner.Declarations.Value.GetArrayLength());
        var compiled = Compile(fixture); var region = compiled.Dependencies!.Regions.Single(item => item.Binding.Source.Context.Equals(fixture.A));
        var project = Assert.Single(region.Binding.Declarations); Assert.IsType<ProjectPlanDeclaration>(project.Declaration);
        Assert.Contains(compiled.Dependencies.Graph!.Findings, finding => finding.Code == "coglatas.relation.unknown-lag" && finding.Location.Context.Equals(fixture.A));
        Assert.Contains(region.Validation!.Findings, finding => finding.Diagnostic.Code == "coglatas.relation.unknown-lag" && finding.Diagnostic.Location.Context.Equals(fixture.A));
        Assert.False(region.Validation.StructuralClosureValidated); Assert.Null(region.Validation.Ir);
        Assert.Equal(CompilerVerdict.Inconclusive, compiled.Result.Verdict);
        Assert.Equal(compiled.Result.Data, Compile(fixture, new PlanDeclarationReadCache()).Result.Data);
    }
}
