using Coglatas.ProjectIde.TestCli;

namespace Coglatas.Tests.ProjectIde;

public sealed class ConsoleCancellationTests
{
    [Fact]
    public void Disposal_rejects_late_cancellation_and_is_idempotent()
    {
        var session = new ConsoleCancellation(observeConsole: false);
        var token = session.Token;
        session.Dispose(); session.Dispose();
        Assert.False(session.RequestCancellation());
        Assert.False(token.IsCancellationRequested);
    }

    [Fact]
    public void Callback_can_dispose_its_owner_without_disposing_an_active_cancel_call()
    {
        using var session = new ConsoleCancellation(observeConsole: false);
        using var registration = session.Token.Register(static state => ((ConsoleCancellation)state!).Dispose(), session);
        Assert.True(session.RequestCancellation());
        Assert.True(session.Token.IsCancellationRequested);
        Assert.False(session.RequestCancellation());
    }

    [Fact]
    public async Task Concurrent_disposal_defers_source_cleanup_until_the_registered_callback_returns()
    {
        var session = new ConsoleCancellation(observeConsole: false);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = session.Token.Register(static state =>
        {
            var signals = ((TaskCompletionSource<bool> Entered, TaskCompletionSource<bool> Release))state!;
            signals.Entered.TrySetResult(true);
            signals.Release.Task.GetAwaiter().GetResult();
        }, (entered, release));
        var cancelling = Task.Factory.StartNew(static state => ((ConsoleCancellation)state!).RequestCancellation(), session,
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            session.Dispose();
            Assert.False(session.RequestCancellation());
            Assert.True(session.Token.IsCancellationRequested);
        }
        finally
        {
            release.TrySetResult(true);
            Assert.True(await cancelling.WaitAsync(TimeSpan.FromSeconds(10)));
            session.Dispose();
        }
    }
}
