using System.Text.Json;
using System.Text.Json.Nodes;

namespace Coglatas.Application.Audit;

/// <summary>
/// Defensive read-side policy for legacy or directly imported Audit metadata.
/// Normal writes already store redacted metadata, but a disclosure endpoint
/// must not assume that every historical JSON object passed through that path.
/// </summary>
public static class AuditMetadataDisclosurePolicy
{
    private const int MaximumJsonDepth = 32;

    private static readonly HashSet<string> ProhibitedFields = Set(
        "password",
        "passwordHash",
        "secret",
        "clientSecret",
        "webhookSecret",
        "token",
        "rawToken",
        "tokenHash",
        "grantToken",
        "signedUrl",
        "authorization",
        "cookie",
        "credential",
        "credentialValue",
        "apiKey",
        "privateKey",
        "connectionString",
        "environmentVariable",
        "storageKey",
        "objectStoragePath",
        "rawFilePath",
        "filePath",
        "fileContent",
        "attachmentContent",
        "messageBody",
        "commentBody",
        "bodyPlainText",
        "body",
        "reviewReason",
        "reviewReturnReason",
        "blockedReason",
        "watchState",
        "watchStates",
        "taskNotificationPreference",
        "preferenceValue",
        "deadlineDigestLocalTime",
        "effectiveDeadlineDigestLocalTime",
        "taskTitle",
        "restrictedTitle",
        "displayName",
        "taskDisplayName",
        "license",
        "licenseKey",
        "licenseMaterial",
        "email",
        "phone",
        "phoneNumber",
        "address",
        "guardianContact",
        "healthNotes",
        "medicalNotes",
        "medicalInformation",
        "rawText",
        "searchRawText",
        "receiptOcr",
        "receiptOcrText",
        "ocrText",
        "exportPackage",
        "exportPackageBody",
        "claim",
        "claims",
        "evidence",
        "rightsEvidence",
        "auditDetail",
        "auditTDetail",
        "securitySetting",
        "securitySettings");

    public static AuditSensitiveMetadataResponse Project(Guid auditId, string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return new AuditSensitiveMetadataResponse(auditId, new JsonObject(), RedactionApplied: false);
        }

        try
        {
            var parsed = JsonNode.Parse(
                metadataJson,
                nodeOptions: null,
                documentOptions: new JsonDocumentOptions { MaxDepth = MaximumJsonDepth });
            if (parsed is not JsonObject source)
            {
                return EmptyRedacted(auditId);
            }

            var redactionApplied = false;
            var metadata = ProjectObject(source, ref redactionApplied);
            return new AuditSensitiveMetadataResponse(auditId, metadata, redactionApplied);
        }
        catch (JsonException)
        {
            // PostgreSQL jsonb is valid JSON, but InMemory/legacy imports can
            // contain malformed data. Fail closed without returning the raw text.
            return EmptyRedacted(auditId);
        }
    }

    private static JsonObject ProjectObject(JsonObject source, ref bool redactionApplied)
    {
        var projected = new JsonObject();
        foreach (var property in source)
        {
            if (IsProhibitedField(property.Key))
            {
                redactionApplied = true;
                continue;
            }

            projected[property.Key] = ProjectNode(property.Value, ref redactionApplied);
        }

        return projected;
    }

    private static JsonNode? ProjectNode(JsonNode? source, ref bool redactionApplied)
    {
        if (source is JsonObject sourceObject)
        {
            return ProjectObject(sourceObject, ref redactionApplied);
        }

        if (source is JsonArray sourceArray)
        {
            var projected = new JsonArray();
            foreach (var item in sourceArray)
            {
                projected.Add(ProjectNode(item, ref redactionApplied));
            }

            return projected;
        }

        return source?.DeepClone();
    }

    private static AuditSensitiveMetadataResponse EmptyRedacted(Guid auditId) =>
        new(auditId, new JsonObject(), RedactionApplied: true);

    private static HashSet<string> Set(params string[] values) =>
        new(values.Select(Normalize), StringComparer.Ordinal);

    private static bool IsProhibitedField(string fieldName)
    {
        var normalized = Normalize(fieldName);
        return ProhibitedFields.Contains(normalized) ||
            ContainsAny(
                normalized,
                "password",
                "secret",
                "token",
                "credential",
                "cookie",
                "connectionstring",
                "privatekey",
                "apikey",
                "signedurl",
                "claim",
                "evidence",
                "rawtext",
                "filecontent",
                "attachmentcontent") ||
            normalized.EndsWith("body", StringComparison.Ordinal);
    }

    private static bool ContainsAny(string value, params string[] fragments) =>
        fragments.Any(fragment => value.Contains(fragment, StringComparison.Ordinal));

    private static string Normalize(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();
}
