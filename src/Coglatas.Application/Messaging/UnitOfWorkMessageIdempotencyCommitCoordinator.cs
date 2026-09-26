using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.Messaging;

/// <summary>
/// Minimal-host fallback. Relational hosts replace this with the provider-aware
/// coordinator registered by Infrastructure.
/// </summary>
internal sealed class UnitOfWorkMessageIdempotencyCommitCoordinator(IUnitOfWork unitOfWork)
    : IMessageIdempotencyCommitCoordinator
{
    public async Task<MessageIdempotencyCommitResult> CommitAsync(
        Message pendingMessage,
        CancellationToken cancellationToken = default)
    {
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new MessageIdempotencyCommitResult(pendingMessage, WasReconciled: false);
    }
}
