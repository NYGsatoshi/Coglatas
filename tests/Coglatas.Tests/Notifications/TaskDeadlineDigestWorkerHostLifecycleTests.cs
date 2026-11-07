using System.Collections.Concurrent;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Notifications;
using Coglatas.Web.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coglatas.Tests.Notifications;

[Trait("Scope", "TaskV1PR07C")]
public sealed class TaskDeadlineDigestWorkerHostLifecycleTests
{
    [Theory]
    [InlineData("ordinary", TaskDeadlineDigestErrorCodes.GenerationFailure)]
    [InlineData("timeout", TaskDeadlineDigestErrorCodes.GenerationTimeout)]
    [InlineData("unrequested-cancellation", TaskDeadlineDigestErrorCodes.GenerationFailure)]
    public async Task CycleFailureLogsOnlySafeCodeAndKeepsWorkerAliveUntilStopped(string kind, string expectedCode)
    {
        const string privateDetail = "private-recipient-and-resource-details";
        Exception failure = kind switch
        {
            "timeout" => new TimeoutException(privateDetail),
            "unrequested-cancellation" => new OperationCanceledException(privateDetail),
            _ => new InvalidOperationException(privateDetail)
        };
        var scopes = new ThrowingScopeFactory(failure);
        var logger = new RecordingLogger();
        using var worker = CreateWorker(scopes, logger);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await logger.FirstEntry.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(worker.ExecuteTask);
            Assert.False(worker.ExecuteTask.IsCompleted);
            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Error, entry.Level);
            Assert.Null(entry.Exception);
            Assert.Equal(expectedCode, entry.Properties["ErrorCode"]);
            Assert.All(entry.Properties.Keys, key => Assert.Contains(key, new[] { "ErrorCode", "{OriginalFormat}" }));
            Assert.DoesNotContain(privateDetail, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(privateDetail, string.Join("|", entry.Properties.Values), StringComparison.Ordinal);
            Assert.Equal(1, scopes.Calls);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.True(worker.ExecuteTask!.IsCompleted);
        Assert.Single(logger.Entries);
        Assert.Equal(1, scopes.Calls);
    }

    [Fact]
    public async Task RequestedCancellationDuringCycleStopsWithoutAnErrorOrAnotherCycle()
    {
        using var cancellation = new CancellationTokenSource();
        var scopes = new ThrowingScopeFactory(new OperationCanceledException(cancellation.Token), cancellation.Cancel);
        var logger = new RecordingLogger();
        using var worker = CreateWorker(scopes, logger);
        await worker.StartAsync(cancellation.Token);
        try
        {
            Assert.NotNull(worker.ExecuteTask);
            await worker.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
            Assert.Equal(1, scopes.Calls);
            Assert.Empty(logger.Entries);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
    }

    [Fact]
    public async Task CancellationBeforeStartDoesNotOpenAProcessingScope()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var scopes = new ThrowingScopeFactory(new InvalidOperationException("Unexpected processing scope."));
        var logger = new RecordingLogger();
        using var worker = CreateWorker(scopes, logger);
        await worker.StartAsync(cancellation.Token);
        try
        {
            Assert.NotNull(worker.ExecuteTask);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => worker.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None));
            Assert.True(worker.ExecuteTask.IsCanceled);
            Assert.Equal(0, scopes.Calls);
            Assert.Empty(logger.Entries);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
    }

    private static TaskDeadlineDigestWorker CreateWorker(IServiceScopeFactory scopes, ILogger<TaskDeadlineDigestWorker> logger) =>
        new(scopes, new FixedClock(), Options.Create(new TaskDeadlineDigestWorkerOptions { PollSeconds = 300 }), logger);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 11, 7, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class ThrowingScopeFactory(Exception failure, Action? beforeThrow = null) : IServiceScopeFactory
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref _calls);
            beforeThrow?.Invoke();
            throw failure;
        }
    }

    private sealed class RecordingLogger : ILogger<TaskDeadlineDigestWorker>
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = [];
        public TaskCompletionSource FirstEntry { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(value => value.Key, value => value.Value)
                : new Dictionary<string, object?>();
            Entries.Enqueue(new LogEntry(logLevel, formatter(state, exception), exception, properties));
            FirstEntry.TrySetResult();
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties);
}
