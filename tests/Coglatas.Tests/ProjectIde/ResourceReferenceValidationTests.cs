using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;
using static Coglatas.Tests.ProjectIde.CompilerAnalysisFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class ResourceReferenceValidationTests
{
    private static (CompilerInputManifest Input, ResourceBindingDeclaration Binding, TypedProjectReference Reference) Fixture(string kind = "coglatas.human", JsonObject? settings = null)
    {
        var original = Source();
        var declaration = Obj(new { entityId = Id(7), documentId = Id(4), kind = "coglatas.resource-binding", schemaVersion = 1,
            payload = new { tenantId = Id(1), resourceId = Id(20), snapshotVersion = "1", snapshotDigest = original.Digest.Data.Value, resourceKind = kind } });
        var source = ProjectSource.Create(original.Context, original.Documents, Json(new JsonArray(declaration)));
        var input = Input(settings ?? AllRulesSettings(), source);
        var binding = Assert.IsType<ResourceBindingDeclaration>(Assert.Single(PlanDeclarationReader.Read(source), item => item.Declaration is ResourceBindingDeclaration).Declaration);
        var reference = Reference(input, binding);
        return (input, binding, reference);
    }
    private static TypedProjectReference Reference(CompilerInputManifest input, ResourceBindingDeclaration binding, Action<JsonObject>? change = null, string? expectedKind = null)
    {
        var data = Obj(new { tenantId = Id(1), projectId = Id(2), resourceId = binding.ResourceId.ToString(), context = input.Source.Context.Data.Value,
            origin = ProjectLocation.Create(DocumentId.Parse(Id(4)), input.Source.Context, entityId: binding.EntityId).Data.Value,
            manifest = new { version = binding.SnapshotVersion, digest = binding.SnapshotDigest.Data.Value } });
        change?.Invoke(data);
        return new(Json(data), "coglatas.resource-reference", expectedKind ?? binding.ResourceKind);
    }
    private static void Limited(ResourceReferenceAssessment result)
    {
        Assert.Equal(ReferenceResolutionState.Unsupported, result.Resolution); Assert.Equal(CompilerFreshness.Undetermined, result.Freshness);
        Assert.Contains(result.Findings, item => item.Diagnostic.Code == "coglatas.analysis.resource-snapshot-unavailable");
    }

    [Theory]
    [InlineData("coglatas.human", ResourceBehavior.Reusable)]
    [InlineData("coglatas.equipment", ResourceBehavior.Reusable)]
    [InlineData("coglatas.facility", ResourceBehavior.Reusable)]
    [InlineData("coglatas.material", ResourceBehavior.Consumable)]
    public void MatchingTenantResourcePinIsNotMaterializationOrReservation(string kind, ResourceBehavior behavior)
    {
        var (input, binding, reference) = Fixture(kind); var before = input.Data.CanonicalText;
        var result = ResourceReferenceValidator.Assess(input, reference, binding);
        Limited(result); Assert.Single(result.Findings); Assert.Equal(behavior, result.Binding.Behavior);
        Assert.Same(reference, result.Reference); Assert.Equal(before, input.Data.CanonicalText);
        Assert.Contains(Compile(input).Result.Diagnostics, item => item.Code == "coglatas.analysis.resource-snapshot-unavailable");
    }

    [Theory]
    [InlineData("resource", "coglatas.resource.identity-mismatch")]
    [InlineData("version", "coglatas.resource.pin-mismatch")]
    [InlineData("digest", "coglatas.resource.pin-mismatch")]
    [InlineData("kind", "coglatas.resource.kind-mismatch")]
    [InlineData("origin", "coglatas.resource.origin-mismatch")]
    [InlineData("origin-entity", "coglatas.resource.origin-mismatch")]
    [InlineData("revision", "coglatas.resource.context-unavailable")]
    [InlineData("project", "coglatas.resource.context-unavailable")]
    [InlineData("tenant", "coglatas.resource.identity-mismatch")]
    public void ResourceIdentityPinKindOriginAndContextAreIndependentlyChecked(string change, string code)
    {
        var (input, binding, _) = Fixture();
        var reference = Reference(input, binding, data =>
        {
            if (change == "resource") data["resourceId"] = Id(21);
            if (change == "version") data["manifest"]!["version"] = "2";
            if (change == "digest") data["manifest"]!["digest"]!["value"] = new string('b', 64);
            if (change == "origin") data["origin"] = Obj(ProjectLocation.Create(DocumentId.Parse(Id(99)), input.Source.Context).Data.Value);
            if (change == "origin-entity") data["origin"] = Obj(ProjectLocation.Create(DocumentId.Parse(Id(4)), input.Source.Context, entityId: EntityId.Parse(Id(99))).Data.Value);
            if (change == "revision") data["context"] = Obj(Source(9).Context.Data.Value);
            if (change is "project" or "tenant")
            {
                var context = SourceRevisionContext.Committed(new(new(TenantId.Parse(Id(change == "tenant" ? 11 : 1)),
                    ProjectId.Parse(Id(change == "project" ? 12 : 2)), BranchId.Parse(Id(3))), RevisionId.Parse(Id(5))));
                data["tenantId"] = context.Branch.TenantId.ToString(); data["projectId"] = context.Branch.ProjectId.ToString(); data["context"] = Obj(context.Data.Value);
                if (change == "tenant") data["origin"] = Obj(ProjectLocation.Create(DocumentId.Parse(Id(4)), context).Data.Value);
            }
        }, change == "kind" ? "coglatas.material" : null);
        var result = ResourceReferenceValidator.Assess(input, reference, binding); Limited(result);
        Assert.Contains(result.Findings, item => item.Diagnostic.Code == code);
        Assert.All(result.Findings, item => Assert.Equal(input.Source.Context, item.Diagnostic.Location.Context));
    }

    [Fact]
    public void FabricatedSourceBindingAndPartialCoverageStayUnresolved()
    {
        var (input, binding, reference) = Fixture(); var data = Obj(binding.Data.Value); data["entityId"] = Id(99);
        var fabricated = Assert.IsType<ResourceBindingDeclaration>(PlanDeclarationReader.Read(Json(data), binding.DocumentId).Declaration);
        var invalid = ResourceReferenceValidator.Assess(input, reference, fabricated); Limited(invalid);
        Assert.Contains(invalid.Findings, item => item.Diagnostic.Code == "coglatas.resource.binding-mismatch");
        var settings = AllRulesSettings(); settings["sourceCoverage"] = "coglatas.partial";
        (input, binding, reference) = Fixture(settings: settings);
        var partial = ResourceReferenceValidator.Assess(input, reference, binding); Limited(partial);
        Assert.Contains(partial.Findings, item => item.Diagnostic.Code == "coglatas.resource.coverage-incomplete");
    }

    [Fact]
    public void ResourceAndEntityIdentitiesCannotBeInterchanged()
    {
        var (input, binding, reference) = Fixture(); var data = Obj(reference.Data.Value); data.Remove("resourceId"); data["entityId"] = binding.EntityId.ToString();
        var entityReference = new TypedProjectReference(Json(data), "coglatas.entity-reference", "coglatas.resource-binding");
        Assert.Throws<ArgumentException>(() => ResourceReferenceValidator.Assess(input, entityReference, binding));
        Assert.NotEqual(binding.EntityId.Value, binding.ResourceId.Value);
    }

    [Fact]
    public void ResourceCancellationAndBudgetExhaustionCannotResolve()
    {
        var (input, binding, reference) = Fixture(); using var cts = new CancellationTokenSource(); cts.Cancel();
        var cancelled = ResourceReferenceValidator.Assess(input, reference, binding, cts.Token); Limited(cancelled);
        Assert.Equal(CompilerExecutionState.Cancelled, cancelled.Execution);
        var settings = AllRulesSettings(); settings["executionProfile"]!["maximumOperations"] = 1;
        (input, binding, reference) = Fixture(settings: settings);
        var exhausted = ResourceReferenceValidator.Assess(input, reference, binding); Limited(exhausted);
        Assert.Equal(CompilerExecutionState.ResourceExhausted, exhausted.Execution);
    }

    [Fact]
    public void GenericSnapshotPinOrReceiptMetadataCannotAuthorizeResourceAcquisition()
    {
        var original = Source(); var settings = AllRulesSettings();
        settings["resourceSnapshots"]!.AsArray().Add(Obj(new { snapshotId = Id(50), version = "1", digest = original.Digest.Data.Value,
            context = original.Context.Data.Value, coverage = "coglatas.complete", asOf = (string?)null,
            receipt = new { verified = true, authorized = true } }));
        Assert.Throws<UnsupportedSourceException>(() => Fixture(settings: settings));
        settings["resourceSnapshots"]![0]!.AsObject().Remove("receipt");
        var (input, binding, reference) = Fixture(settings: settings);
        Limited(ResourceReferenceValidator.Assess(input, reference, binding));
        settings["resourceSnapshots"]!.AsArray().Clear();
        var lost = Input(settings, input.Source);
        Limited(ResourceReferenceValidator.Assess(lost, reference, binding));
        Assert.Contains(Compile(lost).Result.Diagnostics, item => item.Code == "coglatas.analysis.resource-snapshot-unavailable");
    }
}
