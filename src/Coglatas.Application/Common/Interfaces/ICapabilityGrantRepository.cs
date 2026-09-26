using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Common.Interfaces;

public interface ICapabilityGrantRepository
{
    Task<CapabilityGrant?> GetByIdAsync(Guid tenantId, Guid grantId, CancellationToken cancellationToken = default);

    Task<CapabilityGrant?> FindSlotAsync(
        Guid tenantId,
        Guid subjectUserId,
        string capabilityKey,
        CapabilityScopeType scopeType,
        Guid? scopeId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CapabilityGrant>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task AddAsync(CapabilityGrant grant, CancellationToken cancellationToken = default);
}
