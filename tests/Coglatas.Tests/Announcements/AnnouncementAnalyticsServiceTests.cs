using Coglatas.Application.Announcements;
using Coglatas.Application.Channels;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Groups;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Announcements;

// These tests exercise the production use case with recording ports. Repository
// visibility and tenant filtering remain infrastructure responsibilities; these
// doubles do not establish SQL translation, PostgreSQL isolation or RLS behavior.
[Trait("Scope", "Issue390")]
public sealed class AnnouncementAnalyticsServiceTests
{
    [Theory]
    [InlineData("workspace")]
    [InlineData("group")]
    [InlineData("channel")]
    public async Task UnionAnalyticsRequireManagementOfEveryPersistedAudience(string deniedScope)
    {
        var fixture = new Fixture();
        fixture.Targets = [fixture.WorkspaceTarget, fixture.GroupTarget, fixture.ChannelTarget];
        fixture.Grants.UnionWith(fixture.Targets.Select(ScopeKey));
        fixture.Grants.Remove(ScopeKey(deniedScope switch
        {
            "workspace" => fixture.WorkspaceTarget,
            "group" => fixture.GroupTarget,
            _ => fixture.ChannelTarget
        }));

        var denied = await fixture.Service().GetAsync(fixture.Announcement.Id);

        Assert.False(denied.IsSuccess);
        Assert.Equal("You are not allowed to view announcement analytics.", denied.Error);
        Assert.DoesNotContain("recipients", fixture.Calls);
        Assert.Empty(fixture.AggregateRequests);
        fixture.AssertNoWrites();

        fixture.Grants.UnionWith(fixture.Targets.Select(ScopeKey));
        var allowed = await fixture.Service().GetAsync(fixture.Announcement.Id);

        Assert.True(allowed.IsSuccess);
        Assert.Contains(ScopeKey(fixture.WorkspaceTarget), fixture.ManagementChecks);
        Assert.Contains(ScopeKey(fixture.GroupTarget), fixture.ManagementChecks);
        Assert.Contains(ScopeKey(fixture.ChannelTarget), fixture.ManagementChecks);
        Assert.Single(fixture.AggregateRequests);
    }

