namespace Coglatas.ProjectIde.TestCli;

/// <summary>Owns console subscription and defers disposal until in-flight cancellation callbacks return.</summary>
internal sealed class ConsoleCancellation : IDisposable
{
    private readonly CancellationTokenSource _source = new();
    private readonly object _gate = new();
    private readonly bool _observeConsole;
    private bool _disposed;
    private int _activeCalls;
    internal CancellationToken Token { get; }

    internal ConsoleCancellation(bool observeConsole = true)
    {
        Token = _source.Token; _observeConsole = observeConsole;
        if (_observeConsole) Console.CancelKeyPress += OnCancel;
    }
    private void OnCancel(object? sender, ConsoleCancelEventArgs eventArgs) => eventArgs.Cancel = RequestCancellation();
    internal bool RequestCancellation()
    {
        lock (_gate)
        {
            if (_disposed) return false;
            _activeCalls++;
        }
        try { _source.Cancel(); return true; }
        finally
        {
            lock (_gate)
            {
                _activeCalls--;
                if (_disposed && _activeCalls == 0) _source.Dispose();
            }
        }
    }
    public void Dispose()
    {
        if (_observeConsole) Console.CancelKeyPress -= OnCancel;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_activeCalls == 0) _source.Dispose();
        }
    }
}
