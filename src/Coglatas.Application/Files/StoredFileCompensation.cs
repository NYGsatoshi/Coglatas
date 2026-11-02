using Coglatas.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Coglatas.Application.Files;

internal static class StoredFileCompensation
{
    private static readonly EventId CleanupFailed = new(53501, "UploadCompensationFailed");
    private static readonly EventId RetainedAfterFailure = new(53502, "UploadCompensationSkipped");

    public static async Task AfterUploadFailureAsync(IFileStorageService storage, string storageKey,
        IUnitOfWork unitOfWork, UploadSaveOutcome outcome, ILogger? logger)
    {
        if (outcome.Disposition == UploadCommitDisposition.NotCommitted)
        {
            try
            {
                if (unitOfWork.TryDiscardFailedUploadChanges())
                {
                    await TryDeleteAsync(storage, storageKey, logger);
                    return;
                }
            }
            catch
            {
                // Failed discard must not permit metadata to outlive its stored bytes.
            }
            outcome.Disposition = UploadCommitDisposition.Unknown;
        }

        try
        {
            logger?.LogWarning(RetainedAfterFailure,
                "Stored file retained after an upload failure with a committed or unknown outcome.");
        }
        catch
        {
            // Retention observability must preserve the initiating upload failure.
        }
    }

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
