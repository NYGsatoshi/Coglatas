using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit.Abstractions;

namespace Coglatas.Tests.ProjectIde;

// Isolated P0 Batch 6 generator verification. This test does not modify the tracked fixture.
public sealed class ScenarioScheduleGeneratorReproDiagnostics
{
    private readonly ITestOutputHelper _output;

    public ScenarioScheduleGeneratorReproDiagnostics(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task StandaloneGeneratorEmitsIdenticalScheduleBytesTwice()
    {
        var repositoryRoot = FindRepositoryRoot();
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "coglatas-batch6-schedule-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var first = Path.Combine(temporaryDirectory, "schedule-first.json");
            var second = Path.Combine(temporaryDirectory, "schedule-second.json");
            await RunGenerator(repositoryRoot, first);
            await RunGenerator(repositoryRoot, second);
            var a = await File.ReadAllBytesAsync(first);
            var b = await File.ReadAllBytesAsync(second);
            Assert.Equal(a, b);

            var sha256 = Convert.ToHexString(SHA256.HashData(a)).ToLowerInvariant();
            var prefix = Encoding.ASCII.GetBytes("blob " + a.Length + "\0");
            var input = new byte[prefix.Length + a.Length];
            Buffer.BlockCopy(prefix, 0, input, 0, prefix.Length);
            Buffer.BlockCopy(a, 0, input, prefix.Length, a.Length);
            var gitBlob = Convert.ToHexString(SHA1.HashData(input)).ToLowerInvariant();
            _output.WriteLine("BATCH6_SCENARIO_GENERATOR firstBytes=" + a.Length + " sha256=" + sha256 + " gitBlob=" + gitBlob + " secondEqual=true");
            Console.WriteLine("BATCH6_SCENARIO_GENERATOR bytes=" + a.Length + " sha256=" + sha256 + " gitBlob=" + gitBlob);
            Assert.Equal("12e74015e1ca9bd509a85cb72d08fd724461ade65e5b1c4b51003bd1a2db8184", sha256);
            Assert.Equal("90e56bf5ad997a2e9385736907a9e89cd00c8576", gitBlob);

            using var document = JsonDocument.Parse(a);
            var root = document.RootElement;
            Assert.Equal("coglatas.web-preview/1", root.GetProperty("format").GetString());
            var cases = root.GetProperty("cases").EnumerateArray().ToArray();
            Assert.Equal(6, cases.Length);
            Assert.DoesNotContain(cases, entry => entry.TryGetProperty("localProjectTestDiscovery", out _));
            var scheduled = cases.Where(entry => entry.TryGetProperty("scenarioScheduleImpact", out _)).ToArray();
            Assert.Equal(2, scheduled.Length);
            var completed = 0;
            var incomplete = 0;
            foreach (var row in scheduled)
            {
                var schedule = row.GetProperty("scenarioScheduleImpact");
                Assert.False(string.IsNullOrWhiteSpace(schedule.GetProperty("digest").GetString()));
                using var frameDocument = JsonDocument.Parse(schedule.GetProperty("text").GetString()!);
                var frame = frameDocument.RootElement;
                Assert.Equal("coglatas.local-scenario-elapsed-schedule/1", frame.GetProperty("format").GetString());
                var state = frame.GetProperty("state").GetString();
                if (state == "Complete")
                {
                    completed++;
                    Assert.Equal("2", frame.GetProperty("modeledFinishDeltaTicks").GetString());
                    var tasks = frame.GetProperty("baseline").GetProperty("tasks").EnumerateArray().ToArray();
                    Assert.NotEmpty(tasks);
                    Assert.Contains(tasks, task => task.GetProperty("totalFloatTicks").GetString() == "5");
                }
                else
                {
                    Assert.Equal("Incomplete", state);
                    incomplete++;
                }
            }
            Assert.Equal(1, completed);
            Assert.Equal(1, incomplete);
            _output.WriteLine("BATCH6_SCENARIO_GENERATOR sixCases=6 schedules=2 complete=1 incomplete=1 baselineTaskFloat5=true");
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "global.json")) &&
                    File.Exists(Path.Combine(current.FullName, "tools", "ProjectIde.Preview", "ProjectIde.Preview.csproj")))
                    return current.FullName;
                current = current.Parent;
            }
        }
        throw new DirectoryNotFoundException("Cannot locate the pinned ProjectIDE generator checkout.");
    }

    private static async Task RunGenerator(string repositoryRoot, string outputPath)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[]
        {
            "run", "--project", "tools/ProjectIde.Preview/ProjectIde.Preview.csproj",
            "-c", "Release", "--", outputPath, "--scenario-schedule"
        })
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the standalone generator.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        var observedOutput = await stdout;
        var observedError = await stderr;
        Assert.True(process.ExitCode == 0 && File.Exists(outputPath),
            "Standalone Preview failed: exit=" + process.ExitCode + " stdout=" + observedOutput + " stderr=" + observedError);
    }
}
