using Coglatas.Application.Realtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

public sealed class OutboxEventRepository(AppDbContext dbContext) : IOutboxEventRepository
{
    public async Task AddAsync(OutboxEvent eventItem, CancellationToken cancellationToken = default)
    {
        await dbContext.OutboxEvents.AddAsync(eventItem, cancellationToken);
    }

    public async Task<IReadOnlyList<OutboxEvent>> ClaimDueAsync(
        string lockOwner,
        DateTimeOffset now,
        int batchSize,
        TimeSpan lockTimeout,
        CancellationToken cancellationToken = default)
    {
        var boundedBatchSize = Math.Clamp(batchSize, 1, 100);
        var lockToken = Guid.NewGuid();
        var staleBefore = now - lockTimeout;
        await RecoverStaleLocksAsync(staleBefore, now, maximumAttempts: 10, cancellationToken);

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        List<OutboxEvent> claimed;

        if (dbContext.Database.IsNpgsql())
        {
            claimed = await dbContext.OutboxEvents
                .FromSqlInterpolated($@"
                    SELECT * FROM outbox_events
                    WHERE (""Status"" = 'Pending' OR ""Status"" = 'RetryScheduled')
                      AND (""NextAttemptAt"" IS NULL OR ""NextAttemptAt"" <= {now})
                    ORDER BY ""CreatedAt""
                    FOR UPDATE SKIP LOCKED")
                .Take(boundedBatchSize)
                .ToListAsync(cancellationToken);
        }
        else
        {
            claimed = await dbContext.OutboxEvents
                .Where(item =>
                    (item.Status == OutboxEventStatus.Pending || item.Status == OutboxEventStatus.RetryScheduled) &&
                    (!item.NextAttemptAt.HasValue || item.NextAttemptAt <= now))
                .OrderBy(item => item.CreatedAt)
                .Take(boundedBatchSize)
                .ToListAsync(cancellationToken);
        }

        foreach (var item in claimed)
        {
            item.Status = OutboxEventStatus.Processing;
            item.LockedAt = now;
            item.LockOwner = lockOwner;
            item.LockToken = lockToken;
            item.NextAttemptAt = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return claimed;
    }

    public async Task<bool> MarkDeliveredAsync(Guid eventId, Guid lockToken, DateTimeOffset deliveredAt, string? outcomeCode, CancellationToken cancellationToken = default)
    {
        var item = await FindProcessingAsync(eventId, lockToken, cancellationToken);
        if (item is null)
        {
            return false;
        }

        item.Status = OutboxEventStatus.Delivered;
        item.DeliveredAt = deliveredAt;
        item.LastErrorCode = outcomeCode;
        item.LastErrorSummary = null;
        ClearLock(item);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> MarkFailureAsync(
        Guid eventId,
        Guid lockToken,
        DateTimeOffset now,
        bool retryable,
        DateTimeOffset? nextAttemptAt,
        string errorCode,
        string errorSummary,
        int maximumAttempts,
        CancellationToken cancellationToken = default)
    {
        var item = await FindProcessingAsync(eventId, lockToken, cancellationToken);
        if (item is null)
        {
            return false;
        }

        item.AttemptCount++;
        item.LastErrorCode = Bound(errorCode, 100);
        item.LastErrorSummary = Bound(errorSummary, 1000);
        ClearLock(item);
        if (!retryable || item.AttemptCount >= maximumAttempts)
        {
            item.Status = OutboxEventStatus.DeadLetter;
            item.DeadLetteredAt = now;
            item.NextAttemptAt = null;
        }
        else
        {
            item.Status = OutboxEventStatus.RetryScheduled;
            item.NextAttemptAt = nextAttemptAt ?? now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ReleaseAsync(Guid eventId, Guid lockToken, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken = default)
    {
        var item = await FindProcessingAsync(eventId, lockToken, cancellationToken);
        if (item is null)
        {
            return false;
        }

        item.Status = OutboxEventStatus.Pending;
        item.NextAttemptAt = nextAttemptAt;
        ClearLock(item);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> RecoverStaleLocksAsync(DateTimeOffset staleBefore, DateTimeOffset now, int maximumAttempts, CancellationToken cancellationToken = default)
    {
        var stale = await dbContext.OutboxEvents
            .Where(item => item.Status == OutboxEventStatus.Processing && item.LockedAt < staleBefore)
            .ToListAsync(cancellationToken);

        foreach (var item in stale)
        {
            item.AttemptCount++;
            item.LastErrorCode = "StaleProcessingLock";
            item.LastErrorSummary = "The dispatcher processing lock expired before completion.";
            ClearLock(item);
            if (item.AttemptCount >= maximumAttempts)
            {
                item.Status = OutboxEventStatus.DeadLetter;
                item.DeadLetteredAt = now;
                item.NextAttemptAt = null;
            }
            else
            {
                item.Status = OutboxEventStatus.RetryScheduled;
                item.NextAttemptAt = now;
            }
        }

        if (stale.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return stale.Count;
    }

    public async Task<int> CleanupAsync(DateTimeOffset deliveredBefore, DateTimeOffset deadLetterBefore, DateTimeOffset cancelledBefore, CancellationToken cancellationToken = default)
    {
        var expired = await dbContext.OutboxEvents
            .Where(item =>
                (item.Status == OutboxEventStatus.Delivered && item.DeliveredAt < deliveredBefore) ||
                (item.Status == OutboxEventStatus.DeadLetter && item.DeadLetteredAt < deadLetterBefore) ||
                (item.Status == OutboxEventStatus.Cancelled && item.UpdatedAt < cancelledBefore))
            .ToListAsync(cancellationToken);
        dbContext.OutboxEvents.RemoveRange(expired);
        if (expired.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return expired.Count;
    }

    public async Task<RealtimeOutboxDiagnostics> GetDiagnosticsAsync(DateTimeOffset staleBefore, CancellationToken cancellationToken = default)
    {
        var pending = await dbContext.OutboxEvents.CountAsync(item => item.Status == OutboxEventStatus.Pending, cancellationToken);
        var retry = await dbContext.OutboxEvents.CountAsync(item => item.Status == OutboxEventStatus.RetryScheduled, cancellationToken);
        var deadLetter = await dbContext.OutboxEvents.CountAsync(item => item.Status == OutboxEventStatus.DeadLetter, cancellationToken);
        var oldest = await dbContext.OutboxEvents
            .Where(item => item.Status == OutboxEventStatus.Pending || item.Status == OutboxEventStatus.RetryScheduled)
            .OrderBy(item => item.CreatedAt)
            .Select(item => (DateTimeOffset?)item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var stale = await dbContext.OutboxEvents.CountAsync(item => item.Status == OutboxEventStatus.Processing && item.LockedAt < staleBefore, cancellationToken);
        return new RealtimeOutboxDiagnostics(pending, retry, deadLetter, oldest, stale, 0, 0, 0);
    }

    public Task<OutboxEvent?> GetByIdAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        return dbContext.OutboxEvents.FirstOrDefaultAsync(item => item.Id == eventId, cancellationToken);
    }

    public async Task<bool> ReplayAsync(Guid eventId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var item = await dbContext.OutboxEvents.FirstOrDefaultAsync(item => item.Id == eventId, cancellationToken);
        if (item is null || item.Status is OutboxEventStatus.Processing or OutboxEventStatus.Cancelled)
        {
            return false;
        }

        item.Status = OutboxEventStatus.Pending;
        item.NextAttemptAt = now;
        item.DeadLetteredAt = null;
        item.LastErrorCode = null;
        item.LastErrorSummary = null;
        ClearLock(item);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private Task<OutboxEvent?> FindProcessingAsync(Guid eventId, Guid lockToken, CancellationToken cancellationToken)
    {
        return dbContext.OutboxEvents.FirstOrDefaultAsync(
            item => item.Id == eventId && item.Status == OutboxEventStatus.Processing && item.LockToken == lockToken,
            cancellationToken);
    }

    private static void ClearLock(OutboxEvent item)
    {
        item.LockedAt = null;
        item.LockOwner = null;
        item.LockToken = null;
    }

    private static string Bound(string value, int maximumLength)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
        return trimmed.Length <= maximumLength ? trimmed : trimmed[..maximumLength];
    }
}
