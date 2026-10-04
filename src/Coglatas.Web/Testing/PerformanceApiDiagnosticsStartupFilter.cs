using System.Diagnostics;
using System.Globalization;
using Coglatas.Infrastructure.Persistence;

namespace Coglatas.Web.Testing;

/// <summary>Opt-in synthetic request sidecars. Only fixed scalar fields leave the process.</summary>
internal sealed class PerformanceApiDiagnosticsStartupFilter : IStartupFilter
{
    private static readonly HashSet<string> Scenarios = new(StringComparer.Ordinal)
    {
        "auth.session-bootstrap", "workspace.list", "workspace.detail", "project.list", "project.detail",
        "task.list", "task.detail", "task.my-tasks", "project.kanban-load", "project.gantt-load",
        "conversation.list", "notification.list", "mutation.kanban-move"
    };

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, pipeline) =>
        {
            var flushPrefix = context.Request.Headers["X-Performance-Diagnostics-Flush"].ToString();
            if (context.Request.Path == "/health/ready" && flushPrefix.Length == 16 &&
                flushPrefix.All(Uri.IsHexDigit))
            {
                var observer = context.RequestServices.GetRequiredService<PerformanceApiCapture>();
                var evidenceDirectory = context.RequestServices.GetRequiredService<IConfiguration>()["COGLATAS_PERFORMANCE_API_DIAGNOSTICS_PATH"]
                    ?? throw new InvalidOperationException("API diagnostics require a dedicated local evidence directory.");
                // No new route; an explicit Test-only observer header on the existing
                // post-run health probe seals and flushes the bounded memory buffer.
                context.Response.OnCompleted(() => observer.FlushAsync(flushPrefix, evidenceDirectory));
                await pipeline(context);
                return;
            }
            var id = context.Request.Headers["X-Performance-Diagnostic"].ToString();
            var scenario = context.Request.Headers["X-Performance-Scenario"].ToString();
            var warmupId = context.Request.Headers["X-Performance-Warmup-Identity"].ToString();
            if (!Guid.TryParseExact(id, "N", out var captureId) || !Scenarios.Contains(scenario) ||
                warmupId.Length != 16 || warmupId.Any(character => !Uri.IsHexDigit(character)) ||
                !int.TryParse(context.Request.Headers["X-Performance-Sample"], out var ordinal) || ordinal is < 1 or > 100 ||
                !int.TryParse(context.Request.Headers["X-Performance-Trial"], out var trial) || trial is < 1 or > 5)
            {
                await pipeline(context);
                return;
            }
            var capture = context.RequestServices.GetRequiredService<PerformanceApiCapture>();
            using var measurement = capture.Begin();
            if (measurement is null)
            {
                await pipeline(context);
                return;
            }
            var before = RuntimeSnapshot.Take();
            var activityBefore = capture.SnapshotActivity();
            var start = Stopwatch.GetTimestamp();
            var completed = false;
            try
            {
                await pipeline(context);
                completed = true;
            }
            finally
            {
                var end = Stopwatch.GetTimestamp();
                var after = RuntimeSnapshot.Take();
                var database = measurement.Snapshot();
                var evidence = new
                {
                    schemaVersion = 1, captureId = captureId.ToString("N"), scenario,
                    trialOrdinal = trial, sampleOrdinal = ordinal, warmupIdentity = warmupId,
                    stateClass = completed ? "completed" : "pipeline-failed", status = context.Response.StatusCode,
                    monotonicStartTicks = start, monotonicEndTicks = end, monotonicFrequency = Stopwatch.Frequency,
                    serverProcessingElapsedMs = Stopwatch.GetElapsedTime(start, end).TotalMilliseconds,
                    fixtureResetCompletedUtc = capture.FixtureResetCompletedUtc,
                    fixtureResetCompletedMonotonicTicks = capture.FixtureResetCompletedMonotonicTicks,
                    database, runtimeBefore = before, runtimeAfter = after,
                    processCpuTimeDeltaMs = after.ProcessCpuTimeMs - before.ProcessCpuTimeMs,
                    allocatedBytesDelta = after.ProcessAllocatedBytes - before.ProcessAllocatedBytes,
                    activityBefore, activityAfter = capture.SnapshotActivity(),
                    unavailableReasons = new
                    {
                        acquisitionOnly = "logical-open-includes-pool-and-physical-setup",
                        allocatedBytes = "process-wide-overlap-not-request-attribution",
                        directNpgsqlAndReaderDrain = "ef-command-execute-only-no-direct-command-or-reader-drain-attribution",
                        transactionSetup = "included-in-server-boundary-not-isolated",
                        jit = "not-isolated-by-scalar-observer"
                    }
                };
                // No per-sample disk writes. The post-cohort health probe flushes
                // this bounded buffer after every measured request has completed.
                capture.Store(captureId, evidence);
            }
        });
        next(app);
    };

    private sealed record RuntimeSnapshot(int[] GcCollections, long ProcessAllocatedBytes,
        int AvailableWorkers, int AvailableIoThreads, long ThreadPoolQueueLength,
        double ProcessCpuTimeMs, int AvailableProcessorCount, long? HostCpuTotalTicks,
        long? HostCpuIdleTicks, long? CgroupUsageMicroseconds, long? CgroupThrottledMicroseconds,
        long? CgroupThrottlePeriods, long? CgroupQuotaMicroseconds, long? CgroupPeriodMicroseconds)
    {
        public static RuntimeSnapshot Take()
        {
            ThreadPool.GetAvailableThreads(out var workers, out var io);
            using var process = Process.GetCurrentProcess();
            var cpu = ReadNumbers("/proc/stat", "cpu ");
            var cgroup = ReadCounters("/sys/fs/cgroup/cpu.stat");
            var quota = ReadNumbers("/sys/fs/cgroup/cpu.max", "");
            return new(
                Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray(), GC.GetTotalAllocatedBytes(precise: false),
                workers, io, ThreadPool.PendingWorkItemCount, process.TotalProcessorTime.TotalMilliseconds, Environment.ProcessorCount,
                // guest and guest_nice are already counted in user and nice.
                cpu is { Length: >= 8 } ? cpu.Take(8).Sum() : null,
                cpu is { Length: >= 8 } ? cpu[3] + cpu[4] : null,
                Counter(cgroup, "usage_usec"), Counter(cgroup, "throttled_usec"), Counter(cgroup, "nr_throttled"),
                quota is { Length: 2 } ? quota[0] : null, quota is { Length: 2 } ? quota[1] : null);
        }

        private static long? Counter(Dictionary<string, long>? values, string name) =>
            values is not null && values.TryGetValue(name, out var value) ? value : null;

        private static string? ReadBounded(string path)
        {
            try
            {
                using var reader = File.OpenText(path);
                var buffer = new char[4096];
                return new string(buffer, 0, reader.Read(buffer, 0, buffer.Length));
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private static long[]? ReadNumbers(string path, string prefix)
        {
            var line = ReadBounded(path)?.Split('\n').FirstOrDefault();
            if (line is null || !line.StartsWith(prefix, StringComparison.Ordinal)) return null;
            var output = new List<long>();
            foreach (var token in line[prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                // cpu.max uses 'max' for an unlimited quota; keep a numeric sentinel.
                if (token == "max") output.Add(-1);
                else if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) output.Add(number);
                else return null;
            }
            return output.ToArray();
        }

        private static Dictionary<string, long>? ReadCounters(string path)
        {
            var text = ReadBounded(path);
            if (text is null) return null;
            var result = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var line in text.Split('\n'))
            {
                var pair = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (pair.Length == 2 && long.TryParse(pair[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) result[pair[0]] = number;
            }
            return result;
        }
    }
}
