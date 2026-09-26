using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

public sealed class ArtifactEvidenceRepository(AppDbContext dbContext) : IArtifactEvidenceRepository
{
    public Task<bool> HasClaimsAsync(Guid artifactVersionId, CancellationToken cancellationToken = default) =>
        dbContext.Set<ArtifactClaim>()
            .AsNoTracking()
            .AnyAsync(claim => claim.ArtifactVersionId == artifactVersionId, cancellationToken);

    public async Task<IReadOnlyList<ArtifactClaim>> ListClaimsAsync(
        Guid artifactVersionId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Set<ArtifactClaim>()
            .AsNoTracking()
            .Include(claim => claim.Evidence)
            .Where(claim => claim.ArtifactVersionId == artifactVersionId)
            .OrderBy(claim => claim.Ordinal)
            .Take(200)
            .ToListAsync(cancellationToken);
    }

    public async Task AddClaimsAsync(
        IReadOnlyCollection<ArtifactClaim> claims,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Set<ArtifactClaim>().AddRangeAsync(claims, cancellationToken);
    }
}
