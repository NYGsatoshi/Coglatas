namespace Coglatas.Application.Messaging;

public interface IConversationAuthorizationService
{
    Task<bool> CanViewConversation(Guid userId, Guid conversationId, CancellationToken cancellationToken = default);
    Task<bool> CanSendMessage(Guid userId, Guid conversationId, CancellationToken cancellationToken = default);
    Task<bool> CanManageConversation(Guid userId, Guid conversationId, CancellationToken cancellationToken = default);
    Task<bool> CanModerateConversation(Guid userId, Guid conversationId, CancellationToken cancellationToken = default);
    Task<bool> CanCreateThread(Guid userId, Guid parentConversationId, CancellationToken cancellationToken = default);
    Task<bool> CanEditMessage(Guid userId, Guid messageId, CancellationToken cancellationToken = default);
    Task<bool> CanDeleteMessage(Guid userId, Guid messageId, CancellationToken cancellationToken = default);
}
