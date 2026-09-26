using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

public sealed class FileDownloadGrantRepository(AppDbContext dbContext) : IFileDownloadGrantRepository
{
    public Task<FileDownloadGrant?> GetAsync(Guid fileDownloadGrantId, CancellationToken cancellationToken = default)
    {
        return dbContext.FileDownloadGrants
            .FirstOrDefaultAsync(grant => grant.Id == fileDownloadGrantId, cancellationToken);
    }

    public async Task AddAsync(FileDownloadGrant grant, CancellationToken cancellationToken = default)
    {
        await dbContext.FileDownloadGrants.AddAsync(grant, cancellationToken);
    }
}
