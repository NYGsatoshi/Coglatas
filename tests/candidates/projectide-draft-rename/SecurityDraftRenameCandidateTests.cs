using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using Xunit;

namespace Coglatas.Tests.ProjectIde;

/// <summary>Independent local Rename boundaries; no server authority, durable save or Native UI qualification.</summary>
public sealed class SecurityDraftRenameCandidateTests
{
    private static readonly TenantId Tenant = new(Id(1));
    private static readonly ProjectId Project = new(Id(2));
    private static readonly BranchRef Branch = new(Tenant, Project, new(Id(3)));
    private static readonly RevisionRef Basis = new(Branch, new(Id(4)));
    private static readonly DocumentId Document = new(Id(10));
    private static readonly EntityId Root = new(Id(11));
    private static readonly EntityId FirstTask = new(Id(20));
    private static readonly EntityId SecondTask = new(Id(21));
    private static readonly PortId InputPort = new(Id(30));
    private static readonly RelationId Dependency = new(Id(40));
    private static readonly ProposalContext Proposal = new(new(Id(50)), new(Id(51)), Basis, Basis);
    private const string OriginalName = "Independent Rename subject";
    private const string ChangedName = "Renamed local subject";

    [Fact]
    public void ReadyPlanBindsExactBeforeAfterAndCandidateIdentity()
    {
        var fixture = Make();
        var plan = Ready(fixture);
        Assert.Same(fixture.Input, plan.Before);
        Assert.Same(plan.After, plan.ApplyToDraft(fixture.Input));
        Assert.Equal(Target(fixture), plan.Target);
        Assert.Equal(OriginalName, plan.PreviousName);
        Assert.Equal(ChangedName, plan.NewName);
        var next = Assert.IsType<ProposalContext>(plan.After.Source.Context.Proposal);
        Assert.Equal(Proposal.ProposalId, next.ProposalId);
        Assert.Equal(Proposal.BaseRevision, next.BaseRevision);
        Assert.Equal(Proposal.CapturedTargetHead, next.CapturedTargetHead);
        Assert.NotEqual(Proposal.CandidateRevisionId, next.CandidateRevisionId);
        Assert.Equal(Next(), plan.After.Source.Context);
        Assert.NotEqual(plan.Before.InputDigest, plan.After.InputDigest);
        Assert.NotEqual(plan.Before.Source.Digest, plan.After.Source.Digest);
        Assert.Equal(plan.Before.InputDigest.Data, Property(plan.Data, "beforeInputDigest"));
        Assert.Equal(plan.After.InputDigest.Data, Property(plan.Data, "afterInputDigest"));
    }

    [Fact]
    public void RenamePreservesOriginalInputSettingsTypedIdsAndOtherDeclarations()
    {
        var fixture = Make();
        var before = Frozen(fixture.Input);
        var plan = Ready(fixture);
        Assert.Equal(fixture.Input.Settings, plan.After.Settings);
        Assert.Equal(ChangedName, Declaration(plan.After, FirstTask).DisplayName);
        Assert.Equal(FirstTask, Declaration(plan.After, FirstTask).EntityId);
        Assert.Equal(Document, Declaration(plan.After, FirstTask).DocumentId);
        Assert.Equal(Declaration(fixture.Input, FirstTask).Payload, Declaration(plan.After, FirstTask).Payload);
        Assert.Equal(Declaration(fixture.Input, SecondTask).Data, Declaration(plan.After, SecondTask).Data);
        Assert.Equal(fixture.Input.Source.Documents.Select(item => item.Data.CanonicalText),
            plan.After.Source.Documents.Select(item => item.Data.CanonicalText));
        Assert.Equal(before, Frozen(fixture.Input));
        var detached = plan.After.Source.Data.ToCanonicalBytes();
        detached[0] = 0;
        Assert.Equal((byte)'{', plan.After.Source.Data.ToCanonicalBytes()[0]);
        Assert.Equal(before, Frozen(plan.Before));
    }

    [Fact]
    public void DocumentRootRenameRecomputesDocumentAndManifestDigests()
    {
        var fixture = Make();
        var original = Frozen(fixture.Input);
        var location = ProjectLocation.Create(Document, fixture.Input.Source.Context, entityId: Root);
        var plan = RequireReady(ProjectDraftRename.Preview(fixture.Model, location, fixture.Input.InputDigest,
            "New document display name", Next()));
        var beforeDocument = Assert.Single(fixture.Input.Source.Documents);
        var afterDocument = Assert.Single(plan.After.Source.Documents);
        Assert.Equal(Document, afterDocument.DocumentId);
        Assert.NotEqual(beforeDocument.Digest, afterDocument.Digest);
        Assert.Equal("New document display name", Declaration(plan.After, Root).DisplayName);
        Assert.Equal(afterDocument.Digest.Data,
            Json(plan.After.Source.Manifest.Data.Value.GetProperty("documents")[0].GetProperty("digest")));
        Assert.Equal(plan.After.Source.Data,
            ProjectSourceCodec.Decode(plan.After.Source.Data.ToCanonicalBytes()).RequireSource().Data);
        Assert.Equal(original, Frozen(fixture.Input));
    }

