using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

public sealed class TenantRepository(AppDbContext dbContext) : ITenantRepository
{
    public async Task<IReadOnlyList<Tenant>> ListTenantsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Tenants
            .AsNoTracking()
            .OrderBy(tenant => tenant.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<Tenant?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return dbContext.Tenants.FirstOrDefaultAsync(tenant => tenant.Id == tenantId, cancellationToken);
    }

    public Task<Tenant?> GetTenantBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        return dbContext.Tenants.FirstOrDefaultAsync(tenant => tenant.Slug == slug, cancellationToken);
    }

    public Task<Tenant?> GetTenantByPrimaryDomainAsync(string primaryDomain, CancellationToken cancellationToken = default)
    {
        return dbContext.Tenants.FirstOrDefaultAsync(tenant => tenant.PrimaryDomain == primaryDomain, cancellationToken);
    }

    public Task AddTenantAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        return dbContext.Tenants.AddAsync(tenant, cancellationToken).AsTask();
    }

    public async Task<IReadOnlyList<TenantUser>> ListTenantUsersAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await dbContext.TenantUsers
            .Include(tenantUser => tenantUser.User)
            .Where(tenantUser => tenantUser.TenantId == tenantId)
            .OrderBy(tenantUser => tenantUser.User!.DisplayName)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TenantUser>> ListUserTenantMembershipsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Tenant switching must evaluate all tenant memberships for the authenticated user.
        // This bypass is restricted to TenantUser/Tenant data and still predicates on UserId.
        return await dbContext.TenantUsers
            .IgnoreQueryFilters()
            .Include(tenantUser => tenantUser.Tenant)
            .Where(tenantUser => tenantUser.UserId == userId)
            .ToListAsync(cancellationToken);
    }

    public Task<TenantUser?> GetTenantUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Tenant-management services need to verify membership by explicit tenant/user keys,
        // including for tenant switching before the target tenant becomes current.
        return dbContext.TenantUsers
            .IgnoreQueryFilters()
            .Include(tenantUser => tenantUser.User)
            .Include(tenantUser => tenantUser.Tenant)
            .FirstOrDefaultAsync(
                tenantUser => tenantUser.TenantId == tenantId && tenantUser.UserId == userId,
                cancellationToken);
    }

    public Task AddTenantUserAsync(TenantUser tenantUser, CancellationToken cancellationToken = default)
    {
        return dbContext.TenantUsers.AddAsync(tenantUser, cancellationToken).AsTask();
    }

    public Task<User?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return dbContext.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);
    }
}
