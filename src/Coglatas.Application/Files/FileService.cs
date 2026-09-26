using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Application.Realtime;
using Coglatas.Application.Workspaces;
using System.Security.Cryptography;
using System.Text;

namespace Coglatas.Application.Files;

public sealed class FileService(
    IFileRepository files,
    IFileDownloadGrantRepository downloadGrants,
    IFileStorageService storage,
    IFileAuthorizationService authorization,
    IFileUploadPolicy uploadPolicy,
    IFeatureFlagService featureFlags,
    IQuotaService quotaService,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IClock clock,
    IAuditLogger auditLogger,
    ITokenHasher tokenHasher,
    IBusinessInvalidationPublisher invalidations,
    IUnitOfWork unitOfWork,
    IFileSharingService? sharing = null,
    IWorkspaceAuthorizationService? workspaceAuthorization = null) : IFileService
    , IFileObjectService
{
    private static readonly TimeSpan FileDownloadGrantLifetime = TimeSpan.FromMinutes(10);
    private const int MaxFileListPageSize = 100;
    private const int MaxGrantPurposeLength = 200;

    public async Task<Result<AttachmentResponse>> UploadAsync(AttachmentUploadInput input, CancellationToken cancellationToken = default)
    {
        if (!currentTenant.IsAvailable)
        {
            return Result<AttachmentResponse>.Failure("A tenant context is required.");
        }

        var feature = await featureFlags.RequireEnabledAsync(FeatureKeys.FileSharing, cancellationToken);
        if (!feature.IsSuccess)
        {
            return Result<AttachmentResponse>.Failure(feature.Error!);
        }

        if (!TryCurrentUser(out var userId) ||
            !await authorization.CanUploadAttachment(userId, input.OwnerType, input.OwnerId, cancellationToken))
        {
            return Result<AttachmentResponse>.Failure("You are not allowed to upload an attachment for this resource.");
        }

        var validation = ValidateUpload(input);
        if (!validation.IsSuccess)
        {
            return Result<AttachmentResponse>.Failure(validation.Error!);
        }

        var quota = await quotaService.CanUploadFileAsync(currentTenant.TenantId, input.Length, cancellationToken);
        if (!quota.IsSuccess)
        {
            await auditLogger.LogAsync(new AuditLogEntry(userId, "FileUploadBlockedByQuota", "FileObject", null, quota.Error), cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<AttachmentResponse>.Failure(quota.Error!);
        }

        var owner = await files.ResolveOwnerAsync(input.OwnerType, input.OwnerId, cancellationToken);
        if (owner is null)
        {
            return Result<AttachmentResponse>.Failure("Attachment owner not found.");
        }

        var safeFileName = FileNameSanitizer.SanitizeOriginalFileName(input.OriginalFileName);
        var fileObject = new FileObject
        {
            TenantId = currentTenant.TenantId,
            WorkspaceId = owner.WorkspaceId,
            ProjectId = owner.ProjectId,
            UploadedByUserId = userId,
            OriginalFileName = safeFileName,
            ContentType = NormalizeContentType(input.ContentType),
            SizeBytes = input.Length,
            Classification = DataClassification.Private,
            Status = FileObjectStatus.Active
        };
        fileObject.StorageKey = CreateStorageKey(fileObject);

        var saved = await storage.SaveAsync(fileObject.StorageKey, input.Content, fileObject.ContentType, cancellationToken);
        if (!saved.IsSuccess)
        {
            return Result<AttachmentResponse>.Failure(saved.Error!);
        }

        var attachment = new Attachment
        {
            TenantId = currentTenant.TenantId,
            FileObjectId = fileObject.Id,
            WorkspaceId = owner.WorkspaceId,
            OwnerType = input.OwnerType,
            OwnerId = input.OwnerId,
            OwnerUserId = owner.AuthorUserId ?? userId,
            UploadedByUserId = userId,
            FileName = safeFileName,
            StoredFileName = fileObject.Id.ToString("N"),
            FilePath = fileObject.StorageKey,
            ContentType = fileObject.ContentType,
            Extension = Path.GetExtension(safeFileName).ToLowerInvariant(),
            SizeBytes = fileObject.SizeBytes,
            StorageProvider = "Configured",
            StorageKey = fileObject.StorageKey,
            ScanStatus = FileScanStatus.Skipped
        };

        try
        {
            await files.AddFileObjectAsync(fileObject, cancellationToken);
            await files.AddAttachmentAsync(attachment, cancellationToken);
            await auditLogger.LogAsync(new AuditLogEntry(
                userId,
                "FileUploaded",
                "FileObject",
                fileObject.Id,
                "File uploaded.",
                WorkspaceId: owner.WorkspaceId,
                ProjectId: owner.ProjectId), cancellationToken);
            await invalidations.FileChangedAsync(fileObject, attachment, userId, "uploaded", cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await TryDeleteStoredFileAsync(fileObject.StorageKey, CancellationToken.None);
            throw;
        }

        return Result<AttachmentResponse>.Success(ToResponse(attachment));
    }

    public async Task<Result<PagedResponse<FileListItemResponse>>> ListFileObjectsAsync(
        Guid workspaceId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (workspaceId == Guid.Empty)
        {
            return Result<PagedResponse<FileListItemResponse>>.Failure("Workspace is required.");
        }

        if (!TryCurrentUser(out var userId) ||
            !await authorization.CanViewWorkspaceFiles(userId, workspaceId, cancellationToken))
        {
            return Result<PagedResponse<FileListItemResponse>>.Failure("Workspace not found.");
        }

        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, MaxFileListPageSize);
        var canManageSharing = workspaceAuthorization is not null &&
                                await workspaceAuthorization.CanManageWorkspace(userId, workspaceId, cancellationToken);
        var result = await files.ListAccessibleWorkspaceFileObjectsAsync(
            workspaceId,
            userId,
            canManageSharing,
            safePage,
            safePageSize,
            cancellationToken);
        var deletableAttachmentIds = await authorization.GetDeletableWorkspaceAttachmentIdsAsync(
            userId,
            workspaceId,
            result.Items,
            cancellationToken);
        var presentations = sharing is null
            ? new Dictionary<Guid, FileSharingPresentation>()
            : await sharing.GetListPresentationsAsync(
                workspaceId,
                userId,
                result.Items
                    .Where(attachment => attachment.FileObject is not null)
                    .Select(attachment => attachment.FileObject!)
                    .ToArray(),
                cancellationToken);
        return Result<PagedResponse<FileListItemResponse>>.Success(new PagedResponse<FileListItemResponse>(
            result.Items
                .Select(attachment => ToFileListItemResponse(
                    attachment,
                    deletableAttachmentIds.Contains(attachment.Id),
                    presentations.GetValueOrDefault(attachment.FileObjectId)))
                .ToList(),
            result.Page,
            result.PageSize,
            result.TotalCount));
    }

    public async Task<Result<AttachmentResponse>> GetAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        var attachment = await files.GetAttachmentAsync(attachmentId, cancellationToken);
        if (attachment is null || !TryCurrentUser(out var userId))
        {
            return Result<AttachmentResponse>.Failure("Attachment not found.");
        }

        // A detail/list response is not a capability.  In particular, Task/File
        // associations must be checked again when the association is opened.
        var decision = await ValidateAttachmentForGrantAsync(userId, attachment, existingGrant: null, cancellationToken);
        if (!decision.IsAllowed)
        {
            await LogFileOpenDeniedAsync(userId, attachment, decision.DenialReason, cancellationToken);
            return Result<AttachmentResponse>.Failure("Attachment not found.");
        }

        return Result<AttachmentResponse>.Success(ToResponse(attachment));
    }

    public async Task<Result<FileDownloadResponse>> DownloadAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        var grant = await RequestDownloadGrantAsync(attachmentId, new FileDownloadGrantRequest("direct-download"), cancellationToken);
        return grant.IsSuccess
            ? await DownloadWithGrantAsync(grant.Value!.FileDownloadGrantId, grant.Value.Token, cancellationToken)
            : Result<FileDownloadResponse>.Failure(grant.Error!);
    }

    public async Task<Result<FileDownloadGrantResponse>> RequestDownloadGrantAsync(
        Guid attachmentId,
        FileDownloadGrantRequest request,
        CancellationToken cancellationToken = default)
    {
        var attachment = await files.GetAttachmentAsync(attachmentId, cancellationToken);
        if (attachment is null || !TryCurrentUser(out var userId))
        {
            return Result<FileDownloadGrantResponse>.Failure("Attachment not found.");
        }

        var decision = await ValidateAttachmentForGrantAsync(userId, attachment, existingGrant: null, cancellationToken);
        if (!decision.IsAllowed)
        {
            await LogFileGrantDeniedAsync(userId, attachment, null, "file_download.grant_issue_denied", "issue", decision.DenialReason, cancellationToken);
            return Result<FileDownloadGrantResponse>.Failure("Attachment not found.");
        }

        var token = CreateOpaqueToken();
        var grant = new FileDownloadGrant
        {
            TenantId = attachment.TenantId,
            ActorUserId = userId,
            WorkspaceId = attachment.WorkspaceId,
            FileObjectId = attachment.FileObjectId,
            AttachmentId = attachment.Id,
            TargetScopeType = attachment.OwnerType!.Value,
            TargetScopeId = attachment.OwnerId!.Value,
            Classification = attachment.FileObject!.Classification!.Value,
            AllowedOperation = "download",
            TokenHash = tokenHasher.HashToken(token),
            PolicyStamp = ComputeFilePolicyStamp(userId, attachment),
            Purpose = NormalizeGrantPurpose(request.Purpose),
            ExpiresAt = clock.UtcNow.Add(FileDownloadGrantLifetime)
        };

        await downloadGrants.AddAsync(grant, cancellationToken);
        await auditLogger.LogAsync(new AuditLogEntry(
            userId,
            "file_download.reauthorization_passed",
            "FileDownloadGrant",
            grant.Id,
            "File download grant issuance reauthorized.",
            WorkspaceId: grant.WorkspaceId,
            ProjectId: attachment.FileObject.ProjectId,
            Metadata: FileGrantAuditMetadata(grant, "allow", "reauthorization_passed", "issue"),
            TenantId: grant.TenantId), cancellationToken);
        await auditLogger.LogAsync(new AuditLogEntry(
            userId,
            "file_download.grant_issued",
            "FileDownloadGrant",
            grant.Id,
            "File download grant issued.",
            WorkspaceId: grant.WorkspaceId,
            ProjectId: attachment.FileObject.ProjectId,
            Metadata: FileGrantAuditMetadata(grant, "allow", "grant_issued", "issue"),
            TenantId: grant.TenantId), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<FileDownloadGrantResponse>.Success(ToGrantResponse(grant, token));
    }

    public async Task<Result<FileDownloadResponse>> DownloadWithGrantAsync(
        Guid fileDownloadGrantId,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId) || !currentTenant.IsAvailable)
        {
            return Result<FileDownloadResponse>.Failure("Attachment not found.");
        }

        var grant = await downloadGrants.GetAsync(fileDownloadGrantId, cancellationToken);
        if (grant is null)
        {
            return Result<FileDownloadResponse>.Failure("Attachment not found.");
        }

        if (grant.TenantId != currentTenant.TenantId)
        {
            await LogFileGrantDeniedAsync(userId, null, grant, "file_download.grant_use_denied", "download", "tenant_mismatch", cancellationToken);
            return Result<FileDownloadResponse>.Failure("Attachment not found.");
        }

        if (grant.ActorUserId != userId)
        {
            await LogFileGrantDeniedAsync(userId, null, grant, "file_download.grant_use_denied", "download", "actor_mismatch", cancellationToken);
            return Result<FileDownloadResponse>.Failure("Attachment not found.");
        }

        if (string.IsNullOrWhiteSpace(token) ||
            !string.Equals(grant.TokenHash, tokenHasher.HashToken(token), StringComparison.Ordinal))
        {
            await LogFileGrantDeniedAsync(userId, null, grant, "file_download.grant_use_denied", "download", "invalid_grant_token", cancellationToken);
            return Result<FileDownloadResponse>.Failure("Attachment not found.");
        }

        var attachment = await files.GetAttachmentAsync(grant.AttachmentId, cancellationToken);
        if (attachment is null)
        {
            await LogFileGrantDeniedAsync(userId, null, grant, "file_download.grant_use_denied", "download", "target_missing", cancellationToken);
            return Result<FileDownloadResponse>.Failure("Attachment not found.");
        }

        var decision = await ValidateAttachmentForGrantAsync(userId, attachment, grant, cancellationToken);
        if (!decision.IsAllowed)
        {
            await LogFileGrantDeniedAsync(userId, attachment, grant, "file_download.grant_use_denied", "download", decision.DenialReason, cancellationToken);
            return Result<FileDownloadResponse>.Failure("Attachment not found.");
        }

        var fileObject = attachment.FileObject ?? throw new InvalidOperationException("Validated attachment must include a file object.");
        var content = await storage.OpenReadAsync(fileObject.StorageKey, cancellationToken);
        grant.DownloadedAt = clock.UtcNow;
        await auditLogger.LogAsync(new AuditLogEntry(
            userId,
            "file_download.reauthorization_passed",
            "FileDownloadGrant",
            grant.Id,
            "File download grant use reauthorized.",
            WorkspaceId: attachment.WorkspaceId,
            ProjectId: fileObject.ProjectId,
            Metadata: FileGrantAuditMetadata(grant, "allow", "reauthorization_passed", "download"),
            TenantId: grant.TenantId), cancellationToken);
        await auditLogger.LogAsync(new AuditLogEntry(
            userId,
            "file_download.grant_used",
            "FileDownloadGrant",
            grant.Id,
            "File download grant used.",
            WorkspaceId: attachment.WorkspaceId,
            ProjectId: fileObject.ProjectId,
            Metadata: FileGrantAuditMetadata(grant, "allow", "grant_used", "download"),
            TenantId: grant.TenantId), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<FileDownloadResponse>.Success(new FileDownloadResponse(content, fileObject.OriginalFileName, fileObject.ContentType, fileObject.SizeBytes));
    }

    public async Task<Result> DeleteAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        var attachment = await files.GetAttachmentAsync(attachmentId, cancellationToken);
        if (attachment is null || !TryCurrentUser(out var userId) ||
            !await authorization.CanDeleteAttachment(userId, attachment, cancellationToken))
        {
            return Result.Failure("Attachment not found.");
        }

        return await DeleteAttachmentAsync(attachment, userId, "Attachment deleted.", cancellationToken);
    }

    public async Task<Result<FileObjectResponse>> GetFileObjectAsync(Guid fileObjectId, CancellationToken cancellationToken = default)
    {
        var attachment = await files.GetAttachmentByFileObjectAsync(fileObjectId, cancellationToken);
        if (attachment is null || attachment.FileObject is null || !TryCurrentUser(out var userId))
        {
            return Result<FileObjectResponse>.Failure("File not found.");
        }

        if (!await authorization.CanViewAttachment(userId, attachment, cancellationToken))
        {
            await LogDeniedFileAccessAsync(userId, "FileMetadataDenied", attachment, cancellationToken);
            return Result<FileObjectResponse>.Failure("File not found.");
        }

        if (attachment.FileObject.TenantId != currentTenant.TenantId)
        {
            return Result<FileObjectResponse>.Failure("File not found.");
        }

        FileSharingPresentation? presentation = null;
        if (sharing is not null &&
            attachment.OwnerType == AttachmentOwnerType.Workspace &&
            attachment.OwnerId == attachment.WorkspaceId)
        {
            var sharingResult = await sharing.GetAsync(fileObjectId, cancellationToken);
            if (sharingResult.IsSuccess)
            {
                presentation = new FileSharingPresentation(
                    sharingResult.Value!.AccessState,
                    sharingResult.Value.ExternalRecipientCount,
                    sharingResult.Value.CanManageSharing,
                    sharingResult.Value.SharingVersion);
            }
        }

        return Result<FileObjectResponse>.Success(ToFileObjectResponse(attachment.FileObject, presentation));
    }

    public async Task<Result<FileDownloadResponse>> DownloadFileObjectAsync(Guid fileObjectId, CancellationToken cancellationToken = default)
    {
        var attachment = await files.GetAttachmentByFileObjectAsync(fileObjectId, cancellationToken);
        return attachment is null
            ? Result<FileDownloadResponse>.Failure("File not found.")
            : await DownloadAsync(attachment.Id, cancellationToken);
    }

    public async Task<Result<FileDownloadGrantResponse>> RequestFileObjectDownloadGrantAsync(
        Guid fileObjectId,
        FileDownloadGrantRequest request,
        CancellationToken cancellationToken = default)
    {
        var attachment = await files.GetAttachmentByFileObjectAsync(fileObjectId, cancellationToken);
        return attachment is null
            ? Result<FileDownloadGrantResponse>.Failure("File not found.")
            : await RequestDownloadGrantAsync(attachment.Id, request, cancellationToken);
    }

    public Task<Result<FileDownloadResponse>> DownloadFileObjectWithGrantAsync(
        Guid fileDownloadGrantId,
        string token,
        CancellationToken cancellationToken = default)
    {
        return DownloadWithGrantAsync(fileDownloadGrantId, token, cancellationToken);
    }

    public async Task<Result> DeleteFileObjectAsync(Guid fileObjectId, string? reason = null, CancellationToken cancellationToken = default)
    {
        var attachment = await files.GetAttachmentByFileObjectAsync(fileObjectId, cancellationToken);
        if (attachment is null)
        {
            return Result.Failure("File not found.");
        }

        if (!TryCurrentUser(out var userId) || !await authorization.CanDeleteAttachment(userId, attachment, cancellationToken))
        {
            return Result.Failure("File not found.");
        }

        return await DeleteAttachmentAsync(attachment, userId, reason, cancellationToken);
    }

    private bool TryCurrentUser(out Guid userId)
    {
        userId = currentUser.UserId ?? Guid.Empty;
        return currentUser.IsAuthenticated && currentUser.UserId.HasValue;
    }

    public static AttachmentResponse ToResponse(Attachment attachment)
    {
        return new AttachmentResponse(
            attachment.Id,
            attachment.FileObjectId,
            attachment.OwnerType,
            attachment.OwnerId,
            attachment.FileObject?.OriginalFileName ?? attachment.FileName,
            attachment.FileObject?.ContentType ?? attachment.ContentType,
            attachment.FileObject?.SizeBytes ?? attachment.SizeBytes,
            attachment.UploadedByUserId,
            attachment.CreatedAt,
            attachment.FileObject?.DeletedAt ?? attachment.DeletedAt);
    }

    private Result ValidateUpload(AttachmentUploadInput input)
    {
        if (input.Length <= 0)
        {
            return Result.Failure("Empty files are not allowed.");
        }

        if (input.Length > uploadPolicy.MaxFileSizeBytes)
        {
            return Result.Failure($"File exceeds the maximum size of {uploadPolicy.MaxFileSizeBytes} bytes.");
        }

        var extension = Path.GetExtension(input.OriginalFileName).ToLowerInvariant();
        var allowed = uploadPolicy.AllowedExtensions.Select(item => item.ToLowerInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(extension) || !allowed.Contains(extension))
        {
            return Result.Failure("File extension is not allowed.");
        }

        var contentType = NormalizeContentType(input.ContentType);
        var allowedContentTypes = uploadPolicy.AllowedContentTypes
            .Select(item => item.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!allowedContentTypes.Contains(contentType))
        {
            return Result.Failure("File content type is not allowed.");
        }

        return Result.Success();
    }

    private async Task<Result> DeleteAttachmentAsync(Attachment attachment, Guid userId, string? reason, CancellationToken cancellationToken)
    {
        attachment.MarkDeleted(clock.UtcNow);
        attachment.FileObject?.MarkDeleted(clock.UtcNow, userId, reason);
        await auditLogger.LogAsync(new AuditLogEntry(userId, "FileDeleted", "FileObject", attachment.FileObjectId, "File soft-deleted.", WorkspaceId: attachment.WorkspaceId, ProjectId: attachment.FileObject?.ProjectId), cancellationToken);
        if (attachment.FileObject is not null)
        {
            await invalidations.FileChangedAsync(attachment.FileObject, attachment, userId, "deleted", cancellationToken);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task LogDeniedFileAccessAsync(Guid userId, string action, Attachment attachment, CancellationToken cancellationToken)
    {
        await auditLogger.LogAsync(new AuditLogEntry(
            userId,
            action,
            "FileObject",
            attachment.FileObjectId,
            "File access denied."), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task LogFileOpenDeniedAsync(
        Guid userId,
        Attachment attachment,
        string reason,
        CancellationToken cancellationToken)
    {
        var metadata = FileGrantDenialMetadata(userId, attachment, null, reason, "open");
        await auditLogger.LogSecurityAsync(
            "AccessDenied",
            "File metadata open denied.",
            metadata,
            SecurityEventSeverity.Warning,
            cancellationToken);
        await auditLogger.LogAsync(new AuditLogEntry(
            userId,
            "file_download.metadata_open_denied",
            "FileObject",
            attachment.FileObjectId,
            "File metadata open denied.",
            WorkspaceId: attachment.WorkspaceId,
            ProjectId: attachment.FileObject?.ProjectId,
            Metadata: metadata,
            TenantId: currentTenant.IsAvailable ? currentTenant.TenantId : attachment.TenantId), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task TryDeleteStoredFileAsync(string storageKey, CancellationToken cancellationToken)
    {
        try
        {
            await storage.DeleteAsync(storageKey, cancellationToken);
        }
        catch
        {
            // The original persistence/audit failure is the one callers need to see.
        }
    }

    private async Task<FileGrantDecision> ValidateAttachmentForGrantAsync(
        Guid userId,
        Attachment attachment,
        FileDownloadGrant? existingGrant,
        CancellationToken cancellationToken)
    {
        if (!currentTenant.IsAvailable ||
            attachment.TenantId != currentTenant.TenantId ||
            attachment.FileObject is null ||
            attachment.FileObject.TenantId != currentTenant.TenantId)
        {
            return FileGrantDecision.Deny("tenant_mismatch");
        }

        if (existingGrant is not null)
        {
            if (!string.Equals(existingGrant.AllowedOperation, "download", StringComparison.OrdinalIgnoreCase))
            {
                return FileGrantDecision.Deny("operation_mismatch");
            }

            if (clock.UtcNow >= existingGrant.ExpiresAt)
            {
                return FileGrantDecision.Deny("grant_expired");
            }

            if (existingGrant.RevokedAt.HasValue)
            {
                return FileGrantDecision.Deny("grant_revoked");
            }

            if (existingGrant.TenantId != attachment.TenantId ||
                existingGrant.WorkspaceId != attachment.WorkspaceId ||
                existingGrant.FileObjectId != attachment.FileObjectId ||
                existingGrant.AttachmentId != attachment.Id ||
                existingGrant.TargetScopeType != attachment.OwnerType ||
                existingGrant.TargetScopeId != attachment.OwnerId)
            {
                return FileGrantDecision.Deny("scope_mismatch");
            }
        }

        if (attachment.DeletedAt.HasValue ||
            attachment.FileObject.DeletedAt.HasValue ||
            attachment.FileObject.Status == FileObjectStatus.Deleted)
        {
            return FileGrantDecision.Deny("target_deleted");
        }

        if (!attachment.OwnerType.HasValue || !attachment.OwnerId.HasValue)
        {
            return FileGrantDecision.Deny("scope_mismatch");
        }

        // The attachment is a reference to an owner scope; it is not an
        // authorization capability in its own right.  Re-resolve that owner
        // for every open, grant issue, and grant use so a removed Task or a
        // changed Task/File relationship cannot be used to read bytes.
        var owner = await files.ResolveOwnerAsync(attachment.OwnerType.Value, attachment.OwnerId.Value, cancellationToken);
        if (owner is null ||
            owner.WorkspaceId != attachment.WorkspaceId ||
            (attachment.FileObject.WorkspaceId.HasValue && attachment.FileObject.WorkspaceId != owner.WorkspaceId) ||
            (attachment.FileObject.ProjectId.HasValue && attachment.FileObject.ProjectId != owner.ProjectId))
        {
            return FileGrantDecision.Deny("scope_mismatch");
        }

        if (attachment.OwnerType == AttachmentOwnerType.TaskItem &&
            (!owner.ProjectId.HasValue || attachment.ScanStatus != FileScanStatus.Clean))
        {
            return FileGrantDecision.Deny("task_file_not_available");
        }

        if (attachment.FileObject.Status == FileObjectStatus.Archived)
        {
            return FileGrantDecision.Deny("target_archived");
        }

        if (attachment.FileObject.Status == FileObjectStatus.Quarantined ||
            attachment.ScanStatus is FileScanStatus.Pending or FileScanStatus.Infected or FileScanStatus.Failed)
        {
            return FileGrantDecision.Deny("target_quarantined");
        }

        if (attachment.FileObject.Status != FileObjectStatus.Active)
        {
            return FileGrantDecision.Deny("target_not_active");
        }

        if (!attachment.FileObject.Classification.HasValue)
        {
            return FileGrantDecision.Deny("missing_classification");
        }

        if (attachment.FileObject.Classification == DataClassification.UnknownSensitive)
        {
            return FileGrantDecision.Deny("unknown_sensitive_classification");
        }

        var classification = attachment.FileObject.Classification;
        if (existingGrant is not null &&
            (!classification.HasValue || existingGrant.Classification != classification.Value))
        {
            return FileGrantDecision.Deny("policy_changed");
        }

        if (!await authorization.CanDownloadAttachment(userId, attachment, cancellationToken))
        {
            return FileGrantDecision.Deny("current_authorization_failed");
        }

        if (existingGrant is not null &&
            !string.Equals(existingGrant.PolicyStamp, ComputeFilePolicyStamp(userId, attachment), StringComparison.Ordinal))
        {
            return FileGrantDecision.Deny("policy_changed");
        }

        return FileGrantDecision.Allow();
    }

    private async Task LogFileGrantDeniedAsync(
        Guid userId,
        Attachment? attachment,
        FileDownloadGrant? grant,
        string action,
        string operationType,
        string reason,
        CancellationToken cancellationToken)
    {
        var metadata = FileGrantDenialMetadata(userId, attachment, grant, reason, operationType);
        await auditLogger.LogSecurityAsync(
            "AccessDenied",
            "File download grant denied.",
            metadata,
            SecurityEventSeverity.Warning,
            cancellationToken);

        await auditLogger.LogAsync(new AuditLogEntry(
            userId,
            "file_download.reauthorization_failed",
            grant is null ? "FileObject" : "FileDownloadGrant",
            grant?.Id ?? attachment?.FileObjectId,
            "File download grant reauthorization failed.",
            WorkspaceId: attachment?.WorkspaceId ?? grant?.WorkspaceId,
            ProjectId: attachment?.FileObject?.ProjectId,
            Metadata: metadata,
            TenantId: currentTenant.IsAvailable
                ? currentTenant.TenantId
                : attachment?.TenantId ?? grant?.TenantId), cancellationToken);

        await auditLogger.LogAsync(new AuditLogEntry(
            userId,
            action,
            grant is null ? "FileObject" : "FileDownloadGrant",
            grant?.Id ?? attachment?.FileObjectId,
            "File download grant denied.",
            WorkspaceId: attachment?.WorkspaceId ?? grant?.WorkspaceId,
            ProjectId: attachment?.FileObject?.ProjectId,
            Metadata: metadata,
            TenantId: currentTenant.IsAvailable
                ? currentTenant.TenantId
                : attachment?.TenantId ?? grant?.TenantId), cancellationToken);
        var lifecycleAction = reason switch
        {
            "grant_expired" => "file_download.grant_expired",
            "grant_revoked" => "file_download.grant_revoked",
            _ => null
        };
        if (lifecycleAction is not null)
        {
            await auditLogger.LogAsync(new AuditLogEntry(
                userId,
                lifecycleAction,
                "FileDownloadGrant",
                grant?.Id,
                "File download grant lifecycle denial.",
                WorkspaceId: attachment?.WorkspaceId ?? grant?.WorkspaceId,
                ProjectId: attachment?.FileObject?.ProjectId,
                Metadata: metadata,
                TenantId: currentTenant.IsAvailable
                    ? currentTenant.TenantId
                    : attachment?.TenantId ?? grant?.TenantId), cancellationToken);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static IReadOnlyDictionary<string, object?> FileGrantDenialMetadata(
        Guid actorUserId,
        Attachment? attachment,
        FileDownloadGrant? grant,
        string reason,
        string operationType)
    {
        return new Dictionary<string, object?>
        {
            ["actorUserId"] = actorUserId,
            ["grantActorUserId"] = grant?.ActorUserId,
            ["tenantId"] = attachment?.TenantId ?? grant?.TenantId,
            ["workspaceId"] = attachment?.WorkspaceId ?? grant?.WorkspaceId,
            ["fileObjectId"] = attachment?.FileObjectId ?? grant?.FileObjectId,
            ["attachmentId"] = attachment?.Id ?? grant?.AttachmentId,
            ["targetScopeType"] = attachment?.OwnerType?.ToString() ?? grant?.TargetScopeType.ToString(),
            ["targetScopeId"] = attachment?.OwnerId ?? grant?.TargetScopeId,
            ["classification"] = attachment?.FileObject?.Classification?.ToString() ?? grant?.Classification.ToString(),
            ["decision"] = "deny",
            ["decisionReason"] = reason,
            ["grantId"] = grant?.Id,
            ["operationType"] = operationType
        };
    }

    private static IReadOnlyDictionary<string, object?> FileGrantAuditMetadata(
        FileDownloadGrant grant,
        string decision,
        string reason,
        string operationType)
    {
        return new Dictionary<string, object?>
        {
            ["actorUserId"] = grant.ActorUserId,
            ["tenantId"] = grant.TenantId,
            ["workspaceId"] = grant.WorkspaceId,
            ["fileObjectId"] = grant.FileObjectId,
            ["attachmentId"] = grant.AttachmentId,
            ["targetScopeType"] = grant.TargetScopeType.ToString(),
            ["targetScopeId"] = grant.TargetScopeId,
            ["classification"] = grant.Classification.ToString(),
            ["allowedOperation"] = grant.AllowedOperation,
            ["decision"] = decision,
            ["decisionReason"] = reason,
            ["grantId"] = grant.Id,
            ["expiresAt"] = grant.ExpiresAt,
            ["operationType"] = operationType
        };
    }

    private static string ComputeFilePolicyStamp(Guid userId, Attachment attachment)
    {
        var basis = string.Join("|",
            userId,
            attachment.TenantId,
            attachment.WorkspaceId,
            attachment.FileObjectId,
            attachment.Id,
            attachment.OwnerType?.ToString() ?? "none",
            attachment.OwnerId?.ToString("D") ?? "none",
            attachment.FileObject?.TenantId.ToString("D") ?? "missing",
            attachment.FileObject?.WorkspaceId?.ToString("D") ?? "none",
            attachment.FileObject?.ProjectId?.ToString("D") ?? "none",
            attachment.FileObject?.Classification?.ToString() ?? "missing",
            attachment.FileObject?.SharingPolicy.ToString() ?? "missing",
            attachment.FileObject?.SharingVersion.ToString() ?? "missing",
            attachment.FileObject?.Status.ToString() ?? "missing",
            attachment.ScanStatus.ToString());
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(basis));
        return Convert.ToHexString(bytes);
    }

    private static FileDownloadGrantResponse ToGrantResponse(FileDownloadGrant grant, string token)
    {
        return new FileDownloadGrantResponse(
            grant.Id,
            grant.AttachmentId,
            grant.FileObjectId,
            grant.TargetScopeType,
            grant.TargetScopeId,
            grant.Classification.ToString(),
            grant.ExpiresAt,
            token);
    }

    private static string CreateOpaqueToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    private static string? NormalizeGrantPurpose(string? purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose))
        {
            return null;
        }

        var normalized = purpose.Trim();
        return normalized.Length <= MaxGrantPurposeLength ? normalized : normalized[..MaxGrantPurposeLength];
    }

    private sealed record FileGrantDecision(bool IsAllowed, string DenialReason)
    {
        public static FileGrantDecision Allow() => new(true, string.Empty);

        public static FileGrantDecision Deny(string denialReason) => new(false, denialReason);
    }

    private static string CreateStorageKey(FileObject fileObject)
    {
        var tenantPart = fileObject.TenantId.ToString("D");
        var filePart = fileObject.Id.ToString("D");
        return fileObject.ProjectId.HasValue
            ? $"tenants/{tenantPart}/projects/{fileObject.ProjectId.Value:D}/files/{filePart}"
            : $"tenants/{tenantPart}/files/{filePart}";
    }

    private static string NormalizeContentType(string contentType)
    {
        return string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim();
    }

    private static FileObjectResponse ToFileObjectResponse(
        FileObject fileObject,
        FileSharingPresentation? sharing = null)
    {
        return new FileObjectResponse(
            fileObject.Id,
            fileObject.WorkspaceId,
            fileObject.GroupId,
            fileObject.ProjectId,
            fileObject.OriginalFileName,
            fileObject.ContentType,
            fileObject.SizeBytes,
            fileObject.Status.ToString(),
            fileObject.CreatedAt,
            fileObject.UpdatedAt,
            fileObject.DeletedAt,
            sharing?.AccessState,
            sharing?.ExternalRecipientCount,
            sharing?.CanManageSharing ?? false,
            sharing?.SharingVersion);
    }

    private static FileListItemResponse ToFileListItemResponse(
        Attachment attachment,
        bool canDelete,
        FileSharingPresentation? sharing)
    {
        var fileObject = attachment.FileObject ?? throw new InvalidOperationException("Listed attachment must include a file object.");
        return new FileListItemResponse(
            attachment.Id,
            fileObject.Id,
            attachment.WorkspaceId,
            fileObject.OriginalFileName,
            fileObject.ContentType,
            fileObject.SizeBytes,
            fileObject.Status.ToString(),
            attachment.ScanStatus.ToString(),
            fileObject.UploadedByUserId,
            fileObject.UploadedByUser?.DisplayName ?? attachment.UploadedByUser?.DisplayName,
            fileObject.CreatedAt,
            fileObject.UpdatedAt,
            fileObject.DeletedAt ?? attachment.DeletedAt,
            canDelete,
            sharing?.AccessState,
            sharing?.ExternalRecipientCount,
            sharing?.CanManageSharing ?? false,
            sharing?.SharingVersion);
    }
}
