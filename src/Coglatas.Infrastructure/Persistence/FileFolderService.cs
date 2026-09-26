using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Files;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

public sealed class FileFolderService(
    AppDbContext dbContext,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    IWorkspaceAuthorizationService workspaceAuthorization,
    IFileAuthorizationService fileAuthorization) : IFileFolderService
{
    private const int MaxHierarchyDepth = 256;

    public async Task<Result<FileFolderNavigationResponse>> ListAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId();
        if (workspaceId == Guid.Empty || userId is null || !CurrentTenantAvailable() ||
            !await workspaceAuthorization.CanViewWorkspace(userId.Value, workspaceId, cancellationToken))
        {
            return Result<FileFolderNavigationResponse>.Failure("Workspace not found.");
        }

        var folders = await dbContext.Set<FileFolder>()
            .AsNoTracking()
            .Where(folder =>
                folder.TenantId == currentTenant.TenantId &&
                folder.WorkspaceId == workspaceId &&
                folder.DeletedAt == null)
            .OrderBy(folder => folder.ParentFolderId)
            .ThenBy(folder => folder.SortOrder)
            .ThenBy(folder => folder.Name)
            .ThenBy(folder => folder.Id)
            .Select(folder => new FileFolderResponse(
                folder.Id,
                folder.WorkspaceId,
                folder.ParentFolderId,
                folder.Name,
                folder.SortOrder,
                folder.Version))
            .ToListAsync(cancellationToken);

        var rootVersion = await dbContext.Set<FileFolderRootState>()
            .AsNoTracking()
            .Where(root =>
                root.TenantId == currentTenant.TenantId &&
                root.WorkspaceId == workspaceId)
            .Select(root => root.Version)
            .SingleOrDefaultAsync(cancellationToken);

        return Result<FileFolderNavigationResponse>.Success(new FileFolderNavigationResponse(
            workspaceId,
            rootVersion,
            folders));
    }

    public async Task<Result<FileFolderResponse>> CreateAsync(
        FileFolderCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId();
        var name = request.Name?.Trim() ?? string.Empty;
        if (request.WorkspaceId == Guid.Empty || userId is null || !CurrentTenantAvailable() ||
            !await workspaceAuthorization.CanContributeWorkspace(userId.Value, request.WorkspaceId, cancellationToken))
        {
            return Result<FileFolderResponse>.Failure("Workspace not found.");
        }

        if (name.Length is < 1 or > 180 || name.Any(char.IsControl) || name.Contains('/') || name.Contains('\\'))
        {
            return Result<FileFolderResponse>.Failure("Folder name is invalid.");
        }

        var parentContainer = await ResolveContainerAsync(request.WorkspaceId, request.ParentFolderId, cancellationToken);
        if (parentContainer is null)
        {
            return Result<FileFolderResponse>.Failure("Destination folder not found.");
        }

        var duplicate = await dbContext.Set<FileFolder>().AnyAsync(folder =>
            folder.TenantId == currentTenant.TenantId &&
            folder.WorkspaceId == request.WorkspaceId &&
            folder.ParentFolderId == request.ParentFolderId &&
            folder.DeletedAt == null &&
            folder.Name == name,
            cancellationToken);
        if (duplicate)
        {
            return Result<FileFolderResponse>.Failure("A folder with the same name already exists at this location.");
        }

        var sortOrder = await NextSortOrderAsync(request.WorkspaceId, request.ParentFolderId, cancellationToken);
        var folder = new FileFolder
        {
            TenantId = currentTenant.TenantId,
            WorkspaceId = request.WorkspaceId,
            ParentFolderId = request.ParentFolderId,
            Name = name,
            SortOrder = sortOrder,
            Version = 1,
        };
        dbContext.Set<FileFolder>().Add(folder);
        parentContainer.Advance(dbContext, currentTenant.TenantId);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<FileFolderResponse>.Failure("Folder hierarchy changed. Refresh and try again.");
        }
        catch (DbUpdateException)
        {
            return Result<FileFolderResponse>.Failure("Folder hierarchy changed. Refresh and try again.");
        }

        return Result<FileFolderResponse>.Success(ToResponse(folder));
    }

    public async Task<Result<FileLocationResponse>> GetFileLocationAsync(
        Guid fileObjectId,
        CancellationToken cancellationToken = default)
    {
        var authorized = await ResolveAuthorizedWorkspaceFileAsync(fileObjectId, requireContribution: false, cancellationToken);
        if (authorized is null)
        {
            return Result<FileLocationResponse>.Failure("File not found.");
        }

        var placement = await dbContext.Set<FileFolderPlacement>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.TenantId == currentTenant.TenantId &&
                candidate.WorkspaceId == authorized.Value.WorkspaceId &&
                candidate.FileObjectId == fileObjectId,
                cancellationToken);

        return Result<FileLocationResponse>.Success(new FileLocationResponse(
            fileObjectId,
            authorized.Value.WorkspaceId,
            placement?.FolderId,
            placement?.Version ?? 0));
    }

    public async Task<Result<FileLocationResponse>> MoveFileAsync(
        Guid fileObjectId,
        FileMoveRequest request,
        CancellationToken cancellationToken = default)
    {
        var authorized = await ResolveAuthorizedWorkspaceFileAsync(fileObjectId, requireContribution: true, cancellationToken);
        if (authorized is null)
        {
            return Result<FileLocationResponse>.Failure("File not found.");
        }

        if (request.ExpectedVersion < 0 || request.ExpectedDestinationVersion < 0)
        {
            return Result<FileLocationResponse>.Failure("Invalid file move version.");
        }

        var workspaceId = authorized.Value.WorkspaceId;
        var placement = await dbContext.Set<FileFolderPlacement>()
            .SingleOrDefaultAsync(candidate =>
                candidate.TenantId == currentTenant.TenantId &&
                candidate.WorkspaceId == workspaceId &&
                candidate.FileObjectId == fileObjectId,
                cancellationToken);

        var currentVersion = placement?.Version ?? 0;
        var currentFolderId = placement?.FolderId;
        if (currentVersion != request.ExpectedVersion)
        {
            return Result<FileLocationResponse>.Failure("File location changed. Refresh and choose the destination again.");
        }

        var destination = await ResolveContainerAsync(workspaceId, request.DestinationFolderId, cancellationToken);
        if (destination is null)
        {
            return Result<FileLocationResponse>.Failure("Destination folder not found.");
        }
        if (destination.Version != request.ExpectedDestinationVersion)
        {
            return Result<FileLocationResponse>.Failure("Destination changed. Refresh and choose the destination again.");
        }

        if (currentFolderId == request.DestinationFolderId)
        {
            return Result<FileLocationResponse>.Success(new FileLocationResponse(
                fileObjectId,
                workspaceId,
                currentFolderId,
                currentVersion));
        }

        var sourceContainer = await ResolveContainerAsync(workspaceId, currentFolderId, cancellationToken);
        if (sourceContainer is null)
        {
            return Result<FileLocationResponse>.Failure("File location changed. Refresh and choose the destination again.");
        }

        sourceContainer.Advance(dbContext, currentTenant.TenantId);
        destination.Advance(dbContext, currentTenant.TenantId);

        if (placement is null)
        {
            placement = new FileFolderPlacement
            {
                TenantId = currentTenant.TenantId,
                WorkspaceId = workspaceId,
                FileObjectId = fileObjectId,
                FolderId = request.DestinationFolderId,
                Version = 1,
            };
            dbContext.Set<FileFolderPlacement>().Add(placement);
        }
        else
        {
            placement.FolderId = request.DestinationFolderId;
            placement.Version = checked(placement.Version + 1);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<FileLocationResponse>.Failure("File location or destination changed. Refresh and choose the destination again.");
        }
        catch (DbUpdateException)
        {
            // Root-state and first-placement rows both use unique keys. A race
            // on either one is a stale move and must never overwrite the winner.
            return Result<FileLocationResponse>.Failure("File location or destination changed. Refresh and choose the destination again.");
        }

        return Result<FileLocationResponse>.Success(new FileLocationResponse(
            fileObjectId,
            workspaceId,
            placement.FolderId,
            placement.Version));
    }

    public async Task<Result<FileFolderResponse>> MoveFolderAsync(
        Guid folderId,
        FileFolderMoveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (folderId == Guid.Empty || request.ExpectedVersion < 1 || request.ExpectedDestinationVersion < 0)
        {
            return Result<FileFolderResponse>.Failure("Folder not found.");
        }

        var userId = CurrentUserId();
        if (userId is null || !CurrentTenantAvailable())
        {
            return Result<FileFolderResponse>.Failure("Folder not found.");
        }

        var folder = await dbContext.Set<FileFolder>().SingleOrDefaultAsync(candidate =>
            candidate.Id == folderId &&
            candidate.TenantId == currentTenant.TenantId &&
            candidate.DeletedAt == null,
            cancellationToken);
        if (folder is null ||
            !await workspaceAuthorization.CanContributeWorkspace(userId.Value, folder.WorkspaceId, cancellationToken))
        {
            return Result<FileFolderResponse>.Failure("Folder not found.");
        }

        if (folder.Version != request.ExpectedVersion)
        {
            return Result<FileFolderResponse>.Failure("Folder changed. Refresh and choose the destination again.");
        }
        if (request.DestinationParentFolderId == folder.Id)
        {
            return Result<FileFolderResponse>.Failure("A folder cannot be moved into itself.");
        }
        if (request.DestinationParentFolderId.HasValue &&
            await WouldCreateCycleAsync(folder, request.DestinationParentFolderId.Value, cancellationToken))
        {
            return Result<FileFolderResponse>.Failure("A folder cannot be moved into one of its descendants.");
        }

        // Source and destination are resolved from authoritative Folder/root
        // metadata inside the same Tenant/Workspace. Storage paths never take
        // part in Move authorization or concurrency.
        var destination = await ResolveContainerAsync(
            folder.WorkspaceId,
            request.DestinationParentFolderId,
            cancellationToken);
        if (destination is null)
        {
            return Result<FileFolderResponse>.Failure("Destination folder not found.");
        }
        if (destination.Version != request.ExpectedDestinationVersion)
        {
            return Result<FileFolderResponse>.Failure("Destination changed. Refresh and choose the destination again.");
        }

        if (folder.ParentFolderId == request.DestinationParentFolderId)
        {
            return Result<FileFolderResponse>.Success(ToResponse(folder));
        }

        var sourceContainer = await ResolveContainerAsync(folder.WorkspaceId, folder.ParentFolderId, cancellationToken);
        if (sourceContainer is null)
        {
            return Result<FileFolderResponse>.Failure("Folder hierarchy changed. Refresh and choose the destination again.");
        }

        sourceContainer.Advance(dbContext, currentTenant.TenantId);
        destination.Advance(dbContext, currentTenant.TenantId);
        folder.ParentFolderId = request.DestinationParentFolderId;
        folder.SortOrder = await NextSortOrderAsync(folder.WorkspaceId, request.DestinationParentFolderId, cancellationToken);
        folder.Version = checked(folder.Version + 1);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<FileFolderResponse>.Failure("Folder or destination changed. Refresh and choose the destination again.");
        }
        catch (DbUpdateException)
        {
            return Result<FileFolderResponse>.Failure("Folder or destination changed. Refresh and choose the destination again.");
        }

        return Result<FileFolderResponse>.Success(ToResponse(folder));
    }

    private async Task<(Guid WorkspaceId, Attachment Attachment)?> ResolveAuthorizedWorkspaceFileAsync(
        Guid fileObjectId,
        bool requireContribution,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (fileObjectId == Guid.Empty || userId is null || !CurrentTenantAvailable())
        {
            return null;
        }

        var attachment = await dbContext.Set<Attachment>()
            .Include(candidate => candidate.FileObject)
            .OrderBy(candidate => candidate.Id)
            .FirstOrDefaultAsync(candidate =>
                candidate.TenantId == currentTenant.TenantId &&
                candidate.FileObjectId == fileObjectId &&
                candidate.OwnerType == AttachmentOwnerType.Workspace &&
                candidate.OwnerId == candidate.WorkspaceId &&
                candidate.DeletedAt == null &&
                candidate.FileObject != null &&
                candidate.FileObject.TenantId == currentTenant.TenantId &&
                candidate.FileObject.WorkspaceId == candidate.WorkspaceId &&
                candidate.FileObject.Status == FileObjectStatus.Active &&
                candidate.FileObject.DeletedAt == null,
                cancellationToken);
        if (attachment is null)
        {
            return null;
        }

        var workspaceAllowed = requireContribution
            ? await workspaceAuthorization.CanContributeWorkspace(userId.Value, attachment.WorkspaceId, cancellationToken)
            : await workspaceAuthorization.CanViewWorkspace(userId.Value, attachment.WorkspaceId, cancellationToken);
        if (!workspaceAllowed ||
            !await fileAuthorization.CanViewAttachment(userId.Value, attachment, cancellationToken))
        {
            return null;
        }

        return (attachment.WorkspaceId, attachment);
    }

    private async Task<ContainerState?> ResolveContainerAsync(
        Guid workspaceId,
        Guid? folderId,
        CancellationToken cancellationToken)
    {
        if (folderId.HasValue)
        {
            var folder = await dbContext.Set<FileFolder>().SingleOrDefaultAsync(candidate =>
                candidate.Id == folderId.Value &&
                candidate.TenantId == currentTenant.TenantId &&
                candidate.WorkspaceId == workspaceId &&
                candidate.DeletedAt == null,
                cancellationToken);
            return folder is null ? null : new ContainerState(workspaceId, folder, root: null);
        }

        var root = await dbContext.Set<FileFolderRootState>().SingleOrDefaultAsync(candidate =>
            candidate.TenantId == currentTenant.TenantId &&
            candidate.WorkspaceId == workspaceId,
            cancellationToken);
        return new ContainerState(workspaceId, folder: null, root);
    }

    private async Task<int> NextSortOrderAsync(
        Guid workspaceId,
        Guid? parentFolderId,
        CancellationToken cancellationToken)
    {
        var max = await dbContext.Set<FileFolder>()
            .Where(folder =>
                folder.TenantId == currentTenant.TenantId &&
                folder.WorkspaceId == workspaceId &&
                folder.ParentFolderId == parentFolderId &&
                folder.DeletedAt == null)
            .Select(folder => (int?)folder.SortOrder)
            .MaxAsync(cancellationToken);
        return checked((max ?? -1) + 1);
    }

    private async Task<bool> WouldCreateCycleAsync(
        FileFolder source,
        Guid destinationFolderId,
        CancellationToken cancellationToken)
    {
        var parentById = await dbContext.Set<FileFolder>()
            .AsNoTracking()
            .Where(folder =>
                folder.TenantId == currentTenant.TenantId &&
                folder.WorkspaceId == source.WorkspaceId &&
                folder.DeletedAt == null)
            .Select(folder => new { folder.Id, folder.ParentFolderId })
            .ToDictionaryAsync(folder => folder.Id, folder => folder.ParentFolderId, cancellationToken);

        Guid? cursor = destinationFolderId;
        for (var depth = 0; cursor.HasValue && depth < MaxHierarchyDepth; depth += 1)
        {
            if (cursor.Value == source.Id)
            {
                return true;
            }
            if (!parentById.TryGetValue(cursor.Value, out cursor))
            {
                return false;
            }
        }

        // Existing corrupted/cyclic trees fail closed instead of accepting a
        // move whose ancestry cannot be proven safe.
        return cursor.HasValue;
    }

    private bool CurrentTenantAvailable() =>
        currentTenant.IsAvailable && !currentTenant.IsPlatformScope && currentTenant.TenantId != Guid.Empty;

    private Guid? CurrentUserId() =>
        currentUser.IsAuthenticated && currentUser.UserId is { } userId && userId != Guid.Empty
            ? userId
            : null;

    private static FileFolderResponse ToResponse(FileFolder folder) => new(
        folder.Id,
        folder.WorkspaceId,
        folder.ParentFolderId,
        folder.Name,
        folder.SortOrder,
        folder.Version);

    private sealed class ContainerState(Guid workspaceId, FileFolder? folder, FileFolderRootState? root)
    {
        public long Version => folder?.Version ?? Root?.Version ?? 0;

        private FileFolderRootState? Root { get; set; } = root;

        public void Advance(AppDbContext context, Guid tenantId)
        {
            if (folder is not null)
            {
                folder.Version = checked(folder.Version + 1);
                return;
            }

            if (Root is not null)
            {
                Root.Version = checked(Root.Version + 1);
                return;
            }

            Root = new FileFolderRootState
            {
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                Version = 1,
            };
            context.Set<FileFolderRootState>().Add(Root);
        }
    }
}
