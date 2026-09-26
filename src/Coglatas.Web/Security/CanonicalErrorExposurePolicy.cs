namespace Coglatas.Web.Security;

/// <summary>
/// Canonical disclosure classification for transport error fields. Controllers
/// own HTTP status mapping only; they must not invent per-endpoint redaction rules.
/// Unknown codes fail closed as sensitive.
/// </summary>
public static class CanonicalErrorExposurePolicy
{
    private static readonly HashSet<string> PublicSafeCodes = new(StringComparer.Ordinal)
    {
        "AuthenticationRequired",
        "TenantMembershipRequired",
        "CapabilityDenied",
        "ValidationFailed",
        "MissingIdempotencyKey",
        "InvalidIdempotencyKey",
        "IdempotencyConflict",
        "DependencyUnavailable",
        "UnsupportedMediaType",
        "MalformedJson",
        "CsrfRejected",
        "FeatureDisabled",
        "ModuleDisabled"
    };

    public static bool IsSensitive(string? code)
    {
        return string.IsNullOrWhiteSpace(code) || !PublicSafeCodes.Contains(code);
    }
}
