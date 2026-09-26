using Coglatas.Application.Channels;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Messaging;
using Coglatas.Application.Projects;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Files;

public sealed class FileAuthorizationService(
    IFileRepository files,
    IProjectAuthorizationService projects,
    IConversationAuthorizationService conversations,
    IChannelAuthorizationService channels,
    IWorkspaceAuthorizationService workspaces,
    IFileAccessGrantRepository? accessGrants = null) : IFileAuthorizationService
{
    public async Task<bool> CanUploadAttachment(Guid userId, AttachmentOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default)
    {
        var owner = await files.ResolveOwnerAsync(ownerType, ownerId, cancellationToken);
        if (owner is null)
        {
            return false;
        }

        if (owner.ProjectId.HasValue)
        {
            // Project visibility is a read/discovery input. File mutation must
            // use the explicit Project contribution boundary.
            return await projects.CanContributeProject(userId, owner.ProjectId.Value, cancellationToken);
        }

        if (owner.ConversationId.HasValue)
        {
            return await conversations.CanSendMessage(userId, owner.ConversationId.Value, cancellationToken);
        }

        if (owner.ChannelId.HasValue)
        {
            return await channels.CanPostToChannel(userId, owner.ChannelId.Value, cancellationToken);
        }

        return await workspaces.CanContributeWorkspace(userId, owner.WorkspaceId, cancellationToken);
    }

    public Task<bool> CanViewWorkspaceFiles(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        return workspaces.CanViewWorkspace(userId, workspaceId, cancellationToken);
    }

    public async Task<bool> CanViewAttachment(Guid userId, Attachment attachment, CancellationToken cancellationToken = default)
    {
        if (attachment.DeletedAt.HasValue || !attachment.OwnerType.HasValue || !attachment.OwnerId.HasValue)
        {
            return false;
        }

        var owner = await files.ResolveOwnerAsync(attachment.OwnerType.Value, attachment.OwnerId.Value, cancellationToken);
        if (owner is null)
        {
            return false;
        }

        if (owner.ProjectId.HasValue)
        {
            return await projects.CanViewProject(userId, owner.ProjectId.Value, cancellationToken);
        }

        if (owner.ConversationId.HasValue)
        {
            return await conversations.CanViewConversation(userId, owner.ConversationId.Value, cancellationToken);
        }

        if (owner.ChannelId.HasValue)
        {
            return await channels.CanViewChannel(userId, owner.ChannelId.Value, cancellationToken);
        }

        if (attachment.OwnerType == AttachmentOwnerType.Workspace &&
            attachment.OwnerId == attachment.WorkspaceId &&
            attachment.FileObject is { } file &&
            file.WorkspaceId == owner.WorkspaceId)
        {
            // An effective explicit grant is re-evaluated against the current
            // recipient membership boundary by the repository. It is the only
            // path by which a Project-scoped external user can read a direct
            // Workspace attachment.
            if (accessGrants is not null &&
                await accessGrants.HasEffectiveGrantAsync(file.Id, userId, cancellationToken))
            {
                return true;
            }

            if (!await workspaces.CanViewWorkspace(userId, owner.WorkspaceId, cancellationToken))
            {
                return false;
            }

            if (file.SharingPolicy == FileSharingPolicy.Workspace ||
                attachment.UploadedByUserId == userId ||
                attachment.OwnerUserId == userId)
            {
                return true;
            }

            // Workspace managers can inspect and repair a File's sharing
            // policy, but that governance path remains server-authorized.
            return await workspaces.CanManageWorkspace(userId, owner.WorkspaceId, cancellationToken);
        }

        return await workspaces.CanViewWorkspace(userId, owner.WorkspaceId, cancellationToken);
    }

    public Task<bool> CanDownloadAttachment(Guid userId, Attachment attachment, CancellationToken cancellationToken = default)
    {
        return CanViewAttachment(userId, attachment, cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> GetDeletableWorkspaceAttachmentIdsAsync(
        Guid userId,
        Guid workspaceId,
        IReadOnlyCollection<Attachment> attachments,
        CancellationToken cancellationToken = default)
    {
        if (workspaceId == Guid.Empty || attachments.Count == 0 ||
            !await workspaces.CanContributeWorkspace(userId, workspaceId, cancellationToken))
        {
            return new HashSet<Guid>();
        }

        // The Workspace inventory contains only Workspace-owned attachments.
        // Evaluate the shared current-owner boundary once, then preserve the
        // per-record identity rule used by CanDeleteAttachment. The mutation
        // command still performs its own complete reauthorization.
        return attachments
            .Where(attachment =>
                !attachment.DeletedAt.HasValue &&
                attachment.WorkspaceId == workspaceId &&
                attachment.OwnerType == AttachmentOwnerType.Workspace &&
                attachment.OwnerId == workspaceId &&
                (attachment.UploadedByUserId == userId || attachment.OwnerUserId == userId))
            .Select(attachment => attachment.Id)
            .ToHashSet();
    }

    public async Task<bool> CanDeleteAttachment(Guid userId, Attachment attachment, CancellationToken cancellationToken = default)
    {
        if (attachment.DeletedAt.HasValue || !attachment.OwnerType.HasValue || !attachment.OwnerId.HasValue)
        {
            return false;
        }

        var owner = await files.ResolveOwnerAsync(attachment.OwnerType.Value, attachment.OwnerId.Value, cancellationToken);
        if (owner is null || !await CanMutateOwnerAsync(userId, owner, cancellationToken))
        {
            // Historical uploader/owner identity is attribution, not a durable
            // capability. Current owner-scope authorization is always required.
            return false;
        }

        if (attachment.UploadedByUserId == userId || attachment.OwnerUserId == userId)
        {
            return true;
        }

        // Explicit Project managers may moderate another contributor's
        // attachment, but Workspace governance alone cannot bypass the current
        // Project contribution boundary enforced above.
        return owner.ProjectId.HasValue &&
               await projects.CanManageProject(userId, owner.ProjectId.Value, cancellationToken);
    }

    private async Task<bool> CanMutateOwnerAsync(
        Guid userId,
        FileOwnerContext owner,
        CancellationToken cancellationToken)
    {
        if (owner.ProjectId.HasValue)
        {
            return await projects.CanContributeProject(userId, owner.ProjectId.Value, cancellationToken);
        }

        if (owner.ConversationId.HasValue)
        {
            return await conversations.CanSendMessage(userId, owner.ConversationId.Value, cancellationToken);
        }

        if (owner.ChannelId.HasValue)
        {
            return await channels.CanPostToChannel(userId, owner.ChannelId.Value, cancellationToken);
        }

        return await workspaces.CanContributeWorkspace(userId, owner.WorkspaceId, cancellationToken);
    }
}
