using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Files;
using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>Existing-schema query candidate; no new grant, star, ownership or schema writes.</summary>
public sealed class FileSharedProjectionRepository(AppDbContext dbContext, ICurrentTenant currentTenant)
    : IFileSharedProjectionRepository
{
    public async Task<T> ReadCurrentAuthorizationAsync<T>(
        Func<CancellationToken, Task<T>> read,
        CancellationToken cancellationToken = default)
    {
        // Same scoped read pattern as FileRepository.ReadCurrentUploadAdmissionAsync.
        // All current Shared authorization callees use LINQ queries, not Find.
        // NoTracking bypasses stale identity-map materialization without clearing,
        // detaching, reloading or overwriting any pending tracked changes.
        var previousTracking = dbContext.ChangeTracker.QueryTrackingBehavior;
        try
        {
            dbContext.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            return await read(cancellationToken);
        }
        finally
        {
            dbContext.ChangeTracker.QueryTrackingBehavior = previousTracking;
        }
    }

    public async Task<PagedResponse<Attachment>> ListAsync(
        FileSharedProjectionScope scope,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(page, 1);
        var safeSize = Math.Clamp(pageSize, 1, 100);
        if (!currentTenant.IsAvailable || currentTenant.IsPlatformScope ||
            currentTenant.TenantId != scope.TenantId || scope.TenantId == Guid.Empty ||
            scope.WorkspaceId == Guid.Empty || scope.ActorUserId == Guid.Empty ||
            scope.Direction is not (FileSharedDirection.WithMe or FileSharedDirection.ByMe))
        {
            return new PagedResponse<Attachment>([], safePage, safeSize, 0);
        }

        var canonical = FileSharedProjectionQuery.Ordered(dbContext, scope);
        var total = await canonical.CountAsync(cancellationToken);
        var offset = ((long)safePage - 1) * safeSize;
        if (offset >= total)
        {
            return new PagedResponse<Attachment>([], safePage, safeSize, total);
        }
        var items = await canonical
            .Skip((int)offset).Take(safeSize)
            .Include(attachment => attachment.FileObject)
            .ToListAsync(cancellationToken);
        return new PagedResponse<Attachment>(items, safePage, safeSize, total);
    }
}
