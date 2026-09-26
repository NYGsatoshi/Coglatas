using System.Diagnostics;
using System.Text.Json.Serialization;
using Coglatas.Application.Security.Redaction;

namespace Coglatas.Web.Models;

public sealed record ApiSuccessEnvelope<T>(
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("data")] T Data,
    [property: JsonPropertyName("warnings")] IReadOnlyList<object> Warnings);

public sealed record ApiErrorEnvelope(
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("error")] ApiErrorBody Error,
    [property: JsonPropertyName("traceId")] string TraceId,
    [property: JsonPropertyName("status")] int Status);

public sealed record ApiErrorBody(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("target")] string? Target,
    [property: JsonPropertyName("details")] IReadOnlyList<object> Details,
    [property: JsonPropertyName("redactionApplied")] bool RedactionApplied);

/// <summary>
/// Common WPC API envelope factory. Error payloads always pass through the
/// canonical ErrorResponse redaction profile. Existing callers may set
/// redactionApplied=true to classify their supplied content as sensitive; the
/// emitted flag is still derived from whether canonical redaction changed the
/// response rather than copied from that request.
/// </summary>
public static class ApiEnvelope
{
    public static ApiSuccessEnvelope<T> Success<T>(HttpContext context, T data) =>
        new(context.TraceIdentifier, data, Array.Empty<object>());

    public static ApiErrorEnvelope Error(
        HttpContext context,
        int status,
        string code,
        string message,
        string? target = null,
        bool redactionApplied = false,
        AuthorizationContext? authorizationContext = null)
    {
        var requestServices = context.RequestServices;
        var redactionService =
            requestServices?.GetService(typeof(IRedactionService)) as IRedactionService ??
            new CanonicalRedactionService();

        var redactionContext = authorizationContext ?? new AuthorizationContext(
            ActorId: null,
            TenantId: null,
            ModuleKey: "WpcEnvelope",
            Purpose: "NormalOperation",
            RequestId: context.TraceIdentifier,
            AuthorizationState: RedactionAuthorizationState.Unknown);

        var source = new ErrorRedactionSource(
            code,
            message,
            target,
            Array.Empty<object>(),
            redactionApplied ? RedactionSensitivity.Sensitive : RedactionSensitivity.PublicSafe);

        var result = redactionService.Redact(
            redactionContext,
            source,
            RedactionProfile.ErrorResponse);

        if (result.Value is not ErrorRedactionSource redacted)
        {
            throw new InvalidOperationException("Canonical ErrorResponse redaction returned an invalid payload type.");
        }

        return new ApiErrorEnvelope(
            context.TraceIdentifier,
            new ApiErrorBody(
                redacted.Code,
                redacted.Message,
                redacted.Target,
                redacted.Details,
                result.RedactionApplied),
            Activity.Current?.Id ?? context.TraceIdentifier,
            status);
    }

    /// <summary>
    /// Identifies WPC canonical command surfaces that must use the canonical
    /// envelope even when authentication, CSRF, model binding, or exception
    /// handling fails before the controller executes. The method name is
    /// retained for WPC-01 compatibility.
    /// </summary>
    public static bool IsWorkspaceCreationPath(string? path)
    {
        var normalized = path?.TrimEnd('/') ?? string.Empty;
        if (normalized.Equals("/api/workspaces", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("/api/workspaces/capabilities", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 4 &&
            segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) &&
            segments[1].Equals("workspaces", StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParse(segments[2], out _) &&
            segments[3].Equals("projects", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (segments.Length == 5 &&
            segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) &&
            segments[1].Equals("workspaces", StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParse(segments[2], out _) &&
            segments[3].Equals("projects", StringComparison.OrdinalIgnoreCase) &&
            segments[4].Equals("create-options", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return segments.Length == 4 &&
               segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) &&
               segments[1].Equals("projects", StringComparison.OrdinalIgnoreCase) &&
               Guid.TryParse(segments[2], out _) &&
               (segments[3].Equals("activate", StringComparison.OrdinalIgnoreCase) ||
                segments[3].Equals("visibility", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Identifies the Project-scoped canonical Task-create read/command
    /// surfaces.  Keep this separate from the older Workspace classifier so
    /// compatibility Task CRUD endpoints do not acquire a new envelope by
    /// accident.
    /// </summary>
    public static bool IsCanonicalTaskCreatePath(string? path)
    {
        var normalized = path?.TrimEnd('/') ?? string.Empty;
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 5 &&
               segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) &&
               segments[1].Equals("projects", StringComparison.OrdinalIgnoreCase) &&
               Guid.TryParse(segments[2], out _) &&
               segments[3].Equals("tasks", StringComparison.OrdinalIgnoreCase) &&
               (segments[4].Equals("create", StringComparison.OrdinalIgnoreCase) ||
                segments[4].Equals("create-options", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsCanonicalCreatePath(string? path) =>
        IsWorkspaceCreationPath(path) || IsCanonicalTaskCreatePath(path);

    public static bool IsProjectVisibilityPath(string? path)
    {
        var normalized = path?.TrimEnd('/') ?? string.Empty;
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 4 &&
               segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) &&
               segments[1].Equals("projects", StringComparison.OrdinalIgnoreCase) &&
               Guid.TryParse(segments[2], out _) &&
               segments[3].Equals("visibility", StringComparison.OrdinalIgnoreCase);
    }
}
