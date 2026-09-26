using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.Messaging;

/// <summary>
/// Minimal-host fallback. Relational hosts replace this with the
/// provider-aware coordinator registered by Infrastructure.
/// </summary>
internal sealed class UnitOfWorkMessageFollowUpCommitCoordinator(IUnitOfWork unitOfWork)
    : IMessageFollowUpCommitCoordinator
{
    public async Task<MessageFollowUpCommitResult> SaveAsync(
        MessageFollowUp pendingFollowUp,
        CancellationToken cancellationToken = default)
    {
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new MessageFollowUpCommitResult(pendingFollowUp, WasReconciled: false);
    }

    public async Task<bool> RemoveAsync(
        MessageFollowUp pendingFollowUp,
        CancellationToken cancellationToken = default)
    {
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
