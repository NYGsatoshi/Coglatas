using Coglatas.Application.Common;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.Files;

public enum FileSharedDirection
{
    WithMe,
    ByMe
}

public sealed record FileSharedProjectionItemResponse(
    Guid Id,
    Guid FileObjectId,
    Guid WorkspaceId,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string AccessState,
    bool CanManageSharing,
    int? ExternalRecipientCount);

/// <summary>Server-derived read admission, never a browser-supplied authority.</summary>
public sealed record FileSharedProjectionScope(
    Guid TenantId,
    Guid ActorUserId,
    Guid WorkspaceId,
    FileSharedDirection Direction,
    bool CanManageSharing);

public interface IFileSharedProjectionRepository
{
    // Execute the existing policy read graph against persisted state without
    // substituting earlier tracked entities; implementations must restore mode.
    Task<T> ReadCurrentAuthorizationAsync<T>(
        Func<CancellationToken, Task<T>> read,
        CancellationToken cancellationToken = default);

    Task<PagedResponse<Attachment>> ListAsync(
        FileSharedProjectionScope scope,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
