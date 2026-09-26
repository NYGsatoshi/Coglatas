using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Common.Interfaces;

/// <summary>
/// Persistence boundary shared by canonical Workspace/Project default
/// Conversation provisioners. Callers own the business transaction.
/// </summary>
public interface IDefaultConversationStore
{
    Task<Conversation?> FindDefaultAsync(
        Guid workspaceId,
        Guid? projectId,
        ConversationDefaultKind defaultKind,
        CancellationToken cancellationToken = default);

    Task<ConversationMember?> GetMemberAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task AddConversationAsync(Conversation conversation, CancellationToken cancellationToken = default);

    Task AddMemberAsync(ConversationMember member, CancellationToken cancellationToken = default);
}
