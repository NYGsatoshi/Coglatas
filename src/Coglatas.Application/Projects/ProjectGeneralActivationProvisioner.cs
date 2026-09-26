using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Projects;

public interface IProjectGeneralActivationProvisioner
{
    Task<Result> StageAsync(
        Project project,
        Guid actorUserId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Stages the canonical ProjectGeneral Conversation inside the caller-owned
/// activation transaction. This provisioner never saves or commits by itself.
/// </summary>
public sealed class ProjectGeneralActivationProvisioner(
    IDefaultConversationStore conversations,
    IProjectRepository projects,
    ICurrentTenant currentTenant,
    IClock clock,
    IAuthorizationStateChangePublisher authorizationChanges) : IProjectGeneralActivationProvisioner
{
    public async Task<Result> StageAsync(
        Project project,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (project.Id == Guid.Empty ||
            project.WorkspaceId == Guid.Empty ||
            actorUserId == Guid.Empty ||
            currentTenant is not { IsAvailable: true, IsPlatformScope: false } ||
            project.TenantId != currentTenant.TenantId)
        {
            return Failure(
                "InvalidProjectGeneral",
                "Canonical Project activation scope is invalid.");
        }

        var members = await projects.ListMembersAsync(project.Id, cancellationToken);
        if (!members.Any(member => IsInitializationAdmin(project, member)))
        {
            // Canonical Project creation guarantees creator/Owner membership.
            // Never create an operational default Conversation with no explicit
            // initialization administrator when that invariant is broken.
            return Failure(
                "InvalidProjectGeneral",
                "ProjectGeneral requires an explicit Project Owner or creator participant.");
        }

        var conversation = await conversations.FindDefaultAsync(
            project.WorkspaceId,
            project.Id,
            ConversationDefaultKind.ProjectGeneral,
            cancellationToken);

        if (conversation is null)
        {
            conversation = new Conversation
            {
                TenantId = project.TenantId,
                WorkspaceId = project.WorkspaceId,
                ProjectId = project.Id,
                Type = ConversationType.ProjectChannel,
                Title = "general",
                Visibility = ConversationVisibility.PublicWithinScope,
                DefaultKind = ConversationDefaultKind.ProjectGeneral,
                CreatedByUserId = actorUserId
            };
            await conversations.AddConversationAsync(conversation, cancellationToken);
        }
        else if (!IsCanonicalProjectGeneral(conversation, project))
        {
            return Failure(
                "InvalidProjectGeneral",
                "Existing Project default Conversation is not canonical.");
        }

        foreach (var projectMember in members)
        {
            var desiredRole = DesiredConversationRole(project, projectMember);
            var participant = await conversations.GetMemberAsync(
                conversation.Id,
                projectMember.UserId,
                cancellationToken);

            if (participant is null)
            {
                participant = NewParticipant(project, conversation.Id, projectMember.UserId, desiredRole);
                await conversations.AddMemberAsync(participant, cancellationToken);
            }
            else
            {
                RestoreParticipant(participant, desiredRole);
            }

            await authorizationChanges.PublishAsync(
                project.TenantId,
                projectMember.UserId,
                "conversation",
                conversation.Id,
                "membershipChanged",
                cancellationToken);
        }

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
        ConversationMember participant,
        ConversationMemberRole desiredRole)
    {
        participant.LeftAt = null;
        participant.RemovedAt = null;
        participant.RemovedByUserId = null;
        participant.CanRead = true;

        // Preserve separately granted Conversation Admin authority. Project
        // governance roles do not silently revoke an explicit Messaging grant.
        if (participant.Role == ConversationMemberRole.Admin ||
            desiredRole == ConversationMemberRole.Admin)
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

    private static ConversationMemberRole DesiredConversationRole(
        Project project,
        ProjectMember member) =>
        IsInitializationAdmin(project, member)
            ? ConversationMemberRole.Admin
            : member.Role == ProjectRole.Viewer
                ? ConversationMemberRole.ReadOnly
                : ConversationMemberRole.Member;

    private static bool IsInitializationAdmin(Project project, ProjectMember member) =>
        member.Role == ProjectRole.Owner ||
        member.UserId == project.OwnerUserId ||
        member.UserId == project.CreatedByUserId;

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
