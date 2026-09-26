using System.Text.Json;
using System.Text.Json.Nodes;

namespace Coglatas.Application.Security.Redaction;

/// <summary>
/// Enforces the canonical FileMetadata field-classification boundary on top of
/// the shared redaction engine. File name, uploader, target label, and
/// classification are Confidential metadata and require an explicit field
/// policy grant even after record-level authorization succeeds.
/// </summary>
public sealed class CanonicalFileMetadataRedactionService(
    CanonicalRedactionService inner) : IRedactionService
{
    private const string ConfidentialMarker = "[redacted:confidential]";

    private static readonly JsonSerializerOptions ProjectionJson =
        new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> ConfidentialFileMetadataFields = Set(
        "filename",
        "originalfilename",
        "uploadedby",
        "uploadedbyuserid",
        "uploadedbydisplayname",
        "uploader",
        "uploaderuserid",
        "uploaderdisplayname",
        "targetlabel",
        "ownerlabel",
        "scopelabel",
        "classification");

    public RedactionResult Redact(
        AuthorizationContext context,
        object source,
        RedactionProfile profile)
    {
        var baseline = inner.Redact(context, source, profile);
        if (profile != RedactionProfile.FileMetadata || baseline.Value is RedactedPayload)
        {
            return baseline;
        }

        var serialized = baseline.Value is JsonNode existing
            ? existing.DeepClone()
            : JsonSerializer.SerializeToNode(
                baseline.Value,
                baseline.Value.GetType(),
                ProjectionJson);
        if (serialized is null)
        {
            throw new InvalidOperationException(
                "Canonical FileMetadata redaction could not serialize the response projection source.");
        }

        var metadataChanged = false;
        var projected = ProjectNode(serialized, context, ref metadataChanged)
            ?? throw new InvalidOperationException(
                "Canonical FileMetadata redaction returned a null response projection.");

        if (!metadataChanged)
        {
            return baseline;
        }

        return new RedactionResult(projected, RedactionApplied: true);
    }

    private static JsonNode? ProjectNode(
        JsonNode? node,
        AuthorizationContext context,
        ref bool changed)
    {
        if (node is JsonObject sourceObject)
        {
            var projected = new JsonObject();
            foreach (var property in sourceObject)
            {
                var normalizedName = NormalizeFieldName(property.Key);
                if (ConfidentialFileMetadataFields.Contains(normalizedName) &&
                    !context.FieldAccessPolicy.Allows(
                        CanonicalDataClassification.Confidential,
                        normalizedName))
                {
                    projected[property.Key] = RedactedValue(property.Value, normalizedName);
                    changed = true;
                    continue;
                }

                projected[property.Key] = ProjectNode(property.Value, context, ref changed);
            }

            return projected;
        }

        if (node is JsonArray sourceArray)
        {
            var projected = new JsonArray();
            foreach (var item in sourceArray)
            {
                projected.Add(ProjectNode(item, context, ref changed));
            }

            return projected;
        }

        return node?.DeepClone();
    }

    private static JsonNode? RedactedValue(JsonNode? value, string normalizedName)
    {
        if (normalizedName.Contains("filename", StringComparison.Ordinal))
        {
            return JsonValue.Create("[redacted:file]");
        }

        // Identifier-shaped metadata must preserve its transport type after
        // redaction. A string marker in a Guid/Guid? field breaks consumers and
        // leaks that a concrete identifier existed. Null is the canonical
        // fail-closed projection for uploader/user identifiers.
        if (normalizedName.EndsWith("userid", StringComparison.Ordinal))
        {
            return null;
        }

        if (value is JsonValue scalar && scalar.TryGetValue<string>(out _))
        {
            return JsonValue.Create(ConfidentialMarker);
        }

        return null;
    }

    private static string NormalizeFieldName(string name) =>
        name.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

    private static HashSet<string> Set(params string[] values) =>
        new(values.Select(NormalizeFieldName), StringComparer.Ordinal);
}