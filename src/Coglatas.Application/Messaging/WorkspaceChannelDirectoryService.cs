using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Messaging;

public sealed class WorkspaceChannelDirectoryService(
    IWorkspaceChannelDirectoryStore store,
    IMessagingRepository messaging,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    ICreateIdempotencyCoordinator idempotency,
    IUnitOfWork unitOfWork,
    IClock clock,
    IAuditLogger audit,
    IAuthorizationStateChangePublisher authorizationChanges) : IWorkspaceChannelDirectoryService
{
    private const string Unavailable = "Workspace channel unavailable.";

    public async Task<Result<WorkspaceChannelDirectoryResponse>> ListAsync(
        Guid workspaceId, WorkspaceChannelDirectoryQuery query, CancellationToken cancellationToken = default)
    {
        if (!TryScope(workspaceId, out var tenantId, out var userId) ||
            await store.GetCurrentActorRoleAsync(tenantId, workspaceId, userId, cancellationToken) is null)
        {
            return Result<WorkspaceChannelDirectoryResponse>.Failure(Unavailable);
        }
        if (query.Search?.Length > 200)
        {
            return Result<WorkspaceChannelDirectoryResponse>.Failure("Channel search is too long.");
        }
        var page = await store.ListAsync(tenantId, workspaceId, userId,
            Math.Max(1, query.Page), Math.Clamp(query.PageSize, 1, 100), query.Search?.Trim(), cancellationToken);
        var role = await store.GetCurrentActorRoleAsync(tenantId, workspaceId, userId, cancellationToken);
        if (role is null)
        {
            return Result<WorkspaceChannelDirectoryResponse>.Failure(Unavailable);
        }
        return Result<WorkspaceChannelDirectoryResponse>.Success(new(
            workspaceId, CanCreate(role), page.Items, page.Page, page.PageSize, page.TotalCount));
    }

    public async Task<Result<WorkspaceChannelDirectoryItem>> CreateAsync(
        Guid workspaceId, CreateWorkspaceChannelRequest request, string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (!TryScope(workspaceId, out var tenantId, out var userId) ||
            !CanCreate(await store.GetCurrentActorRoleAsync(tenantId, workspaceId, userId, cancellationToken)))
        {
            return Result<WorkspaceChannelDirectoryItem>.Failure(Unavailable);
        }
        var name = string.IsNullOrWhiteSpace(request.Name) ? string.Empty : request.Name.Trim();
        if (!IsValidName(name) || !TryVisibility(request.Visibility, out var visibility))
        {
            return Result<WorkspaceChannelDirectoryItem>.Failure("Channel name or visibility is invalid.");
        }
        if (!Guid.TryParse(idempotencyKey, out var requestId) || requestId == Guid.Empty)
        {
            return Result<WorkspaceChannelDirectoryItem>.Failure("A valid Idempotency-Key is required.");
        }
        var conversation = new Conversation
        {
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            Type = ConversationType.WorkspaceChannel,
            Title = name,
            Visibility = visibility,
            CreatedByUserId = userId
        };
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { WorkspaceId = workspaceId, Name = name, Visibility = visibility.ToString() }))));
        try
        {
            var result = await idempotency.ExecuteAsync<WorkspaceChannelDirectoryItem>(
                new(tenantId, userId, "workspace-channel.create", requestId.ToString("D"), hash,
                    "Conversation", conversation.Id),
                async token =>
                {
                    await unitOfWork.LockInviteAcceptanceMembershipsAsync(tenantId, workspaceId, userId, token);
                    if (!CanCreate(await store.GetCurrentActorRoleAsync(tenantId, workspaceId, userId, token)))
                    {
                        throw new ChannelAdmissionChangedException();
                    }
                    var creator = new ConversationMember
                    {
                        TenantId = tenantId,
                        ConversationId = conversation.Id,
                        UserId = userId,
                        Role = ConversationMemberRole.Admin,
                        CanRead = true,
                        CanPost = true,
                        CanManageMembers = true,
                        CanCreateThread = true,
                        JoinedAt = clock.UtcNow
                    };
                    await messaging.AddConversationAsync(conversation, token);
                    await messaging.AddMemberAsync(creator, token);
                    await RecordAdmissionAsync(tenantId, workspaceId, conversation.Id, userId, userId,
                        "WorkspaceChannelCreated", token);
                    return ToItem(conversation, creator);
                },
                async (id, token) =>
                {
                    if (!CanCreate(await store.GetCurrentActorRoleAsync(tenantId, workspaceId, userId, token))) return null;
                    var committed = await store.GetCurrentAsync(tenantId, workspaceId, id, token);
                    var member = await store.GetCurrentMemberAsync(tenantId, id, userId, token);
                    return committed is { IsArchived: false, IsLocked: false, DefaultKind: null } && IsReadable(member)
                        ? ToItem(committed, member) : null;
                }, cancellationToken);
            if (result.Disposition is IdempotentCreateDisposition.ReplayUnavailable)
            {
                // A matching claim exists. A new key could duplicate a committed
                // channel; retain the original key until the resource can be reconciled.
                return Result<WorkspaceChannelDirectoryItem>.Failure(new ApplicationErrorDetail(
                    "IdempotencyConflict",
                    "Channel creation could not be confirmed. Refresh the channel directory before retrying.",
                    Target: "header.Idempotency-Key"));
            }
            return (result.Disposition is IdempotentCreateDisposition.Created or IdempotentCreateDisposition.Replayed) && result.Value is not null
                ? Result<WorkspaceChannelDirectoryItem>.Success(result.Value)
                : Result<WorkspaceChannelDirectoryItem>.Failure("Channel creation could not be confirmed. Refresh the channel directory before retrying.");
        }
        catch (ChannelAdmissionChangedException)
        {
            return Result<WorkspaceChannelDirectoryItem>.Failure(Unavailable);
        }
    }

    public async Task<Result<WorkspaceChannelDirectoryItem>> JoinAsync(
        Guid workspaceId, Guid conversationId, CancellationToken cancellationToken = default)
    {
        if (!TryScope(workspaceId, out var tenantId, out var userId) || conversationId == Guid.Empty ||
            await store.GetCurrentActorRoleAsync(tenantId, workspaceId, userId, cancellationToken) is null)
        {
            return Result<WorkspaceChannelDirectoryItem>.Failure(Unavailable);
        }
        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await unitOfWork.LockInviteAcceptanceMembershipsAsync(tenantId, workspaceId, userId, token);
            await store.LockAsync(tenantId, workspaceId, conversationId, token);
            var role = await store.GetCurrentActorRoleAsync(tenantId, workspaceId, userId, token);
            var conversation = await store.GetCurrentAsync(tenantId, workspaceId, conversationId, token);
            if (role is null || !IsNamedActiveChannel(conversation) ||
                conversation!.Visibility != ConversationVisibility.PublicWithinScope)
            {
                return Result<WorkspaceChannelDirectoryItem>.Failure(Unavailable);
            }
            var member = await store.GetMemberForMutationAsync(tenantId, conversationId, userId, token);
            if (IsReadable(member)) return Result<WorkspaceChannelDirectoryItem>.Success(ToItem(conversation, member, role.Value.CanContribute()));
            // A removed or explicitly read-denied participant cannot self-restore.
            if (member is not null && (member.RemovedAt.HasValue || !member.LeftAt.HasValue || !member.CanRead))
            {
                return Result<WorkspaceChannelDirectoryItem>.Failure(Unavailable);
            }
            member = await AdmitAsync(tenantId, conversationId, userId, role.Value, member, token);
            await RecordAdmissionAsync(tenantId, workspaceId, conversationId, userId, userId, "WorkspaceChannelJoined", token);
            await unitOfWork.SaveChangesAsync(token);
            return Result<WorkspaceChannelDirectoryItem>.Success(ToItem(conversation, member, role.Value.CanContribute()));
        }, cancellationToken);
    }

    public async Task<Result> InviteAsync(Guid workspaceId, Guid conversationId, Guid recipientUserId,
        CancellationToken cancellationToken = default)
    {
        if (!TryScope(workspaceId, out var tenantId, out var userId) || conversationId == Guid.Empty ||
            recipientUserId == Guid.Empty || recipientUserId == userId ||
            await store.GetCurrentActorRoleAsync(tenantId, workspaceId, userId, cancellationToken) is null)
        {
            return Result.Failure(Unavailable);
        }
        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // Every admission path takes membership locks before the channel lock.
            foreach (var id in new[] { userId, recipientUserId }.OrderBy(id => id))
            {
                await unitOfWork.LockInviteAcceptanceMembershipsAsync(tenantId, workspaceId, id, token);
            }
            await store.LockAsync(tenantId, workspaceId, conversationId, token);
            var actorRole = await store.GetCurrentActorRoleAsync(tenantId, workspaceId, userId, token);
            var recipientRole = await store.GetCurrentActorRoleAsync(tenantId, workspaceId, recipientUserId, token);
            var conversation = await store.GetCurrentAsync(tenantId, workspaceId, conversationId, token);
            var manager = await store.GetMemberForMutationAsync(tenantId, conversationId, userId, token);
            if (actorRole is null || !actorRole.Value.CanContribute() || recipientRole is null ||
                !IsNamedActiveChannel(conversation) || !IsReadable(manager) ||
                (manager!.Role != ConversationMemberRole.Admin && !manager.CanManageMembers))
            {
                return Result.Failure(Unavailable);
            }
            var member = await store.GetMemberForMutationAsync(tenantId, conversationId, recipientUserId, token);
            if (IsReadable(member)) return Result.Success();
            await AdmitAsync(tenantId, conversationId, recipientUserId, recipientRole.Value, member, token);
            await RecordAdmissionAsync(tenantId, workspaceId, conversationId, userId, recipientUserId,
                "WorkspaceChannelMemberAdded", token);
            await unitOfWork.SaveChangesAsync(token);
            return Result.Success();
        }, cancellationToken);
    }

    private async Task<ConversationMember> AdmitAsync(Guid tenantId, Guid conversationId, Guid userId,
        WorkspaceRole role, ConversationMember? member, CancellationToken cancellationToken)
    {
        if (member is null)
        {
            member = new ConversationMember { TenantId = tenantId, ConversationId = conversationId, UserId = userId };
            await messaging.AddMemberAsync(member, cancellationToken);
        }
        member.Role = role == WorkspaceRole.ReadOnly ? ConversationMemberRole.ReadOnly : ConversationMemberRole.Member;
        member.CanRead = true;
        member.CanPost = role != WorkspaceRole.ReadOnly;
        member.CanCreateThread = role != WorkspaceRole.ReadOnly;
        member.CanManageMembers = false;
        member.JoinedAt = clock.UtcNow;
        member.LeftAt = null;
        member.RemovedAt = null;
        member.RemovedByUserId = null;
        return member;
    }

    private async Task RecordAdmissionAsync(Guid tenantId, Guid workspaceId, Guid conversationId,
        Guid actorUserId, Guid affectedUserId, string action, CancellationToken cancellationToken)
    {
        await audit.LogAsync(new AuditLogEntry(actorUserId, action, "Conversation", conversationId,
            WorkspaceId: workspaceId, TenantId: tenantId), cancellationToken);
        await authorizationChanges.PublishAsync(tenantId, affectedUserId, "conversation", conversationId, "granted", cancellationToken);
    }

    private static bool CanCreate(WorkspaceRole? role) => role is WorkspaceRole.Owner or WorkspaceRole.Admin;

    private static bool IsValidName(string name) => name.Length is > 0 and <= 200 &&
        !string.Equals(name, "general", StringComparison.OrdinalIgnoreCase) && !name.Any(char.IsControl);

    private static bool TryVisibility(string? value, out ConversationVisibility visibility)
    {
        visibility = value == "Private" ? ConversationVisibility.Private : ConversationVisibility.PublicWithinScope;
        return value is "PublicWithinScope" or "Private";
    }

    private bool TryScope(Guid workspaceId, out Guid tenantId, out Guid userId)
    {
        tenantId = Guid.Empty;
        userId = Guid.Empty;
        if (workspaceId == Guid.Empty || !currentTenant.IsAvailable || currentTenant.IsPlatformScope ||
            !CurrentUserIdentity.TryGetAuthenticatedUserId(currentUser, out userId)) return false;
        tenantId = currentTenant.TenantId;
        return tenantId != Guid.Empty;
    }

    private static bool IsNamedActiveChannel(Conversation? conversation) =>
        conversation is { IsArchived: false, IsLocked: false, DefaultKind: null } &&
        conversation.Visibility is ConversationVisibility.PublicWithinScope or ConversationVisibility.Private;

    private static bool IsReadable(ConversationMember? member) => member is { CanRead: true, LeftAt: null, RemovedAt: null };

    private static WorkspaceChannelDirectoryItem ToItem(Conversation conversation, ConversationMember? member,
        bool workspaceMayManage = true)
    {
        var readable = IsReadable(member);
        return new(conversation.Id, conversation.WorkspaceId, conversation.Title ?? string.Empty,
            conversation.Visibility?.ToString() ?? "ParticipantOnly", readable,
            !readable && IsNamedActiveChannel(conversation) && conversation.Visibility == ConversationVisibility.PublicWithinScope &&
                (member is null || (member.LeftAt.HasValue && !member.RemovedAt.HasValue && member.CanRead)),
            workspaceMayManage && readable && IsNamedActiveChannel(conversation) &&
                (member!.Role == ConversationMemberRole.Admin || member.CanManageMembers),
            conversation.DefaultKind.HasValue);
    }

    private sealed class ChannelAdmissionChangedException : Exception { }
}
