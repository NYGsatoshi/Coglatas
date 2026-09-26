using System.Security.Cryptography;
using System.Text;

namespace Coglatas.Application.Messaging;

public sealed class InMemoryCommunicationSafetyGuard(CommunicationSafetyOptions options) : ICommunicationSafetyGuard
{
    private readonly object gate = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> windows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> duplicatePosts = new(StringComparer.Ordinal);

    public CommunicationSafetyDecision CheckMessagePost(CommunicationSafetyScope scope, string normalizedBody, DateTimeOffset now)
    {
        lock (gate)
        {
            if (TryLimit($"post:user:{scope.TenantId:N}:{scope.WorkspaceId:N}:{scope.ActorUserId:N}", options.MaxPostsPerMinutePerUser, TimeSpan.FromMinutes(1), now, out var userRetryAfter))
            {
                return CommunicationSafetyDecision.Deny("rate_limited", userRetryAfter);
            }

            if (TryLimit($"post:conversation:{scope.TenantId:N}:{scope.WorkspaceId:N}:{scope.ConversationId:N}", options.MaxPostsPerMinutePerConversation, TimeSpan.FromMinutes(1), now, out var conversationRetryAfter))
            {
                return CommunicationSafetyDecision.Deny("rate_limited", conversationRetryAfter);
            }

            if (!string.IsNullOrEmpty(normalizedBody))
            {
                var duplicateKey = $"duplicate:{scope.TenantId:N}:{scope.WorkspaceId:N}:{scope.ConversationId:N}:{scope.ActorUserId:N}:{Hash(normalizedBody)}";
                var duplicateWindow = TimeSpan.FromSeconds(Math.Max(1, options.DuplicatePostWindowSeconds));
                if (duplicatePosts.TryGetValue(duplicateKey, out var lastPostedAt) && now - lastPostedAt < duplicateWindow)
                {
                    duplicatePosts[duplicateKey] = now;
                    return CommunicationSafetyDecision.Deny("duplicate_post");
                }

                duplicatePosts[duplicateKey] = now;
            }

            return CommunicationSafetyDecision.Allow();
        }
    }

    public CommunicationSafetyDecision CheckThreadCreate(CommunicationSafetyScope scope, DateTimeOffset now)
    {
        lock (gate)
        {
            var limited = TryLimit($"thread:user:{scope.TenantId:N}:{scope.WorkspaceId:N}:{scope.ActorUserId:N}", options.MaxThreadCreatesPerMinutePerUser, TimeSpan.FromMinutes(1), now, out var retryAfter);
            return limited ? CommunicationSafetyDecision.Deny("rate_limited", retryAfter) : CommunicationSafetyDecision.Allow();
        }
    }

    public CommunicationSafetyDecision CheckReport(CommunicationSafetyScope scope, DateTimeOffset now)
    {
        lock (gate)
        {
            var limited = TryLimit($"report:user:{scope.TenantId:N}:{scope.WorkspaceId:N}:{scope.ActorUserId:N}", options.MaxReportsPerHourPerUser, TimeSpan.FromHours(1), now, out var retryAfter);
            return limited ? CommunicationSafetyDecision.Deny("rate_limited", retryAfter) : CommunicationSafetyDecision.Allow();
        }
    }

    private bool TryLimit(string key, int limit, TimeSpan window, DateTimeOffset now, out int retryAfterSeconds)
    {
        retryAfterSeconds = 0;
        var boundedLimit = Math.Max(1, limit);
        if (!windows.TryGetValue(key, out var queue))
        {
            queue = new Queue<DateTimeOffset>();
            windows[key] = queue;
        }

        while (queue.Count > 0 && now - queue.Peek() >= window)
        {
            queue.Dequeue();
        }

        if (queue.Count >= boundedLimit)
        {
            retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((queue.Peek() + window - now).TotalSeconds));
            return true;
        }

        queue.Enqueue(now);
        return false;
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }
}