    [Theory]
    [InlineData("workspace")]
    [InlineData("group")]
    [InlineData("channel")]
    public async Task LegacyAnalyticsRequireTheDeepestEffectiveScope(string scope)
    {
        var fixture = new Fixture();
        var target = scope switch
        {
            "workspace" => fixture.WorkspaceTarget,
            "group" => fixture.GroupTarget,
            _ => fixture.ChannelTarget
        };
        fixture.Announcement.WorkspaceId = target.WorkspaceId;
        fixture.Announcement.GroupId = target.GroupId;
        fixture.Announcement.ChannelId = target.ChannelId;
        fixture.Grants.UnionWith([ScopeKey(fixture.WorkspaceTarget), ScopeKey(fixture.GroupTarget)]);
        fixture.Grants.Remove(ScopeKey(target));

        Assert.False((await fixture.Service().GetAsync(fixture.Announcement.Id)).IsSuccess);
        Assert.Empty(fixture.AggregateRequests);
        fixture.Grants.Add(ScopeKey(target));

        Assert.True((await fixture.Service().GetAsync(fixture.Announcement.Id)).IsSuccess);
        Assert.All(fixture.ManagementChecks, check => Assert.Equal(ScopeKey(target), check));
        fixture.AssertNoWrites();
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public async Task MalformedPersistedTargetsFailClosed(bool workspace, bool group, bool channel)
    {
        var fixture = new Fixture();
        fixture.Targets = [new AnnouncementDraftTargetRequest(
            workspace ? fixture.WorkspaceTarget.WorkspaceId : null,
            group ? fixture.GroupTarget.GroupId : null,
            channel ? fixture.ChannelTarget.ChannelId : null)];
        // Valid-looking legacy columns and broad parent authority cannot repair
        // an incomplete persisted target.
        fixture.Grants.UnionWith([ScopeKey(fixture.WorkspaceTarget), ScopeKey(fixture.GroupTarget)]);

        Assert.False((await fixture.Service().GetAsync(fixture.Announcement.Id)).IsSuccess);
        Assert.Empty(fixture.AggregateRequests);
        fixture.AssertNoWrites();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task OnlyAnActivePersistedSystemAdministratorBypassesScopeManagement(bool inactive, bool deleted)
    {
        var fixture = new Fixture
        {
            User =
            {
                SystemRole = SystemRole.SystemAdmin,
                Status = inactive ? UserStatus.Suspended : UserStatus.Active
            }
        };
        if (deleted) fixture.User.MarkDeleted(fixture.UtcNow);
        fixture.Targets = [fixture.ChannelTarget];

        var result = await fixture.Service().GetAsync(fixture.Announcement.Id);

        Assert.Equal(!inactive && !deleted, result.IsSuccess);
        Assert.Equal(!inactive && !deleted ? 1 : 0, fixture.AggregateRequests.Count);
        fixture.AssertNoWrites();
    }

    [Fact]
    public async Task ClaimedAdministratorAndAuthorshipDoNotGrantAnalyticsAuthority()
    {
        var fixture = new Fixture { ClaimedRole = SystemRole.SystemAdmin };
        fixture.Announcement.AuthorUserId = fixture.ActorId;
        fixture.Targets = [fixture.WorkspaceTarget];

        Assert.False((await fixture.Service().GetAsync(fixture.Announcement.Id)).IsSuccess);
        Assert.Empty(fixture.AggregateRequests);
        fixture.AssertNoWrites();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GlobalLegacyAnalyticsAreRestrictedToSystemAdministrators(bool administrator)
    {
        var fixture = new Fixture
        {
            Announcement = { WorkspaceId = null },
            User = { SystemRole = administrator ? SystemRole.SystemAdmin : SystemRole.Teacher }
        };

        Assert.Equal(administrator, (await fixture.Service().GetAsync(fixture.Announcement.Id)).IsSuccess);
        Assert.Empty(fixture.ManagementChecks);
    }

    [Theory]
    [InlineData("get", false)]
    [InlineData("get", true)]
    [InlineData("acknowledge", false)]
    [InlineData("acknowledge", true)]
    [InlineData("cta", false)]
    [InlineData("cta", true)]
    public async Task MissingAndDeletedAnnouncementsHaveNoEngagementEffects(string operation, bool deleted)
    {
        var fixture = new Fixture { Missing = !deleted };
        if (deleted) fixture.Announcement.MarkDeleted(fixture.UtcNow);

        var result = await fixture.InvokeAsync(operation);

        Assert.False(result.IsSuccess);
        Assert.Equal("Announcement not found.", result.Error);
        Assert.DoesNotContain("recipients", fixture.Calls);
        Assert.Empty(fixture.AggregateRequests);
        fixture.AssertNoWrites();
    }

    [Theory]
    [InlineData("get", false)]
    [InlineData("get", true)]
    [InlineData("acknowledge", false)]
    [InlineData("acknowledge", true)]
    [InlineData("cta", false)]
    [InlineData("cta", true)]
    public async Task AuthenticationRequiresBothIdentityAndAuthenticatedState(string operation, bool hasIdentity)
    {
        var fixture = new Fixture
        {
            Authenticated = !hasIdentity, HasIdentity = hasIdentity,
            User = { SystemRole = SystemRole.SystemAdmin }
        };

        Assert.False((await fixture.InvokeAsync(operation)).IsSuccess);
        Assert.DoesNotContain("recipients", fixture.Calls);
        Assert.Empty(fixture.AggregateRequests);
        fixture.AssertNoWrites();
    }

    [Theory]
    [InlineData("acknowledge", false)]
    [InlineData("acknowledge", true)]
    [InlineData("cta", false)]
    [InlineData("cta", true)]
    public async Task ManagersAndAdministratorsMustAlsoBeVisibleRecipientsForEngagement(string operation, bool visible)
    {
        var fixture = new Fixture
        {
            Visible = visible,
            User = { SystemRole = SystemRole.SystemAdmin }
        };
        fixture.Recipients = [Fixture.Recipient(Guid.NewGuid())];

        var result = await fixture.InvokeAsync(operation);

        Assert.False(result.IsSuccess);
        Assert.Equal("Announcement not found.", result.Error);
        Assert.Equal(visible, fixture.Calls.Contains("recipients"));
        fixture.AssertNoWrites();
    }

    [Theory]
    [InlineData("acknowledge", "ordinary", false)]
    [InlineData("acknowledge", "active-admin", true)]
    [InlineData("acknowledge", "claimed-only-admin", false)]
    [InlineData("acknowledge", "suspended-admin", false)]
    [InlineData("acknowledge", "deleted-admin", false)]
    [InlineData("cta", "ordinary", false)]
    [InlineData("cta", "active-admin", true)]
    [InlineData("cta", "claimed-only-admin", false)]
    [InlineData("cta", "suspended-admin", false)]
    [InlineData("cta", "deleted-admin", false)]
    public async Task EngagementVisibilityReceivesThePersistedAdministratorDecision(
        string operation, string actorState, bool expectedAdministrator)
    {
        var fixture = new Fixture
        {
            ClaimedRole = actorState == "claimed-only-admin" ? SystemRole.SystemAdmin : SystemRole.Teacher
        };
        if (actorState is "active-admin" or "suspended-admin" or "deleted-admin")
            fixture.User.SystemRole = SystemRole.SystemAdmin;
        if (actorState == "suspended-admin") fixture.User.Status = UserStatus.Suspended;
        if (actorState == "deleted-admin") fixture.User.MarkDeleted(fixture.UtcNow);

        var result = await fixture.InvokeAsync(operation);

        // Visibility is independently supplied by the repository double. These
        // cases verify the scope-bypass flag, not an account-admission policy.
        Assert.True(result.IsSuccess);
        Assert.Equal((fixture.Announcement.Id, fixture.ActorId, expectedAdministrator),
            Assert.Single(fixture.VisibilityChecks));
        Assert.Equal((fixture.Announcement.TenantId, fixture.Announcement.Id, fixture.ActorId,
            operation == "acknowledge" ? AnnouncementEngagementActions.Acknowledged : AnnouncementEngagementActions.CtaClicked),
            Assert.Single(fixture.Events));
    }

    [Theory]
    [InlineData("acknowledge")]
    [InlineData("cta")]
    public async Task UnsupportedEngagementDoesNotStageReadsOrPersistEvents(string operation)
    {
        var fixture = new Fixture
        {
            Announcement = { RequiresReadConfirmation = false, Body = "Plain announcement with no CTA" }
        };

        var result = await fixture.InvokeAsync(operation);

        Assert.False(result.IsSuccess);
        Assert.Equal(operation == "cta" ? "This announcement does not have a CTA." :
            "This announcement does not require acknowledgement.", result.Error);
        fixture.AssertNoWrites();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcknowledgementCommitsReadBeforeSidecarAndDoesNotRestageExistingRead(bool alreadyRead)
    {
        var fixture = new Fixture();
        fixture.Recipients = [Fixture.Recipient(fixture.ActorId, alreadyRead)];

        Assert.True((await fixture.Service().AcknowledgeAsync(fixture.Announcement.Id)).IsSuccess);

        Assert.Equal(alreadyRead ? 0 : 1, fixture.CommittedReads.Count);
        if (!alreadyRead)
        {
            var read = Assert.Single(fixture.CommittedReads);
            Assert.Equal(fixture.Announcement.Id, read.AnnouncementId);
            Assert.Equal(fixture.ActorId, read.UserId);
            Assert.Equal(fixture.UtcNow, read.ReadAt);
        }
        Assert.Equal(["save", "event:" + AnnouncementEngagementActions.Acknowledged],
            fixture.Calls.Where(call => call == "save" || call.StartsWith("event:", StringComparison.Ordinal)));
        var written = Assert.Single(fixture.Events);
        Assert.Equal((fixture.Announcement.TenantId, fixture.Announcement.Id, fixture.ActorId,
            AnnouncementEngagementActions.Acknowledged), written);
    }

    [Fact]
    public async Task ReadSaveFailurePreventsAcknowledgementSidecarWrite()
    {
        var fixture = new Fixture { FailureAt = "save" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service().AcknowledgeAsync(fixture.Announcement.Id));

        Assert.Single(fixture.PendingReads);
        Assert.Empty(fixture.CommittedReads);
        Assert.Empty(fixture.Events);
        Assert.DoesNotContain("event:" + AnnouncementEngagementActions.Acknowledged, fixture.Calls);
    }

    [Fact]
    public async Task SidecarFailureLeavesCommittedReadAndRetryDoesNotCreateAnotherRead()
    {
        var fixture = new Fixture { FailureAt = "event" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service().AcknowledgeAsync(fixture.Announcement.Id));

        Assert.Single(fixture.CommittedReads);
        Assert.Empty(fixture.Events);
        fixture.FailureAt = null;
        Assert.True((await fixture.Service().AcknowledgeAsync(fixture.Announcement.Id)).IsSuccess);
        Assert.Single(fixture.CommittedReads);
        Assert.Single(fixture.Events);
    }

    [Fact]
    public async Task CtaClickRecordsOnlyTheAuthenticatedRecipientsEvent()
    {
        var fixture = new Fixture();

        Assert.True((await fixture.Service().TrackCtaClickAsync(fixture.Announcement.Id)).IsSuccess);

        Assert.Equal((fixture.Announcement.TenantId, fixture.Announcement.Id, fixture.ActorId,
            AnnouncementEngagementActions.CtaClicked), Assert.Single(fixture.Events));
        Assert.Empty(fixture.PendingReads);
        Assert.Empty(fixture.CommittedReads);
        Assert.DoesNotContain("save", fixture.Calls);
    }

    [Theory]
    [InlineData("get", false)]
    [InlineData("get", true)]
    [InlineData("acknowledge", false)]
    [InlineData("cta", false)]
    public async Task MissingPersistenceFailsBeforeResourceOrAudienceReads(string operation, bool missingDistribution)
    {
        var fixture = new Fixture();
        var service = fixture.Service(engagementAvailable: missingDistribution, distributionAvailable: !missingDistribution);

        var result = await fixture.InvokeAsync(operation, service);

        Assert.False(result.IsSuccess);
        Assert.Contains("persistence is unavailable", result.Error);
        Assert.Empty(fixture.Calls);
        fixture.AssertNoWrites();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AggregateUsesStoreDenominatorAndDeduplicatesAudienceInput(bool frozen)
    {
        var fixture = new Fixture();
        fixture.Grants.Add(ScopeKey(fixture.WorkspaceTarget));
        var other = Guid.NewGuid();
        fixture.Recipients = [Fixture.Recipient(fixture.ActorId), Fixture.Recipient(other), Fixture.Recipient(fixture.ActorId)];
        fixture.Aggregate = new AnnouncementEngagementAggregate(frozen, 6, 2, 1, 3, []);

        var result = await fixture.Service().GetAsync(fixture.Announcement.Id);

        Assert.True(result.IsSuccess);
        var response = result.Value!;
        Assert.Equal(6, response.RecipientCount);
        Assert.Equal(2, response.ReadCount);
        Assert.Equal(0.3333, response.ReadRate);
        Assert.Equal(1, response.AcknowledgedCount);
        Assert.Equal(0.1667, response.AcknowledgementRate);
        Assert.Equal(3, response.CtaClickCount);
        Assert.Equal(0.5, response.CtaClickThroughRate);
        Assert.Equal("clickThrough", response.CtaMetric);
        Assert.Equal(frozen ? "frozenDeliveryCohort" : "currentAuthorizedAudience", response.DenominatorKind);
        var request = Assert.Single(fixture.AggregateRequests);
        Assert.Equal(fixture.Announcement.TenantId, request.TenantId);
        Assert.Equal(fixture.Announcement.Id, request.AnnouncementId);
        Assert.Equal(new[] { fixture.ActorId, other }, request.RecipientIds);
        fixture.AssertNoWrites();
    }

    [Theory]
    [InlineData(AnnouncementPriority.Normal, false)]
    [InlineData(AnnouncementPriority.Important, true)]
    [InlineData(AnnouncementPriority.Urgent, true)]
    public async Task RecognitionMedianIncludesPeriodBoundariesAndExcludesOutsideReads(AnnouncementPriority priority, bool reported)
    {
        var fixture = new Fixture();
        fixture.Grants.Add(ScopeKey(fixture.WorkspaceTarget));
        fixture.Announcement.Priority = priority;
        var start = fixture.Announcement.PublishedAt;
        fixture.Announcement.ExpiresAt = start.AddSeconds(40);
        fixture.Aggregate = new AnnouncementEngagementAggregate(true, 6, 6, 0, 0,
            [start.AddSeconds(41), start.AddSeconds(40), start.AddSeconds(-1), start.AddSeconds(10), start, start.AddSeconds(30)]);

        var response = (await fixture.Service().GetAsync(fixture.Announcement.Id)).Value!;

        Assert.Equal(start, response.PeriodStartUtc);
        Assert.Equal(start.AddSeconds(40), response.PeriodEndUtc);
        double? expectedMedian = reported ? 20d : null;
        Assert.Equal(expectedMedian, response.MedianTimeToRecognitionSeconds);
        Assert.Equal(6, response.ReadCount);
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(30, 30)]
    [InlineData(300, 120)]
    public async Task PeriodEndIsBoundedByExpiryNowAndPublication(int expirySeconds, int expectedSeconds)
    {
        var fixture = new Fixture();
        fixture.Grants.Add(ScopeKey(fixture.WorkspaceTarget));
        fixture.Announcement.ExpiresAt = fixture.Announcement.PublishedAt.AddSeconds(expirySeconds);

        var response = (await fixture.Service().GetAsync(fixture.Announcement.Id)).Value!;

        Assert.Equal(fixture.Announcement.PublishedAt.AddSeconds(expectedSeconds), response.PeriodEndUtc);
    }

    [Fact]
    public async Task EmptyAudienceHasZeroRatesAndDisabledMetricsRemainAbsent()
    {
        var fixture = new Fixture();
        fixture.Grants.Add(ScopeKey(fixture.WorkspaceTarget));
        fixture.Announcement.Body = "Plain text";
        fixture.Announcement.RequiresReadConfirmation = false;
        fixture.Announcement.Priority = AnnouncementPriority.Urgent;
        fixture.Aggregate = new AnnouncementEngagementAggregate(true, 0, 0, 0, 0, []);

        var response = (await fixture.Service().GetAsync(fixture.Announcement.Id)).Value!;

        Assert.Equal(0, response.ReadRate);
        Assert.Null(response.AcknowledgedCount);
        Assert.Null(response.AcknowledgementRate);
        Assert.Null(response.CtaClickCount);
        Assert.Null(response.CtaClickThroughRate);
        Assert.Null(response.CtaMetric);
        Assert.Null(response.MedianTimeToRecognitionSeconds);
    }

    [Fact]
    public async Task FuturePublicationClampsPeriodAndAnEnabledEmptyCohortHasZeroRates()
    {
        var fixture = new Fixture();
        fixture.Grants.Add(ScopeKey(fixture.WorkspaceTarget));
        fixture.Announcement.PublishedAt = fixture.UtcNow.AddMinutes(2);
        fixture.Aggregate = new AnnouncementEngagementAggregate(true, 0, 0, 0, 0, []);

        var response = (await fixture.Service().GetAsync(fixture.Announcement.Id)).Value!;

        Assert.Equal(fixture.Announcement.PublishedAt, response.PeriodEndUtc);
        Assert.Equal(0d, response.AcknowledgementRate);
        Assert.Equal(0d, response.CtaClickThroughRate);
    }

    [Fact]
    public async Task OddRecognitionSampleReturnsMiddleElapsedTime()
    {
        var fixture = new Fixture();
        fixture.Grants.Add(ScopeKey(fixture.WorkspaceTarget));
        fixture.Announcement.Priority = AnnouncementPriority.Important;
        var start = fixture.Announcement.PublishedAt;
        fixture.Aggregate = new AnnouncementEngagementAggregate(false, 3, 3, 0, 0,
            [start.AddSeconds(100), start.AddSeconds(15), start.AddSeconds(50)]);

        var response = (await fixture.Service().GetAsync(fixture.Announcement.Id)).Value!;

        Assert.Equal(50d, response.MedianTimeToRecognitionSeconds);
        Assert.Equal(fixture.UtcNow, response.PeriodEndUtc);
    }

    [Theory]
    [InlineData(false, false, 1, 1, 10d)]
    [InlineData(true, false, 2, 2, 20d)]
    [InlineData(true, true, 0, 0, null)]
    public async Task RealEngagementStorePreservesFrozenAccountingAndExcludesOutsideEvents(
        bool frozen, bool emptyLedger, int expectedRecipients, int expectedClicks, double? expectedMedian)
    {
        var fixture = new Fixture();
        fixture.Grants.Add(ScopeKey(fixture.WorkspaceTarget));
        fixture.Announcement.Priority = AnnouncementPriority.Important;
        var tenant = new CurrentTenantService();
        // EF InMemory executes the real store's cohort/numerator selection and
        // deduplication fallback. It cannot establish relational uniqueness,
        // PostgreSQL sidecar persistence, transactions, or RLS.
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options, tenant);
        tenant.SetPlatformScope();
        db.Tenants.Add(new Tenant
        {
            Id = fixture.Announcement.TenantId, Name = "Analytics fixture", DisplayName = "Analytics fixture",
            Slug = "analytics-fixture", Status = TenantStatus.Active
        });
        await db.SaveChangesAsync();
        tenant.SetTenant(fixture.Announcement.TenantId, "analytics-fixture");
        var formerRecipient = Guid.NewGuid();
        var outsideRecipient = Guid.NewGuid();
        if (frozen)
        {
            db.AuditLogs.Add(new AuditLog
            {
                TenantId = fixture.Announcement.TenantId, EntityId = fixture.Announcement.Id,
                EntityType = "Announcement", Action = AnnouncementDistributionContract.FrozenCohortAuditAction,
                CreatedAt = fixture.UtcNow
            });
        }
        if (!emptyLedger)
        {
            db.Notifications.AddRange(Delivery(fixture.ActorId), Delivery(formerRecipient), Delivery(formerRecipient));
            var unrelated = Delivery(outsideRecipient);
            unrelated.LogicalKey = "unrelated-delivery";
            db.Notifications.Add(unrelated);
        }
        db.AnnouncementReads.AddRange(Read(fixture.ActorId, 10), Read(formerRecipient, 30), Read(outsideRecipient, 60));
        await db.SaveChangesAsync();
        var store = new AnnouncementEngagementStore(db);
        fixture.EngagementOverride = store;
        foreach (var userId in new[] { fixture.ActorId, formerRecipient, outsideRecipient })
        {
            await store.RecordOnceAsync(fixture.Announcement.TenantId, fixture.Announcement.Id, userId, AnnouncementEngagementActions.CtaClicked);
            await store.RecordOnceAsync(fixture.Announcement.TenantId, fixture.Announcement.Id, userId, AnnouncementEngagementActions.CtaClicked);
        }
        await store.RecordOnceAsync(fixture.Announcement.TenantId, fixture.Announcement.Id,
            fixture.ActorId, AnnouncementEngagementActions.Acknowledged);
        await store.RecordOnceAsync(fixture.Announcement.TenantId, fixture.Announcement.Id,
            outsideRecipient, AnnouncementEngagementActions.Acknowledged);

        var result = await fixture.Service().GetAsync(fixture.Announcement.Id);

        Assert.True(result.IsSuccess);
        var response = result.Value!;
        Assert.Equal(expectedRecipients, response.RecipientCount);
        Assert.Equal(expectedRecipients, response.ReadCount);
        Assert.Equal(expectedRecipients == 0 ? 0d : 1d, response.ReadRate);
        Assert.Equal(expectedRecipients == 0 ? 0 : 1, response.AcknowledgedCount);
        Assert.Equal(expectedRecipients == 0 ? 0d : Math.Round(1d / expectedRecipients, 4), response.AcknowledgementRate);
        Assert.Equal(expectedClicks, response.CtaClickCount);
        Assert.Equal(expectedRecipients == 0 ? 0d : 1d, response.CtaClickThroughRate);
        Assert.Equal(expectedMedian, response.MedianTimeToRecognitionSeconds);
        Assert.Equal(frozen ? "frozenDeliveryCohort" : "currentAuthorizedAudience", response.DenominatorKind);
        Assert.False(await db.AuditLogs.AnyAsync(log => log.Action == AnnouncementEngagementActions.Acknowledged ||
            log.Action == AnnouncementEngagementActions.CtaClicked));
        fixture.AssertNoWrites();

        Notification Delivery(Guid userId) => new()
        {
            TenantId = fixture.Announcement.TenantId, UserId = userId, NotificationType = NotificationType.Announcement,
            RelatedEntityType = "Announcement", RelatedEntityId = fixture.Announcement.Id,
            LogicalKey = AnnouncementDistributionContract.DeliveryLogicalKey(fixture.Announcement.Id),
            Title = "Synthetic delivery", CreatedAt = fixture.UtcNow
        };

        AnnouncementRead Read(Guid userId, int seconds) => new()
        {
            TenantId = fixture.Announcement.TenantId, AnnouncementId = fixture.Announcement.Id,
            UserId = userId, ReadAt = fixture.Announcement.PublishedAt.AddSeconds(seconds)
        };
    }

    [Theory]
    [InlineData("get", "targets")]
    [InlineData("get", "aggregate")]
    [InlineData("acknowledge", "recipients")]
    [InlineData("cta", "event")]
    public async Task DependencyErrorsPropagateWithoutReturningSuccessfulAnalyticsOrEngagement(string operation, string stage)
    {
        var fixture = new Fixture { FailureAt = stage };
        fixture.Grants.Add(ScopeKey(fixture.WorkspaceTarget));

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.InvokeAsync(operation));

        fixture.AssertNoWrites();
    }

    [Theory]
    [InlineData("get", "aggregate")]
    [InlineData("acknowledge", "save")]
    [InlineData("cta", "event")]
    public async Task CancellationIsForwardedToTheActualPersistenceBoundary(string operation, string stage)
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture { CancelAt = stage, Cancellation = cancellation };
        fixture.Grants.Add(ScopeKey(fixture.WorkspaceTarget));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.InvokeAsync(operation, token: cancellation.Token));

        Assert.True(cancellation.IsCancellationRequested);
        Assert.NotEmpty(fixture.ObservedTokens);
        Assert.All(fixture.ObservedTokens, token => Assert.Equal(cancellation.Token, token));
        Assert.Empty(fixture.CommittedReads);
        Assert.Empty(fixture.Events);
    }

    private static string ScopeKey(AnnouncementDraftTargetRequest target) => target.ChannelId is { } channel
        ? $"channel:{channel}" : target.GroupId is { } group ? $"group:{group}" : $"workspace:{target.WorkspaceId}";

    private sealed class Fixture : IAnnouncementRepository, IWorkspaceAuthorizationService,
        IGroupAuthorizationService, IChannelAuthorizationService, IUserRepository, ICurrentUser,
        IClock, IUnitOfWork, IAnnouncementEngagementStore, IAnnouncementDistributionStore
    {
        public Guid ActorId { get; } = Guid.NewGuid();
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 4, 8, 2, 0, TimeSpan.Zero);
        public Announcement Announcement { get; }
        public User User { get; }
        public AnnouncementDraftTargetRequest WorkspaceTarget { get; } = new(Guid.NewGuid(), null, null);
        public AnnouncementDraftTargetRequest GroupTarget { get; } = new(Guid.NewGuid(), Guid.NewGuid(), null);
        public AnnouncementDraftTargetRequest ChannelTarget { get; } = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        public IReadOnlyList<AnnouncementDraftTargetRequest> Targets { get; set; } = [];
        public IReadOnlyList<AnnouncementTargetUser> Recipients { get; set; }
        public AnnouncementEngagementAggregate Aggregate { get; set; } = new(true, 1, 0, 0, 0, []);
        public IAnnouncementEngagementStore? EngagementOverride { get; set; }
        public HashSet<string> Grants { get; } = [];
        public List<string> ManagementChecks { get; } = [];
        public List<string> Calls { get; } = [];
        public List<(Guid AnnouncementId, Guid UserId, bool IsSystemAdmin)> VisibilityChecks { get; } = [];
        public List<CancellationToken> ObservedTokens { get; } = [];
        public List<AnnouncementRead> PendingReads { get; } = [];
        public List<AnnouncementRead> CommittedReads { get; } = [];
        public List<(Guid TenantId, Guid AnnouncementId, Guid UserId, string Action)> Events { get; } = [];
        public List<(Guid TenantId, Guid AnnouncementId, Guid[] RecipientIds)> AggregateRequests { get; } = [];
        public bool Missing { get; init; }
        public bool Visible { get; init; } = true;
        public bool Authenticated { get; init; } = true;
        public bool HasIdentity { get; init; } = true;
        public SystemRole? ClaimedRole { get; init; } = SystemRole.Teacher;
        public string? FailureAt { get; set; }
        public string? CancelAt { get; init; }
        public CancellationTokenSource? Cancellation { get; init; }
        Guid? ICurrentUser.UserId => HasIdentity ? ActorId : null;
        Guid? ICurrentUser.SessionId => null;
        string ICurrentUser.Email => "actor@example.test";
        SystemRole? ICurrentUser.SystemRole => ClaimedRole;
        bool ICurrentUser.IsAuthenticated => Authenticated;

        public Fixture()
        {
            User = new User { Id = ActorId, Status = UserStatus.Active, SystemRole = SystemRole.Teacher };
            Announcement = new Announcement
            {
                Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), WorkspaceId = WorkspaceTarget.WorkspaceId,
                PublishedAt = UtcNow.AddMinutes(-2), RequiresReadConfirmation = true,
                Body = AnnouncementContentContract.PrepareForPersistence("Synthetic announcement",
                    new AnnouncementActionLink("Open", "/app/workspaces")).PersistedBody
            };
            Recipients = [Recipient(ActorId)];
        }

