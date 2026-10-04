using System.Text.Json;

namespace Coglatas.Domain.ProjectIde;

public enum SourceDecodeStatus { StructurallyDecoded, Unsupported, Invalid }

/// <summary>A structural decode is never a Compiler verdict or authorization PASS.</summary>
public sealed class SourceDecodeResult
{
    private readonly byte[] originalBytes;
    public SourceDecodeStatus Status { get; }
    public ProjectSource? Source { get; }
    public string? Reason { get; }
    internal SourceDecodeResult(SourceDecodeStatus status, ProjectSource? source, string? reason, byte[] originalBytes)
    {
        Status = status; Source = source; Reason = reason; this.originalBytes = originalBytes;
    }
    public byte[] RecoverOriginalBytes() => (byte[])originalBytes.Clone();
    public ProjectSource RequireSource() => Source ?? throw new FormatException(Reason ?? "No supported Source representation.");
}

public static class ProjectSourceCodec
{
    public static SourceDecodeResult Decode(ReadOnlySpan<byte> bytes, SourceCodecLimits? limits = null)
    {
        var original = bytes.ToArray();
        try
        {
            var parsed = SourceJson.Parse(bytes, limits);
            var root = parsed.Value;
            if (SourceFields.String(root, "format") != ProjectSource.Format ||
                SourceFields.String(root, "schemaId") != ProjectSource.SchemaId ||
                SourceFields.PositiveInteger(root, "schemaVersion") != ProjectSource.SchemaVersion ||
                SourceFields.PositiveInteger(root, "canonicalEncodingVersion") != ProjectSource.CanonicalEncodingVersion)
                return new(SourceDecodeStatus.Unsupported, null, "Unsupported Source format/schema/canonical encoding; original bytes retained.", original);
            var canonical = NormalizeSets(parsed, limits ?? new());
            var source = new ProjectSource(canonical);
            var opaque = HasUnsupportedData(source);
            return new(opaque ? SourceDecodeStatus.Unsupported : SourceDecodeStatus.StructurallyDecoded, source,
                opaque ? "Opaque data retained; semantic compatibility/coverage has not been established." : null, original);
        }
        catch (UnsupportedSourceException)
        {
            return new(SourceDecodeStatus.Unsupported, null, "Unsupported document/context/digest representation; original bytes retained.", original);
        }
        catch (Exception error) when (error is FormatException or ArgumentException or JsonException or OverflowException)
        {
            // Avoid publishing field values, private identifiers or fragments in error text.
            return new(SourceDecodeStatus.Invalid, null, "Invalid Source structure, identity, integrity or processing bound; original bytes retained.", original);
        }
    }

    public static byte[] Encode(ProjectSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Data.ToCanonicalBytes();
    }

    public static byte[] Encode(SourceDecodeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Source is null ? result.RecoverOriginalBytes() : Encode(result.Source);
    }

