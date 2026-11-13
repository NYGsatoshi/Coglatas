using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class DraftRenameTests
{
    internal static CompilerInputManifest Committed()
    {
        var original = ReferenceCompletionTests.CompletionInput(2);
        object Reference(int id) => new { tenantId = Id(1), projectId = Id(2), entityId = Id(id), context = original.Source.Context.Data.Value };
        var declarations = JsonNode.Parse(original.Source.Declarations.CanonicalText)!.AsArray();
        declarations.Add(Obj(new { entityId = Id(30), documentId = Id(4), kind = "coglatas.requirement", schemaVersion = 1,
            payload = new { target = Reference(10), condition = PlanDeclarationTests.Literal(true), evidenceKind = "coglatas.model-proof" } }));
        var relations = new JsonArray(Obj(new { relationId = Id(20), documentId = Id(4), kind = RelationKinds.RelatedTo, schemaVersion = 1,
            descriptor = new { descriptorId = RelationKinds.RelatedTo, version = 1 },
            source = new { kind = "coglatas.entity", expectedKind = "coglatas.task", reference = Reference(10) },
            target = new { kind = "coglatas.entity", expectedKind = "coglatas.task", reference = Reference(11) }, payload = new { } }));
        return Input(source: ProjectSource.Create(original.Source.Context, original.Source.Documents, Json(declarations), Json(relations),
            sourceReferences: Json(new JsonArray(Obj(new { kind = "coglatas.entity-reference", documentId = Id(4), entityId = Id(10) })))));
    }
    internal static ProposalContext Proposal(CompilerInputManifest committed, int candidate = 51) =>
        new(ProposalId.Parse(Id(50)), CandidateRevisionId.Parse(Id(candidate)), committed.Source.Context.CommittedRevision!, committed.Source.Context.CommittedRevision!);
    internal static CompilerInputManifest Draft() { var input = Committed(); return ProjectDraftRename.Fork(ProjectSemanticBinder.Bind(input), Proposal(input)); }
    internal static SourceRevisionContext Next(CompilerInputManifest draft, int candidate = 52) => SourceRevisionContext.Candidate(new(
        draft.Source.Context.Proposal!.ProposalId, CandidateRevisionId.Parse(Id(candidate)), draft.Source.Context.Proposal.BaseRevision, draft.Source.Context.Proposal.CapturedTargetHead));
    internal static ProjectLocation Target(CompilerInputManifest input, int entity = 10) => ProjectLocation.Create(DocumentId.Parse(Id(4)), input.Source.Context, entityId: EntityId.Parse(Id(entity)));

    [Fact]
    public void RenameIsOneImmutableDraftReplacementAndPreservesAllTypedIdentitiesAndBindings()
    {
        var input = Draft(); var model = ProjectSemanticBinder.Bind(input); Assert.True(model.BindingComplete);
        var preview = ProjectDraftRename.Preview(model, Target(input), input.InputDigest, "Renamed 🌌", Next(input));
        Assert.Equal(DraftRenameState.Ready, preview.State); var plan = Assert.IsType<ProjectDraftRenamePlan>(preview.Plan);
        Assert.Equal(3, plan.References.Count); Assert.True(plan.ReferenceCoverageComplete); Assert.Equal("Task 0", plan.PreviousName);
        Assert.Same(plan.After, plan.ApplyToDraft(input)); Assert.Null(plan.ApplyToDraft(plan.After));
        Assert.Equal(Next(input), plan.After.Source.Context); Assert.Equal(input.Settings, plan.After.Settings);
        var after = ProjectSemanticBinder.Bind(plan.After); Assert.True(after.BindingComplete);
        Assert.Equal(model.Symbols.Select(symbol => (symbol.EntityId, symbol.PortId)), after.Symbols.Select(symbol => (symbol.EntityId, symbol.PortId)));
        Assert.Equal(model.References.Count, after.References.Count); Assert.All(after.References, reference => Assert.Equal(ReferenceResolutionState.Resolved, reference.State));
        Assert.Equal("Renamed 🌌", after.Declarations.Single(item => item.Declaration?.EntityId == EntityId.Parse(Id(10))).Declaration!.DisplayName);
        Assert.Equal("Task 0", model.Declarations.Single(item => item.Declaration?.EntityId == EntityId.Parse(Id(10))).Declaration!.DisplayName);
        Assert.Equal(input.Source.SourceReferences, plan.After.Source.SourceReferences);
        Assert.Equal(input.Source.Context.Proposal!.ProposalId, plan.After.Source.Context.Proposal!.ProposalId);
    }

    [Theory]
    [InlineData("same-candidate")]
    [InlineData("proposal")]
    [InlineData("base")]
    [InlineData("head")]
    [InlineData("committed")]
    public void RejectsAnyAttemptToChangeTheCapturedDraftContract(string mode)
    {
        var input = Draft(); var proposal = input.Source.Context.Proposal!;
        var next = mode == "committed" ? Source().Context : SourceRevisionContext.Candidate(new(
            mode == "proposal" ? ProposalId.Parse(Id(99)) : proposal.ProposalId,
            mode == "same-candidate" ? proposal.CandidateRevisionId : CandidateRevisionId.Parse(Id(52)),
            mode == "base" ? new(proposal.BaseRevision.Branch, RevisionId.Parse(Id(99))) : proposal.BaseRevision,
            mode == "head" ? new(proposal.TargetBranch, RevisionId.Parse(Id(99))) : proposal.CapturedTargetHead));
        var preview = ProjectDraftRename.Preview(ProjectSemanticBinder.Bind(input), Target(input), input.InputDigest, "New", next);
        Assert.Equal(DraftRenameState.InvalidContext, preview.State); Assert.Null(preview.Plan);
    }

    [Fact]
    public void StaleInputForeignLocationAndNonDeclarationTargetsCannotCreatePlans()
    {
        var input = Draft(); var model = ProjectSemanticBinder.Bind(input);
        Assert.Equal(DraftRenameState.StaleInput, ProjectDraftRename.Preview(model, Target(input), Committed().InputDigest, "New", Next(input)).State);
        Assert.Equal(DraftRenameState.OutOfScope, ProjectDraftRename.Preview(model, Target(Committed()), input.InputDigest, "New", Next(input)).State);
        var port = model.Symbols.First(symbol => symbol.PortId is not null);
        Assert.Equal(DraftRenameState.Unavailable, ProjectDraftRename.Preview(model, port.Location, input.InputDigest, "New", Next(input)).State);
        var preview = ProjectDraftRename.Preview(model, Target(input), input.InputDigest, "New", Next(input));
        var settings = Settings(); settings["sourceCoverage"] = "coglatas.partial";
        Assert.Null(preview.Plan!.ApplyToDraft(Input(settings, input.Source)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("観測 🚀 é")]
    public void DisplayNamesUseExistingSourceUnicodeSemanticsWithoutChangingExternalKeys(string name)
    {
        var input = Draft(); var model = ProjectSemanticBinder.Bind(input);
        var plan = ProjectDraftRename.Preview(model, Target(input), input.InputDigest, name, Next(input)).Plan!;
        var renamed = PlanDeclarationReader.Read(plan.After.Source).Single(item => item.Declaration?.EntityId == EntityId.Parse(Id(10)));
        Assert.Equal(name, renamed.Declaration!.DisplayName); Assert.Equal(EntityId.Parse(Id(10)), renamed.Declaration.EntityId);
        Assert.Equal(input.Source.PackageLock, plan.After.Source.PackageLock);
    }

    [Fact]
    public void RefusesInvalidNamesAndBoundsAndHonorsCancellationBeforeAnyReplacement()
    {
        var input = Draft(); var model = ProjectSemanticBinder.Bind(input);
        Assert.Equal(DraftRenameState.NoChange, ProjectDraftRename.Preview(model, Target(input), input.InputDigest, "Task 0", Next(input)).State);
        Assert.Equal(DraftRenameState.InvalidName, ProjectDraftRename.Preview(model, Target(input), input.InputDigest, new string((char)0xd800, 1), Next(input)).State);
        Assert.Equal(DraftRenameState.ResourceLimit, ProjectDraftRename.Preview(model, Target(input), input.InputDigest, new string('x', 4097), Next(input)).State);
        var plan = ProjectDraftRename.Preview(model, Target(input), input.InputDigest, "New", Next(input)).Plan!;
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectDraftRename.Preview(model, Target(input), input.InputDigest, "New", Next(input), cancelled.Token));
        Assert.Throws<OperationCanceledException>(() => plan.ApplyToDraft(input, cancelled.Token));
        Assert.Throws<OperationCanceledException>(() => ProjectDraftRename.Fork(ProjectSemanticBinder.Bind(Committed()), Proposal(Committed()), cancelled.Token));
        Assert.Equal(input.Source.Digest, model.Input.Source.Digest);
    }

    [Fact]
    public void OpaqueDeclarationsExtensionsAndUnknownBasisArePreservedWithoutContextSearch()
    {
        var original = Committed();
        var nodes = JsonNode.Parse(original.Source.Declarations.CanonicalText)!.AsArray();
        var opaque = Obj(new { entityId = Id(40), documentId = Id(4), kind = "example.future", schemaVersion = 1,
            payload = new { context = original.Source.Context.Data.Value, displayName = "Opaque" } }); nodes.Add(opaque.DeepClone());
        var task = nodes[0]!["payload"]!["ports"]![0]!;
        task["type"] = Obj(new { kind = "coglatas.text", schemaVersion = 1, nullable = false, acceptsUnknown = true });
        task["defaultValue"] = Obj(new { type = task["type"]!.DeepClone(), state = "coglatas.unknown", reason = "coglatas.missing-input", basis = new { context = original.Source.Context.Data.Value } });
        var basis = Json(task["defaultValue"]!["basis"]!);
        var extensions = Json(Obj(new { context = original.Source.Context.Data.Value, arbitrary = "preserve" }));
        var committed = Input(source: ProjectSource.Create(original.Source.Context, original.Source.Documents, Json(nodes), original.Source.Relations,
            sourceReferences: original.Source.SourceReferences, extensions: extensions));
        var draft = ProjectDraftRename.Fork(ProjectSemanticBinder.Bind(committed), Proposal(committed));
        var plan = ProjectDraftRename.Preview(ProjectSemanticBinder.Bind(draft), Target(draft), draft.InputDigest, "New", Next(draft)).Plan!;
        Assert.False(plan.ReferenceCoverageComplete);
        var afterOpaque = plan.After.Source.Declarations.Value.EnumerateArray().Single(node => node.GetProperty("entityId").GetString() == Id(40));
        Assert.Equal(Json(opaque), SourceJson.Parse(afterOpaque.GetRawText()));
        Assert.Equal(extensions, SourceJson.Parse(plan.After.Source.Data.Value.GetProperty("extensions").GetRawText()));
        var afterTask = plan.After.Source.Declarations.Value[0];
        Assert.Equal(basis, SourceJson.Parse(afterTask.GetProperty("payload").GetProperty("ports")[0].GetProperty("defaultValue").GetProperty("basis").GetRawText()));
    }

    [Fact]
    public void RootRenameRecomputesDocumentAndManifestDigestsAndPreservesHeaders()
    {
        var input = Draft(); var model = ProjectSemanticBinder.Bind(input);
        var plan = ProjectDraftRename.Preview(model, Target(input, 6), input.InputDigest, "Project renamed", Next(input)).Plan!;
        Assert.NotEqual(input.Source.Documents[0].Digest, plan.After.Source.Documents[0].Digest);
        Assert.Equal(input.Source.Documents[0].DocumentId, plan.After.Source.Documents[0].DocumentId);
        Assert.Equal(input.Source.Documents[0].LogicalName, plan.After.Source.Documents[0].LogicalName);
        Assert.Equal(plan.After.Source.Data, ProjectSourceCodec.Decode(plan.After.Source.Data.ToCanonicalBytes()).RequireSource().Data);
    }

    [Fact]
    public void PinnedValueTargetAndManifestStayFixedWhileItsTypedOriginAdvances()
    {
        var original = ReferenceCompletionTests.CompletionInput(1); var external = Source(9);
        var declarations = JsonNode.Parse(original.Source.Declarations.CanonicalText)!.AsArray();
        var port = declarations[0]!["payload"]!["ports"]![0]!;
        port["type"] = Obj(new { kind = "coglatas.entity-reference", schemaVersion = 1, nullable = false, acceptsUnknown = false, expectedKind = "coglatas.project" });
        var pinned = Obj(new { tenantId = Id(1), projectId = Id(2), entityId = Id(6), context = external.Context.Data.Value,
            origin = Target(original).Data.Value, manifest = new { version = "1", digest = external.Digest.Data.Value } });
        port["defaultValue"] = Obj(new { type = port["type"]!.DeepClone(), state = "coglatas.known", value = pinned.DeepClone() });
        var settings = Settings(); settings["authorizedExternalSnapshots"] = new JsonArray(Obj(new { snapshotId = Id(60), version = "1",
            context = external.Context.Data.Value, digest = external.Digest.Data.Value, coverage = "coglatas.complete", asOf = (string?)null }));
        var input = Input(settings, ProjectSource.Create(original.Source.Context, original.Source.Documents, Json(declarations)));
        var materialized = new CompilerMaterializedSnapshot(ExternalSnapshotId.Parse(Id(60)), "1", new(external, true, true, true));
        var model = ProjectSemanticBinder.Bind(input, [materialized]); Assert.True(model.BindingComplete);
        var draft = ProjectDraftRename.Fork(model, Proposal(input));
        var plan = ProjectDraftRename.Preview(ProjectSemanticBinder.Bind(draft, [materialized]), Target(draft), draft.InputDigest, "New", Next(draft)).Plan!;
        var value = plan.After.Source.Declarations.Value[0].GetProperty("payload").GetProperty("ports")[0].GetProperty("defaultValue").GetProperty("value");
        Assert.Equal(external.Context.Data, SourceJson.Parse(value.GetProperty("context").GetRawText()));
        Assert.Equal(Json(pinned["manifest"]!), SourceJson.Parse(value.GetProperty("manifest").GetRawText()));
        Assert.Equal(Next(draft).Data, SourceJson.Parse(value.GetProperty("origin").GetProperty("context").GetRawText()));
        var after = ProjectSemanticBinder.Bind(plan.After, [materialized]); Assert.True(after.BindingComplete);
        Assert.Equal(ReferenceResolutionState.Resolved, Assert.Single(after.References).State);
    }

    [Fact]
    public void CanonicalByteBoundsDoNotAccidentallyBecomeSerializerEscapeBounds()
    {
        var original = ReferenceCompletionTests.CompletionInput(1);
        var extensions = SourceJson.Parse("{\"text\":\"" + new string('界', 800000) + "\"}");
        var settings = Settings(); settings["executionProfile"]!["maximumOperations"] = 2000000;
        var committed = Input(settings, ProjectSource.Create(original.Source.Context, original.Source.Documents, original.Source.Declarations, extensions: extensions));
        var draft = ProjectDraftRename.Fork(ProjectSemanticBinder.Bind(committed), Proposal(committed));
        var preview = ProjectDraftRename.Preview(ProjectSemanticBinder.Bind(draft), Target(draft), draft.InputDigest, "Renamed", Next(draft));
        Assert.Equal(DraftRenameState.Ready, preview.State);
        Assert.Equal(extensions, SourceJson.Parse(preview.Plan!.After.Source.Data.Value.GetProperty("extensions").GetRawText()));
    }

    [Fact]
    public void ForkRetainsAnExplicitOtherBranchTargetWithoutGrantingCommitAuthority()
    {
        var committed = Committed(); var target = new BranchRef(committed.Source.TenantId, committed.Source.ProjectId, BranchId.Parse(Id(90)));
        var proposal = new ProposalContext(ProposalId.Parse(Id(50)), CandidateRevisionId.Parse(Id(51)), committed.Source.Context.CommittedRevision!, new(target, RevisionId.Parse(Id(91))));
        var draft = ProjectDraftRename.Fork(ProjectSemanticBinder.Bind(committed), proposal);
        Assert.Equal(target, draft.Source.Context.Branch); Assert.Equal(committed.Source.Context.CommittedRevision, draft.Source.Context.Proposal!.BaseRevision);
        Assert.Equal(RevisionId.Parse(Id(91)), draft.Source.Context.Proposal.CapturedTargetHead.RevisionId);
        var model = ProjectSemanticBinder.Bind(draft); Assert.True(model.BindingComplete);
        Assert.All(model.References, reference => Assert.Equal(ReferenceResolutionState.Resolved, reference.State));
        Assert.NotEqual(committed.Source.Context.Branch, draft.Source.Context.Branch);
    }

    [Fact]
    public void ReferencePressureCannotProduceATruncatedReadyRenamePlan()
    {
        var original = ReferenceCompletionTests.CompletionInput(1); var refs = new JsonArray();
        var documents = original.Source.Documents.ToList();
        for (var i = 0; i < 4097; i++)
        {
            var document = i == 0 ? 4 : 100 + i;
            if (i != 0) documents.Add(SourceDocument.Create(DocumentId.Parse(Id(document)), "doc-" + i,
                Json(Obj(new { entityId = Id(10000 + i), kind = "coglatas.project", schemaVersion = 1, payload = new { } }))));
            refs.Add(Obj(new { kind = "coglatas.entity-reference", documentId = Id(document), entityId = Id(10) }));
        }
        var settings = Settings(); settings["executionProfile"]!["maximumOperations"] = 10000000;
        var committed = Input(settings, ProjectSource.Create(original.Source.Context, documents, original.Source.Declarations, sourceReferences: Json(refs)));
        var draft = ProjectDraftRename.Fork(ProjectSemanticBinder.Bind(committed), Proposal(committed));
        var model = ProjectSemanticBinder.Bind(draft); Assert.True(model.BindingComplete); Assert.Equal(4097, model.References.Count);
        var preview = ProjectDraftRename.Preview(model, Target(draft), draft.InputDigest, "New", Next(draft));
        Assert.Equal(DraftRenameState.ResourceLimit, preview.State); Assert.Null(preview.Plan);
    }

    [Fact]
    public async Task WindowsPreparationAppliesOnlyItsDisplayedPlanThenRequiresFreshCompilation()
    {
        var committed = ReferenceCompletionTests.CompletionInput(1);
        var input = ProjectDraftRename.Fork(ProjectSemanticBinder.Bind(committed), Proposal(committed));
        CompilerArtifactIdentity Artifact(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString()));
        var host = new LocalNetCompilerHost(profile); var request = new CompilerHostRequest(new(input), 1); var response = await host.CompileAsync(request);
        using var view = new SyntheticWorkbenchViewModel(); view.BeginSynthetic(request); Assert.True(view.TryAccept(response));
        Assert.True(view.Select(Target(input))); var plan = view.PreviewRename("New", Next(input))!.Plan!;
        var different = ProjectDraftRename.Preview(ProjectSemanticBinder.Bind(input), Target(input), input.InputDigest, "Other", Next(input)).Plan!;
        Assert.Null(view.ApplyPreviewedRename(different, 2)); Assert.Null(view.ApplyPreviewedRename(plan, 1));
        var next = Assert.IsType<CompilerHostRequest>(view.ApplyPreviewedRename(plan, 2));
        Assert.Equal("Waiting", view.State); Assert.Null(view.Result); Assert.Null(view.RenamePreview); Assert.Null(view.SourceTarget);
        Assert.Equal(plan.After.Source.Data, view.Source!.Data); Assert.False(view.TryAccept(response));
        Assert.True(view.TryAccept(await host.CompileAsync(next)));
        Assert.Equal(plan.After.InputDigest, view.Result!.Input.InputDigest); Assert.True(view.Select(Target(plan.After)));
        var retained = view.PreviewRename("Next", Next(plan.After, 53))!.Plan!;
        Assert.True(view.Select(Target(plan.After))); Assert.Null(view.RenamePreview); Assert.Null(view.ApplyPreviewedRename(retained, 3));
    }
}
