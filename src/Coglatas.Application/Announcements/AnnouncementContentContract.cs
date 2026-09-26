using System.Text.Json;
using System.Text.Json.Serialization;

namespace Coglatas.Application.Announcements;

/// <summary>
/// A user-visible Announcement action. The URL is never a credential or a
/// download grant; it is limited to an application-relative path or an HTTPS
/// URL without embedded credentials.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementActionLink(string Label, string Url);

/// <summary>
/// The presentation content decoded from the backwards-compatible persisted
/// string. PersistedBody is the canonical value written to the existing Body
/// column; API responses expose Body, CTA, and one linked Attachment action
/// separately.
/// </summary>
public sealed record AnnouncementDecodedContent(
    string Body,
    AnnouncementActionLink? Cta,
    AnnouncementActionLink? Attachment,
    string PersistedBody,
    bool IsEnvelope);

/// <summary>
/// Versioned compatibility codec for a CTA and one linked Attachment action.
/// Existing plain-text Announcement rows remain valid. No storage key, signed
/// URL, capability, or file-download grant is placed in this envelope.
/// </summary>
public static class AnnouncementContentContract
{
    public const int MaximumPersistedLength = 20_000;
    public const int MaximumLabelLength = 120;
    public const int MaximumUrlLength = 2_048;

    private const string EnvelopePrefix = "@coglatas-announcement-content:v1\n";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Validates and canonicalizes browser-supplied content. A valid existing
    /// envelope is accepted so the durable worker can revalidate a stored
    /// draft without double-encoding it.
    /// </summary>
    public static AnnouncementDecodedContent PrepareForPersistence(
        string? body,
        AnnouncementActionLink? cta = null,
        AnnouncementActionLink? attachment = null)
    {
        var suppliedBody = body ?? string.Empty;
        if (cta is null && attachment is null)
        {
            var existing = Decode(suppliedBody);
            if (existing.IsEnvelope)
            {
                return existing;
            }
        }

        var normalizedBody = suppliedBody.Trim();
        if (normalizedBody.Length == 0)
        {
            throw new JsonException("Announcement body is required.");
        }

        var normalizedCta = NormalizeLink(cta, "CTA");
        var normalizedAttachment = NormalizeLink(attachment, "Attachment");
        var persisted = normalizedCta is null && normalizedAttachment is null
            ? normalizedBody
            : EncodeEnvelope(normalizedBody, normalizedCta, normalizedAttachment);

        if (persisted.Length > MaximumPersistedLength)
        {
            throw new JsonException($"Announcement content must be {MaximumPersistedLength} characters or fewer after action metadata is encoded.");
        }

        return new AnnouncementDecodedContent(
            normalizedBody,
            normalizedCta,
            normalizedAttachment,
            persisted,
            normalizedCta is not null || normalizedAttachment is not null);
    }

    /// <summary>
    /// Decodes a stored value without throwing. Unknown, malformed, or future
    /// envelopes fail closed to plain text with no actionable content.
    /// </summary>
    public static AnnouncementDecodedContent Decode(string? persistedBody)
    {
        var persisted = persistedBody ?? string.Empty;
        if (!persisted.StartsWith(EnvelopePrefix, StringComparison.Ordinal))
        {
            return Plain(persisted);
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<AnnouncementContentEnvelope>(
                persisted[EnvelopePrefix.Length..],
                JsonOptions);
            if (envelope is null || envelope.Version != 1)
            {
                return Plain(persisted);
            }

            var body = envelope.Body?.Trim() ?? string.Empty;
            if (body.Length == 0)
            {
                return Plain(persisted);
            }

            var cta = NormalizeLink(envelope.Cta, "CTA");
            var attachment = NormalizeLink(envelope.Attachment, "Attachment");
            if (cta is null && attachment is null)
            {
                return Plain(persisted);
            }

            var canonical = EncodeEnvelope(body, cta, attachment);
            if (canonical.Length > MaximumPersistedLength)
            {
                return Plain(persisted);
            }

            return new AnnouncementDecodedContent(body, cta, attachment, canonical, true);
        }
        catch (JsonException)
        {
            return Plain(persisted);
        }
        catch (UriFormatException)
        {
            return Plain(persisted);
        }
    }

    public static bool IsSafeUrl(string? rawUrl)
    {
        var value = rawUrl?.Trim();
        if (string.IsNullOrEmpty(value) ||
            value.Length > MaximumUrlLength ||
            value.Any(char.IsControl) ||
            value.Contains('\\') ||
            value.Contains(' '))
        {
            return false;
        }

        if (value.StartsWith("/", StringComparison.Ordinal))
        {
            if (value.StartsWith("//", StringComparison.Ordinal))
            {
                return false;
            }

            string decoded;
            try
            {
                decoded = Uri.UnescapeDataString(value);
            }
            catch (UriFormatException)
            {
                return false;
            }

            if (decoded.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => string.Equals(segment, "..", StringComparison.Ordinal)))
            {
                return false;
            }

            return Uri.TryCreate(value, UriKind.Relative, out _);
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var absolute) &&
            string.Equals(absolute.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(absolute.Host) &&
            string.IsNullOrEmpty(absolute.UserInfo);
    }

    private static AnnouncementActionLink? NormalizeLink(
        AnnouncementActionLink? link,
        string fieldName)
    {
        if (link is null)
        {
            return null;
        }

        var label = link.Label?.Trim() ?? string.Empty;
        var url = link.Url?.Trim() ?? string.Empty;
        if (label.Length == 0 || url.Length == 0)
        {
            throw new JsonException($"{fieldName} label and URL must be supplied together.");
        }
        if (label.Length > MaximumLabelLength)
        {
            throw new JsonException($"{fieldName} label must be {MaximumLabelLength} characters or fewer.");
        }
        if (!IsSafeUrl(url))
        {
            throw new JsonException($"{fieldName} URL must be an application-relative path or a safe HTTPS URL.");
        }

        return new AnnouncementActionLink(label, url);
    }

    private static string EncodeEnvelope(
        string body,
        AnnouncementActionLink? cta,
        AnnouncementActionLink? attachment) =>
        EnvelopePrefix + JsonSerializer.Serialize(
            new AnnouncementContentEnvelope(1, body, cta, attachment),
            JsonOptions);

    private static AnnouncementDecodedContent Plain(string body) =>
        new(body, null, null, body, false);

    private sealed record AnnouncementContentEnvelope(
        int Version,
        string Body,
        AnnouncementActionLink? Cta,
        AnnouncementActionLink? Attachment);
}
