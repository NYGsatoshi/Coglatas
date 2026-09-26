using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Projects;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Messaging;

public sealed class ConversationAuthorizationService(IMessagingRepository messaging, IProjectAuthorizationService projects, IWorkspaceAuthorizationService? workspaces = null) : IConversationAuthorizationService
{
    private const int MaxThreadDepth = 32;

    public async Task<bool> CanViewConversation(Guid userId, Guid conversationId, CancellationToken cancellationToken = default)
    {
        if (conversationId == Guid.Empty)
        {
            return false;
        }

        var conversation = await messaging.GetConversationAsync(conversationId, cancellationToken);
        if (conversation is null ||
            !IsSupportedMvpType(conversation.Type) ||
            !await IsConversationScopeAllowed(userId, conversation, cancellationToken))
        {
            return false;
        }

        var readableIds = await messaging.FilterReadableConversationIdsAsync(
            userId,
            [conversationId],
            cancellationToken);
        return readableIds.Contains(conversationId);
    }

    public async Task<bool> CanSendMessage(Guid userId, Guid conversationId, CancellationToken cancellationToken = default)
    {
        return await CanViewConversation(userId, conversationId, cancellationToken) &&
            await CanSendMessageCore(userId, conversationId, [], 0, cancellationToken);
    }

    public async Task<bool> CanManageConversation(Guid userId, Guid conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await messaging.GetConversationAsync(conversationId, cancellationToken);
        if (conversation is null ||
            !IsSupportedMvpType(conversation.Type) ||
            conversation.Type is ConversationType.DirectMessage or ConversationType.Thread ||
            !await IsConversationScopeAllowed(userId, conversation, cancellationToken))
        {
            return false;
        }

        var member = await messaging.GetMemberAsync(conversationId, userId, cancellationToken);
        return member is not null &&
            IsActiveParticipant(member) &&
            (member.Role == ConversationMemberRole.Admin || member.CanManageMembers);
    }

    public async Task<bool> CanModerateConversation(Guid userId, Guid conversationId, CancellationToken cancellationToken = default)
    {
        return await CanViewConversation(userId, conversationId, cancellationToken) &&
            await CanModerateConversationCore(userId, conversationId, [], 0, cancellationToken);
    }

    private async Task<bool> CanModerateConversationCore(
        Guid userId,
        Guid conversationId,
        HashSet<Guid> visited,
        int depth,
        CancellationToken cancellationToken)
    {
        if (conversationId == Guid.Empty || depth > MaxThreadDepth || !visited.Add(conversationId))
        {
            return false;
        }

        var conversation = await messaging.GetConversationAsync(conversationId, cancellationToken);
        if (conversation is null ||
            !IsSupportedMvpType(conversation.Type) ||
            !await IsConversationScopeAllowed(userId, conversation, cancellationToken))
        {
            return false;
        }

        var member = await messaging.GetMemberAsync(conversationId, userId, cancellationToken);
        if (member is null ||
            !IsActiveParticipant(member) ||
            (member.Role != ConversationMemberRole.Admin && !member.CanManageMembers))
        {
            return false;
        }

        return conversation.Type != ConversationType.Thread ||
            await CanModerateConversationCore(
                userId,
                conversation.ParentConversationId ?? Guid.Empty,
                visited,
                depth + 1,
                cancellationToken);
    }

    public async Task<bool> CanCreateThread(Guid userId, Guid parentConversationId, CancellationToken cancellationToken = default)
    {
        return await CanViewConversation(userId, parentConversationId, cancellationToken) &&
            await CanCreateThreadCore(userId, parentConversationId, [], 0, cancellationToken);
    }

