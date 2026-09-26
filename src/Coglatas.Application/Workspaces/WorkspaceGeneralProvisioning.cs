using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Workspaces;

public sealed class WorkspaceGeneralRequiredInitialization(
    IDefaultConversationStore conversations,
    ICurrentTenant currentTenant,
    IClock clock,
    IAuthorizationStateChangePublisher authorizationChanges) : IWorkspaceRequiredInitialization
{
    public bool IsAvailable => true;

    public async Task<Result> StageAsync(
        Workspace workspace,
        Guid creatorUserId,
        CancellationToken cancellationToken = default)
    {
        if (workspace.Id == Guid.Empty ||
            creatorUserId == Guid.Empty ||
            currentTenant is not { IsAvailable: true, IsPlatformScope: false } ||
            (workspace.TenantId != Guid.Empty && workspace.TenantId != currentTenant.TenantId))
        {
            return Result.Failure("Canonical Workspace initialization scope is invalid.");
        }

        var conversation = await conversations.FindDefaultAsync(
            workspace.Id,
            projectId: null,
            ConversationDefaultKind.WorkspaceGeneral,
            cancellationToken);

        if (conversation is null)
        {
            conversation = new Conversation
            {
                TenantId = currentTenant.TenantId,
                WorkspaceId = workspace.Id,
                ProjectId = null,
                Type = ConversationType.WorkspaceChannel,
                Title = "general",
                Visibility = ConversationVisibility.PublicWithinScope,
                DefaultKind = ConversationDefaultKind.WorkspaceGeneral,
                CreatedByUserId = creatorUserId
            };
            await conversations.AddConversationAsync(conversation, cancellationToken);
        }
        else if (!IsCanonicalWorkspaceGeneral(conversation, workspace.Id))
        {
            return Result.Failure("Existing Workspace default Conversation is not canonical.");
        }

        var creator = await conversations.GetMemberAsync(conversation.Id, creatorUserId, cancellationToken);
        if (creator is null)
        {
            creator = new ConversationMember
            {
                TenantId = currentTenant.TenantId,
                ConversationId = conversation.Id,
                UserId = creatorUserId,
                Role = ConversationMemberRole.Admin,
                CanRead = true,
                CanPost = true,
                CanManageMembers = true,
                CanCreateThread = true,
                JoinedAt = clock.UtcNow
            };
            await conversations.AddMemberAsync(creator, cancellationToken);
        }
        else
        {
            RestoreConversationAdmin(creator);
        }

        await authorizationChanges.PublishAsync(
            currentTenant.TenantId,
            creatorUserId,
            "conversation",
            conversation.Id,
            "granted",
            cancellationToken);
        return Result.Success();
    }

    private static bool IsCanonicalWorkspaceGeneral(Conversation conversation, Guid workspaceId) =>
        conversation.WorkspaceId == workspaceId &&
        conversation.ProjectId is null &&
        conversation.Type == ConversationType.WorkspaceChannel &&
        conversation.Title == "general" &&
        conversation.Visibility == ConversationVisibility.PublicWithinScope &&
        conversation.DefaultKind == ConversationDefaultKind.WorkspaceGeneral;

    private static void RestoreConversationAdmin(ConversationMember member)
    {
        member.Role = ConversationMemberRole.Admin;
        member.CanRead = true;
        member.CanPost = true;
        member.CanManageMembers = true;
        member.CanCreateThread = true;
        member.LeftAt = null;
        member.RemovedAt = null;
        member.RemovedByUserId = null;
    }
}

