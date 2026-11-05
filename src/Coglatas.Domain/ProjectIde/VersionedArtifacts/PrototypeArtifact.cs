using System.Text.Json;

namespace Coglatas.Domain.ProjectIde.VersionedArtifacts;

/// <summary>Data-only candidate. Supported syntax establishes neither acquisition nor trust nor evidence eligibility.</summary>
public sealed class PrototypeArtifact
{
    public SourceJson Data { get; }
    public ArtifactIdentity Identity { get; }
    public bool SemanticsSupported { get; }
    public IReadOnlyList<ArtifactIdentity> Dependencies { get; }
    public ArtifactSourcePin? Source { get; }

    private PrototypeArtifact(SourceJson data)
    {
        Data = data;
        var root = data.Value;
        var type = SourceFields.String(root, "artifactType");
        var version = SourceFields.PositiveInteger(root, "schemaVersion");
        var encoding = SourceFields.PositiveInteger(root, "canonicalEncodingVersion");
        var domain = PrototypeArtifactRegistry.Domain(type, version, encoding);
        // The complete semantic envelope is hashed; there is no self digest or proof exclusion.
        Identity = new(type, version, encoding, ContentDigest.Compute(domain, data.ToCanonicalBytes()).Value);
        var reader = new ArtifactReader(data);
        reader.Object(root, "artifactType", "schemaVersion", "canonicalEncodingVersion", "source", "payload");
        try
        {
            var sourceData = RelationFields.Child(data, "source");
            reader.Object(sourceData.Value, "schemaId", "schemaVersion", "canonicalEncodingVersion", "context", "digest");
            reader.Digest(SourceFields.Object(sourceData.Value, "digest"));
            Source = new(sourceData);
            Validate(reader, type, SourceFields.Object(root, "payload"), Source);
        }
        catch (UnsupportedSourceException)
        {
            reader.Unsupported();
        }
        SemanticsSupported = reader.Supported;
        Dependencies = Array.AsReadOnly(reader.Dependencies.Distinct().ToArray());
    }

    public static PrototypeArtifact Parse(SourceJson data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new(data);
    }

    public static PrototypeArtifact ReadCanonical(ReadOnlySpan<byte> bytes)
    {
        var data = SourceJson.Parse(bytes);
        if (!bytes.SequenceEqual(data.ToCanonicalBytes())) throw new FormatException("Artifact bytes are not canonical.");
        return Parse(data);
    }