    private async Task<bool> CanCreateThreadCore(
        Guid userId,
        Guid parentConversationId,
        HashSet<Guid> visited,
        int depth,
        CancellationToken cancellationToken)
    {
        if (parentConversationId == Guid.Empty || depth > MaxThreadDepth || !visited.Add(parentConversationId))
        {
            return false;
        }

        var parent = await messaging.GetConversationAsync(parentConversationId, cancellationToken);
        if (parent is null ||
            !IsSupportedMvpType(parent.Type) ||
            parent.IsArchived ||
            parent.IsLocked ||
            !await IsConversationScopeAllowed(userId, parent, cancellationToken))
        {
            return false;
        }

        var member = await messaging.GetMemberAsync(parentConversationId, userId, cancellationToken);
        if (member is null ||
            !IsActiveParticipant(member) ||
            !member.CanPost ||
            !member.CanCreateThread ||
            member.Role == ConversationMemberRole.ReadOnly)
        {
            return false;
        }

        if (parent.Type != ConversationType.Thread)
        {
            // The new Thread adds one edge below this root. A parent chain
            // that already consumes the full read-depth budget must not be
            // allowed to create a durable child that immediately fails the
            // authoritative read boundary.
            return depth < MaxThreadDepth;
        }

        return await CanCreateThreadCore(
            userId,
            parent.ParentConversationId ?? Guid.Empty,
            visited,
            depth + 1,
            cancellationToken);
    }

    public async Task<bool> CanEditMessage(Guid userId, Guid messageId, CancellationToken cancellationToken = default)
    {
        var message = await messaging.GetMessageAsync(messageId, cancellationToken);
        return message is not null &&
            message.AuthorUserId == userId &&
            !message.DeletedAt.HasValue &&
            await CanSendMessage(userId, message.ConversationId, cancellationToken);
    }

    public async Task<bool> CanDeleteMessage(Guid userId, Guid messageId, CancellationToken cancellationToken = default)
    {
        var message = await messaging.GetMessageAsync(messageId, cancellationToken);
        return message is not null &&
            !message.DeletedAt.HasValue &&
            ((message.AuthorUserId == userId && await CanViewConversation(userId, message.ConversationId, cancellationToken)) ||
            await CanModerateConversation(userId, message.ConversationId, cancellationToken));
    }

    private async Task<bool> CanSendMessageCore(
        Guid userId,
        Guid conversationId,
        HashSet<Guid> visited,
        int depth,
        CancellationToken cancellationToken)
    {
        if (conversationId == Guid.Empty || depth > MaxThreadDepth || !visited.Add(conversationId))
        {
            return false;
        }

        var conversation = await messaging.GetConversationAsync(conversationId, cancellationToken);
        if (conversation is null ||
            !IsSupportedMvpType(conversation.Type) ||
            conversation.IsArchived ||
            conversation.IsLocked ||
            !await IsConversationScopeAllowed(userId, conversation, cancellationToken))
        {
            return false;
        }

        var member = await messaging.GetMemberAsync(conversationId, userId, cancellationToken);
        if (member is null ||
            !IsActiveParticipant(member) ||
            !member.CanPost ||
            member.Role == ConversationMemberRole.ReadOnly)
        {
            return false;
        }

        return conversation.Type != ConversationType.Thread ||
            await CanSendMessageCore(
                userId,
                conversation.ParentConversationId ?? Guid.Empty,
                visited,
                depth + 1,
                cancellationToken);
    }

    private async Task<bool> IsConversationScopeAllowed(Guid userId, Conversation conversation, CancellationToken cancellationToken)
    {
        if (conversation.Type == ConversationType.WorkspaceChannel)
        {
            return !conversation.ProjectId.HasValue &&
                   workspaces is not null &&
                   await workspaces.CanViewWorkspace(userId, conversation.WorkspaceId, cancellationToken);
        }

        if (conversation.Type == ConversationType.ProjectChannel && !conversation.ProjectId.HasValue)
        {
            return false;
        }

        return !conversation.ProjectId.HasValue ||
            await projects.CanViewProject(userId, conversation.ProjectId.Value, cancellationToken);
    }

    private static bool IsActiveParticipant(ConversationMember? member)
    {
        return member is { LeftAt: null, RemovedAt: null, CanRead: true };
    }

    private static bool IsSupportedMvpType(ConversationType type)
    {
        return type is ConversationType.DirectMessage or ConversationType.WorkspaceChannel or ConversationType.ProjectChannel or ConversationType.Thread;
    }
}
