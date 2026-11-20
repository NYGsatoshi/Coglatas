namespace Coglatas.ProjectIde.TestCli;

internal static class ProjectTestOutput
{
    internal static async Task WriteNewAsync(string path, ReadOnlyMemory<byte> bytes)
    {
        var destination = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".projectide-test-{Guid.NewGuid():N}.tmp");
        var ownsTemporary = false;
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous))
            {
                ownsTemporary = true;
                await output.WriteAsync(bytes);
                await output.FlushAsync();
            }

            if (OperatingSystem.IsWindows())
            {
                File.Move(temporary, destination, overwrite: false);
                ownsTemporary = false;
            }
            else
            {
                // A Unix no-overwrite Move can check existence before calling a replacing rename.
                // Linking the closed file creates the destination in one no-replace operation.
                File.CreateHardLink(destination, temporary);
            }
        }
        finally
        {
            if (ownsTemporary)
            {
                try { File.Delete(temporary); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // Keep the publication error if the filesystem also refuses cleanup.
                }
            }
        }
    }
}
