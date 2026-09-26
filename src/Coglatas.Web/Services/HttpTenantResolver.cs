using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coglatas.Web.Services;

public sealed class HttpTenantResolver(
    IHttpContextAccessor httpContextAccessor,
    IWebHostEnvironment environment,
    IOptions<TenancyOptions> options,
    AppDbContext dbContext) : ITenantResolver
{
    public async Task<TenantResolutionResult> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var tenancy = options.Value;
        var strategy = tenancy.AppMode == AppMode.OnPremSingleTenant
            ? TenantResolutionStrategy.ConfigDefault
            : tenancy.TenantResolutionStrategy;

        var slugOrDomain = strategy switch
        {
            TenantResolutionStrategy.ConfigDefault => tenancy.DefaultTenantSlug,
            TenantResolutionStrategy.Session => ReadTenantCookie(tenancy),
            TenantResolutionStrategy.HeaderForDevelopmentOnly => ReadDevelopmentHeader(tenancy),
            TenantResolutionStrategy.Subdomain => ReadSubdomain(),
            TenantResolutionStrategy.Host => ReadHostOrDefault(tenancy),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(slugOrDomain))
        {
            return TenantResolutionResult.Unresolved("Tenant could not be resolved.");
        }

        var tenant = await dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate =>
                candidate.Slug == slugOrDomain ||
                candidate.PrimaryDomain == slugOrDomain,
                cancellationToken);

        if (tenant is null)
        {
            return TenantResolutionResult.Unresolved("Tenant does not exist.");
        }

        return tenant.Status == TenantStatus.Active
            ? TenantResolutionResult.Resolved(tenant.Id, tenant.Slug)
            : TenantResolutionResult.Unresolved($"Tenant is {tenant.Status}.");
    }

    private string? ReadDevelopmentHeader(TenancyOptions tenancy)
    {
        if (!tenancy.AllowDevelopmentHeaderTenantResolution)
        {
            return null;
        }

        if (environment.IsProduction() && !tenancy.AllowDevelopmentHeaderInProduction)
        {
            return null;
        }

        var request = httpContextAccessor.HttpContext?.Request;
        return request?.Headers.TryGetValue(tenancy.DevelopmentTenantHeaderName, out var value) == true
            ? value.ToString().Trim()
            : null;
    }

    private string? ReadTenantCookie(TenancyOptions tenancy)
    {
        var request = httpContextAccessor.HttpContext?.Request;
        return request?.Cookies.TryGetValue(tenancy.TenantCookieName, out var value) == true
            ? value.Trim()
            : null;
    }

    private string? ReadSubdomain()
    {
        var host = httpContextAccessor.HttpContext?.Request.Host.Host;
        if (string.IsNullOrWhiteSpace(host) || IsLocalHost(host))
        {
            return null;
        }

        var segments = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 2 ? segments[0] : null;
    }

    private string? ReadHostOrDefault(TenancyOptions tenancy)
    {
        var host = httpContextAccessor.HttpContext?.Request.Host.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        return IsLocalHost(host) ? tenancy.DefaultTenantSlug : host.Trim().ToLowerInvariant();
    }

    private static bool IsLocalHost(string host)
    {
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);
    }
}
