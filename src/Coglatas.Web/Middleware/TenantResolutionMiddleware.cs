using Coglatas.Application.Common.Interfaces;

namespace Coglatas.Web.Middleware;

public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        ITenantResolver tenantResolver,
        ICurrentTenantAccessor currentTenant)
    {
        if (context.Request.Path.StartsWithSegments("/api/platform"))
        {
            currentTenant.SetPlatformScope();
            await next(context);
            return;
        }

        var result = await tenantResolver.ResolveAsync(context.RequestAborted);
        if (result.IsResolved && result.TenantId.HasValue && result.TenantSlug is not null)
        {
            currentTenant.SetTenant(result.TenantId.Value, result.TenantSlug);
        }
        else
        {
            currentTenant.Clear();
        }

        await next(context);
    }
}
