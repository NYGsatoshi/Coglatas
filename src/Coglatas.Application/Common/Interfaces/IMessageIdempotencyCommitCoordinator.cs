using Coglatas.Domain.Entities;

namespace Coglatas.Application.Common.Interfaces;

public sealed record MessageIdempotencyCommitResult(
    Message Message,
    bool WasReconciled);

/// <summary>
/// Commits one staged Message unit of work and reconciles the database-backed
/// client-request uniqueness race when the full Infrastructure provider is in use.
/// </summary>
public interface IMessageIdempotencyCommitCoordinator
{
    Task<MessageIdempotencyCommitResult> CommitAsync(
        Message pendingMessage,
        CancellationToken cancellationToken = default);
}
