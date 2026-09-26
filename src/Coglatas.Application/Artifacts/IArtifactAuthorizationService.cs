namespace Coglatas.Application.Artifacts;

public interface IArtifactAuthorizationService
{
    Task<bool> CanViewArtifact(Guid userId, Guid artifactId, CancellationToken cancellationToken = default);

    Task<bool> CanUploadArtifact(Guid userId, Guid projectId, CancellationToken cancellationToken = default);

    Task<bool> CanUpdateArtifact(Guid userId, Guid artifactId, CancellationToken cancellationToken = default);

    Task<bool> CanDownloadArtifactVersion(Guid userId, Guid versionId, CancellationToken cancellationToken = default);
}
