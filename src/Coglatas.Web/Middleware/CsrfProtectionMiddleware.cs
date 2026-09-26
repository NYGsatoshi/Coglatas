using Coglatas.Web.Configuration;
using Coglatas.Web.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;

namespace Coglatas.Web.Middleware;

public sealed class CsrfProtectionMiddleware(
    RequestDelegate next,
    IAntiforgery antiforgery,
    IOptions<SecurityOptions> securityOptions,
    ILogger<CsrfProtectionMiddleware> logger)
{
    private const string MethodNotAllowedEndpointDisplayName = "405 HTTP Method Not Supported";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!securityOptions.Value.EnableCsrfProtection ||
            IsSafeMethod(context.Request.Method) ||
            IsMethodNotAllowedEndpoint(context))
        {
            await next(context);
            return;
        }

        // Authentication runs before this middleware.  PR06 command actions are
        // deliberately AllowAnonymous so their application services can return
        // the canonical typed 401 envelope.  Do not replace that response with
        // a CSRF 403 when no authenticated cookie exists.
        if ((IsPr06CommandPath(context.Request.Path.Value) ||
             ApiEnvelope.IsCanonicalCreatePath(context.Request.Path.Value)) &&
            context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException ex)
        {
            var safeMethod = context.Request.Method
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal);
            var safePath = (context.Request.Path.Value ?? string.Empty)
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal);
            logger.LogWarning(
                ex,
                "Rejected unsafe request without a valid CSRF token: {Method} {Path}",
                safeMethod,
                safePath);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            var path = context.Request.Path.Value;
            if (ApiEnvelope.IsCanonicalCreatePath(path))
            {
                await context.Response.WriteAsJsonAsync(ApiEnvelope.Error(
                    context,
                    StatusCodes.Status403Forbidden,
                    "CsrfRejected",
                    "A valid CSRF token is required."),
                    context.RequestAborted);
            }
            else if (IsPr06CommandPath(path))
            {
                var dependency = path?.Contains("/dependencies", StringComparison.OrdinalIgnoreCase) == true;
                await context.Response.WriteAsJsonAsync(new
                {
                    requestId = context.TraceIdentifier,
                    error = new
                    {
                        code = dependency ? "TASK_DEPENDENCY_CSRF_REQUIRED" : "GANTT_CSRF_REQUIRED",
                        message = "A valid CSRF token is required.",
                        target = (string?)null,
                        details = Array.Empty<object>(),
                        redactionApplied = false
                    }
                }, context.RequestAborted);
            }
            else
            {
                await context.Response.WriteAsJsonAsync(new { error = "A valid CSRF token is required." }, context.RequestAborted);
            }
            return;
        }

        await next(context);
    }

    private static bool IsSafeMethod(string method)
    {
        return HttpMethods.IsGet(method) ||
               HttpMethods.IsHead(method) ||
               HttpMethods.IsOptions(method) ||
               HttpMethods.IsTrace(method);
    }

    // Endpoint routing selects this framework-owned terminal endpoint only when
    // the path is known but the request method is unsupported. Let it produce
    // the protocol-level 405 + Allow response instead of replacing it with a
    // CSRF 403. Supported unsafe endpoints still require antiforgery validation.
    private static bool IsMethodNotAllowedEndpoint(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        return endpoint?.Metadata.Count == 0 &&
               string.Equals(
                   endpoint.DisplayName,
                   MethodNotAllowedEndpointDisplayName,
                   StringComparison.Ordinal);
    }

    private static bool IsPr06CommandPath(string? path) =>
        NormalizePath(path).StartsWith("/api/tasks/", StringComparison.OrdinalIgnoreCase) &&
        (NormalizePath(path).EndsWith("/schedule", StringComparison.OrdinalIgnoreCase) ||
         NormalizePath(path).EndsWith("/progress", StringComparison.OrdinalIgnoreCase) ||
         NormalizePath(path).Contains("/dependencies", StringComparison.OrdinalIgnoreCase));

    private static string NormalizePath(string? path) =>
        path?.TrimEnd('/') ?? string.Empty;
}
