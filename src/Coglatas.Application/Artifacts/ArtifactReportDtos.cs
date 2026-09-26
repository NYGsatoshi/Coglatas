using Coglatas.Application.Common;

namespace Coglatas.Application.Artifacts;

public sealed record AttachArtifactReportRequest(string Title, IReadOnlyList<ArtifactReportSectionRequest> Sections);
public sealed record ArtifactReportSectionRequest(Guid? LogicalSectionId, int Ordinal, string Heading, string BodyText, IReadOnlyList<ArtifactReportCitationRequest> Citations);
public sealed record ArtifactReportCitationRequest(int Ordinal, int AnchorStartUtf16, int AnchorLengthUtf16, Guid ArtifactClaimId);
public sealed record ArtifactReportAttachedResponse(Guid ArtifactVersionId, Guid ReportDocumentId, int SectionCount);
public sealed record ArtifactReportResponse(Guid ProjectId, Guid? TaskId, Guid ArtifactId, Guid ArtifactVersionId, int VersionNumber, bool CanRefine, string Title, IReadOnlyList<ArtifactReportSectionResponse> Sections);
public sealed record ArtifactReportSectionResponse(Guid Id, Guid LogicalSectionId, int Ordinal, string Heading, IReadOnlyList<ArtifactReportRunResponse> Runs);
public sealed record ArtifactReportRunResponse(string Kind, string Text, ArtifactReportCitationResponse? Citation);
public sealed record ArtifactReportCitationResponse(Guid Id, int Ordinal, Guid ClaimId, Guid LogicalClaimId, IReadOnlyList<ArtifactReportEvidenceResponse> Evidence);
public sealed record ArtifactReportEvidenceResponse(Guid Id, int Ordinal, string SourceKind, string? SourceTitle, string Passage, string? Location, Guid? TraceEventId);

public interface IArtifactReportService
{
    Task<Result<ArtifactReportAttachedResponse>> AttachAsync(Guid artifactVersionId, AttachArtifactReportRequest request, CancellationToken cancellationToken = default);
    Task<Result<ArtifactReportResponse>> GetAsync(Guid projectId, Guid artifactVersionId, Guid? requiredTaskId, CancellationToken cancellationToken = default);
}
