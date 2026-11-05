using System.Globalization;
using System.Text.Json;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

/// <summary>One resolver for Diagnostic and Symbol navigation over the accepted immutable Core objects.</summary>
internal static class SyntheticSourceNavigation
{
    internal static string Resolve(CompilerHostRequest? desired, ProjectSource? source, CoreCompilation? compilation, ProjectLocation location)
    {
        if (desired is null || source is null || compilation?.Model is not { } model) return "unavailable";
        var input = compilation.Input;
        if (!input.InputDigest.Equals(desired.Input.ExpectedInputDigest) || !input.Source.Data.Equals(source.Data) ||
            !compilation.Result.Input.Data.Equals(input.Data) || !model.Input.Data.Equals(input.Data)) return "stale";
        if (!source.Context.Equals(location.Context)) return "wrong-scope";
        if (!source.Manifest.DocumentIds.Contains(location.DocumentId)) return "unavailable";
        if (location.EntityId is not null && location.RelationId is not null) return "invalid-location";
        if (location.EntityId is null && location.RelationId is null) return "unsupported";
        var records = location.EntityId is { } entity
            ? model.Declarations.Where(item => item.DocumentId == location.DocumentId &&
                HasIdentity(item.Data.Value, "entityId", entity.ToString())).Select(item => item.Data.Value).Take(2).ToArray()
            : model.Relations.Where(item => HasIdentity(item.Data.Value, "relationId", location.RelationId!.Value.ToString()) &&
                HasIdentity(item.Data.Value, "documentId", location.DocumentId.ToString())).Select(item => item.Data.Value).Take(2).ToArray();
        if (records.Length != 1) return "unavailable";
        // A partial model may have no IR/map. When produced, use its exact input and artifact binding.
        var mapArtifact = compilation.Result.Artifacts.SingleOrDefault(item => item.Role == "coglatas.source-map");
        if (compilation.Validation?.Ir is { } ir)
        {
            if (!ir.Input.Data.Equals(input.Data) || mapArtifact is null || !mapArtifact.InputDigest.Equals(input.InputDigest) ||
                !mapArtifact.Digest.Equals(ir.SourceMapDigest)) return "wrong-scope";
            var entries = ir.SourceMap.Value.GetProperty(location.EntityId is null ? "relations" : "nodes").EnumerateArray();
            if (entries.Count(entry => SameRecord(ProjectLocation.Parse(SourceJson.Parse(entry.GetProperty("location").GetRawText())), location)) != 1)
                return "unavailable";
        }
        else if (mapArtifact is not null) return "wrong-scope";
        var target = records[0];
        var segments = location.PropertyPath.Length == 0 ? [] : location.PropertyPath[1..].Split('/');
        for (var position = 0; position < segments.Length; position++)
        {
            var segment = segments[position];
            var key = segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            if (target.ValueKind == JsonValueKind.Object && target.TryGetProperty(key, out var property)) target = property;
            else if (target.ValueKind == JsonValueKind.Array && int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index) &&
                index.ToString(CultureInfo.InvariantCulture) == key && index < target.GetArrayLength()) target = target[index];
            else if (position == 2 && segments[0] == "payload" && segments[1] == "ports" && target.ValueKind == JsonValueKind.Array)
            {
                // Core symbol locations identify ports by PortId, rather than an array index.
                var ports = target.EnumerateArray().Where(item => HasIdentity(item, "portId", key)).Take(2).ToArray();
                if (ports.Length != 1) return "invalid-location";
                target = ports[0];
            }
            else return "invalid-location";
        }
        if (location.TextSpan is { } span)
        {
            var text = source.Data.CanonicalText;
            var end = (long)span.Start + span.Length;
            if (end > text.Length || !Boundary(text, span.Start) || !Boundary(text, (int)end)) return "invalid-location";
        }
        return "ready";
    }

    private static bool SameRecord(ProjectLocation a, ProjectLocation b) => a.Context.Equals(b.Context) &&
        a.DocumentId == b.DocumentId && a.EntityId == b.EntityId && a.RelationId == b.RelationId;
    private static bool Boundary(string text, int index) => index == 0 || index == text.Length ||
        !(char.IsHighSurrogate(text[index - 1]) && char.IsLowSurrogate(text[index]));
    private static bool HasIdentity(JsonElement value, string field, string expected) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(field, out var id) && id.ValueKind == JsonValueKind.String && id.GetString() == expected;
}
