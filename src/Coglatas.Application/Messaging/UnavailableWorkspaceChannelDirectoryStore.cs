using Coglatas.Application.Common;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Messaging;

public sealed class UnavailableWorkspaceChannelDirectoryStore : IWorkspaceChannelDirectoryStore
{
    public Task<WorkspaceRole?> GetCurrentActorRoleAsync(Guid tenantId, Guid workspaceId, Guid userId,
        CancellationToken cancellationToken = default) => Task.FromResult<WorkspaceRole?>(null);

    public Task<PagedResponse<WorkspaceChannelDirectoryItem>> ListAsync(Guid tenantId, Guid workspaceId, Guid userId,
        int page, int pageSize, string? search, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PagedResponse<WorkspaceChannelDirectoryItem>([], Math.Max(1, page), Math.Clamp(pageSize, 1, 100), 0));

    public Task<Conversation?> GetCurrentAsync(Guid tenantId, Guid workspaceId, Guid conversationId,
        CancellationToken cancellationToken = default) => Task.FromResult<Conversation?>(null);

    public Task<ConversationMember?> GetCurrentMemberAsync(Guid tenantId, Guid conversationId, Guid userId,
        CancellationToken cancellationToken = default) => Task.FromResult<ConversationMember?>(null);

    public Task LockAsync(Guid tenantId, Guid workspaceId, Guid conversationId,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new InvalidOperationException("Workspace channel persistence is unavailable."));

    public Task<ConversationMember?> GetMemberForMutationAsync(Guid tenantId, Guid conversationId, Guid userId,
        CancellationToken cancellationToken = default) =>
        Task.FromException<ConversationMember?>(new InvalidOperationException("Workspace channel persistence is unavailable."));
}
