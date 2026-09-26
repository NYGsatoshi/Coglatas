using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Tenancy;

public sealed class TenantAuthorizationService(ITenantRepository tenantRepository) : ITenantAuthorizationService
{
    public async Task<bool> CanAccessTenantAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var membership = await tenantRepository.GetTenantUserAsync(tenantId, userId, cancellationToken);
        return membership is { Status: TenantUserStatus.Active };
    }

    public async Task<bool> CanManageTenantAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var membership = await tenantRepository.GetTenantUserAsync(tenantId, userId, cancellationToken);
        if (membership is not
        {
            Status: TenantUserStatus.Active,
            Role: TenantUserRole.Owner or TenantUserRole.Admin
        })
        {
            return false;
        }

        var tenant = membership.Tenant ?? await tenantRepository.GetTenantAsync(tenantId, cancellationToken);
        var user = membership.User ?? await tenantRepository.GetUserAsync(userId, cancellationToken);
        return tenant is { Status: TenantStatus.Active, DeletedAt: null } &&
               user is { Status: UserStatus.Active, DeletedAt: null };
    }

    public async Task<bool> IsPlatformAdminAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await tenantRepository.GetUserAsync(userId, cancellationToken);
        return user is
        {
            Status: UserStatus.Active,
            DeletedAt: null,
            SystemRole: SystemRole.PlatformAdmin
        };
    }

    public Task<bool> CanSwitchTenantAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        return CanAccessTenantAsync(userId, tenantId, cancellationToken);
    }
}
