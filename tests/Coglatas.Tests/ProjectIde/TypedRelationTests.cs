using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using Coglatas.Application.ProjectIde.Core;

namespace Coglatas.Tests.ProjectIde;

public sealed class TypedRelationTests
{
    private static string Id(int number) => new Guid(number, 0, 0x4000, 0x80, 0, 0, 0, 0, 0, 0, 1).ToString();
    private static readonly DocumentId Document = DocumentId.Parse(Id(90));
    private static SourceJson Json(JsonNode data) => SourceJson.Parse(data.ToJsonString());
    private static JsonObject Obj(object value) => JsonSerializer.SerializeToNode(value)!.AsObject();
    private static SourceRevisionContext Context(int project = 92, int revision = 94, int tenant = 91, int branch = 93) =>
        SourceRevisionContext.Committed(new(new(TenantId.Parse(Id(tenant)), ProjectId.Parse(Id(project)), BranchId.Parse(Id(branch))), RevisionId.Parse(Id(revision))));
    private static JsonObject Type(string kind = "coglatas.text", bool nullable = false, bool acceptsUnknown = false) =>
        Obj(new { kind, schemaVersion = 1, nullable, acceptsUnknown });
    private static JsonObject Known(string kind, object value) => Obj(new { type = Type(kind), state = "coglatas.known", value });
    private static JsonObject Predicate() => Obj(new { kind = "coglatas.literal", value = Known("coglatas.boolean", true) });
    private static JsonObject Task(int id, bool required = false) => Declaration(id, "coglatas.task", new JsonObject
    {
        ["duration"] = Known("coglatas.elapsed-duration", "10"),
        ["ports"] = new JsonArray(Port(80, "coglatas.output"), Port(81, "coglatas.input", required ? 1 : 0))
    });
    private static JsonObject Port(int id, string direction, int minimum = 0) => Obj(new
    { portId = Id(id), direction, type = Type(), minimumConnections = minimum, maximumConnections = direction == "coglatas.input" ? (int?)1 : null });
    private static JsonObject Declaration(int id, string kind, JsonObject payload) => Obj(new
    { entityId = Id(id), documentId = Document.ToString(), kind, schemaVersion = 1, payload });
    private static JsonObject Endpoint(int entity, SourceRevisionContext? context = null, string kind = "coglatas.entity", string expectedKind = "coglatas.task", int? port = null)
    {
        context ??= Context();
        var result = new JsonObject
        {
            ["kind"] = kind, ["expectedKind"] = expectedKind,
            ["reference"] = Obj(new { tenantId = context.Branch.TenantId.ToString(), projectId = context.Branch.ProjectId.ToString(), entityId = Id(entity), context = context.Data.Value })
        };
        if (port is not null) result["portId"] = Id(port.Value);
        return result;
    }
    private static JsonObject Edge(int id = 50, int from = 1, int to = 2, string kind = RelationKinds.DependsOn)
    {
        var ports = kind == RelationKinds.FlowsTo;
        return Obj(new
        {
            relationId = Id(id), documentId = Document.ToString(), kind, schemaVersion = 1,
            descriptor = new { descriptorId = kind, version = 1 },
            source = Endpoint(from, kind: ports ? "coglatas.port" : "coglatas.entity", port: ports ? 80 : null),
            target = Endpoint(to, kind: ports ? "coglatas.port" : "coglatas.entity", port: ports ? 81 : null),
            payload = kind == RelationKinds.DependsOn ? Obj(new { mode = "coglatas.finish-to-start", lag = Known("coglatas.elapsed-duration", "0") }) : new JsonObject()
        });
    }
    private static ProjectSource Source(IEnumerable<JsonObject>? edges = null, IEnumerable<JsonObject>? declarations = null, SourceRevisionContext? context = null)
    {
        var root = Obj(new { entityId = Id(99), kind = "coglatas.project", schemaVersion = 1, payload = new { } });
        return ProjectSource.Create(context ?? Context(), [SourceDocument.Create(Document, "project", Json(root))],
            SourceJson.Parse(JsonSerializer.Serialize(declarations ?? [Task(1), Task(2), Task(3)])),
            SourceJson.Parse(JsonSerializer.Serialize(edges ?? [])));
    }
    private static RelationValidationResult Validate(IEnumerable<JsonObject> edges, IEnumerable<JsonObject>? declarations = null, bool complete = true,
        IEnumerable<RelationScopeSnapshot>? snapshots = null, IEnumerable<UnitDefinition>? units = null) =>
        RelationGraphValidator.Validate(new(Source(edges, declarations), true, complete, complete), snapshots, units);
    private static void Has(RelationValidationResult result, string suffix, RelationFindingState? state = null) =>
        Assert.Contains(result.Findings, item => item.Code == "coglatas.relation." + suffix && (state is null || item.State == state));

