using Coglatas.Web.Configuration;
using Microsoft.Extensions.Options;

namespace Coglatas.Web.Middleware;

/// <summary>Checks browser origins for every Hub transport, including WebSocket upgrades.</summary>
public sealed class RealtimeOriginMiddleware(RequestDelegate next, IOptions<SecurityOptions> securityOptions)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/hubs/app") &&
            !HttpSecurityPolicy.IsAllowedRealtimeOrigin(context.Request, securityOptions.Value))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        await next(context);
    }
}
