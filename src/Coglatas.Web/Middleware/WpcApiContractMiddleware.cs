using Coglatas.Web.Models;

namespace Coglatas.Web.Middleware;

/// <summary>
/// Rejects unsupported media types at canonical WPC command boundaries before
/// MVC generates its body-less 415 response.
/// </summary>
public sealed class WpcApiContractMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsCanonicalJsonCommand(context.Request.Method, context.Request.Path.Value) ||
            string.IsNullOrWhiteSpace(context.Request.ContentType) ||
            context.Request.HasJsonContentType())
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
        await context.Response.WriteAsJsonAsync(ApiEnvelope.Error(
            context,
            StatusCodes.Status415UnsupportedMediaType,
            "UnsupportedMediaType",
            "The request Content-Type is not supported.",
            "header.Content-Type"));
    }

    private static bool IsCanonicalJsonCommand(string method, string? path)
    {
        if (HttpMethods.IsPut(method))
        {
            return ApiEnvelope.IsProjectVisibilityPath(path);
        }

        if (!HttpMethods.IsPost(method) || !ApiEnvelope.IsCanonicalCreatePath(path))
        {
            return false;
        }

        // This shared WPC classifier also serves GET-only capabilities for
        // authorization/exception envelopes. It is not a JSON command route.
        var normalized = path?.TrimEnd('/') ?? string.Empty;
        return !normalized.Equals(
            "/api/workspaces/capabilities",
            StringComparison.OrdinalIgnoreCase) &&
               !ApiEnvelope.IsProjectVisibilityPath(path);
    }
}
