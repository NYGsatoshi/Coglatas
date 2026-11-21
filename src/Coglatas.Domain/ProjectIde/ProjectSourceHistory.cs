using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Coglatas.Domain.ProjectIde;

public enum HistoryReadState { Ready, Unavailable, OutOfScope, Unsupported, Corrupt, ResourceLimit }
public sealed record SourceHistoryChange<TIdentity>(TIdentity Identity, SourceJson? Before, SourceJson? After)
    where TIdentity : struct, IProjectIdentity<TIdentity>;

/// <summary>In-process typed forward delta. No stored Source references, executable effects or Registry adoption.</summary>
public sealed class ProjectSourceDelta
{
    public RevisionRef BeforeRevision { get; }
    public RevisionRef AfterRevision { get; }
    public ContentDigest BeforeDigest { get; }
    public ContentDigest AfterDigest { get; }
    public SourceJson BeforeMetadata { get; }
    public SourceJson AfterMetadata { get; }
    public IReadOnlyList<SourceHistoryChange<DocumentId>> Documents { get; }
    public IReadOnlyList<SourceHistoryChange<EntityId>> Declarations { get; }
    public IReadOnlyList<SourceHistoryChange<RelationId>> Relations { get; }
    public int RetainedCanonicalBytes { get; }
    internal int ProcessingNodeCharge { get; }

