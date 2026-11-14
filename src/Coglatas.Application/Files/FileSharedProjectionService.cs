using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Files;

/// <summary>Read-only Shared projection candidate; intentionally absent from default DI.</summary>
public sealed class FileSharedProjectionService(
    IFileSharedProjectionRepository repository,
    IFileAuthorizationService fileAuthorization,
    IWorkspaceAuthorizationService workspaceAuthorization,
    IFileSharingService sharing,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant) : IFileSharedProjectionService
{
    public Task<Result<PagedResponse<FileSharedProjectionItemResponse>>> ListAsync(
        Guid workspaceId,
        string? direction,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        // Includes initial/final scope and per-file checks and the sharing
        // presentation's own policy queries on this same scoped context.
        repository.ReadCurrentAuthorizationAsync(
            token => ListCurrentAsync(workspaceId, direction, page, pageSize, token), cancellationToken);

    private async Task<Result<PagedResponse<FileSharedProjectionItemResponse>>> ListCurrentAsync(
        Guid workspaceId,
        string? direction,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } actor || actor == Guid.Empty)
        {
            return Failure("AuthenticationRequired", "Authentication is required.");
        }

        if (direction is not ("withMe" or "byMe"))
        {
            return Failure("ValidationFailed", "A supported sharing direction is required.", "direction");
        }
        if (workspaceId == Guid.Empty)
        {
            return Failure("ValidationFailed", "Workspace is required.", "workspaceId");
        }
        if (!currentTenant.IsAvailable || currentTenant.IsPlatformScope || currentTenant.TenantId == Guid.Empty ||
            !await fileAuthorization.CanViewWorkspaceFiles(actor, workspaceId, cancellationToken))
        {
            return Unavailable();
        }

        var tenant = currentTenant.TenantId;
        var canManage = await workspaceAuthorization.CanManageWorkspace(actor, workspaceId, cancellationToken);
        var scope = new FileSharedProjectionScope(tenant, actor, workspaceId,
            direction == "withMe" ? FileSharedDirection.WithMe : FileSharedDirection.ByMe, canManage);
        var result = await repository.ListAsync(scope, Math.Max(page, 1), Math.Clamp(pageSize, 1, 100), cancellationToken);

        // Recheck the current existing authority before projecting a retained page.
        // This is fail-closed defense, not a universal concurrent-revocation guarantee.
        if (!await HasCurrentScopeAuthorityAsync(scope, cancellationToken))
        {
            return Unavailable();
        }
        foreach (var attachment in result.Items)
        {
            if (!IsExactWorkspaceAttachment(attachment, scope) ||
                !await fileAuthorization.CanViewAttachment(actor, attachment, cancellationToken))
            {
                return Unavailable();
            }
        }

        var presentations = await sharing.GetListPresentationsAsync(workspaceId, actor,
            result.Items.Select(attachment => attachment.FileObject!).ToArray(), cancellationToken);
        // Sharing presentation is an awaited boundary: the same actor may have
        // lost membership/management or a directional grant while it was built.
        if (!await HasCurrentScopeAuthorityAsync(scope, cancellationToken))
        {
            return Unavailable();
        }
        var currentPage = await repository.ListAsync(scope, result.Page, result.PageSize, cancellationToken);
        if (!IsSameAuthorizedPage(result, currentPage) ||
            !await HasCurrentScopeAuthorityAsync(scope, cancellationToken))
        {
            // Invalidate the whole retained page, including its aggregate count.
            // Never drop denied items while returning the previous total.
            return Unavailable();
        }
        foreach (var attachment in currentPage.Items)
        {
            if (!IsExactWorkspaceAttachment(attachment, scope) ||
                !await fileAuthorization.CanViewAttachment(actor, attachment, cancellationToken))
            {
                return Unavailable();
            }
        }
        if (!HasSameIdentity(scope)) return Unavailable();

        var items = new List<FileSharedProjectionItemResponse>(currentPage.Items.Count);
        foreach (var attachment in currentPage.Items)
        {
            if (!presentations.TryGetValue(attachment.FileObjectId, out var presentation) ||
                presentation.SharingVersion != attachment.FileObject!.SharingVersion)
            {
                return Unavailable();
            }
            items.Add(new FileSharedProjectionItemResponse(
                attachment.Id, attachment.FileObjectId, workspaceId,
                attachment.FileObject!.OriginalFileName, attachment.FileObject.ContentType,
                attachment.FileObject.SizeBytes, presentation.AccessState,
                presentation.CanManageSharing, presentation.ExternalRecipientCount));
        }
        return Result<PagedResponse<FileSharedProjectionItemResponse>>.Success(
            new PagedResponse<FileSharedProjectionItemResponse>(items, currentPage.Page, currentPage.PageSize, currentPage.TotalCount));
    }

    private async Task<bool> HasCurrentScopeAuthorityAsync(FileSharedProjectionScope scope, CancellationToken cancellationToken)
    {
        if (!HasSameIdentity(scope) ||
            !await fileAuthorization.CanViewWorkspaceFiles(scope.ActorUserId, scope.WorkspaceId, cancellationToken))
        {
            return false;
        }
        var canManage = await workspaceAuthorization.CanManageWorkspace(scope.ActorUserId, scope.WorkspaceId, cancellationToken);
        return HasSameIdentity(scope) && canManage == scope.CanManageSharing;
    }

    private static bool IsSameAuthorizedPage(PagedResponse<Attachment> previous, PagedResponse<Attachment> current) =>
        previous.Page == current.Page && previous.PageSize == current.PageSize &&
        previous.TotalCount == current.TotalCount &&
        previous.Items.Select(attachment => (attachment.Id, attachment.FileObjectId))
            .SequenceEqual(current.Items.Select(attachment => (attachment.Id, attachment.FileObjectId)));

    private bool HasSameIdentity(FileSharedProjectionScope scope) =>
        currentUser.IsAuthenticated && currentUser.UserId == scope.ActorUserId && currentTenant.IsAvailable &&
        !currentTenant.IsPlatformScope && currentTenant.TenantId == scope.TenantId;

    private static bool IsExactWorkspaceAttachment(Attachment attachment, FileSharedProjectionScope scope) =>
        attachment.TenantId == scope.TenantId && attachment.WorkspaceId == scope.WorkspaceId &&
        attachment.OwnerType == AttachmentOwnerType.Workspace && attachment.OwnerId == scope.WorkspaceId &&
        !attachment.DeletedAt.HasValue && attachment.FileObject is { } file &&
        file.Id == attachment.FileObjectId && file.TenantId == scope.TenantId &&
        file.WorkspaceId == scope.WorkspaceId && !file.DeletedAt.HasValue && file.Status != FileObjectStatus.Deleted;

    private static Result<PagedResponse<FileSharedProjectionItemResponse>> Unavailable() =>
        Failure("SharedFileScopeUnavailable", "Workspace not found.");

    private static Result<PagedResponse<FileSharedProjectionItemResponse>> Failure(string code, string message, string? target = null) =>
        Result<PagedResponse<FileSharedProjectionItemResponse>>.Failure(new ApplicationErrorDetail(code, message, Target: target));
}
