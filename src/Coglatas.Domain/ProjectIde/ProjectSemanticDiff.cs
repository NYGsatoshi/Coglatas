using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Coglatas.Domain.ProjectIde;

public enum SemanticDiffState { Complete, Incomplete, Unavailable, StaleInput, OutOfScope, ResourceLimit }
public enum SemanticChangeKind { Added, Removed, DisplayName, Property, Reference, Uninterpreted }

/// <summary>Before/after are original Source values. Null means absent, distinct from a JSON null value.</summary>
public sealed record SemanticChange(string RecordType, string Identity, string Path, SemanticChangeKind Kind,
    SourceJson? Before, SourceJson? After, ProjectLocation? BeforeLocation, ProjectLocation? AfterLocation);

/// <summary>Derived local comparison only; neither historical access nor impact/execution evidence.</summary>
public sealed class ProjectSemanticDiff
{
    public SemanticDiffState State { get; }
    public ContentDigest? BeforeInputDigest { get; }
    public ContentDigest? AfterInputDigest { get; }
    public SourceRevisionContext? BeforeContext { get; }
    public SourceRevisionContext? AfterContext { get; }
    public IReadOnlyList<SemanticChange> Changes { get; }
    public bool NoChanges => State == SemanticDiffState.Complete && Changes.Count == 0;
    /// <summary>Offline synthetic projection; not a registered history artifact or trusted server result.</summary>
    public SourceJson Data => SourceJson.FromObject(new
    {
        format = "coglatas.synthetic-semantic-diff/1", state = State.ToString(), noChanges = NoChanges,
        beforeInputDigest = BeforeInputDigest?.Data.Value, afterInputDigest = AfterInputDigest?.Data.Value,
        beforeContext = BeforeContext?.Data.Value, afterContext = AfterContext?.Data.Value,
        changes = Changes.Select(change => new
        {
            recordType = change.RecordType, identity = change.Identity, path = change.Path, kind = change.Kind.ToString(),
            beforePresent = change.Before is not null, afterPresent = change.After is not null,
            before = change.Before?.Value, after = change.After?.Value,
            beforeLocation = change.BeforeLocation?.Data.Value, afterLocation = change.AfterLocation?.Data.Value
        }).ToArray()
    });

    private ProjectSemanticDiff(SemanticDiffState state, IEnumerable<SemanticChange> changes,
        PartialSemanticModel? before = null, PartialSemanticModel? after = null)
    {
        State = state; Changes = new ImmutableListView<SemanticChange>(changes);
        BeforeInputDigest = before?.Input.InputDigest; AfterInputDigest = after?.Input.InputDigest;
        BeforeContext = before?.Input.Source.Context; AfterContext = after?.Input.Source.Context;
    }

