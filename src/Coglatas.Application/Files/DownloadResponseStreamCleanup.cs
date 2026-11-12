using Microsoft.Extensions.Logging;

namespace Coglatas.Application.Files;

/// <summary>Releases an untransferred download stream without replacing the original failure.</summary>
public static class DownloadResponseStreamCleanup
{
    public static async ValueTask TryDisposeAsync(Stream stream, ILogger? logger)
    {
        try
        {
            await stream.DisposeAsync();
        }
        catch
        {
            try
            {
                logger?.LogWarning("Download response stream cleanup failed.");
            }
            catch
            {
                // Cleanup observability must preserve the original download failure.
            }
        }
    }
}
