using System.Globalization;
using System.Text.Json;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

/// <summary>One resolver for Diagnostic, Symbol and Reference navigation over the accepted immutable Core objects.</summary>
internal static class SyntheticSourceNavigation
{
    internal static string Resolve(CompilerHostRequest? desired, ProjectSource? source, CoreCompilation? compilation,
        ProjectLocation location, bool requireSourceMap = false)
    {
        if (desired is null || source is null || compilation?.Model is not { } model) return "unavailable";
        if (requireSourceMap && compilation.Result.Execution != CompilerExecutionState.Completed) return "unavailable";
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
        var mapArtifacts = compilation.Result.Artifacts.Where(item => item.Role == "coglatas.source-map").Take(2).ToArray();
        if (mapArtifacts.Length > 1) return "wrong-scope";
        if (compilation.Validation?.Ir is { } ir)
        {
            if (!ir.Input.Data.Equals(input.Data) || mapArtifacts.Length != 1 || !mapArtifacts[0].InputDigest.Equals(input.InputDigest) ||
                !mapArtifacts[0].Digest.Equals(ir.SourceMapDigest)) return "wrong-scope";
            var entries = ir.SourceMap.Value.GetProperty(location.EntityId is null ? "relations" : "nodes").EnumerateArray();
            var matches = entries.Select(entry => ProjectLocation.Parse(SourceJson.Parse(entry.GetProperty("location").GetRawText())))
                .Where(mapped => SameRecord(mapped, location)).Take(2).ToArray();
            if (matches.Length != 1 || (requireSourceMap && (matches[0].PropertyPath.Length != 0 || matches[0].TextSpan is not null)))
                return "unavailable";
        }
        else if (mapArtifacts.Length != 0) return "wrong-scope";
        else if (requireSourceMap) return "unavailable";
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
