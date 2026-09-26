using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Projects;

public interface IProjectGeneralMembershipSynchronizer
{
    Task<Result> StageAsync(
        Project project,
        ProjectMember? member,
        Guid affectedUserId,
        ProjectRole? previousRole,
        bool isCurrentMember,
        Guid actorUserId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Stages ProjectGeneral participant changes in the caller-owned Project
/// membership transaction. It never saves by itself and never materializes
/// broad WorkspaceVisible viewers as Conversation members.
/// </summary>
public sealed class ProjectGeneralMembershipSynchronizer(
    IDefaultConversationStore conversations,
    ICurrentTenant currentTenant,
    IClock clock,
    IAuthorizationStateChangePublisher authorizationChanges) : IProjectGeneralMembershipSynchronizer
{
    public async Task<Result> StageAsync(
        Project project,
        ProjectMember? member,
        Guid affectedUserId,
        ProjectRole? previousRole,
        bool isCurrentMember,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (project.Id == Guid.Empty ||
            project.WorkspaceId == Guid.Empty ||
            affectedUserId == Guid.Empty ||
            actorUserId == Guid.Empty ||
            currentTenant is not { IsAvailable: true, IsPlatformScope: false } ||
            project.TenantId != currentTenant.TenantId)
        {
            return Failure("InvalidProjectGeneral", "ProjectGeneral synchronization scope is invalid.");
        }

        var conversation = await conversations.FindDefaultAsync(
            project.WorkspaceId,
            project.Id,
            ConversationDefaultKind.ProjectGeneral,
            cancellationToken);

        if (conversation is null)
        {
            // A canonical Activated Project must have committed ProjectGeneral
            // during activation. Missing operational state is corruption and
            // member mutation must fail closed rather than silently continuing.
            return project.ActivationState == ProjectActivationState.Activated
                ? Failure("InvalidProjectGeneral", "Activated Project is missing its canonical ProjectGeneral Conversation.")
                : Result.Success();
        }

        if (!IsCanonicalProjectGeneral(conversation, project))
        {
            return Failure("InvalidProjectGeneral", "Existing ProjectGeneral Conversation identity is inconsistent.");
        }

        var participant = await conversations.GetMemberAsync(
            conversation.Id,
            affectedUserId,
            cancellationToken);

        if (!isCurrentMember)
        {
            if (participant is not null && !ShouldPreserveIndependentAdmin(project, participant, affectedUserId, previousRole))
            {
                participant.CanRead = false;
                participant.CanPost = false;
                participant.CanManageMembers = false;
                participant.CanCreateThread = false;
                participant.RemovedAt ??= clock.UtcNow;
                participant.RemovedByUserId = actorUserId;
            }

            await authorizationChanges.PublishAsync(
                project.TenantId,
                affectedUserId,
                "conversation",
                conversation.Id,
                "revoked",
                cancellationToken);
            return Result.Success();
        }

        if (member is null || member.ProjectId != project.Id || member.UserId != affectedUserId)
        {
            return Failure("InvalidProjectGeneral", "Current Project member state is unavailable for synchronization.");
        }

        var desiredRole = DesiredConversationRole(project, member);
        if (participant is null)
        {
            await conversations.AddMemberAsync(
                NewParticipant(project, conversation.Id, affectedUserId, desiredRole),
                cancellationToken);
        }
        else
        {
            RestoreParticipant(project, participant, affectedUserId, previousRole, desiredRole);
        }

        await authorizationChanges.PublishAsync(
            project.TenantId,
            affectedUserId,
            "conversation",
            conversation.Id,
            "membershipChanged",
            cancellationToken);
        return Result.Success();
    }

    private ConversationMember NewParticipant(
        Project project,
        Guid conversationId,
        Guid userId,
        ConversationMemberRole role)
    {
        var readOnly = role == ConversationMemberRole.ReadOnly;
        var admin = role == ConversationMemberRole.Admin;
        return new ConversationMember
        {
            TenantId = project.TenantId,
            ConversationId = conversationId,
            UserId = userId,
            Role = role,
            CanRead = true,
            CanPost = !readOnly,
            CanManageMembers = admin,
            CanCreateThread = !readOnly,
            JoinedAt = clock.UtcNow
        };
    }

    private static void RestoreParticipant(
        Project project,
        ConversationMember participant,
        Guid userId,
        ProjectRole? previousRole,
        ConversationMemberRole desiredRole)
    {
        participant.LeftAt = null;
        participant.RemovedAt = null;
        participant.RemovedByUserId = null;
        participant.CanRead = true;

        var preserveIndependentAdmin =
            participant.Role == ConversationMemberRole.Admin &&
            desiredRole != ConversationMemberRole.Admin &&
            !WasProjectDerivedAdmin(project, userId, previousRole);
        if (preserveIndependentAdmin || desiredRole == ConversationMemberRole.Admin)
        {
            participant.Role = ConversationMemberRole.Admin;
            participant.CanPost = true;
            participant.CanManageMembers = true;
            participant.CanCreateThread = true;
            return;
        }

        var readOnly = desiredRole == ConversationMemberRole.ReadOnly;
        participant.Role = desiredRole;
        participant.CanPost = !readOnly;
        participant.CanManageMembers = false;
        participant.CanCreateThread = !readOnly;
    }

    private static bool ShouldPreserveIndependentAdmin(
        Project project,
        ConversationMember participant,
        Guid userId,
        ProjectRole? previousRole) =>
        participant.Role == ConversationMemberRole.Admin &&
        !WasProjectDerivedAdmin(project, userId, previousRole);

    private static bool WasProjectDerivedAdmin(Project project, Guid userId, ProjectRole? previousRole) =>
        previousRole == ProjectRole.Owner ||
        userId == project.OwnerUserId ||
        userId == project.CreatedByUserId;

    private static ConversationMemberRole DesiredConversationRole(Project project, ProjectMember member) =>
        member.Role == ProjectRole.Owner ||
        member.UserId == project.OwnerUserId ||
        member.UserId == project.CreatedByUserId
            ? ConversationMemberRole.Admin
            : member.Role == ProjectRole.Viewer
                ? ConversationMemberRole.ReadOnly
                : ConversationMemberRole.Member;

    private static bool IsCanonicalProjectGeneral(Conversation conversation, Project project) =>
        conversation.TenantId == project.TenantId &&
        conversation.WorkspaceId == project.WorkspaceId &&
        conversation.ProjectId == project.Id &&
        conversation.Type == ConversationType.ProjectChannel &&
        conversation.Title == "general" &&
        conversation.Visibility == ConversationVisibility.PublicWithinScope &&
        conversation.DefaultKind == ConversationDefaultKind.ProjectGeneral;

    private static Result Failure(string code, string message) =>
        Result.Failure(new ApplicationErrorDetail(code, message));
}
