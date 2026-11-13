using System.Text.Json.Nodes;

namespace Coglatas.Domain.ProjectIde;

public enum DraftRevertState { Ready, NoChange, Conflict, StaleInput, OutOfScope, InvalidContext, ContextConflict, InvalidSource, Unavailable, Unsupported, ResourceLimit }
public sealed record DraftRevertConflict(string RecordType, string Identity);
public sealed record DraftRevertPreview(DraftRevertState State, ProjectDraftRevertPlan? Plan, IReadOnlyList<DraftRevertConflict> Conflicts);

/// <summary>Whole local inverse Draft preparation, never a write, review, authorization or execution grant.</summary>
public sealed class ProjectDraftRevertPlan
{
    public CompilerInputManifest Before { get; }
    public CompilerInputManifest After { get; }
    public RevisionRef RevertedRevision { get; }
    public RevisionRef RevertedParent { get; }
    public SourceJson Data { get; }
    internal ProjectDraftRevertPlan(CompilerInputManifest before, CompilerInputManifest after, CompilerInputManifest reverted, CompilerInputManifest parent, int changedRecords)
    {
        Before = before; After = after; RevertedRevision = reverted.Source.Context.CommittedRevision!; RevertedParent = parent.Source.Context.CommittedRevision!;
        Data = SourceJson.FromObject(new
        {
            currentInputDigest = before.InputDigest.Data.Value, candidateInputDigest = after.InputDigest.Data.Value,
            parentInputDigest = parent.InputDigest.Data.Value, revertedInputDigest = reverted.InputDigest.Data.Value,
            currentContext = before.Source.Context.Data.Value, candidateContext = after.Source.Context.Data.Value,
            parentContext = parent.Source.Context.Data.Value, revertedContext = reverted.Source.Context.Data.Value,
            changedRecordCount = changedRecords
        });
    }
    public CompilerInputManifest? ApplyToDraft(CompilerInputManifest current, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current); cancellationToken.ThrowIfCancellationRequested();
        return current.Data.Equals(Before.Data) && current.Source.Data.Equals(Before.Source.Data) ? After : null;
    }
}

/// <summary>
/// Conservative single-parent, same-Branch inverse. Overlapping record changes conflict atomically.
/// Only already-authorized materializations may be supplied. Semantic checks and governed application remain separate.
/// </summary>
public static class ProjectDraftRevert
{
    public const int MaximumRecordChanges = 4096;

