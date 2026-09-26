namespace Coglatas.Application.Tenancy;

public interface ITenantAuthorizationService
{
    Task<bool> CanAccessTenantAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);

    Task<bool> CanManageTenantAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);

    Task<bool> IsPlatformAdminAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> CanSwitchTenantAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);
}
