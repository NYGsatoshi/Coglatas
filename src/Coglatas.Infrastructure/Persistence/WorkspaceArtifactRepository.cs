using Coglatas.Application.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

// Private review candidate: no Product registration or schema/index changes.
public sealed class WorkspaceArtifactRepository(AppDbContext dbContext) : IWorkspaceArtifactRepository
{
    public async Task<IReadOnlyList<ArtifactListItemResponse>> ReadAsync(
        WorkspaceArtifactReadQuery query,
        CancellationToken cancellationToken = default) =>
        await WorkspaceArtifactReadScope.Query(dbContext, query).ToListAsync(cancellationToken);
}

public static class WorkspaceArtifactReadScope
{
    public static IQueryable<ArtifactListItemResponse> Query(AppDbContext dbContext, WorkspaceArtifactReadQuery request)
    {
        var visibleProjectIds = dbContext.VisibleProjectsFor(request.ActorUserId)
            .Where(project => project.TenantId == request.TenantId && project.WorkspaceId == request.WorkspaceId)
            .Select(project => project.Id);
        var query = dbContext.Artifacts.AsNoTracking()
            .Where(artifact => artifact.TenantId == request.TenantId && artifact.DeletedAt == null &&
                               visibleProjectIds.Contains(artifact.ProjectId));

        if (request.Query.Length > 0)
        {
            var pattern = "%" + request.Query.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            // Parameterized provider candidate. Exact Unicode ordinal equivalence remains a review gate.
            query = query.Where(artifact => EF.Functions.ILike(artifact.Name, pattern, "\\") ||
                artifact.Description != null && EF.Functions.ILike(artifact.Description, pattern, "\\"));
        }
        if (request.After is { } after)
        {
            query = query.Where(artifact => (artifact.UpdatedAt ?? artifact.CreatedAt) < after.UpdatedAtUtc ||
                (artifact.UpdatedAt ?? artifact.CreatedAt) == after.UpdatedAtUtc && artifact.Id.CompareTo(after.Id) < 0);
        }

        return query.OrderByDescending(artifact => artifact.UpdatedAt ?? artifact.CreatedAt)
            .ThenByDescending(artifact => artifact.Id)
            .Take(Math.Clamp(request.PageSize, 1, 100) + 1)
            .Select(artifact => new ArtifactListItemResponse(
                artifact.Id, artifact.ProjectId, artifact.Name, artifact.Description,
                artifact.ArtifactType, artifact.Status, artifact.CurrentVersionId,
                artifact.CreatedByUserId, artifact.CreatedAt, artifact.UpdatedAt));
    }
}
