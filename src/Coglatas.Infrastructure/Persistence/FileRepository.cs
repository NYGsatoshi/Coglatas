using System.Data;
using System.Data.Common;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

public sealed class FileRepository(AppDbContext dbContext) : IFileRepository
{
    public Task<FileObject?> GetFileObjectAsync(Guid fileObjectId, CancellationToken cancellationToken = default)
    {
        return dbContext.FileObjects.FirstOrDefaultAsync(file => file.Id == fileObjectId, cancellationToken);
    }

    public async Task<PagedResponse<Attachment>> ListWorkspaceFileObjectsAsync(
        Guid workspaceId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        return await ListWorkspaceFileObjectsCoreAsync(
            workspaceId,
            null,
            canManageSharing: true,
            page,
            pageSize,
            cancellationToken);
    }

    public async Task<PagedResponse<Attachment>> ListAccessibleWorkspaceFileObjectsAsync(
        Guid workspaceId,
        Guid userId,
        bool canManageSharing,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        return await ListWorkspaceFileObjectsCoreAsync(
            workspaceId,
            userId,
            canManageSharing,
            page,
            pageSize,
            cancellationToken);
    }

    private async Task<PagedResponse<Attachment>> ListWorkspaceFileObjectsCoreAsync(
        Guid workspaceId,
        Guid? userId,
        bool canManageSharing,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Attachments
            .AsNoTracking()
            .Include(attachment => attachment.FileObject)
            .ThenInclude(file => file!.UploadedByUser)
            .Include(attachment => attachment.UploadedByUser)
            .Where(attachment =>
                attachment.WorkspaceId == workspaceId &&
                attachment.OwnerType == AttachmentOwnerType.Workspace &&
                attachment.OwnerId == workspaceId &&
                !attachment.DeletedAt.HasValue &&
                attachment.FileObject != null &&
                !attachment.FileObject.DeletedAt.HasValue &&
                attachment.FileObject.Status != FileObjectStatus.Deleted);

        // This is the metadata-discovery boundary for the Files inventory.
        // Never return a Private File merely because the caller can see the
        // Workspace: explicit grants and direct-file ownership are evaluated
        // in the query, not reconstructed by the browser.
        if (!canManageSharing && userId.HasValue)
        {
            var currentUserId = userId.Value;
            var effectiveGrantFileObjectIds = EffectiveFileAccessGrantQuery.For(dbContext)
                .Where(grant =>
                    grant.WorkspaceId == workspaceId &&
                    grant.RecipientUserId == currentUserId)
                .Select(grant => grant.FileObjectId);
            query = query.Where(attachment =>
                attachment.FileObject!.SharingPolicy == FileSharingPolicy.Workspace ||
                attachment.UploadedByUserId == currentUserId ||
                attachment.OwnerUserId == currentUserId ||
                effectiveGrantFileObjectIds.Contains(attachment.FileObjectId));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(attachment => attachment.FileObject!.CreatedAt)
            .ThenByDescending(attachment => attachment.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResponse<Attachment>(items, page, pageSize, total);
    }


    public async Task AddFileObjectAsync(FileObject fileObject, CancellationToken cancellationToken = default)
    {
        await dbContext.FileObjects.AddAsync(fileObject, cancellationToken);
    }

    public Task<Attachment?> GetAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        return dbContext.Attachments
            .Include(attachment => attachment.FileObject)
            .FirstOrDefaultAsync(attachment => attachment.Id == attachmentId, cancellationToken);
    }

    public Task<Attachment?> GetAttachmentByFileObjectAsync(Guid fileObjectId, CancellationToken cancellationToken = default)
    {
        return dbContext.Attachments
            .Include(attachment => attachment.FileObject)
            .FirstOrDefaultAsync(attachment => attachment.FileObjectId == fileObjectId, cancellationToken);
    }

    public async Task AddAttachmentAsync(Attachment attachment, CancellationToken cancellationToken = default)
    {
        await dbContext.Attachments.AddAsync(attachment, cancellationToken);
    }

    public async Task<IReadOnlyList<FileVersionRecord>> ListFileVersionsAsync(
        Guid tenantId,
        Guid fileObjectId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var safeLimit = Math.Clamp(limit, 1, 100);
        return await ReadFileVersionsAsync(
            tenantId,
            fileObjectId,
            versionId: null,
            safeLimit,
            cancellationToken);
    }

    public async Task<FileVersionRecord?> GetFileVersionAsync(
        Guid tenantId,
        Guid fileObjectId,
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        var versions = await ReadFileVersionsAsync(
            tenantId,
            fileObjectId,
            versionId,
            limit: 1,
            cancellationToken);
        return versions.FirstOrDefault();
    }

    public async Task<IReadOnlyList<FileSharingActivityRecord>> ListFileSharingActivityAsync(
        Guid tenantId,
        Guid fileObjectId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var safeLimit = Math.Clamp(limit, 1, 100);
        return await dbContext.AuditLogs
            .AsNoTracking()
            .Where(log =>
                log.TenantId == tenantId &&
                log.EntityType == "FileObject" &&
                log.EntityId == fileObjectId &&
                log.Action == "FileSharingChanged")
            .OrderByDescending(log => log.CreatedAt)
            .ThenByDescending(log => log.Id)
            .Take(safeLimit)
            .Select(log => new FileSharingActivityRecord(
                log.Id,
                log.ActorUser != null ? log.ActorUser.DisplayName : string.Empty,
                log.CreatedAt,
                log.MetadataJson))
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<FileVersionRecord>> ReadFileVersionsAsync(
        Guid tenantId,
        Guid fileObjectId,
        Guid? versionId,
        int limit,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    fv."Id",
                    fv."FileObjectId",
                    fv."VersionNumber",
                    fv."OriginalFileName",
                    fv."StorageKey",
                    fv."ContentType",
                    fv."SizeBytes",
                    fv."HashSha256",
                    fv."CreatedByUserId",
                    COALESCE(NULLIF(u."DisplayName", ''), 'Unknown user') AS "CreatedByDisplayName",
                    fv."CreatedAt"
                FROM file_versions AS fv
                LEFT JOIN users AS u ON u."Id" = fv."CreatedByUserId"
                WHERE fv."TenantId" = @tenantId
                  AND fv."FileObjectId" = @fileObjectId
                  AND (@versionId IS NULL OR fv."Id" = @versionId)
                ORDER BY fv."VersionNumber" DESC, fv."CreatedAt" DESC, fv."Id" DESC
                LIMIT @limit;
                """;
            AddParameter(command, "@tenantId", tenantId);
            AddParameter(command, "@fileObjectId", fileObjectId);
            AddParameter(command, "@versionId", versionId.HasValue ? versionId.Value : DBNull.Value);
            AddParameter(command, "@limit", limit);

            var versions = new List<FileVersionRecord>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                versions.Add(new FileVersionRecord(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetInt64(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.GetGuid(8),
                    reader.GetString(9),
                    reader.GetFieldValue<DateTimeOffset>(10)));
            }
            return versions;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    public async Task<IReadOnlyList<Attachment>> ListTaskAttachmentsAsync(Guid taskItemId, CancellationToken cancellationToken = default) =>
        await dbContext.Attachments.Include(x => x.FileObject).Where(x => x.OwnerType == AttachmentOwnerType.TaskItem && x.OwnerId == taskItemId && !x.DeletedAt.HasValue).OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken);

    public async Task<PagedResponse<Attachment>> ListTaskAttachmentsPageAsync(Guid taskItemId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Attachments.Include(x => x.FileObject)
            .Where(x => x.OwnerType == AttachmentOwnerType.TaskItem && x.OwnerId == taskItemId && !x.DeletedAt.HasValue);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResponse<Attachment>(items, page, pageSize, total);
    }

    public void RemoveAttachment(Attachment attachment) => dbContext.Attachments.Remove(attachment);

    public async Task<FileOwnerContext?> ResolveOwnerAsync(AttachmentOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default)
    {
        return ownerType switch
        {
            AttachmentOwnerType.Workspace => await ResolveWorkspaceAsync(ownerId, cancellationToken),
            AttachmentOwnerType.Message => await ResolveMessageAsync(ownerId, cancellationToken),
            AttachmentOwnerType.Post => await ResolvePostAsync(ownerId, cancellationToken),
            AttachmentOwnerType.TaskItem => await ResolveTaskAsync(ownerId, cancellationToken),
            AttachmentOwnerType.ArtifactVersion => await ResolveArtifactVersionAsync(ownerId, cancellationToken),
            AttachmentOwnerType.Comment => await ResolveCommentAsync(ownerId, cancellationToken),
            AttachmentOwnerType.ActivityLog => await ResolveActivityLogAsync(ownerId, cancellationToken),
            _ => null
        };
    }

    private Task<FileOwnerContext?> ResolveWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        return dbContext.Workspaces
            .AsNoTracking()
            .Where(workspace => workspace.Id == workspaceId && workspace.Status == WorkspaceStatus.Active)
            .Select(workspace => new FileOwnerContext(workspace.Id))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private Task<FileOwnerContext?> ResolveMessageAsync(Guid messageId, CancellationToken cancellationToken)
    {
        return dbContext.Messages
            .AsNoTracking()
            .Where(message => message.Id == messageId && !message.DeletedAt.HasValue)
            .Select(message => new FileOwnerContext(message.Conversation!.WorkspaceId, null, message.ConversationId, null, message.AuthorUserId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private Task<FileOwnerContext?> ResolvePostAsync(Guid postId, CancellationToken cancellationToken)
    {
        return dbContext.Posts
            .AsNoTracking()
            .Where(post => post.Id == postId && !post.DeletedAt.HasValue)
            .Select(post => new FileOwnerContext(post.Channel!.WorkspaceId, null, null, post.ChannelId, post.AuthorUserId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private Task<FileOwnerContext?> ResolveTaskAsync(Guid taskId, CancellationToken cancellationToken)
    {
        return dbContext.TaskItems
            .AsNoTracking()
            .Where(task => task.Id == taskId && !task.DeletedAt.HasValue)
            .Select(task => new FileOwnerContext(task.Project!.WorkspaceId, task.ProjectId, null, null, task.CreatedByUserId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private Task<FileOwnerContext?> ResolveArtifactVersionAsync(Guid versionId, CancellationToken cancellationToken)
    {
        return dbContext.ArtifactVersions
            .AsNoTracking()
            .Where(version => version.Id == versionId && !version.DeletedAt.HasValue && !version.Artifact!.DeletedAt.HasValue)
            .Select(version => new FileOwnerContext(version.Artifact!.Project!.WorkspaceId, version.Artifact.ProjectId, null, null, version.CreatedByUserId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<FileOwnerContext?> ResolveCommentAsync(Guid commentId, CancellationToken cancellationToken)
    {
        var comment = await dbContext.Comments
            .AsNoTracking()
            .Where(comment => comment.Id == commentId && !comment.DeletedAt.HasValue)
            .FirstOrDefaultAsync(cancellationToken);

        if (comment is null)
        {
            return null;
        }

        var projectId = comment.TargetType switch
        {
            CommentTargetType.Project => comment.TargetId,
            CommentTargetType.TaskItem => await dbContext.TaskItems.AsNoTracking().Where(task => task.Id == comment.TargetId).Select(task => (Guid?)task.ProjectId).FirstOrDefaultAsync(cancellationToken),
            CommentTargetType.Milestone => await dbContext.Milestones.AsNoTracking().Where(milestone => milestone.Id == comment.TargetId).Select(milestone => (Guid?)milestone.ProjectId).FirstOrDefaultAsync(cancellationToken),
            CommentTargetType.Artifact => await dbContext.Artifacts.AsNoTracking().Where(artifact => artifact.Id == comment.TargetId).Select(artifact => (Guid?)artifact.ProjectId).FirstOrDefaultAsync(cancellationToken),
            CommentTargetType.ArtifactVersion => await dbContext.ArtifactVersions.AsNoTracking().Where(version => version.Id == comment.TargetId).Select(version => (Guid?)version.Artifact!.ProjectId).FirstOrDefaultAsync(cancellationToken),
            CommentTargetType.ActivityLog => await dbContext.ActivityLogs.AsNoTracking().Where(log => log.Id == comment.TargetId).Select(log => (Guid?)log.ProjectId).FirstOrDefaultAsync(cancellationToken),
            _ => null
        };

        return new FileOwnerContext(comment.WorkspaceId, projectId, null, null, comment.AuthorUserId);
    }

    private Task<FileOwnerContext?> ResolveActivityLogAsync(Guid activityLogId, CancellationToken cancellationToken)
    {
        return dbContext.ActivityLogs
            .AsNoTracking()
            .Where(log => log.Id == activityLogId)
            .Select(log => new FileOwnerContext(log.Project!.WorkspaceId, log.ProjectId, null, null, log.AuthorUserId))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
