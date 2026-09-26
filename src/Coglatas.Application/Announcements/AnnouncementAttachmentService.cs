using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Files;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Announcements;

/// <summary>
/// Recipient-safe file metadata for an Announcement. It contains no storage
/// key, signed URL, grant token, or capability. A short-lived download grant is
/// still requested through the canonical File API when the recipient acts.
/// </summary>
public sealed record AnnouncementAttachmentResponse(
    Guid AttachmentId,
    Guid FileObjectId,
    Guid WorkspaceId,
    string FileName,
    string ContentType,
    long SizeBytes);

public interface IAnnouncementAttachmentService
{
    /// <summary>
    /// Re-resolves one canonical Workspace-owned Attachment for the supplied
    /// actor. The same operation is used at draft save, transition acceptance,
    /// scheduled publication, and recipient rendering.
    /// </summary>
    Task<Result<AnnouncementAttachmentResponse>> ResolveAsync(
        Guid actorUserId,
        Guid? announcementWorkspaceId,
        Guid attachmentId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Announcement attachments intentionally use only the canonical Workspace
/// inventory. Group and Channel audiences are subsets of that Workspace; a
/// global Announcement has no Workspace boundary and therefore cannot carry a
/// file. Download use remains protected by FileService's expiring grant and
/// use-time reauthorization.
/// </summary>
public sealed class AnnouncementAttachmentService(
    IFileRepository files,
    IFileAuthorizationService fileAuthorization,
    IFeatureFlagService featureFlags,
    ICurrentTenant currentTenant) : IAnnouncementAttachmentService
{
    private const string UnavailableMessage = "Announcement attachment is unavailable or not authorized.";

    public async Task<Result<AnnouncementAttachmentResponse>> ResolveAsync(
        Guid actorUserId,
        Guid? announcementWorkspaceId,
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        if (!currentTenant.IsAvailable ||
            currentTenant.IsPlatformScope ||
            actorUserId == Guid.Empty ||
            !announcementWorkspaceId.HasValue ||
            attachmentId == Guid.Empty)
        {
            return Denied();
        }

        var feature = await featureFlags.RequireEnabledAsync(FeatureKeys.FileSharing, cancellationToken);
        if (!feature.IsSuccess)
        {
            return Denied();
        }

        var workspaceId = announcementWorkspaceId.Value;
        var attachment = await files.GetAttachmentAsync(attachmentId, cancellationToken);
        if (attachment is null ||
            attachment.TenantId != currentTenant.TenantId ||
            attachment.WorkspaceId != workspaceId ||
            attachment.DeletedAt.HasValue ||
            attachment.OwnerType != AttachmentOwnerType.Workspace ||
            attachment.OwnerId != workspaceId ||
            attachment.FileObject is null ||
            attachment.FileObject.TenantId != currentTenant.TenantId ||
            attachment.FileObject.WorkspaceId != workspaceId ||
            attachment.FileObject.ProjectId.HasValue ||
            attachment.FileObject.DeletedAt.HasValue ||
            attachment.FileObject.Status != FileObjectStatus.Active ||
            attachment.ScanStatus is FileScanStatus.Pending or FileScanStatus.Infected or FileScanStatus.Failed ||
            !attachment.FileObject.Classification.HasValue ||
            attachment.FileObject.Classification == DataClassification.UnknownSensitive)
        {
            return Denied();
        }

        var owner = await files.ResolveOwnerAsync(
            AttachmentOwnerType.Workspace,
            workspaceId,
            cancellationToken);
        if (owner is null ||
            owner.WorkspaceId != workspaceId ||
            owner.ProjectId.HasValue ||
            !await fileAuthorization.CanDownloadAttachment(actorUserId, attachment, cancellationToken))
        {
            return Denied();
        }

        return Result<AnnouncementAttachmentResponse>.Success(new AnnouncementAttachmentResponse(
            attachment.Id,
            attachment.FileObjectId,
            workspaceId,
            attachment.FileObject.OriginalFileName,
            attachment.FileObject.ContentType,
            attachment.FileObject.SizeBytes));
    }

    private static Result<AnnouncementAttachmentResponse> Denied() =>
        Result<AnnouncementAttachmentResponse>.Failure(UnavailableMessage);
}
