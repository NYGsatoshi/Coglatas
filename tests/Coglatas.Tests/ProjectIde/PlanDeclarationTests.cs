using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class PlanDeclarationTests
{
    private const string Tenant = "00000000-0000-4000-8000-000000000001";
    private const string Project = "00000000-0000-4000-8000-000000000002";
    private const string Entity = "00000000-0000-4000-8000-000000000003";
    private const string Document = "00000000-0000-4000-8000-000000000004";
    private const string Other = "00000000-0000-4000-8000-000000000005";
    private static readonly DocumentId DocumentIdentity = DocumentId.Parse(Document);
    private static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private static SourceRevisionContext Context() => SourceRevisionContext.Committed(new(new(TenantId.Parse(Tenant), ProjectId.Parse(Project), BranchId.Parse(Other)), RevisionId.Parse(Other)));
    private static object BooleanType() => new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = true };
    internal static object Literal(bool? value) => new
    {
        kind = "coglatas.literal",
        value = value.HasValue ? new Dictionary<string, object?> { ["type"] = BooleanType(), ["state"] = "coglatas.known", ["value"] = value.Value }
            : new Dictionary<string, object?> { ["type"] = BooleanType(), ["state"] = "coglatas.unknown", ["reason"] = "coglatas.missing-input", ["basis"] = new { coverage = "coglatas.partial" } }
    };
    private static object Duration(string ticks = "10") => new
    {
        type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = true, acceptsUnknown = true },
        state = "coglatas.known", value = ticks
    };
    private static object Port() => new
    {
        portId = Other, direction = "coglatas.input", type = BooleanType(), minimumConnections = 1, maximumConnections = 1,
        defaultValue = new { type = BooleanType(), state = "coglatas.known", value = false }
    };
    private static object Payload(string kind) => kind switch
    {
        "coglatas.project" => new { roots = new[] { Other } },
        "coglatas.task" => new { duration = Duration(), ports = new[] { Port() } },
        "coglatas.milestone" => new { target = new { type = new { kind = "coglatas.instant", schemaVersion = 1, nullable = false, acceptsUnknown = true }, state = "coglatas.known", value = "2026-10-31T00:00:00.0000000Z" } },
        "coglatas.requirement" => new { target = new { tenantId = Tenant, projectId = Project, entityId = Other, context = Context().Data.Value }, condition = Literal(true), evidenceKind = "coglatas.model-proof" },
        "coglatas.resource-binding" => new { tenantId = Tenant, resourceId = Other, snapshotVersion = "1", snapshotDigest = new { algorithm = "sha-256", value = new string('a', 64) }, resourceKind = "coglatas.equipment" },
        "coglatas.constraint" => new { predicate = Literal(null), classification = "coglatas.required" },
        "coglatas.workflow" => new { states = new[] { Other }, initialState = Other, maximumTransitions = 2, transitions = new[] { new { transitionId = Other, from = Other, to = Other, guard = Literal(true) } } },
        _ => new { opaque = new { number = "123456789012345678901234567890", ordered = new[] { "z", "a" } } }
    };
    private static SourceJson Declaration(string kind) => Json(new { entityId = Entity, documentId = Document, kind, schemaVersion = 1, displayName = "Synthetic", payload = Payload(kind) });
    private static DeclarationDecodeResult Read(JsonNode value) => PlanDeclarationReader.Read(SourceJson.Parse(value.ToJsonString()), DocumentIdentity);

    [Theory]
    [InlineData("coglatas.project", typeof(ProjectPlanDeclaration))]
    [InlineData("coglatas.task", typeof(TaskPlanDeclaration))]
    [InlineData("coglatas.milestone", typeof(MilestonePlanDeclaration))]
    [InlineData("coglatas.requirement", typeof(RequirementPlanDeclaration))]
    [InlineData("coglatas.resource-binding", typeof(ResourceBindingDeclaration))]
    [InlineData("coglatas.constraint", typeof(ConstraintPlanDeclaration))]
    [InlineData("coglatas.workflow", typeof(WorkflowPlanDeclaration))]
    public void AllSevenConceptsAreSourceBackedTypedViews(string kind, Type type)
    {
        var source = Declaration(kind);
        var result = PlanDeclarationReader.Read(source, DocumentIdentity);
        Assert.Equal(ValueDecodeStatus.Decoded, result.Status);
        Assert.IsType(type, result.Declaration);
        Assert.Same(source, result.Declaration!.Data);
        Assert.Equal(EntityId.Parse(Entity), result.Declaration.EntityId);
        Assert.Equal(DocumentIdentity, result.Declaration.DocumentId);
    }

    [Theory]
    [InlineData("coglatas.task", "duration")]
    [InlineData("coglatas.task", "ports")]
    [InlineData("coglatas.requirement", "target")]
    [InlineData("coglatas.resource-binding", "snapshotDigest")]
    [InlineData("coglatas.workflow", "maximumTransitions")]
    public void MissingRequiredFieldsAreInvalidAndRetained(string kind, string field)
    {
        var node = JsonNode.Parse(Declaration(kind).CanonicalText)!;
        node["payload"]!.AsObject().Remove(field);
        var result = Read(node);
        Assert.Equal(ValueDecodeStatus.Invalid, result.Status);
        Assert.Equal(SourceJson.Parse(node.ToJsonString()), result.Data);
    }

    [Theory]
    [InlineData("coglatas.task")]
    [InlineData("coglatas.requirement")]
    [InlineData("coglatas.resource-binding")]
    [InlineData("coglatas.workflow")]
    [InlineData("future.widget")]
    public void UnsupportedDataIsNotDiscardedOrGivenDefaultSemantics(string kind)
    {
        var node = JsonNode.Parse(Declaration(kind).CanonicalText)!;
        node["payload"]!["future"] = JsonNode.Parse("{\"number\":123456789012345678901234567890.123456789,\"ordered\":[\"z\",\"a\"],\"nil\":null}");
        Assert.Equal(ValueDecodeStatus.Unsupported, Read(node).Status);
        Assert.Equal(SourceJson.Parse(node.ToJsonString()), Read(node).Data);
        Assert.Null(Read(node).Declaration);
    }

    [Fact]
    public void WorkDurationAndMilestoneSemanticsHaveNegativeControls()
    {
        var task = JsonNode.Parse(Declaration("coglatas.task").CanonicalText)!;
        task["payload"]!["duration"]!["value"] = "-1";
        Assert.Equal(ValueDecodeStatus.Invalid, Read(task).Status);
        var unknown = task["payload"]!["duration"]!;
        unknown["state"] = "coglatas.unknown";
        unknown.AsObject().Remove("value"); unknown["reason"] = "coglatas.missing-input"; unknown["basis"] = new JsonObject();
        var decoded = Assert.IsType<TaskPlanDeclaration>(Read(task).Declaration);
        Assert.Equal(ValueState.Unknown, decoded.Duration.State);
        Assert.Null(decoded.Duration.KnownValue);
        var milestone = JsonNode.Parse(Declaration("coglatas.milestone").CanonicalText)!;
        milestone["payload"]!["duration"] = JsonSerializer.SerializeToNode(Duration());
        Assert.Equal(ValueDecodeStatus.Invalid, Read(milestone).Status);
    }

    [Fact]
    public void PortDefaultCardinalityAndIdentityAreValidated()
    {
        var node = JsonNode.Parse(Declaration("coglatas.task").CanonicalText)!;
        var ports = node["payload"]!["ports"]!.AsArray();
        ports.Add(ports[0]!.DeepClone());
        Assert.Equal(ValueDecodeStatus.Invalid, Read(node).Status);
        ports.RemoveAt(1);
        ports[0]!["minimumConnections"] = 2;
        Assert.Equal(ValueDecodeStatus.Invalid, Read(node).Status);
        ports[0]!["minimumConnections"] = 1; ports[0]!["maximumConnections"] = 2;
        Assert.Equal(ValueDecodeStatus.Unsupported, Read(node).Status);
        ports[0]!["maximumConnections"] = 1; ports[0]!["direction"] = "coglatas.output";
        Assert.Equal(ValueDecodeStatus.Invalid, Read(node).Status);
        ports[0]!["direction"] = "coglatas.input"; ports[0]!["defaultValue"]!["type"]!["nullable"] = true;
        Assert.Equal(ValueDecodeStatus.Invalid, Read(node).Status);
    }

    [Theory]
    [InlineData("coglatas.human", ResourceBehavior.Reusable)]
    [InlineData("coglatas.equipment", ResourceBehavior.Reusable)]
    [InlineData("coglatas.facility", ResourceBehavior.Reusable)]
    [InlineData("coglatas.material", ResourceBehavior.Consumable)]
    public void ResourceKindsKeepAllocationAndConsumptionDistinct(string kind, ResourceBehavior behavior)
    {
        var node = JsonNode.Parse(Declaration("coglatas.resource-binding").CanonicalText)!;
        node["payload"]!["resourceKind"] = kind;
        Assert.Equal(behavior, Assert.IsType<ResourceBindingDeclaration>(Read(node).Declaration).Behavior);
        node["payload"]!["resourceKind"] = "future.resource";
        Assert.Equal(ValueDecodeStatus.Unsupported, Read(node).Status);
    }

    [Fact]
    public void WorkflowAllowsExplicitlyBoundedLoopsWithoutClaimingExecution()
    {
        var node = JsonNode.Parse(Declaration("coglatas.workflow").CanonicalText)!;
        var workflow = Assert.IsType<WorkflowPlanDeclaration>(Read(node).Declaration);
        Assert.Equal(workflow.Transitions[0].From, workflow.Transitions[0].To);
        node["payload"]!["maximumTransitions"] = 0;
        Assert.Equal(ValueDecodeStatus.Invalid, Read(node).Status);
        node["payload"]!["maximumTransitions"] = 2; node["payload"]!["transitions"]![0]!["to"] = Entity;
        Assert.Equal(ValueDecodeStatus.Invalid, Read(node).Status);
    }

    [Fact]
    public void ReferenceScopeAndUnknownContextCannotBeGuessed()
    {
        var node = JsonNode.Parse(Declaration("coglatas.requirement").CanonicalText)!;
        node["payload"]!["target"]!["tenantId"] = Other;
        Assert.Equal(ValueDecodeStatus.Invalid, Read(node).Status);
        node["payload"]!["target"]!["tenantId"] = Tenant;
        node["payload"]!["target"]!["context"]!["future"] = true;
        Assert.Equal(ValueDecodeStatus.Unsupported, Read(node).Status);
        node["payload"]!["target"]!["context"]!.AsObject().Remove("future");
        node["documentId"] = Other;
        Assert.Equal(ValueDecodeStatus.Invalid, Read(node).Status);
        Assert.Equal(ValueDecodeStatus.Invalid, PlanDeclarationReader.Read(SourceJson.Parse("[]"), DocumentIdentity).Status);
    }

    [Fact]
    public void SourceIntegrationRetainsIdentityAndRejectsCrossTenantResourceBinding()
    {
        var project = Declaration("coglatas.project");
        var root = JsonNode.Parse(project.CanonicalText)!; root.AsObject().Remove("documentId");
        var resource = JsonNode.Parse(Declaration("coglatas.resource-binding").CanonicalText)!; resource["entityId"] = Other;
        var document = SourceDocument.Create(DocumentIdentity, "project", SourceJson.Parse(root.ToJsonString()));
        var source = ProjectSource.Create(Context(), [document], declarations: SourceJson.Parse(new JsonArray(resource).ToJsonString()));
        var before = ProjectSourceCodec.Encode(source);
        var results = PlanDeclarationReader.Read(source);
        Assert.All(results, result => Assert.Equal(ValueDecodeStatus.Decoded, result.Status));
        Assert.Equal(before, ProjectSourceCodec.Encode(source));
        resource["payload"]!["tenantId"] = Other;
        var denied = ProjectSource.Create(Context(), [document], declarations: SourceJson.Parse(new JsonArray(resource.DeepClone()).ToJsonString()));
        Assert.Equal(ValueDecodeStatus.Invalid, PlanDeclarationReader.Read(denied)[1].Status);
        var resourceRoot = resource.DeepClone(); resourceRoot.AsObject().Remove("documentId");
        var extraDocument = SourceDocument.Create(DocumentId.Parse(Other), "resource", SourceJson.Parse(resourceRoot.ToJsonString()));
        var rootDenied = ProjectSource.Create(Context(), [document, extraDocument]);
        Assert.Single(PlanDeclarationReader.Read(rootDenied), result => result.Status == ValueDecodeStatus.Invalid);
    }

    [Theory]
    [InlineData(typeof(ResourceId))]
    [InlineData(typeof(PortId))]
    [InlineData(typeof(WorkflowStateId))]
    [InlineData(typeof(WorkflowTransitionId))]
    public void AdditionalTypedIdsUseExistingUuidCodec(Type type)
    {
        var parsed = type.GetMethod("Parse")!.Invoke(null, [Entity]);
        Assert.Equal("\"" + Entity + "\"", JsonSerializer.Serialize(parsed, type));
        Assert.Equal(parsed, JsonSerializer.Deserialize(JsonSerializer.Serialize(parsed, type), type));
        Assert.DoesNotContain(type.GetMethods(), method => method.Name == "op_Implicit");
        var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => Activator.CreateInstance(type, Guid.Empty));
        Assert.IsType<ArgumentException>(error.InnerException);
    }
}
