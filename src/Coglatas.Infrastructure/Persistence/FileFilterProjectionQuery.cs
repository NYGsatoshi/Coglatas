using Coglatas.Application.Files;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>
/// Private review query. Caller must admit the current Workspace and optional Project first.
/// Per-attachment owner and direct-Workspace sharing predicates precede canonical selection.
/// </summary>
public static class FileFilterProjectionQuery
{
    public static IQueryable<Attachment> Build(
        AppDbContext db, FileFilterQuery filter, Guid actorUserId, bool canManageWorkspace)
    {
        var placements = db.Set<FileFolderPlacement>().AsNoTracking().Where(p =>
            p.TenantId == filter.TenantId && p.WorkspaceId == filter.WorkspaceId);
        var liveFolders = db.Set<FileFolder>().AsNoTracking().Where(f =>
            f.TenantId == filter.TenantId && f.WorkspaceId == filter.WorkspaceId && f.DeletedAt == null);
        var query = db.Attachments.AsNoTracking()
            .Where(a => a.TenantId == filter.TenantId && a.WorkspaceId == filter.WorkspaceId &&
                a.DeletedAt == null && a.FileObject != null &&
                a.FileObject.TenantId == filter.TenantId && a.FileObject.WorkspaceId == filter.WorkspaceId &&
                a.FileObject.DeletedAt == null &&
                (a.FileObject.Status == FileObjectStatus.Active || a.FileObject.Status == FileObjectStatus.Quarantined));
        if (filter.ProjectId.HasValue)
        {
            // No Project AttachmentOwnerType exists. Only live TaskItem associations implement
            // this branch; ArtifactVersion/Comment/ActivityLog must not enter via ProjectId.
            query = query.Where(a => a.FileObject!.ProjectId == filter.ProjectId &&
                a.OwnerType == AttachmentOwnerType.TaskItem && db.TaskItems.Any(t =>
                    t.Id == a.OwnerId && t.TenantId == filter.TenantId &&
                    t.WorkspaceId == filter.WorkspaceId && t.ProjectId == filter.ProjectId &&
                    t.DeletedAt == null));
        }
        else
        {
            var effective = EffectiveFileAccessGrantQuery.For(db).Where(g =>
                g.TenantId == filter.TenantId && g.WorkspaceId == filter.WorkspaceId &&
                g.RecipientUserId == actorUserId).Select(g => g.FileObjectId);
            query = query.Where(a => a.OwnerType == AttachmentOwnerType.Workspace &&
                a.OwnerId == filter.WorkspaceId && a.FileObject!.ProjectId == null &&
                (canManageWorkspace || a.FileObject.SharingPolicy == FileSharingPolicy.Workspace ||
                    a.UploadedByUserId == actorUserId || a.OwnerUserId == actorUserId ||
                    effective.Contains(a.FileObjectId)));
        }
        // A malformed placement in another Workspace is not an implicit root placement.
        query = query.Where(a => !db.Set<FileFolderPlacement>().Any(p =>
            p.TenantId == filter.TenantId && p.FileObjectId == a.FileObjectId && p.WorkspaceId != filter.WorkspaceId));
        // Stale placement targets never become visible Folder identity or candidates.
        query = query.Where(a => !placements.Any(p => p.FileObjectId == a.FileObjectId &&
            p.FolderId.HasValue && !liveFolders.Any(f => f.Id == p.FolderId)));
        if (filter.FolderId.HasValue)
            query = query.Where(a => placements.Any(p => p.FileObjectId == a.FileObjectId && p.FolderId == filter.FolderId));
        else if (filter.AtRoot)
            query = query.Where(a => !placements.Any(p => p.FileObjectId == a.FileObjectId && p.FolderId != null));

        // Choose only among admitted associations. An unauthorized earlier row cannot hide
        // the authorized association or contribute to totals. No graph is materialized here.
        var admitted = query;
        query = query.Where(a => !admitted.Any(other => other.FileObjectId == a.FileObjectId &&
            (other.CreatedAt < a.CreatedAt || (other.CreatedAt == a.CreatedAt && other.Id.CompareTo(a.Id) < 0))));
        return query;
    }

    public static IOrderedQueryable<Attachment> Order(
        IQueryable<Attachment> query, FileFilterQuery filter, bool postgres)
    {
        IOrderedQueryable<Attachment> ordered;
        if (filter.Sort == "name")
        {
            // Explicit PG key: btrim(space), upper under C, ordinal C comparison.
            // InMemory is diagnostic only and does not qualify provider Unicode behavior.
            if (postgres)
                ordered = filter.Descending
                    ? query.OrderByDescending(a => EF.Functions.Collate(EF.Functions.Collate(a.FileObject!.OriginalFileName.Trim(), "C").ToUpper(), "C"))
                    : query.OrderBy(a => EF.Functions.Collate(EF.Functions.Collate(a.FileObject!.OriginalFileName.Trim(), "C").ToUpper(), "C"));
            else
                ordered = filter.Descending
                    ? query.OrderByDescending(a => a.FileObject!.OriginalFileName.Trim().ToUpperInvariant())
                    : query.OrderBy(a => a.FileObject!.OriginalFileName.Trim().ToUpperInvariant());
        }
        else if (filter.Sort == "updatedAt")
            ordered = filter.Descending
                ? query.OrderByDescending(a => a.FileObject!.UpdatedAt ?? a.FileObject.CreatedAt)
                : query.OrderBy(a => a.FileObject!.UpdatedAt ?? a.FileObject.CreatedAt);
        else
            ordered = filter.Descending
                ? query.OrderByDescending(a => a.FileObject!.CreatedAt)
                : query.OrderBy(a => a.FileObject!.CreatedAt);
        return ordered.ThenBy(a => a.FileObjectId);
    }
}
