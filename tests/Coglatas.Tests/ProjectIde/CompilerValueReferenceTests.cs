using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class CompilerValueReferenceTests
{
    private static JsonObject Task(int id, JsonArray? ports = null) => Obj(new { entityId = Id(id), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1, payload = new
    {
        duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "1" },
        ports = ports ?? new JsonArray()
    } });

    private static (CompilerInputManifest Input, CompilerMaterializedSnapshot Snapshot) Fixture(string change = "", string shape = "scalar")
    {
        var original = Source();
        var context = SourceRevisionContext.Committed(new(new(original.TenantId, ProjectId.Parse(Id(22)), BranchId.Parse(Id(23))), RevisionId.Parse(Id(24))));
        var target = ProjectSource.Create(context, original.Documents, Json(new JsonArray(Task(7))));
        if (change == "opaque")
        {
            var opaque = Task(7); opaque["example.future"] = true;
            target = ProjectSource.Create(context, original.Documents, Json(new JsonArray(opaque)));
        }
        var kind = shape == "document" ? "coglatas.document-reference" : change == "resource" ? "coglatas.resource-reference" : change == "snapshot" ? "coglatas.external-snapshot-reference" : "coglatas.entity-reference";
        var expected = shape == "document" ? SourceDocument.SchemaId : change == "kind" ? "coglatas.milestone" : change == "resource" ? "coglatas.human" : change == "snapshot" ? "example.snapshot" : "coglatas.task";
        var type = Obj(new { kind, schemaVersion = 1, nullable = true, acceptsUnknown = true, expectedKind = expected });
        var reference = Obj(new { tenantId = target.TenantId.ToString(), projectId = target.ProjectId.ToString(), context = target.Context.Data.Value,
            origin = ProjectLocation.Create(DocumentId.Parse(Id(change == "origin-document" ? 99 : 4)), change == "origin-context" ? Source(99).Context : original.Context, entityId: EntityId.Parse(Id(change == "origin-entity" ? 99 : 8))).Data.Value,
            manifest = new { version = change == "version" ? "2" : "1", digest = (change == "digest" ? original.Digest : target.Digest).Data.Value } });
        reference[kind switch { "coglatas.document-reference" => "documentId", "coglatas.resource-reference" => "resourceId", "coglatas.external-snapshot-reference" => "snapshotId", _ => "entityId" }] = Id(change == "missing" ? 99 : shape == "document" ? 4 : 7);
        if (change == "revision") reference["context"]!["revisionId"] = Id(25);
        if (change == "branch") reference["context"]!["branch"]!["branchId"] = Id(25);
        if (change == "tenant") { reference["tenantId"] = Id(31); reference["context"]!["branch"]!["tenantId"] = Id(31); reference["origin"]!["context"]!["branch"]!["tenantId"] = Id(31); }
        var value = Obj(new { type, state = "coglatas.known", value = reference });
        if (shape == "unknown") { value.Remove("value"); value["state"] = "coglatas.unknown"; value["reason"] = "coglatas.not-evaluated"; value["basis"] = Obj(new { nested = new { type, state = "coglatas.known", value = reference } }); }
        if (shape == "null") { value.Remove("value"); value["state"] = "coglatas.null"; }
        if (shape is "list" or "sequence" or "set" or "nullable" or "optional" or "absent" or "structure" or "map")
        {
            var outer = Obj(new { kind = "coglatas." + (shape == "absent" ? "optional" : shape), schemaVersion = 1, nullable = false, acceptsUnknown = false });
            JsonNode payload;
            if (shape == "structure") { outer["fields"] = new JsonArray(Obj(new { name = "a/b~c", required = true, type })); payload = Obj(new Dictionary<string, object> { ["a/b~c"] = value }); }
            else if (shape == "map")
            {
                var keyType = Obj(new { kind = "coglatas.text", schemaVersion = 1, nullable = false, acceptsUnknown = false });
                outer["keyType"] = keyType; outer["valueType"] = type.DeepClone(); outer["identity"] = "coglatas.canonical-value";
                payload = new JsonArray(Obj(new { key = new { type = keyType, state = "coglatas.known", value = "key" }, value }));
            }
            else
            {
                outer["elementType"] = type.DeepClone();
                if (shape == "set") outer["identity"] = "coglatas.canonical-value";
                payload = shape == "nullable" ? value : shape == "optional" ? Obj(new { present = true, value }) : shape == "absent" ? Obj(new { present = false }) : new JsonArray(value);
            }
            value = Obj(new { type = outer, state = "coglatas.known", value = payload }); type = outer;
        }
        var ports = new JsonArray(Obj(new { portId = Id(9), direction = "coglatas.input", type, minimumConnections = 0, maximumConnections = 1, defaultValue = value }));
        var source = ProjectSource.Create(original.Context, original.Documents, Json(new JsonArray(Task(8, ports))));
        var settings = Settings();
        settings["authorizedExternalSnapshots"] = new JsonArray(Obj(new { snapshotId = Id(30), version = "1", digest = target.Digest.Data.Value, context = target.Context.Data.Value, coverage = change == "partial" ? "coglatas.partial" : "coglatas.complete", asOf = (string?)null }));
        return (Input(settings, source), new(ExternalSnapshotId.Parse(Id(30)), "1", new(target, change != "unauthorized")));
    }

    [Theory]
    [InlineData("scalar")]
    [InlineData("document")]
    [InlineData("list")]
    [InlineData("sequence")]
    [InlineData("set")]
    [InlineData("nullable")]
    [InlineData("optional")]
    [InlineData("structure")]
    [InlineData("map")]
    public void PinnedKnownReferencesResolveWithStableSourceLocations(string shape)
    {
        var f = Fixture(shape: shape); var before = f.Input.Source.Data;
        var a = ProjectSemanticBinder.Bind(f.Input, [f.Snapshot]); var b = ProjectSemanticBinder.Bind(f.Input, [f.Snapshot]);
        Assert.True(a.BindingComplete); Assert.Empty(a.Findings);
        var reference = Assert.Single(a.References); Assert.Equal(ReferenceResolutionState.Resolved, reference.State);
        Assert.NotNull(reference.SymbolKey); Assert.Contains(a.Symbols, symbol => symbol.Key == reference.SymbolKey);
        Assert.Equal(EntityId.Parse(Id(8)), reference.Origin.EntityId); Assert.Contains("/payload/ports/" + Id(9) + "/defaultValue", reference.Origin.PropertyPath);
        Assert.Equal(reference, Assert.Single(b.References)); Assert.Equal(before, f.Input.Source.Data);
        var validation = CompilerSemanticValidation.Validate(a);
        Assert.Null(validation.Ir); Assert.Contains(validation.Findings, item => item.Diagnostic.Code == "coglatas.semantic.external-closure-not-lowered");
    }

    [Theory]
    [InlineData("kind", ReferenceResolutionState.Unsupported, "coglatas.binding.value-reference-kind-mismatch")]
    [InlineData("version", ReferenceResolutionState.Unsupported, "coglatas.binding.value-reference-manifest-mismatch")]
    [InlineData("digest", ReferenceResolutionState.Unsupported, "coglatas.binding.value-reference-manifest-mismatch")]
    [InlineData("origin-document", ReferenceResolutionState.Unsupported, "coglatas.binding.value-reference-origin-mismatch")]
    [InlineData("origin-context", ReferenceResolutionState.Unsupported, "coglatas.binding.value-reference-origin-mismatch")]
    [InlineData("origin-entity", ReferenceResolutionState.Unsupported, "coglatas.binding.value-reference-origin-mismatch")]
    [InlineData("missing", ReferenceResolutionState.MissingWithinCompleteScope, "coglatas.binding.reference-missing")]
    [InlineData("revision", ReferenceResolutionState.UnknownDueToCoverage, "coglatas.binding.reference-coverage-unknown")]
    [InlineData("branch", ReferenceResolutionState.UnknownDueToCoverage, "coglatas.binding.reference-coverage-unknown")]
    [InlineData("tenant", ReferenceResolutionState.OutOfScope, "coglatas.binding.reference-out-of-scope")]
    [InlineData("unauthorized", ReferenceResolutionState.OutOfScope, "coglatas.binding.reference-out-of-scope")]
    [InlineData("opaque", ReferenceResolutionState.Unsupported, "coglatas.binding.reference-unsupported")]
    [InlineData("resource", ReferenceResolutionState.Unsupported, "coglatas.binding.value-reference-unavailable")]
    [InlineData("snapshot", ReferenceResolutionState.Unsupported, "coglatas.binding.value-reference-unavailable")]
    public void InvalidUnavailableOrUnsupportedBindingsNeverProduceIr(string change, ReferenceResolutionState expected, string code)
    {
        var f = Fixture(change); var a = ProjectSemanticBinder.Bind(f.Input, [f.Snapshot]);
        Assert.False(a.BindingComplete); var reference = Assert.Single(a.References);
        Assert.Equal(expected, reference.State); Assert.Null(reference.SymbolKey);
        Assert.Contains(a.Findings, finding => finding.Diagnostic.Code == code);
        Assert.Null(CompilerSemanticValidation.Validate(a).Ir);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("null")]
    [InlineData("absent")]
    public void NonKnownValuesAndAbsentOptionalsHaveNoFabricatedDependency(string shape)
    {
        var f = Fixture(shape: shape); var model = ProjectSemanticBinder.Bind(f.Input);
        Assert.True(model.BindingComplete); Assert.Empty(model.References); Assert.Empty(model.Findings);
    }

    [Fact]
    public void PartialMissingAndDuplicateMaterializationsRemainDistinct()
    {
        var f = Fixture("missing"); var settings = JsonNode.Parse(f.Input.Settings.CanonicalText)!.AsObject();
        settings["authorizedExternalSnapshots"]![0]!["coverage"] = "coglatas.partial";
        var partial = ProjectSemanticBinder.Bind(Input(settings, f.Input.Source), [f.Snapshot]);
        Assert.Equal(ReferenceResolutionState.UnknownDueToCoverage, Assert.Single(partial.References).State);
        f = Fixture(); var duplicate = ProjectSemanticBinder.Bind(f.Input, [f.Snapshot, f.Snapshot]);
        Assert.Equal(ReferenceResolutionState.Ambiguous, Assert.Single(duplicate.References).State);
        Assert.Null(CompilerSemanticValidation.Validate(duplicate).Ir);
    }

    [Fact]
    public void BudgetAndCancellationCannotPublishSuccessfulClosure()
    {
        var f = Fixture(); var settings = JsonNode.Parse(f.Input.Settings.CanonicalText)!.AsObject(); settings["executionProfile"]!["maximumOperations"] = 1;
        var input = Input(settings, f.Input.Source); var result = PureCoreCompiler.Compile(input, input.Compiler, [f.Snapshot]);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, result.Result.Execution); Assert.Null(result.Validation?.Ir);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectSemanticBinder.Bind(f.Input, [f.Snapshot], cancellation.Token));
    }
}
