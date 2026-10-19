using Coglatas.Application.Files;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>Composable existing-policy query; callers supply server-derived admission only.</summary>
public static class FileSharedProjectionQuery
{
    public static IQueryable<Attachment> Ordered(AppDbContext dbContext, FileSharedProjectionScope scope)
    {
        var directionalGrants = EffectiveFileAccessGrantQuery.For(dbContext)
            .Where(grant => grant.TenantId == scope.TenantId && grant.WorkspaceId == scope.WorkspaceId);
        directionalGrants = scope.Direction == FileSharedDirection.WithMe
            ? directionalGrants.Where(grant => grant.RecipientUserId == scope.ActorUserId)
            : directionalGrants.Where(grant => grant.GrantedByUserId == scope.ActorUserId);

        var effectiveRecipientFileIds = EffectiveFileAccessGrantQuery.For(dbContext)
            .Where(grant => grant.TenantId == scope.TenantId && grant.WorkspaceId == scope.WorkspaceId &&
                            grant.RecipientUserId == scope.ActorUserId)
            .Select(grant => grant.FileObjectId);
        var directionalFileIds = directionalGrants.Select(grant => grant.FileObjectId);
        var authorized = FileAttachmentQueryFilters.WorkspaceFiles(dbContext, scope.WorkspaceId)
            .Where(attachment => attachment.TenantId == scope.TenantId &&
                                 attachment.FileObject!.TenantId == scope.TenantId &&
                                 attachment.FileObject.WorkspaceId == scope.WorkspaceId &&
                                 directionalFileIds.Contains(attachment.FileObjectId));
        if (!scope.CanManageSharing)
        {
            // Same direct-Workspace discovery predicate as the existing Files inventory.
            // Direction still requires an effective recipient/grantor record above.
            authorized = authorized.Where(attachment =>
                attachment.FileObject!.SharingPolicy == FileSharingPolicy.Workspace ||
                attachment.UploadedByUserId == scope.ActorUserId || attachment.OwnerUserId == scope.ActorUserId ||
                effectiveRecipientFileIds.Contains(attachment.FileObjectId));
        }

        // Canonicalize only after current discovery authorization. A second Attachment
        // cannot duplicate counts or cause an unreadable first-row fallback.
        var canonical = authorized.Where(attachment => attachment.Id == authorized
            .Where(candidate => candidate.FileObjectId == attachment.FileObjectId)
            .OrderBy(candidate => candidate.CreatedAt).ThenBy(candidate => candidate.Id)
            .Select(candidate => candidate.Id).First());
        return canonical
            .OrderByDescending(attachment => directionalGrants
                .Where(grant => grant.FileObjectId == attachment.FileObjectId)
                .Max(grant => grant.CreatedAt))
            .ThenByDescending(attachment => attachment.FileObjectId);
    }
}
