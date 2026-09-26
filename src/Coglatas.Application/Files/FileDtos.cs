using Coglatas.Domain.Enums;

namespace Coglatas.Application.Files;

public sealed record AttachmentUploadInput(
    AttachmentOwnerType OwnerType,
    Guid OwnerId,
    string OriginalFileName,
    string ContentType,
    long Length,
    Stream Content);

public sealed record AttachmentResponse(
    Guid Id,
    Guid FileObjectId,
    AttachmentOwnerType? OwnerType,
    Guid? OwnerId,
    string OriginalFileName,
    string ContentType,
    long FileSize,
    Guid UploadedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt);

public sealed record FileDownloadResponse(Stream Content, string FileName, string ContentType, long SizeBytes);

public sealed record FileDownloadGrantRequest(string? Purpose = null);

public sealed record FileDownloadGrantResponse(
    Guid FileDownloadGrantId,
    Guid AttachmentId,
    Guid FileObjectId,
    AttachmentOwnerType TargetScopeType,
    Guid TargetScopeId,
    string Classification,
    DateTimeOffset ExpiresAt,
    string Token);

public sealed record FileDownloadGrantTokenRequest(string Token);

public sealed record FileListItemResponse(
    Guid Id,
    Guid FileObjectId,
    Guid WorkspaceId,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string Status,
    string? ScanStatus,
    Guid UploadedByUserId,
    string? UploadedByDisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? DeletedAt,
    bool CanDelete,
    string? AccessState = null,
    int? ExternalRecipientCount = null,
    bool CanManageSharing = false,
    long? SharingVersion = null);

public sealed record FileObjectResponse(
    Guid Id,
    Guid? WorkspaceId,
    Guid? GroupId,
    Guid? ProjectId,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? DeletedAt,
    string? AccessState = null,
    int? ExternalRecipientCount = null,
    bool CanManageSharing = false,
    long? SharingVersion = null);
