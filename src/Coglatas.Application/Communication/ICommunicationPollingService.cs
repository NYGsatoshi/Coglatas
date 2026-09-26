using Coglatas.Application.Common;

namespace Coglatas.Application.Communication;

public interface ICommunicationPollingService
{
    Task<Result<ConversationUnreadPollingResponse>> GetUnreadCountsAsync(CommunicationPollingQuery query, CancellationToken cancellationToken = default);

    Task<Result<NotificationPollingResponse>> GetNotificationsAsync(CommunicationPollingQuery query, CancellationToken cancellationToken = default);

    Task<Result<CommunicationUpdatesPollingResponse>> GetUpdatesAsync(CommunicationUpdatesPollingQuery query, CancellationToken cancellationToken = default);
}
