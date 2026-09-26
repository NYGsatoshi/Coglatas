using Coglatas.Application.Announcements;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Announcements;

[Trait("Scope", "Issue390")]
public sealed class AnnouncementEngagementStoreTests
{
    [Fact]
    public async Task AggregateUsesImmutableFrozenCohortDedupesRetriesAndKeepsEngagementOutOfAuditLog()
    {
        var tenantId = Guid.NewGuid();
        var announcementId = Guid.NewGuid();
        var recipientA = Guid.NewGuid();
        var recipientB = Guid.NewGuid();
        var outsideCohort = Guid.NewGuid();
        var tenant = new CurrentTenantService();
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options,
            tenant);
        tenant.SetPlatformScope();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Issue 390 tenant",
            DisplayName = "Issue 390 tenant",
            Slug = $"issue-390-{tenantId:N}",
            Status = TenantStatus.Active
        });
        await db.SaveChangesAsync();
        tenant.SetTenant(tenantId, $"issue-390-{tenantId:N}");

        var clock = new FixedClock(new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.Zero));
        var store = new AnnouncementEngagementStore(db);
        var deliveryLogicalKey = AnnouncementDistributionContract.DeliveryLogicalKey(announcementId);

        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            Action = AnnouncementDistributionContract.FrozenCohortAuditAction,
            EntityType = "Announcement",
            EntityId = announcementId,
            CreatedAt = clock.UtcNow
        });
        db.Notifications.AddRange(
            FrozenDelivery(tenantId, announcementId, recipientA, deliveryLogicalKey, clock.UtcNow),
            FrozenDelivery(tenantId, announcementId, recipientB, deliveryLogicalKey, clock.UtcNow));
        db.AnnouncementReads.Add(new AnnouncementRead
        {
            TenantId = tenantId,
            AnnouncementId = announcementId,
            UserId = recipientA,
            ReadAt = clock.UtcNow.AddMinutes(-5)
        });
        await db.SaveChangesAsync();

        await store.RecordOnceAsync(
            tenantId,
            announcementId,
            recipientA,
            AnnouncementEngagementActions.Acknowledged);
        await store.RecordOnceAsync(
            tenantId,
            announcementId,
            recipientA,
            AnnouncementEngagementActions.Acknowledged);
        await store.RecordOnceAsync(
            tenantId,
            announcementId,
            recipientA,
            AnnouncementEngagementActions.CtaClicked);
        await store.RecordOnceAsync(
            tenantId,
            announcementId,
            recipientA,
            AnnouncementEngagementActions.CtaClicked);
        await store.RecordOnceAsync(
            tenantId,
            announcementId,
            recipientB,
            AnnouncementEngagementActions.CtaClicked);
        await store.RecordOnceAsync(
            tenantId,
            announcementId,
            outsideCohort,
            AnnouncementEngagementActions.Acknowledged);

        // Simulate current membership shrinking to recipientA. Once #388 has
        // frozen delivery, analytics must still use A+B from the delivery ledger.
        var aggregate = await store.GetAggregateAsync(
            tenantId,
            announcementId,
            [recipientA]);

        Assert.True(aggregate.HasFrozenDeliveryCohort);
        Assert.Equal(2, aggregate.RecipientCount);
        Assert.Equal(1, aggregate.ReadCount);
        Assert.Equal(1, aggregate.AcknowledgedCount);
        Assert.Equal(2, aggregate.CtaClickedCount);
        Assert.Single(aggregate.ReadTimesUtc);

        var persistedEngagement = await db.AuditLogs
            .AsNoTracking()
            .Where(log =>
                log.EntityId == announcementId &&
                (log.Action == AnnouncementEngagementActions.Acknowledged ||
                 log.Action == AnnouncementEngagementActions.CtaClicked))
            .ToListAsync();
        Assert.Empty(persistedEngagement);
    }

    private static Notification FrozenDelivery(
        Guid tenantId,
        Guid announcementId,
        Guid userId,
        string logicalKey,
        DateTimeOffset createdAt) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            LogicalKey = logicalKey,
            NotificationType = NotificationType.Announcement,
            Title = "Delivery ledger",
            RelatedEntityType = "Announcement",
            RelatedEntityId = announcementId,
            CreatedAt = createdAt
        };

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
