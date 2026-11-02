using Coglatas.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Coglatas.Application.Files;

internal static class StoredFileCompensation
{
    private static readonly EventId CleanupFailed = new(53501, "UploadCompensationFailed");

    public static async Task TryDeleteAsync(IFileStorageService storage, string storageKey, ILogger? logger)
    {
        try
        {
            await storage.DeleteAsync(storageKey, CancellationToken.None);
        }
        catch
        {
            try
            {
                // Storage errors can contain private paths or credentials. Log neither
                // the exception nor upload identities, and preserve the caller's failure.
                logger?.LogWarning(CleanupFailed, "Stored file cleanup failed after an unsuccessful upload.");
            }
            catch
            {
                // An unavailable log provider must not replace the original upload result.
            }
        }
    }
}
