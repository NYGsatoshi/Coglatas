using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

/// <summary>
/// Independent P0 #1932 controls for already materialized, synthetic local history.
/// Reconstruction and inverse preparation grant no access, persistence or Merge authority.
/// </summary>
public sealed class SecurityProjectHistoryCandidateTests
{
    private static string Id(int value) => "64001932-0000-4000-8000-" + value.ToString("x12", CultureInfo.InvariantCulture);
    private static BranchRef Branch(int tenant = 1, int project = 2, int branch = 3) =>
        new(TenantId.Parse(Id(tenant)), ProjectId.Parse(Id(project)), BranchId.Parse(Id(branch)));
    private static RevisionRef Revision(int value, BranchRef? branch = null) =>
        new(branch ?? Branch(), RevisionId.Parse(Id(value)));
    private static DocumentId Document => DocumentId.Parse(Id(100));
    private static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private static JsonObject Object(object value) => JsonSerializer.SerializeToNode(value)!.AsObject();
    private static ContentDigest OtherDigest => ContentDigest.Compute("example.security-history/1", Encoding.UTF8.GetBytes("different-original"));

    private static JsonObject Task(int identity, string name) => Object(new
    {
        entityId = Id(identity), documentId = Document.ToString(), kind = "coglatas.task", schemaVersion = 1,
        displayName = name,
        payload = new
        {
            duration = new
            {
                type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false },
                state = "coglatas.known", value = "10"
            },
            ports = Array.Empty<object>()
        }
    });

    private static SourceJson Opaque(string value) => Json(new
    {
        label = value, nested = new { original = "history \u03a9", values = new object?[] { 7, null, true } }
    });

    private static ProjectSource Source(int revision, string first = "Original first", string second = "Original second",
        BranchRef? branch = null, string? opaque = null, Action<JsonObject>? edit = null, bool includeDeclarations = true) =>
        SourceAt(SourceRevisionContext.Committed(Revision(revision, branch)), first, second, opaque, edit, includeDeclarations);

    private static ProjectSource SourceAt(SourceRevisionContext context, string first, string second,
        string? opaque = null, Action<JsonObject>? edit = null, bool includeDeclarations = true)
    {
        var root = Json(new { entityId = Id(101), kind = "coglatas.project", schemaVersion = 1, payload = new { } });
        var document = SourceDocument.Create(Document, "independent-history.plan", root,
            opaque is null ? null : Opaque(opaque));
        var declarations = includeDeclarations ? new JsonArray(Task(201, first), Task(202, second)) : new JsonArray();
        if (opaque is not null)
            declarations.Add(Object(new
            {
                entityId = Id(203), documentId = Document.ToString(), kind = "example.history-opaque",
                schemaVersion = 1, payload = Opaque(opaque).Value
            }));
        var extensions = opaque is null ? null : Json(new[]
        {
            new { @namespace = "example.security.history", schemaVersion = 1, payload = Opaque(opaque).Value }
        });
        var source = ProjectSource.Create(context, new[] { document }, Json(declarations),
            extensionDeclarations: extensions, extensions: opaque is null ? null : Opaque(opaque));
        var body = JsonNode.Parse(source.Data.CanonicalText)!.AsObject();
        if (opaque is not null) body["manifest"]!["extensions"] = JsonNode.Parse(Opaque(opaque).CanonicalText);
        edit?.Invoke(body);
        return ProjectSourceCodec.Decode(Json(body).ToCanonicalBytes()).RequireSource();
    }

    private static SourceJson Settings()
    {
        object Artifact(string name) => new { artifactId = name, version = "1", digest = OtherDigest.Data.Value };
        var none = new { kind = "coglatas.none" };
        var settings = new Dictionary<string, object?>
        {
            ["schema"] = Artifact(ProjectSource.SchemaId), ["compiler"] = Artifact("example.history.compiler"),
            ["ruleset"] = Artifact("example.history.ruleset"), ["semantics"] = Artifact("example.history.semantics"),
            ["ir"] = Artifact("example.history.ir"), ["evaluator"] = Artifact("example.history.evaluator"),
            ["scheduler"] = none, ["solver"] = none, ["stochastic"] = none, ["importer"] = none, ["scenario"] = none,
            ["clock"] = new { kind = "coglatas.not-applicable" }, ["sourceCoverage"] = "coglatas.complete",
            ["rules"] = new[] { CoreCompilerRules.Schema, CoreCompilerRules.Declarations, CoreCompilerRules.Relations }
                .Select(rule => new { ruleId = rule, version = 1, required = true, configuration = new { } }).ToArray(),
            ["executionProfile"] = new
            {
                purpose = "coglatas.interactive", maximumOperations = 1_000_000, maximumDeclarations = 10_000,
                maximumRelations = 10_000, maximumDigits = 100, maximumScale = 18, rounding = "coglatas.ties-to-even"
            }
        };
        foreach (var key in new[] { "authorizedExternalSnapshots", "resourceSnapshots", "allocationSnapshots", "observations",
                     "evidence", "descriptors", "units", "calendars", "timeZones", "featureSet", "targets", "analysisScope" })
            settings[key] = Array.Empty<object>();
        return Json(settings);
    }

    private static CompilerInputManifest Input(ProjectSource source, SourceJson? settings = null) => new(source, settings ?? Settings());
    private static RevisionRef At(ProjectSource source) => source.Context.CommittedRevision!;
    private static ProposalContext Proposal(ProjectSource current) =>
        new(ProposalId.Parse(Id(801)), CandidateRevisionId.Parse(Id(802)), At(current), At(current));
    private static ProjectHistoryRevision[] History(ProjectSource before, ProjectSource after, ProjectSource current) =>
    [
        ProjectHistoryRevision.Checkpoint(before, 10),
        ProjectHistoryRevision.ForwardDelta(ProjectSourceDelta.Create(before, after), 20),
        ProjectHistoryRevision.ForwardDelta(ProjectSourceDelta.Create(after, current), 30)
    ];
    private static DraftRevertPreview Revert(CompilerInputManifest before, CompilerInputManifest after, CompilerInputManifest current,
        ProposalContext? proposal = null, IEnumerable<ProjectHistoryRevision?>? history = null,
        ContentDigest? expectedCurrent = null, CancellationToken cancellationToken = default) =>
        ProjectDraftRevert.Preview(ProjectSemanticBinder.Bind(before), ProjectSemanticBinder.Bind(after), ProjectSemanticBinder.Bind(current),
            before.InputDigest, after.InputDigest, expectedCurrent ?? current.InputDigest, proposal ?? Proposal(current.Source),
            history ?? History(before.Source, after.Source, current.Source), cancellationToken);

    private static void ExactSource(ProjectSource expected, HistoryReconstruction actual, params RevisionRef[] path)
    {
        Assert.Equal(HistoryReadState.Ready, actual.State);
        var source = Assert.IsType<ProjectSource>(actual.Source);
        Assert.Equal(expected.Data.ToCanonicalBytes(), source.Data.ToCanonicalBytes());
        Assert.Equal(expected.Digest, source.Digest);
        Assert.Equal(expected.Context, source.Context);
        Assert.Equal(path, actual.ReconstructionPath.ToArray());
    }

    private static void Refused(HistoryReconstruction actual, HistoryReadState expected)
    {
        Assert.Equal(expected, actual.State);
        Assert.Null(actual.Source);
        Assert.Null(actual.SourceAdmissionStatus);
        Assert.Empty(actual.ReconstructionPath);
    }

    private static void Refused(ProjectSemanticDiff actual, SemanticDiffState expected)
    {
        Assert.Equal(expected, actual.State);
        Assert.False(actual.NoChanges);
        Assert.Empty(actual.Changes);
        Assert.Null(actual.BeforeInputDigest);
        Assert.Null(actual.AfterInputDigest);
        Assert.Null(actual.BeforeContext);
        Assert.Null(actual.AfterContext);
    }

    private static void Refused(DraftRevertPreview actual, DraftRevertState expected)
    {
        Assert.Equal(expected, actual.State);
        Assert.Null(actual.Plan);
        if (expected != DraftRevertState.Conflict) Assert.Empty(actual.Conflicts);
    }

    private static void RejectCollectionMutation<T>(IReadOnlyList<T> values)
    {
        var original = values.ToArray();
        Assert.False(values is T[]);
        if (values is ICollection legacy)
        {
            Assert.False(legacy.SyncRoot is Array);
            if (legacy.SyncRoot is IList storage)
            {
                Assert.Throws<NotSupportedException>(() => storage.Clear());
                if (storage.Count > 0) Assert.Throws<NotSupportedException>(() => storage[0] = default(T));
            }
        }
        if (values is ICollection<T> collection)
        {
            Assert.True(collection.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => collection.Clear());
            Assert.Throws<NotSupportedException>(() => collection.Add(default!));
        }
        if (values is IList<T> indexed && values.Count > 0)
        {
            Assert.Throws<NotSupportedException>(() => indexed[0] = default!);
            Assert.Throws<NotSupportedException>(() => indexed.RemoveAt(0));
        }
        if (original.Length > 0)
        {
            var detached = values.ToArray(); detached[0] = default!;
            Assert.Equal(original, values.ToArray());
        }
    }

    [Fact]
    public void CheckpointReadPreservesExactSourceAndRequestedRevision()
    {
        var source = Source(11, includeDeclarations: false);
        var checkpoint = ProjectHistoryRevision.Checkpoint(source, 47);
        var result = ProjectSourceHistory.Read(At(source), new[] { checkpoint });
        ExactSource(source, result, At(source));
        Assert.Equal(SourceDecodeStatus.StructurallyDecoded, result.SourceAdmissionStatus);
        Assert.Equal(source.Data.ToCanonicalBytes(), checkpoint.CheckpointData!.ToCanonicalBytes());
        Assert.Equal(source.Digest, checkpoint.ExpectedSourceDigest);
        Assert.Null(checkpoint.Delta);
    }

    [Fact]
    public void ForwardDeltaReconstructsExactBytesIndependentOfRecordOrder()
    {
        var before = Source(11); var after = Source(12, "Changed first");
        var delta = ProjectSourceDelta.Create(before, after);
        var records = new[] { ProjectHistoryRevision.ForwardDelta(delta, 20), ProjectHistoryRevision.Checkpoint(before, 10) };
        ExactSource(after, ProjectSourceHistory.Read(At(after), records), At(before), At(after));
        ExactSource(after, ProjectSourceHistory.Read(At(after), records.Reverse()), At(before), At(after));
        Assert.Equal(At(before), delta.BeforeRevision); Assert.Equal(At(after), delta.AfterRevision);
        Assert.Equal(before.Digest, delta.BeforeDigest); Assert.Equal(after.Digest, delta.AfterDigest);
        Assert.Null(records[0].CheckpointData);
        Assert.Equal(Id(201), Assert.Single(delta.Declarations).Identity.ToString());
    }

    [Fact]
    public void DeltasRetainOpaqueMetadataAndOriginalAddedRemovedValues()
    {
        var before = Source(11, opaque: "old opaque");
        var after = Source(12, opaque: "new opaque", edit: body =>
        {
            body["declarations"]!.AsArray().RemoveAt(0);
            body["declarations"]!.AsArray().Add(Task(204, "New independent record"));
        });
        var delta = ProjectSourceDelta.Create(before, after);
        var removed = Assert.Single(delta.Declarations, item => item.Identity == EntityId.Parse(Id(201)));
        var added = Assert.Single(delta.Declarations, item => item.Identity == EntityId.Parse(Id(204)));
        Assert.Equal(Json(Task(201, "Original first")).ToCanonicalBytes(), removed.Before!.ToCanonicalBytes());
        Assert.Null(removed.After); Assert.Null(added.Before);
        Assert.Equal(Json(Task(204, "New independent record")).ToCanonicalBytes(), added.After!.ToCanonicalBytes());
        var document = Assert.Single(delta.Documents);
        Assert.Equal(before.Documents[0].Data.ToCanonicalBytes(), document.Before!.ToCanonicalBytes());
        Assert.Equal(after.Documents[0].Data.ToCanonicalBytes(), document.After!.ToCanonicalBytes());
        Assert.Equal(Opaque("old opaque"), SourceJson.Parse(delta.BeforeMetadata.Value.GetProperty("extensions").GetRawText()));
        Assert.Equal(Opaque("new opaque"), SourceJson.Parse(delta.AfterMetadata.Value.GetProperty("extensions").GetRawText()));
        Assert.True(delta.RetainedCanonicalBytes > 0);
        var result = ProjectSourceHistory.Read(At(after), new[]
        {
            ProjectHistoryRevision.Checkpoint(before, 10), ProjectHistoryRevision.ForwardDelta(delta, 20)
        });
        ExactSource(after, result, At(before), At(after));
        Assert.Equal(SourceDecodeStatus.Unsupported, result.SourceAdmissionStatus);
    }

    [Fact]
    public void CompactedCheckpointPreservesLaterDeltaAndMissingAnchorRefuses()
    {
        var first = Source(11); var second = Source(12, "Second"); var third = Source(13, "Third");
        var full = History(first, second, third);
        ExactSource(third, ProjectSourceHistory.Read(At(third), full), At(first), At(second), At(third));
        var compacted = new[] { ProjectHistoryRevision.Checkpoint(second, 20, new[] { At(first) }), full[2] };
        ExactSource(third, ProjectSourceHistory.Read(At(third), compacted), At(second), At(third));
        Refused(ProjectSourceHistory.Read(At(third), new[] { full[2] }), HistoryReadState.Unavailable);
        Assert.Equal(third.Data.ToCanonicalBytes(), ProjectSourceHistory.Read(At(third), full).Source!.Data.ToCanonicalBytes());
    }

    [Fact]
    public void MissingOrUnavailableHistoryNeverExposesSourceOrPath()
    {
        var source = Source(11); var checkpoint = ProjectHistoryRevision.Checkpoint(source, 10);
        Refused(ProjectSourceHistory.Read(At(source), Array.Empty<ProjectHistoryRevision>()), HistoryReadState.Unavailable);
        Refused(ProjectSourceHistory.Read(Revision(12), new[] { checkpoint }), HistoryReadState.Unavailable);
        Refused(ProjectSourceHistory.Read(At(source), new[] { ProjectHistoryRevision.Unavailable(At(source), 10, source.Digest) }), HistoryReadState.Unavailable);
        Refused(ProjectSourceHistory.Read(At(source), new ProjectHistoryRevision?[] { checkpoint, null }), HistoryReadState.Corrupt);
        ExactSource(source, ProjectSourceHistory.Read(At(source), new[] { checkpoint }), At(source));
    }

    [Fact]
    public void CheckpointIdentitySchemaAndBodyFailuresHaveExplicitOutcomes()
    {
        var source = Source(11); var sameVersion = Source(11, "Contradictory body"); var otherVersion = Source(12);
        foreach (var record in new[]
        {
            ProjectHistoryRevision.MaterializedCheckpoint(At(source), 10, OtherDigest, source.Data),
            ProjectHistoryRevision.MaterializedCheckpoint(At(source), 10, source.Digest, sameVersion.Data),
            ProjectHistoryRevision.MaterializedCheckpoint(At(source), 10, otherVersion.Digest, otherVersion.Data)
        }) Refused(ProjectSourceHistory.Read(At(source), new[] { record }), HistoryReadState.Corrupt);
        var future = JsonNode.Parse(source.Data.CanonicalText)!.AsObject(); future["schemaVersion"] = 2;
        var unsupported = ProjectHistoryRevision.MaterializedCheckpoint(At(source), 10, source.Digest, Json(future));
        Refused(ProjectSourceHistory.Read(At(source), new[] { unsupported }), HistoryReadState.Unsupported);
        ExactSource(source, ProjectSourceHistory.Read(At(source), new[] { ProjectHistoryRevision.Checkpoint(source, 10) }), At(source));
    }

    [Fact]
    public void DuplicateSelfParentAndCycleLineageAreRefused()
    {
        var source = Source(11); var next = Source(12); var checkpoint = ProjectHistoryRevision.Checkpoint(source, 10);
        Refused(ProjectSourceHistory.Read(At(source), new[] { checkpoint, checkpoint }), HistoryReadState.Corrupt);
        Refused(ProjectSourceHistory.Read(At(source), new[] { ProjectHistoryRevision.Checkpoint(source, 10, new[] { At(source) }) }), HistoryReadState.Corrupt);
        Refused(ProjectSourceHistory.Read(At(next), new[]
        {
            checkpoint, ProjectHistoryRevision.Checkpoint(next, 20, new[] { At(source), At(source) })
        }), HistoryReadState.Corrupt);
        var otherBranch = Source(13, branch: Branch(branch: 4));
        Refused(ProjectSourceHistory.Read(At(source), new[]
        {
            ProjectHistoryRevision.Checkpoint(source, 10, new[] { At(otherBranch) }),
            ProjectHistoryRevision.Checkpoint(otherBranch, 10, new[] { At(source) })
        }), HistoryReadState.Corrupt);
    }

    [Fact]
    public void ForeignTenantProjectAndParentScopesAreRefused()
    {
        var source = Source(11); var checkpoint = ProjectHistoryRevision.Checkpoint(source, 10);
        foreach (var branch in new[] { Branch(tenant: 91), Branch(project: 92) })
        {
            var foreign = Source(12, branch: branch);
            Refused(ProjectSourceHistory.Read(At(source), new[] { checkpoint, ProjectHistoryRevision.Checkpoint(foreign, 20) }), HistoryReadState.OutOfScope);
            Refused(ProjectSourceHistory.Read(At(source), new[]
            {
                ProjectHistoryRevision.Checkpoint(source, 10, new[] { At(foreign) })
            }), HistoryReadState.OutOfScope);
            Assert.Throws<ArgumentException>(() => ProjectSourceDelta.Create(source, foreign));
        }
        ExactSource(source, ProjectSourceHistory.Read(At(source), new[] { checkpoint }), At(source));
    }

    [Fact]
    public void SameBranchSequenceMustIncreaseButCrossBranchSequenceIsIndependent()
    {
        var before = Source(11); var after = Source(12, "Changed"); var delta = ProjectSourceDelta.Create(before, after);
        foreach (var sequence in new[] { 20, 21 })
            Refused(ProjectSourceHistory.Read(At(after), new[]
            {
                ProjectHistoryRevision.Checkpoint(before, sequence), ProjectHistoryRevision.ForwardDelta(delta, 20)
            }), HistoryReadState.Corrupt);
        var otherBranch = Source(13, branch: Branch(branch: 4));
        var independent = ProjectSourceHistory.Read(At(otherBranch), new[]
        {
            ProjectHistoryRevision.Checkpoint(before, 900), ProjectHistoryRevision.Checkpoint(otherBranch, 1, new[] { At(before) })
        });
        ExactSource(otherBranch, independent, At(otherBranch));
        Assert.Throws<ArgumentException>(() => ProjectSourceDelta.Create(before, otherBranch));
        Assert.Throws<ArgumentException>(() => ProjectSourceDelta.Create(before, before));
    }

    [Fact]
    public void RecordAndDepthBoundsRefuseWithoutPartialHistory()
    {
        var source = Source(11, includeDeclarations: false);
        var records = Enumerable.Range(1000, ProjectSourceHistory.MaximumRecords - 1)
            .Select(number => ProjectHistoryRevision.Unavailable(Revision(number), number, OtherDigest))
            .Prepend(ProjectHistoryRevision.Checkpoint(source, 10)).ToArray();
        ExactSource(source, ProjectSourceHistory.Read(At(source), records), At(source));
        Refused(ProjectSourceHistory.Read(At(source), records.Append(ProjectHistoryRevision.Unavailable(Revision(9999), 9999, OtherDigest))), HistoryReadState.ResourceLimit);
        var versions = Enumerable.Range(1, ProjectSourceHistory.MaximumReconstructionDepth + 1)
            .Select(number => Source(number + 100, includeDeclarations: false)).ToArray();
        var chain = new List<ProjectHistoryRevision> { ProjectHistoryRevision.Checkpoint(versions[0], 0) };
        for (var index = 1; index < versions.Length; index++)
            chain.Add(ProjectHistoryRevision.ForwardDelta(ProjectSourceDelta.Create(versions[index - 1], versions[index]), index));
        ExactSource(versions[^2], ProjectSourceHistory.Read(At(versions[^2]), chain.Take(chain.Count - 1)),
            versions.Take(versions.Length - 1).Select(At).ToArray());
        Refused(ProjectSourceHistory.Read(At(versions[^1]), chain), HistoryReadState.ResourceLimit);
        var parents = Enumerable.Range(2000, 8).Select(number => Revision(number)).ToArray();
        Assert.Equal(parents, ProjectHistoryRevision.Checkpoint(source, 10, parents).Parents.ToArray());
        Assert.Throws<ArgumentException>(() => ProjectHistoryRevision.Checkpoint(source, 10, parents.Append(Revision(3000))));
    }

    [Fact]
    public void CancellationDuringHistoryEnumerationDisposesAndPreservesInputs()
    {
        var source = Source(11); var bytes = source.Data.ToCanonicalBytes(); var disposed = false; var yielded = 0;
        using var cancellation = new CancellationTokenSource();
        IEnumerable<ProjectHistoryRevision?> Interrupted()
        {
            try
            {
                yielded++; yield return ProjectHistoryRevision.Checkpoint(source, 10);
                cancellation.Cancel(); yielded++; yield return ProjectHistoryRevision.Unavailable(Revision(12), 20, OtherDigest);
            }
            finally { disposed = true; }
        }
        Assert.Throws<OperationCanceledException>(() => ProjectSourceHistory.Read(At(source), Interrupted(), cancellation.Token));
        Assert.True(disposed); Assert.Equal(2, yielded); Assert.Equal(bytes, source.Data.ToCanonicalBytes());
        Assert.Throws<OperationCanceledException>(() => ProjectSourceHistory.Read(At(source), Array.Empty<ProjectHistoryRevision>(), cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => ProjectSourceDelta.Create(source, Source(12), cancellation.Token));
        ExactSource(source, ProjectSourceHistory.Read(At(source), new[] { ProjectHistoryRevision.Checkpoint(source, 10) }), At(source));
    }

    [Fact]
    public void RetainedHistorySourceAndDeltaCollectionsCannotBeMutated()
    {
        void Edge(JsonObject body, string label) => body["relations"] = new JsonArray(Object(new
        {
            relationId = Id(301), documentId = Document.ToString(), kind = "example.history-edge", schemaVersion = 1,
            source = new { }, target = new { }, payload = new { label }
        }));
        var before = Source(11, opaque: "before", edit: body => Edge(body, "before"));
        var after = Source(12, "After", opaque: "after", edit: body => Edge(body, "after"));
        var original = after.Data.ToCanonicalBytes(); var delta = ProjectSourceDelta.Create(before, after);
        var parentArray = new[] { At(before) }; var checkpoint = ProjectHistoryRevision.Checkpoint(after, 20, parentArray);
        parentArray[0] = Revision(999);
        Assert.Equal(At(before), Assert.Single(checkpoint.Parents));
        RejectCollectionMutation(checkpoint.Parents); RejectCollectionMutation(after.Documents);
        RejectCollectionMutation(after.Manifest.DocumentIds); RejectCollectionMutation(delta.Documents);
        RejectCollectionMutation(delta.Declarations); RejectCollectionMutation(delta.Relations);
        var result = ProjectSourceHistory.Read(At(after), new[] { ProjectHistoryRevision.Checkpoint(before, 10), ProjectHistoryRevision.ForwardDelta(delta, 20) });
        RejectCollectionMutation(result.ReconstructionPath);
        var detached = result.Source!.Data.ToCanonicalBytes(); Array.Fill(detached, (byte)0);
        Assert.Equal(original, after.Data.ToCanonicalBytes());
        ExactSource(after, result, At(before), At(after));
    }

    [Fact]
    public void SemanticDiffBindsOriginalValuesToTheirExactSides()
    {
        var before = Input(Source(11)); var after = Input(Source(12, "Updated first"));
        var left = ProjectSemanticBinder.Bind(before); var right = ProjectSemanticBinder.Bind(after);
        Assert.True(left.BindingComplete); Assert.True(right.BindingComplete);
        var diff = ProjectSemanticDiff.Compare(left, right, before.InputDigest, after.InputDigest);
        Assert.Equal(SemanticDiffState.Complete, diff.State); Assert.False(diff.NoChanges);
        Assert.Equal(before.InputDigest, diff.BeforeInputDigest); Assert.Equal(after.InputDigest, diff.AfterInputDigest);
        var change = Assert.Single(diff.Changes);
        Assert.Equal(SemanticChangeKind.DisplayName, change.Kind); Assert.Equal(Id(201), change.Identity);
        Assert.Equal("/displayName", change.Path);
        Assert.Equal(Json("Original first").ToCanonicalBytes(), change.Before!.ToCanonicalBytes());
        Assert.Equal(Json("Updated first").ToCanonicalBytes(), change.After!.ToCanonicalBytes());
        Assert.Equal(before.Source.Context, change.BeforeLocation!.Context); Assert.Equal(after.Source.Context, change.AfterLocation!.Context);
        Assert.Equal(Document, change.BeforeLocation.DocumentId); Assert.Equal(Document, change.AfterLocation.DocumentId);
        RejectCollectionMutation(diff.Changes); RejectCollectionMutation(left.Declarations); RejectCollectionMutation(right.Declarations);
        var unchanged = Input(Source(13));
        var equal = ProjectSemanticDiff.Compare(left, ProjectSemanticBinder.Bind(unchanged), before.InputDigest, unchanged.InputDigest);
        Assert.True(equal.NoChanges); Assert.Empty(equal.Changes);
    }

    [Fact]
    public void SemanticDiffRefusesMissingStaleAndForeignInputsWithoutMetadata()
    {
        var before = Input(Source(11)); var after = Input(Source(12, "Updated"));
        var left = ProjectSemanticBinder.Bind(before); var right = ProjectSemanticBinder.Bind(after);
        Refused(ProjectSemanticDiff.Compare(null, right, before.InputDigest, after.InputDigest), SemanticDiffState.Unavailable);
        Refused(ProjectSemanticDiff.Compare(left, null, before.InputDigest, after.InputDigest), SemanticDiffState.Unavailable);
        Refused(ProjectSemanticDiff.Compare(left, right, OtherDigest, after.InputDigest), SemanticDiffState.StaleInput);
        Refused(ProjectSemanticDiff.Compare(left, right, before.InputDigest, OtherDigest), SemanticDiffState.StaleInput);
        foreach (var branch in new[] { Branch(project: 92), Branch(tenant: 91) })
        {
            var foreign = Input(Source(12, branch: branch));
            Refused(ProjectSemanticDiff.Compare(left, ProjectSemanticBinder.Bind(foreign), before.InputDigest, foreign.InputDigest), SemanticDiffState.OutOfScope);
        }
    }

    [Fact]
    public void SameVersionContradictionsAndOpaqueChangesRemainIncomplete()
    {
        var before = Input(Source(11)); var contradiction = Input(Source(11, "Contradictory same version"));
        var conflict = ProjectSemanticDiff.Compare(ProjectSemanticBinder.Bind(before), ProjectSemanticBinder.Bind(contradiction), before.InputDigest, contradiction.InputDigest);
        Assert.Equal(SemanticDiffState.Incomplete, conflict.State); Assert.False(conflict.NoChanges); Assert.Single(conflict.Changes);
        Assert.Equal(before.Source.Context, conflict.BeforeContext); Assert.Equal(conflict.BeforeContext, conflict.AfterContext);
        var old = Input(Source(12, opaque: "opaque before")); var newer = Input(Source(13, opaque: "opaque after"));
        var diff = ProjectSemanticDiff.Compare(ProjectSemanticBinder.Bind(old), ProjectSemanticBinder.Bind(newer), old.InputDigest, newer.InputDigest);
        Assert.Equal(SemanticDiffState.Incomplete, diff.State); Assert.False(diff.NoChanges);
        var sourceChange = Assert.Single(diff.Changes, change => change.RecordType == "source" && change.Path == "/extensions");
        Assert.Equal(SemanticChangeKind.Uninterpreted, sourceChange.Kind);
        Assert.Equal(Opaque("opaque before"), sourceChange.Before); Assert.Equal(Opaque("opaque after"), sourceChange.After);
        var documentChange = Assert.Single(diff.Changes, change => change.RecordType == "document" && change.Path == "/extensions");
        Assert.Equal(old.Source.Context, documentChange.BeforeLocation!.Context); Assert.Equal(newer.Source.Context, documentChange.AfterLocation!.Context);
        Assert.Equal(Opaque("opaque before"), documentChange.Before); Assert.Equal(Opaque("opaque after"), documentChange.After);
        var manifestChange = Assert.Single(diff.Changes, change => change.RecordType == "source" && change.Path == "/manifest");
        Assert.Equal(old.Source.Manifest.Data, manifestChange.Before); Assert.Equal(newer.Source.Manifest.Data, manifestChange.After);
    }

    [Fact]
    public void DiffBoundsNeverPublishATruncatedEquality()
    {
        var before = Input(Source(11)); var after = Input(Source(12, "First changed", "Second changed"));
        var left = ProjectSemanticBinder.Bind(before); var right = ProjectSemanticBinder.Bind(after);
        var accepted = ProjectSemanticDiff.Compare(left, right, before.InputDigest, after.InputDigest, maximumChanges: 2);
        Assert.Equal(SemanticDiffState.Complete, accepted.State); Assert.Equal(2, accepted.Changes.Count);
        Refused(ProjectSemanticDiff.Compare(left, right, before.InputDigest, after.InputDigest, maximumChanges: 1), SemanticDiffState.ResourceLimit);
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectSemanticDiff.Compare(left, right, before.InputDigest, after.InputDigest, maximumChanges: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectSemanticDiff.Compare(left, right, before.InputDigest, after.InputDigest, maximumChanges: 1025));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProjectSemanticDiff.Compare(left, right, before.InputDigest, after.InputDigest, cancellationToken: cancelled.Token));
    }

    [Fact]
    public void InverseDraftRestoresChangedRecordsAndPreservesLaterIndependentWork()
    {
        var before = Input(Source(11)); var after = Input(Source(12, "Reverted edit"));
        var current = Input(Source(13, "Reverted edit", "Later independent edit"));
        var bytes = current.Source.Data.ToCanonicalBytes(); var proposal = Proposal(current.Source);
        var preview = Revert(before, after, current, proposal);
        Assert.Equal(DraftRevertState.Ready, preview.State); Assert.Empty(preview.Conflicts);
        var plan = Assert.IsType<ProjectDraftRevertPlan>(preview.Plan);
        var expected = SourceAt(SourceRevisionContext.Candidate(proposal), "Original first", "Later independent edit");
        Assert.Equal(expected.Data.ToCanonicalBytes(), plan.After.Source.Data.ToCanonicalBytes());
        Assert.Equal(expected.Digest, plan.After.Source.Digest); Assert.Equal(current.Settings, plan.After.Settings);
        Assert.Same(current, plan.Before); Assert.Same(plan.After, plan.ApplyToDraft(current));
        Assert.Equal(At(before.Source), plan.RevertedParent); Assert.Equal(At(after.Source), plan.RevertedRevision);
        Assert.Equal(1, plan.Data.Value.GetProperty("changedRecordCount").GetInt32());
        Assert.Equal(bytes, current.Source.Data.ToCanonicalBytes()); Assert.NotEqual(before.InputDigest, plan.After.InputDigest);
    }

    [Fact]
    public void InverseDraftRejectsOverlappingChangesAtomically()
    {
        var before = Input(Source(11)); var after = Input(Source(12, "First target edit", "Second target edit"));
        var current = Input(Source(13, "Conflicting later edit", "Second target edit"));
        var original = current.Source.Data.ToCanonicalBytes();
        var refused = Revert(before, after, current);
        Refused(refused, DraftRevertState.Conflict);
        var conflict = Assert.Single(refused.Conflicts);
        Assert.Equal("declarations", conflict.RecordType); Assert.Equal(Id(201), conflict.Identity);
        RejectCollectionMutation(refused.Conflicts); Assert.Equal(original, current.Source.Data.ToCanonicalBytes());
        var independent = Input(Source(13, "First target edit", "Second target edit"));
        var accepted = Revert(before, after, independent);
        var plan = Assert.IsType<ProjectDraftRevertPlan>(accepted.Plan);
        Assert.Equal(DraftRevertState.Ready, accepted.State);
        var expected = SourceAt(SourceRevisionContext.Candidate(Proposal(independent.Source)), "Original first", "Original second");
        Assert.Equal(expected.Data.ToCanonicalBytes(), plan.After.Source.Data.ToCanonicalBytes());
        Assert.Equal(2, plan.Data.Value.GetProperty("changedRecordCount").GetInt32());
    }

    [Fact]
    public void InverseDraftRequiresExactHistoryAndCurrentProposalBasis()
    {
        var before = Input(Source(11)); var after = Input(Source(12, "Target edit")); var current = Input(Source(13, "Target edit", "Later"));
        var history = History(before.Source, after.Source, current.Source);
        Assert.Equal(DraftRevertState.Ready, Revert(before, after, current).State);
        Refused(Revert(before, after, current, history: history.Skip(1)), DraftRevertState.Unavailable);
        var substitute = Source(13, "Different current body", "Later");
        var wrongBody = new[] { history[0], history[1], ProjectHistoryRevision.Checkpoint(substitute, 30, new[] { At(after.Source) }) };
        Refused(Revert(before, after, current, history: wrongBody), DraftRevertState.Unavailable);
        var proposal = Proposal(current.Source);
        Refused(Revert(before, after, current, new(proposal.ProposalId, proposal.CandidateRevisionId, At(before.Source), At(current.Source))), DraftRevertState.InvalidContext);
        Refused(Revert(before, after, current, new(proposal.ProposalId, proposal.CandidateRevisionId, At(current.Source), At(after.Source))), DraftRevertState.InvalidContext);
        Refused(Revert(before, after, current, expectedCurrent: OtherDigest), DraftRevertState.StaleInput);
        var merged = new[] { history[0], ProjectHistoryRevision.Checkpoint(after.Source, 20, new[] { At(before.Source), Revision(99) }), history[2] };
        Refused(Revert(before, after, current, history: merged), DraftRevertState.Unsupported);
        var foreign = Input(Source(13, "Target edit", "Later", Branch(branch: 4)));
        Refused(Revert(before, after, foreign, history: history), DraftRevertState.OutOfScope);
    }

    [Fact]
    public void InverseApplicationRejectsStaleBodiesSettingsAndCancellation()
    {
        var before = Input(Source(11)); var after = Input(Source(12, "Target edit")); var current = Input(Source(13, "Target edit", "Later"));
        var plan = Assert.IsType<ProjectDraftRevertPlan>(Revert(before, after, current).Plan);
        var candidateBytes = plan.After.Source.Data.ToCanonicalBytes(); var currentBytes = current.Source.Data.ToCanonicalBytes();
        Assert.Same(plan.After, plan.ApplyToDraft(current));
        Assert.Null(plan.ApplyToDraft(Input(Source(13, "Changed after preview", "Later"))));
        Assert.Null(plan.ApplyToDraft(Input(Source(14, "Target edit", "Later"))));
        var settings = JsonNode.Parse(current.Settings.CanonicalText)!.AsObject();
        settings["featureSet"] = new JsonArray("example.changed-history-feature");
        Assert.Null(plan.ApplyToDraft(Input(current.Source, Json(settings))));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => plan.ApplyToDraft(current, cancelled.Token));
        Assert.Throws<OperationCanceledException>(() => Revert(before, after, current, cancellationToken: cancelled.Token));
        Assert.Equal(candidateBytes, plan.After.Source.Data.ToCanonicalBytes()); Assert.Equal(currentBytes, current.Source.Data.ToCanonicalBytes());
    }
}
