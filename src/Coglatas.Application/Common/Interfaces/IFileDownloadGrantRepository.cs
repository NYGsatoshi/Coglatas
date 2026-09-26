using Coglatas.Domain.Entities;

namespace Coglatas.Application.Common.Interfaces;

public interface IFileDownloadGrantRepository
{
    Task<FileDownloadGrant?> GetAsync(Guid fileDownloadGrantId, CancellationToken cancellationToken = default);

    Task AddAsync(FileDownloadGrant grant, CancellationToken cancellationToken = default);
}