        public AnnouncementAnalyticsService Service(bool engagementAvailable = true, bool distributionAvailable = true) =>
            new(this, this, this, this, this, this, this, this,
                engagementAvailable ? EngagementOverride ?? this : null, distributionAvailable ? this : null);

        public async Task<Result> InvokeAsync(string operation, AnnouncementAnalyticsService? service = null, CancellationToken token = default)
        {
            service ??= Service();
            if (operation == "get")
            {
                var response = await service.GetAsync(Announcement.Id, token);
                return response.IsSuccess ? Result.Success() : Result.Failure(response.Error!);
            }
            return operation == "acknowledge"
                ? await service.AcknowledgeAsync(Announcement.Id, token)
                : await service.TrackCtaClickAsync(Announcement.Id, token);
        }

        public void AssertNoWrites()
        {
            Assert.Empty(PendingReads);
            Assert.Empty(CommittedReads);
            Assert.Empty(Events);
            Assert.DoesNotContain("save", Calls);
        }

        public static AnnouncementTargetUser Recipient(Guid id, bool read = false) => new(id, "Synthetic recipient", "recipient@example.test", read);

        private void Visit(string stage, CancellationToken token)
        {
            Calls.Add(stage);
            ObservedTokens.Add(token);
            if (CancelAt == stage) Cancellation!.Cancel();
            token.ThrowIfCancellationRequested();
            if (FailureAt == stage) throw new InvalidOperationException("Synthetic " + stage + " failure");
        }

