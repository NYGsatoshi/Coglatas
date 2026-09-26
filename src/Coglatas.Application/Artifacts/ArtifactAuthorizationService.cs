using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Projects;

namespace Coglatas.Application.Artifacts;

public sealed class ArtifactAuthorizationService(
    IArtifactRepository artifacts,
    IProjectAuthorizationService projects) : IArtifactAuthorizationService
{
    public async Task<bool> CanViewArtifact(Guid userId, Guid artifactId, CancellationToken cancellationToken = default)
    {
        var artifact = await artifacts.GetArtifactAsync(artifactId, cancellationToken);
        return artifact is not null &&
            !artifact.DeletedAt.HasValue &&
            await projects.CanViewProject(userId, artifact.ProjectId, cancellationToken);
    }

    public Task<bool> CanUploadArtifact(Guid userId, Guid projectId, CancellationToken cancellationToken = default)
    {
        return projects.CanManageProject(userId, projectId, cancellationToken);
    }

    public async Task<bool> CanUpdateArtifact(Guid userId, Guid artifactId, CancellationToken cancellationToken = default)
    {
        var artifact = await artifacts.GetArtifactAsync(artifactId, cancellationToken);
        return artifact is not null &&
            !artifact.DeletedAt.HasValue &&
            await projects.CanManageProject(userId, artifact.ProjectId, cancellationToken);
    }

    public async Task<bool> CanDownloadArtifactVersion(Guid userId, Guid versionId, CancellationToken cancellationToken = default)
    {
        var version = await artifacts.GetVersionAsync(versionId, cancellationToken);
        return version?.Artifact is not null &&
            !version.DeletedAt.HasValue &&
            !version.Artifact.DeletedAt.HasValue &&
            await projects.CanViewProject(userId, version.Artifact.ProjectId, cancellationToken);
    }
}
