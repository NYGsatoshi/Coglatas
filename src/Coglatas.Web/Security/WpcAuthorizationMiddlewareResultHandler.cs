using Coglatas.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Coglatas.Web.Security;

/// <summary>
/// Keeps authentication/authorization failures that occur before the WPC
/// controller inside the common safe API envelope.
/// </summary>
public sealed class WpcAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _fallback = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (!ApiEnvelope.IsCanonicalCreatePath(context.Request.Path.Value) || authorizeResult.Succeeded)
        {
            await _fallback.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        var status = authorizeResult.Challenged
            ? StatusCodes.Status401Unauthorized
            : StatusCodes.Status403Forbidden;
        var code = authorizeResult.Challenged
            ? "AuthenticationRequired"
            : "CapabilityDenied";
        var message = authorizeResult.Challenged
            ? "Authentication is required."
            : "You are not allowed to perform this action.";

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(ApiEnvelope.Error(
            context,
            status,
            code,
            message),
            context.RequestAborted);
    }
}