        public Task<Announcement?> GetAsync(Guid announcementId, CancellationToken cancellationToken = default)
        {
            Visit("get", cancellationToken);
            return Task.FromResult(Missing || announcementId != Announcement.Id ? null : Announcement);
        }

        public Task<bool> IsVisibleToUserAsync(Guid announcementId, Guid userId, bool isSystemAdmin, CancellationToken cancellationToken = default)
        {
            Visit("visible", cancellationToken);
            VisibilityChecks.Add((announcementId, userId, isSystemAdmin));
            return Task.FromResult(Visible && announcementId == Announcement.Id && userId == ActorId);
        }

        public Task<IReadOnlyList<AnnouncementTargetUser>> ListTargetUsersAsync(Announcement announcement, CancellationToken cancellationToken = default)
        {
            Visit("recipients", cancellationToken);
            if (announcement != Announcement) throw new InvalidOperationException("Unexpected announcement");
            return Task.FromResult<IReadOnlyList<AnnouncementTargetUser>>(Recipients.Select(recipient => recipient with
            {
                HasRead = recipient.HasRead || CommittedReads.Any(read => read.UserId == recipient.UserId)
            }).ToArray());
        }

        public Task AddReadAsync(AnnouncementRead read, CancellationToken cancellationToken = default)
        {
            Visit("read", cancellationToken);
            PendingReads.Add(read);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Visit("save", cancellationToken);
            var count = PendingReads.Count;
            CommittedReads.AddRange(PendingReads);
            PendingReads.Clear();
            return Task.FromResult(count);
        }

