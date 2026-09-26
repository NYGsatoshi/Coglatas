namespace Coglatas.Application.Common.Interfaces;

public interface ICurrentTenant
{
    Guid TenantId { get; }

    bool IsAvailable { get; }

    string? TenantSlug { get; }

    bool IsPlatformScope { get; }
}

public interface ICurrentTenantAccessor : ICurrentTenant
{
    void SetTenant(Guid tenantId, string tenantSlug);

    void SetPlatformScope();

    void Clear();
}