    private static SourceJson NormalizeSets(SourceJson data, SourceCodecLimits limits)
    {
        var fields = data.Value.EnumerateObject().ToDictionary(member => member.Name, member => (object)member.Value, StringComparer.Ordinal);
        foreach (var (field, key) in new[]
        {
            ("documents", "documentId"), ("declarations", "entityId"), ("relations", "relationId"),
            ("extensionDeclarations", "namespace"), ("packageLock", "artifactId")
        })
            fields[field] = OrderSet(SourceFields.Array(data.Value, field), key);
        // References are an explicitly unordered set. Entire canonical reference is the stable composite key.
        var references = SourceFields.Array(data.Value, "sourceReferences").EnumerateArray().ToArray();
        foreach (var reference in references) ValidateReference(reference, limits);
        var keys = references.Select(reference => SourceJson.FromElement(reference, limits).CanonicalText).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length) throw new FormatException("Duplicate Source reference.");
        fields["sourceReferences"] = references.OrderBy(reference => SourceJson.FromElement(reference, limits).CanonicalText,
            Comparer<string>.Create(CanonicalJson.ScalarCompare)).ToArray();
        var manifest = SourceFields.Object(data.Value, "manifest").EnumerateObject()
            .ToDictionary(member => member.Name, member => (object)member.Value, StringComparer.Ordinal);
        manifest["documents"] = OrderSet(SourceFields.Array(SourceFields.Object(data.Value, "manifest"), "documents"), "documentId");
        fields["manifest"] = manifest;
        return SourceJson.FromObject(fields, limits);
    }

    private static JsonElement[] OrderSet(JsonElement array, string key)
    {
        var entries = array.EnumerateArray().ToArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
            if (!seen.Add(SourceFields.String(entry, key))) throw new FormatException("Duplicate Source set identity.");
        return entries.OrderBy(entry => SourceFields.String(entry, key), Comparer<string>.Create(CanonicalJson.ScalarCompare)).ToArray();
    }

    private static void ValidateReference(JsonElement reference, SourceCodecLimits limits)
    {
        _ = SourceFields.Identity<DocumentId>(reference, "documentId");
        var kind = SourceFields.Token(reference, "kind");
        if (kind == "coglatas.entity-reference") _ = SourceFields.Identity<EntityId>(reference, "entityId");
        else if (kind == "coglatas.relation-reference") _ = SourceFields.Identity<RelationId>(reference, "relationId");
        // Unknown reference kinds remain opaque; no guessed target or first-match resolution.
        if (reference.TryGetProperty("externalManifest", out var external))
        {
            var tenant = SourceFields.Identity<TenantId>(external, "tenantId");
            var project = SourceFields.Identity<ProjectId>(external, "projectId");
            var context = SourceRevisionContext.Parse(SourceJson.FromElement(SourceFields.Object(external, "context"), limits));
            if (context.Branch.TenantId != tenant || context.Branch.ProjectId != project)
                throw new FormatException("External manifest context scope mismatch.");
            _ = ContentDigest.Parse(SourceJson.FromElement(SourceFields.Object(external, "digest"), limits));
        }
    }

    private static bool HasUnsupportedData(ProjectSource source)
    {
        // Slice 1 parses common headers only. It does not pretend to know entity/relation/extension semantics.
        if (source.Declarations.Value.GetArrayLength() > 0 || source.Relations.Value.GetArrayLength() > 0 ||
            source.ExtensionDeclarations.Value.GetArrayLength() > 0 || source.PackageLock.Value.GetArrayLength() > 0 ||
            source.SourceReferences.Value.GetArrayLength() > 0 || HasUnknownContext(source.Context))
            return true;
        if (source.Documents.Any(document => document.Root.Kind != "coglatas.project" || document.Root.SchemaVersion != 1 ||
            document.Root.Payload.Value.EnumerateObject().Any())) return true;
        var sourceFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "format", "schemaId", "schemaVersion", "canonicalEncodingVersion", "tenantId", "projectId", "context",
            "documents", "manifest", "declarations", "relations", "extensionDeclarations", "packageLock", "sourceReferences"
        };
        if (source.Data.Value.EnumerateObject().Any(member => !sourceFields.Contains(member.Name))) return true;
        return source.Documents.Any(document =>
            HasUnknown(document.Data.Value, "documentId", "logicalName", "schemaId", "schemaVersion", "root", "digest") ||
            HasUnknown(document.Root.Data.Value, "entityId", "kind", "schemaVersion", "payload") ||
            HasUnknown(document.Digest.Data.Value, "algorithm", "value")) ||
            HasUnknown(source.Manifest.Data.Value, "documents") ||
            source.Manifest.Data.Value.GetProperty("documents").EnumerateArray().Any(entry =>
                HasUnknown(entry, "documentId", "schemaId", "schemaVersion", "digest") ||
                HasUnknown(entry.GetProperty("digest"), "algorithm", "value"));
    }

    private static bool HasUnknownContext(SourceRevisionContext context)
    {
        if (HasUnknown(context.Data.Value.GetProperty("branch"), "tenantId", "projectId", "branchId")) return true;
        return context.Kind switch
        {
            "coglatas.committed" => HasUnknown(context.Data.Value, "kind", "branch", "revisionId"),
            "coglatas.candidate" => HasUnknown(context.Data.Value, "kind", "branch", "proposalId", "candidateRevisionId", "baseRevision", "capturedTargetHead") ||
                HasUnknownContext(SourceRevisionContext.Parse(SourceJson.FromElement(context.Data.Value.GetProperty("baseRevision"), context.Data.Limits))) ||
                HasUnknownContext(SourceRevisionContext.Parse(SourceJson.FromElement(context.Data.Value.GetProperty("capturedTargetHead"), context.Data.Limits))),
            "coglatas.scenario" => HasUnknown(context.Data.Value, "kind", "branch", "scenarioId", "scenarioRevisionId", "baseContext", "overlayDigest") ||
                HasUnknownContext(context.Scenario!.BaseContext) || HasUnknown(context.Scenario.OverlayDigest.Data.Value, "algorithm", "value"),
            _ => true
        };
    }

    private static bool HasUnknown(JsonElement value, params string[] known) =>
        value.EnumerateObject().Any(member => !known.Contains(member.Name, StringComparer.Ordinal));
}
