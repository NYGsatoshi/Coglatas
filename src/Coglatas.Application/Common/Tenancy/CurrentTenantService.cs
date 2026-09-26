using Coglatas.Application.Common.Interfaces;

namespace Coglatas.Application.Common.Tenancy;

public sealed class CurrentTenantService : ICurrentTenantAccessor
{
    public Guid TenantId { get; private set; }

    public bool IsAvailable { get; private set; }

    public string? TenantSlug { get; private set; }

    public bool IsPlatformScope { get; private set; }

    public void SetTenant(Guid tenantId, string tenantSlug)
    {
        TenantId = tenantId;
        TenantSlug = tenantSlug;
        IsAvailable = true;
        IsPlatformScope = false;
    }

    public void SetPlatformScope()
    {
        TenantId = Guid.Empty;
        TenantSlug = null;
        IsAvailable = false;
        IsPlatformScope = true;
    }

    public void Clear()
    {
        TenantId = Guid.Empty;
        TenantSlug = null;
        IsAvailable = false;
        IsPlatformScope = false;
    }
}
