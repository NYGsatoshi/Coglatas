namespace Coglatas.Web.Realtime;

public sealed class RealtimeDiagnostics
{
    private long dispatchSuccessCount;
    private long dispatchFailureCount;
    private long subscriptionDenialCount;
    private long dispatcherFailureCount;

    public void RecordDispatchSuccess() => Interlocked.Increment(ref dispatchSuccessCount);
    public void RecordDispatchFailure() => Interlocked.Increment(ref dispatchFailureCount);
    public void RecordSubscriptionDenial() => Interlocked.Increment(ref subscriptionDenialCount);
    public void RecordDispatcherFailure() => Interlocked.Increment(ref dispatcherFailureCount);

    public RealtimeDiagnosticCounters Snapshot() => new(
        Interlocked.Read(ref dispatchSuccessCount),
        Interlocked.Read(ref dispatchFailureCount),
        Interlocked.Read(ref subscriptionDenialCount),
        Interlocked.Read(ref dispatcherFailureCount));
}

public sealed record RealtimeDiagnosticCounters(long DispatchSuccessCount, long DispatchFailureCount, long SubscriptionDenialCount, long DispatcherFailureCount);
