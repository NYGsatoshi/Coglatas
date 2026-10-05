using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>
/// Scalar-only observation registered exclusively by the explicit PERF Test boundary.
/// Command text, parameters, connection strings and exception messages are never read.
/// </summary>
public sealed class PerformanceApiCapture : DbCommandInterceptor
{
    private readonly AsyncLocal<Measurement?> _current = new();
    private readonly long[] _workerStarts = new long[4];
    private readonly long[] _activeWorkers = new long[4];
    private long _eventDispatches;
    private long _signalRSends;
    private long _allCommandCount;
    private long _acceptedCaptures;
    private readonly Lock _evidenceGate = new();
    private readonly List<(Guid Id, object Evidence)> _evidence = [];
    private bool _flushed;
    public DateTimeOffset? FixtureResetCompletedUtc { get; private set; }
    public long? FixtureResetCompletedMonotonicTicks { get; private set; }

    public void MarkFixtureResetCompleted()
    {
        FixtureResetCompletedUtc = DateTimeOffset.UtcNow;
        FixtureResetCompletedMonotonicTicks = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    public void Store(Guid id, object evidence)
    {
        lock (_evidenceGate)
        {
            if (!_flushed && _evidence.Count < 1300) _evidence.Add((id, evidence));
        }
    }

    public async Task FlushAsync(string prefix, string directory)
    {
        (Guid Id, object Evidence)[] samples;
        lock (_evidenceGate)
        {
            if (_flushed) return;
            _flushed = true;
            samples = _evidence.Where(item => item.Id.ToString("N").StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            _evidence.Clear();
        }
        Directory.CreateDirectory(directory);
        foreach (var sample in samples)
        {
            var path = Path.Combine(directory, $"{sample.Id:N}.json");
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(sample.Evidence,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)), CancellationToken.None);
            File.Move(temporary, path, overwrite: true);
        }
    }

    public Measurement? Begin()
    {
        // The largest supported one-VU profile is 13 scenarios * 100 iterations.
        if (_current.Value is not null || Interlocked.Increment(ref _acceptedCaptures) > 1300) return null;
        var measurement = new Measurement(() => _current.Value = null);
        _current.Value = measurement;
        return measurement;
    }

    public IDisposable BeginWorker(WorkerKind worker)
    {
        var index = (int)worker;
        Interlocked.Increment(ref _workerStarts[index]);
        Interlocked.Increment(ref _activeWorkers[index]);
        return new CloseScope(() => Interlocked.Decrement(ref _activeWorkers[index]));
    }

    public void RecordEventDispatch() => Interlocked.Increment(ref _eventDispatches);
    public void RecordSignalRSend() => Interlocked.Increment(ref _signalRSends);

    public ActivityCounters SnapshotActivity() => new(
        Enumerable.Range(0, 4).Select(index => Interlocked.Read(ref _workerStarts[index])).ToArray(),
        Enumerable.Range(0, 4).Select(index => Interlocked.Read(ref _activeWorkers[index])).ToArray(),
        Interlocked.Read(ref _eventDispatches), Interlocked.Read(ref _signalRSends),
        Interlocked.Read(ref _allCommandCount));

    public void RecordCommand(TimeSpan duration, bool failed)
    {
        Interlocked.Increment(ref _allCommandCount);
        _current.Value?.RecordCommand(duration.TotalMilliseconds, failed);
    }

    public void RecordConnectionOpen(TimeSpan duration) => _current.Value?.RecordConnectionOpen(duration.TotalMilliseconds);

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        RecordCommand(eventData.Duration, false);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        DbDataReader result, CancellationToken cancellationToken = default)
    {
        RecordCommand(eventData.Duration, false);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        RecordCommand(eventData.Duration, false);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        int result, CancellationToken cancellationToken = default)
    {
        RecordCommand(eventData.Duration, false);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        RecordCommand(eventData.Duration, false);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        object? result, CancellationToken cancellationToken = default)
    {
        RecordCommand(eventData.Duration, false);
        return ValueTask.FromResult(result);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) => RecordCommand(eventData.Duration, true);
    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        RecordCommand(eventData.Duration, true);
        return Task.CompletedTask;
    }

    public enum WorkerKind { Outbox, NotificationDigest, ScheduledAnnouncement, AuditExport }
    public sealed record ActivityCounters(long[] WorkerStarts, long[] ActiveWorkers, long EventDispatches, long SignalRSends, long AllEfCommands);
    public sealed record DatabaseCounters(int CommandCount, int FailedCommandCount, double SummedCommandDurationMs,
        double[] SlowestCommandDurationsMs, int ConnectionOpenCount, double SummedConnectionOpenDurationMs);

    public sealed class Measurement(Action close) : IDisposable
    {
        private readonly Lock _gate = new();
        private readonly List<double> _slowest = [];
        private int _commands, _failures, _opens;
        private double _duration, _openDuration;
        private bool _closed;

        public void RecordCommand(double durationMs, bool failed)
        {
            lock (_gate)
            {
                if (_closed) return;
                _commands++;
                if (failed) _failures++;
                _duration += durationMs;
                var index = _slowest.FindIndex(value => value < durationMs);
                if (index < 0) index = _slowest.Count;
                if (index < 5) _slowest.Insert(index, durationMs);
                if (_slowest.Count > 5) _slowest.RemoveAt(5);
            }
        }

        public void RecordConnectionOpen(double durationMs)
        {
            lock (_gate)
            {
                if (_closed) return;
                _opens++;
                _openDuration += durationMs;
            }
        }

        public DatabaseCounters Snapshot()
        {
            lock (_gate) return new(_commands, _failures, _duration, _slowest.ToArray(), _opens, _openDuration);
        }

        public void Dispose()
        {
            lock (_gate) _closed = true;
            close();
        }
    }

    private sealed class CloseScope(Action close) : IDisposable
    {
        private int _closed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 0) close();
        }
    }
}

/// <summary>Logical Open elapsed includes pool acquisition; it cannot isolate that delay from physical connection setup.</summary>
public sealed class PerformanceApiConnectionCapture(PerformanceApiCapture capture) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => capture.RecordConnectionOpen(eventData.Duration);
    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        capture.RecordConnectionOpen(eventData.Duration);
        return Task.CompletedTask;
    }
}
