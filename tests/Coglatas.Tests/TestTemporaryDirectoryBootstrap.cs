using System.Runtime.CompilerServices;

namespace Coglatas.Tests;

internal static class TestTemporaryDirectoryBootstrap
{
    internal static string CollectorTemporaryDirectory { get; }

    static TestTemporaryDirectoryBootstrap()
    {
        // Collector preparation precedes this module's runner-scoped redirection.
        CollectorTemporaryDirectory = Path.GetTempPath();
    }

    [ModuleInitializer]
    internal static void Initialize()
    {
        var runnerTemp = Environment.GetEnvironmentVariable("RUNNER_TEMP");
        if (string.IsNullOrWhiteSpace(runnerTemp))
        {
            return;
        }

        var runId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID") ?? "local";
        var runAttempt = Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT") ?? "1";
        var testTemp = Path.Combine(
            runnerTemp,
            "coglatas-tests",
            $"{runId}-{runAttempt}-{Environment.ProcessId}");

        Directory.CreateDirectory(testTemp);
        Environment.SetEnvironmentVariable("TMPDIR", testTemp);
        Environment.SetEnvironmentVariable("TMP", testTemp);
        Environment.SetEnvironmentVariable("TEMP", testTemp);
    }
}
