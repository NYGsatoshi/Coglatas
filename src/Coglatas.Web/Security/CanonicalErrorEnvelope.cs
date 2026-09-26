using Coglatas.Application.Common;
using Coglatas.Web.Models;

namespace Coglatas.Web.Security;

/// <summary>
/// Converts a result failure into the canonical ErrorResponse profile. Error
/// sensitivity is classified centrally; callers only provide status/fallbacks.
/// </summary>
public static class CanonicalErrorEnvelope
{
    public static ApiErrorEnvelope FromResult(
        HttpContext httpContext,
        int status,
        ApplicationErrorDetail? detail,
        string? fallbackError,
        string fallbackCode,
        string fallbackMessage = "The request could not be completed.")
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackMessage);

        var code = detail?.Code ?? fallbackCode;
        return ApiEnvelope.Error(
            httpContext,
            status,
            code,
            detail?.Message ?? fallbackError ?? fallbackMessage,
            detail?.Target,
            redactionApplied: CanonicalErrorExposurePolicy.IsSensitive(code));
    }

    // Compatibility name retained for existing WPC-02E call sites. The method
    // no longer assumes every failure is sensitive; classification is canonical.
    public static ApiErrorEnvelope FromSensitiveResult(
        HttpContext httpContext,
        int status,
        ApplicationErrorDetail? detail,
        string? fallbackError,
        string fallbackCode,
        string fallbackMessage = "The request could not be completed.") =>
        FromResult(
            httpContext,
            status,
            detail,
            fallbackError,
            fallbackCode,
            fallbackMessage);
}
