using System.Collections.Concurrent;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
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
        Assert.False(worker.ExecuteTask.IsFaulted);
        Assert.Single(logger.Entries);
        Assert.Equal(1, scopes.Calls);
    }

    [Fact]
    public async Task StopDuringActiveCycleCancelsRepositoryAndPreventsLaterScopes()
    {
        var gate = new CycleGate();
        var services = new ServiceCollection();
        services.AddScoped<ICurrentTenantAccessor, CurrentTenantService>();
        services.AddScoped<ITaskDeadlineDigestRepository>(_ => new GatedTenantRepository(gate));
        await using var provider = services.BuildServiceProvider();
        var scopes = new CountingScopeFactory(provider.GetRequiredService<IServiceScopeFactory>());
        var logger = new RecordingLogger();
        using var worker = CreateWorker(scopes, logger);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var observedToken = await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.True(observedToken.CanBeCanceled);
            Assert.False(observedToken.IsCancellationRequested);
            Assert.NotNull(worker.ExecuteTask);
            Assert.False(worker.ExecuteTask.IsCompleted);
            await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            await gate.Exited.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.True(observedToken.IsCancellationRequested);
            Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
            Assert.Equal(1, scopes.Calls);
            Assert.Equal(1, gate.Calls);
            Assert.Empty(logger.Entries);
        }
        finally
        {
            gate.Release.TrySetResult();
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

    private sealed class CountingScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref _calls);
            return inner.CreateScope();
        }
    }

    private sealed class CycleGate
    {
        public int Calls;
        public TaskCompletionSource<CancellationToken> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class GatedTenantRepository(CycleGate gate) : ITaskDeadlineDigestRepository
    {
        public async Task<IReadOnlyList<Guid>> ListActiveTenantIdsAsync(int page, int pageSize, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref gate.Calls);
            gate.Entered.TrySetResult(cancellationToken);
            try
            {
                await gate.Release.Task.WaitAsync(cancellationToken);
                return [];
            }
            finally
            {
                gate.Exited.TrySetResult();
            }
        }

        public Task<string?> GetTenantTimeZoneIdAsync(Guid tenantId, CancellationToken cancellationToken = default) => Unexpected<string?>();
        public Task<IReadOnlyList<TaskDeadlineDigestScheduleCandidate>> ListScheduleCandidatesAsync(int page, int pageSize, CancellationToken cancellationToken = default) => Unexpected<IReadOnlyList<TaskDeadlineDigestScheduleCandidate>>();
        public Task<int> UpsertSchedulesAsync(IReadOnlyCollection<TaskDeadlineDigestScheduleWrite> schedules, DateTimeOffset now, CancellationToken cancellationToken = default) => Unexpected<int>();
        public Task<IReadOnlyList<TaskDeadlineDigestClaim>> ClaimDueAsync(string claimOwner, DateTimeOffset now, int batchSize, TimeSpan claimTimeout, CancellationToken cancellationToken = default) => Unexpected<IReadOnlyList<TaskDeadlineDigestClaim>>();
        public Task<TaskDeadlineDigestClaim?> GetClaimedAsync(Guid jobId, Guid claimToken, bool forUpdate, CancellationToken cancellationToken = default) => Unexpected<TaskDeadlineDigestClaim?>();
        public Task<TaskDeadlineDigestClaim?> AcquireGenerationClaimFenceAsync(TaskDeadlineDigestClaim claim, CancellationToken cancellationToken = default) => Unexpected<TaskDeadlineDigestClaim?>();
        public Task<TaskDeadlineDigestCurrentContext?> GetCurrentContextAsync(Guid jobId, Guid claimToken, CancellationToken cancellationToken = default) => Unexpected<TaskDeadlineDigestCurrentContext?>();
        public Task<IReadOnlyList<TaskDeadlineDigestCandidate>> ListCurrentCandidatesAsync(Guid jobId, Guid claimToken, DateTimeOffset deadlineBeforeUtc, int page, int pageSize, CancellationToken cancellationToken = default) => Unexpected<IReadOnlyList<TaskDeadlineDigestCandidate>>();
        public Task<TaskDeadlineDigestGenerationFenceOutcome> AcquireGenerationFenceAsync(TaskDeadlineDigestClaim claim, TaskDeadlineDigestCurrentContext? evaluatedContext, IReadOnlyCollection<TaskDeadlineDigestCandidate> evaluatedCandidates, CancellationToken cancellationToken = default) => Unexpected<TaskDeadlineDigestGenerationFenceOutcome>();
        public Task<ITaskDeadlineDigestTransaction> BeginGenerationTransactionAsync(CancellationToken cancellationToken = default) => Unexpected<ITaskDeadlineDigestTransaction>();
        public Task<bool> MarkSucceededAsync(Guid jobId, Guid claimToken, Guid? notificationId, DateTimeOffset completedAt, CancellationToken cancellationToken = default) => Unexpected<bool>();
        public Task<bool> DeferAsync(Guid jobId, Guid claimToken, DateTimeOffset scheduledForUtc, DateTimeOffset deferredAt, CancellationToken cancellationToken = default) => Unexpected<bool>();
        public Task<bool> ReleaseFeatureDisabledClaimAsync(Guid jobId, Guid claimToken, DateTimeOffset releasedAt, CancellationToken cancellationToken = default) => Unexpected<bool>();
        public Task<TaskDeadlineDigestTransition> MarkFailureAsync(Guid jobId, Guid claimToken, string errorCode, DateTimeOffset failedAt, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken = default) => Unexpected<TaskDeadlineDigestTransition>();
        public Task<TaskDeadlineDigestRestartOutcome> RestartFailedAsync(Guid jobId, Guid actorUserId, string reason, DateTimeOffset requestedAt, CancellationToken cancellationToken = default) => Unexpected<TaskDeadlineDigestRestartOutcome>();
        public Task<TaskDeadlineDigestStoreDiagnostics> GetDiagnosticsAsync(DateTimeOffset now, CancellationToken cancellationToken = default) => Unexpected<TaskDeadlineDigestStoreDiagnostics>();
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Unexpected<int>();
        public void ResetGenerationState() => throw new NotSupportedException("Only active Tenant enumeration is expected.");
        private static Task<T> Unexpected<T>() => throw new NotSupportedException("Only active Tenant enumeration is expected.");
    }
}
