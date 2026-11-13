using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class DraftRevertTests
{
    private static ProjectSource Revision(int revision, string first = "Original", string second = "Other", bool opaque = false)
    {
        var basis = Source(revision);
        object Task(int id, string name) => new { entityId = Id(id), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1, displayName = name,
            payload = new { duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = true }, state = "coglatas.known", value = "10" }, ports = Array.Empty<object>() } };
        var records = new JsonArray(Obj(Task(7, first)), Obj(Task(8, second)));
        if (opaque) records.Add(Obj(new { entityId = Id(9), documentId = Id(4), kind = "example.future", schemaVersion = 1, payload = new { deleted = "retain exactly", ordered = new[] { 2, 1 } } }));
        return ProjectSource.Create(basis.Context, basis.Documents, Json(records));
    }
    private static ProjectHistoryRevision[] History(ProjectSource before, ProjectSource after, ProjectSource current) =>
        [ProjectHistoryRevision.Checkpoint(before, 0), ProjectHistoryRevision.Checkpoint(after, 1, [before.Context.CommittedRevision!]),
            ProjectHistoryRevision.Checkpoint(current, 2, [after.Context.CommittedRevision!])];
    private static DraftRevertPreview Preview(ProjectSource before, ProjectSource after, ProjectSource current, IEnumerable<ProjectHistoryRevision?>? history = null)
    {
        var a = Input(source: before); var b = Input(source: after); var c = Input(source: current);
        return ProjectDraftRevert.Preview(ProjectSemanticBinder.Bind(a), ProjectSemanticBinder.Bind(b), ProjectSemanticBinder.Bind(c),
            a.InputDigest, b.InputDigest, c.InputDigest, DraftRenameTests.Proposal(c, 80), history ?? History(before, after, current));
    }
    private static string Name(ProjectSource source, int entity) => source.Declarations.Value.EnumerateArray().Single(item => item.GetProperty("entityId").GetString() == Id(entity)).GetProperty("displayName").GetString()!;

    [Fact]
    public void NewInverseDraftPreservesLaterIndependentChangesAndAppliesOnlyToExactCurrentInput()
    {
        var before = Revision(60); var after = Revision(61, "Selected change"); var current = Revision(62, "Selected change", "Later independent change");
        var result = Preview(before, after, current); Assert.Equal(DraftRevertState.Ready, result.State); Assert.Empty(result.Conflicts);
        var plan = Assert.IsType<ProjectDraftRevertPlan>(result.Plan);
        Assert.Equal("Original", Name(plan.After.Source, 7)); Assert.Equal("Later independent change", Name(plan.After.Source, 8));
        Assert.Equal(current.Context.CommittedRevision, plan.After.Source.Context.Proposal!.BaseRevision);
        Assert.Equal(current.Context.CommittedRevision, plan.After.Source.Context.Proposal.CapturedTargetHead);
        Assert.Equal(after.Context.CommittedRevision, plan.RevertedRevision); Assert.Equal(before.Context.CommittedRevision, plan.RevertedParent);
        Assert.Equal(Input(source: current).Settings, plan.After.Settings); Assert.Same(plan.After, plan.ApplyToDraft(Input(source: current)));
        Assert.Null(plan.ApplyToDraft(Input(source: after))); Assert.Null(plan.ApplyToDraft(plan.After));
        Assert.Equal("Selected change", Name(current, 7)); Assert.Equal("Original", Name(before, 7));
    }
    [Fact]
    public void OverlappingLaterRecordEditConflictsWithoutReturningAnyPartiallyApplicableDraft()
    {
        var result = Preview(Revision(60), Revision(61, "Selected change"), Revision(62, "Further edit", "Later independent change"));
        Assert.Equal(DraftRevertState.Conflict, result.State); Assert.Null(result.Plan);
        var conflict = Assert.Single(result.Conflicts); Assert.Equal("declarations", conflict.RecordType); Assert.Equal(Id(7), conflict.Identity);
    }
    [Fact]
    public void RestoresDeletedOpaqueRecordWithoutReinterpretingItsPayload()
    {
        var before = Revision(60, opaque: true); var after = Revision(61); var current = Revision(62, second: "Later");
        var result = Preview(before, after, current); Assert.Equal(DraftRevertState.Ready, result.State);
        var restored = result.Plan!.After.Source.Declarations.Value.EnumerateArray().Single(item => item.GetProperty("entityId").GetString() == Id(9));
        Assert.Equal(before.Declarations.Value.EnumerateArray().Single(item => item.GetProperty("entityId").GetString() == Id(9)).GetRawText(), restored.GetRawText());
        Assert.Equal("Later", Name(result.Plan.After.Source, 8));
    }
    [Fact]
    public void AlreadyInvertedCurrentRecordsAreAnExplicitNoChange()
    {
        var result = Preview(Revision(60), Revision(61, "Selected change"), Revision(62, second: "Later"));
        Assert.Equal(DraftRevertState.NoChange, result.State); Assert.Null(result.Plan); Assert.Empty(result.Conflicts);
    }
    [Theory]
    [InlineData("ancestry")]
    [InlineData("body")]
    [InlineData("merge-parent")]
    public void MissingDescentMismatchedBodiesAndMultiParentSelectionsCannotPrepareDrafts(string mode)
    {
        var before = Revision(60); var after = Revision(61, "Selected change"); var current = Revision(62, "Selected change", "Later");
        var history = History(before, after, current);
        if (mode == "ancestry") history[2] = ProjectHistoryRevision.Checkpoint(current, 2);
        if (mode == "body") history[1] = ProjectHistoryRevision.Checkpoint(Revision(61, "Different body"), 1, [before.Context.CommittedRevision!]);
        if (mode == "merge-parent") history[1] = ProjectHistoryRevision.Checkpoint(after, 1, [before.Context.CommittedRevision!, new(after.Context.Branch, RevisionId.Parse(Id(59)))]);
        var result = Preview(before, after, current, history);
        Assert.Equal(mode == "merge-parent" ? DraftRevertState.Unsupported : DraftRevertState.Unavailable, result.State); Assert.Null(result.Plan); Assert.Empty(result.Conflicts);
    }
    [Theory]
    [InlineData("before")]
    [InlineData("after")]
    [InlineData("current")]
    public void EveryExpectedInputDigestIsRequiredBeforeHistoryIsEnumerated(string mode)
    {
        var a = Input(source: Revision(60)); var b = Input(source: Revision(61, "Changed")); var c = Input(source: Revision(62, "Changed", "Later"));
        var invoked = false;
        IEnumerable<ProjectHistoryRevision> Supply() { invoked = true; yield return ProjectHistoryRevision.Checkpoint(a.Source, 0); }
        var result = ProjectDraftRevert.Preview(ProjectSemanticBinder.Bind(a), ProjectSemanticBinder.Bind(b), ProjectSemanticBinder.Bind(c),
            mode == "before" ? b.InputDigest : a.InputDigest, mode == "after" ? a.InputDigest : b.InputDigest, mode == "current" ? a.InputDigest : c.InputDigest,
            DraftRenameTests.Proposal(c, 80), Supply());
        Assert.Equal(DraftRevertState.StaleInput, result.State); Assert.Null(result.Plan); Assert.False(invoked);
    }
    [Fact]
    public void RevertingTheCurrentHeadAlsoCreatesANewDraftWithoutInventingAnotherCommittedRevision()
    {
        var before = Revision(60); var after = Revision(61, "Changed");
        var result = Preview(before, after, after, [ProjectHistoryRevision.Checkpoint(before, 0), ProjectHistoryRevision.Checkpoint(after, 1, [before.Context.CommittedRevision!])]);
        Assert.Equal(DraftRevertState.Ready, result.State); Assert.Equal("Original", Name(result.Plan!.After.Source, 7));
        Assert.Equal(after.Context.CommittedRevision, result.Plan.After.Source.Context.Proposal!.CapturedTargetHead);
        Assert.Null(result.Plan.After.Source.Context.CommittedRevision);
    }
    [Fact]
    public void DocumentInverseRebuildsManifestDigestsAndKeepsLaterDeclarationChanges()
    {
        ProjectSource WithRoot(ProjectSource source, string name) => ProjectSource.Create(source.Context,
            [SourceDocument.Create(DocumentId.Parse(Id(4)), "project", Json(Obj(new { entityId = Id(6), kind = "coglatas.project", schemaVersion = 1, displayName = name, payload = new { } })))], source.Declarations, source.Relations);
        var before = WithRoot(Revision(60), "Old root"); var after = WithRoot(Revision(61), "Changed root"); var current = WithRoot(Revision(62, second: "Later"), "Changed root");
        var result = Preview(before, after, current); Assert.Equal(DraftRevertState.Ready, result.State);
        var candidate = result.Plan!.After.Source; Assert.Equal(before.Documents[0].Data, candidate.Documents[0].Data);
        Assert.Equal("Later", Name(candidate, 8)); Assert.Equal(candidate.Data, ProjectSourceCodec.Decode(candidate.Data.ToCanonicalBytes()).RequireSource().Data);
    }
    [Fact]
    public void BoundsAndDisposesAnUnendingHistorySupplyAndHonorsCancellationDuringEnumeration()
    {
        var before = Revision(60); var after = Revision(61, "Changed"); var current = Revision(62, "Changed", "Later");
        var disposed = false; var yielded = 0;
        IEnumerable<ProjectHistoryRevision> Endless()
        {
            try { while (true) { yielded++; yield return ProjectHistoryRevision.Checkpoint(before, 0); } }
            finally { disposed = true; }
        }
        Assert.Equal(DraftRevertState.ResourceLimit, Preview(before, after, current, Endless()).State);
        Assert.True(disposed); Assert.Equal(ProjectSourceHistory.MaximumRecords + 1, yielded);
        using var cancellation = new CancellationTokenSource(); Action cancel = cancellation.Cancel; var token = cancellation.Token; disposed = false;
        IEnumerable<ProjectHistoryRevision> Cancelling()
        {
            try { cancel(); yield return ProjectHistoryRevision.Checkpoint(before, 0); }
            finally { disposed = true; }
        }
        var a = Input(source: before); var b = Input(source: after); var c = Input(source: current);
        Assert.Throws<OperationCanceledException>(() => ProjectDraftRevert.Preview(ProjectSemanticBinder.Bind(a), ProjectSemanticBinder.Bind(b), ProjectSemanticBinder.Bind(c),
            a.InputDigest, b.InputDigest, c.InputDigest, DraftRenameTests.Proposal(c, 80), Cancelling(), token));
        Assert.True(disposed);
    }
    [Fact]
    public void LocalReferencesAdvanceWhilePinnedTargetsAndOpaqueContextsRemainExact()
    {
        var external = Source(9); var settings = Settings();
        settings["authorizedExternalSnapshots"] = new JsonArray(Obj(new { snapshotId = Id(90), version = "1", context = external.Context.Data.Value,
            digest = external.Digest.Data.Value, coverage = "coglatas.complete", asOf = (string?)null }));
        CompilerInputManifest Version(int revision, string name, string later)
        {
            var source = Revision(revision, name, later); var records = JsonNode.Parse(source.Declarations.CanonicalText)!.AsArray();
            var type = Obj(new { kind = "coglatas.entity-reference", schemaVersion = 1, nullable = false, acceptsUnknown = false, expectedKind = "coglatas.project" });
            records[0]!["payload"]!["ports"] = new JsonArray(Obj(new { portId = Id(70), direction = "coglatas.input", type,
                minimumConnections = 0, maximumConnections = 1, defaultValue = new { type, state = "coglatas.known", value = new
                { tenantId = Id(1), projectId = Id(2), entityId = Id(6), context = external.Context.Data.Value,
                    origin = ProjectLocation.Create(DocumentId.Parse(Id(4)), source.Context, entityId: EntityId.Parse(Id(7))).Data.Value,
                    manifest = new { version = "1", digest = external.Digest.Data.Value } } } }));
            records.Add(Obj(new { entityId = Id(10), documentId = Id(4), kind = "coglatas.requirement", schemaVersion = 1,
                payload = new { target = new { tenantId = Id(1), projectId = Id(2), entityId = Id(7), context = source.Context.Data.Value },
                    condition = PlanDeclarationTests.Literal(true), evidenceKind = "coglatas.model-proof" } }));
            records[1]!["futureOpaque"] = Obj(new { context = Source(60).Context.Data.Value, doNotRewrite = true });
            return Input(settings, ProjectSource.Create(source.Context, source.Documents, Json(records)));
        }
        var a = Version(60, "Original", "Other"); var b = Version(61, "Changed", "Other"); var c = Version(62, "Changed", "Later");
        var snapshot = new CompilerMaterializedSnapshot(ExternalSnapshotId.Parse(Id(90)), "1", new(external, true, true, true));
        var result = ProjectDraftRevert.Preview(ProjectSemanticBinder.Bind(a, [snapshot]), ProjectSemanticBinder.Bind(b, [snapshot]), ProjectSemanticBinder.Bind(c, [snapshot]),
            a.InputDigest, b.InputDigest, c.InputDigest, DraftRenameTests.Proposal(c, 80), History(a.Source, b.Source, c.Source));
        Assert.Equal(DraftRevertState.Ready, result.State); var candidate = result.Plan!.After; var next = candidate.Source.Context;
        var rows = candidate.Source.Declarations.Value.EnumerateArray().ToDictionary(item => item.GetProperty("entityId").GetString()!);
        var value = rows[Id(7)].GetProperty("payload").GetProperty("ports")[0].GetProperty("defaultValue").GetProperty("value");
        Assert.Equal(external.Context.Data, SourceJson.Parse(value.GetProperty("context").GetRawText()));
        Assert.Equal(external.Digest.Data, SourceJson.Parse(value.GetProperty("manifest").GetProperty("digest").GetRawText()));
        Assert.Equal(next.Data, SourceJson.Parse(value.GetProperty("origin").GetProperty("context").GetRawText()));
        Assert.Equal(next.Data, SourceJson.Parse(rows[Id(10)].GetProperty("payload").GetProperty("target").GetProperty("context").GetRawText()));
        Assert.Equal(Source(60).Context.Data, SourceJson.Parse(rows[Id(8)].GetProperty("futureOpaque").GetProperty("context").GetRawText()));
        Assert.Equal("Later", Name(candidate.Source, 8));
        // Opaque declaration fields already make the original semantic coverage incomplete.
        Assert.False(ProjectSemanticBinder.Bind(c, [snapshot]).BindingComplete);
        var rebound = ProjectSemanticBinder.Bind(candidate, [snapshot]); Assert.False(rebound.BindingComplete);
        Assert.All(rebound.References, item => Assert.Equal(ReferenceResolutionState.Resolved, item.State));
    }
    [Fact]
    public void RejectsReservedCandidateContextBeforeReadingHistoricalBodies()
    {
        var before = Revision(60); var after = Revision(61, "Changed"); var current = Revision(62, "Changed", "Later");
        var proposal = DraftRenameTests.Proposal(Input(source: current), 80); var reserved = SourceRevisionContext.Candidate(proposal);
        var settings = Settings(); settings["evidence"] = new JsonArray(Obj(new { snapshotId = Id(91), version = "1", context = reserved.Data.Value,
            digest = current.Digest.Data.Value, coverage = "coglatas.partial", asOf = (string?)null }));
        var a = Input(settings, before); var b = Input(settings, after); var c = Input(settings, current); var invoked = false;
        IEnumerable<ProjectHistoryRevision> Supply() { invoked = true; yield return ProjectHistoryRevision.Checkpoint(before, 0); }
        var result = ProjectDraftRevert.Preview(ProjectSemanticBinder.Bind(a), ProjectSemanticBinder.Bind(b), ProjectSemanticBinder.Bind(c),
            a.InputDigest, b.InputDigest, c.InputDigest, proposal, Supply());
        Assert.Equal(DraftRevertState.ContextConflict, result.State); Assert.Null(result.Plan); Assert.False(invoked);
    }
    [Fact]
    public async Task WindowsPreparationCreatesOnlyAnExactFreshLocalDraftAndClearsRetainedPlans()
    {
        var a = Input(source: Revision(60)); var b = Input(source: Revision(61, "Changed")); var c = Input(source: Revision(62, "Changed", "Later"));
        CompilerArtifactIdentity Artifact(string key) => new(SourceJson.Parse(c.Settings.Value.GetProperty(key).GetRawText()));
        var host = new LocalNetCompilerHost(new(c.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            c.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString())));
        using var view = new SyntheticWorkbenchViewModel(); var request = new CompilerHostRequest(new(c), 1); view.BeginSynthetic(request);
        Assert.True(view.TryAccept(await host.CompileAsync(request)));
        var history = History(a.Source, b.Source, c.Source);
        var preview = view.PreviewRevert(ProjectSemanticBinder.Bind(a), ProjectSemanticBinder.Bind(b), a.InputDigest, b.InputDigest, DraftRenameTests.Proposal(c, 80), history)!;
        Assert.Equal(DraftRevertState.Ready, preview.State); var plan = preview.Plan!;
        var copied = Preview(a.Source, b.Source, c.Source).Plan!; Assert.Null(view.ApplyPreviewedRevert(copied, 2));
        Assert.Null(view.ApplyPreviewedRevert(plan, 1));
        var next = Assert.IsType<CompilerHostRequest>(view.ApplyPreviewedRevert(plan, 2)); Assert.Null(view.RevertPreview); Assert.Null(view.Result);
        Assert.True(view.TryAccept(await host.CompileAsync(next))); Assert.Equal(plan.After.Source.Data, view.Source!.Data);
        Assert.Null(view.ApplyPreviewedRevert(plan, 3)); Assert.Equal("Original", Name(view.Source, 7)); Assert.Equal("Later", Name(view.Source, 8));
        view.Reset(); Assert.Null(view.RevertPreview); Assert.Null(view.Source);
    }
    private static SourceDocument Document(int id) => SourceDocument.Create(DocumentId.Parse(Id(id)), "Document " + id,
        Json(Obj(new { entityId = Id(id + 1), kind = "coglatas.project", schemaVersion = 1, payload = new { } })));
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReversesDocumentAdditionOrDeletionWhileKeepingALaterIndependentDocument(bool added)
    {
        ProjectSource WithDocuments(int revision, bool selected, bool later) => ProjectSource.Create(Source(revision).Context,
            new[] { Source(revision).Documents[0] }.Concat(selected ? [Document(20)] : []).Concat(later ? [Document(30)] : []));
        var before = WithDocuments(60, !added, false); var after = WithDocuments(61, added, false); var current = WithDocuments(62, added, true);
        var result = Preview(before, after, current); Assert.Equal(DraftRevertState.Ready, result.State);
        var candidate = result.Plan!.After.Source;
        Assert.Equal(!added, candidate.Documents.Any(item => item.DocumentId == DocumentId.Parse(Id(20))));
        Assert.Equal(Document(30).Data, candidate.Documents.Single(item => item.DocumentId == DocumentId.Parse(Id(30))).Data);
        Assert.Equal(candidate.Data, ProjectSourceCodec.Decode(candidate.Data.ToCanonicalBytes()).RequireSource().Data);
    }
    [Theory]
    [InlineData("source", true)]
    [InlineData("source", false)]
    [InlineData("manifest", true)]
    [InlineData("manifest", false)]
    public void InvertsOpaqueMetadataWithoutConflatingAbsentAndNullOrLosingLaterFields(string container, bool originallyPresent)
    {
        ProjectSource Version(int revision, bool present, bool later)
        {
            var root = JsonNode.Parse(Revision(revision).Data.CanonicalText)!.AsObject(); var target = container == "source" ? root : root["manifest"]!.AsObject();
            if (present) target["futureMetadata"] = null;
            if (later) target["laterMetadata"] = new JsonArray(2, 1);
            return ProjectSourceCodec.Decode(Json(root).ToCanonicalBytes()).RequireSource();
        }
        var result = Preview(Version(60, originallyPresent, false), Version(61, !originallyPresent, false), Version(62, !originallyPresent, true));
        Assert.Equal(DraftRevertState.Ready, result.State); var data = result.Plan!.After.Source.Data.Value;
        var target = container == "source" ? data : data.GetProperty("manifest");
        Assert.Equal(originallyPresent, target.TryGetProperty("futureMetadata", out var value));
        if (originallyPresent) Assert.Equal("null", value.GetRawText());
        Assert.Equal("[2,1]", target.GetProperty("laterMetadata").GetRawText());
    }
    [Fact]
    public void UsesCurrentSettingsEvenWhenHistoricalCompilerInputsHadDifferentSettings()
    {
        var a = Input(source: Revision(60)); var b = Input(source: Revision(61, "Changed")); var settings = Settings();
        settings["executionProfile"]!["maximumOperations"] = 400000;
        var c = Input(settings, Revision(62, "Changed", "Later"));
        var result = ProjectDraftRevert.Preview(ProjectSemanticBinder.Bind(a), ProjectSemanticBinder.Bind(b), ProjectSemanticBinder.Bind(c),
            a.InputDigest, b.InputDigest, c.InputDigest, DraftRenameTests.Proposal(c, 80), History(a.Source, b.Source, c.Source));
        Assert.Equal(DraftRevertState.Ready, result.State); Assert.Equal(c.Settings, result.Plan!.After.Settings); Assert.NotEqual(a.Settings, c.Settings);
    }
    [Fact]
    public void RestorationCannotCollideWithAnIdentityUsedByALaterDocumentRoot()
    {
        var before = Revision(60, opaque: true); var after = Revision(61);
        var current = ProjectSource.Create(Revision(62).Context, [Source(62).Documents[0], Document(8)], after.Declarations);
        var result = Preview(before, after, current); Assert.Equal(DraftRevertState.InvalidSource, result.State); Assert.Null(result.Plan); Assert.Empty(result.Conflicts);
    }
    [Theory]
    [InlineData("cancel")]
    [InlineData("exception")]
    [InlineData("reset-clear")]
    [InlineData("reset-supply")]
    [InlineData("dispose-publish")]
    public async Task WindowsClearNotifiesAndReentrantBoundariesCannotPublishAnObsoletePlan(string mode)
    {
        var a = Input(source: Revision(60)); var b = Input(source: Revision(61, "Changed")); var c = Input(source: Revision(62, "Changed", "Later"));
        CompilerArtifactIdentity Artifact(string key) => new(SourceJson.Parse(c.Settings.Value.GetProperty(key).GetRawText()));
        var host = new LocalNetCompilerHost(new(c.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            c.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString())));
        using var view = new SyntheticWorkbenchViewModel(); var request = new CompilerHostRequest(new(c), 1); view.BeginSynthetic(request);
        Assert.True(view.TryAccept(await host.CompileAsync(request)));
        var history = History(a.Source, b.Source, c.Source); var parent = ProjectSemanticBinder.Bind(a); var reverted = ProjectSemanticBinder.Bind(b); var proposal = DraftRenameTests.Proposal(c, 80);
        Assert.NotNull(view.PreviewRevert(parent, reverted, a.InputDigest, b.InputDigest, proposal, history));
        var observedClear = false;
        view.PropertyChanged += (sender, _) =>
        {
            if (sender is not SyntheticWorkbenchViewModel current) return;
            if (current.RevertPreview is null && !observedClear) { observedClear = true; if (mode == "reset-clear") current.Reset(); }
            else if (mode == "dispose-publish" && current.RevertPreview?.Plan is not null) current.Dispose();
        };
        Action reset = view.Reset; var invoked = false;
        IEnumerable<ProjectHistoryRevision> Supply()
        {
            invoked = true; if (mode == "exception") throw new IOException("Synthetic history failure");
            if (mode == "reset-supply") reset();
            foreach (var item in history) yield return item;
        }
        if (mode == "cancel") Assert.Throws<OperationCanceledException>(() => view.PreviewRevert(parent, reverted, a.InputDigest, b.InputDigest, proposal, Supply(), new(true)));
        else if (mode == "exception") Assert.Throws<IOException>(() => view.PreviewRevert(parent, reverted, a.InputDigest, b.InputDigest, proposal, Supply()));
        else Assert.Null(view.PreviewRevert(parent, reverted, a.InputDigest, b.InputDigest, proposal, Supply()));
        Assert.True(observedClear); Assert.Null(view.RevertPreview);
        if (mode is "reset-clear" or "cancel") Assert.False(invoked);
        if (mode is "reset-clear" or "reset-supply" or "dispose-publish") { Assert.Equal("Empty", view.State); Assert.Null(view.Source); }
    }
    [Fact]
    public void ForeignModelsAreRejectedBeforeSupplyAndConflictStorageCannotBeMutated()
    {
        var a = Input(source: Revision(60)); var b = Input(source: Revision(61, "Changed")); var foreign = Revision(62, "Changed");
        var context = SourceRevisionContext.Committed(new(new(TenantId.Parse(Id(1)), ProjectId.Parse(Id(99)), BranchId.Parse(Id(3))), RevisionId.Parse(Id(62))));
        var c = Input(source: ProjectSource.Create(context, foreign.Documents, foreign.Declarations)); var invoked = false;
        IEnumerable<ProjectHistoryRevision> Supply() { invoked = true; yield return ProjectHistoryRevision.Checkpoint(a.Source, 0); }
        var result = ProjectDraftRevert.Preview(ProjectSemanticBinder.Bind(a), ProjectSemanticBinder.Bind(b), ProjectSemanticBinder.Bind(c),
            a.InputDigest, b.InputDigest, c.InputDigest, DraftRenameTests.Proposal(c, 80), Supply());
        Assert.Equal(DraftRevertState.OutOfScope, result.State); Assert.Null(result.Plan); Assert.False(invoked);
        var conflict = Preview(a.Source, b.Source, Revision(62, "Further change")); Assert.Equal(DraftRevertState.Conflict, conflict.State);
        Assert.Throws<NotSupportedException>(() => ((IList<DraftRevertConflict>)conflict.Conflicts).Clear());
        Assert.False(conflict.Conflicts is System.Collections.ICollection);
    }
    [Fact]
    public void ExportedInverseFixtureIsTheExactRecomputedCorePlanAndCandidate()
    {
        var cases = WebPreviewContractTests.Cases().Select(row => SourceJson.Parse((string)row[0]).Value).ToDictionary(item => item.GetProperty("key").GetString()!);
        CompilerInputManifest Materialize(string key)
        {
            var item = cases[key]; var source = ProjectSourceCodec.Decode(System.Text.Encoding.UTF8.GetBytes(item.GetProperty("sourceText").GetString()!)).RequireSource();
            return CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(item.GetProperty("manifest").GetRawText()));
        }
        var parent = Materialize("document-before"); var current = Materialize("document-after"); var candidate = Materialize("revert-draft");
        var history = new[] { ProjectHistoryRevision.Checkpoint(parent.Source, 0), ProjectHistoryRevision.ForwardDelta(ProjectSourceDelta.Create(parent.Source, current.Source), 1) };
        var result = ProjectDraftRevert.Preview(ProjectSemanticBinder.Bind(parent), ProjectSemanticBinder.Bind(current), ProjectSemanticBinder.Bind(current),
            parent.InputDigest, current.InputDigest, current.InputDigest, candidate.Source.Context.Proposal!, history);
        Assert.Equal(DraftRevertState.Ready, result.State); var plan = result.Plan!;
        Assert.Equal(plan.After.Data, candidate.Data); Assert.Equal(plan.After.Source.Data, candidate.Source.Data);
        Assert.Equal(plan.Data, SourceJson.Parse(cases["document-after"].GetProperty("preparedRevert").GetProperty("plan").GetRawText()));
        Assert.Same(plan.After, plan.ApplyToDraft(current)); Assert.Null(plan.ApplyToDraft(candidate));
    }
    [Fact]
    public void OversizedCombinedCandidateReturnsResourceLimitRatherThanUnsupported()
    {
        ProjectSource Large(int revision, int entity)
        {
            var basis = Source(revision);
            return ProjectSource.Create(basis.Context, basis.Documents, Json(new JsonArray(Obj(new { entityId = Id(entity), documentId = Id(4),
                kind = "example.future", schemaVersion = 1, payload = new { retained = new string('x', 2200000) } }))));
        }
        var before = Large(60, 7); var after = Source(61); var current = Large(62, 8);
        var settings = Settings(); settings["executionProfile"]!["maximumOperations"] = 10000000;
        var a = Input(settings, before); var b = Input(settings, after); var c = Input(settings, current);
        var am = ProjectSemanticBinder.Bind(a); var bm = ProjectSemanticBinder.Bind(b); var cm = ProjectSemanticBinder.Bind(c);
        Assert.Equal(2, am.Declarations.Count); Assert.Equal(2, cm.Declarations.Count);
        var result = ProjectDraftRevert.Preview(am, bm, cm, a.InputDigest, b.InputDigest, c.InputDigest,
            DraftRenameTests.Proposal(c, 80), History(before, after, current));
        Assert.Equal(DraftRevertState.ResourceLimit, result.State); Assert.Null(result.Plan); Assert.Empty(result.Conflicts);
    }
    private static ProjectSource WithDigestMetadata(ProjectSource source, string documentMetadata, string manifestMetadata)
    {
        var root = JsonNode.Parse(source.Data.CanonicalText)!.AsObject();
        root["documents"]![0]!["digest"]!["futureDocumentDigest"] = documentMetadata;
        root["manifest"]!["documents"]![0]!["digest"]!["futureManifestDigest"] = manifestMetadata;
        return ProjectSourceCodec.Decode(Json(root).ToCanonicalBytes()).RequireSource();
    }
    [Fact]
    public void InvertsOpaqueDigestMetadataWithoutDroppingOrMixingIndependentDigestEnvelopes()
    {
        var before = WithDigestMetadata(Revision(60), "document-before", "manifest-before");
        var after = WithDigestMetadata(Revision(61), "document-after", "manifest-after");
        var current = WithDigestMetadata(Revision(62, second: "Later"), "document-after", "manifest-after");
        var result = Preview(before, after, current); Assert.Equal(DraftRevertState.Ready, result.State);
        var candidate = result.Plan!.After.Source; var documentDigest = candidate.Documents[0].Digest.Data.Value;
        var manifestDigest = candidate.Manifest.Data.Value.GetProperty("documents")[0].GetProperty("digest");
        Assert.Equal("document-before", documentDigest.GetProperty("futureDocumentDigest").GetString());
        Assert.Equal("manifest-before", manifestDigest.GetProperty("futureManifestDigest").GetString());
        Assert.False(documentDigest.TryGetProperty("futureManifestDigest", out _));
        Assert.False(manifestDigest.TryGetProperty("futureDocumentDigest", out _));
        Assert.Equal(documentDigest.GetProperty("value").GetString(), manifestDigest.GetProperty("value").GetString());
        Assert.Equal("Later", Name(candidate, 8));
        Assert.Equal(candidate.Data, ProjectSourceCodec.Decode(candidate.Data.ToCanonicalBytes()).RequireSource().Data);
    }
    [Fact]
    public void ForkAndRootRenamePreserveOpaqueDigestFieldsWhileRefreshingOnlyTheKnownHash()
    {
        var committed = Input(source: WithDigestMetadata(Revision(60), "document-retained", "manifest-retained"));
        var draft = ProjectDraftRename.Fork(ProjectSemanticBinder.Bind(committed), DraftRenameTests.Proposal(committed));
        var preview = ProjectDraftRename.Preview(ProjectSemanticBinder.Bind(draft), DraftRenameTests.Target(draft, 6), draft.InputDigest,
            "Changed root", DraftRenameTests.Next(draft));
        Assert.Equal(DraftRenameState.Ready, preview.State); var candidate = preview.Plan!.After.Source;
        Assert.Equal("document-retained", candidate.Documents[0].Digest.Data.Value.GetProperty("futureDocumentDigest").GetString());
        var manifestDigest = candidate.Manifest.Data.Value.GetProperty("documents")[0].GetProperty("digest");
        Assert.Equal("manifest-retained", manifestDigest.GetProperty("futureManifestDigest").GetString());
        Assert.False(manifestDigest.TryGetProperty("futureDocumentDigest", out _));
        Assert.NotEqual(committed.Source.Documents[0].Digest.Value, candidate.Documents[0].Digest.Value);
        Assert.Equal(candidate.Documents[0].Digest.Value, manifestDigest.GetProperty("value").GetString());
        Assert.False(ProjectSemanticBinder.Bind(preview.Plan.After).BindingComplete);
    }
    [Fact]
    public void LaterManifestDigestMetadataSurvivesAnInverseDocumentContentChange()
    {
        ProjectSource Version(int revision, string name, string metadata)
        {
            var original = Revision(revision, second: revision == 62 ? "Later" : "Other");
            var document = SourceDocument.Create(DocumentId.Parse(Id(4)), "project", Json(Obj(new
                { entityId = Id(6), kind = "coglatas.project", schemaVersion = 1, displayName = name, payload = new { } })));
            return WithDigestMetadata(ProjectSource.Create(original.Context, [document], original.Declarations), "document-stable", metadata);
        }
        var before = Version(60, "Original root", "manifest-stable"); var after = Version(61, "Changed root", "manifest-stable");
        var current = Version(62, "Changed root", "manifest-later"); var result = Preview(before, after, current);
        Assert.Equal(DraftRevertState.Ready, result.State); var source = result.Plan!.After.Source;
        Assert.Equal("Original root", source.Documents[0].Root.Data.Value.GetProperty("displayName").GetString());
        Assert.Equal("document-stable", source.Documents[0].Digest.Data.Value.GetProperty("futureDocumentDigest").GetString());
        var digest = source.Manifest.Data.Value.GetProperty("documents")[0].GetProperty("digest");
        Assert.Equal("manifest-later", digest.GetProperty("futureManifestDigest").GetString());
        Assert.Equal(source.Documents[0].Digest.Value, digest.GetProperty("value").GetString()); Assert.Equal("Later", Name(source, 8));
    }
    [Fact]
    public void ConflictingOpaqueManifestDigestEditsCannotDisappearAsNoChange()
    {
        var before = WithDigestMetadata(Revision(60), "document-stable", "manifest-old");
        var after = WithDigestMetadata(Revision(61), "document-stable", "manifest-new");
        var current = WithDigestMetadata(Revision(62), "document-stable", "manifest-further");
        var result = Preview(before, after, current); Assert.Equal(DraftRevertState.Conflict, result.State); Assert.Null(result.Plan);
        var conflict = Assert.Single(result.Conflicts); Assert.Equal("manifest-document", conflict.RecordType); Assert.Equal(Id(4), conflict.Identity);
        Assert.Equal("manifest-further", current.Manifest.Data.Value.GetProperty("documents")[0].GetProperty("digest").GetProperty("futureManifestDigest").GetString());
    }
    [Fact]
    public async Task ReentrantHistorySupplyCannotReplaceANewerPreviewWithoutChangingTheWorkbenchLifecycle()
    {
        var a = Input(source: Revision(60)); var b = Input(source: Revision(61, "Changed")); var c = Input(source: Revision(62, "Changed", "Later"));
        CompilerArtifactIdentity Artifact(string key) => new(SourceJson.Parse(c.Settings.Value.GetProperty(key).GetRawText()));
        var host = new LocalNetCompilerHost(new(c.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            c.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString())));
        using var view = new SyntheticWorkbenchViewModel(); var request = new CompilerHostRequest(new(c), 1); view.BeginSynthetic(request);
        Assert.True(view.TryAccept(await host.CompileAsync(request)));
        var parent = ProjectSemanticBinder.Bind(a); var reverted = ProjectSemanticBinder.Bind(b); var history = History(a.Source, b.Source, c.Source);
        Func<PartialSemanticModel, PartialSemanticModel, ContentDigest, ContentDigest, ProposalContext, IEnumerable<ProjectHistoryRevision?>, CancellationToken, DraftRevertPreview?> prepare = view.PreviewRevert;
        DraftRevertPreview? newer = null;
        IEnumerable<ProjectHistoryRevision> Supply()
        {
            newer = prepare(parent, reverted, a.InputDigest, b.InputDigest, DraftRenameTests.Proposal(c, 81), history, CancellationToken.None);
            foreach (var item in history) yield return item;
        }
        Assert.Null(view.PreviewRevert(parent, reverted, a.InputDigest, b.InputDigest, DraftRenameTests.Proposal(c, 80), Supply()));
        Assert.NotNull(newer); Assert.Same(newer, view.RevertPreview);
        Assert.Equal(CandidateRevisionId.Parse(Id(81)), view.RevertPreview!.Plan!.After.Source.Context.Proposal!.CandidateRevisionId);
        Assert.Equal(c.Source.Data, view.Source!.Data);
    }
}