    private ProjectSourceDelta(ProjectSource before, ProjectSource after, int processingNodeCharge, CancellationToken cancellationToken)
    {
        BeforeRevision = before.Context.CommittedRevision!; AfterRevision = after.Context.CommittedRevision!;
        BeforeDigest = before.Digest; AfterDigest = after.Digest;
        ProcessingNodeCharge = processingNodeCharge;
        BeforeMetadata = Metadata(before); AfterMetadata = Metadata(after);
        Documents = Changes<DocumentId>("documents", "documentId");
        Declarations = Changes<EntityId>("declarations", "entityId");
        Relations = Changes<RelationId>("relations", "relationId");
        RetainedCanonicalBytes = Bytes(BeforeMetadata) + Bytes(AfterMetadata) +
            Documents.Sum(item => Bytes(item.Before) + Bytes(item.After)) +
            Declarations.Sum(item => Bytes(item.Before) + Bytes(item.After)) + Relations.Sum(item => Bytes(item.Before) + Bytes(item.After));
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<SourceHistoryChange<T>> Changes<T>(string field, string identity) where T : struct, IProjectIdentity<T>
        {
            var oldRecords = Records<T>(before, field, identity); var newRecords = Records<T>(after, field, identity);
            var changes = new List<SourceHistoryChange<T>>();
            foreach (var id in oldRecords.Keys.Union(newRecords.Keys).OrderBy(id => id.ToString(), StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested(); oldRecords.TryGetValue(id, out var oldRecord); newRecords.TryGetValue(id, out var newRecord);
                if (!Equals(oldRecord, newRecord)) changes.Add(new(id, oldRecord, newRecord));
            }
            return new ImmutableListView<SourceHistoryChange<T>>(changes);
        }
    }

    public static ProjectSourceDelta Create(ProjectSource before, ProjectSource after, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(before); ArgumentNullException.ThrowIfNull(after); cancellationToken.ThrowIfCancellationRequested();
        if (before.Context.CommittedRevision is null || after.Context.CommittedRevision is null ||
            before.Context.Branch != after.Context.Branch || before.Context.Equals(after.Context))
            throw new ArgumentException("A forward delta needs two distinct committed revisions of one Branch.");
        if (Bytes(before.Data) > 4_194_304 || Bytes(after.Data) > 4_194_304 || Bytes(before.Data) + Bytes(after.Data) > ProjectSourceHistory.MaximumStoredBytes)
            throw new ArgumentException("Delta creation exceeds the local byte bound.");
        var nodes = ProjectSourceHistory.MaximumJsonNodes;
        if (!ProjectSourceHistory.WithinJsonBounds(before.Data.Value, 0, ref nodes, cancellationToken) ||
            !ProjectSourceHistory.WithinJsonBounds(after.Data.Value, 0, ref nodes, cancellationToken))
            throw new ArgumentException("Delta creation exceeds local JSON processing bounds.");
        return new(before, after, ProjectSourceHistory.MaximumJsonNodes - nodes, cancellationToken);
    }

    internal SourceDecodeResult? Apply(ProjectSource before, CancellationToken cancellationToken)
    {
        if (before.Context.CommittedRevision != BeforeRevision || !before.Digest.Equals(BeforeDigest) || !Metadata(before).Equals(BeforeMetadata)) return null;
        var root = JsonNode.Parse(AfterMetadata.CanonicalText)!.AsObject();
        if (!ApplyRecords("documents", "documentId", Documents) || !ApplyRecords("declarations", "entityId", Declarations) ||
            !ApplyRecords("relations", "relationId", Relations)) return null;
        cancellationToken.ThrowIfCancellationRequested();
        var decoded = ProjectSourceCodec.Decode(SourceJson.FromObject(root).ToCanonicalBytes(), cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var after = decoded.Source;
        return after?.Context.CommittedRevision == AfterRevision && after.Digest.Equals(AfterDigest) ? decoded : null;

        bool ApplyRecords<T>(string field, string identity, IReadOnlyList<SourceHistoryChange<T>> changes) where T : struct, IProjectIdentity<T>
        {
            var records = Records<T>(before, field, identity);
            foreach (var change in changes)
            {
                cancellationToken.ThrowIfCancellationRequested(); records.TryGetValue(change.Identity, out var existing);
                if (!Equals(existing, change.Before)) return false;
                if (change.After is null) records.Remove(change.Identity); else records[change.Identity] = change.After;
            }
            var array = new JsonArray();
            foreach (var item in records.OrderBy(item => item.Key.ToString(), StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested(); array.Add(JsonNode.Parse(item.Value.CanonicalText));
            }
            root[field] = array; return true;
        }
    }

    internal static int Bytes(SourceJson? data) => data is null ? 0 : Encoding.UTF8.GetByteCount(data.CanonicalText);
    private static SourceJson Metadata(ProjectSource source) => SourceJson.FromObject(source.Data.Value.EnumerateObject()
        .Where(member => member.Name is not ("documents" or "declarations" or "relations"))
        .ToDictionary(member => member.Name, member => member.Value), source.Data.Limits);
    private static Dictionary<T, SourceJson> Records<T>(ProjectSource source, string field, string identity) where T : struct, IProjectIdentity<T> =>
        source.Data.Value.GetProperty(field).EnumerateArray().ToDictionary(item => SourceFields.Identity<T>(item, identity),
            item => SourceJson.FromElement(item, source.Data.Limits));
}

/// <summary>Caller-materialized checkpoint/delta carrier. Its metadata and supplied body confer no historical access.</summary>
public sealed class ProjectHistoryRevision
{
    public RevisionRef Revision { get; }
    public long BranchSequence { get; }
    public ContentDigest ExpectedSourceDigest { get; }
    public IReadOnlyList<RevisionRef> Parents { get; }
    public SourceJson? CheckpointData { get; }
    public ProjectSourceDelta? Delta { get; }
    internal int RetainedCanonicalBytes => ProjectSourceDelta.Bytes(CheckpointData) + (Delta?.RetainedCanonicalBytes ?? 0);

    private ProjectHistoryRevision(RevisionRef revision, long branchSequence, ContentDigest digest, IEnumerable<RevisionRef> parents,
        SourceJson? checkpoint, ProjectSourceDelta? delta, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revision); ArgumentNullException.ThrowIfNull(digest); ArgumentNullException.ThrowIfNull(parents);
        if (branchSequence < 0) throw new ArgumentOutOfRangeException(nameof(branchSequence));
        Revision = revision; BranchSequence = branchSequence; ExpectedSourceDigest = digest;
        // Bound even hostile enumerators before publishing an immutable carrier.
        var supplied = new List<RevisionRef>();
        using (var iterator = parents.GetEnumerator())
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested(); if (!iterator.MoveNext()) break; cancellationToken.ThrowIfCancellationRequested();
                if (iterator.Current is null || supplied.Count == 8) throw new ArgumentException("Parent roster exceeds the local bound.", nameof(parents));
                supplied.Add(iterator.Current);
            }
        Parents = new ImmutableListView<RevisionRef>(supplied); CheckpointData = checkpoint; Delta = delta;
    }

    public static ProjectHistoryRevision Checkpoint(ProjectSource source, long branchSequence, IEnumerable<RevisionRef>? parents = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(source.Context.CommittedRevision ?? throw new ArgumentException("Only committed Source can be a history checkpoint.", nameof(source)),
            branchSequence, source.Digest, parents ?? [], source.Data, null, cancellationToken);
    }
    /// <summary>Materialize a supplied checkpoint body; Read verifies schema, integrity and exact context before exposing Source.</summary>
    public static ProjectHistoryRevision MaterializedCheckpoint(RevisionRef revision, long branchSequence, ContentDigest expectedDigest,
        SourceJson body, IEnumerable<RevisionRef>? parents = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body); return new(revision, branchSequence, expectedDigest, parents ?? [], body, null, cancellationToken);
    }
    public static ProjectHistoryRevision ForwardDelta(ProjectSourceDelta delta, long branchSequence)
    {
        ArgumentNullException.ThrowIfNull(delta); return new(delta.AfterRevision, branchSequence, delta.AfterDigest, [delta.BeforeRevision], null, delta);
    }
    public static ProjectHistoryRevision Unavailable(RevisionRef revision, long branchSequence, ContentDigest expectedDigest) =>
        new(revision, branchSequence, expectedDigest, [], null, null);
}

