using Coglatas.Application.Common;

namespace Coglatas.Application.Artifacts;

public interface IArtifactEvidenceManifestService
{
    Task<Result<ArtifactEvidenceManifestResponse>> AttachAsync(
        Guid artifactVersionId,
        AttachArtifactEvidenceManifestRequest request,
        CancellationToken cancellationToken = default);
}
