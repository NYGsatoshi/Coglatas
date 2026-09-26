using Coglatas.Web.Configuration;

namespace Coglatas.Web.Middleware;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = HttpSecurityPolicy.FrameOptions;
            headers["Referrer-Policy"] = HttpSecurityPolicy.ReferrerPolicy;
            headers["Permissions-Policy"] = HttpSecurityPolicy.PermissionsPolicy;
            headers["Content-Security-Policy"] = HttpSecurityPolicy.BuildContentSecurityPolicy(context.Request);

            if (HttpSecurityPolicy.ShouldPreventCaching(context))
            {
                headers["Cache-Control"] = HttpSecurityPolicy.NoStoreCacheControl;
                headers["Pragma"] = "no-cache";
                headers["Expires"] = "0";
            }

            if (HttpSecurityPolicy.IsRedirectStatusCode(context.Response.StatusCode) &&
                headers.TryGetValue("Location", out var locations) &&
                locations.Count > 0 &&
                !HttpSecurityPolicy.IsApprovedRedirectLocation(context.Request, locations[0]))
            {
                // Redirect targets are server-owned. A future endpoint that
                // needs a cross-origin redirect must add an explicitly reviewed
                // contract instead of accepting arbitrary Location values.
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                headers.Remove("Location");
                headers["Cache-Control"] = HttpSecurityPolicy.NoStoreCacheControl;
                headers["Pragma"] = "no-cache";
                headers["Expires"] = "0";
            }

            return Task.CompletedTask;
        });

        if (IsTaskDetailRead(context.Request))
        {
            var headers = context.Response.Headers;
            headers["Cache-Control"] = "no-store, max-age=0";
            headers["Pragma"] = "no-cache";
            headers["Expires"] = "0";
        }

        await next(context);
    }

    private static bool IsTaskDetailRead(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) ||
            !request.Path.StartsWithSegments("/api/tasks", out var remaining))
        {
            return false;
        }

        var taskIdSegment = remaining.Value?.Trim('/');
        return Guid.TryParse(taskIdSegment, out _);
    }
}
