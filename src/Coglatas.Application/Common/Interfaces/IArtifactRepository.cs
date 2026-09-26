using Coglatas.Domain.Entities;

namespace Coglatas.Application.Common.Interfaces;

public interface IArtifactRepository
{
    Task<IReadOnlyList<Artifact>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<Artifact?> GetArtifactAsync(Guid artifactId, CancellationToken cancellationToken = default);

    Task<ArtifactVersion?> GetVersionAsync(Guid versionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArtifactVersion>> ListVersionsAsync(Guid artifactId, CancellationToken cancellationToken = default);

    Task<int> GetNextVersionNumberAsync(Guid artifactId, CancellationToken cancellationToken = default);

    Task AddArtifactAsync(Artifact artifact, CancellationToken cancellationToken = default);

    Task AddVersionAsync(ArtifactVersion version, CancellationToken cancellationToken = default);
}
