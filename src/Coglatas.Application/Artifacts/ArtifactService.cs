using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Files;
using Coglatas.Application.Projects;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Artifacts;

public sealed class ArtifactService(
    IArtifactRepository artifacts,
    IProjectRepository projects,
    IProjectAuthorizationService projectAuthorization,
    IFileRepository files,
    IFileStorageService storage,
    IFileUploadPolicy uploadPolicy,
    IFeatureFlagService featureFlags,
    IQuotaService quotaService,
    IArtifactAuthorizationService authorization,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IClock clock,
    IAuditLogger auditLogger,
    INotificationService notifications,
    IUnitOfWork unitOfWork) : IArtifactService
{
    public async Task<Result<IReadOnlyList<ArtifactListItemResponse>>> ListAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId) || !await projectAuthorization.CanViewProject(userId, projectId, cancellationToken))
        {
            return Result<IReadOnlyList<ArtifactListItemResponse>>.Failure("Project not found.");
        }

        var items = await artifacts.ListByProjectAsync(projectId, cancellationToken);
        return Result<IReadOnlyList<ArtifactListItemResponse>>.Success(items
            .Where(artifact => !artifact.DeletedAt.HasValue)
            .Select(ToListItem)
            .ToList());
    }

    public async Task<Result<ArtifactDetailResponse>> CreateAsync(Guid projectId, CreateArtifactRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId) || !await authorization.CanUploadArtifact(userId, projectId, cancellationToken))
        {
            return Result<ArtifactDetailResponse>.Failure("You are not allowed to create artifacts for this project.");
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Result<ArtifactDetailResponse>.Failure("Artifact title is required.");
        }

        var artifact = new Artifact
        {
            ProjectId = projectId,
            Name = request.Title.Trim(),
            Description = request.Description?.Trim(),
            ArtifactType = request.ArtifactType,
            Status = request.Status ?? ArtifactStatus.Draft,
            CreatedByUserId = userId
        };

        await artifacts.AddArtifactAsync(artifact, cancellationToken);
        await auditLogger.LogAsync(new AuditLogEntry(userId, "ArtifactCreated", "Artifact", artifact.Id), cancellationToken);
        await NotifyProjectManagersAsync(projectId, "New artifact uploaded", artifact.Name, artifact.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<ArtifactDetailResponse>.Success(ToDetail(artifact, []));
    }

    public async Task<Result<ArtifactDetailResponse>> GetAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        var artifact = await artifacts.GetArtifactAsync(artifactId, cancellationToken);
        if (artifact is null || !TryCurrentUser(out var userId) || !await authorization.CanViewArtifact(userId, artifactId, cancellationToken))
        {
            return Result<ArtifactDetailResponse>.Failure("Artifact not found.");
        }

        var versions = await artifacts.ListVersionsAsync(artifactId, cancellationToken);
        return Result<ArtifactDetailResponse>.Success(ToDetail(artifact, versions.Where(version => !version.DeletedAt.HasValue).Select(ToVersion).ToList()));
    }

    public async Task<Result<ArtifactDetailResponse>> UpdateAsync(Guid artifactId, UpdateArtifactRequest request, CancellationToken cancellationToken = default)
    {
        var artifact = await artifacts.GetArtifactAsync(artifactId, cancellationToken);
        if (artifact is null || !TryCurrentUser(out var userId) || !await authorization.CanUpdateArtifact(userId, artifactId, cancellationToken))
        {
            return Result<ArtifactDetailResponse>.Failure("Artifact not found.");
        }

        if (request.Title is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return Result<ArtifactDetailResponse>.Failure("Artifact title is required.");
            }

            artifact.Name = request.Title.Trim();
        }

        var previousStatus = artifact.Status;
        artifact.Description = request.Description?.Trim() ?? artifact.Description;
        artifact.ArtifactType = request.ArtifactType ?? artifact.ArtifactType;
        artifact.Status = request.Status ?? artifact.Status;

        await auditLogger.LogAsync(new AuditLogEntry(userId, "ArtifactUpdated", "Artifact", artifact.Id), cancellationToken);
        if (artifact.Status != previousStatus)
        {
            await NotifyStatusAsync(artifact, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        var versions = await artifacts.ListVersionsAsync(artifactId, cancellationToken);
        return Result<ArtifactDetailResponse>.Success(ToDetail(artifact, versions.Where(version => !version.DeletedAt.HasValue).Select(ToVersion).ToList()));
    }

    public async Task<Result> DeleteAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        var artifact = await artifacts.GetArtifactAsync(artifactId, cancellationToken);
        if (artifact is null || !TryCurrentUser(out var userId) || !await authorization.CanUpdateArtifact(userId, artifactId, cancellationToken))
        {
            return Result.Failure("Artifact not found.");
        }

        artifact.Status = ArtifactStatus.Archived;
        artifact.MarkDeleted(clock.UtcNow);
        await auditLogger.LogAsync(new AuditLogEntry(userId, "ArtifactArchived", "Artifact", artifact.Id), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<ArtifactVersionResponse>>> ListVersionsAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId) || !await authorization.CanViewArtifact(userId, artifactId, cancellationToken))
        {
            return Result<IReadOnlyList<ArtifactVersionResponse>>.Failure("Artifact not found.");
        }

        var versions = await artifacts.ListVersionsAsync(artifactId, cancellationToken);
        return Result<IReadOnlyList<ArtifactVersionResponse>>.Success(versions
            .Where(version => !version.DeletedAt.HasValue)
            .Select(ToVersion)
            .ToList());
    }

    public async Task<Result<ArtifactVersionResponse>> UploadVersionAsync(Guid artifactId, UploadArtifactVersionInput input, CancellationToken cancellationToken = default)
    {
        var artifact = await artifacts.GetArtifactAsync(artifactId, cancellationToken);
        if (artifact is null || !TryCurrentUser(out var userId) || !await authorization.CanUpdateArtifact(userId, artifactId, cancellationToken))
        {
            return Result<ArtifactVersionResponse>.Failure("Artifact not found.");
        }

        if (!currentTenant.IsAvailable)
        {
            return Result<ArtifactVersionResponse>.Failure("A tenant context is required.");
        }

        var feature = await featureFlags.RequireEnabledAsync(FeatureKeys.FileSharing, cancellationToken);
        if (!feature.IsSuccess)
        {
            return Result<ArtifactVersionResponse>.Failure(feature.Error!);
        }

        var validation = ValidateUpload(input.OriginalFileName, input.ContentType, input.Length);
        if (!validation.IsSuccess)
        {
            return Result<ArtifactVersionResponse>.Failure(validation.Error!);
        }

        var quota = await quotaService.CanUploadFileAsync(currentTenant.TenantId, input.Length, cancellationToken);
        if (!quota.IsSuccess)
        {
            await auditLogger.LogAsync(new AuditLogEntry(userId, "FileUploadBlockedByQuota", "Artifact", artifact.Id, quota.Error), cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<ArtifactVersionResponse>.Failure(quota.Error!);
        }

        var project = await projects.GetProjectAsync(artifact.ProjectId, cancellationToken);
        if (project is null)
        {
            return Result<ArtifactVersionResponse>.Failure("Project not found.");
        }

        var safeFileName = FileNameSanitizer.SanitizeOriginalFileName(input.OriginalFileName);
        var fileObject = new FileObject
        {
            TenantId = currentTenant.TenantId,
            WorkspaceId = project.WorkspaceId,
            GroupId = project.GroupId,
            ProjectId = project.Id,
            UploadedByUserId = userId,
            OriginalFileName = safeFileName,
            ContentType = NormalizeContentType(input.ContentType),
            SizeBytes = input.Length,
            Classification = DataClassification.Private,
            Status = FileObjectStatus.Active
        };
        fileObject.StorageKey = $"tenants/{fileObject.TenantId:D}/projects/{project.Id:D}/files/{fileObject.Id:D}";

        var saved = await storage.SaveAsync(fileObject.StorageKey, input.Content, fileObject.ContentType, cancellationToken);
        if (!saved.IsSuccess)
        {
            return Result<ArtifactVersionResponse>.Failure(saved.Error!);
        }

        var version = new ArtifactVersion
        {
            ArtifactId = artifactId,
            VersionNumber = await artifacts.GetNextVersionNumberAsync(artifactId, cancellationToken),
            FileObjectId = fileObject.Id,
            FileObject = fileObject,
            Notes = input.ChangeNote?.Trim(),
            CreatedByUserId = userId
        };

        var attachment = new Attachment
        {
            TenantId = currentTenant.TenantId,
            FileObjectId = fileObject.Id,
            FileObject = fileObject,
            WorkspaceId = project.WorkspaceId,
            OwnerType = AttachmentOwnerType.ArtifactVersion,
            OwnerId = version.Id,
            OwnerUserId = userId,
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

        version.AttachmentId = attachment.Id;
        version.Attachment = attachment;
        await files.AddFileObjectAsync(fileObject, cancellationToken);
        await files.AddAttachmentAsync(attachment, cancellationToken);
        await artifacts.AddVersionAsync(version, cancellationToken);
        artifact.CurrentVersionId = version.Id;
        await auditLogger.LogAsync(new AuditLogEntry(userId, "ArtifactVersionUploaded", "ArtifactVersion", version.Id), cancellationToken);
        await NotifyProjectManagersAsync(artifact.ProjectId, "New artifact version uploaded", artifact.Name, artifact.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<ArtifactVersionResponse>.Success(ToVersion(version));
    }

    public async Task<Result<FileDownloadResponse>> DownloadVersionAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        var version = await artifacts.GetVersionAsync(versionId, cancellationToken);
        if (version?.Attachment is null || !TryCurrentUser(out var userId) || !await authorization.CanDownloadArtifactVersion(userId, versionId, cancellationToken))
        {
            return Result<FileDownloadResponse>.Failure("Artifact version not found.");
        }

        if (version.FileObject?.Status != FileObjectStatus.Active || version.FileObject.DeletedAt.HasValue)
        {
            return Result<FileDownloadResponse>.Failure("Artifact version not found.");
        }

        var content = await storage.OpenReadAsync(version.FileObject.StorageKey, cancellationToken);
        await auditLogger.LogAsync(new AuditLogEntry(userId, "ArtifactVersionDownloaded", "ArtifactVersion", version.Id), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<FileDownloadResponse>.Success(new FileDownloadResponse(content, version.FileObject.OriginalFileName, version.FileObject.ContentType, version.FileObject.SizeBytes));
    }

    public async Task<Result> DeleteVersionAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        var version = await artifacts.GetVersionAsync(versionId, cancellationToken);
        if (version?.Artifact is null || !TryCurrentUser(out var userId) || !await authorization.CanUpdateArtifact(userId, version.ArtifactId, cancellationToken))
        {
            return Result.Failure("Artifact version not found.");
        }

        version.MarkDeleted(clock.UtcNow);
        if (version.Artifact.CurrentVersionId == version.Id)
        {
            var latest = (await artifacts.ListVersionsAsync(version.ArtifactId, cancellationToken))
                .Where(item => item.Id != version.Id && !item.DeletedAt.HasValue)
                .OrderByDescending(item => item.VersionNumber)
                .FirstOrDefault();
            version.Artifact.CurrentVersionId = latest?.Id;
        }

        await auditLogger.LogAsync(new AuditLogEntry(userId, "ArtifactVersionDeleted", "ArtifactVersion", version.Id), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task NotifyStatusAsync(Artifact artifact, CancellationToken cancellationToken)
    {
        var title = artifact.Status switch
        {
            ArtifactStatus.Submitted => "Artifact submitted for review",
            ArtifactStatus.Reviewed => "Artifact reviewed",
            ArtifactStatus.Approved => "Artifact approved",
            _ => null
        };

        if (title is not null)
        {
            await NotifyProjectManagersAsync(artifact.ProjectId, title, artifact.Name, artifact.Id, cancellationToken);
        }
    }

    private async Task NotifyProjectManagersAsync(Guid projectId, string title, string body, Guid artifactId, CancellationToken cancellationToken)
    {
        var members = await projects.ListMembersAsync(projectId, cancellationToken);
        foreach (var member in members.Where(member => member.Role is ProjectRole.Owner or ProjectRole.Manager or ProjectRole.Reviewer))
        {
            await notifications.NotifyAsync(member.UserId, title, body, "Artifact", artifactId, cancellationToken);
        }
    }

    private bool TryCurrentUser(out Guid userId)
    {
        userId = currentUser.UserId ?? Guid.Empty;
        return currentUser.IsAuthenticated && currentUser.UserId.HasValue;
    }

    private Result ValidateUpload(string originalFileName, string contentType, long length)
    {
        if (length <= 0)
        {
            return Result.Failure("Empty files are not allowed.");
        }

        if (length > uploadPolicy.MaxFileSizeBytes)
        {
            return Result.Failure($"File exceeds the maximum size of {uploadPolicy.MaxFileSizeBytes} bytes.");
        }

        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        var allowed = uploadPolicy.AllowedExtensions.Select(item => item.ToLowerInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(extension) || !allowed.Contains(extension))
        {
            return Result.Failure("File extension is not allowed.");
        }

        var normalizedContentType = NormalizeContentType(contentType);
        var allowedContentTypes = uploadPolicy.AllowedContentTypes
            .Select(item => item.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!allowedContentTypes.Contains(normalizedContentType))
        {
            return Result.Failure("File content type is not allowed.");
        }

        return Result.Success();
    }

    private static string NormalizeContentType(string contentType)
    {
        return string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim();
    }

    private static ArtifactListItemResponse ToListItem(Artifact artifact)
    {
        return new ArtifactListItemResponse(artifact.Id, artifact.ProjectId, artifact.Name, artifact.Description, artifact.ArtifactType, artifact.Status, artifact.CurrentVersionId, artifact.CreatedByUserId, artifact.CreatedAt, artifact.UpdatedAt);
    }

    private static ArtifactDetailResponse ToDetail(Artifact artifact, IReadOnlyList<ArtifactVersionResponse> versions)
    {
        return new ArtifactDetailResponse(artifact.Id, artifact.ProjectId, artifact.Name, artifact.Description, artifact.ArtifactType, artifact.Status, artifact.CurrentVersionId, artifact.CreatedByUserId, artifact.CreatedAt, artifact.UpdatedAt, versions);
    }

    private static ArtifactVersionResponse ToVersion(ArtifactVersion version)
    {
        var attachment = version.Attachment ?? new Attachment();
        var fileObject = version.FileObject;
        return new ArtifactVersionResponse(
            version.Id,
            version.ArtifactId,
            version.VersionNumber,
            fileObject?.OriginalFileName ?? attachment.FileName,
            fileObject?.ContentType ?? attachment.ContentType,
            fileObject?.SizeBytes ?? attachment.SizeBytes,
            version.CreatedByUserId,
            version.Notes,
            version.CreatedAt,
            fileObject?.DeletedAt ?? version.DeletedAt);
    }
}
