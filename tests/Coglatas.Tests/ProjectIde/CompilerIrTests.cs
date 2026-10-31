using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class CompilerIrTests
{
    private static CompilerSemanticValidation Validate(CompilerInputManifest input, params UnitDefinition[] units) => CompilerSemanticValidation.Validate(ProjectSemanticBinder.Bind(input), units);

    [Fact]
    public void IrIsReproducibleMappedAndBoundToItsExactInput()
    {
        var input = Input(); var a = Validate(input); var b = Validate(input);
        Assert.True(a.StructuralClosureValidated); Assert.Empty(a.Findings);
        Assert.NotNull(a.Ir); Assert.Equal(a.Ir.Digest, b.Ir!.Digest); Assert.Equal(a.Ir.Data, b.Ir.Data);
        var node = Assert.Single(a.Ir.Nodes); Assert.Equal(8, node.NodeId.Value.Version);
        var golden = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ProjectIde", "Fixtures", "compiler-ir-identity-v1.json")))!;
        Assert.Equal(golden["nodeId"]!.GetValue<string>(), node.NodeId.ToString());
        Assert.Equal(golden["derivationDigest"]!.GetValue<string>(), node.NodeId.DerivationDigest.Value);
        Assert.Equal(input.Source.Context, node.Origin.Context); Assert.Equal(EntityId.Parse(Id(6)), node.EntityId);
        Assert.Equal(a.Ir.SourceMapDigest, ContentDigest.Compute("coglatas.semantic-source-map/1", a.Ir.SourceMap.ToCanonicalBytes()));
        Assert.Equal(a.Ir.Digest, ContentDigest.Compute("coglatas.project-ir/1", a.Ir.Data.ToCanonicalBytes()));
        Assert.True(a.LogicalOperationsConsumed > a.Model.LogicalOperationsConsumed);
    }

    [Fact]
    public void NewRevisionRecompilesIrButRetainsScopedSourceNodeIdentity()
    {
        var old = Validate(Input()).Ir!; var next = Validate(Input(source: Source(9))).Ir!;
        Assert.Equal(old.Nodes[0].NodeId, next.Nodes[0].NodeId); Assert.NotEqual(old.Input.InputId, next.Input.InputId);
        Assert.NotEqual(old.Digest, next.Digest); Assert.NotEqual(old.SourceMapDigest, next.SourceMapDigest);
    }

    private static JsonObject Task(int id) => Obj(new { entityId = Id(id), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1, payload = new
    {
        duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "1" }, ports = Array.Empty<object>()
    } });
    private static JsonObject Endpoint(int id, SourceRevisionContext context) => Obj(new { kind = "coglatas.entity", expectedKind = "coglatas.task",
        reference = new { tenantId = context.Branch.TenantId.ToString(), projectId = context.Branch.ProjectId.ToString(), entityId = Id(id), context = context.Data.Value } });
    private static JsonObject Edge(int id, int from, int to, string kind, SourceRevisionContext context) => Obj(new
    {
        relationId = Id(id), documentId = Id(4), kind, schemaVersion = 1, descriptor = new { descriptorId = kind, version = 1 },
        source = Endpoint(from, context), target = Endpoint(to, context),
        payload = kind == RelationKinds.DependsOn ? Obj(new { mode = "coglatas.finish-to-start", lag = new
        { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "0" } }) : new JsonObject()
    });

    [Theory]
    [InlineData(RelationKinds.DependsOn, false)]
    [InlineData(RelationKinds.RelatedTo, true)]
    public void OnlySupportedGraphClosureCanProduceIr(string kind, bool expected)
    {
        var original = Source(); var context = original.Context;
        var source = ProjectSource.Create(context, original.Documents, Json(new JsonArray(Task(7), Task(8))),
            Json(new JsonArray(Edge(20, 7, 8, kind, context), Edge(21, 8, 7, kind, context))));
        var result = Validate(Input(source: source));
        Assert.Equal(expected, result.StructuralClosureValidated);
        if (expected) { Assert.Equal(3, result.Ir!.Nodes.Count); Assert.Equal(2, result.Ir.Relations.Value.GetArrayLength()); }
        else { Assert.Null(result.Ir); Assert.Contains(result.Findings, item => item.Diagnostic.Code == "coglatas.relation.dependency-cycle"); }
    }

    [Fact]
    public void MissingOpaqueAndUnsupportedClosureDoesNotProduceIr()
    {
        var original = Source();
        var root = JsonNode.Parse(original.Documents[0].Root.Data.CanonicalText)!.AsObject(); root["example.future"] = true;
        var source = ProjectSource.Create(original.Context, [SourceDocument.Create(DocumentId.Parse(Id(4)), "project", Json(root))]);
        Assert.Null(Validate(Input(source: source)).Ir);
        var settings = Settings(); settings["ir"]!["version"] = "2";
        Assert.Null(Validate(Input(settings)).Ir);
    }

    private static (CompilerInputManifest Input, UnitDefinition Unit) QuantityInput(bool wrongDimension = false)
    {
        var original = Source(); var task = Task(7);
        var dimension = Obj(new Dictionary<string, int> { ["dimension.mass"] = 1 });
        var type = Obj(new { kind = "coglatas.quantity", schemaVersion = 1, nullable = false, acceptsUnknown = false, unitId = "unit.kg", dimension });
        task["payload"]!["ports"] = new JsonArray(Obj(new { portId = Id(8), direction = "coglatas.input", type, minimumConnections = 0, maximumConnections = 1,
            defaultValue = new { type, state = "coglatas.known", value = new { amount = "1", unitId = "unit.kg", dimension } } }));
        var source = ProjectSource.Create(original.Context, original.Documents, Json(new JsonArray(task)));
        var data = Json(Obj(new { unitId = "unit.kg", version = 1, dimension = wrongDimension ? Obj(new Dictionary<string, int> { ["dimension.length"] = 1 }) : dimension }));
        var unit = new UnitDefinition(data, ContentDigest.Compute("coglatas.unit-definition/1", data.ToCanonicalBytes()));
        var settings = Settings(); settings["units"] = new JsonArray(Obj(new { artifactId = unit.UnitId, version = "1", digest = unit.Digest.Data.Value }));
        return (Input(settings, source), unit);
    }

    [Fact]
    public void UnitMaterializationRequiresExactPinsAndCompatibleDimensions()
    {
        var valid = QuantityInput();
        Assert.NotNull(Validate(valid.Input, valid.Unit).Ir);
        Assert.Contains(Validate(valid.Input).Findings, item => item.Diagnostic.Code == "coglatas.semantic.unit-unavailable");
        var wrong = QuantityInput(true);
        Assert.Contains(Validate(wrong.Input, wrong.Unit).Findings, item => item.Diagnostic.Code == "coglatas.semantic.unit-dimension");
        var unpinned = Input(source: valid.Input.Source);
        Assert.Contains(Validate(unpinned, valid.Unit).Findings, item => item.Diagnostic.Code == "coglatas.semantic.unit-pin");
        Assert.Contains(Validate(valid.Input, valid.Unit, valid.Unit).Findings, item => item.Diagnostic.Code == "coglatas.semantic.unit-ambiguous");
    }

    [Fact]
    public void SemanticBudgetIncludesWorkAlreadyConsumedByBinding()
    {
        var initial = ProjectSemanticBinder.Bind(Input()); var settings = Settings();
        settings["executionProfile"]!["maximumOperations"] = initial.LogicalOperationsConsumed + 1;
        var result = Validate(Input(settings));
        Assert.Null(result.Ir); Assert.Contains(result.Findings, item => item.Diagnostic.Code == "coglatas.semantic.budget-exhausted");
    }

    [Fact]
    public void ResultArtifactsRequireMatchingInputAndAnActualProducingStage()
    {
        var input = Input(); var ir = Validate(input).Ir!;
        var reference = new CompilerArtifactReference("coglatas.ir", "coglatas.project-ir", 1, input.InputDigest, ir.Digest);
        Assert.Throws<ArgumentException>(() => new CompilerResult(input, CompilerExecutionState.Completed, CompilerCoverage.Complete, Passes(input), [], [Stage(input)], true, [reference]));
        var stage = new CompilerStageOutcome("coglatas.ir", 1, CompilerExecutionState.Completed, CompilerCoverage.Complete, input.InputDigest, [ir.Digest]);
        var result = new CompilerResult(input, CompilerExecutionState.Completed, CompilerCoverage.Complete, Passes(input), [], [stage], true, [reference]);
        Assert.Single(result.Artifacts);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => CompilerSemanticValidation.Validate(ProjectSemanticBinder.Bind(input), cancellationToken: cancelled.Token));
    }

    [Fact]
    public void RequiredDescriptorCapabilityCannotBeImplicitlyEnabled()
    {
        var original = Source(); var context = original.Context;
        var source = ProjectSource.Create(context, original.Documents, Json(new JsonArray(Task(7), Task(8))),
            Json(new JsonArray(Edge(20, 7, 8, RelationKinds.DependsOn, context))));
        var settings = Settings(); settings["featureSet"] = new JsonArray("coglatas.core.structural");
        var result = Validate(Input(settings, source));
        Assert.Null(result.Ir); Assert.Contains(result.Findings, item => item.Diagnostic.Code == "coglatas.semantic.capability-unavailable");
    }
}