public sealed class HistoryReconstruction
{
    public HistoryReadState State { get; }
    public ProjectSource? Source { get; }
    /// <summary>Structural Source admission only. Even StructurallyDecoded is not semantic/Compiler support.</summary>
    public SourceDecodeStatus? SourceAdmissionStatus { get; }
    /// <summary>Only the actual reconstruction path from an anchor. This is not a complete ancestry graph or authority receipt.</summary>
    public IReadOnlyList<RevisionRef> ReconstructionPath { get; }
    internal HistoryReconstruction(HistoryReadState state, ProjectSource? source = null, IEnumerable<RevisionRef>? path = null,
        SourceDecodeStatus? sourceStatus = null)
    { State = state; Source = source; ReconstructionPath = new ImmutableListView<RevisionRef>(path ?? []); SourceAdmissionStatus = sourceStatus; }
}

/// <summary>Bounded pure reconstruction of already-authorized inputs. No storage mutation, retention policy or execution.</summary>
public static class ProjectSourceHistory
{
    public const int MaximumStoredBytes = 8_388_608;
    public const int MaximumRecords = 256;
    public const int MaximumReconstructionDepth = 32;
    public const int MaximumJsonNodes = 100_000;

    public static HistoryReconstruction Read(RevisionRef target, IEnumerable<ProjectHistoryRevision?> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(records); cancellationToken.ThrowIfCancellationRequested();
        var index = new Dictionary<RevisionRef, ProjectHistoryRevision>(); var storedBytes = 0; var remainingNodes = MaximumJsonNodes;
        using (var iterator = records.GetEnumerator())
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested(); if (!iterator.MoveNext()) break; cancellationToken.ThrowIfCancellationRequested();
                var record = iterator.Current;
                if (record is null) return new(HistoryReadState.Corrupt);
                if (record.Revision.Branch.TenantId != target.Branch.TenantId || record.Revision.Branch.ProjectId != target.Branch.ProjectId)
                    return new(HistoryReadState.OutOfScope);
                if (index.Count == MaximumRecords || record.RetainedCanonicalBytes > MaximumStoredBytes - storedBytes)
                    return new(HistoryReadState.ResourceLimit);
                storedBytes += record.RetainedCanonicalBytes;
                if (record.Delta is { } suppliedDelta)
                {
                    remainingNodes -= suppliedDelta.ProcessingNodeCharge;
                    if (remainingNodes < 0) return new(HistoryReadState.ResourceLimit);
                }
                if (record.CheckpointData is { } suppliedBody &&
                    (ProjectSourceDelta.Bytes(suppliedBody) > 4_194_304 || !WithinJsonBounds(suppliedBody.Value, 0, ref remainingNodes, cancellationToken)))
                    return new(HistoryReadState.ResourceLimit);
                if (!index.TryAdd(record.Revision, record) || record.Parents.Distinct().Count() != record.Parents.Count || record.Parents.Contains(record.Revision))
                    return new(HistoryReadState.Corrupt);
                if (record.Parents.Any(parent => parent.Branch.TenantId != target.Branch.TenantId || parent.Branch.ProjectId != target.Branch.ProjectId))
                    return new(HistoryReadState.OutOfScope);
            }
        }
        // Validate known parent metadata, including cross-Branch cycles. Missing parents of a checkpoint
        // may have been compacted; reconstruction does not pretend to recover that older ancestry.
        var incoming = index.ToDictionary(item => item.Key, _ => 0);
        var children = index.Keys.ToDictionary(revision => revision, _ => new List<RevisionRef>());
        foreach (var record in index.Values)
            foreach (var parent in record.Parents)
            {
                cancellationToken.ThrowIfCancellationRequested(); if (!index.TryGetValue(parent, out var parentRecord)) continue;
                if (parent.Branch == record.Revision.Branch && parentRecord.BranchSequence >= record.BranchSequence) return new(HistoryReadState.Corrupt);
                incoming[record.Revision]++; children[parent].Add(record.Revision);
            }
        var ready = new Queue<RevisionRef>(incoming.Where(item => item.Value == 0).Select(item => item.Key)); var visited = 0;
        while (ready.TryDequeue(out var revision))
        {
            cancellationToken.ThrowIfCancellationRequested(); visited++;
            foreach (var child in children[revision]) if (--incoming[child] == 0) ready.Enqueue(child);
        }
        if (visited != index.Count) return new(HistoryReadState.Corrupt);
        var active = new HashSet<RevisionRef>(); var path = new List<RevisionRef>();
        try { return Reconstruct(target, 0); }
        catch (Exception error) when (error is FormatException or JsonException or InvalidOperationException or ArgumentException)
        { return new(HistoryReadState.Corrupt); }

        HistoryReconstruction Reconstruct(RevisionRef revision, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (depth >= MaximumReconstructionDepth) return new(HistoryReadState.ResourceLimit);
            if (!index.TryGetValue(revision, out var record)) return new(HistoryReadState.Unavailable);
            if (!active.Add(revision)) return new(HistoryReadState.Corrupt);
            foreach (var parent in record.Parents)
                if (parent.Branch == revision.Branch && index.TryGetValue(parent, out var parentRecord) && parentRecord.BranchSequence >= record.BranchSequence)
                    return new(HistoryReadState.Corrupt);
            ProjectSource? source; SourceDecodeStatus sourceStatus;
            if (record.CheckpointData is { } checkpoint)
            {
                var decoded = ProjectSourceCodec.Decode(checkpoint.ToCanonicalBytes(), cancellationToken: cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                // Source admission may preserve opaque members with Unsupported status and a lossless Source body.
                // That body is reconstructible; this does not grant it semantic/Compiler support.
                if (decoded.Source is not { } admitted)
                    return new(decoded.Status == SourceDecodeStatus.Unsupported ? HistoryReadState.Unsupported : HistoryReadState.Corrupt);
                source = admitted;
                sourceStatus = decoded.Status;
            }
            else if (record.Delta is { } delta)
            {
                if (delta.AfterRevision != record.Revision || record.Parents.Count != 1 || record.Parents[0] != delta.BeforeRevision)
                    return new(HistoryReadState.Corrupt);
                var previous = Reconstruct(delta.BeforeRevision, depth + 1);
                if (previous.State != HistoryReadState.Ready) return previous;
                var applied = delta.Apply(previous.Source!, cancellationToken);
                if (applied?.Source is null) return new(HistoryReadState.Corrupt);
                source = applied.Source; sourceStatus = applied.Status;
            }
            else return new(HistoryReadState.Unavailable);
            if (source?.Context.CommittedRevision != revision || !source.Digest.Equals(record.ExpectedSourceDigest)) return new(HistoryReadState.Corrupt);
            path.Add(revision); active.Remove(revision);
            return new(HistoryReadState.Ready, source, path, sourceStatus);
        }
    }

    internal static bool WithinJsonBounds(JsonElement value, int depth, ref int remaining, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (depth > 63 || --remaining < 0 || value.ValueKind == JsonValueKind.Number && value.GetRawText().Length > 100_000) return false;
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var member in value.EnumerateObject()) { if (!WithinJsonBounds(member.Value, depth + 1, ref remaining, cancellationToken)) return false; }
        if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray()) { if (!WithinJsonBounds(child, depth + 1, ref remaining, cancellationToken)) return false; }
        return true;
    }
}
