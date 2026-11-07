using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Coglatas.Tests.TestInfrastructure;

public sealed class OwnedChildProcessShutdownTests
{
    [LinuxOwnedChildFact]
    public async Task NativeTerminationStopsTheOwnedSleepingChild()
    {
        using var child = Start("/bin/sleep", "30");
        try
        {
            await OwnedChildProcessShutdown.StopAsync(child, TimeSpan.FromSeconds(5));
            Assert.True(child.HasExited);
            Assert.Equal(143, child.ExitCode);
        }
        finally { await ForceStopIfRunningAsync(child); }
    }

    [LinuxOwnedChildFact]
    public async Task AlreadyExitedChildKeepsItsOriginalExitStatus()
    {
        using var child = Start("/bin/true");
        try
        {
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await OwnedChildProcessShutdown.StopAsync(child, TimeSpan.FromSeconds(5));
            Assert.True(child.HasExited);
            Assert.Equal(0, child.ExitCode);
        }
        finally { await ForceStopIfRunningAsync(child); }
    }

    [LinuxOwnedChildFact]
    public async Task ChildIgnoringTerminationIsForceStoppedAfterTheGracePeriod()
    {
        // Fixed test-only command: set SIGTERM ignored before announcing readiness,
        // then exec sleep so there are no remaining shell descendants to signal.
        using var child = Start("/bin/sh", "-c", "trap '' TERM; printf 'ready\\n'; exec /bin/sleep 30");
        try
        {
            Assert.Equal("ready", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            await OwnedChildProcessShutdown.StopAsync(child, TimeSpan.FromMilliseconds(200));
            Assert.True(child.HasExited);
            Assert.Equal(137, child.ExitCode);
        }
        finally { await ForceStopIfRunningAsync(child); }
    }

    private static Process Start(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
        };
        start.Environment.Clear();
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException("Owned test child did not start.");
    }

    private static async Task ForceStopIfRunningAsync(Process child)
    {
        if (child.HasExited) return;
        child.Kill(entireProcessTree: true);
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class LinuxOwnedChildFactAttribute : FactAttribute
    {
        public LinuxOwnedChildFactAttribute()
        {
            if (!OperatingSystem.IsLinux() ||
                RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
                Skip = "Native pidfd controls require Linux x64 or arm64; other fixture platforms retain force-stop behavior.";
        }
    }
}
