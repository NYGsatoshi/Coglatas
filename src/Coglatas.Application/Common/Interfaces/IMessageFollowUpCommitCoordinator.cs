using Coglatas.Domain.Entities;

namespace Coglatas.Application.Common.Interfaces;

public sealed record MessageFollowUpCommitResult(
    MessageFollowUp FollowUp,
    bool WasReconciled);

/// <summary>
/// Commits participant-private saved-message state and reconciles the exact
/// database uniqueness/deletion races when the relational provider is in use.
/// </summary>
public interface IMessageFollowUpCommitCoordinator
{
    Task<MessageFollowUpCommitResult> SaveAsync(
        MessageFollowUp pendingFollowUp,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(
        MessageFollowUp pendingFollowUp,
        CancellationToken cancellationToken = default);
}
