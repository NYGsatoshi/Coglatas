using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

public sealed class DefaultConversationStore(AppDbContext dbContext) : IDefaultConversationStore
{
    public Task<Conversation?> FindDefaultAsync(
        Guid workspaceId,
        Guid? projectId,
        ConversationDefaultKind defaultKind,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Conversations.FirstOrDefaultAsync(
            conversation => conversation.WorkspaceId == workspaceId &&
                            conversation.ProjectId == projectId &&
                            conversation.DefaultKind == defaultKind,
            cancellationToken);
    }

    public Task<ConversationMember?> GetMemberAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.ConversationMembers.FirstOrDefaultAsync(
            member => member.ConversationId == conversationId && member.UserId == userId,
            cancellationToken);
    }

    public Task AddConversationAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        return dbContext.Conversations.AddAsync(conversation, cancellationToken).AsTask();
    }

    public Task AddMemberAsync(ConversationMember member, CancellationToken cancellationToken = default)
    {
        return dbContext.ConversationMembers.AddAsync(member, cancellationToken).AsTask();
    }
}
