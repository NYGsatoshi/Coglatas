using Coglatas.Domain.Enums;

namespace Coglatas.Application.Artifacts;

public sealed record CreateArtifactRequest(
    string Title,
    string? Description,
    ArtifactType ArtifactType,
    ArtifactStatus? Status);

public sealed record UpdateArtifactRequest(
    string? Title,
    string? Description,
    ArtifactType? ArtifactType,
    ArtifactStatus? Status);

public sealed record ArtifactListItemResponse(
    Guid Id,
    Guid ProjectId,
    string Title,
    string? Description,
    ArtifactType ArtifactType,
    ArtifactStatus Status,
    Guid? CurrentVersionId,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record ArtifactDetailResponse(
    Guid Id,
    Guid ProjectId,
    string Title,
    string? Description,
    ArtifactType ArtifactType,
    ArtifactStatus Status,
    Guid? CurrentVersionId,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<ArtifactVersionResponse> Versions);

public sealed record UploadArtifactVersionInput(
    string OriginalFileName,
    string ContentType,
    long Length,
    Stream Content,
    string? ChangeNote);

public sealed record ArtifactVersionResponse(
    Guid Id,
    Guid ArtifactId,
    int VersionNumber,
    string OriginalFileName,
    string ContentType,
    long FileSize,
    Guid UploadedByUserId,
    string? ChangeNote,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt);
