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

        // Keep incomplete bytes away from the canonical key, including replacements.
        // A sibling temporary file keeps publication on the same filesystem.
        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".upload";
        try
        {
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true))
            {
                await stream.CopyToAsync(output, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        catch (Exception uploadError)
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException)
            {
                // Retain both failures if storage also prevents compensation.
                throw new AggregateException("File upload and temporary-file cleanup failed.", uploadError, cleanupError);
            }
            throw;
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
