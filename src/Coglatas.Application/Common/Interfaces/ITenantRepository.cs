using Coglatas.Domain.Entities;

namespace Coglatas.Application.Common.Interfaces;

public interface ITenantRepository
{
    Task<IReadOnlyList<Tenant>> ListTenantsAsync(CancellationToken cancellationToken = default);

    Task<Tenant?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<Tenant?> GetTenantBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<Tenant?> GetTenantByPrimaryDomainAsync(string primaryDomain, CancellationToken cancellationToken = default);

    Task AddTenantAsync(Tenant tenant, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantUser>> ListTenantUsersAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantUser>> ListUserTenantMembershipsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<TenantUser?> GetTenantUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

    Task AddTenantUserAsync(TenantUser tenantUser, CancellationToken cancellationToken = default);

    Task<User?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
