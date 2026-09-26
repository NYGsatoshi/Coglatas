using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Notifications;

public sealed class DbNotificationTaskStagingTests
{
    [Fact]
    public async Task NotificationCreatedStillRoutesOnlyToRecipientUser()
    {
        var tenantId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var tenant = TenantScope(tenantId);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(options, tenant);
        var outbox = new RecordingOutbox();
        var service = new DbNotificationService(db, FixedClock.Instance, tenant, outbox);

        var first = await service.StageTaskByLogicalKeyAsync(
            recipientId,
            NotificationType.TaskAssigned,
            "Task assignment changed",
            taskId,
            "task:assignment:version:2");
        var retry = await service.StageTaskByLogicalKeyAsync(
            recipientId,
            NotificationType.TaskAssigned,
            "A retry must not replace content",
            taskId,
            "task:assignment:version:2");

        Assert.Equal(first, retry);
        var notification = Assert.Single(db.ChangeTracker.Entries<Notification>());
        Assert.Equal(EntityState.Added, notification.State);
        Assert.Null(notification.Entity.Body);
        Assert.Equal("TaskItem", notification.Entity.RelatedEntityType);
        Assert.Equal(taskId, notification.Entity.RelatedEntityId);
        Assert.Equal(1, notification.Entity.StateVersion);
        var state = Assert.Single(db.ChangeTracker.Entries<NotificationUserState>()).Entity;
        Assert.Equal(1, state.Version);

        var queued = Assert.Single(outbox.Items);
        Assert.Equal("Notifications.NotificationCreated.v1", queued.Envelope.EventType);
        Assert.Equal(first, queued.Envelope.AggregateId);
        var properties = queued.Envelope.Payload.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(["notificationId", "stateVersion", "requiresRefetch"], properties);
        Assert.Equal(first, queued.Envelope.Payload.GetProperty("notificationId").GetGuid());
        Assert.True(queued.Envelope.Payload.GetProperty("requiresRefetch").GetBoolean());
        var target = Assert.Single(queued.Targets);
        Assert.Equal(RealtimeSubscriptionType.User, target.SubscriptionType);
        Assert.Equal(recipientId, target.ResourceId);
        Assert.DoesNotContain("title", queued.Envelope.Payload.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("body", queued.Envelope.Payload.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("route", queued.Envelope.Payload.GetRawText(), StringComparison.OrdinalIgnoreCase);

        var verificationTenant = TenantScope(tenantId);
        await using var verification = new AppDbContext(options, verificationTenant);
        Assert.Empty(await verification.Notifications.ToListAsync());
        Assert.Empty(await verification.NotificationUserStates.ToListAsync());
    }

    [Fact]
    public async Task StagingDistinctLogicalEventsForOneRecipientReusesLocalStateAndAdvancesIt()
    {
        var tenantId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var tenant = TenantScope(tenantId);
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options,
            tenant);
        var outbox = new RecordingOutbox();
        var service = new DbNotificationService(db, FixedClock.Instance, tenant, outbox);

        await service.StageTaskByLogicalKeyAsync(
            recipientId, NotificationType.TaskAssigned, "Task assignment changed", taskId, "task:assignment:version:2");
        await service.StageTaskByLogicalKeyAsync(
            recipientId, NotificationType.Mention, "Task comment requires attention", taskId, "task:comment:version:3");

        var state = Assert.Single(db.NotificationUserStates.Local);
        Assert.Equal(2, state.Version);
        Assert.Equal([1L, 2L], db.Notifications.Local.OrderBy(item => item.StateVersion).Select(item => item.StateVersion).ToArray());
        Assert.Equal(2, outbox.Items.Count);
        Assert.Equal([1L, 2L], outbox.Items.Select(item => item.Envelope.AggregateVersion!.Value).ToArray());
    }

    [Fact]
    public async Task ExistingSoftDeletedLogicalIdentityIsReturnedWithoutStagingAReplacementOrSignal()
    {
        var tenantId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        const string logicalKey = "task:deadline:version:9";
        var tenant = new CurrentTenantService();
        tenant.SetPlatformScope();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(options, tenant);
        db.Tenants.Add(new Tenant(tenantId)
        {
            Name = "Tenant",
            DisplayName = "Tenant",
            Slug = $"tenant-{tenantId:N}",
            Status = TenantStatus.Active
        });
        var existing = new Notification
        {
            TenantId = tenantId,
            UserId = recipientId,
            LogicalKey = logicalKey,
            NotificationType = NotificationType.TaskDueSoon,
            Title = "Deleted notification",
            CreatedAt = FixedClock.Instance.UtcNow.AddDays(-1),
            DeletedAt = FixedClock.Instance.UtcNow,
            StateVersion = 4
        };
        db.Notifications.Add(existing);
        var existingId = existing.Id;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        tenant.SetTenant(tenantId, $"tenant-{tenantId:N}");
        var outbox = new RecordingOutbox();
        var service = new DbNotificationService(db, FixedClock.Instance, tenant, outbox);

        var result = await service.StageTaskByLogicalKeyAsync(
            recipientId,
            NotificationType.TaskDueSoon,
            "Task deadline changed",
            Guid.NewGuid(),
            logicalKey);

        Assert.Equal(existingId, result);
        Assert.Empty(db.Notifications.Local);
        Assert.Empty(db.NotificationUserStates.Local);
        Assert.Empty(outbox.Items);
        Assert.Single(await db.Notifications.Where(item => item.LogicalKey == logicalKey).ToListAsync());
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
        public DateTimeOffset UtcNow => new(2026, 8, 2, 3, 0, 0, TimeSpan.Zero);
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
