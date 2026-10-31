using System.Text;
using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class CompilerBindingTests
{
    private static JsonObject BindingSettings()
    {
        var settings = Settings(); settings["schema"] = Artifact(ProjectSource.SchemaId); return settings;
    }
    private static CompilerInputManifest BindingInput(ProjectSource? source = null, JsonObject? settings = null) => Input(settings ?? BindingSettings(), source);
    private static ProjectSource CrossDocumentSource(bool missing = false, bool opaque = false)
    {
        var context = Source().Context;
        var project = Obj(new { entityId = Id(6), kind = "coglatas.project", schemaVersion = 1, payload = new { roots = new[] { Id(missing ? 9 : 7) } } });
        if (opaque) project["example.future"] = true;
        var task = Obj(new { entityId = Id(7), kind = "coglatas.task", schemaVersion = 1, payload = new
        {
            duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "1" },
            ports = Array.Empty<object>()
        } });
        return ProjectSource.Create(context, [SourceDocument.Create(DocumentId.Parse(Id(4)), "project", Json(project)),
            SourceDocument.Create(DocumentId.Parse(Id(8)), "task", Json(task))]);
    }

    [Fact]
    public void CanonicalParserPreservesInvalidAndUnsupportedBuffersWithoutPriorAstFallback()
    {
        var source = Source(); var bytes = Encoding.UTF8.GetBytes("not valid JSON");
        var invalid = CanonicalSourceParser.Parse(bytes, source.Context);
        Assert.Equal(SourceDecodeStatus.Invalid, invalid.Status); Assert.Null(invalid.Source); Assert.Equal(bytes, invalid.RecoverBuffer());
        var clone = invalid.RecoverBuffer(); clone[0] = 0; Assert.Equal(bytes, invalid.RecoverBuffer());
        var dsl = CanonicalSourceParser.Parse(bytes, source.Context, "example.dsl/1");
        Assert.Equal(SourceDecodeStatus.Unsupported, dsl.Status); Assert.Null(dsl.Source); Assert.Equal(bytes, dsl.RecoverBuffer());
        Assert.Equal(SourceDecodeStatus.Invalid, CanonicalSourceParser.Parse(source.Data.ToCanonicalBytes(), Source(9).Context).Status);
    }

    [Fact]
    public void MultiDocumentBindingPublishesTypedSymbolsAndStableLocations()
    {
        var source = CrossDocumentSource(); var before = source.Data.ToCanonicalBytes();
        var model = ProjectSemanticBinder.Bind(BindingInput(source));
        Assert.True(model.BindingComplete); Assert.Empty(model.Findings); Assert.Equal(2, model.Symbols.Count);
        Assert.Equal(ReferenceResolutionState.Resolved, Assert.Single(model.References).State);
        Assert.Contains(model.Symbols, symbol => symbol.EntityId == EntityId.Parse(Id(7)) && symbol.Location.DocumentId == DocumentId.Parse(Id(8)));
        Assert.Equal(before, source.Data.ToCanonicalBytes());
    }

    [Theory]
    [InlineData(true, ReferenceResolutionState.MissingWithinCompleteScope)]
    [InlineData(false, ReferenceResolutionState.UnknownDueToCoverage)]
    public void BindingAbsenceDependsOnPinnedCoverage(bool complete, ReferenceResolutionState state)
    {
        var settings = BindingSettings(); settings["sourceCoverage"] = complete ? "coglatas.complete" : "coglatas.partial";
        var model = ProjectSemanticBinder.Bind(BindingInput(CrossDocumentSource(missing: true), settings));
        Assert.Equal(state, Assert.Single(model.References).State); Assert.False(model.BindingComplete);
    }

    [Fact]
    public void OpaqueDeclarationsStayPartialAndOriginalSourceRemainsRecoverable()
    {
        var source = CrossDocumentSource(opaque: true); var model = ProjectSemanticBinder.Bind(BindingInput(source));
        Assert.False(model.BindingComplete); Assert.Contains(model.Findings, finding => finding.State == RelationFindingState.Unsupported);
        Assert.Equal(source.Data.ToCanonicalBytes(), ProjectSourceCodec.Encode(ProjectSourceCodec.Decode(source.Data.ToCanonicalBytes()).RequireSource()));
    }

    private static (CompilerInputManifest Input, CompilerMaterializedSnapshot Materialized) External(bool missing = false, bool complete = true, bool authorized = true)
    {
        var original = Source();
        var context = SourceRevisionContext.Committed(new(new(original.TenantId, ProjectId.Parse(Id(22)), BranchId.Parse(Id(23))), RevisionId.Parse(Id(24))));
        var external = ProjectSource.Create(context, original.Documents);
        var target = Obj(new { tenantId = external.TenantId.ToString(), projectId = external.ProjectId.ToString(), entityId = Id(missing ? 9 : 6), context = context.Data.Value });
        var requirement = Obj(new { entityId = Id(7), documentId = Id(4), kind = "coglatas.requirement", schemaVersion = 1, payload = new
        {
            target, evidenceKind = "coglatas.model-proof", condition = new { kind = "coglatas.literal", value = new
            { type = new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = true } }
        } });
        var source = ProjectSource.Create(original.Context, original.Documents, Json(new JsonArray(requirement)));
        var settings = BindingSettings();
        settings["authorizedExternalSnapshots"] = new JsonArray(Obj(new { snapshotId = Id(25), version = "1", digest = external.Digest.Data.Value, context = context.Data.Value,
            coverage = complete ? "coglatas.complete" : "coglatas.partial", asOf = (string?)null }));
        return (BindingInput(source, settings), new(ExternalSnapshotId.Parse(Id(25)), "1", new(external, authorized, true, true)));
    }

    [Fact]
    public void MaterializedExternalSnapshotMustMatchItsAuthorizedManifestPin()
    {
        var fixture = External();
        var bound = ProjectSemanticBinder.Bind(fixture.Input, [fixture.Materialized]);
        Assert.True(bound.BindingComplete); Assert.Equal(ReferenceResolutionState.Resolved, Assert.Single(bound.References).State);
        Assert.Equal(ReferenceResolutionState.UnknownDueToCoverage, Assert.Single(ProjectSemanticBinder.Bind(fixture.Input).References).State);
        var wrong = new CompilerMaterializedSnapshot(fixture.Materialized.SnapshotId, "2", fixture.Materialized.Snapshot);
        Assert.Contains(ProjectSemanticBinder.Bind(fixture.Input, [wrong]).Findings, finding => finding.Diagnostic.Code == "coglatas.binding.snapshot-pin-mismatch");
    }

    [Theory]
    [InlineData(true, true, ReferenceResolutionState.MissingWithinCompleteScope)]
    [InlineData(false, true, ReferenceResolutionState.UnknownDueToCoverage)]
    [InlineData(true, false, ReferenceResolutionState.OutOfScope)]
    public void ExternalCoverageAndAuthorizationDoNotCollapse(bool complete, bool authorized, ReferenceResolutionState state)
    {
        var fixture = External(missing: true, complete, authorized);
        var model = ProjectSemanticBinder.Bind(fixture.Input, [fixture.Materialized]);
        Assert.Equal(state, Assert.Single(model.References).State); Assert.Null(Assert.Single(model.References).SymbolKey);
        if (!authorized) Assert.DoesNotContain(model.Symbols, symbol => symbol.Context.Equals(fixture.Materialized.Snapshot.Source.Context));
    }

    [Fact]
    public void DuplicatePinnedMaterializationsAreAmbiguousWithoutFirstMatchResolution()
    {
        var fixture = External(); var model = ProjectSemanticBinder.Bind(fixture.Input, [fixture.Materialized, fixture.Materialized]);
        Assert.Equal(ReferenceResolutionState.Ambiguous, Assert.Single(model.References).State); Assert.False(model.BindingComplete);
    }

    [Fact]
    public void LogicalBudgetsNullInputsAndCancellationRemainExplicit()
    {
        var settings = BindingSettings(); settings["executionProfile"]!["maximumOperations"] = 1;
        var bounded = ProjectSemanticBinder.Bind(BindingInput(settings: settings));
        Assert.False(bounded.BindingComplete); Assert.Contains(bounded.Findings, finding => finding.Diagnostic.Code == "coglatas.binding.budget-exhausted");
        Assert.Contains(ProjectSemanticBinder.Bind(BindingInput(), [null]).Findings, finding => finding.State == RelationFindingState.Unknown);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectSemanticBinder.Bind(BindingInput(), cancellationToken: cancelled.Token));
    }

    [Fact]
    public void UnknownSchemaAndMissingPlatformBaselineCannotBecomeCompleteBinding()
    {
        var settings = BindingSettings(); settings["schema"]!["version"] = "2";
        Assert.False(ProjectSemanticBinder.Bind(BindingInput(settings: settings)).BindingComplete);
        settings = BindingSettings(); settings["rules"] = new JsonArray();
        Assert.Throws<ArgumentException>(() => ProjectSemanticBinder.Bind(BindingInput(settings: settings)));
    }

    [Theory]
    [InlineData(true, RelationFindingState.Invalid)]
    [InlineData(false, RelationFindingState.Unknown)]
    public void UnmappedReferenceOriginDoesNotGuessAnAvailableDocument(bool complete, RelationFindingState expected)
    {
        var original = Source();
        var references = new JsonArray(Obj(new { kind = "coglatas.entity-reference", documentId = Id(33), entityId = Id(6) }));
        var source = ProjectSource.Create(original.Context, original.Documents, sourceReferences: Json(references));
        var settings = BindingSettings(); settings["sourceCoverage"] = complete ? "coglatas.complete" : "coglatas.partial";
        var model = ProjectSemanticBinder.Bind(BindingInput(source, settings));
        Assert.Contains(model.Findings, finding => finding.State == expected && finding.Diagnostic.Code == "coglatas.binding.reference-origin");
    }

    [Fact]
    public void NumericProfileExhaustionDoesNotBecomeInvalidSourceOrCompleteBinding()
    {
        var original = Source(); var type = Obj(new { kind = "coglatas.decimal", schemaVersion = 1, nullable = false, acceptsUnknown = false });
        var task = Obj(new { entityId = Id(7), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1, payload = new
        {
            duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "1" },
            ports = new[] { new { portId = Id(8), direction = "coglatas.input", type, minimumConnections = 0, maximumConnections = 1,
                defaultValue = new { type, state = "coglatas.known", value = "12345" } } }
        } });
        var source = ProjectSource.Create(original.Context, original.Documents, Json(new JsonArray(task)));
        var settings = BindingSettings(); settings["executionProfile"]!["maximumDigits"] = 2;
        var model = ProjectSemanticBinder.Bind(BindingInput(source, settings));
        Assert.Contains(model.Findings, finding => finding.State == RelationFindingState.Unknown && finding.Diagnostic.Code == "coglatas.binding.budget-exhausted");
        Assert.Empty(model.Declarations); Assert.False(model.BindingComplete);
    }

    [Fact]
    public void ParsedReferenceValueIsNotSilentlyPromotedToCompleteBinding()
    {
        var original = Source(); var type = Obj(new { kind = "coglatas.entity-reference", schemaVersion = 1, nullable = false, acceptsUnknown = false, expectedKind = "coglatas.task" });
        var reference = Obj(new { tenantId = original.TenantId.ToString(), projectId = original.ProjectId.ToString(), entityId = Id(99), context = original.Context.Data.Value,
            origin = ProjectLocation.Create(DocumentId.Parse(Id(4)), original.Context, entityId: EntityId.Parse(Id(7))).Data.Value,
            manifest = new { version = "1", digest = original.Digest.Data.Value } });
        var task = Obj(new { entityId = Id(7), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1, payload = new
        {
            duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "1" },
            ports = new[] { new { portId = Id(8), direction = "coglatas.input", type, minimumConnections = 0, maximumConnections = 1,
                defaultValue = new { type, state = "coglatas.known", value = reference } } }
        } });
        var source = ProjectSource.Create(original.Context, original.Documents, Json(new JsonArray(task))); var before = source.Data.ToCanonicalBytes();
        Assert.All(PlanDeclarationReader.Read(source), item => Assert.Equal(ValueDecodeStatus.Decoded, item.Status));
        var model = ProjectSemanticBinder.Bind(BindingInput(source));
        Assert.False(model.BindingComplete); Assert.Contains(model.References, item => item.State == ReferenceResolutionState.Unsupported);
        Assert.Contains(model.Findings, item => item.Diagnostic.Code == "coglatas.binding.value-reference-unavailable");
        Assert.Equal(before, source.Data.ToCanonicalBytes());
    }
    [Theory]
    [InlineData("reference")]
    [InlineData("numeric")]
    public void UnknownDurationBasisRemainsOpaqueToValueBindingAndNumericProfiles(string content)
    {
        var original = Source();
        var referenceType = Obj(new { kind = "coglatas.entity-reference", schemaVersion = 1, nullable = false, acceptsUnknown = false, expectedKind = "coglatas.task" });
        var reference = Obj(new { tenantId = original.TenantId.ToString(), projectId = original.ProjectId.ToString(), entityId = Id(99), context = original.Context.Data.Value,
            origin = ProjectLocation.Create(DocumentId.Parse(Id(4)), original.Context, entityId: EntityId.Parse(Id(7))).Data.Value,
            manifest = new { version = "1", digest = original.Digest.Data.Value } });
        var basis = content == "reference" ? Obj(new { type = referenceType, state = "coglatas.known", value = reference }) :
            Obj(new { type = new { kind = "coglatas.decimal", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "12345" });
        var task = Obj(new { entityId = Id(7), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1, payload = new
        {
            duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = true },
                state = "coglatas.unknown", reason = "coglatas.not-evaluated", basis }, ports = Array.Empty<object>()
        } });
        var source = ProjectSource.Create(original.Context, original.Documents, Json(new JsonArray(task))); var before = source.Data.ToCanonicalBytes();
        Assert.All(PlanDeclarationReader.Read(source), item => Assert.Equal(ValueDecodeStatus.Decoded, item.Status));
        var settings = BindingSettings(); settings["executionProfile"]!["maximumDigits"] = 2;
        var model = ProjectSemanticBinder.Bind(BindingInput(source, settings));
        Assert.True(model.BindingComplete); Assert.Empty(model.Findings); Assert.Empty(model.References);
        Assert.Equal(before, source.Data.ToCanonicalBytes());
    }

}
