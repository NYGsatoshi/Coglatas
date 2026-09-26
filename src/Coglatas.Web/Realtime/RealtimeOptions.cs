namespace Coglatas.Web.Realtime;

public sealed class RealtimeOptions
{
    public int SubscriptionLimitPerConnection { get; set; } = 32;
    public int SubscriptionAttemptsPerMinute { get; set; } = 60;
    public int DispatcherBatchSize { get; set; } = 20;
    public int DispatcherPollSeconds { get; set; } = 5;
    public int ProcessingLockSeconds { get; set; } = 120;
    public int InitialRetrySeconds { get; set; } = 5;
    public int MaximumAutomaticAttempts { get; set; } = 10;
    public int MaximumRetryMinutes { get; set; } = 15;
}
