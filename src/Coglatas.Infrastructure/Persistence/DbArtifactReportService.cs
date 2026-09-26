using Coglatas.Application.Artifacts;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Files;
using Coglatas.Application.Projects;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

public sealed class DbArtifactReportService(AppDbContext db, IArtifactRepository artifacts, IArtifactAuthorizationService artifactAuthorization, IProjectAuthorizationService projectAuthorization, IFileRepository files, IFileAuthorizationService fileAuthorization, ICurrentUser currentUser, IUnitOfWork unitOfWork) : IArtifactReportService
{
    private const int MaxSections = 100, MaxCitations = 500;
    public async Task<Result<ArtifactReportAttachedResponse>> AttachAsync(Guid artifactVersionId, AttachArtifactReportRequest request, CancellationToken ct = default)
    {
        if (!User(out var userId)) return Fail<ArtifactReportAttachedResponse>();
        if (request is null) return Validation<ArtifactReportAttachedResponse>();
        var version = await artifacts.GetVersionAsync(artifactVersionId, ct);
        if (version?.Artifact is null || version.DeletedAt.HasValue || version.Artifact.DeletedAt.HasValue || !await artifactAuthorization.CanUpdateArtifact(userId, version.ArtifactId, ct)) return Fail<ArtifactReportAttachedResponse>();
        if (await db.Set<ArtifactReportDocument>().AnyAsync(x => x.ArtifactVersionId == artifactVersionId, ct)) return Error<ArtifactReportAttachedResponse>("ReportAlreadyAttached", "A report is already attached to this immutable artifact version.");
        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title) || title.Length > 512 || request.Sections is null || request.Sections.Count is < 1 or > MaxSections) return Validation<ArtifactReportAttachedResponse>();
        var claims = await db.Set<ArtifactClaim>().Where(x => x.ArtifactVersionId == artifactVersionId).ToDictionaryAsync(x => x.Id, ct);
        var document = new ArtifactReportDocument { TenantId = version.TenantId, ArtifactVersionId = version.Id, Title = title, ArtifactVersion = version };
        var sectionOrdinals = new HashSet<int>(); var citationCount = 0;
        foreach (var item in request.Sections)
        {
            var heading = item.Heading?.Trim();
            if (item.Ordinal <= 0 || !sectionOrdinals.Add(item.Ordinal) || string.IsNullOrEmpty(heading) || heading.Length > 512 || item.BodyText is null || item.BodyText.Length > 50000 || item.Citations is null) return Validation<ArtifactReportAttachedResponse>();
            var section = new ArtifactReportSection { TenantId = version.TenantId, ArtifactReportDocumentId = document.Id, LogicalSectionId = item.LogicalSectionId is { } logical && logical != Guid.Empty ? logical : Guid.NewGuid(), Ordinal = item.Ordinal, Heading = heading, BodyText = item.BodyText, Document = document };
            var ordinals = new HashSet<int>(); var ranges = new List<(int Start, int End)>();
            foreach (var citation in item.Citations.OrderBy(x => x.AnchorStartUtf16))
            {
                citationCount++;
                if (citationCount > MaxCitations || citation.Ordinal <= 0 || !ordinals.Add(citation.Ordinal) || citation.AnchorStartUtf16 < 0 || citation.AnchorLengthUtf16 <= 0 || citation.AnchorStartUtf16 > item.BodyText.Length - citation.AnchorLengthUtf16 || !claims.TryGetValue(citation.ArtifactClaimId, out var claim) || ranges.Any(x => citation.AnchorStartUtf16 < x.End && citation.AnchorStartUtf16 + citation.AnchorLengthUtf16 > x.Start)) return Validation<ArtifactReportAttachedResponse>();
                ranges.Add((citation.AnchorStartUtf16, citation.AnchorStartUtf16 + citation.AnchorLengthUtf16));
                section.Citations.Add(new ArtifactReportCitation { TenantId = version.TenantId, ArtifactReportSectionId = section.Id, Ordinal = citation.Ordinal, AnchorStartUtf16 = citation.AnchorStartUtf16, AnchorLengthUtf16 = citation.AnchorLengthUtf16, ArtifactClaimId = claim.Id, Section = section, Claim = claim });
            }
            document.Sections.Add(section);
        }
        db.Set<ArtifactReportDocument>().Add(document); await unitOfWork.SaveChangesAsync(ct);
        return Result<ArtifactReportAttachedResponse>.Success(new(version.Id, document.Id, document.Sections.Count));
    }

    public async Task<Result<ArtifactReportResponse>> GetAsync(Guid projectId, Guid artifactVersionId, Guid? requiredTaskId, CancellationToken ct = default)
    {
        if (!User(out var userId) || !await projectAuthorization.CanViewProject(userId, projectId, ct)) return Fail<ArtifactReportResponse>();
        var version = await artifacts.GetVersionAsync(artifactVersionId, ct);
        if (version?.Artifact is null || version.Artifact.ProjectId != projectId || version.DeletedAt.HasValue || version.Artifact.DeletedAt.HasValue || !await artifactAuthorization.CanViewArtifact(userId, version.ArtifactId, ct) || (requiredTaskId.HasValue && version.Artifact.TaskItemId != requiredTaskId)) return Fail<ArtifactReportResponse>();
        if (version.Artifact.TaskItemId is { } taskId && !await db.TaskItems.AsNoTracking().AnyAsync(x => x.Id == taskId && x.ProjectId == projectId && !x.DeletedAt.HasValue, ct)) return Fail<ArtifactReportResponse>();
        var document = await db.Set<ArtifactReportDocument>().AsNoTracking().Include(x => x.Sections).ThenInclude(x => x.Citations).ThenInclude(x => x.Claim).ThenInclude(x => x!.Evidence).SingleOrDefaultAsync(x => x.ArtifactVersionId == artifactVersionId, ct);
        if (document is null || document.SchemaVersion != 1) return Fail<ArtifactReportResponse>();
        var sourceAuth = new Dictionary<(ArtifactEvidenceSourceKind, string), bool>();
        async Task<bool> CanSource(ArtifactEvidence e) { var key=(e.SourceKind,e.SourceReference); if(sourceAuth.TryGetValue(key,out var ok)) return ok; ok=e.SourceKind switch { ArtifactEvidenceSourceKind.WebSnapshot => true, ArtifactEvidenceSourceKind.ArtifactVersion when Guid.TryParse(e.SourceReference,out var id) => await artifactAuthorization.CanDownloadArtifactVersion(userId,id,ct), ArtifactEvidenceSourceKind.FileAttachment when Guid.TryParse(e.SourceReference,out var id) => await CanFile(id), _ => false }; sourceAuth[key]=ok; return ok; }
        async Task<bool> CanFile(Guid id) { var attachment=await files.GetAttachmentAsync(id,ct); return attachment is not null && !attachment.DeletedAt.HasValue && await fileAuthorization.CanViewAttachment(userId,attachment,ct); }
        var sections = new List<ArtifactReportSectionResponse>();
        foreach(var section in document.Sections.OrderBy(x=>x.Ordinal))
        {
            var runs=new List<ArtifactReportRunResponse>(); var cursor=0;
            foreach(var citation in section.Citations.OrderBy(x=>x.AnchorStartUtf16).ThenBy(x=>x.Ordinal))
            {
                if(citation.Claim?.ArtifactVersionId != artifactVersionId || citation.AnchorStartUtf16 < cursor || citation.AnchorStartUtf16 > section.BodyText.Length-citation.AnchorLengthUtf16) return Fail<ArtifactReportResponse>();
                if(citation.AnchorStartUtf16>cursor) runs.Add(new("text",section.BodyText[cursor..citation.AnchorStartUtf16],null));
                var evidence=new List<ArtifactReportEvidenceResponse>(); foreach(var e in citation.Claim.Evidence.OrderBy(x=>x.Ordinal).Take(20)) if(await CanSource(e)) evidence.Add(new(e.Id,e.Ordinal,e.SourceKind.ToString(),e.SourceTitleSnapshot,e.PassageSnapshot,e.LocationSnapshot,null));
                runs.Add(new("citation",section.BodyText.Substring(citation.AnchorStartUtf16,citation.AnchorLengthUtf16),new(citation.Id,citation.Ordinal,citation.Claim.Id,citation.Claim.LogicalClaimId,evidence))); cursor=citation.AnchorStartUtf16+citation.AnchorLengthUtf16;
            }
            if(cursor<section.BodyText.Length) runs.Add(new("text",section.BodyText[cursor..],null)); sections.Add(new(section.Id,section.LogicalSectionId,section.Ordinal,section.Heading,runs));
        }
        var canRefine = version.Artifact.TaskItemId.HasValue && await artifactAuthorization.CanUpdateArtifact(userId, version.ArtifactId, ct);
        return Result<ArtifactReportResponse>.Success(new(projectId,version.Artifact.TaskItemId,version.ArtifactId,version.Id,version.VersionNumber,canRefine,document.Title,sections));
    }
    private bool User(out Guid id){id=currentUser.UserId??Guid.Empty;return currentUser.IsAuthenticated&&id!=Guid.Empty;}
    private static Result<T> Fail<T>()=>Error<T>("ReportNotFound","The report is not available.");
    private static Result<T> Validation<T>()=>Error<T>("ValidationFailed","The report manifest is invalid.");
    private static Result<T> Error<T>(string code,string message)=>Result<T>.Failure(new ApplicationErrorDetail(code,message));
}
