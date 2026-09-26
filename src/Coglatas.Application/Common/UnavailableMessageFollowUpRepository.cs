using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.Common;

/// <summary>
/// Fail-closed fallback for minimal hosts that compose Application without the
/// Infrastructure persistence layer. Production registration replaces it.
/// </summary>
internal sealed class UnavailableMessageFollowUpRepository : IMessageFollowUpRepository
{
    public Task<PagedResponse<MessageFollowUp>> ListVisibleAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new PagedResponse<MessageFollowUp>([], page, pageSize, 0));

    public Task<MessageFollowUp?> GetAsync(
        Guid userId,
        Guid messageId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<MessageFollowUp?>(null);

    public Task AddAsync(
        MessageFollowUp followUp,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Message follow-up persistence is unavailable.");

    public void Remove(MessageFollowUp followUp) =>
        throw new InvalidOperationException("Message follow-up persistence is unavailable.");
}