    public static DraftRevertPreview Preview(PartialSemanticModel before, PartialSemanticModel after, PartialSemanticModel current,
        ContentDigest expectedBefore, ContentDigest expectedAfter, ContentDigest expectedCurrent, ProposalContext proposal,
        IEnumerable<ProjectHistoryRevision?> history, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(before); ArgumentNullException.ThrowIfNull(after); ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(expectedBefore); ArgumentNullException.ThrowIfNull(expectedAfter); ArgumentNullException.ThrowIfNull(expectedCurrent);
        ArgumentNullException.ThrowIfNull(proposal); ArgumentNullException.ThrowIfNull(history); cancellationToken.ThrowIfCancellationRequested();
        DraftRevertPreview Refuse(DraftRevertState state) => new(state, null, new ImmutableListView<DraftRevertConflict>([]));
        if (!before.Input.InputDigest.Equals(expectedBefore) || !after.Input.InputDigest.Equals(expectedAfter) || !current.Input.InputDigest.Equals(expectedCurrent))
            return Refuse(DraftRevertState.StaleInput);
        var old = before.Input.Source; var changed = after.Input.Source; var now = current.Input.Source;
        if (old.Context.Branch != changed.Context.Branch || now.Context.Branch != changed.Context.Branch)
            return Refuse(DraftRevertState.OutOfScope);
        if (old.Context.CommittedRevision is not { } parent || changed.Context.CommittedRevision is not { } revision ||
            now.Context.CommittedRevision is not { } head || parent == revision || head == parent ||
            proposal.BaseRevision != head || proposal.CapturedTargetHead != head)
            return Refuse(DraftRevertState.InvalidContext);
        var next = SourceRevisionContext.Candidate(proposal);
        foreach (var model in new[] { before, after, current })
        {
            if (ProjectDraftRename.ContextAlreadyKnown(model, next, cancellationToken)) return Refuse(DraftRevertState.ContextConflict);
            if (model.Declarations.Count != model.Input.Source.Declarations.Value.GetArrayLength() + model.Input.Source.Documents.Count ||
                model.Relations.Count != model.Input.Source.Relations.Value.GetArrayLength()) return Refuse(DraftRevertState.Unavailable);
        }
        var supplied = new List<ProjectHistoryRevision?>();
        using (var iterator = history.GetEnumerator())
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested(); if (!iterator.MoveNext()) break; cancellationToken.ThrowIfCancellationRequested();
                if (supplied.Count == ProjectSourceHistory.MaximumRecords) return Refuse(DraftRevertState.ResourceLimit);
                supplied.Add(iterator.Current);
            }
        // Reconstruct all three bindings; an ancestry label cannot substitute for exact supplied bodies.
        foreach (var source in new[] { old, changed, now })
        {
            var read = ProjectSourceHistory.Read(source.Context.CommittedRevision!, supplied, cancellationToken);
            if (read.State == HistoryReadState.ResourceLimit) return Refuse(DraftRevertState.ResourceLimit);
            if (read.State == HistoryReadState.OutOfScope) return Refuse(DraftRevertState.OutOfScope);
            if (read.State != HistoryReadState.Ready || !source.Data.Equals(read.Source!.Data)) return Refuse(DraftRevertState.Unavailable);
        }
        var index = supplied.ToDictionary(item => item!.Revision, item => item!);
        if (index[revision].Parents.Count != 1 || index[revision].Parents[0] != parent) return Refuse(DraftRevertState.Unsupported);
        var pending = new Stack<RevisionRef>(); var visited = new HashSet<RevisionRef>(); pending.Push(head);
        while (pending.TryPop(out var item))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(item) || item == revision || !index.TryGetValue(item, out var record)) continue;
            foreach (var ancestor in record.Parents) pending.Push(ancestor);
        }
        if (!visited.Contains(revision)) return Refuse(DraftRevertState.Unavailable);
        // The narrow profile refuses Branch-hopping ancestry, even when another path proves reachability.
        if (visited.Any(item => item.Branch != head.Branch)) return Refuse(DraftRevertState.Unsupported);
        try
        {
            var remainingNodes = ProjectSourceHistory.MaximumJsonNodes; var bytes = 0;
            foreach (var source in new[] { old, changed, now })
            {
                bytes += ProjectSourceDelta.Bytes(source.Data);
                if (bytes > ProjectSourceHistory.MaximumStoredBytes || !ProjectSourceHistory.WithinJsonBounds(source.Data.Value, 0, ref remainingNodes, cancellationToken))
                    return Refuse(DraftRevertState.ResourceLimit);
            }
            // Normalize only decoded local references/origins; pinned targets and opaque data remain untouched.
            var oldRoot = JsonNode.Parse(DraftSourceTransition.Recontextualize(before, next, null, null, cancellationToken).Data.CanonicalText)!.AsObject();
            var afterRoot = JsonNode.Parse(DraftSourceTransition.Recontextualize(after, next, null, null, cancellationToken).Data.CanonicalText)!.AsObject();
            var root = JsonNode.Parse(DraftSourceTransition.Recontextualize(current, next, null, null, cancellationToken).Data.CanonicalText)!.AsObject();
            SourceJson Encode(JsonNode node) => SourceJson.FromObject(node);
            Dictionary<string, JsonNode> Rows(JsonObject node, string field, string identity) => node[field]!.AsArray()
                .ToDictionary(item => item![identity]!.GetValue<string>(), item => item!, StringComparer.Ordinal);
            var oldManifest = oldRoot["manifest"]!.DeepClone().AsObject(); var afterManifest = afterRoot["manifest"]!.DeepClone().AsObject();
            var manifest = root["manifest"]!.AsObject();
            foreach (var version in new[] { oldManifest, afterManifest, manifest })
            {
                foreach (var entry in version["documents"]!.AsArray()) entry!.AsObject().Remove("digest");
            }
            var conflicts = new List<DraftRevertConflict>(); var count = 0; var applied = 0;
            Invert(oldRoot, afterRoot, root, "documents", "documentId");
            Invert(oldRoot, afterRoot, root, "declarations", "entityId");
            Invert(oldRoot, afterRoot, root, "relations", "relationId");
            Invert(oldManifest, afterManifest, manifest, "documents", "documentId", "manifest-document");
            InvertMembers(oldRoot, afterRoot, root, ["context", "documents", "declarations", "relations", "manifest"], "source");
            InvertMembers(oldManifest, afterManifest, manifest, ["documents"], "manifest");
            cancellationToken.ThrowIfCancellationRequested();
            if (count > MaximumRecordChanges) return Refuse(DraftRevertState.ResourceLimit);
            if (conflicts.Count != 0) return new(DraftRevertState.Conflict, null, new ImmutableListView<DraftRevertConflict>(conflicts));
            if (applied == 0) return Refuse(DraftRevertState.NoChange);
            var documents = Rows(root, "documents", "documentId");
            foreach (var entry in root["manifest"]!["documents"]!.AsArray())
                entry!["digest"] = documents[entry["documentId"]!.GetValue<string>()]["digest"]!.DeepClone();
            var decoded = ProjectSourceCodec.Decode(Encode(root).ToCanonicalBytes(), cancellationToken: cancellationToken);
            if (decoded.Source is not { } candidate) return Refuse(decoded.Status == SourceDecodeStatus.Invalid ? DraftRevertState.InvalidSource : DraftRevertState.Unsupported);
            var output = new CompilerInputManifest(candidate, current.Input.Settings); cancellationToken.ThrowIfCancellationRequested();
            return new(DraftRevertState.Ready, new(current.Input, output, after.Input, before.Input, applied), new ImmutableListView<DraftRevertConflict>([]));

            bool Same(JsonNode? left, JsonNode? right) => left is null ? right is null : right is not null && Encode(left).Equals(Encode(right));
            void Invert(JsonObject oldVersion, JsonObject changedVersion, JsonObject currentVersion, string field, string identity, string? category = null)
            {
                var previous = Rows(oldVersion, field, identity); var subsequent = Rows(changedVersion, field, identity); var present = Rows(currentVersion, field, identity);
                foreach (var key in previous.Keys.Union(subsequent.Keys).Order(StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested(); previous.TryGetValue(key, out var oldValue); subsequent.TryGetValue(key, out var newValue);
                    if (Same(oldValue, newValue)) continue;
                    if (++count > MaximumRecordChanges) return;
                    present.TryGetValue(key, out var currentValue);
                    if (Same(currentValue, oldValue)) continue;
                    if (!Same(currentValue, newValue)) { conflicts.Add(new(category ?? field, key)); continue; }
                    applied++; if (oldValue is null) present.Remove(key); else present[key] = oldValue;
                }
                currentVersion[field] = new JsonArray(present.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item.Value.DeepClone()).ToArray());
            }
            void InvertMembers(JsonObject oldVersion, JsonObject changedVersion, JsonObject currentVersion, string[] excluded, string category)
            {
                foreach (var key in oldVersion.Select(item => item.Key).Union(changedVersion.Select(item => item.Key)).Except(excluded).Order(StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var hadOld = oldVersion.TryGetPropertyValue(key, out var oldValue); var hadNew = changedVersion.TryGetPropertyValue(key, out var newValue);
                    if (hadOld == hadNew && Same(oldValue, newValue)) continue;
                    if (++count > MaximumRecordChanges) return;
                    var hasCurrent = currentVersion.TryGetPropertyValue(key, out var currentValue);
                    if (hasCurrent == hadOld && Same(currentValue, oldValue)) continue;
                    if (hasCurrent != hadNew || !Same(currentValue, newValue)) { conflicts.Add(new(category, key)); continue; }
                    applied++; if (!hadOld) currentVersion.Remove(key); else currentVersion[key] = oldValue?.DeepClone();
                }
            }
        }
        catch (DraftSourceLimitException) { return Refuse(DraftRevertState.ResourceLimit); }
        catch (UnsupportedSourceException) { return Refuse(DraftRevertState.Unsupported); }
        catch (FormatException) { return Refuse(DraftRevertState.Unsupported); }
    }
}
