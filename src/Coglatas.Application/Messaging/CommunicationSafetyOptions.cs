namespace Coglatas.Application.Messaging;

public sealed class CommunicationSafetyOptions
{
    public int MaxMessageLength { get; init; } = 12000;
    public int MaxAttachmentsPerMessage { get; init; } = 5;
    public int MaxPostsPerMinutePerUser { get; init; } = 30;
    public int MaxPostsPerMinutePerConversation { get; init; } = 120;
    public int MaxThreadCreatesPerMinutePerUser { get; init; } = 10;
    public int MaxReportsPerHourPerUser { get; init; } = 20;
    public int DuplicatePostWindowSeconds { get; init; } = 15;
}

