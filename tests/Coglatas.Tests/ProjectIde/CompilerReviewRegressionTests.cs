using System.Diagnostics.Tracing;
using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class CompilerReviewRegressionTests
{
    private static JsonObject TaskDeclaration(JsonObject? duration = null, JsonObject? portType = null) => Obj(new
    {
        entityId = Id(7), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1,
        payload = new
        {
            duration = duration ?? Obj(new { type = Type("coglatas.elapsed-duration"), state = "coglatas.known", value = "1" }),
            ports = portType is null ? Array.Empty<JsonObject>() : [Obj(new
            { portId = Id(8), direction = "coglatas.input", type = portType, minimumConnections = 0, maximumConnections = 1 })]
        }
    });
    private static JsonObject Type(string kind, string? expectedKind = null)
    {
        var type = Obj(new { kind, schemaVersion = 1, nullable = false, acceptsUnknown = true });
        if (expectedKind is not null) type["expectedKind"] = expectedKind;
        return type;
    }
    private static CompilerInputManifest WithTask(JsonObject task, JsonObject? settings = null)
    {
        var source = Source();
        return Input(settings, ProjectSource.Create(source.Context, source.Documents, Json(new JsonArray(task))));
    }

    [Theory]
    [InlineData("coglatas.document-reference", "coglatas.task", false)]
    [InlineData("coglatas.document-reference", "coglatas.project", false)]
    [InlineData("coglatas.document-reference", "coglatas.requirement", false)]
    [InlineData("coglatas.entity-reference", SourceDocument.SchemaId, false)]
    [InlineData("coglatas.document-reference", SourceDocument.SchemaId, true)]
    [InlineData("coglatas.entity-reference", "coglatas.task", true)]
    public void ReferenceDescriptorAndDefaultlessPortEnforceTheSameTargetCategory(string kind, string expected, bool supported)
    {
        var type = Type(kind, expected);
        Assert.Equal(supported, new TypeReference(Json(type)).IsSupported);
        var port = Obj(new { portId = Id(8), direction = "coglatas.input", type, minimumConnections = 0, maximumConnections = 1 });
        if (supported) Assert.Null(new PortDefinition(Json(port)).DefaultValue);
        else Assert.Throws<UnsupportedSourceException>(() => new PortDefinition(Json(port)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DocumentReferenceToEntityCannotProduceIrEvenWithoutAnyDefault(bool nested)
    {
        var type = Type("coglatas.document-reference", "coglatas.task");
        if (nested) type = Obj(new { kind = "coglatas.list", schemaVersion = 1, nullable = false, acceptsUnknown = false, elementType = type });
        var input = WithTask(TaskDeclaration(portType: type));
        var validation = CompilerSemanticValidation.Validate(ProjectSemanticBinder.Bind(input));
        Assert.Null(validation.Ir);
        Assert.Contains(validation.Findings, finding => finding.State == RelationFindingState.Unsupported);
        Assert.Equal(input.Source.Data, validation.Model.Input.Source.Data);
    }

    [Theory]
    [InlineData("quantity")]
    [InlineData("reference")]
    public void UnknownBasisIsOpaqueToSemanticValidationWhileRetainingInputAndBudget(string content)
    {
        var nestedType = content == "quantity" ? Type("coglatas.quantity") : Type("coglatas.entity-reference", "coglatas.task");
        if (content == "quantity")
        {
            nestedType["unitId"] = "unit.unavailable";
            nestedType["dimension"] = Obj(new Dictionary<string, int> { ["dimension.mass"] = 1 });
        }
        var source = Source();
        var nestedValue = content == "quantity" ? Obj(new { amount = "1", unitId = "unit.unavailable", dimension = new Dictionary<string, int> { ["dimension.mass"] = 1 } }) :
            Obj(new { tenantId = source.TenantId.ToString(), projectId = source.ProjectId.ToString(), entityId = Id(99), context = source.Context.Data.Value,
                origin = ProjectLocation.Create(DocumentId.Parse(Id(4)), source.Context, entityId: EntityId.Parse(Id(7))).Data.Value,
                manifest = new { version = "1", digest = source.Digest.Data.Value } });
        var basis = Obj(new { type = nestedType, state = "coglatas.known", value = nestedValue });
        Assert.Equal(ValueDecodeStatus.Decoded, TypedProjectValueCodec.Decode(Json(basis)).Status);
        var duration = Obj(new { type = Type("coglatas.elapsed-duration"), state = "coglatas.unknown", reason = "coglatas.not-evaluated", basis });
        var decoded = TypedProjectValueCodec.Decode(Json(duration)).Value!;
        Assert.Equal(ValueState.Unknown, decoded.State); Assert.Null(decoded.KnownValue); Assert.Equal(Json(basis), decoded.UnknownBasis);
        var input = WithTask(TaskDeclaration(duration)); var before = input.Source.Data.ToCanonicalBytes();
        var binding = ProjectSemanticBinder.Bind(input);
        Assert.True(binding.BindingComplete); Assert.Empty(binding.References);
        var validation = CompilerSemanticValidation.Validate(binding);
        Assert.NotNull(validation.Ir); Assert.Empty(validation.Findings);
        Assert.Equal(before, validation.Model.Input.Source.Data.ToCanonicalBytes());
        var settings = Settings(); settings["executionProfile"]!["maximumOperations"] = binding.LogicalOperationsConsumed - 1;
        var exhausted = CompilerSemanticValidation.Validate(ProjectSemanticBinder.Bind(WithTask(TaskDeclaration(duration.DeepClone().AsObject()), settings)));
        Assert.Null(exhausted.Ir); Assert.Contains(exhausted.Findings, finding => finding.Diagnostic.Code.Contains("budget-exhausted", StringComparison.Ordinal));
    }

    private sealed class Transport : IServerCompilerTransport
    {
        public int Calls { get; private set; }
        public Task<CompilerHostResponse> SubmitAsync(CompilerHostRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            // Deliberately ignore cancellation; a cancelled wait cannot undo this side effect.
            return Task.FromResult(new CompilerHostResponse(CompilerHostStatus.InternalError, "test.transport-called", request.Input.ExpectedInputDigest, request.ClientGeneration, Guid.NewGuid(), null));
        }
    }

    // TPL emits TaskStarted synchronously before the scheduled delegate runs. Target
    // only the first Task.Run scheduled by this calling thread, after the host's
    // entry guard, avoiding sleeps, global ThreadPool changes or Product test hooks.
    private sealed class CancelMaterialization(CancellationTokenSource cancellation) : EventListener
    {
        private readonly int _caller = Environment.CurrentManagedThreadId;
        private int _target;
        public bool Triggered { get; private set; }
        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "System.Threading.Tasks.TplEventSource") EnableEvents(source, EventLevel.Informational, (EventKeywords)3);
        }
        protected override void OnEventWritten(EventWrittenEventArgs data)
        {
            if (data.Payload is not { Count: > 2 }) return;
            if (data.EventName == "TaskScheduled" && Environment.CurrentManagedThreadId == _caller)
                Interlocked.CompareExchange(ref _target, (int)data.Payload[2]!, 0);
            if (data.EventName == "TaskStarted" && (int)data.Payload[2]! == Volatile.Read(ref _target) && _target != 0)
            {
                Triggered = true;
                cancellation.Cancel();
            }
        }
    }

    [Fact]
    public async Task CancellationAfterEntryBeforeMaterializationCompletesDoesNotSubmitTransport()
    {
        var input = Input();
        CompilerArtifactIdentity Artifact(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString()));
        var transport = new Transport(); var host = new ServerCompilerHost(profile, transport);
        using var cancellation = new CancellationTokenSource();
        using var listener = new CancelMaterialization(cancellation);
        var response = await host.CompileAsync(new(new(input)), cancellation.Token).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(listener.Triggered); Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(CompilerHostStatus.Cancelled, response.Status); Assert.Null(response.Compilation);
        Assert.Equal(0, transport.Calls);
    }
}
