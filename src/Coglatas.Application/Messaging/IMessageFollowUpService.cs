using Coglatas.Application.Common;

namespace Coglatas.Application.Messaging;

public interface IMessageFollowUpService
{
    Task<Result<PagedResponse<MessageFollowUpListItemResponse>>> ListAsync(
        MessageFollowUpListQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<MessageFollowUpStateResponse>> SaveAsync(
        Guid messageId,
        CancellationToken cancellationToken = default);

    Task<Result<MessageFollowUpStateResponse>> RemoveAsync(
        Guid messageId,
        CancellationToken cancellationToken = default);
}
