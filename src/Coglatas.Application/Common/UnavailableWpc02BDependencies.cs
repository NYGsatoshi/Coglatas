using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Common;

/// <summary>
/// Fail-closed fallback used only by minimal hosts that compose AddApplication()
/// without Infrastructure. Full application composition registers the real
/// persistence implementations later and therefore overrides these fallbacks.
/// </summary>
internal sealed class UnavailableCapabilityGrantRepository : ICapabilityGrantRepository
{
    public Task<CapabilityGrant?> GetByIdAsync(
        Guid tenantId,
        Guid grantId,
        CancellationToken cancellationToken = default) => Task.FromResult<CapabilityGrant?>(null);

    public Task<CapabilityGrant?> FindSlotAsync(
        Guid tenantId,
        Guid subjectUserId,
        string capabilityKey,
        CapabilityScopeType scopeType,
        Guid? scopeId,
        CancellationToken cancellationToken = default) => Task.FromResult<CapabilityGrant?>(null);

    public Task<IReadOnlyList<CapabilityGrant>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CapabilityGrant>>([]);

    public Task AddAsync(CapabilityGrant grant, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("CapabilityGrant persistence is unavailable.");
}

internal sealed class UnavailableDefaultConversationStore : IDefaultConversationStore
{
    public Task<Conversation?> FindDefaultAsync(
        Guid workspaceId,
        Guid? projectId,
        ConversationDefaultKind defaultKind,
        CancellationToken cancellationToken = default) => Task.FromResult<Conversation?>(null);

    public Task<ConversationMember?> GetMemberAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken = default) => Task.FromResult<ConversationMember?>(null);

    public Task AddConversationAsync(Conversation conversation, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Canonical default Conversation persistence is unavailable.");

    public Task AddMemberAsync(ConversationMember member, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Canonical default Conversation persistence is unavailable.");
}
