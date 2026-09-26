using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.Common;

/// <summary>
/// Fail-closed fallback for hosts that compose AddApplication() without the
/// Infrastructure persistence layer. Full application composition registers
/// ArtifactEvidenceRepository later and overrides this fallback.
/// </summary>
internal sealed class UnavailableArtifactEvidenceRepository : IArtifactEvidenceRepository
{
    public Task<bool> HasClaimsAsync(
        Guid artifactVersionId,
        CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<IReadOnlyList<ArtifactClaim>> ListClaimsAsync(
        Guid artifactVersionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ArtifactClaim>>([]);

    public Task AddClaimsAsync(
        IReadOnlyCollection<ArtifactClaim> claims,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Artifact Claims/Evidence persistence is unavailable.");
}