    [Fact]
    public void StructuralReferencesAdvanceWhileTypedTargetsAndManifestsRemainPinned()
    {
        var pinnedContext = SourceRevisionContext.Committed(new(Branch, new(Id(70))));
        var fixture = Make(valueTarget: pinnedContext);
        var original = Frozen(fixture.Input);
        var plan = Ready(fixture);
        var relation = Assert.Single(TypedRelationReader.Read(plan.After.Source)).Relation;
        Assert.NotNull(relation);
        Assert.Equal(Dependency, relation!.RelationId);
        Assert.Equal(Next(), relation.Source.Reference.Context);
        Assert.Equal(Next(), relation.Target.Reference.Context);
        Assert.Equal(FirstTask, relation.Source.Reference.EntityId);
        Assert.Equal(SecondTask, relation.Target.Reference.EntityId);
        var beforeValue = Assert.IsType<TaskPlanDeclaration>(Declaration(fixture.Input, FirstTask)).Ports[0].DefaultValue!;
        var afterValue = Assert.IsType<TaskPlanDeclaration>(Declaration(plan.After, FirstTask)).Ports[0].DefaultValue!;
        var beforeReference = Assert.IsType<TypedProjectReference>(beforeValue.KnownValue);
        var afterReference = Assert.IsType<TypedProjectReference>(afterValue.KnownValue);
        Assert.Equal(pinnedContext, afterReference.Context);
        Assert.Equal(Next(), afterReference.Origin.Context);
        Assert.Equal(beforeReference.Origin.DocumentId, afterReference.Origin.DocumentId);
        Assert.Equal(beforeReference.Origin.PropertyPath, afterReference.Origin.PropertyPath);
        Assert.Equal(Property(Json(beforeValue.Data.Value.GetProperty("value")), "manifest"),
            Property(Json(afterValue.Data.Value.GetProperty("value")), "manifest"));
        Assert.False(plan.ReferenceCoverageComplete);
        Assert.Equal(original, Frozen(fixture.Input));
    }

    [Fact]
    public void OpaqueSourceObjectsAndUnknownDeclarationsRemainExact()
    {
        var fixture = Make(opaque: true);
        var original = Frozen(fixture.Input);
        var plan = Ready(fixture);
        foreach (var field in new[] { "extensionDeclarations", "packageLock", "sourceReferences", "opaqueMetadata" })
            Assert.Equal(Property(fixture.Input.Source.Data, field), Property(plan.After.Source.Data, field));
        var opaqueBefore = fixture.Input.Source.Declarations.Value.EnumerateArray().Single(item => item.GetProperty("kind").GetString() == "coglatas.security-rename.future");
        var opaqueAfter = plan.After.Source.Declarations.Value.EnumerateArray().Single(item => item.GetProperty("kind").GetString() == "coglatas.security-rename.future");
        Assert.Equal(Json(opaqueBefore), Json(opaqueAfter));
        Assert.Equal(OriginalName, opaqueAfter.GetProperty("payload").GetProperty("displayName").GetString());
        Assert.False(plan.ReferenceCoverageComplete);
        Assert.Equal(original, Frozen(fixture.Input));
    }

    [Fact]
    public void UnchangedDisplayNameReturnsNoPlanOrInputMutation()
    {
        var fixture = Make();
        var original = Frozen(fixture.Input);
        Refused(Preview(fixture, name: OriginalName), DraftRenameState.NoChange);
        Assert.Equal(original, Frozen(fixture.Input));
        Assert.Equal(ChangedName, Ready(fixture).NewName);
    }

    [Fact]
    public void StaleExpectedInputAndForeignLocationsAreRefused()
    {
        var fixture = Make();
        var original = Frozen(fixture.Input);
        Refused(ProjectDraftRename.Preview(fixture.Model, Target(fixture), Digest("unrelated-input"), ChangedName, Next()),
            DraftRenameState.StaleInput);
        foreach (var context in new[]
        {
            Next(),
            SourceRevisionContext.Committed(Basis),
            SourceRevisionContext.Committed(new(new(new(Id(101)), Project, new(Id(102))), new(Id(103))))
        })
            Refused(ProjectDraftRename.Preview(fixture.Model, ProjectLocation.Create(Document, context, entityId: FirstTask),
                fixture.Input.InputDigest, ChangedName, Next()), DraftRenameState.OutOfScope);
        Assert.Equal(original, Frozen(fixture.Input));
    }

    [Fact]
    public void ProposalBaseTargetAndCandidateDriftCannotCreateARenamePlan()
    {
        var fixture = Make();
        var original = Frozen(fixture.Input);
        foreach (var next in new[]
        {
            Proposal,
            new ProposalContext(new(Id(60)), new(Id(61)), Basis, Basis),
            new ProposalContext(Proposal.ProposalId, new(Id(61)), new(Branch, new(Id(62))), Basis),
            new ProposalContext(Proposal.ProposalId, new(Id(61)), Basis, new(Branch, new(Id(63)))),
            new ProposalContext(Proposal.ProposalId, new(Id(61)), Basis, new(new(Tenant, Project, new(Id(64))), new(Id(65))))
        })
            Refused(Preview(fixture, next: SourceRevisionContext.Candidate(next)), DraftRenameState.InvalidContext);
        Assert.Equal(original, Frozen(fixture.Input));
    }

