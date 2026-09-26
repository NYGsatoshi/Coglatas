using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace Coglatas.Infrastructure.Files;

public sealed class UnsupportedObjectStorageService(IOptions<FileStorageOptions> options) : IFileStorageService
{
    private readonly FileStorageOptions _options = options.Value;

    public Task<Result> SaveAsync(string storageKey, Stream stream, string contentType, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure($"FileStorage provider '{_options.Provider}' is configured but is not implemented in this build."));
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"FileStorage provider '{_options.Provider}' is not implemented in this build.");
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<string?> CreateSignedReadUrlAsync(string storageKey, TimeSpan expiresIn, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }
}
