using System.Text;
using System.Text.Json;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

/// <summary>Offline synthetic fixture transport, never current authorization or a runtime acquisition adapter. Denied bodies are not exported.</summary>
public static class SyntheticSnapshotMaterialization
{
    public const string Format = "coglatas.local-snapshot-materialization/1";
    public static SourceJson Export(CompilerInputManifest input, IEnumerable<CompilerMaterializedSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(snapshots);
        var entries = new List<object>();
        foreach (var item in snapshots)
        {
            if (entries.Count == 32) throw new FormatException("Synthetic snapshot count exceeds the bound.");
            entries.Add(item.Snapshot.IsAuthorized ? new { snapshotId = item.SnapshotId.ToString(), version = item.Version,
                context = item.Snapshot.Source.Context.Data.Value, admission = "Available", source = new { text = item.Snapshot.Source.Data.CanonicalText,
                    digest = item.Snapshot.Source.Digest.Data.Value }, declarationsComplete = item.Snapshot.DeclarationsComplete, relationsComplete = item.Snapshot.RelationsComplete } :
                new { snapshotId = item.SnapshotId.ToString(), version = item.Version, context = item.Snapshot.Source.Context.Data.Value, admission = "Unavailable" });
        }
        var data = SourceJson.Parse(JsonSerializer.Serialize(new { format = Format, inputDigest = input.InputDigest.Data.Value, entries }, new JsonSerializerOptions { MaxDepth = 256 }));
        _ = Read(input, data); return data;
    }
    public static IReadOnlyList<CompilerMaterializedSnapshot> Read(CompilerInputManifest input, SourceJson data)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(data); var root = data.Value;
        Fields(root, "format", "inputDigest", "entries");
        if (root.GetProperty("format").GetString() != Format || !Json(root.GetProperty("inputDigest")).Equals(input.InputDigest.Data))
            throw new FormatException("Synthetic snapshot input binding mismatch.");
        var entries = root.GetProperty("entries"); if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > 32) throw new FormatException("Synthetic snapshot count exceeds the bound.");
        var pins = input.Settings.Value.GetProperty("authorizedExternalSnapshots").EnumerateArray().ToArray();
        var result = new List<CompilerMaterializedSnapshot>(); var seen = new HashSet<ExternalSnapshotId>(); long bytes = 0;
        foreach (var entry in entries.EnumerateArray())
        {
            var available = entry.GetProperty("admission").GetString() == "Available";
            if (available) Fields(entry, "snapshotId", "version", "context", "admission", "source", "declarationsComplete", "relationsComplete");
            else { Fields(entry, "snapshotId", "version", "context", "admission"); if (entry.GetProperty("admission").GetString() != "Unavailable") throw new FormatException("Unknown snapshot admission."); }
            var id = ExternalSnapshotId.Parse(entry.GetProperty("snapshotId").GetString()!); if (!seen.Add(id)) throw new FormatException("Duplicate synthetic snapshot.");
            var matches = pins.Where(pin => pin.GetProperty("snapshotId").GetString() == id.ToString()).ToArray();
            if (matches.Length != 1) throw new FormatException("Synthetic snapshot requires an exact input pin."); var pin = matches[0];
            var version = entry.GetProperty("version").GetString()!;
            var context = SourceRevisionContext.Parse(Json(entry.GetProperty("context")));
            if (version != pin.GetProperty("version").GetString() || !context.Data.Equals(Json(pin.GetProperty("context")))) throw new FormatException("Synthetic snapshot pin mismatch.");
            ProjectSource source; bool declarations = false, relations = false;
            if (available)
            {
                var encoded = entry.GetProperty("source"); Fields(encoded, "text", "digest"); var text = encoded.GetProperty("text").GetString()!;
                bytes += Encoding.UTF8.GetByteCount(text); if (bytes > 4_194_304) throw new FormatException("Synthetic snapshot bodies exceed the bound.");
                source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(text)).RequireSource();
                if (source.Data.CanonicalText != text || !source.Context.Equals(context) || !source.Digest.Data.Equals(Json(pin.GetProperty("digest"))) || !source.Digest.Data.Equals(Json(encoded.GetProperty("digest"))))
                    throw new FormatException("Synthetic snapshot Source mismatch.");
                declarations = entry.GetProperty("declarationsComplete").GetBoolean(); relations = entry.GetProperty("relationsComplete").GetBoolean();
            }
            // The binder does not inspect a denied body. Reconstruct only a harmless context carrier, with no denied Source bytes in the artifact.
            else source = ProjectSource.Create(context, input.Source.Documents.Take(1));
            result.Add(new(id, version, new(source, available, declarations, relations)));
        }
        return result.AsReadOnly();
    }
    private static SourceJson Json(JsonElement value) => SourceJson.Parse(value.GetRawText());
    private static void Fields(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != names.Length || names.Any(name => !value.TryGetProperty(name, out _)))
            throw new FormatException("Unsupported synthetic snapshot fields.");
    }
}