        public Task RecordOnceAsync(Guid tenantId, Guid announcementId, Guid userId, string action, CancellationToken cancellationToken = default)
        {
            Visit("event", cancellationToken);
            Calls.Add("event:" + action);
            Events.Add((tenantId, announcementId, userId, action));
            return Task.CompletedTask;
        }

        public Task<AnnouncementEngagementAggregate> GetAggregateAsync(Guid tenantId, Guid announcementId, IReadOnlyCollection<Guid> recipientUserIds, CancellationToken cancellationToken = default)
        {
            Visit("aggregate", cancellationToken);
            AggregateRequests.Add((tenantId, announcementId, recipientUserIds.ToArray()));
            return Task.FromResult(Aggregate);
        }

        public Task<IReadOnlyList<AnnouncementDraftTargetRequest>> GetAnnouncementTargetsAsync(Guid tenantId, Guid announcementId, CancellationToken cancellationToken = default)
        {
            Visit("targets", cancellationToken);
            if (tenantId != Announcement.TenantId || announcementId != Announcement.Id) throw new InvalidOperationException("Unexpected distribution scope");
            return Task.FromResult(Targets);
        }

        private Task<bool> Manage(Guid userId, string scope, CancellationToken token)
        {
            Visit("manage", token);
            ManagementChecks.Add(scope);
            return Task.FromResult(userId == ActorId && Grants.Contains(scope));
        }