    [Fact]
    public void CommittedAndScenarioIdentitiesCannotMasqueradeAsCandidates()
    {
        var candidate = Make();
        var committed = Make(context: SourceRevisionContext.Committed(Basis));
        var scenarioContext = SourceRevisionContext.Hypothetical(new(new(Id(80)), new(Id(81)),
            SourceRevisionContext.Committed(Basis), Digest("independent-overlay")));
        var scenario = Make(context: scenarioContext);
        Refused(Preview(committed), DraftRenameState.InvalidContext);
        Refused(Preview(scenario), DraftRenameState.InvalidContext);
        Refused(Preview(candidate, next: SourceRevisionContext.Committed(Basis)), DraftRenameState.InvalidContext);
        Refused(Preview(candidate, next: scenarioContext), DraftRenameState.InvalidContext);
        Assert.Equal(Proposal, candidate.Input.Source.Context.Proposal);
    }

    [Fact]
    public void PinnedAndUnresolvedReferenceContextsCannotBeReallocated()
    {
        foreach (var field in new[] { "authorizedExternalSnapshots", "resourceSnapshots", "allocationSnapshots", "observations", "evidence" })
        {
            var settings = Node(Settings());
            settings[field] = new JsonArray(JsonSerializer.SerializeToNode(Pin(Next(), Digest("reserved-context"))));
            var fixture = Make(settings: Json(settings));
            Refused(Preview(fixture), DraftRenameState.ContextConflict);
        }
        var unresolved = Make(valueTarget: Next());
        var original = Frozen(unresolved.Input);
        Refused(Preview(unresolved), DraftRenameState.ContextConflict);
        Assert.False(unresolved.Model.BindingComplete);
        Assert.Equal(original, Frozen(unresolved.Input));
    }

    [Fact]
    public void UnknownTargetsAndPortsDoNotBecomeDeclarationRenames()
    {
        var fixture = Make(opaque: true);
        var original = Frozen(fixture.Input);
        foreach (var location in new[]
        {
            ProjectLocation.Create(Document, fixture.Input.Source.Context, entityId: new(Id(999))),
            ProjectLocation.Create(Document, fixture.Input.Source.Context, entityId: new(Id(90))),
            ProjectLocation.Create(Document, fixture.Input.Source.Context, "/payload/ports/" + InputPort, entityId: FirstTask),
            ProjectLocation.Create(new(Id(998)), fixture.Input.Source.Context, entityId: FirstTask)
        })
            Refused(ProjectDraftRename.Preview(fixture.Model, location, fixture.Input.InputDigest, ChangedName, Next()),
                DraftRenameState.Unavailable);
        Assert.Equal(original, Frozen(fixture.Input));
    }

    [Fact]
    public void NameValidationEnforcesUnicodeAndExactUtf8ByteLimit()
    {
        var fixture = Make();
        var original = Frozen(fixture.Input);
        var exact = new string('\u00e9', ProjectDraftRename.MaximumNameBytes / 2);
        Assert.Equal(ProjectDraftRename.MaximumNameBytes, Encoding.UTF8.GetByteCount(exact));
        Assert.Equal(exact, RequireReady(Preview(fixture, name: exact)).NewName);
        Refused(Preview(fixture, name: exact + "x"), DraftRenameState.ResourceLimit);
        Refused(Preview(fixture, name: "\ud800"), DraftRenameState.InvalidName);
        Assert.Equal(original, Frozen(fixture.Input));
    }

    [Fact]
    public void ForkRequiresExactCommittedBaseAndRetainsCapturedTarget()
    {
        var committed = Make(context: SourceRevisionContext.Committed(Basis));
        var original = Frozen(committed.Input);
        var otherTarget = new RevisionRef(new(Tenant, Project, new(Id(110))), new(Id(111)));
        var proposal = new ProposalContext(new(Id(112)), new(Id(113)), Basis, otherTarget);
        var fork = ProjectDraftRename.Fork(committed.Model, proposal);
        Assert.Equal(proposal, fork.Source.Context.Proposal);
        Assert.Equal(otherTarget.Branch, fork.Source.Context.Branch);
        Assert.Equal(committed.Input.Settings, fork.Settings);
        Assert.Equal(Declaration(committed.Input, FirstTask).Data, Declaration(fork, FirstTask).Data);
        Assert.Throws<ArgumentException>(() => ProjectDraftRename.Fork(committed.Model,
            new(proposal.ProposalId, new(Id(114)), new(Branch, new(Id(115))), otherTarget)));
        Assert.Throws<ArgumentException>(() => ProjectDraftRename.Fork(ProjectSemanticBinder.Bind(fork), proposal));
        var settings = Node(Settings());
        settings["evidence"] = new JsonArray(JsonSerializer.SerializeToNode(Pin(SourceRevisionContext.Candidate(proposal), Digest("occupied-fork"))));
        var pinned = Make(context: SourceRevisionContext.Committed(Basis), settings: Json(settings));
        Assert.Throws<ArgumentException>(() => ProjectDraftRename.Fork(pinned.Model, proposal));
        Assert.Equal(original, Frozen(committed.Input));
    }

