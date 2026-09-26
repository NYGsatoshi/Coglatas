using Coglatas.Application.Common;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.Files;

public interface IFileSharingService
{
    Task<IReadOnlyDictionary<Guid, FileSharingPresentation>> GetListPresentationsAsync(
        Guid workspaceId,
        Guid actorUserId,
        IReadOnlyCollection<FileObject> files,
        CancellationToken cancellationToken = default);

    Task<Result<FileSharingResponse>> GetAsync(Guid fileObjectId, CancellationToken cancellationToken = default);

    Task<Result<FileSharingResponse>> UpdatePolicyAsync(
        Guid fileObjectId,
        FileSharingPolicyUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<FileSharingResponse>> GrantAsync(
        Guid fileObjectId,
        FileShareGrantCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<FileSharingResponse>> RevokeAsync(
        Guid fileObjectId,
        Guid grantId,
        long expectedSharingVersion,
        CancellationToken cancellationToken = default);
}
