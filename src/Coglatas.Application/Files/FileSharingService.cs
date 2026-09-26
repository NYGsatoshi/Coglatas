using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Realtime;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Files;

/// <summary>
/// Owns the authoritative sharing policy for direct Workspace attachments.
/// The browser receives a projection of this decision but never determines a
/// File's access state or whether a recipient is eligible.
/// </summary>
public sealed class FileSharingService(
    IFileAccessGrantRepository grants,
    IFileAuthorizationService fileAuthorization,
    IWorkspaceAuthorizationService workspaceAuthorization,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IClock clock,
    IAuditLogger audit,
    IBusinessInvalidationPublisher invalidations,
    IUnitOfWork unitOfWork) : IFileSharingService
{
    public async Task<IReadOnlyDictionary<Guid, FileSharingPresentation>> GetListPresentationsAsync(
        Guid workspaceId,
        Guid actorUserId,
        IReadOnlyCollection<FileObject> files,
        CancellationToken cancellationToken = default)
    {
        var fileIds = files
            .Where(file => file.Id != Guid.Empty && file.WorkspaceId == workspaceId)
            .Select(file => file.Id)
            .Distinct()
            .ToArray();
        if (fileIds.Length == 0)
        {
            return new Dictionary<Guid, FileSharingPresentation>();
        }

        var canManage = await workspaceAuthorization.CanManageWorkspace(
            actorUserId,
            workspaceId,
            cancellationToken);
        var summaries = await grants.GetEffectiveSummariesAsync(fileIds, cancellationToken);

        return files
            .Where(file => fileIds.Contains(file.Id))
            .ToDictionary(
                file => file.Id,
                file => ToPresentation(
                    file,
                    summaries.GetValueOrDefault(file.Id),
                    canManage));
    }

    public async Task<Result<FileSharingResponse>> GetAsync(
        Guid fileObjectId,
        CancellationToken cancellationToken = default)
    {
        if (!TryCurrentActor(out var actorUserId) || fileObjectId == Guid.Empty)
        {
            return NotFound();
        }

        var attachment = await grants.GetWorkspaceAttachmentAsync(fileObjectId, cancellationToken);
        if (attachment is null || !IsCurrentTenant(attachment))
        {
            return NotFound();
        }

        var canManage = await workspaceAuthorization.CanManageWorkspace(
            actorUserId,
            attachment.WorkspaceId,
            cancellationToken);
        if (!canManage && !await fileAuthorization.CanViewAttachment(actorUserId, attachment, cancellationToken))
        {
            return NotFound();
        }

        return Result<FileSharingResponse>.Success(
            await BuildResponseAsync(attachment, canManage, cancellationToken));
    }

    public async Task<Result<FileSharingResponse>> UpdatePolicyAsync(
        Guid fileObjectId,
        FileSharingPolicyUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var managed = await ManagedAttachmentAsync(fileObjectId, cancellationToken);
        if (managed.Attachment is null)
        {
            return NotFound();
        }

        var (attachment, actorUserId) = managed;
        if (!HasExpectedVersion(attachment.FileObject!, request.ExpectedSharingVersion))
        {
            return Stale();
        }

        var nextPolicy = request.ShareWithWorkspace
            ? FileSharingPolicy.Workspace
            : FileSharingPolicy.Private;
        if (attachment.FileObject!.SharingPolicy == nextPolicy)
        {
            return Result<FileSharingResponse>.Success(
                await BuildResponseAsync(attachment, canManage: true, cancellationToken));
        }

        attachment.FileObject.SharingPolicy = nextPolicy;
        attachment.FileObject.SharingVersion = NextVersion(attachment.FileObject.SharingVersion);
        return await SaveAndProjectAsync(attachment, actorUserId, "policyChanged", cancellationToken);
    }

    public async Task<Result<FileSharingResponse>> GrantAsync(
        Guid fileObjectId,
        FileShareGrantCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var managed = await ManagedAttachmentAsync(fileObjectId, cancellationToken);
        if (managed.Attachment is null)
        {
            return NotFound();
        }

        var (attachment, actorUserId) = managed;
        if (request.RecipientUserId == Guid.Empty)
        {
            return Invalid("recipientUserId", "A recipient is required.");
        }
        if (!HasExpectedVersion(attachment.FileObject!, request.ExpectedSharingVersion))
        {
            return Stale();
        }

        // Recipient eligibility is derived from canonical current memberships;
        // callers cannot use a display name, email, or cached member list as a
        // substitute for this server-side check.
        var candidate = await grants.FindEligibleRecipientAsync(
            attachment.WorkspaceId,
            request.RecipientUserId,
            cancellationToken);
        if (candidate is null)
        {
            return NotFound();
        }

        var existing = await grants.GetActiveGrantForRecipientAsync(
            attachment.FileObjectId,
            candidate.UserId,
            cancellationToken);
        if (existing is not null && existing.WorkspaceId != attachment.WorkspaceId)
        {
            return NotFound();
        }

        if (existing is not null && existing.RecipientKind == candidate.RecipientKind)
        {
            return Result<FileSharingResponse>.Success(
                await BuildResponseAsync(attachment, canManage: true, cancellationToken));
        }

        if (existing is null)
        {
            await grants.AddAsync(new FileAccessGrant
            {
                TenantId = attachment.TenantId,
                WorkspaceId = attachment.WorkspaceId,
                FileObjectId = attachment.FileObjectId,
                RecipientUserId = candidate.UserId,
                RecipientKind = candidate.RecipientKind,
                GrantedByUserId = actorUserId
            }, cancellationToken);
        }
        else
        {
            // A user can move from an external Project boundary to an active
            // Workspace member (or the reverse). Keep the persisted grant but
            // update its recorded kind so later effective checks remain exact.
            existing.RecipientKind = candidate.RecipientKind;
            existing.GrantedByUserId = actorUserId;
        }

        attachment.FileObject!.SharingVersion = NextVersion(attachment.FileObject.SharingVersion);
        return await SaveAndProjectAsync(attachment, actorUserId, "recipientGranted", cancellationToken);
    }

    public async Task<Result<FileSharingResponse>> RevokeAsync(
        Guid fileObjectId,
        Guid grantId,
        long expectedSharingVersion,
        CancellationToken cancellationToken = default)
    {
        var managed = await ManagedAttachmentAsync(fileObjectId, cancellationToken);
        if (managed.Attachment is null || grantId == Guid.Empty)
        {
            return NotFound();
        }

        var (attachment, actorUserId) = managed;
        if (!HasExpectedVersion(attachment.FileObject!, expectedSharingVersion))
        {
            return Stale();
        }

        var grant = await grants.GetActiveGrantAsync(attachment.FileObjectId, grantId, cancellationToken);
        if (grant is null ||
            grant.TenantId != attachment.TenantId ||
            grant.WorkspaceId != attachment.WorkspaceId)
        {
            return NotFound();
        }

        grant.RevokedAt = clock.UtcNow;
        grant.RevokedByUserId = actorUserId;
        attachment.FileObject!.SharingVersion = NextVersion(attachment.FileObject.SharingVersion);
        return await SaveAndProjectAsync(attachment, actorUserId, "recipientRevoked", cancellationToken);
    }

    private async Task<(Attachment? Attachment, Guid ActorUserId)> ManagedAttachmentAsync(
        Guid fileObjectId,
        CancellationToken cancellationToken)
    {
        if (!TryCurrentActor(out var actorUserId) || fileObjectId == Guid.Empty)
        {
            return (null, Guid.Empty);
        }

        var attachment = await grants.GetWorkspaceAttachmentAsync(fileObjectId, cancellationToken);
        if (attachment is null || !IsCurrentTenant(attachment) ||
            !await workspaceAuthorization.CanManageWorkspace(actorUserId, attachment.WorkspaceId, cancellationToken))
        {
            return (null, Guid.Empty);
        }

        return (attachment, actorUserId);
    }

    private async Task<Result<FileSharingResponse>> SaveAndProjectAsync(
        Attachment attachment,
        Guid actorUserId,
        string change,
        CancellationToken cancellationToken)
    {
        var file = attachment.FileObject!;
        await audit.LogAsync(new AuditLogEntry(
            actorUserId,
            "FileSharingChanged",
            "FileObject",
            file.Id,
            "File sharing changed.",
            WorkspaceId: attachment.WorkspaceId,
            Metadata: new Dictionary<string, object?>
            {
                ["sharingVersion"] = file.SharingVersion,
                ["accessState"] = file.SharingPolicy.ToString(),
                ["change"] = change
            },
            TenantId: attachment.TenantId), cancellationToken);
        await invalidations.FileChangedAsync(file, attachment, actorUserId, "sharingChanged", cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            return Stale();
        }

        return Result<FileSharingResponse>.Success(
            await BuildResponseAsync(attachment, canManage: true, cancellationToken));
    }

    private async Task<FileSharingResponse> BuildResponseAsync(
        Attachment attachment,
        bool canManage,
        CancellationToken cancellationToken)
    {
        var file = attachment.FileObject!;
        var summaries = await grants.GetEffectiveSummariesAsync([file.Id], cancellationToken);
        var summary = summaries.GetValueOrDefault(file.Id);
        var presentation = ToPresentation(file, summary, canManage);

        if (!canManage)
        {
            return new FileSharingResponse(
                file.Id,
                presentation.AccessState,
                file.SharingPolicy.ToString(),
                file.SharingVersion,
                CanInspectSharing: false,
                CanManageSharing: false,
                ExternalRecipientCount: null,
                Recipients: [],
                AvailableRecipients: []);
        }

        var recipients = await grants.ListEffectiveRecipientsAsync(file.Id, cancellationToken);
        var candidates = await grants.ListEligibleRecipientsAsync(attachment.WorkspaceId, cancellationToken);
        return new FileSharingResponse(
            file.Id,
            presentation.AccessState,
            file.SharingPolicy.ToString(),
            file.SharingVersion,
            CanInspectSharing: true,
            CanManageSharing: true,
            presentation.ExternalRecipientCount,
            recipients.Select(recipient => new FileShareRecipientResponse(
                recipient.GrantId,
                recipient.DisplayName,
                recipient.RecipientKind.ToString())).ToList(),
            candidates.Select(candidate => new FileShareRecipientCandidateResponse(
                candidate.UserId,
                candidate.DisplayName,
                candidate.RecipientKind.ToString())).ToList());
    }

    private static FileSharingPresentation ToPresentation(
        FileObject file,
        FileAccessGrantSummary? summary,
        bool canManage)
    {
        var hasExternalRecipients = (summary?.ExternalRecipientCount ?? 0) > 0;
        var state = hasExternalRecipients
            ? "External"
            : file.SharingPolicy == FileSharingPolicy.Workspace
                ? "Workspace"
                : "Private";
        return new FileSharingPresentation(
            state,
            canManage && hasExternalRecipients ? summary!.ExternalRecipientCount : null,
            canManage,
            file.SharingVersion);
    }

    private bool TryCurrentActor(out Guid userId)
    {
        userId = currentUser.UserId ?? Guid.Empty;
        return currentTenant.IsAvailable && currentUser.IsAuthenticated && userId != Guid.Empty;
    }

    private bool IsCurrentTenant(Attachment? attachment) =>
        attachment is { FileObject: not null } &&
        currentTenant.IsAvailable &&
        attachment.TenantId == currentTenant.TenantId &&
        attachment.FileObject.TenantId == currentTenant.TenantId &&
        attachment.FileObject.WorkspaceId == attachment.WorkspaceId;

    private static bool HasExpectedVersion(FileObject file, long expectedVersion) =>
        expectedVersion > 0 && file.SharingVersion == expectedVersion;

    private static long NextVersion(long version) => checked(Math.Max(version, 0) + 1);

    private static bool IsConcurrencyConflict(Exception exception) =>
        exception.GetType().Name == "DbUpdateConcurrencyException" ||
        exception.InnerException is not null && IsConcurrencyConflict(exception.InnerException);

    private static Result<FileSharingResponse> NotFound() =>
        Result<FileSharingResponse>.Failure(new ApplicationErrorDetail(
            "FILE_NOT_FOUND",
            "File not found."));

    private static Result<FileSharingResponse> Stale() =>
        Result<FileSharingResponse>.Failure(new ApplicationErrorDetail(
            "FILE_SHARING_STALE",
            "File sharing changed. Refresh and try again."));

    private static Result<FileSharingResponse> Invalid(string target, string message) =>
        Result<FileSharingResponse>.Failure(new ApplicationErrorDetail(
            "FILE_SHARING_VALIDATION",
            message,
            Target: target));
}