public interface IWorkspaceGeneralMembershipSynchronizer
{
    Task<Result> StageAsync(
        WorkspaceMember workspaceMember,
        Guid actorUserId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Mirrors current Workspace membership into the canonical WorkspaceGeneral
/// participant set without deriving Conversation Admin from Workspace role.
/// Legacy Workspaces with no canonical default are intentionally not repaired
/// implicitly; historical identity must not be guessed.
/// </summary>
public sealed class WorkspaceGeneralMembershipSynchronizer(
    IDefaultConversationStore conversations,
    ICurrentTenant currentTenant,
    IClock clock,
    IAuthorizationStateChangePublisher authorizationChanges,
    ITenantRepository? tenants = null) : IWorkspaceGeneralMembershipSynchronizer
{
    public async Task<Result> StageAsync(
        WorkspaceMember workspaceMember,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (workspaceMember.WorkspaceId == Guid.Empty ||
            workspaceMember.UserId == Guid.Empty ||
            currentTenant is not { IsAvailable: true, IsPlatformScope: false } ||
            (workspaceMember.TenantId != Guid.Empty && workspaceMember.TenantId != currentTenant.TenantId))
        {
            return Result.Failure("WorkspaceGeneral membership scope is invalid.");
        }

        // Production composition supplies ITenantRepository. Before an active
        // Workspace membership can become a Conversation participant, revalidate
        // the current Tenant membership and both parent records. Revocation must
        // remain possible after Tenant suspension, so inactive Workspace members
        // continue to the removal path below.
        if (workspaceMember.Status == MembershipStatus.Active && tenants is not null)
        {
            var tenantMembership = await tenants.GetTenantUserAsync(
                currentTenant.TenantId,
                workspaceMember.UserId,
                cancellationToken);
            if (tenantMembership is not
                {
                    Status: TenantUserStatus.Active,
                    User: { Status: UserStatus.Active, DeletedAt: null },
                    Tenant: { Status: TenantStatus.Active, DeletedAt: null }
                } ||
                tenantMembership.TenantId != currentTenant.TenantId ||
                tenantMembership.UserId != workspaceMember.UserId)
            {
                return Result.Failure(
                    "WorkspaceGeneral membership requires an active Tenant membership.");
            }
        }

        var conversation = await conversations.FindDefaultAsync(
            workspaceMember.WorkspaceId,
            projectId: null,
            ConversationDefaultKind.WorkspaceGeneral,
            cancellationToken);
        if (conversation is null)
        {
            // Legacy compatibility: do not fabricate a historical default from
            // the display name or infer that a pre-existing channel is general.
            return Result.Success();
        }

        if (conversation.Type != ConversationType.WorkspaceChannel ||
            conversation.ProjectId is not null ||
            conversation.Visibility != ConversationVisibility.PublicWithinScope)
        {
            return Result.Failure("WorkspaceGeneral identity is inconsistent.");
        }

        var participant = await conversations.GetMemberAsync(
            conversation.Id,
            workspaceMember.UserId,
            cancellationToken);

        if (workspaceMember.Status != MembershipStatus.Active)
        {
            if (participant is not null)
            {
                participant.CanRead = false;
                participant.CanPost = false;
                participant.CanManageMembers = false;
                participant.CanCreateThread = false;
                participant.RemovedAt ??= clock.UtcNow;
                participant.RemovedByUserId ??= actorUserId == Guid.Empty ? null : actorUserId;
            }

            await PublishAsync(workspaceMember.UserId, conversation.Id, "revoked", cancellationToken);
            return Result.Success();
        }

        if (participant is null)
        {
            participant = NewLeastPrivilegeParticipant(workspaceMember, conversation.Id);
            await conversations.AddMemberAsync(participant, cancellationToken);
        }
        else
        {
            RestoreActiveParticipant(participant, workspaceMember.Role);
        }

        await PublishAsync(workspaceMember.UserId, conversation.Id, "membershipChanged", cancellationToken);
        return Result.Success();
    }

    private ConversationMember NewLeastPrivilegeParticipant(
        WorkspaceMember workspaceMember,
        Guid conversationId)
    {
        var readOnly = workspaceMember.Role == WorkspaceRole.ReadOnly;
        return new ConversationMember
        {
            TenantId = currentTenant.TenantId,
            ConversationId = conversationId,
            UserId = workspaceMember.UserId,
            Role = readOnly ? ConversationMemberRole.ReadOnly : ConversationMemberRole.Member,
            CanRead = true,
            CanPost = !readOnly,
            CanManageMembers = false,
            CanCreateThread = !readOnly,
            JoinedAt = clock.UtcNow
        };
    }

    private static void RestoreActiveParticipant(
        ConversationMember participant,
        WorkspaceRole workspaceRole)
    {
        participant.LeftAt = null;
        participant.RemovedAt = null;
        participant.RemovedByUserId = null;
        participant.CanRead = true;

        // Explicit Conversation Admin is durable authority and is not derived
        // from, or silently removed by, Workspace governance role changes.
        if (participant.Role == ConversationMemberRole.Admin)
        {
            participant.CanPost = true;
            participant.CanManageMembers = true;
            participant.CanCreateThread = true;
            return;
        }

        var readOnly = workspaceRole == WorkspaceRole.ReadOnly;
        participant.Role = readOnly ? ConversationMemberRole.ReadOnly : ConversationMemberRole.Member;
        participant.CanPost = !readOnly;
        participant.CanManageMembers = false;
        participant.CanCreateThread = !readOnly;
    }

    private Task PublishAsync(
        Guid userId,
        Guid conversationId,
        string change,
        CancellationToken cancellationToken)
    {
        return authorizationChanges.PublishAsync(
            currentTenant.TenantId,
            userId,
            "conversation",
            conversationId,
            change,
            cancellationToken);
    }
}