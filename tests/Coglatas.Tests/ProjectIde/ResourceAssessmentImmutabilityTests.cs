using System.Collections;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;
using static Coglatas.Tests.ProjectIde.CompilerAnalysisFixtures;
namespace Coglatas.Tests.ProjectIde;
public sealed class ResourceAssessmentImmutabilityTests
{
    private static (CompilerInputManifest Input, ResourceReferenceAssessment Assessment) Fixture(int revision)
    {
        var original = Source(revision);
        var declaration = Obj(new { entityId = Id(7), documentId = Id(4), kind = "coglatas.resource-binding", schemaVersion = 1,
            payload = new { tenantId = Id(1), resourceId = Id(20), snapshotVersion = "1", snapshotDigest = original.Digest.Data.Value, resourceKind = "coglatas.human" } });
        var source = ProjectSource.Create(original.Context, original.Documents, Json(new JsonArray(declaration)));
        var input = Input(AllRulesSettings(), source);
        var binding = Assert.IsType<ResourceBindingDeclaration>(Assert.Single(PlanDeclarationReader.Read(source), item => item.Declaration is ResourceBindingDeclaration).Declaration);
        var reference = new TypedProjectReference(Json(Obj(new { tenantId = Id(1), projectId = Id(2), resourceId = binding.ResourceId.ToString(),
            context = source.Context.Data.Value, origin = ProjectLocation.Create(DocumentId.Parse(Id(4)), source.Context, entityId: binding.EntityId).Data.Value,
            manifest = new { version = binding.SnapshotVersion, digest = binding.SnapshotDigest.Data.Value } })), "coglatas.resource-reference", binding.ResourceKind);
        return (input, ResourceReferenceValidator.Assess(input, reference, binding));
    }
    [Theory]
    [InlineData(9)]
    [InlineData(11)]
    public void MissingResourceDiagnosticsStayWithTheirAdmittedInput(int otherRevision)
    {
        var (input, result) = Fixture(5); var (_, other) = Fixture(otherRevision);
        var original = Assert.Single(result.Findings);
        Assert.Equal(input.InputId, original.Diagnostic.InputId);
        Assert.Equal("coglatas.analysis.resource-snapshot-unavailable", original.Diagnostic.Code);
        if (result.Findings is ICollection legacy && legacy.SyncRoot is CompilerBindingFinding[] storage)
            storage[0] = Assert.Single(other.Findings);
        Assert.Equal(ReferenceResolutionState.Unsupported, result.Resolution);
        Assert.Equal(CompilerFreshness.Undetermined, result.Freshness);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(input.InputId, finding.Diagnostic.InputId);
        Assert.Equal(input.Source.Context, finding.Diagnostic.Location.Context);
        Assert.Equal(original.Diagnostic.Data, finding.Diagnostic.Data);
        Assert.Throws<NotSupportedException>(() => ((IList<CompilerBindingFinding>)result.Findings).Clear());
    }
}
