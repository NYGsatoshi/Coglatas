using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Coglatas.SecurityArchitecture;

/// <summary>Reads immutable Git blobs without interpolating input into a shell command.</summary>
public sealed partial class RepositoryArtifactReader(string repositoryRoot)
{
    public const int MaximumBlobBytes = 16 * 1024 * 1024;
    [GeneratedRegex("^[a-f0-9]{40}$")]
    internal static partial Regex RevisionPattern();
    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9._/-]*$")]
    private static partial Regex PathPattern();

    public static bool IsSafePath(string path) => path.Length <= 1024 && PathPattern().IsMatch(path) &&
        !path.Contains("//", StringComparison.Ordinal) &&
        path.Split('/').All(segment => segment is not "." and not ".." && segment.Length > 0);

    public async Task<byte[]?> ReadAsync(SpecSource source)
    {
        if (!RevisionPattern().IsMatch(source.Revision) || !IsSafePath(source.Path)) return null;
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryRoot, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("cat-file");
        info.ArgumentList.Add("blob");
        info.ArgumentList.Add(source.Revision + ":" + source.Path);
        using var process = Process.Start(info);
        if (process is null) return null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        using var bytes = new MemoryStream();
        try
        {
            var buffer = new byte[8192];
            int count;
            while ((count = await process.StandardOutput.BaseStream.ReadAsync(buffer.AsMemory(), timeout.Token)) != 0)
            {
                if (bytes.Length + count > MaximumBlobBytes)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(timeout.Token);
                    await errorTask;
                    return null;
                }
                await bytes.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
            }
            await process.WaitForExitAsync(timeout.Token);
            await errorTask; // Drain, but never echo source paths or Git error text.
            return process.ExitCode == 0 ? bytes.ToArray() : null;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            // The timed-out process must still be reaped after kill; the cancelled read token cannot perform cleanup.
            await process.WaitForExitAsync(CancellationToken.None);
            try { await errorTask; } catch (OperationCanceledException) { }
            return null;
        }
    }
}