    [Fact]
    public void ApplyRequiresExactSourceSettingsAndCurrentCandidate()
    {
        var fixture = Make();
        var plan = Ready(fixture);
        var before = Frozen(fixture.Input);
        var after = Frozen(plan.After);
        var settings = Node(Settings());
        settings["executionProfile"]!["maximumOperations"] = 190001;
        foreach (var changed in new[]
        {
            Make(name: "different current name"),
            Make(context: Next()),
            Make(settings: Json(settings)),
            Make(duration: 24)
        })
            Assert.Null(plan.ApplyToDraft(changed.Input));
        var identical = new CompilerInputTransfer(fixture.Input).Materialize();
        Assert.NotSame(fixture.Input, identical);
        Assert.Same(plan.After, plan.ApplyToDraft(identical));
        Assert.Equal(before, Frozen(fixture.Input));
        Assert.Equal(after, Frozen(plan.After));
    }

    [Fact]
    public void CancelledCorePreviewApplyAndForkPreserveOriginalInputs()
    {
        var fixture = Make();
        var plan = Ready(fixture);
        var committed = Make(context: SourceRevisionContext.Committed(Basis));
        var before = Frozen(fixture.Input);
        var after = Frozen(plan.After);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectDraftRename.Preview(fixture.Model, Target(fixture),
            fixture.Input.InputDigest, ChangedName, Next(), cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => plan.ApplyToDraft(fixture.Input, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => ProjectDraftRename.Fork(committed.Model, Proposal, cancellation.Token));
        Assert.Equal(before, Frozen(fixture.Input));
        Assert.Equal(after, Frozen(plan.After));
        Assert.Same(plan.After, plan.ApplyToDraft(fixture.Input));
    }

    [Fact]
    public async Task RetainedPlanAndLegacyReferenceRostersRejectMutation()
    {
        using var fixture = await OpenView();
        var view = fixture.View;
        var plan = RequireReady(view.PreviewRename(ChangedName, Next())!);
        var search = Assert.IsType<SyntheticWorkbenchViewModel.ReferenceSearchResult>(view.FindReferences());
        var source = Frozen(fixture.Input);
        var result = view.Result;
        var inspection = view.Inspection;
        var originalPlan = plan.Data.ToCanonicalBytes();
        var originals = search.References.ToArray();
        Assert.NotEmpty(plan.References);
        Assert.False(search.Truncated);
        Assert.True(search.CoverageComplete);
        AssertFrozenRoster(plan.References);
        AssertFrozenRoster(search.References);
        Assert.Equal(originals, search.References.ToArray());
        Assert.Equal(originals, view.FindReferences()!.References.ToArray());
        Assert.Equal(originalPlan, plan.Data.ToCanonicalBytes());
        Assert.Same(result, view.Result);
        Assert.Same(inspection, view.Inspection);
        Assert.Equal(source, Frozen(fixture.Input));
    }

    [Fact]
    public async Task ViewModelApplyRequiresAFreshCompilerResponse()
    {
        using var fixture = await OpenView();
        var original = Frozen(fixture.Input);
        var plan = RequireReady(fixture.View.PreviewRename(ChangedName, Next())!);
        var request = Assert.IsType<CompilerHostRequest>(fixture.View.ApplyPreviewedRename(plan, 2));
        Assert.Equal(plan.After.InputDigest, request.Input.ExpectedInputDigest);
        Assert.Equal(fixture.Request.Materializations, request.Materializations);
        Assert.Equal("Waiting", fixture.View.State);
        Assert.Null(fixture.View.Result);
        Assert.Null(fixture.View.Selected);
        Assert.Null(fixture.View.RenamePreview);
        Assert.False(fixture.View.TryAccept(fixture.Response));
        var response = await Compile(request);
        Assert.True(fixture.View.TryAccept(response));
        Assert.Equal(plan.After.Source.Data, fixture.View.Source!.Data);
        Assert.Equal(plan.After.InputDigest, fixture.View.Result!.Input.InputDigest);
        Assert.Null(fixture.View.ApplyPreviewedRename(plan, 3));
        Assert.Equal(original, Frozen(fixture.Input));
    }

    [Fact]
    public async Task ViewModelRejectsCopiedPlansSelectionDriftAndOldGenerations()
    {
        using var fixture = await OpenView();
        var view = fixture.View;
        var plan = RequireReady(view.PreviewRename(ChangedName, Next())!);
        var copied = Ready(new(fixture.Input, ProjectSemanticBinder.Bind(fixture.Input)));
        Assert.NotSame(plan, copied);
        Assert.Equal(plan.Data, copied.Data);
        var result = view.Result;
        Assert.Null(view.ApplyPreviewedRename(copied, 2));
        Assert.Null(view.ApplyPreviewedRename(plan, 1));
        Assert.Same(plan, view.RenamePreview!.Plan);
        Assert.True(view.Select(ProjectLocation.Create(Document, fixture.Input.Source.Context, entityId: SecondTask)));
        Assert.Null(view.RenamePreview);
        Assert.Null(view.ApplyPreviewedRename(plan, 2));
        Assert.Same(result, view.Result);
        Assert.Equal(fixture.Input.Source.Data, view.Source!.Data);
    }

    [Fact]
    public async Task PreviewCancellationWithdrawsOnlyItsPublishedPlan()
    {
        using var fixture = await OpenView();
        using var cancellation = new CancellationTokenSource();
        ProjectDraftRenamePlan? retained = null;
        using var notification = new Notification(fixture.View, () => fixture.View.RenamePreview is not null, () =>
        {
            retained = fixture.View.RenamePreview!.Plan;
            cancellation.Cancel();
        });
        var result = fixture.View.Result;
        Assert.Throws<OperationCanceledException>(() => fixture.View.PreviewRename(ChangedName, Next(), cancellation.Token));
        Assert.True(notification.Fired);
        Assert.NotNull(retained);
        Assert.Null(fixture.View.RenamePreview);
        Assert.Same(result, fixture.View.Result);
        Assert.Equal(fixture.Input.Source.Data, fixture.View.Source!.Data);
        Assert.Same(retained!.After, retained.ApplyToDraft(fixture.Input));
    }

    [Fact]
    public async Task PreviewCancellationPreservesSynchronousNewerSelection()
    {
        using var fixture = await OpenView();
        using var cancellation = new CancellationTokenSource();
        DraftRenamePreview? newer = null;
        using var notification = new Notification(fixture.View, () => fixture.View.RenamePreview is not null, () =>
        {
            Assert.True(fixture.View.Select(ProjectLocation.Create(Document, fixture.Input.Source.Context, entityId: SecondTask)));
            newer = fixture.View.PreviewRename("Newer second task", Next(53));
            Assert.Equal(DraftRenameState.Ready, newer!.State);
            cancellation.Cancel();
        });
        Assert.Throws<OperationCanceledException>(() => fixture.View.PreviewRename(ChangedName, Next(), cancellation.Token));
        Assert.True(notification.Fired);
        Assert.Same(newer, fixture.View.RenamePreview);
        Assert.Equal(SecondTask, fixture.View.Selected!.Symbol.EntityId);
        Assert.Equal("Newer second task", fixture.View.RenamePreview!.Plan!.NewName);
        Assert.Equal(fixture.Input.Source.Data, fixture.View.Source!.Data);
    }

    [Fact]
    public async Task CancellationBeforeSourcePublicationLeavesNoReplacementContext()
    {
        using var fixture = await OpenView();
        var plan = RequireReady(fixture.View.PreviewRename(ChangedName, Next())!);
        var before = Frozen(fixture.Input);
        var after = Frozen(plan.After);
        using var cancellation = new CancellationTokenSource();
        using var notification = new Notification(fixture.View, () => fixture.View.State == "Empty", cancellation.Cancel);
        Assert.Throws<OperationCanceledException>(() => fixture.View.ApplyPreviewedRename(plan, 2, cancellation.Token));
        Assert.True(notification.Fired);
        Assert.Equal("Empty", fixture.View.State);
        Assert.Null(fixture.View.Source);
        Assert.Null(fixture.View.Result);
        Assert.Null(fixture.View.RenamePreview);
        Assert.False(fixture.View.TryAccept(fixture.Response));
        Assert.Equal(before, Frozen(fixture.Input));
        Assert.Equal(after, Frozen(plan.After));
    }

    [Fact]
    public async Task PrepublicationSupersessionPreservesTheNewerContext()
    {
        foreach (var cancelOlder in new[] { false, true })
        {
            using var fixture = await OpenView();
            var plan = RequireReady(fixture.View.PreviewRename(ChangedName, Next())!);
            var replacement = Make(context: Next(54), name: "Newer admitted input");
            var request = new CompilerHostRequest(new CompilerInputTransfer(replacement.Input), 3);
            var response = await Compile(request);
            using var cancellation = new CancellationTokenSource();
            using var notification = new Notification(fixture.View, () => fixture.View.State == "Empty", () =>
            {
                fixture.View.BeginSynthetic(request);
                Assert.True(fixture.View.TryAccept(response));
                Assert.True(fixture.View.Select(Target(replacement)));
                if (cancelOlder) cancellation.Cancel();
            });
            if (cancelOlder)
                Assert.Throws<OperationCanceledException>(() => fixture.View.ApplyPreviewedRename(plan, 2, cancellation.Token));
            else Assert.Null(fixture.View.ApplyPreviewedRename(plan, 2, cancellation.Token));
            Assert.True(notification.Fired);
            Assert.Equal("Ready", fixture.View.State);
            Assert.Same(response.Compilation!.Result, fixture.View.Result);
            Assert.Equal(replacement.Input.Source.Data, fixture.View.Source!.Data);
            Assert.Equal(replacement.Input.Source.Context, fixture.View.Selected!.Symbol.Context);
            Assert.False(fixture.View.TryAccept(fixture.Response));
            Assert.NotEqual(plan.After.InputDigest, fixture.View.Result!.Input.InputDigest);
        }
    }

    [Fact]
    public async Task CancellationAfterSourcePublicationRetainsTheLocalCommitBoundary()
    {
        using var fixture = await OpenView();
        var plan = RequireReady(fixture.View.PreviewRename(ChangedName, Next())!);
        using var cancellation = new CancellationTokenSource();
        using var notification = new Notification(fixture.View,
            () => fixture.View.State == "Waiting" && fixture.View.Source?.Data.Equals(plan.After.Source.Data) == true,
            cancellation.Cancel);
        var request = Assert.IsType<CompilerHostRequest>(fixture.View.ApplyPreviewedRename(plan, 2, cancellation.Token));
        Assert.True(notification.Fired);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(plan.After.Source.Data, fixture.View.Source!.Data);
        Assert.Equal("Waiting", fixture.View.State);
        Assert.Null(fixture.View.Result);
        Assert.False(fixture.View.TryAccept(fixture.Response));
        Assert.True(fixture.View.TryAccept(await Compile(request)));
        Assert.Equal(plan.After.InputDigest, fixture.View.Result!.Input.InputDigest);
        Assert.Equal(OriginalName, Declaration(fixture.Input, FirstTask).DisplayName);
    }

    [Fact]
    public void MaterializationReadsOwnTheirRostersAndExcludeDeniedBodies()
    {
        var external = Make(context: SourceRevisionContext.Committed(new(Branch, new(Id(120)))), name: "External synthetic body");
        var settings = Node(Settings());
        settings["authorizedExternalSnapshots"] = new JsonArray(JsonSerializer.SerializeToNode(Pin(external.Input.Source.Context, external.Input.Source.Digest)));
        var fixture = Make(settings: Json(settings));
        var snapshot = new CompilerMaterializedSnapshot(new(Id(130)), "rename-fixed-1", new(external.Input.Source, true, true, true));
        var submitted = new[] { snapshot };
        var exported = SyntheticSnapshotMaterialization.Export(fixture.Input, submitted);
        var retained = SyntheticSnapshotMaterialization.Read(fixture.Input, exported);
        var original = Assert.Single(retained);
        Array.Clear(submitted);
        AssertFrozenRoster(retained);
        Assert.Same(original, Assert.Single(retained));
        Assert.Equal(external.Input.Source.Data, retained[0].Snapshot.Source.Data);
        Assert.Equal(external.Input.Source.Data,
            Assert.Single(SyntheticSnapshotMaterialization.Read(fixture.Input, exported)).Snapshot.Source.Data);
        var denied = new CompilerMaterializedSnapshot(snapshot.SnapshotId, snapshot.Version, new(external.Input.Source, false, true, true));
        var unavailable = SyntheticSnapshotMaterialization.Export(fixture.Input, [denied]);
        var entry = Assert.Single(unavailable.Value.GetProperty("entries").EnumerateArray());
        Assert.Equal("Unavailable", entry.GetProperty("admission").GetString());
        Assert.False(entry.TryGetProperty("source", out _));
        Assert.DoesNotContain("External synthetic body", unavailable.CanonicalText, StringComparison.Ordinal);
        var deniedRead = Assert.Single(SyntheticSnapshotMaterialization.Read(fixture.Input, unavailable));
        Assert.False(deniedRead.Snapshot.IsAuthorized);
        Assert.False(deniedRead.Snapshot.DeclarationsComplete);
        Assert.False(deniedRead.Snapshot.RelationsComplete);
        Assert.Throws<FormatException>(() => SyntheticSnapshotMaterialization.Read(Make(name: "Changed input").Input, exported));
    }

    private sealed record Fixture(CompilerInputManifest Input, PartialSemanticModel Model);
    private static Guid Id(int number) => Guid.Parse("b5193000-0000-4000-8000-" + number.ToString("x12", CultureInfo.InvariantCulture));
    private static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private static JsonObject Node(SourceJson value) => JsonNode.Parse(value.CanonicalText)!.AsObject();
    private static SourceJson Property(SourceJson value, string name) => Json(value.Value.GetProperty(name));
    private static ContentDigest Digest(string value) => ContentDigest.Compute("coglatas.security-rename-fixture/1", Encoding.UTF8.GetBytes(value));
    private static SourceRevisionContext Next(int candidate = 52) => SourceRevisionContext.Candidate(new(Proposal.ProposalId, new(Id(candidate)), Basis, Basis));
    private static string Frozen(CompilerInputManifest input) => input.Data.CanonicalText + "\n" + input.Source.Data.CanonicalText + "\n" + input.Settings.CanonicalText;
    private static ProjectLocation Target(Fixture fixture) => ProjectLocation.Create(Document, fixture.Input.Source.Context, entityId: FirstTask);
    private static DraftRenamePreview Preview(Fixture fixture, string name = ChangedName, SourceRevisionContext? next = null) =>
        ProjectDraftRename.Preview(fixture.Model, Target(fixture), fixture.Input.InputDigest, name, next ?? Next());
    private static ProjectDraftRenamePlan Ready(Fixture fixture) => RequireReady(Preview(fixture));
    private static ProjectDraftRenamePlan RequireReady(DraftRenamePreview preview)
    { Assert.Equal(DraftRenameState.Ready, preview.State); return Assert.IsType<ProjectDraftRenamePlan>(preview.Plan); }
    private static void Refused(DraftRenamePreview preview, DraftRenameState state)
    { Assert.Equal(state, preview.State); Assert.Null(preview.Plan); }
    private static PlanDeclaration Declaration(CompilerInputManifest input, EntityId id) =>
        Assert.Single(PlanDeclarationReader.Read(input).Where(item => item.Declaration?.EntityId == id)).Declaration!;
    private static SourceJson Type(string kind) => Json(new { kind, schemaVersion = 1, nullable = false, acceptsUnknown = true });
    private static SourceJson Known(string kind, object value) => Json(new { type = Type(kind).Value, state = "coglatas.known", value });
    private static object Pin(SourceRevisionContext context, ContentDigest digest) => new
    {
        snapshotId = new ExternalSnapshotId(Id(130)).ToString(), version = "rename-fixed-1",
        digest = digest.Data.Value, context = context.Data.Value, coverage = "coglatas.complete", asOf = (string?)null
    };

    private static SourceJson Settings()
    {
        object Artifact(string id) => new { artifactId = id, version = "1", digest = Digest(id).Data.Value };
        object None() => new { kind = "coglatas.none" };
        return Json(new
        {
            schema = Artifact(ProjectSource.SchemaId), compiler = Artifact("coglatas.security-rename.compiler"),
            ruleset = Artifact("coglatas.security-rename.ruleset"), semantics = Artifact("coglatas.semantic-model"),
            ir = Artifact("coglatas.project-ir"), evaluator = Artifact("coglatas.security-rename.evaluator"),
            authorizedExternalSnapshots = Array.Empty<object>(), resourceSnapshots = Array.Empty<object>(), allocationSnapshots = Array.Empty<object>(),
            observations = Array.Empty<object>(), evidence = Array.Empty<object>(),
            descriptors = RelationKindDescriptor.BuiltIns.Select(item => new { artifactId = item.DescriptorId, version = "1", digest = item.Digest.Data.Value }).ToArray(),
            units = Array.Empty<object>(), calendars = Array.Empty<object>(), timeZones = Array.Empty<object>(),
            solver = None(), stochastic = None(), importer = None(), scheduler = None(), scenario = None(),
            rules = new[] { CoreCompilerRules.Schema, CoreCompilerRules.Declarations, CoreCompilerRules.Relations }
                .Select(ruleId => new { ruleId, version = 1, required = true, configuration = new { } }).ToArray(),
            featureSet = RelationKindDescriptor.BuiltIns.SelectMany(item => item.RequiredCapabilities).Prepend("coglatas.core.structural").Distinct(StringComparer.Ordinal).ToArray(),
            targets = new[] { "coglatas.semantic-model" }, analysisScope = new[] { "coglatas.source-structure" }, sourceCoverage = "coglatas.complete",
            executionProfile = new { purpose = "coglatas.normal", maximumOperations = 190000, maximumDeclarations = 1024,
                maximumRelations = 1024, maximumDigits = 32, maximumScale = 8, rounding = "coglatas.ties-to-even" },
            clock = new { kind = "coglatas.not-applicable" }
        });
    }

    private static Fixture Make(SourceRevisionContext? context = null, string name = OriginalName, int duration = 23,
        SourceJson? settings = null, SourceRevisionContext? valueTarget = null, bool opaque = false)
    {
        context ??= SourceRevisionContext.Candidate(Proposal);
        var document = SourceDocument.Create(Document, "independent-rename-document", Json(new
        {
            entityId = Root.ToString(), kind = "coglatas.project", schemaVersion = 1,
            displayName = "Independent document", payload = new { }
        }));
        var port = new Dictionary<string, object?>
        {
            ["portId"] = InputPort.ToString(), ["direction"] = "coglatas.input", ["minimumConnections"] = 0, ["maximumConnections"] = 1,
            ["type"] = Type("coglatas.boolean").Value, ["defaultValue"] = Known("coglatas.boolean", true).Value
        };
        if (valueTarget is not null)
        {
            var type = Json(new { kind = "coglatas.entity-reference", schemaVersion = 1, nullable = false, acceptsUnknown = true, expectedKind = "coglatas.task" });
            port["type"] = type.Value;
            port["defaultValue"] = Json(new
            {
                type = type.Value, state = "coglatas.known", value = new
                {
                    tenantId = Tenant.ToString(), projectId = Project.ToString(), entityId = SecondTask.ToString(), context = valueTarget.Data.Value,
                    origin = ProjectLocation.Create(Document, context, "/payload/ports/" + InputPort + "/defaultValue", entityId: FirstTask).Data.Value,
                    manifest = new { version = "rename-reference-1", digest = Digest("pinned-target").Data.Value }
                }
            }).Value;
        }
        var declarations = new List<object>
        {
            new { entityId = FirstTask.ToString(), documentId = Document.ToString(), kind = "coglatas.task", schemaVersion = 1, displayName = name,
                payload = new { duration = Known("coglatas.elapsed-duration", duration.ToString(CultureInfo.InvariantCulture)).Value, ports = new[] { port } } },
            new { entityId = SecondTask.ToString(), documentId = Document.ToString(), kind = "coglatas.task", schemaVersion = 1, displayName = "Other independent task",
                payload = new { duration = Known("coglatas.elapsed-duration", "41").Value, ports = Array.Empty<object>() } }
        };
        if (opaque) declarations.Add(new { entityId = new EntityId(Id(90)).ToString(), documentId = Document.ToString(),
            kind = "coglatas.security-rename.future", schemaVersion = 1,
            payload = new { displayName = OriginalName, context = context.Data.Value } });
        object Endpoint(EntityId id) => new { kind = "coglatas.entity", expectedKind = "coglatas.task",
            reference = new { tenantId = Tenant.ToString(), projectId = Project.ToString(), entityId = id.ToString(), context = context.Data.Value } };
        var relations = Json(new[] { new { relationId = Dependency.ToString(), documentId = Document.ToString(), kind = RelationKinds.DependsOn,
            schemaVersion = 1, descriptor = new { descriptorId = RelationKinds.DependsOn, version = 1 },
            source = Endpoint(FirstTask), target = Endpoint(SecondTask),
            payload = new { mode = "coglatas.finish-to-start", lag = Known("coglatas.elapsed-duration", "0").Value } } });
        var source = ProjectSource.Create(context, [document], Json(declarations), relations,
            opaque ? Json(new[] { new { @namespace = "coglatas.security-rename.opaque", displayName = OriginalName, context = context.Data.Value } }) : null,
            opaque ? Json(new[] { new { artifactId = "coglatas.security-rename.package", displayName = OriginalName } }) : null,
            opaque ? Json(new[] { new { documentId = Document.ToString(), kind = "coglatas.security-rename.reference", displayName = OriginalName } }) : null,
            opaque ? Json(new { opaqueMetadata = new { displayName = OriginalName, context = context.Data.Value } }) : null);
        var effectiveSettings = settings ?? Settings();
        if (context.Scenario is { } scenario)
        {
            var node = Node(effectiveSettings);
            node["scenario"] = JsonSerializer.SerializeToNode(new { baseContext = scenario.BaseContext.Data.Value,
                overlayDigest = scenario.OverlayDigest.Data.Value, assumptions = Array.Empty<object>() });
            effectiveSettings = Json(node);
        }
        var decoded = ProjectSourceCodec.Decode(source.Data.ToCanonicalBytes()).RequireSource();
        Assert.Equal(source.Data, decoded.Data);
        var input = new CompilerInputManifest(source, effectiveSettings);
        var model = ProjectSemanticBinder.Bind(input);
        Assert.Equal(source.Declarations.Value.GetArrayLength() + source.Documents.Count, model.Declarations.Count);
        Assert.Equal(source.Relations.Value.GetArrayLength(), model.Relations.Count);
        return new(input, model);
    }

    private static void AssertFrozenRoster<T>(IReadOnlyList<T> roster)
    {
        Assert.NotEmpty(roster);
        Assert.False(roster is ICollection);
        Assert.False(roster is IList);
        var mutable = Assert.IsAssignableFrom<IList<T>>(roster);
        Assert.True(mutable.IsReadOnly);
        var originals = roster.ToArray();
        Assert.Throws<NotSupportedException>(() => { mutable[0] = originals[0]; });
        Assert.Throws<NotSupportedException>(() => mutable.Clear());
        Assert.Throws<NotSupportedException>(() => mutable.Add(originals[0]));
        Assert.Throws<NotSupportedException>(() => mutable.Insert(0, originals[0]));
        Assert.Throws<NotSupportedException>(() => mutable.RemoveAt(0));
        Assert.Throws<NotSupportedException>(() => mutable.Remove(originals[0]));
        var copy = new T[roster.Count + 1];
        mutable.CopyTo(copy, 1);
        Assert.Equal(originals, copy.Skip(1));
        copy[1] = default!;
        Assert.Equal(originals, roster.ToArray());
    }

    private sealed class ViewFixture(CompilerInputManifest input, CompilerHostRequest request,
        CompilerHostResponse response, SyntheticWorkbenchViewModel view) : IDisposable
    {
        public CompilerInputManifest Input { get; } = input;
        public CompilerHostRequest Request { get; } = request;
        public CompilerHostResponse Response { get; } = response;
        public SyntheticWorkbenchViewModel View { get; } = view;
        public void Dispose() => View.Dispose();
    }

    private static async Task<ViewFixture> OpenView()
    {
        var fixture = Make();
        var request = new CompilerHostRequest(new CompilerInputTransfer(fixture.Input), 1);
        var response = await Compile(request);
        var view = new SyntheticWorkbenchViewModel();
        view.BeginSynthetic(request);
        Assert.True(view.TryAccept(response));
        Assert.True(view.Select(Target(fixture)));
        return new(fixture.Input, request, response, view);
    }

    private static async Task<CompilerHostResponse> Compile(CompilerHostRequest request)
    {
        var input = request.Input.Materialize();
        CompilerArtifactIdentity Contract(string field) => new(Property(input.Settings, field));
        var profile = new CompilerHostProfile(input.Compiler, Contract("schema"), Contract("ruleset"), Contract("semantics"),
            Contract("ir"), Contract("evaluator"), input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString()));
        var response = await new LocalNetCompilerHost(profile).CompileAsync(request);
        Assert.Equal(CompilerHostStatus.Completed, response.Status);
        Assert.NotNull(response.Execution);
        Assert.NotNull(response.Compilation);
        Assert.Equal(CompilerExecutionState.Completed, response.Compilation!.Result.Execution);
        Assert.NotNull(response.Compilation.Validation?.Ir);
        return response;
    }

    private sealed class Notification : IDisposable
    {
        private readonly SyntheticWorkbenchViewModel _view;
        private readonly Func<bool> _predicate;
        private readonly Action _action;
        public bool Fired { get; private set; }
        public Notification(SyntheticWorkbenchViewModel view, Func<bool> predicate, Action action)
        {
            _view = view; _predicate = predicate; _action = action;
            _view.PropertyChanged += Changed;
        }
        private void Changed(object? sender, PropertyChangedEventArgs args)
        {
            if (Fired || !_predicate()) return;
            Assert.Same(_view, sender);
            Fired = true;
            _action();
        }
        public void Dispose() => _view.PropertyChanged -= Changed;
    }
}