        public Task<bool> CanManageWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default) => Manage(userId, $"workspace:{workspaceId}", cancellationToken);
        public Task<bool> CanManageGroup(Guid userId, Guid groupId, CancellationToken cancellationToken = default) => Manage(userId, $"group:{groupId}", cancellationToken);
        public Task<bool> CanManageChannel(Guid userId, Guid channelId, CancellationToken cancellationToken = default) => Manage(userId, $"channel:{channelId}", cancellationToken);
        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Visit("user", cancellationToken);
            return Task.FromResult(id == ActorId ? User : null);
        }

        public Task<PagedResponse<Announcement>> ListVisibleAsync(Guid userId, bool isSystemAdmin, AnnouncementListQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasReadAsync(Guid announcementId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAsync(Announcement announcement, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountReadsAsync(Guid announcementId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> CanViewWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> CanContributeWorkspace(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> CanCreateWorkspace(Guid userId, Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> CanViewGroup(Guid userId, Guid groupId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> CanCreateGroup(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> CanViewChannel(Guid userId, Guid channelId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> CanPostToChannel(Guid userId, Guid channelId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> CanPinPost(Guid userId, Guid postId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAsync(User user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StageCreatedDraftTargetsAsync(Guid tenantId, Guid draftId, IReadOnlyList<AnnouncementDraftTargetRequest> targets, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CommitDraftSaveAsync(Guid tenantId, Guid draftId, IReadOnlyList<AnnouncementDraftTargetRequest> targets, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AnnouncementDraftTargetRequest>> GetDraftTargetsAsync(Guid tenantId, Guid draftId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CommitPublicationAsync(Guid tenantId, Guid announcementId, IReadOnlyList<AnnouncementDraftTargetRequest> targets, Func<CancellationToken, Task> stagePublication, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