    public static PrototypeArtifact Create(string artifactType, ArtifactSourcePin source, SourceJson payload)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(payload);
        return Parse(SourceJson.FromObject(new
        {
            artifactType, schemaVersion = 1, canonicalEncodingVersion = 1,
            source = source.Data.Value, payload = payload.Value
        }));
    }

    public void RequireSupported()
    {
        if (!SemanticsSupported) throw new UnsupportedSourceException("Artifact contains unsupported semantics; original content is retained.");
    }

    private static void Validate(ArtifactReader reader, string type, JsonElement payload, ArtifactSourcePin source)
    {
        switch (type)
        {
            case PrototypeArtifactRegistry.DependencyManifest:
                reader.Object(payload, "entries");
                foreach (var entry in reader.Entries(payload))
                {
                    reader.Object(entry, "entryKey", "dependencyKind", "expected", "scope", "target", "coverageClaim", "sourceBinding");
                    var kind = reader.Choice(entry, "dependencyKind", "unit", "calendar", "source-document", "tenant-resource");
                    var expected = reader.Pin(entry, "expected");
                    var expectedType = kind switch
                    {
                        "unit" => "coglatas.prototype.unit", "calendar" => "coglatas.prototype.calendar",
                        "source-document" => SourceDocument.SchemaId, _ => "coglatas.prototype.tenant-resource"
                    };
                    if (expected.ArtifactType != expectedType) throw new FormatException("Dependency kind and expected artifact type disagree.");
                    var target = SourceFields.Object(entry, "target");
                    reader.Object(target, "kind", "id");
                    if (SourceFields.String(target, "kind") != kind) throw new FormatException("Dependency target kind mismatch.");
                    var targetId = SourceFields.String(target, "id");
                    if (!Guid.TryParseExact(targetId, "D", out var targetGuid) || targetGuid == Guid.Empty || targetId != targetGuid.ToString("D"))
                        throw new FormatException("Target requires a canonical UUID.");
                    var scope = SourceFields.Object(entry, "scope");
                    if (kind == "tenant-resource")
                    {
                        reader.Object(scope, "kind", "tenantId", "resourceId");
                        if (SourceFields.String(scope, "kind") != "tenant-resource" ||
                            SourceFields.Identity<TenantId>(scope, "tenantId") != source.Context.Branch.TenantId ||
                            SourceFields.String(scope, "resourceId") != SourceFields.String(target, "id"))
                            throw new FormatException("Tenant Resource scope mismatch.");
                    }
                    else
                    {
                        reader.Object(scope, "kind", "context");
                        if (SourceFields.String(scope, "kind") != "project" ||
                            !SourceRevisionContext.Parse(SourceJson.FromElement(SourceFields.Object(scope, "context"))).Equals(source.Context))
                            throw new FormatException("Dependency Project/Branch/revision scope mismatch.");
                    }
                    _ = reader.Choice(entry, "coverageClaim", "complete", "partial", "unknown");
                    var binding = SourceFields.Object(entry, "sourceBinding");
                    reader.Object(binding, "referenceSchemaVersion", "referenceKey");
                    if (SourceFields.PositiveInteger(binding, "referenceSchemaVersion") != 2)
                        throw new UnsupportedSourceException("Unsupported provisional reference mapping version.");
                    reader.Key(SourceFields.String(binding, "referenceKey"));
                }
                break;
            case PrototypeArtifactRegistry.CompilerInputAssociation:
                reader.Object(payload, "dependencyManifest", "compilerConfiguration", "legacyInputDigest");
                reader.Pin(payload, "dependencyManifest", PrototypeArtifactRegistry.DependencyManifest);
                _ = SourceFields.Object(payload, "compilerConfiguration");
                reader.Digest(SourceFields.Object(payload, "legacyInputDigest"));
                break;
            case PrototypeArtifactRegistry.MaterializationSet:
                reader.Object(payload, "dependencyManifest", "entries", "enumeration");
                reader.Pin(payload, "dependencyManifest", PrototypeArtifactRegistry.DependencyManifest);
                _ = reader.Choice(payload, "enumeration", "completed", "cancelled", "exhausted", "invalid");
                foreach (var entry in reader.Entries(payload))
                {
                    reader.Object(entry, "entryKey", "expected", "state", "actual");
                    var expected = reader.Pin(entry, "expected");
                    var state = reader.Choice(entry, "state", "present", "missing", "partial", "unsupported", "denied");
                    var actual = entry.GetProperty("actual");
                    if (state == "present")
                    {
                        if (reader.Pin(entry, "actual") != expected) throw new FormatException("Present materialization pin mismatch.");
                    }
                    else if (state is "missing" or "denied")
                    {
                        if (actual.ValueKind != JsonValueKind.Null) throw new FormatException("Missing or denied materialization cannot carry a body.");
                    }
                    else if (actual.ValueKind != JsonValueKind.Null) reader.Pin(entry, "actual");
                }
                break;
            case PrototypeArtifactRegistry.MaterializationReceipt:
                reader.Object(payload, "compilerInput", "actualSet", "producerClaim", "captureContext", "execution", "coverage", "provenance");
                reader.Pin(payload, "compilerInput", PrototypeArtifactRegistry.CompilerInputAssociation);
                reader.Pin(payload, "actualSet", PrototypeArtifactRegistry.MaterializationSet);
                var producer = SourceFields.Object(payload, "producerClaim");
                reader.Object(producer, "kind", "identity");
                _ = reader.Choice(producer, "kind", "trusted-ci-candidate", "backend-worker-candidate", "service-candidate", "local-diagnostic", "unknown");
                reader.Key(SourceFields.String(producer, "identity"));
                var capture = SourceFields.Object(payload, "captureContext");
                reader.Object(capture, "runId", "capturedAtUtc");
                reader.Key(SourceFields.String(capture, "runId"));
                var instant = SourceFields.String(capture, "capturedAtUtc");
                _ = ProjectInstant.Parse(instant);
                if (!instant.EndsWith('Z')) throw new FormatException("Capture time requires explicit UTC.");
                _ = reader.Choice(payload, "execution", "completed", "invalid", "cancelled", "exhausted", "failed", "unknown");
                _ = reader.Choice(payload, "coverage", "complete", "partial", "unknown");
                var provenance = SourceFields.Array(payload, "provenance");
                foreach (var item in provenance.EnumerateArray()) reader.Key(ValueFields.String(item));
                break;
        }
    }
}

