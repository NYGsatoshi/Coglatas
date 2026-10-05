using Coglatas.Web.Testing;
using Coglatas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Coglatas.Tests.Web;

public sealed class PerformanceCiTestBoundaryTests
{
    [Theory]
    [InlineData("Test", true, true, true)]
    [InlineData("Test", false, true, false)]
    [InlineData("Test", true, false, false)]
    [InlineData("Production", true, true, false)]
    [InlineData("Development", true, true, false)]
    public void ApiDiagnosticsRequireIndependentOptInWithoutActivatingTheDbFixture(
        string environmentName, bool fixtureRequested, bool diagnosticsRequested, bool expected)
    {
        using var host = new HostBuilder()
            .UseEnvironment(environmentName)
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["COGLATAS_PERFORMANCE_CI_FIXTURE_ENABLED"] = fixtureRequested.ToString(),
                ["COGLATAS_PERFORMANCE_API_DIAGNOSTICS_ENABLED"] = diagnosticsRequested.ToString()
            }))
            .ConfigureWebHost(builder =>
            {
                builder.UseEnvironment(environmentName).UseKestrel().Configure(_ => { });
                new PerformanceCiHostingStartup().Configure(builder);
            })
            .Build();
        Assert.Equal(expected, host.Services.GetService<PerformanceApiCapture>() is not null);
        Assert.Null(host.Services.GetService<PerformanceDbCapture>());
    }

    [Fact]
    public void ApiDiagnosticsBoundCommandEvidenceAndSeparateWorkerOverlap()
    {
        var capture = new PerformanceApiCapture();
        using var measurement = capture.Begin();
        Assert.NotNull(measurement);
        for (var duration = 1; duration <= 100; duration++) capture.RecordCommand(TimeSpan.FromMilliseconds(duration), failed: duration == 100);
        capture.RecordConnectionOpen(TimeSpan.FromMilliseconds(2));
        var database = measurement.Snapshot();
        Assert.Equal(100, database.CommandCount);
        Assert.Equal(1, database.FailedCommandCount);
        Assert.Equal(5050d, database.SummedCommandDurationMs);
        Assert.Equal(new double[] { 100, 99, 98, 97, 96 }, database.SlowestCommandDurationsMs);
        Assert.Equal(2d, database.SummedConnectionOpenDurationMs);
        using (capture.BeginWorker(PerformanceApiCapture.WorkerKind.Outbox))
        {
            capture.RecordEventDispatch();
            capture.RecordSignalRSend();
            var activity = capture.SnapshotActivity();
            Assert.Equal(1, activity.ActiveWorkers[0]);
            Assert.Equal(1, activity.WorkerStarts[0]);
            Assert.Equal(1, activity.EventDispatches);
            Assert.Equal(1, activity.SignalRSends);
        }
        Assert.Equal(0, capture.SnapshotActivity().ActiveWorkers[0]);
    }

    [Fact]
    public void ApiDiagnosticsRemainBoundedAndDoNotRetainClosedRequestCommands()
    {
        var capture = new PerformanceApiCapture();
        using (var first = capture.Begin())
        {
            Assert.NotNull(first);
            capture.RecordCommand(TimeSpan.FromMilliseconds(1), false);
        }
        capture.RecordCommand(TimeSpan.FromMilliseconds(2), false);
        for (var index = 1; index < 1300; index++)
        {
            using var next = capture.Begin();
            Assert.NotNull(next);
            Assert.Equal(0, next.Snapshot().CommandCount);
        }
        Assert.Null(capture.Begin());
        Assert.Equal(2, capture.SnapshotActivity().AllEfCommands);
    }

    [Fact]
    public async Task ApiDiagnosticsBufferUntilThePostCohortHealthProbeAndExportOnlyAllowlistedScalars()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"perf04-diagnostic-{Guid.NewGuid():N}");
        const string prefix = "0123456789abcdef";
        var id = prefix + "0000000000000000";
        using var host = new HostBuilder()
            .UseEnvironment("Test")
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["COGLATAS_PERFORMANCE_CI_FIXTURE_ENABLED"] = "true",
                ["COGLATAS_PERFORMANCE_API_DIAGNOSTICS_ENABLED"] = "true",
                ["COGLATAS_PERFORMANCE_API_DIAGNOSTICS_PATH"] = directory
            }))
            .ConfigureWebHost(builder =>
            {
                builder.UseKestrel().Configure(_ => { });
                new PerformanceCiHostingStartup().Configure(builder);
            })
            .Build();
        try
        {
            var capture = host.Services.GetRequiredService<PerformanceApiCapture>();
            capture.MarkFixtureResetCompleted();
            var filter = host.Services.GetServices<IStartupFilter>()
                .Single(candidate => candidate.GetType().Name == "PerformanceApiDiagnosticsStartupFilter");
            var app = new ApplicationBuilder(host.Services);
            filter.Configure(pipeline => pipeline.Run(context =>
            {
                capture.RecordCommand(TimeSpan.FromMilliseconds(2), false);
                context.Response.StatusCode = 200;
                return context.Response.Body.WriteAsync("protected-response-body"u8.ToArray()).AsTask();
            }))(app);
            var execute = app.Build();
            using var scope = host.Services.CreateScope();
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            context.Request.Headers["X-Performance-Diagnostic"] = id;
            context.Request.Headers["X-Performance-Scenario"] = "mutation.kanban-move";
            context.Request.Headers["X-Performance-Sample"] = "1";
            context.Request.Headers["X-Performance-Trial"] = "1";
            context.Request.Headers["X-Performance-Warmup-Identity"] = prefix;
            context.Request.Headers.Authorization = "protected-token";
            context.Request.Headers.Cookie = "protected-cookie";
            context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("protected-task-body"));
            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;
            await execute(context);
            Assert.Equal("protected-response-body", System.Text.Encoding.UTF8.GetString(responseBody.ToArray()));
            Assert.False(Directory.Exists(directory));

            var responseFeature = new CompletionResponseFeature();
            var health = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            health.Features.Set<IHttpResponseFeature>(responseFeature);
            health.Request.Path = "/health/ready";
            health.Request.Headers["X-Performance-Diagnostics-Flush"] = prefix;
            await execute(health);
            Assert.False(Directory.Exists(directory));
            await responseFeature.CompleteAsync();
            var evidence = await File.ReadAllTextAsync(Path.Combine(directory, id + ".json"));
            Assert.DoesNotContain("protected", evidence);
            Assert.DoesNotContain("authorization", evidence, StringComparison.OrdinalIgnoreCase);
            using var document = JsonDocument.Parse(evidence);
            Assert.Equal("mutation.kanban-move", document.RootElement.GetProperty("scenario").GetString());
            Assert.Equal(1, document.RootElement.GetProperty("database").GetProperty("commandCount").GetInt32());
            Assert.Equal(JsonValueKind.String, document.RootElement.GetProperty("fixtureResetCompletedUtc").ValueKind);
            foreach (var name in new[] { "runtimeBefore", "runtimeAfter" })
            {
                var runtime = document.RootElement.GetProperty(name);
                Assert.Equal(14, runtime.EnumerateObject().Count());
                Assert.Equal(3, runtime.GetProperty("gcCollections").GetArrayLength());
                Assert.True(runtime.GetProperty("availableWorkers").GetInt32() >= 0);
                Assert.True(runtime.GetProperty("availableIoThreads").GetInt32() >= 0);
                Assert.True(runtime.GetProperty("threadPoolQueueLength").GetInt64() >= 0);
                Assert.True(runtime.GetProperty("processAllocatedBytes").GetInt64() >= 0);
                Assert.True(runtime.GetProperty("processCpuTimeMs").GetDouble() >= 0);
                Assert.True(runtime.GetProperty("availableProcessorCount").GetInt32() > 0);
                foreach (var counter in new[] { "hostCpuTotalTicks", "hostCpuIdleTicks",
                             "cgroupUsageMicroseconds", "cgroupThrottledMicroseconds",
                             "cgroupThrottlePeriods", "cgroupQuotaMicroseconds", "cgroupPeriodMicroseconds" })
                {
                    Assert.Contains(runtime.GetProperty(counter).ValueKind,
                        new[] { JsonValueKind.Number, JsonValueKind.Null });
                }
            }
            Assert.True(document.RootElement.GetProperty("monotonicEndTicks").GetInt64() >= document.RootElement.GetProperty("monotonicStartTicks").GetInt64());
            Assert.Single(Directory.GetFiles(directory, "*.json"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class CompletionResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _callbacks = [];
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted => false;
        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) => _callbacks.Add((callback, state));
        public async Task CompleteAsync()
        {
            foreach (var (callback, state) in _callbacks) await callback(state);
        }
    }
    [Theory]
    [InlineData("Test", true)]
    [InlineData("test", true)]
    [InlineData("Development", false)]
    [InlineData("Staging", false)]
    [InlineData("Production", false)]
    public void PerformanceFixtureIsRestrictedToAnExplicitTestEnvironmentOptIn(
        string environmentName,
        bool expected)
    {
        Assert.Equal(expected, PerformanceCiTestBoundary.IsEnabled(environmentName, requested: true));
    }

    [Fact]
    public void PerformanceFixtureRemainsDisabledWithoutExplicitOptIn()
    {
        Assert.False(PerformanceCiTestBoundary.IsEnabled("Test", requested: false));
    }
    [Theory]
    [InlineData("Test", true, true, true)]
    [InlineData("Test", false, true, false)]
    [InlineData("Test", true, false, false)]
    [InlineData("Production", true, true, false)]
    [InlineData("Development", true, true, false)]
    public void DbInstrumentationRequiresBothExplicitOptInsAndTheTestEnvironment(
        string environmentName, bool fixtureRequested, bool captureRequested, bool expected)
    {
        using var host = new HostBuilder()
            .UseEnvironment(environmentName)
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["COGLATAS_PERFORMANCE_CI_FIXTURE_ENABLED"] = fixtureRequested.ToString(),
                ["COGLATAS_PERFORMANCE_DB_CAPTURE_ENABLED"] = captureRequested.ToString()
            }))
            .ConfigureWebHost(builder =>
            {
                builder.UseEnvironment(environmentName).UseKestrel().Configure(_ => { });
                new PerformanceCiHostingStartup().Configure(builder);
            })
            .Build();
        Assert.Equal(expected, host.Services.GetService<PerformanceDbCapture>() is not null);
    }
    [Fact]
    public async Task CaptureEvidenceCompletesAfterTheResponseClientHasDisconnected()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"perf05-capture-{Guid.NewGuid():N}");
        var id = Guid.NewGuid().ToString("N");
        using var host = new HostBuilder()
            .UseEnvironment("Test")
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["COGLATAS_PERFORMANCE_CI_FIXTURE_ENABLED"] = "true",
                ["COGLATAS_PERFORMANCE_DB_CAPTURE_ENABLED"] = "true",
                ["COGLATAS_PERFORMANCE_DB_EVIDENCE_PATH"] = directory
            }))
            .ConfigureWebHost(builder =>
            {
                builder.UseKestrel().Configure(_ => { });
                new PerformanceCiHostingStartup().Configure(builder);
            })
            .Build();
        try
        {
            var filter = host.Services.GetServices<IStartupFilter>()
                .Single(candidate => candidate.GetType().Name == "PerformanceDbStartupFilter");
            var app = new ApplicationBuilder(host.Services);
            filter.Configure(pipeline => pipeline.Run(context =>
            {
                using var source = new ActivitySource("Npgsql");
                using (var activity = source.StartActivity())
                {
                    Assert.NotNull(activity);
                    activity.SetTag("db.query.text", "SELECT 'protected-body' FROM task_items LIMIT 5");
                }
                context.Response.StatusCode = 200;
                context.RequestAborted = new CancellationToken(canceled: true);
                return Task.CompletedTask;
            }))(app);
            using var scope = host.Services.CreateScope();
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            context.Request.Headers["X-Performance-Capture"] = id;
            await app.Build()(context);
            var evidence = await File.ReadAllTextAsync(Path.Combine(directory, id + ".json"));
            Assert.DoesNotContain("protected-body", evidence);
            using var document = JsonDocument.Parse(evidence);
            Assert.Equal(1, document.RootElement.GetProperty("commandCount").GetInt32());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
