using Microsoft.Extensions.Logging;

namespace Coglatas.Tests.Files;

internal sealed class UploadCleanupLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, EventId Event, string Message, Exception? Exception,
        KeyValuePair<string, object?>[] State)> Entries { get; } = [];
    public bool ThrowOnLog { get; set; }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (ThrowOnLog) { throw new InvalidOperationException("Synthetic logger failure."); }
        Entries.Add((logLevel, eventId, formatter(state, exception), exception,
            state is IEnumerable<KeyValuePair<string, object?>> values ? values.ToArray() : []));
    }

    public void AssertOnlyFixedWarning()
    {
        var entry = Assert.Single(Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(53501, entry.Event.Id);
        Assert.Equal("UploadCompensationFailed", entry.Event.Name);
        Assert.Equal("Stored file cleanup failed after an unsuccessful upload.", entry.Message);
        Assert.Null(entry.Exception);
        var format = Assert.Single(entry.State);
        Assert.Equal("{OriginalFormat}", format.Key);
        Assert.Equal(entry.Message, format.Value);
    }
}
