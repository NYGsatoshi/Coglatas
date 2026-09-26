using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Notifications;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Notifications;

[Trait("Scope", "TaskV1PR07C")]
public sealed class DbNotificationDigestStagingTests
{
    [Fact]
    public async Task StagesGenericDigestAndRecipientOnlyReferenceWithoutSaving()
    {
        var tenantId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var digestJobId = Guid.NewGuid();
        const string logicalKey = "task-deadline-digest:workspace:11111111111111111111111111111111:date:2026-08-04:policy:1";
        var tenant = new CurrentTenantService();
        tenant.SetPlatformScope();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(options, tenant);
        db.Tenants.Add(new Tenant(tenantId)
        {
            Name = "Digest tenant",
            DisplayName = "Digest tenant",
            Slug = $"tenant-{tenantId:N}",
            Status = TenantStatus.Active
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        tenant.SetTenant(tenantId, $"tenant-{tenantId:N}");
        var outbox = new RecordingOutbox();
        var service = new DbNotificationService(db, FixedClock.Instance, tenant, outbox);

        var first = await service.StageTaskDeadlineDigestByLogicalKeyAsync(
            recipientId,
            digestJobId,
            logicalKey);
        var localRetry = await service.StageTaskDeadlineDigestByLogicalKeyAsync(
            recipientId,
            Guid.NewGuid(),
            $"  {logicalKey}  ");

        Assert.Equal(first, localRetry);
        var notificationEntry = Assert.Single(db.ChangeTracker.Entries<Notification>());
        Assert.Equal(EntityState.Added, notificationEntry.State);
        var notification = notificationEntry.Entity;
        Assert.Equal(tenantId, notification.TenantId);
        Assert.Equal(recipientId, notification.UserId);
        Assert.Equal(logicalKey, notification.LogicalKey);
        Assert.Equal(NotificationType.TaskDueSoon, notification.NotificationType);
        Assert.Equal(TaskDeadlineDigestPolicy.NotificationTitle, notification.Title);
        Assert.Null(notification.Body);
        Assert.Equal(TaskDeadlineDigestPolicy.RelatedEntityType, notification.RelatedEntityType);
        Assert.Equal(digestJobId, notification.RelatedEntityId);
        Assert.Equal(1, notification.StateVersion);

        var stateEntry = Assert.Single(db.ChangeTracker.Entries<NotificationUserState>());
        Assert.Equal(EntityState.Added, stateEntry.State);
        Assert.Equal(1, stateEntry.Entity.Version);

        var queued = Assert.Single(outbox.Items);
        Assert.Equal("Notifications.NotificationCreated.v1", queued.Envelope.EventType);
        Assert.Equal(first, queued.Envelope.AggregateId);
        Assert.Equal(1, queued.Envelope.AggregateVersion);
        var propertyNames = queued.Envelope.Payload
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        Assert.Equal(["notificationId", "stateVersion", "requiresRefetch"], propertyNames);
        Assert.Equal(first, queued.Envelope.Payload.GetProperty("notificationId").GetGuid());
        Assert.Equal(1, queued.Envelope.Payload.GetProperty("stateVersion").GetInt64());
        Assert.True(queued.Envelope.Payload.GetProperty("requiresRefetch").GetBoolean());

        var target = Assert.Single(queued.Targets);
        Assert.Equal(RealtimeSubscriptionType.User, target.SubscriptionType);
        Assert.Equal(recipientId, target.ResourceId);

        var payload = queued.Envelope.Payload.GetRawText();
        foreach (var forbidden in new[]
                 {
                     "title", "body", "task", "project", "workspace", "deadline", "comment", "reason", "route"
                 })
        {
            Assert.DoesNotContain(forbidden, payload, StringComparison.OrdinalIgnoreCase);
        }

        // A second context cannot observe the staged entities. The digest
        // generator owns the explicit Notification + Outbox + ledger commit.
        await using var verification = new AppDbContext(options, TenantScope(tenantId));
        Assert.Empty(await verification.Notifications.ToListAsync());
        Assert.Empty(await verification.NotificationUserStates.ToListAsync());
    }

    [Fact]
    public async Task PersistedLogicalIdentityIsStableAndDoesNotStageAnotherSignal()
    {
        var tenantId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var originalDigestJobId = Guid.NewGuid();
        const string logicalKey = "task-deadline-digest:workspace:22222222222222222222222222222222:date:2026-08-04:policy:1";
        var tenant = new CurrentTenantService();
        tenant.SetPlatformScope();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(options, tenant);
        db.Tenants.Add(new Tenant(tenantId)
        {
            Name = "Digest tenant",
            DisplayName = "Digest tenant",
            Slug = $"tenant-{tenantId:N}",
            Status = TenantStatus.Active
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        tenant.SetTenant(tenantId, $"tenant-{tenantId:N}");
        var outbox = new RecordingOutbox();
        var service = new DbNotificationService(db, FixedClock.Instance, tenant, outbox);

        var first = await service.StageTaskDeadlineDigestByLogicalKeyAsync(
            recipientId,
            originalDigestJobId,
            logicalKey);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var persistedRetry = await service.StageTaskDeadlineDigestByLogicalKeyAsync(
            recipientId,
            Guid.NewGuid(),
            logicalKey);

        Assert.Equal(first, persistedRetry);
        Assert.Empty(db.Notifications.Local);
        Assert.Empty(db.NotificationUserStates.Local);
        Assert.Single(outbox.Items);
        var persisted = Assert.Single(await db.Notifications.Where(item => item.LogicalKey == logicalKey).ToListAsync());
        Assert.Equal(originalDigestJobId, persisted.RelatedEntityId);
        Assert.Equal(TaskDeadlineDigestPolicy.NotificationTitle, persisted.Title);
        Assert.Null(persisted.Body);
    }

    private static CurrentTenantService TenantScope(Guid tenantId)
    {
        var tenant = new CurrentTenantService();
        tenant.SetTenant(tenantId, $"tenant-{tenantId:N}");
        return tenant;
    }

    private sealed class FixedClock : IClock
    {
        public static FixedClock Instance { get; } = new();
        public DateTimeOffset UtcNow => new(2026, 8, 4, 3, 0, 0, TimeSpan.Zero);
    }

    private sealed class RecordingOutbox : ITransactionalOutbox
    {
        public List<(DurableEventEnvelope Envelope, IReadOnlyCollection<RealtimeRoutingTarget> Targets)> Items { get; } = [];

        public Task<Result<Guid>> EnqueueAsync(
            DurableEventEnvelope envelope,
            IReadOnlyCollection<RealtimeRoutingTarget> routingTargets,
            CancellationToken cancellationToken = default)
        {
            Items.Add((envelope, routingTargets));
            return Task.FromResult(Result<Guid>.Success(envelope.EventId));
        }
    }
}
