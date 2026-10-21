using System.ComponentModel.DataAnnotations;
using Coglatas.Application.Common;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Messaging;

public sealed record WorkspaceChannelDirectoryQuery(int Page = 1, int PageSize = 50, string? Search = null);

public sealed record CreateWorkspaceChannelRequest(
    [Required, MaxLength(200)] string Name,
    [Required] string Visibility);

public sealed record InviteWorkspaceChannelMemberRequest(Guid UserId);

public sealed record WorkspaceChannelDirectoryItem(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string Visibility,
    bool IsMember,
    bool CanJoin,
    bool CanManage,
    bool IsDefault);

public sealed record WorkspaceChannelDirectoryResponse(
    Guid WorkspaceId,
    bool CanCreate,
    IReadOnlyList<WorkspaceChannelDirectoryItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

public interface IWorkspaceChannelDirectoryService
{
    Task<Result<WorkspaceChannelDirectoryResponse>> ListAsync(
        Guid workspaceId, WorkspaceChannelDirectoryQuery query, CancellationToken cancellationToken = default);
    Task<Result<WorkspaceChannelDirectoryItem>> CreateAsync(
        Guid workspaceId, CreateWorkspaceChannelRequest request, string? idempotencyKey,
        CancellationToken cancellationToken = default);
    Task<Result<WorkspaceChannelDirectoryItem>> JoinAsync(
        Guid workspaceId, Guid conversationId, CancellationToken cancellationToken = default);
    Task<Result> InviteAsync(
        Guid workspaceId, Guid conversationId, Guid recipientUserId, CancellationToken cancellationToken = default);
}

/// <summary>
/// All reads are current persisted snapshots, explicitly scoped by Tenant and
/// Workspace. Discovery projects metadata only; it never grants Message access.
/// </summary>
public interface IWorkspaceChannelDirectoryStore
{
    Task<WorkspaceRole?> GetCurrentActorRoleAsync(
        Guid tenantId, Guid workspaceId, Guid userId, CancellationToken cancellationToken = default);
    Task<PagedResponse<WorkspaceChannelDirectoryItem>> ListAsync(
        Guid tenantId, Guid workspaceId, Guid userId, int page, int pageSize, string? search,
        CancellationToken cancellationToken = default);
    Task<Conversation?> GetCurrentAsync(
        Guid tenantId, Guid workspaceId, Guid conversationId, CancellationToken cancellationToken = default);
    Task<ConversationMember?> GetCurrentMemberAsync(
        Guid tenantId, Guid conversationId, Guid userId, CancellationToken cancellationToken = default);
    /// <summary>Serializes admission inside the caller-owned transaction.</summary>
    Task LockAsync(Guid tenantId, Guid workspaceId, Guid conversationId, CancellationToken cancellationToken = default);
    Task<ConversationMember?> GetMemberForMutationAsync(
        Guid tenantId, Guid conversationId, Guid userId, CancellationToken cancellationToken = default);
}
