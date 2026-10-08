
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Web.Configuration;

public sealed class SecurityOptions
{
    public const string CsrfHeaderName = "X-CSRF-Token";

    /// <summary>Security Foundation is opt-in Shadow; the safe default is Disabled.</summary>
    public SecurityEnforcementMode EvaluationMode { get; set; } = SecurityEnforcementMode.Disabled;

    /// <summary>Must remain false before a separately approved enforcement implementation.</summary>
    public bool EnforcementAllowed { get; set; }

    public CookieSecurePolicy CookieSecurePolicy { get; set; } = CookieSecurePolicy.Always;

    public bool RequireHttps { get; set; } = true;

    public bool EnableHsts { get; set; } = true;

    public bool EnableCsrfProtection { get; set; } = true;

    public bool EnableRateLimiting { get; set; } = true;

    /// <summary>
    /// Cross-origin browser callers explicitly approved by the operator.
    /// Empty means same-origin only; wildcard origins are never accepted.
    /// </summary>
    public string[] AllowedCorsOrigins { get; set; } = [];

    /// <summary>
    /// Whether the explicit CORS allowlist may receive credentialed responses.
    /// This has no effect while <see cref="AllowedCorsOrigins"/> is empty.
    /// </summary>
    public bool AllowCorsCredentials { get; set; }

    /// <summary>
    /// Canonical application-side HTTP request-body ceiling. The startup
    /// validator requires upload/multipart limits to fit inside this envelope.
    /// </summary>
    public long MaxRequestBodySizeBytes { get; set; } = 64L * 1024 * 1024;

    public long MaxMultipartBodySizeBytes { get; set; } = 64L * 1024 * 1024;

    public bool LoginLockoutEnabled { get; set; } = true;

    public int MaxFailedLoginAttempts { get; set; } = 5;

    public int LoginLockoutDurationMinutes { get; set; } = 15;
}
