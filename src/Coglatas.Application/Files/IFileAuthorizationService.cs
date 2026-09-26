using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Files;

public interface IFileAuthorizationService
{
    Task<bool> CanUploadAttachment(Guid userId, AttachmentOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default);

    Task<bool> CanViewWorkspaceFiles(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default);

    Task<bool> CanViewAttachment(Guid userId, Attachment attachment, CancellationToken cancellationToken = default);

    Task<bool> CanDownloadAttachment(Guid userId, Attachment attachment, CancellationToken cancellationToken = default);

    Task<IReadOnlySet<Guid>> GetDeletableWorkspaceAttachmentIdsAsync(
        Guid userId,
        Guid workspaceId,
        IReadOnlyCollection<Attachment> attachments,
        CancellationToken cancellationToken = default);

    Task<bool> CanDeleteAttachment(Guid userId, Attachment attachment, CancellationToken cancellationToken = default);
}
