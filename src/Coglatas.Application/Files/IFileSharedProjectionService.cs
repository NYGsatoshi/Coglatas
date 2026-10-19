using Coglatas.Application.Common;

namespace Coglatas.Application.Files;

public interface IFileSharedProjectionService
{
    Task<Result<PagedResponse<FileSharedProjectionItemResponse>>> ListAsync(
        Guid workspaceId,
        string? direction,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);
}
