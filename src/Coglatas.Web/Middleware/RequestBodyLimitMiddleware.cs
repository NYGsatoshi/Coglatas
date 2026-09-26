using Coglatas.Web.Configuration;
using Coglatas.Web.Models;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace Coglatas.Web.Middleware;

public sealed class RequestBodyLimitMiddleware(
    RequestDelegate next,
    IOptions<SecurityOptions> securityOptions)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var maxRequestBodySize = securityOptions.Value.MaxRequestBodySizeBytes;
        if (context.Request.ContentLength is long contentLength &&
            contentLength > maxRequestBodySize)
        {
            await WriteTooLargeResponseAsync(context);
            return;
        }

        var bodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySizeFeature is { IsReadOnly: false })
        {
            bodySizeFeature.MaxRequestBodySize = maxRequestBodySize;
        }

        await next(context);
    }

    public static async Task WriteTooLargeResponseAsync(HttpContext context)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new ErrorResponse(
            "RequestTooLarge",
            "The request body exceeds the maximum allowed size.",
            context.TraceIdentifier),
            context.RequestAborted);
    }
}
