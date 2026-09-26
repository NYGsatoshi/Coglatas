using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace Coglatas.Infrastructure.Files;

public sealed class LocalFileStorageService(IOptions<FileStorageOptions> options) : IFileStorageService
{
    private readonly FileStorageOptions _options = options.Value;

    public async Task<Result> SaveAsync(
        string storageKey,
        Stream stream,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.RootPath))
        {
            return Result.Failure("FileStorage:RootPath is not configured.");
        }

        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return Result.Failure("Storage key is required.");
        }

        var root = GetRootPath();
        var fullPath = EnsureSafePath(root, storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using (var output = File.Create(fullPath))
        {
            await stream.CopyToAsync(output, cancellationToken);
        }

        return Result.Success();
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = EnsureSafePath(GetRootPath(), storageKey);
        return Task.FromResult<Stream>(File.OpenRead(fullPath));
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = EnsureSafePath(GetRootPath(), storageKey);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = EnsureSafePath(GetRootPath(), storageKey);
        return Task.FromResult(File.Exists(fullPath));
    }

    public Task<string?> CreateSignedReadUrlAsync(string storageKey, TimeSpan expiresIn, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }

    private string GetRootPath()
    {
        return Path.GetFullPath(_options.RootPath);
    }

    private static string EnsureSafePath(string root, string storageKey)
    {
        var fullPath = Path.GetFullPath(Path.Combine(root, storageKey));
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.Equals(root, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Storage key resolved outside the configured file root.");
        }

        return fullPath;
    }
}
