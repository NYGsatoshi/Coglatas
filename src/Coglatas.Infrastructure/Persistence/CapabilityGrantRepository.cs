using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

public sealed class CapabilityGrantRepository(AppDbContext dbContext) : ICapabilityGrantRepository
{
    private DbSet<CapabilityGrant> Grants => dbContext.Set<CapabilityGrant>();

    public Task<CapabilityGrant?> GetByIdAsync(
        Guid tenantId,
        Guid grantId,
        CancellationToken cancellationToken = default)
    {
        return Grants.FirstOrDefaultAsync(
            grant => grant.TenantId == tenantId && grant.Id == grantId,
            cancellationToken);
    }

    public Task<CapabilityGrant?> FindSlotAsync(
        Guid tenantId,
        Guid subjectUserId,
        string capabilityKey,
        CapabilityScopeType scopeType,
        Guid? scopeId,
        CancellationToken cancellationToken = default)
    {
        return Grants.FirstOrDefaultAsync(
            grant => grant.TenantId == tenantId &&
                     grant.SubjectUserId == subjectUserId &&
                     grant.CapabilityKey == capabilityKey &&
                     grant.ScopeType == scopeType &&
                     grant.ScopeId == scopeId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<CapabilityGrant>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return await Grants
            .AsNoTracking()
            .Where(grant => grant.TenantId == tenantId)
            .OrderBy(grant => grant.CapabilityKey)
            .ThenBy(grant => grant.SubjectUserId)
            .ThenBy(grant => grant.Id)
            .ToListAsync(cancellationToken);
    }

    public Task AddAsync(CapabilityGrant grant, CancellationToken cancellationToken = default)
    {
        return Grants.AddAsync(grant, cancellationToken).AsTask();
    }
}
