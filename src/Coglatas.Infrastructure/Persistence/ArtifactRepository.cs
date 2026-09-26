using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

public sealed class ArtifactRepository(AppDbContext dbContext) : IArtifactRepository
{
    public async Task<IReadOnlyList<Artifact>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Artifacts
            .AsNoTracking()
            .Where(artifact => artifact.ProjectId == projectId)
            .OrderByDescending(artifact => artifact.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<Artifact?> GetArtifactAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        return dbContext.Artifacts.FirstOrDefaultAsync(artifact => artifact.Id == artifactId, cancellationToken);
    }

    public Task<ArtifactVersion?> GetVersionAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        return dbContext.ArtifactVersions
            .Include(version => version.Artifact)
            .Include(version => version.Attachment)
            .Include(version => version.FileObject)
            .FirstOrDefaultAsync(version => version.Id == versionId, cancellationToken);
    }

    public async Task<IReadOnlyList<ArtifactVersion>> ListVersionsAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        return await dbContext.ArtifactVersions
            .AsNoTracking()
            .Include(version => version.Attachment)
            .Include(version => version.FileObject)
            .Where(version => version.ArtifactId == artifactId)
            .OrderByDescending(version => version.VersionNumber)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetNextVersionNumberAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        var current = await dbContext.ArtifactVersions
            .Where(version => version.ArtifactId == artifactId)
            .Select(version => (int?)version.VersionNumber)
            .MaxAsync(cancellationToken);

        return (current ?? 0) + 1;
    }

    public async Task AddArtifactAsync(Artifact artifact, CancellationToken cancellationToken = default)
    {
        await dbContext.Artifacts.AddAsync(artifact, cancellationToken);
    }

    public async Task AddVersionAsync(ArtifactVersion version, CancellationToken cancellationToken = default)
    {
        await dbContext.ArtifactVersions.AddAsync(version, cancellationToken);
    }
}