    /// <summary>
    /// Inputs must already be authorized/materialized by the caller. Exact expected inputs prevent stale pair reuse.
    /// Limits are local technical ceilings, not a retention policy or permission to load history.
    /// </summary>
    public static ProjectSemanticDiff Compare(PartialSemanticModel? before, PartialSemanticModel? after,
        ContentDigest expectedBefore, ContentDigest expectedAfter, int maximumChanges = 256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedBefore); ArgumentNullException.ThrowIfNull(expectedAfter);
        if (maximumChanges is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(maximumChanges));
        cancellationToken.ThrowIfCancellationRequested();
        if (before is null || after is null) return new(SemanticDiffState.Unavailable, []);
        var left = before.Input.Source; var right = after.Input.Source;
        if (left.TenantId != right.TenantId || left.ProjectId != right.ProjectId)
            return new(SemanticDiffState.OutOfScope, []);
        if (!before.Input.InputDigest.Equals(expectedBefore) || !after.Input.InputDigest.Equals(expectedAfter))
            return new(SemanticDiffState.StaleInput, []);
        // Bound both parsing work and returned data. No truncated result can claim equality.
        if (Encoding.UTF8.GetByteCount(left.Data.CanonicalText) + Encoding.UTF8.GetByteCount(right.Data.CanonicalText) > 2_097_152 ||
            before.Declarations.Count + after.Declarations.Count + before.Relations.Count + after.Relations.Count > 8192)
            return new(SemanticDiffState.ResourceLimit, []);
        var remainingNodes = 100_000;
        if (!WithinProjectionBounds(left.Data.Value, 0) || !WithinProjectionBounds(right.Data.Value, 0))
            return new(SemanticDiffState.ResourceLimit, []);
        var changes = new List<SemanticChange>(); var outputBytes = 0;
        // One immutable version cannot supply two different bodies; preserve useful rows without complete credit.
        var complete = before.BindingComplete && after.BindingComplete &&
            (!left.Context.Equals(right.Context) || left.Digest.Equals(right.Digest));
        try
        {
            CompareRecords(Declarations(before), Declarations(after));
            CompareRecords(Relations(before), Relations(after));
            var documentsBefore = left.Documents.ToDictionary(item => item.DocumentId);
            var documentsAfter = right.Documents.ToDictionary(item => item.DocumentId);
            foreach (var id in documentsBefore.Keys.Union(documentsAfter.Keys).OrderBy(id => id.ToString(), StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                documentsBefore.TryGetValue(id, out var oldDocument); documentsAfter.TryGetValue(id, out var newDocument);
                var oldValue = oldDocument is null ? null : SourceJson.FromObject(oldDocument.LogicalName);
                var newValue = newDocument is null ? null : SourceJson.FromObject(newDocument.LogicalName);
                if (!Equals(oldValue, newValue)) Add(new("document", id.ToString(), "/logicalName",
                    oldDocument is null ? SemanticChangeKind.Added : newDocument is null ? SemanticChangeKind.Removed : SemanticChangeKind.Property,
                    oldValue, newValue, oldDocument is null ? null : ProjectLocation.Create(id, left.Context),
                    newDocument is null ? null : ProjectLocation.Create(id, right.Context)));
                var oldData = oldDocument?.Data; var newData = newDocument?.Data;
                foreach (var field in UnknownFields(oldData, newData, ["documentId", "logicalName", "schemaId", "schemaVersion", "root", "digest"]))
                {
                    Opaque("document", id.ToString(), "/" + Escape(field), Member(oldData, field), Member(newData, field),
                        oldDocument is null ? null : ProjectLocation.Create(id, left.Context), newDocument is null ? null : ProjectLocation.Create(id, right.Context));
                }
                var oldDigest = Member(oldData, "digest"); var newDigest = Member(newData, "digest");
                if (!Equals(OpaqueDigest(oldDigest), OpaqueDigest(newDigest)))
                    Opaque("document", id.ToString(), "/digest", oldDigest, newDigest,
                        oldDocument is null ? null : ProjectLocation.Create(id, left.Context), newDocument is null ? null : ProjectLocation.Create(id, right.Context));
            }
            // Opaque/package data is retained as uninterpreted; never infer layout, impact or absence of changes from it.
            foreach (var field in UnknownFields(left.Data, right.Data,
                ["format", "schemaId", "schemaVersion", "canonicalEncodingVersion", "tenantId", "projectId", "context", "documents", "manifest", "declarations", "relations"]))
            {
                Opaque("source", "", "/" + Escape(field), Member(left.Data, field), Member(right.Data, field), null, null);
            }
            if (!Equals(OpaqueManifest(left.Manifest.Data), OpaqueManifest(right.Manifest.Data)))
                Opaque("source", "", "/manifest", left.Manifest.Data, right.Manifest.Data, null, null);
            cancellationToken.ThrowIfCancellationRequested();
            var comparison = new ProjectSemanticDiff(complete ? SemanticDiffState.Complete : SemanticDiffState.Incomplete, changes, before, after);
            // Keep returned projections inside the existing browser reader profile,
            // including paths, locations and repeated context metadata, not just values.
            try
            {
                if (Encoding.UTF8.GetByteCount(comparison.Data.CanonicalText) > 600_000) throw new DiffLimitException();
            }
            catch (FormatException) { throw new DiffLimitException(); }
            cancellationToken.ThrowIfCancellationRequested();
            return comparison;
        }
        catch (DiffLimitException) { return new(SemanticDiffState.ResourceLimit, []); }

        bool WithinProjectionBounds(JsonElement value, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Leave depth headroom for original values inside the result envelope and reject oversized numeric lexemes.
            if (depth > 48 || --remainingNodes < 0 || value.ValueKind == JsonValueKind.Number && value.GetRawText().Length > 100_000) return false;
            if (value.ValueKind == JsonValueKind.Object)
                foreach (var member in value.EnumerateObject()) { if (!WithinProjectionBounds(member.Value, depth + 1)) return false; }
            if (value.ValueKind == JsonValueKind.Array)
                foreach (var child in value.EnumerateArray()) { if (!WithinProjectionBounds(child, depth + 1)) return false; }
            return true;
        }

        void Add(SemanticChange change)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (change.Path.Length > 4096) throw new DiffLimitException();
            outputBytes += Encoding.UTF8.GetByteCount(change.Before?.CanonicalText ?? "") + Encoding.UTF8.GetByteCount(change.After?.CanonicalText ?? "");
            if (changes.Count >= maximumChanges || outputBytes > 262_144) throw new DiffLimitException();
            changes.Add(change);
        }
        void Opaque(string type, string id, string path, SourceJson? oldValue, SourceJson? newValue, ProjectLocation? oldLocation, ProjectLocation? newLocation)
        {
            if (Equals(oldValue, newValue)) return;
            complete = false;
            Add(new(type, id, path, SemanticChangeKind.Uninterpreted, oldValue, newValue, oldLocation, newLocation));
        }
        SourceJson? Member(SourceJson? data, string field) => data is not null && data.Value.TryGetProperty(field, out var value)
            ? SourceJson.FromElement(value, data.Limits) : null;
        IEnumerable<string> UnknownFields(SourceJson? oldData, SourceJson? newData, string[] known) =>
            (oldData?.Value.EnumerateObject().Select(member => member.Name) ?? [])
                .Union(newData?.Value.EnumerateObject().Select(member => member.Name) ?? [], StringComparer.Ordinal)
                .Except(known, StringComparer.Ordinal).OrderBy(field => field, StringComparer.Ordinal);
        SourceJson? OpaqueDigest(SourceJson? digest)
        {
            if (digest is null) return null;
            var opaque = JsonNode.Parse(digest.CanonicalText)!.AsObject(); opaque.Remove("algorithm"); opaque.Remove("value");
            return opaque.Count == 0 ? null : SourceJson.FromObject(opaque);
        }
        SourceJson OpaqueManifest(SourceJson manifest)
        {
            var opaque = JsonNode.Parse(manifest.CanonicalText)!.AsObject(); var entries = opaque["documents"]!.AsArray();
            var projected = new JsonObject();
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = entry!.AsObject(); var id = item["documentId"]!.GetValue<string>();
                item.Remove("documentId"); item.Remove("schemaId"); item.Remove("schemaVersion");
                var digest = OpaqueDigest(SourceJson.FromObject(item["digest"]!)); item.Remove("digest");
                if (digest is not null) item["digest"] = JsonNode.Parse(digest.CanonicalText);
                if (item.Count > 0) projected[id] = item.DeepClone();
            }
            opaque["documents"] = projected;
            return SourceJson.FromObject(opaque);
        }
        void CompareRecords(Dictionary<string, DiffRecord> oldRecords, Dictionary<string, DiffRecord> newRecords)
        {
            foreach (var id in oldRecords.Keys.Union(newRecords.Keys).OrderBy(id => id, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                oldRecords.TryGetValue(id, out var oldRecord); newRecords.TryGetValue(id, out var newRecord);
                var record = oldRecord ?? newRecord!;
                if (oldRecord is null || newRecord is null)
                {
                    if (!record.Supported) complete = false;
                    Add(new(record.Type, id, "", oldRecord is null ? SemanticChangeKind.Added : SemanticChangeKind.Removed,
                        oldRecord?.Data, newRecord?.Data, oldRecord?.Location, newRecord?.Location));
                    continue;
                }
                if (!oldRecord.Supported || !newRecord.Supported)
                {
                    complete = false;
                    if (!oldRecord.Data.Equals(newRecord.Data)) Add(new(record.Type, id, "", SemanticChangeKind.Uninterpreted,
                        oldRecord.Data, newRecord.Data, oldRecord.Location, newRecord.Location));
                    continue;
                }
                if (oldRecord.Location.DocumentId != newRecord.Location.DocumentId)
                    Add(new(record.Type, id, "/documentId", SemanticChangeKind.Property,
                        SourceJson.FromObject(oldRecord.Location.DocumentId.ToString()), SourceJson.FromObject(newRecord.Location.DocumentId.ToString()),
                        oldRecord.Location, newRecord.Location));
                Walk(oldRecord.Projection!, newRecord.Projection!, oldRecord.Data.Value, newRecord.Data.Value, "");
                void Walk(JsonNode? oldNode, JsonNode? newNode, JsonElement oldRaw, JsonElement newRaw, string path)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if ((oldRaw.ValueKind == JsonValueKind.Undefined) == (newRaw.ValueKind == JsonValueKind.Undefined) &&
                        JsonNode.DeepEquals(oldNode, newNode)) return;
                    if (oldNode is JsonObject oldObject && newNode is JsonObject newObject)
                    {
                        foreach (var key in oldObject.Select(pair => pair.Key).Union(newObject.Select(pair => pair.Key)).OrderBy(key => key, StringComparer.Ordinal))
                        {
                            if (path.Length == 0 && key == "documentId") continue; // Compared through the admitted owning document above.
                            oldRaw.TryGetProperty(key, out var oldChild); newRaw.TryGetProperty(key, out var newChild);
                            Walk(oldObject[key], newObject[key], oldChild, newChild, path + "/" + Escape(key));
                        }
                        return;
                    }
                    var reference = oldRecord.ReferencePaths.Concat(newRecord.ReferencePaths)
                        .Any(prefix => path == prefix || path.StartsWith(prefix + "/", StringComparison.Ordinal) || prefix.StartsWith(path + "/", StringComparison.Ordinal));
                    Add(new(record.Type, id, path, path == "/displayName" ? SemanticChangeKind.DisplayName :
                        reference ? SemanticChangeKind.Reference : SemanticChangeKind.Property,
                        oldRaw.ValueKind == JsonValueKind.Undefined ? null : SourceJson.FromElement(oldRaw, oldRecord.Data.Limits),
                        newRaw.ValueKind == JsonValueKind.Undefined ? null : SourceJson.FromElement(newRaw, newRecord.Data.Limits),
                        oldRecord.Location, newRecord.Location));
                }
            }
        }
        Dictionary<string, DiffRecord> Declarations(PartialSemanticModel model)
        {
            var result = new Dictionary<string, DiffRecord>(StringComparer.Ordinal);
            foreach (var item in model.Declarations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = SourceFields.Identity<EntityId>(item.Data.Value, "entityId");
                var record = new DiffRecord("entity", item.Data, ProjectLocation.Create(item.DocumentId, model.Input.Source.Context, entityId: id), item.Declaration is not null);
                if (item.Declaration is { } declaration)
                {
                    if (declaration is ProjectPlanDeclaration) record.ReferencePaths.Add("/payload/roots");
                    if (declaration is RequirementPlanDeclaration requirement) Reference(record, "/payload/target", requirement.Target, model.Input.Source.Context);
                    if (declaration is TaskPlanDeclaration task)
                        for (var index = 0; index < task.Ports.Count; index++)
                            if (task.Ports[index].DefaultValue is { } value)
                                Value(record, record.Projection!["payload"]!["ports"]![index]!["defaultValue"]!.AsObject(), value,
                                    "/payload/ports/" + index + "/defaultValue", model.Input.Source.Context);
                }
                result.Add(id.ToString(), record);
            }
            return result;
        }
        Dictionary<string, DiffRecord> Relations(PartialSemanticModel model)
        {
            var result = new Dictionary<string, DiffRecord>(StringComparer.Ordinal);
            foreach (var item in model.Relations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = SourceFields.Identity<RelationId>(item.Data.Value, "relationId");
                var document = SourceFields.Identity<DocumentId>(item.Data.Value, "documentId");
                var record = new DiffRecord("relation", item.Data, ProjectLocation.Create(document, model.Input.Source.Context, relationId: id), item.Relation is not null);
                if (item.Relation is { } relation)
                {
                    record.ReferencePaths.Add("/source"); record.ReferencePaths.Add("/target");
                    Reference(record, "/source/reference", relation.Source.Reference, model.Input.Source.Context);
                    Reference(record, "/target/reference", relation.Target.Reference, model.Input.Source.Context);
                    if (relation.Payload is ConstraintRelationPayload constraint) Reference(record, "/payload/predicateReference", constraint.PredicateReference, model.Input.Source.Context);
                }
                result.Add(id.ToString(), record);
            }
            return result;
        }
        void Reference(DiffRecord record, string path, PlanEntityReference reference, SourceRevisionContext context)
        {
            record.ReferencePaths.Add(path);
            if (!reference.Context.Equals(context)) return;
            var node = record.Projection!;
            foreach (var part in path.Split('/').Skip(1)) node = node[part]!;
            // Internal tagged comparison value; never recontextualize to the other side's actual context.
            node["context"] = "coglatas.diff.local-context";
        }
        void Value(DiffRecord record, JsonObject node, TypedProjectValue value, string path, SourceRevisionContext context)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (value.State != ValueState.Known) return; // Unknown basis remains opaque.
            switch (value.KnownValue)
            {
                case TypedProjectReference reference:
                    record.ReferencePaths.Add(path);
                    // Value targets are pinned even when they happen to name the containing revision.
                    if (reference.Origin.Context.Equals(context)) node["value"]!["origin"]!["context"] = "coglatas.diff.local-context";
                    break;
                case TypedProjectValue child: Value(record, node["value"]!.AsObject(), child, path + "/value", context); break;
                case OptionalProjectValue { Present: true, Value: { } child }:
                    Value(record, node["value"]!["value"]!.AsObject(), child, path + "/value/value", context); break;
                case ProjectStructureValue structure:
                    foreach (var field in structure.Fields) Value(record, node["value"]![field.Key]!.AsObject(), field.Value, path + "/value/" + Escape(field.Key), context);
                    break;
                case ProjectCollectionValue collection:
                    var array = node["value"]!.AsArray();
                    for (var i = 0; i < collection.Items.Count; i++) Value(record, array[i]!.AsObject(), collection.Items[i], path + "/value/" + i, context);
                    for (var i = 0; i < collection.Entries.Count; i++) Value(record, array[i]!["value"]!.AsObject(), collection.Entries[i].Value, path + "/value/" + i + "/value", context);
                    if (value.Type.Kind == "coglatas.set")
                    {
                        var sorted = array.Select(item => item!).OrderBy(item => SourceJson.FromObject(item).CanonicalText, StringComparer.Ordinal).ToArray();
                        array.Clear(); foreach (var item in sorted) array.Add(item);
                    }
                    break;
            }
        }
    }

    private static string Escape(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    private sealed class DiffLimitException : Exception { }
    private sealed class DiffRecord(string type, SourceJson data, ProjectLocation location, bool supported)
    {
        internal string Type { get; } = type;
        internal SourceJson Data { get; } = data;
        internal ProjectLocation Location { get; } = location;
        internal bool Supported { get; } = supported;
        internal JsonNode? Projection { get; } = supported ? JsonNode.Parse(data.CanonicalText) : null;
        internal List<string> ReferencePaths { get; } = [];
    }
}