internal sealed class ArtifactReader(SourceJson data)
{
    internal bool Supported { get; private set; } = true;
    internal List<ArtifactIdentity> Dependencies { get; } = [];
    internal void Unsupported() => Supported = false;

    internal void Object(JsonElement value, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new FormatException("Expected artifact object.");
        foreach (var field in fields)
            if (!value.TryGetProperty(field, out _)) throw new FormatException("Missing artifact field: " + field);
        if (value.EnumerateObject().Any(p => !fields.Contains(p.Name, StringComparer.Ordinal))) Supported = false;
    }

    internal void Key(string value)
    {
        if (value.Length is < 1 or > 256 || value.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '.' and not '-' and not '_'))
            throw new FormatException("Invalid bounded artifact key.");
    }

    internal string Choice(JsonElement value, string field, params string[] choices)
    {
        var token = SourceFields.String(value, field);
        if (!choices.Contains(token, StringComparer.Ordinal)) throw new UnsupportedSourceException("Unsupported artifact state or kind.");
        return token;
    }

    internal void Digest(JsonElement digest)
    {
        Object(digest, "algorithm", "value");
        _ = ContentDigest.Parse(SourceJson.FromElement(digest, data.Limits));
    }

    internal ArtifactIdentity Pin(JsonElement value, string field, string? expectedType = null)
    {
        var pin = SourceFields.Object(value, field);
        Object(pin, "artifactType", "schemaVersion", "canonicalEncodingVersion", "digest");
        var identity = new ArtifactIdentity(SourceFields.String(pin, "artifactType"), SourceFields.PositiveInteger(pin, "schemaVersion"),
            SourceFields.PositiveInteger(pin, "canonicalEncodingVersion"), SourceFields.String(pin, "digest"));
        if (expectedType is not null && identity.ArtifactType != expectedType) throw new FormatException("Wrong artifact pin type.");
        if (identity.SchemaVersion != 1 || identity.CanonicalEncodingVersion != 1)
            throw new UnsupportedSourceException("Unsupported pinned artifact version.");
        Dependencies.Add(identity);
        return identity;
    }

    internal IEnumerable<JsonElement> Entries(JsonElement payload)
    {
        var entries = SourceFields.Array(payload, "entries");
        if (entries.GetArrayLength() > 4096) throw new FormatException("Prototype entry processing bound exceeded.");
        string? previous = null;
        var bindings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries.EnumerateArray())
        {
            var key = SourceFields.String(entry, "entryKey"); Key(key);
            if (previous is not null && StringComparer.Ordinal.Compare(previous, key) >= 0)
                throw new FormatException("Entry set must be unique and ordered by entryKey.");
            previous = key;
            if (entry.TryGetProperty("sourceBinding", out var binding) && !bindings.Add(SourceFields.String(binding, "referenceKey")))
                throw new FormatException("Conflicting reference binding.");
            yield return entry;
        }
    }
}