    public static IEnumerable<object[]> Cases()
    {
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ProjectIde", "Fixtures", "relation-cases-v1.json")));
        foreach (var item in data.RootElement.GetProperty("cases").EnumerateArray()) yield return [item.GetRawText()];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void VersionedGraphFixturesDistinguishKindsAndInvariants(string fixture)
    {
        using var document = JsonDocument.Parse(fixture);
        var test = document.RootElement;
        var kind = test.GetProperty("kind").GetString()!;
        var edges = test.GetProperty("edges").EnumerateArray().Select((pair, index) => Edge(50 + index, pair[0].GetInt32(), pair[1].GetInt32(), kind));
        var result = Validate(edges);
        var code = test.GetProperty("code").GetString();
        if (code is null) Assert.Empty(result.Findings);
        else Has(result, code);
    }

    [Fact]
    public void DescriptorsHaveVersionedCapabilitiesScopesAndCanonicalIdentity()
    {
        Assert.Equal(10, RelationKindDescriptor.BuiltIns.Count);
        Assert.Equal(10, RelationKindDescriptor.BuiltIns.Select(item => item.DescriptorId).Distinct().Count());
        foreach (var descriptor in RelationKindDescriptor.BuiltIns)
        {
            Assert.NotEmpty(descriptor.RequiredCapabilities); Assert.NotEmpty(descriptor.Invariants); Assert.NotEmpty(descriptor.AllowedScopes);
            Assert.Equal(1, descriptor.SourceEndpointCount); Assert.Equal(1, descriptor.TargetEndpointCount);
            Assert.Equal(descriptor.Digest, ContentDigest.Compute("coglatas.relation-descriptor/1", descriptor.Data.ToCanonicalBytes()));
            Assert.Same(descriptor, RelationKindDescriptor.Find(descriptor.DescriptorId, 1));
            Assert.Null(RelationKindDescriptor.Find(descriptor.DescriptorId, 2));
        }
        Assert.Equal(RelationCyclePolicy.Allowed, RelationKindDescriptor.Find(RelationKinds.RelatedTo, 1)!.CyclePolicy);
        Assert.Equal(RelationCyclePolicy.Allowed, RelationKindDescriptor.Find(RelationKinds.Contains, 1)!.CyclePolicy);
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("schemaVersion")]
    [InlineData("descriptorVersion")]
    [InlineData("extension")]
    [InlineData("payload")]
    [InlineData("endpoint")]
    public void UnsupportedSemanticsRetainIdentityEndpointsAndRawPayload(string change)
    {
        var edge = Edge();
        switch (change)
        {
            case "kind": edge["kind"] = "example.future"; edge.Remove("descriptor"); break;
            case "schemaVersion": edge["schemaVersion"] = 2; edge.Remove("descriptor"); break;
            case "descriptorVersion": edge["descriptor"]!["version"] = 2; break;
            case "extension": edge["example.extension"] = new JsonObject { ["precise"] = "0.000000000000000001" }; break;
            case "payload": edge["payload"]!["example.future"] = new JsonArray(3, 2, 1); break;
            case "endpoint": edge["source"]!["example.future"] = true; break;
        }
        var source = Source([edge]); var before = source.Data.ToCanonicalBytes();
        var decoded = TypedRelationReader.Read(source).Single();
        Assert.Equal(ValueDecodeStatus.Unsupported, decoded.Status); Assert.Null(decoded.Relation);
        Assert.Equal(Json(edge), decoded.Data); Assert.Equal(before, source.Data.ToCanonicalBytes());
        Assert.Equal(before, ProjectSourceCodec.Encode(ProjectSourceCodec.Decode(before).RequireSource()));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("target")]
    [InlineData("payload")]
    [InlineData("descriptor")]
    public void MalformedSupportedShapeFailsSafely(string field)
    {
        var edge = Edge(); edge[field] = 7;
        Assert.Equal(ValueDecodeStatus.Invalid, TypedRelationReader.Read(Json(edge), Context()).Status);
    }

    [Theory]
    [InlineData(true, ReferenceResolutionState.MissingWithinCompleteScope)]
    [InlineData(false, ReferenceResolutionState.UnknownDueToCoverage)]
    public void MissingReferenceRequiresCompleteAuthorizedScope(bool complete, ReferenceResolutionState expected)
    {
        var result = Validate([Edge(to: 20)], complete: complete);
        Assert.Contains(result.Resolutions, item => !item.IsSource && item.State == expected);
    }

    [Theory]
    [InlineData(95, 94, 91, 93)]
    [InlineData(92, 95, 91, 93)]
    [InlineData(92, 94, 95, 93)]
    [InlineData(92, 94, 91, 95)]
    public void ContextMismatchIsNeverResolved(int project, int revision, int tenant, int branch)
    {
        var edge = Edge(); edge["target"] = Endpoint(2, Context(project, revision, tenant, branch));
        var result = Validate([edge]);
        Assert.DoesNotContain(result.Resolutions, item => !item.IsSource && item.State == ReferenceResolutionState.Resolved);
    }

    [Fact]
    public void CrossProjectSnapshotMustBePinnedAuthorizedAndUnambiguous()
    {
        var context = Context(project: 95); var edge = Edge(); edge["target"] = Endpoint(2, context);
        var other = new RelationScopeSnapshot(Source(context: context), true, true, true);
        Assert.Empty(Validate([edge], snapshots: [other]).Findings);
        Has(Validate([edge]), "reference-coverage-unknown", RelationFindingState.Unknown);
        Has(Validate([edge], snapshots: [other with { IsAuthorized = false }]), "reference-out-of-scope");
        Has(Validate([edge], snapshots: [other, other]), "reference-ambiguous");
    }

    [Fact]
    public void RequiredInputCoverageAndDefaultAreSeparateFromMaximumCardinality()
    {
        var tasks = new[] { Task(1, true), Task(2), Task(3) };
        Has(Validate([], tasks), "required-port", RelationFindingState.Invalid);
        Has(Validate([], tasks, complete: false), "required-port", RelationFindingState.Unknown);
        tasks[0]["payload"]!["ports"]![1]!["defaultValue"] = Known("coglatas.text", "default");
        Assert.Empty(Validate([], tasks).Findings);
        Has(Validate([Edge(kind: RelationKinds.FlowsTo, from: 2, to: 1), Edge(id: 51, kind: RelationKinds.FlowsTo, from: 3, to: 1)], tasks, false), "port-cardinality", RelationFindingState.Invalid);
    }

    [Theory]
    [InlineData("source", "direction", "coglatas.input", "port-direction")]
    [InlineData("target", "direction", "coglatas.output", "port-direction")]
    [InlineData("target", "type", "coglatas.boolean", "port-type")]
    public void FlowChecksDirectionAndExactTypes(string side, string field, string value, string expected)
    {
        var tasks = new[] { Task(1), Task(2) };
        var port = tasks[side == "source" ? 0 : 1]["payload"]!["ports"]![side == "source" ? 0 : 1]!;
        port[field] = field == "type" ? Type(value) : JsonValue.Create(value);
        if (value == "coglatas.output") port["maximumConnections"] = null;
        if (side == "source") port["maximumConnections"] = 1;
        Has(Validate([Edge(kind: RelationKinds.FlowsTo)], tasks), expected);
    }

    [Fact]
    public void MixedRelatednessCycleDoesNotEnterSchedulingGraph()
    {
        Assert.Empty(Validate([Edge(), Edge(51, 2, 1, RelationKinds.RelatedTo)]).Findings);
    }

    [Theory]
    [InlineData("coglatas.start-to-start")]
    [InlineData("coglatas.finish-to-finish")]
    [InlineData("coglatas.start-to-finish")]
    public void ReadableDependencyModeDoesNotImplyLoweringSupport(string mode)
    {
        var edge = Edge(); edge["payload"]!["mode"] = mode;
        Assert.Equal(ValueDecodeStatus.Decoded, TypedRelationReader.Read(Json(edge), Context()).Status);
        Has(Validate([edge]), "mode-not-lowered", RelationFindingState.Unsupported);
    }

    [Fact]
    public void BoundedWorkflowRepetitionIsNotADependencyCycle()
    {
        var workflow = Declaration(4, "coglatas.workflow", Obj(new
        {
            states = new[] { Id(70) }, initialState = Id(70), maximumTransitions = 4,
            transitions = new[] { new { transitionId = Id(71), from = Id(70), to = Id(70), guard = Predicate() } }
        }));
        var edge = Edge(kind: RelationKinds.Governs, from: 4);
        edge["source"] = Endpoint(4, expectedKind: "coglatas.workflow");
        edge["payload"] = Obj(new { stateId = Id(70), transitionId = Id(71), guard = Predicate() });
        var result = Validate([edge], [Task(2), workflow]);
        Assert.False(result.HasInvalid); Has(result, "workflow-engine-not-evaluated", RelationFindingState.Unsupported);
        edge["payload"]!["stateId"] = Id(72);
        Has(Validate([edge], [Task(2), workflow]), "workflow-state");
    }

    [Theory]
    [InlineData(RelationKinds.Satisfies)]
    [InlineData(RelationKinds.Verifies)]
    public void RequirementLinkNeverProvesEvidenceOrSatisfaction(string kind)
    {
        var requirement = Declaration(4, "coglatas.requirement", Obj(new { target = Endpoint(2)["reference"]!.DeepClone(), condition = Predicate(), evidenceKind = "coglatas.model-proof" }));
        var edge = Edge(kind: kind); edge["target"] = Endpoint(4, expectedKind: "coglatas.requirement");
        edge["payload"] = Obj(new { requirementVersion = Id(94), requirementDigest = ContentDigest.Compute("coglatas.requirement/1", Json(requirement).ToCanonicalBytes()).Data.Value,
            condition = Predicate(), evidenceKind = "coglatas.model-proof" });
        var result = Validate([edge], [Task(1), Task(2), requirement]);
        Assert.False(result.HasInvalid); Has(result, "evidence-not-evaluated", RelationFindingState.Unknown);
        edge["payload"]!["requirementVersion"] = "wrong";
        Has(Validate([edge], [Task(1), Task(2), requirement]), "requirement-version");
    }

    [Theory]
    [InlineData(RelationKinds.Consumes, "coglatas.human")]
    [InlineData(RelationKinds.Allocates, "coglatas.material")]
    public void ResourceBehaviorAndUnboundUnitsCannotBeTreatedAsValid(string kind, string resourceKind)
    {
        var binding = Declaration(4, "coglatas.resource-binding", Obj(new { tenantId = Id(91), resourceId = Id(7), snapshotVersion = "1",
            snapshotDigest = ContentDigest.Compute("fixture", []).Data.Value, resourceKind }));
        var edge = Edge(kind: kind); edge["target"] = Endpoint(4, kind: "coglatas.resource-binding", expectedKind: "coglatas.resource-binding");
        var quantityType = Type("coglatas.quantity"); quantityType["unitId"] = "unit.kg"; quantityType["dimension"] = Obj(new Dictionary<string, int> { ["dimension.mass"] = 1 });
        var amount = Obj(new { type = quantityType, state = "coglatas.known", value = new { amount = "1", unitId = "unit.kg", dimension = new Dictionary<string, int> { ["dimension.mass"] = 1 } } });
        edge["payload"] = kind == RelationKinds.Consumes ? Obj(new { amount, at = Known("coglatas.instant", "2026-11-01T00:00:00.0000000Z") })
            : Obj(new { amount, interval = new { start = Known("coglatas.instant", "2026-11-01T00:00:00.0000000Z"), end = Known("coglatas.instant", "2026-11-01T01:00:00.0000000Z") }, release = "coglatas.interval-end" });
        var result = Validate([edge], [Task(1), binding]);
        Has(result, "resource-kind", RelationFindingState.Invalid); Has(result, "unit-unavailable", RelationFindingState.Unknown);
    }

    [Fact]
    public void SourceOrderDoesNotChangeDiagnosticsOrSourceBytesAndBudgetIsExplicit()
    {
        var edges = new[] { Edge(), Edge(51, 2, 1), Edge(52, 2, 1) };
        var first = Source(edges); var second = Source(edges.Reverse(), [Task(3), Task(2), Task(1)]);
        Assert.Equal(first.Digest, second.Digest);
        var before = first.Data.ToCanonicalBytes();
        var a = RelationGraphValidator.Validate(new(first, true, true, true));
        var b = RelationGraphValidator.Validate(new(second, true, true, true));
        Assert.Equal(a.Findings, b.Findings); Assert.Equal(before, first.Data.ToCanonicalBytes());
        var limited = RelationGraphValidator.Validate(new(first, true, true, true), limits: new(MaximumRelations: 1));
        Has(limited, "budget-exhausted", RelationFindingState.Unknown); Assert.Empty(limited.Resolutions); Assert.False(limited.IsComplete);
    }

    [Fact]
    public void CycleDiagnosticsDoNotAccuseADownstreamTail()
    {
        var tail = Edge(52, 2, 3);
        var result = Validate([Edge(), Edge(51, 2, 1), tail]);
        Assert.Equal(2, result.Findings.Count(item => item.Code == "coglatas.relation.dependency-cycle"));
        Assert.DoesNotContain(result.Findings, item => item.Location.RelationId == RelationId.Parse(Id(52)));
    }

    [Theory]
    [InlineData(false, false, true, true, false)]
    [InlineData(true, false, false, true, true)]
    [InlineData(false, true, true, false, true)]
    [InlineData(true, true, true, true, false)]
    public void FlowNullAndUnknownCapabilitiesAreCheckedDirectionally(bool outputNull, bool outputUnknown, bool inputNull, bool inputUnknown, bool invalid)
    {
        var tasks = new[] { Task(1), Task(2) };
        tasks[0]["payload"]!["ports"]![0]!["type"] = Type(nullable: outputNull, acceptsUnknown: outputUnknown);
        tasks[1]["payload"]!["ports"]![1]!["type"] = Type(nullable: inputNull, acceptsUnknown: inputUnknown);
        var result = Validate([Edge(kind: RelationKinds.FlowsTo)], tasks);
        Assert.Equal(invalid, result.HasInvalid);
    }

    [Fact]
    public void AttachedConstraintChecksTargetAndPredicateWithoutClaimingSatisfaction()
    {
        var declaration = Declaration(4, "coglatas.constraint", Obj(new { predicate = Predicate(), classification = "coglatas.required" }));
        var edge = Edge(kind: RelationKinds.Constrains, from: 4);
        edge["source"] = Endpoint(4, expectedKind: "coglatas.constraint");
        edge["payload"] = Obj(new { predicateReference = Endpoint(4)["reference"]!.DeepClone(), version = Id(94),
            predicateDigest = ContentDigest.Compute("coglatas.predicate/1", Json(Predicate()).ToCanonicalBytes()).Data.Value });
        var result = Validate([edge], [Task(2), declaration]);
        Assert.False(result.HasInvalid); Has(result, "constraint-not-evaluated", RelationFindingState.Unknown);
        var requirement = Declaration(2, "coglatas.requirement", Obj(new { target = Endpoint(4)["reference"]!.DeepClone(), condition = Predicate(), evidenceKind = "coglatas.model-proof" }));
        edge["target"] = Endpoint(2, expectedKind: "coglatas.requirement");
        Has(Validate([edge], [declaration, requirement]), "endpoint-type", RelationFindingState.Invalid);
    }

    [Fact]
    public void ApplicationReceiptBindsSourceAndCancellation()
    {
        var source = Source([Edge()]);
        var receipt = ProjectRelationValidation.Validate(new(source, true, true, true));
        Assert.Equal(source.Digest, receipt.SourceDigest); Assert.Equal(source.Context, receipt.Context); Assert.True(receipt.Result.IsComplete);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectRelationValidation.Validate(new(source, true, true, true), cancellationToken: cancellation.Token));
        Has(RelationGraphValidator.Validate(new(source, false, true, true)), "scope-unavailable", RelationFindingState.Unknown);
    }

    [Fact]
    public void InvalidSnapshotAndUnitEntriesFailSafelyWithoutDroppingTheirChecks()
    {
        var source = Source([Edge()]);
        Assert.Throws<ArgumentNullException>(() => new RelationScopeSnapshot(null!, true, true, true));
        Has(RelationGraphValidator.Validate(new(source, true, true, true), externalSnapshots: [null]), "budget-exhausted", RelationFindingState.Unknown);
        Has(RelationGraphValidator.Validate(new(source, true, true, true), units: [null]), "unit-budget", RelationFindingState.Unknown);
    }
}
