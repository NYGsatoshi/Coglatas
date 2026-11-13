using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class SourceHistoryTests
{
    internal static ProjectSource Revision(int revision, string? name = null, bool opaque = false)
    {
        var original = Source(revision); var root = JsonNode.Parse(original.Data.CanonicalText)!.AsObject();
        root["futureSource"] = Obj(new { retained = "Unknown source metadata", revision });
        root["manifest"]!["futureManifest"] = Obj(new { retained = true });
        root["declarations"] = new JsonArray(Obj(new { entityId = Id(7), documentId = Id(4), kind = opaque ? "example.future" : "coglatas.task", schemaVersion = 1,
            displayName = name ?? "Task", payload = opaque ? (object)new { unknown = "Deleted values must survive", ordered = new[] { 2, 1 } } : new
            { duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = true }, state = "coglatas.known", value = "10" }, ports = Array.Empty<object>() } }));
        return ProjectSourceCodec.Decode(Json(root).ToCanonicalBytes()).RequireSource();
    }
    internal static RevisionRef Ref(ProjectSource source) => source.Context.CommittedRevision!;

    [Fact]
    public void ReconstructsExactCanonicalSourceAndOriginalUnknownMetadataFromForwardRecords()
    {
        var first = Revision(50); var second = Revision(51, "Changed"); var third = Revision(52, "Unknown", true);
        var a = ProjectSourceDelta.Create(first, second); var b = ProjectSourceDelta.Create(second, third);
        var result = ProjectSourceHistory.Read(Ref(third), [ProjectHistoryRevision.ForwardDelta(b, 2), ProjectHistoryRevision.Checkpoint(first, 0), ProjectHistoryRevision.ForwardDelta(a, 1)]);
        Assert.Equal(HistoryReadState.Ready, result.State); Assert.Equal(third.Data, result.Source!.Data); Assert.Equal(third.Digest, result.Source.Digest);
        Assert.Equal(new[] { Ref(first), Ref(second), Ref(third) }, result.ReconstructionPath);
        Assert.Equal(first.Declarations, Json(new JsonArray(JsonNode.Parse(a.Declarations[0].Before!.CanonicalText))));
        Assert.Equal(SourceDecodeStatus.Unsupported, result.SourceAdmissionStatus);
        Assert.Equal("Unknown source metadata", result.Source.Data.Value.GetProperty("futureSource").GetProperty("retained").GetString());
        Assert.True(result.Source.Manifest.Data.Value.GetProperty("futureManifest").GetProperty("retained").GetBoolean());
        Assert.False(result.Source.Data.Equals(first.Data)); Assert.Equal("Task", first.Declarations.Value[0].GetProperty("displayName").GetString());
    }

    [Fact]
    public void DeltaRetainsDeletedRecordsAndReconstructsAfterReplacementCheckpointCompaction()
    {
        var first = Revision(50, opaque: true); var root = JsonNode.Parse(first.Data.CanonicalText)!.AsObject();
        root["context"]!["revisionId"] = Id(51); root["declarations"] = new JsonArray();
        var second = ProjectSourceCodec.Decode(Json(root).ToCanonicalBytes()).RequireSource(); var third = Revision(52);
        var removal = ProjectSourceDelta.Create(first, second); var addition = ProjectSourceDelta.Create(second, third);
        var deleted = Assert.Single(removal.Declarations); Assert.NotNull(deleted.Before); Assert.Null(deleted.After);
        Assert.Equal("Deleted values must survive", deleted.Before.Value.GetProperty("payload").GetProperty("unknown").GetString());
        var oldRecords = new[] { ProjectHistoryRevision.Checkpoint(first, 0), ProjectHistoryRevision.ForwardDelta(removal, 1), ProjectHistoryRevision.ForwardDelta(addition, 2) };
        var reconstructed = ProjectSourceHistory.Read(Ref(second), oldRecords); Assert.Equal(HistoryReadState.Ready, reconstructed.State);
        // Compact only in the caller-owned local collection. No storage or deletion operation is performed.
        var compacted = new[] { ProjectHistoryRevision.Checkpoint(reconstructed.Source!, 1, [Ref(first)]), ProjectHistoryRevision.ForwardDelta(addition, 2) };
        Assert.Equal(third.Data, ProjectSourceHistory.Read(Ref(third), compacted).Source!.Data);
        Assert.Equal(HistoryReadState.Unavailable, ProjectSourceHistory.Read(Ref(first), compacted).State);
        var missingAnchor = ProjectSourceHistory.Read(Ref(third), [compacted[1]]);
        Assert.Equal(HistoryReadState.Unavailable, missingAnchor.State); Assert.Null(missingAnchor.Source); Assert.Empty(missingAnchor.ReconstructionPath);
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("context")]
    [InlineData("delta-base")]
    [InlineData("schema")]
    [InlineData("malformed")]
    public void UntrustedCheckpointIntegrityScopeAndSchemaNeverBecomeReady(string mode)
    {
        var first = Revision(50); var second = Revision(51); var body = JsonNode.Parse(first.Data.CanonicalText)!.AsObject();
        var digest = first.Digest;
        if (mode == "digest") digest = second.Digest;
        if (mode == "context") body["context"]!["revisionId"] = Id(99);
        if (mode == "schema") body["schemaVersion"] = 2;
        if (mode == "malformed") body.Remove("manifest");
        if (mode == "delta-base") { body["futureSource"]!["retained"] = "Wrong base body"; var corruptBase = ProjectSourceCodec.Decode(Json(body).ToCanonicalBytes()).RequireSource(); digest = corruptBase.Digest; }
        var checkpoint = ProjectHistoryRevision.MaterializedCheckpoint(Ref(first), 0, digest, Json(body));
        var result = mode == "delta-base" ? ProjectSourceHistory.Read(Ref(second), [checkpoint, ProjectHistoryRevision.ForwardDelta(ProjectSourceDelta.Create(first, second), 1)]) :
            ProjectSourceHistory.Read(Ref(first), [checkpoint]);
        Assert.Equal(mode == "schema" ? HistoryReadState.Unsupported : HistoryReadState.Corrupt, result.State);
        Assert.Null(result.Source); Assert.Empty(result.ReconstructionPath);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("self-parent")]
    [InlineData("duplicate-parent")]
    [InlineData("reverse-order")]
    [InlineData("foreign-project")]
    [InlineData("foreign-parent")]
    public void InvalidLineageAndForeignMaterializationsAreRefusedWithoutADataProjection(string mode)
    {
        var first = Revision(50); var second = Revision(51); var checkpoint = ProjectHistoryRevision.Checkpoint(first, 0);
        var foreign = new RevisionRef(new(TenantId.Parse(Id(1)), ProjectId.Parse(Id(99)), BranchId.Parse(Id(3))), RevisionId.Parse(Id(50)));
        ProjectHistoryRevision[] records = mode switch
        {
            "duplicate" => [checkpoint, checkpoint],
            "self-parent" => [ProjectHistoryRevision.Checkpoint(first, 0, [Ref(first)])],
            "duplicate-parent" => [ProjectHistoryRevision.Checkpoint(second, 1, [Ref(first), Ref(first)]), checkpoint],
            "reverse-order" => [ProjectHistoryRevision.Checkpoint(first, 5), ProjectHistoryRevision.ForwardDelta(ProjectSourceDelta.Create(first, second), 1)],
            "foreign-project" => [ProjectHistoryRevision.Unavailable(foreign, 0, first.Digest)],
            "foreign-parent" => [ProjectHistoryRevision.Checkpoint(first, 0, [foreign])], _ => throw new ArgumentException("Unknown test mode")
        };
        var result = ProjectSourceHistory.Read(mode is "reverse-order" or "duplicate-parent" ? Ref(second) : Ref(first), records);
        Assert.Equal(mode.StartsWith("foreign", StringComparison.Ordinal) ? HistoryReadState.OutOfScope : HistoryReadState.Corrupt, result.State);
        Assert.Null(result.Source); Assert.Empty(result.ReconstructionPath);
    }

    [Fact]
    public void BranchSequenceIsNotAGlobalClockAndExplicitOtherBranchSnapshotsDoNotReplaceTheTarget()
    {
        var first = Revision(50); var root = JsonNode.Parse(first.Data.CanonicalText)!.AsObject(); root["context"]!["branch"]!["branchId"] = Id(80);
        var other = ProjectSourceCodec.Decode(Json(root).ToCanonicalBytes()).RequireSource();
        var result = ProjectSourceHistory.Read(Ref(other), [ProjectHistoryRevision.Checkpoint(first, 900), ProjectHistoryRevision.Checkpoint(other, 0, [Ref(first)])]);
        Assert.Equal(HistoryReadState.Ready, result.State); Assert.Equal(other.Data, result.Source!.Data); Assert.Equal(new[] { Ref(other) }, result.ReconstructionPath);
        Assert.Throws<ArgumentException>(() => ProjectSourceDelta.Create(first, other));
    }

    [Fact]
    public void BoundedEnumerationDisposesAndCancellationDoesNotPublishPartialSource()
    {
        var source = Revision(50); var record = ProjectHistoryRevision.Checkpoint(source, 0); var disposed = false; var yielded = 0;
        IEnumerable<ProjectHistoryRevision> Infinite()
        {
            try { while (true) { yielded++; yield return ProjectHistoryRevision.Unavailable(new(source.Context.Branch, RevisionId.New()), yielded, source.Digest); } }
            finally { disposed = true; }
        }
        var result = ProjectSourceHistory.Read(Ref(source), Infinite()); Assert.Equal(HistoryReadState.ResourceLimit, result.State);
        Assert.True(disposed); Assert.Equal(ProjectSourceHistory.MaximumRecords + 1, yielded); Assert.Null(result.Source);
        using var cancellation = new CancellationTokenSource(); var token = cancellation.Token; Action cancel = cancellation.Cancel;
        IEnumerable<ProjectHistoryRevision> CancelDuringMove()
        { try { cancel(); yield return record; } finally { disposed = true; } }
        disposed = false;
        Assert.Throws<OperationCanceledException>(() => ProjectSourceHistory.Read(Ref(source), CancelDuringMove(), token)); Assert.True(disposed);
    }

    [Fact]
    public void ReconstructionLengthHasAnExplicitFailureAndCanBeShortenedWithACheckpoint()
    {
        var sources = Enumerable.Range(50, ProjectSourceHistory.MaximumReconstructionDepth + 1).Select(n => Revision(n)).ToArray();
        var records = new List<ProjectHistoryRevision> { ProjectHistoryRevision.Checkpoint(sources[0], 0) };
        for (var i = 1; i < sources.Length; i++) records.Add(ProjectHistoryRevision.ForwardDelta(ProjectSourceDelta.Create(sources[i - 1], sources[i]), i));
        var result = ProjectSourceHistory.Read(Ref(sources[^1]), records); Assert.Equal(HistoryReadState.ResourceLimit, result.State); Assert.Empty(result.ReconstructionPath);
        records[1] = ProjectHistoryRevision.Checkpoint(sources[1], 1, [Ref(sources[0])]);
        Assert.Equal(sources[^1].Data, ProjectSourceHistory.Read(Ref(sources[^1]), records).Source!.Data);
    }

    [Fact]
    public void PublicHistoryCollectionsExposeNoMutableStorageAndKeepRejectingGenericMutation()
    {
        var a = Revision(50); var b = Revision(51); var delta = ProjectSourceDelta.Create(a, b); var record = ProjectHistoryRevision.ForwardDelta(delta, 1);
        var read = ProjectSourceHistory.Read(Ref(b), [record, ProjectHistoryRevision.Checkpoint(a, 0)]);
        foreach (var collection in new object[] { delta.Documents, delta.Declarations, delta.Relations, record.Parents, read.ReconstructionPath })
            Assert.False(collection is System.Collections.ICollection);
        Assert.Throws<NotSupportedException>(() => ((IList<RevisionRef>)record.Parents).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<SourceHistoryChange<EntityId>>)delta.Declarations).Clear());
        Assert.Throws<ArgumentException>(() => ProjectSourceDelta.Create(a, a));
    }

    [Fact]
    public void DocumentAndRelationChangesRoundTripTogetherWithTheirTypedIdentityAndManifest()
    {
        var first = Revision(50); var context = Source(51).Context;
        var document = SourceDocument.Create(DocumentId.Parse(Id(40)), "Another document", Json(Obj(new
        { entityId = Id(41), kind = "coglatas.project", schemaVersion = 1, payload = new { } })), Json(Obj(new { opaque = "Document extension" })));
        object Endpoint(int entity) => new { kind = "coglatas.entity", expectedKind = "coglatas.project", reference = new
        { tenantId = Id(1), projectId = Id(2), entityId = Id(entity), context = context.Data.Value } };
        var relations = Json(new JsonArray(Obj(new { relationId = Id(30), documentId = Id(4), kind = RelationKinds.RelatedTo, schemaVersion = 1,
            descriptor = new { descriptorId = RelationKinds.RelatedTo, version = 1 }, source = Endpoint(6), target = Endpoint(41), payload = new { } })));
        var second = ProjectSource.Create(context, [first.Documents[0], document], first.Declarations, relations);
        var delta = ProjectSourceDelta.Create(first, second);
        Assert.Equal(document.DocumentId, Assert.Single(delta.Documents).Identity); Assert.Equal(RelationId.Parse(Id(30)), Assert.Single(delta.Relations).Identity);
        var reconstructed = ProjectSourceHistory.Read(Ref(second), [ProjectHistoryRevision.Checkpoint(first, 0), ProjectHistoryRevision.ForwardDelta(delta, 1)]);
        Assert.Equal(HistoryReadState.Ready, reconstructed.State); Assert.Equal(second.Data, reconstructed.Source!.Data); Assert.Equal(second.Manifest.Data, reconstructed.Source.Manifest.Data);
        var back = ProjectSource.Create(Source(52).Context, [first.Documents[0]], first.Declarations);
        var deletion = ProjectSourceDelta.Create(second, back);
        Assert.NotNull(Assert.Single(deletion.Documents).Before); Assert.Null(Assert.Single(deletion.Documents).After);
        Assert.NotNull(Assert.Single(deletion.Relations).Before); Assert.Null(Assert.Single(deletion.Relations).After);
        Assert.Equal(back.Data, ProjectSourceHistory.Read(Ref(back), [ProjectHistoryRevision.Checkpoint(second, 1), ProjectHistoryRevision.ForwardDelta(deletion, 2)]).Source!.Data);
    }

    [Fact]
    public void KnownCrossBranchParentCyclesAreNotHiddenByAnAvailableCheckpoint()
    {
        var first = Revision(50); var body = JsonNode.Parse(first.Data.CanonicalText)!.AsObject(); body["context"]!["branch"]!["branchId"] = Id(80);
        var other = ProjectSourceCodec.Decode(Json(body).ToCanonicalBytes()).RequireSource();
        var result = ProjectSourceHistory.Read(Ref(first), [ProjectHistoryRevision.Checkpoint(first, 0, [Ref(other)]), ProjectHistoryRevision.Checkpoint(other, 0, [Ref(first)])]);
        Assert.Equal(HistoryReadState.Corrupt, result.State); Assert.Null(result.Source);
    }

    [Fact]
    public void DeepAndDenseSuppliedJsonHasExplicitProcessingBounds()
    {
        var source = Revision(50); var body = JsonNode.Parse(source.Data.CanonicalText)!.AsObject();
        JsonNode deep = JsonValue.Create("Opaque"); for (var i = 0; i < 80; i++) deep = new JsonObject { ["nested"] = deep };
        body["futureSource"] = deep;
        var custom = SourceJson.Parse(body.ToJsonString(new() { MaxDepth = 256 }), new(MaximumDepth: 256));
        var result = ProjectSourceHistory.Read(Ref(source), [ProjectHistoryRevision.MaterializedCheckpoint(Ref(source), 0, source.Digest, custom)]);
        Assert.Equal(HistoryReadState.ResourceLimit, result.State); Assert.Null(result.Source);
        body["futureSource"] = new JsonArray(Enumerable.Range(0, ProjectSourceHistory.MaximumJsonNodes).Select(_ => (JsonNode?)null).ToArray());
        result = ProjectSourceHistory.Read(Ref(source), [ProjectHistoryRevision.MaterializedCheckpoint(Ref(source), 0, source.Digest, Json(body))]);
        Assert.Equal(HistoryReadState.ResourceLimit, result.State); Assert.Empty(result.ReconstructionPath);
    }

    [Fact]
    public void ParentEnumerationIsBoundedAndCancellationDisposesTheSupplier()
    {
        var source = Revision(50); var disposed = false;
        IEnumerable<RevisionRef> Endless()
        { try { while (true) yield return new(source.Context.Branch, RevisionId.New()); } finally { disposed = true; } }
        Assert.Throws<ArgumentException>(() => ProjectHistoryRevision.Checkpoint(source, 0, Endless())); Assert.True(disposed);
        using var cancellation = new CancellationTokenSource(); var token = cancellation.Token; Action cancel = cancellation.Cancel;
        IEnumerable<RevisionRef> Cancelling()
        { try { cancel(); yield return Ref(source); } finally { disposed = true; } }
        disposed = false;
        Assert.Throws<OperationCanceledException>(() => ProjectHistoryRevision.Checkpoint(source, 1, Cancelling(), token)); Assert.True(disposed);
    }

    [Fact]
    public async Task WindowsPreparationRejectsForeignAndReentrantHistoryAndClearsAcrossLifecycles()
    {
        var input = Input(source: Source(50));
        CompilerArtifactIdentity Artifact(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString()));
        var host = new LocalNetCompilerHost(profile); var request = new CompilerHostRequest(new(input), 1);
        using var view = new SyntheticWorkbenchViewModel(); view.BeginSynthetic(request); Assert.True(view.TryAccept(await host.CompileAsync(request)));
        var checkpoint = ProjectHistoryRevision.Checkpoint(input.Source, 0);
        Assert.Equal(input.Source.Data, view.ReadSyntheticHistory(Ref(input.Source), [checkpoint])!.Source!.Data);
        var foreign = new RevisionRef(new(TenantId.Parse(Id(1)), ProjectId.Parse(Id(99)), BranchId.Parse(Id(3))), RevisionId.Parse(Id(50)));
        var invoked = false;
        IEnumerable<ProjectHistoryRevision> ForeignSupply() { invoked = true; yield return checkpoint; }
        Assert.Null(view.ReadSyntheticHistory(foreign, ForeignSupply())); Assert.False(invoked); Assert.Null(view.History);
        Action reset = view.Reset;
        IEnumerable<ProjectHistoryRevision> ResettingSupply() { reset(); yield return checkpoint; }
        Assert.Null(view.ReadSyntheticHistory(Ref(input.Source), ResettingSupply())); Assert.Null(view.History); Assert.Equal("Empty", view.State);
        view.BeginSynthetic(new(new(input), 2)); Assert.True(view.TryAccept(await host.CompileAsync(new(new(input), 2))));
        view.PropertyChanged += (sender, _) => { if (sender is SyntheticWorkbenchViewModel { History: not null } current) current.Reset(); };
        Assert.Null(view.ReadSyntheticHistory(Ref(input.Source), [checkpoint])); Assert.Null(view.History);
    }

    [Fact]
    public void ExportedHistoryActuallyReconstructsItsExactCaseAndSurvivesCheckpointCompaction()
    {
        var cases = WebPreviewContractTests.Cases().Select(item => SourceJson.Parse((string)item[0]).Value).ToArray();
        var before = cases.Single(item => item.GetProperty("key").GetString() == "document-before");
        var after = cases.Single(item => item.GetProperty("key").GetString() == "document-after");
        ProjectSource Materialize(System.Text.Json.JsonElement item) => ProjectSourceCodec.Decode(System.Text.Encoding.UTF8.GetBytes(item.GetProperty("sourceText").GetString()!)).RequireSource();
        var beforeSource = Materialize(before); var afterSource = Materialize(after); var delta = ProjectSourceDelta.Create(beforeSource, afterSource);
        var result = ProjectSourceHistory.Read(Ref(afterSource), [ProjectHistoryRevision.Checkpoint(beforeSource, 0), ProjectHistoryRevision.ForwardDelta(delta, 1)]);
        var projection = before.GetProperty("preparedHistory");
        Assert.Equal(result.State.ToString(), projection.GetProperty("state").GetString()); Assert.Equal(result.SourceAdmissionStatus.ToString(), projection.GetProperty("sourceAdmissionStatus").GetString());
        Assert.Equal(afterSource.Data, result.Source!.Data); Assert.Equal(2, projection.GetProperty("reconstructionPath").GetArrayLength());
        Assert.Equal(2, projection.GetProperty("revisions").GetArrayLength());
        Assert.Equal(beforeSource.Context.Data, SourceJson.Parse(projection.GetProperty("reconstructionPath")[0].GetRawText()));
        Assert.Equal(afterSource.Context.Data, SourceJson.Parse(projection.GetProperty("reconstructionPath")[1].GetRawText()));
        Assert.Equal(delta.Documents.Count, projection.GetProperty("deltaRecordCounts").GetProperty("documents").GetInt32());
        Assert.Equal(delta.Declarations.Count, projection.GetProperty("deltaRecordCounts").GetProperty("declarations").GetInt32());
        Assert.Equal(delta.Relations.Count, projection.GetProperty("deltaRecordCounts").GetProperty("relations").GetInt32());
        var missing = ProjectSourceHistory.Read(Ref(afterSource), [ProjectHistoryRevision.ForwardDelta(delta, 1)]);
        Assert.Equal(missing.State.ToString(), projection.GetProperty("missingAnchorState").GetString()); Assert.Null(missing.Source);
        Assert.Equal(afterSource.Data, ProjectSourceHistory.Read(Ref(afterSource), [ProjectHistoryRevision.Checkpoint(result.Source, 1, [Ref(beforeSource)])]).Source!.Data);
        Assert.True(projection.GetProperty("compactedCheckpointVerified").GetBoolean());
    }
}
