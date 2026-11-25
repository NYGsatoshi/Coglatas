using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class TaskDependencyReachabilityTests
{
    [Fact]
    public void TraversesDependentsWithDeterministicShortestReasonsAndIgnoresAssociations()
    {
        var input = Diamond();
        var compilation = Compile(input);
        Assert.Null(compilation.Publication?.ExecutionPlan);
        var result = ProjectTaskDependencyReachability.Read(compilation, input.InputDigest, At(input, 10));

        Assert.Equal(TaskDependencyReachabilityState.Complete, result.State);
        Assert.Equal(input.InputDigest, result.InputDigest);
        Assert.Equal(compilation.Dependencies!.Graph!.Digest, result.GraphDigest);
        Assert.Equal(At(input, 10), result.Anchor);
        Assert.Equal(new[] { Id(11), Id(12), Id(13), Id(14) }, result.Tasks.Select(row => row.Task.EntityId!.Value.ToString()));
        Assert.Equal(new[] { 1, 1, 2, 3 }, result.Tasks.Select(row => row.Depth));
        Assert.Equal(new[] { Id(31), Id(33), Id(35) }, result.Tasks[^1].Relations.Select(location => location.RelationId!.Value.ToString()));
        Assert.All(result.Tasks.SelectMany(row => row.Relations), location => Assert.Equal(input.Source.Context, location.Context));
        Assert.Empty(ProjectTaskDependencyReachability.Read(compilation, input.InputDigest, At(input, 14)).Tasks);

        var reversed = Diamond(reverse: true);
        var reordered = ProjectTaskDependencyReachability.Read(Compile(reversed), reversed.InputDigest, At(reversed, 10));
        Assert.Equal(result.Tasks.Select(row => row.Task.Data), reordered.Tasks.Select(row => row.Task.Data));
        Assert.Equal(result.Tasks.SelectMany(row => row.Relations.Select(location => location.Data)),
            reordered.Tasks.SelectMany(row => row.Relations.Select(location => location.Data)));
    }

    [Fact]
    public void DepthAndWorkLimitsDistinguishIncompleteTraversalFromAnEmptyCompleteResult()
    {
        var input = Diamond();
        var compilation = Compile(input);
        var anchor = At(input, 10);
        var full = ProjectTaskDependencyReachability.Read(compilation, input.InputDigest, anchor);
        var atAnchor = ProjectTaskDependencyReachability.Read(compilation, input.InputDigest, anchor, maximumDepth: 0);
        var atLeaves = ProjectTaskDependencyReachability.Read(compilation, input.InputDigest, anchor, maximumDepth: 3);
        var direct = ProjectTaskDependencyReachability.Read(compilation, input.InputDigest, anchor, maximumDepth: 1);
        var leaf = ProjectTaskDependencyReachability.Read(compilation, input.InputDigest, At(input, 14), maximumDepth: 0);
        var limitedBeforeIndex = ProjectTaskDependencyReachability.Read(compilation, input.InputDigest, anchor, maximumOperations: 1);
        var regions = compilation.Dependencies!.Graph!.Binding.Regions;
        var indexOperations = regions.Sum(region => 1 + region.Declarations.Count + region.Relations.Count);
        var afterFirstPath = ProjectTaskDependencyReachability.Read(compilation, input.InputDigest, anchor, maximumOperations: indexOperations + 3);

        Assert.Equal(TaskDependencyReachabilityState.DepthLimit, atAnchor.State);
        Assert.Empty(atAnchor.Tasks);
        Assert.Equal(TaskDependencyReachabilityState.DepthLimit, direct.State);
        Assert.Equal(2, direct.Tasks.Count);
        Assert.All(direct.Tasks, row => Assert.Equal(1, row.Depth));
        Assert.Equal(TaskDependencyReachabilityState.Complete, atLeaves.State);
        Assert.Equal(full.Tasks.Count, atLeaves.Tasks.Count);
        Assert.Equal(TaskDependencyReachabilityState.Complete, leaf.State);
        Assert.Empty(leaf.Tasks);
        Assert.Equal(TaskDependencyReachabilityState.OperationLimit, limitedBeforeIndex.State);
        Assert.Empty(limitedBeforeIndex.Tasks);
        Assert.Null(limitedBeforeIndex.Anchor);
        Assert.Equal(TaskDependencyReachabilityState.OperationLimit, afterFirstPath.State);
        Assert.Equal(At(input, 11), Assert.Single(afterFirstPath.Tasks).Task);
        Assert.Equal(indexOperations + 3, afterFirstPath.LogicalOperationsConsumed);
    }

    [Fact]
    public void StaleInputsAndUnadmittedLocationsDoNotReturnPathsOrBasis()
    {
        var input = Diamond();
        var compilation = Compile(input);
        var stale = ProjectTaskDependencyReachability.Read(compilation, ContentDigest.Compute("example.foreign-input", []), At(input, 10));
        Assert.Equal(TaskDependencyReachabilityState.StaleInput, stale.State);
        Assert.Null(stale.GraphDigest);
        Assert.Null(stale.InputDigest);
        Assert.Null(stale.Anchor);
        Assert.Empty(stale.Tasks);
        var other = Source().Context.Branch;
        var revision = SourceRevisionContext.Committed(new(other, RevisionId.Parse(Id(99))));
        foreach (var anchor in new[] { ProjectLocation.Create(DocumentId.Parse(Id(4)), revision, entityId: EntityId.Parse(Id(10))),
            ProjectLocation.Create(DocumentId.Parse(Id(99)), input.Source.Context, entityId: EntityId.Parse(Id(10))), At(input, 90) })
        {
            var refused = ProjectTaskDependencyReachability.Read(compilation, input.InputDigest, anchor);
            Assert.Equal(TaskDependencyReachabilityState.Unavailable, refused.State);
            Assert.Null(refused.GraphDigest);
            Assert.Empty(refused.Tasks);
        }

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectTaskDependencyReachability.Read(compilation, input.InputDigest,
            At(input, 10), cancellationToken: canceled.Token));
    }

    [Fact]
    public void BaseAndActualScenarioUseTheSameTraversalWithTheirOwnLocationsAndInputIdentity()
    {
        var input = Diamond();
        var basis = Compile(input);
        var model = Assert.IsType<PartialSemanticModel>(basis.Model);
        var preview = ProjectScenarios.Prepare(model, input.InputDigest, ScenarioId.Parse(Id(90)), ScenarioRevisionId.Parse(Id(91)),
            [new(At(input, 10), ProjectScenarioOverrideKind.TaskDuration, Json(Duration("9")))]);
        Assert.Equal(ProjectScenarioState.Ready, preview.State);
        var scenario = Assert.IsType<ProjectScenarioPlan>(preview.Plan).Input;
        var compiled = Compile(scenario);
        var baseline = ProjectTaskDependencyReachability.Read(basis, input.InputDigest, At(input, 10));
        var hypothesis = ProjectTaskDependencyReachability.Read(compiled, scenario.InputDigest, At(scenario, 10));
        Assert.Equal(TaskDependencyReachabilityState.Complete, hypothesis.State);
        Assert.NotEqual(baseline.InputDigest, hypothesis.InputDigest);
        Assert.NotEqual(baseline.GraphDigest, hypothesis.GraphDigest);
        Assert.Equal(baseline.Tasks.Select(row => row.Task.EntityId), hypothesis.Tasks.Select(row => row.Task.EntityId));
        Assert.Equal(baseline.Tasks.Select(row => row.Depth), hypothesis.Tasks.Select(row => row.Depth));
        Assert.All(hypothesis.Tasks, row =>
        {
            Assert.Equal(scenario.Source.Context, row.Task.Context);
            Assert.All(row.Relations, location => Assert.Equal(scenario.Source.Context, location.Context));
        });
        Assert.Equal(TaskDependencyReachabilityState.StaleInput,
            ProjectTaskDependencyReachability.Read(compiled, input.InputDigest, At(input, 10)).State);
        Assert.Equal(TaskDependencyReachabilityState.Unavailable,
            ProjectTaskDependencyReachability.Read(compiled, scenario.InputDigest, At(input, 10)).State);
    }

    private static CompilerInputManifest Diamond(bool reverse = false)
    {
        var basis = Source();
        var tasks = Enumerable.Range(10, 6).Select(id => Obj(new { entityId = Id(id), documentId = Id(4), kind = "coglatas.task",
            schemaVersion = 1, payload = new { duration = Duration("1"), ports = Array.Empty<object>() } }));
        JsonObject Edge(int id, int from, int to, string kind = RelationKinds.DependsOn)
        {
            object Endpoint(int entity) => new { kind = "coglatas.entity", expectedKind = "coglatas.task",
                reference = CompilerAnalysisFixtures.Reference(entity, basis.Context) };
            return Obj(new { relationId = Id(id), documentId = Id(4), kind, schemaVersion = 1, descriptor = new { descriptorId = kind, version = 1 },
                source = Endpoint(from), target = Endpoint(to), payload = kind == RelationKinds.DependsOn
                    ? Obj(new { mode = "coglatas.finish-to-start", lag = Duration("0") }) : new JsonObject() });
        }

        JsonObject[] edges = [Edge(31, 11, 10), Edge(32, 12, 10), Edge(33, 13, 11), Edge(34, 13, 12), Edge(35, 14, 13),
            Edge(36, 15, 10, RelationKinds.RelatedTo)];
        var ordered = reverse ? edges.Reverse() : edges;
        var source = ProjectSource.Create(basis.Context, basis.Documents,
            Json(new JsonArray(tasks.Cast<JsonNode>().ToArray())), Json(new JsonArray(ordered.Cast<JsonNode>().ToArray())));
        return CompilerScopedPublicationTests.Upgrade(Input(source: source));
    }

    private static CoreCompilation Compile(CompilerInputManifest input) => PureCoreCompiler.Compile(input, input.Compiler);
    private static ProjectLocation At(CompilerInputManifest input, int id) =>
        ProjectLocation.Create(DocumentId.Parse(Id(4)), input.Source.Context, entityId: EntityId.Parse(Id(id)));
    private static JsonObject Duration(string ticks) => Obj(new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1,
        nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = ticks });
}
