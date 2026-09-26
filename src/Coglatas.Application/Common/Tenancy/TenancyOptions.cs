using Coglatas.Domain.Enums;

namespace Coglatas.Application.Common.Tenancy;

public sealed class TenancyOptions
{
    public AppMode AppMode { get; set; } = AppMode.SaaS;

    public string DefaultTenantSlug { get; set; } = "default";

    public TenantResolutionStrategy TenantResolutionStrategy { get; set; } = TenantResolutionStrategy.Host;

    public bool AllowTenantSwitching { get; set; } = true;

    public bool SeedOnStartup { get; set; }

    public bool AllowDevelopmentHeaderInProduction { get; set; }

    public bool AllowDevelopmentHeaderTenantResolution { get; set; }

    public string DevelopmentTenantHeaderName { get; set; } = "X-Tenant-Slug";

    public string TenantCookieName { get; set; } = "coglatas_tenant";
}
