using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class CompilerReferenceIndexTests
{
    [Theory]
    [InlineData("committed")]
    [InlineData("candidate")]
    [InlineData("scenario")]
    public void RepeatedEntityAndPortIdentitiesResolveInTheirExactCanonicalContexts(string kind)
    {
        var fixture = Fixture(kind);
        var before = fixture.Input.Source.Data.ToCanonicalBytes();
        var model = ProjectSemanticBinder.Bind(fixture.Input, [fixture.Body]);
        Assert.True(model.BindingComplete); Assert.Empty(model.Findings); Assert.Equal(5, model.References.Count);
        Assert.All(model.References, reference => Assert.Equal(ReferenceResolutionState.Resolved, reference.State));
        Assert.Equal(Key(fixture.Input.Source.Context, 10), Reference(model, 6).SymbolKey);
        Assert.Equal(Key(fixture.Body.Snapshot.Source.Context, 10), Reference(model, 10).SymbolKey);
        Assert.Equal(Key(fixture.Body.Snapshot.Source.Context, 10, 10), Reference(model, 60).SymbolKey);
        Assert.Equal(Key(fixture.Body.Snapshot.Source.Context, 10), Reference(model, 61).SymbolKey);
        Assert.Equal(Key(fixture.Input.Source.Context, 10, 10000), Reference(model, 62).SymbolKey);
        Assert.Equal(before, fixture.Input.Source.Data.ToCanonicalBytes());
        // Parsed reference contexts are equal to, but distinct from, the supplied context.
        var typed = (TaskPlanDeclaration)Assert.Single(model.Declarations, row => row.Declaration?.EntityId == EntityId.Parse(Id(10))).Declaration!;
        var value = Assert.IsType<TypedProjectReference>(Assert.Single(typed.Ports).DefaultValue!.KnownValue);
        Assert.NotSame(fixture.Body.Snapshot.Source.Context, value.Context);
        Assert.Equal(fixture.Body.Snapshot.Source.Context, value.Context);
        Assert.Equal(2, model.Symbols.Count(symbol => symbol.EntityId == EntityId.Parse(Id(10)) && symbol.PortId is null));
    }

    [Theory]
    [InlineData("alias", ReferenceResolutionState.Ambiguous)]
    [InlineData("denied-alias", ReferenceResolutionState.OutOfScope)]
    [InlineData("conflicting-version", ReferenceResolutionState.Ambiguous)]
    [InlineData("conflicting-coverage", ReferenceResolutionState.Ambiguous)]
    [InlineData("missing", ReferenceResolutionState.UnknownDueToCoverage)]
    [InlineData("wrong-pin", ReferenceResolutionState.UnknownDueToCoverage)]
    public void ContextIndexRetainsAliasCardinalityAndFreshAdmissionPrecedence(string change, ReferenceResolutionState expected)
    {
        var fixture = Fixture(); var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject();
        var supplied = new List<CompilerMaterializedSnapshot> { fixture.Body };
        if (change is "missing" or "wrong-pin")
        {
            supplied.Clear();
            if (change == "wrong-pin") supplied.Add(new(fixture.Body.SnapshotId, "wrong", fixture.Body.Snapshot));
        }
        else
        {
            var pin = settings["authorizedExternalSnapshots"]![0]!.DeepClone(); pin["snapshotId"] = Id(31);
            var version = change == "conflicting-version" ? "2" : "1"; pin["version"] = version;
            settings["authorizedExternalSnapshots"]!.AsArray().Add(pin);
            supplied.Add(new(ExternalSnapshotId.Parse(Id(31)), version, fixture.Body.Snapshot with
            { IsAuthorized = change != "denied-alias", DeclarationsComplete = change != "conflicting-coverage" }));
        }
        var input = Input(settings, fixture.Input.Source);
        var forward = ProjectSemanticBinder.Bind(input, supplied);
        supplied.Reverse(); var reversed = ProjectSemanticBinder.Bind(input, supplied);
        Assert.False(forward.BindingComplete);
        foreach (var entity in new[] { 10, 60, 61 })
        { Assert.Equal(expected, Reference(forward, entity).State); Assert.Null(Reference(forward, entity).SymbolKey); }
        foreach (var entity in new[] { 6, 62 }) Assert.Equal(ReferenceResolutionState.Resolved, Reference(forward, entity).State);
        Assert.Equal(forward.References, reversed.References);
        Assert.Equal(forward.Findings.Select(row => row.Diagnostic.Data), reversed.Findings.Select(row => row.Diagnostic.Data));
        Assert.Equal(forward.LogicalOperationsConsumed, reversed.LogicalOperationsConsumed);
    }

    [Theory]
    [InlineData(true, ReferenceResolutionState.MissingWithinCompleteScope)]
    [InlineData(false, ReferenceResolutionState.UnknownDueToCoverage)]
    public void MissingExternalEntityAndPortNeverFallBackToAvailableLocalIdentities(bool complete, ReferenceResolutionState expected)
    {
        var fixture = Fixture(); var external = fixture.Body.Snapshot.Source;
        external = ProjectSource.Create(external.Context, external.Documents);
        var settings = JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject();
        settings["authorizedExternalSnapshots"]![0]!["digest"] = JsonNode.Parse(external.Digest.Data.CanonicalText);
        settings["authorizedExternalSnapshots"]![0]!["coverage"] = complete ? "coglatas.complete" : "coglatas.partial";
        var declarations = JsonNode.Parse(fixture.Input.Source.Declarations.CanonicalText)!.AsArray();
        declarations[0]!["payload"]!["ports"]![0]!["defaultValue"]!["value"]!["manifest"]!["digest"] = JsonNode.Parse(external.Digest.Data.CanonicalText);
        var source = ProjectSource.Create(fixture.Input.Source.Context, fixture.Input.Source.Documents, Json(declarations));
        var body = new CompilerMaterializedSnapshot(fixture.Body.SnapshotId, "1", new(external, true, true, true));
        var model = ProjectSemanticBinder.Bind(Input(settings, source), [body]);
        foreach (var entity in new[] { 10, 60, 61 }) Assert.Equal(expected, Reference(model, entity).State);
        foreach (var entity in new[] { 6, 62 }) Assert.Equal(ReferenceResolutionState.Resolved, Reference(model, entity).State);
        Assert.Contains(model.Symbols, symbol => symbol.Context.Equals(source.Context) && symbol.EntityId == EntityId.Parse(Id(10)));
    }

    [Fact]
    public void DocumentSymbolsDoNotBecomeEntityLookupCandidates()
    {
        var fixture = Fixture(); var declarations = JsonNode.Parse(fixture.Input.Source.Declarations.CanonicalText)!.AsArray();
        var port = declarations[0]!["payload"]!["ports"]![0]!;
        var type = Obj(new { kind = "coglatas.document-reference", schemaVersion = 1, nullable = false, acceptsUnknown = false, expectedKind = SourceDocument.SchemaId });
        port["type"] = type.DeepClone(); port["defaultValue"]!["type"] = type.DeepClone();
        var value = port["defaultValue"]!["value"]!.AsObject(); value.Remove("entityId"); value["documentId"] = Id(4);
        var source = ProjectSource.Create(fixture.Input.Source.Context, fixture.Input.Source.Documents, Json(declarations));
        var model = ProjectSemanticBinder.Bind(Input(JsonNode.Parse(fixture.Input.Settings.CanonicalText)!.AsObject(), source), [fixture.Body]);
        Assert.True(model.BindingComplete);
        var documentKey = Json(Obj(new { context = fixture.Body.Snapshot.Source.Context.Data.Value, documentId = Id(4) })).CanonicalText;
        Assert.Equal(documentKey, Reference(model, 10).SymbolKey);
        Assert.Single(model.Symbols, symbol => symbol.Key == documentKey);
        Assert.Equal(Key(fixture.Body.Snapshot.Source.Context, 10), Reference(model, 61).SymbolKey);
        Assert.Equal(Key(fixture.Body.Snapshot.Source.Context, 10, 10), Reference(model, 60).SymbolKey);
    }

    private static CompilerBoundReference Reference(PartialSemanticModel model, int origin) =>
        Assert.Single(model.References, reference => reference.Origin.EntityId == EntityId.Parse(Id(origin)));
    private static string Key(SourceRevisionContext context, int entity, int? port = null) =>
        Json(Obj(new { context = context.Data.Value, entityId = Id(entity), portId = port is null ? null : Id(port.Value) })).CanonicalText;

    private static (CompilerInputManifest Input, CompilerMaterializedSnapshot Body) Fixture(string kind = "committed")
    {
        var local = ReferenceCompletionTests.CompletionInput(1);
        var revision = new RevisionRef(new(local.Source.TenantId, ProjectId.Parse(Id(22)), BranchId.Parse(Id(23))), RevisionId.Parse(Id(24)));
        var context = SourceRevisionContext.Committed(revision);
        if (kind == "candidate") context = SourceRevisionContext.Candidate(new(ProposalId.Parse(Id(40)), CandidateRevisionId.Parse(Id(41)), revision, revision));
        if (kind == "scenario") context = SourceRevisionContext.Hypothetical(new(ScenarioId.Parse(Id(42)), ScenarioRevisionId.Parse(Id(43)), context, local.Source.Digest));
        var externalTask = JsonNode.Parse(local.Source.Declarations.CanonicalText)!.AsArray()[0]!.DeepClone();
        externalTask["payload"]!["ports"]![0]!["portId"] = Id(10);
        var external = ProjectSource.Create(context, local.Source.Documents, Json(new JsonArray(externalTask)));
        var declarations = JsonNode.Parse(local.Source.Declarations.CanonicalText)!.AsArray();
        var port = declarations[0]!["payload"]!["ports"]![0]!;
        port["defaultValue"] = Obj(new { type = port["type"]!.DeepClone(), state = "coglatas.known", value = new
        { tenantId = external.TenantId.ToString(), projectId = external.ProjectId.ToString(), entityId = Id(10), context = context.Data.Value,
            origin = ProjectLocation.Create(DocumentId.Parse(Id(4)), local.Source.Context, entityId: EntityId.Parse(Id(10))).Data.Value,
            manifest = new { version = "1", digest = external.Digest.Data.Value } } });
        JsonObject Test(int entity, int targetPort, SourceRevisionContext target) => Obj(new
        { entityId = Id(entity), documentId = Id(4), kind = "coglatas.project-test", schemaVersion = 1,
            payload = new { assertions = new[] { new { key = "example.target", kind = "coglatas.assert-reference-resolved",
                target = CompilerAnalysisFixtures.Reference(10, target), portId = Id(targetPort) } } } });
        declarations.Add(Test(60, 10, context));
        var requirement = CompilerAnalysisFixtures.RequirementDeclaration(context); requirement["entityId"] = Id(61);
        requirement["payload"]!["target"] = CompilerAnalysisFixtures.Reference(10, context); declarations.Add(requirement);
        declarations.Add(Test(62, 10000, local.Source.Context));
        var document = SourceDocument.Create(DocumentId.Parse(Id(4)), "project", Json(Obj(new
        { entityId = Id(6), kind = "coglatas.project", schemaVersion = 1, payload = new { roots = new[] { Id(10) } } })));
        var source = ProjectSource.Create(local.Source.Context, [document], Json(declarations));
        var settings = Settings(); settings["authorizedExternalSnapshots"] = new JsonArray(Obj(new
        { snapshotId = Id(30), version = "1", digest = external.Digest.Data.Value, context = context.Data.Value, coverage = "coglatas.complete", asOf = (string?)null }));
        return (Input(settings, source), new(ExternalSnapshotId.Parse(Id(30)), "1", new(external, true, true, true)));
    }
}
