using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coglatas.Application.Announcements;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Scope = Coglatas.Tests.SecurityArchitecture.SecurityArchitectureSignalRTests.Scope;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed class SecurityArchitectureWorkerProducerTests
{
    private const string AnnouncementEvent = "Announcements.AnnouncementChanged.v1";
    private const string NotificationEvent = "Notifications.NotificationCreated.v1";
    private const string Worker = "Coglatas.Web.Notifications.AnnouncementPublisherWorker";
    private const string DigestWorker = "Coglatas.Web.Notifications.TaskDeadlineDigestWorker";

    [PostgreSqlFact]
    public async Task ActualRegisteredAnnouncementWorkerRechecksAuthorAndAudienceBeforePublication()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            // Test-only non-default cadence; registered workers, authentication,
            // publication services and dispatcher are the actual Web composition.
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database, workerPollSeconds: 1);
            using var ownerClient = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAOwnerEmail);
            using var memberClient = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
            using var betaClient = await app.LoginAsync("beta", SecurityCiFixtureSeed.TenantBSlug, SecurityCiFixtureSeed.TenantBOwnerEmail);
            var scope = await SecurityArchitectureSignalRTests.ScopeAsync(database, SecurityCiFixtureSeed.TenantASlug);
            var betaScope = await SecurityArchitectureSignalRTests.ScopeAsync(database, SecurityCiFixtureSeed.TenantBSlug);
            var controls = SecurityArchitectureSignalRControlRecorder.Create(GetType());
            var worker = SecurityArchitectureWorkerControlRecorder.Create(GetType());
            await using var owner = await app.ConnectAsync(ownerClient, "owner", SecurityCiFixtureSeed.TenantASlug);
            await using var member = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug);
            await using var beta = await app.ConnectAsync(betaClient, "beta", SecurityCiFixtureSeed.TenantBSlug);
            controls.ObserveInvocation("SubscribeUser", await member.InvokeAsync("SubscribeUser"), true, "Subscribed");
            Assert.True(await owner.SubscribeAsync("SubscribeUser"));
            Assert.True(await beta.SubscribeAsync("SubscribeUser"));

            var betaDraft = await CreateDraftAsync(betaClient, betaScope.Workspace);
            await PublishAsync(betaClient, betaDraft);
            var betaAnnouncement = await WaitPublishedAsync(database, betaDraft.Id);
            await ObservePublicationAsync(database, app, null, betaAnnouncement, SecurityCiFixtureSeed.TenantBOwnerUserId, beta, null);

            var draft = await CreateDraftAsync(ownerClient, scope.Workspace);
            await PublishAsync(ownerClient, draft);
            var announcement = await WaitPublishedAsync(database, draft.Id);
            var original = await ObservePublicationAsync(database, app, controls, announcement,
                SecurityCiFixtureSeed.TenantAMemberUserId, member, "WORKER_ANNOUNCEMENT_ORIGINAL_PRODUCER", beta);
            await owner.WaitEventAsync(original.AnnouncementEvent);
            controls.ObserveIsolation(NotificationEvent, RealtimeSubscriptionType.User,
                "WORKER_ANNOUNCEMENT_NOTIFICATION_RECIPIENT", member, original.NotificationEvent, owner, original.NotificationEvent);
            worker.Observe(Worker, "ACTUAL_PUBLICATION", "PUBLISHED_WITH_AUDIT_FROZEN_COHORT_AND_TYPED_FRAMES");

            var denied = await CreateDraftAsync(ownerClient, scope.Workspace);
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                var workspace = await db.Workspaces.SingleAsync(item => item.Id == scope.Workspace);
                workspace.TimeZone = "UTC";
                await db.SaveChangesAsync();
            }
            // Accept a real future schedule while the author is authorized.
            // Revoke before its due instant instead of inserting a queued row.
            using (var request = Idempotent(HttpMethod.Post, $"/api/announcement-drafts/{denied.Id:D}/schedule",
                       new ScheduleAnnouncementDraftRequest(denied.Version,
                           DateTime.SpecifyKind(DateTime.UtcNow.AddSeconds(5), DateTimeKind.Unspecified), "UTC")))
            using (var response = await ownerClient.SendAsync(request))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var before = await PublicationStateAsync(database);
            await SetWorkspaceMemberAsync(database, scope, SecurityCiFixtureSeed.TenantAOwnerUserId,
                WorkspaceRole.Member, MembershipStatus.Active);
            await WaitDeferredAsync(database, denied.Id, denied.Version + 1);
            var after = await PublicationStateAsync(database);
            Assert.Equal(before.Announcements, after.Announcements);
            Assert.Equal(before.Notifications, after.Notifications);
            Assert.Equal(before.Outbox, after.Outbox);
            Assert.Equal(before.FrozenCohorts, after.FrozenCohorts);
            Assert.Equal(before.Audit + 1, after.Audit);
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                Assert.Equal(1, await db.AuditLogs.CountAsync(item => item.Action == "AnnouncementPublicationDeferred" && item.EntityId == denied.Id));
            }
            worker.Observe(Worker, "CURRENT_AUTHOR_DENIED", "DEFERRED_DRAFT_WITH_NO_ANNOUNCEMENT_NOTIFICATION_COHORT_OR_OUTBOX_AND_ONE_DEFER_AUDIT");

            await SetWorkspaceMemberAsync(database, scope, SecurityCiFixtureSeed.TenantAOwnerUserId,
                WorkspaceRole.Owner, MembershipStatus.Active);
            var audienceDraft = await CreateDraftAsync(ownerClient, scope.Workspace);
            // Suspended audience is recomputed by the actual worker at due time.
            await SetWorkspaceMemberAsync(database, scope, SecurityCiFixtureSeed.TenantAMemberUserId,
                WorkspaceRole.Member, MembershipStatus.Suspended);
            await PublishAsync(ownerClient, audienceDraft);
            var audienceAnnouncement = await WaitPublishedAsync(database, audienceDraft.Id);
            var current = await ObservePublicationAsync(database, app, controls, audienceAnnouncement,
                SecurityCiFixtureSeed.TenantAOwnerUserId, owner, "WORKER_ANNOUNCEMENT_CURRENT_AUDIENCE", member);
            Assert.False(member.Received(current.AnnouncementEvent));
            Assert.False(member.Received(current.NotificationEvent));
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                Assert.Equal(0, await db.Notifications.CountAsync(item => item.RelatedEntityId == audienceAnnouncement && item.UserId == SecurityCiFixtureSeed.TenantAMemberUserId));
            worker.Observe(Worker, "CURRENT_AUDIENCE_RECOMPUTED", "CURRENT_OWNER_FRAMES_WITH_NO_SUSPENDED_MEMBER_NOTIFICATION_OR_DELIVERY");

            await SetWorkspaceMemberAsync(database, scope, SecurityCiFixtureSeed.TenantAMemberUserId,
                WorkspaceRole.Member, MembershipStatus.Active);
            var restoredDraft = await CreateDraftAsync(ownerClient, scope.Workspace);
            await PublishAsync(ownerClient, restoredDraft);
            var restoredAnnouncement = await WaitPublishedAsync(database, restoredDraft.Id);
            await ObservePublicationAsync(database, app, controls, restoredAnnouncement,
                SecurityCiFixtureSeed.TenantAMemberUserId, member, "WORKER_ANNOUNCEMENT_RESTORED_AUDIENCE");
            worker.Observe(Worker, "RESTORED_AUDIENCE_PUBLICATION", "FRESH_PUBLISHED_ANNOUNCEMENT_AND_NOTIFICATION_TYPED_FRAMES");
            // The denied draft remains deferred; restoration did not silently
            // rewrite its lease, version, retry cadence or accepted due instant.
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                Assert.Null((await db.AnnouncementDrafts.AsNoTracking().SingleAsync(item => item.Id == denied.Id)).PublishedAnnouncementId);
            await controls.SaveAsync();
            await worker.SaveAsync();
        });
    }

    [PostgreSqlFact]
    public async Task ActualRegisteredDeadlineWorkerUsesOptInAndCurrentWorkspaceRecipientAuthority()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database, workerPollSeconds: 1);
            using var ownerClient = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAOwnerEmail);
            using var memberClient = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
            using var betaClient = await app.LoginAsync("beta", SecurityCiFixtureSeed.TenantBSlug, SecurityCiFixtureSeed.TenantBOwnerEmail);
            var scope = await SecurityArchitectureSignalRTests.ScopeAsync(database, SecurityCiFixtureSeed.TenantASlug);
            var betaScope = await SecurityArchitectureSignalRTests.ScopeAsync(database, SecurityCiFixtureSeed.TenantBSlug);
            var controls = SecurityArchitectureSignalRControlRecorder.Create(GetType());
            var worker = SecurityArchitectureWorkerControlRecorder.Create(GetType());
            await using var owner = await app.ConnectAsync(ownerClient, "owner", SecurityCiFixtureSeed.TenantASlug);
            await using var member = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug);
            await using var beta = await app.ConnectAsync(betaClient, "beta", SecurityCiFixtureSeed.TenantBSlug);
            controls.ObserveInvocation("SubscribeUser", await member.InvokeAsync("SubscribeUser"), true, "Subscribed");
            Assert.True(await owner.SubscribeAsync("SubscribeUser"));
            Assert.True(await beta.SubscribeAsync("SubscribeUser"));
            await PrepareDeadlineAsync(database, betaScope);
            await OptInAsync(database, betaScope);
            var betaJob = await WaitDigestAsync(database, betaScope, SecurityCiFixtureSeed.TenantBOwnerUserId);
            var betaEvent = await NotificationEventAsync(database, betaJob.NotificationId!.Value);
            await beta.WaitEventAsync(betaEvent);
            Assert.True(beta.Received(betaEvent, NotificationEvent));
            await app.WaitDeliveredAsync(betaEvent);

            Guid task;
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                task = await db.TaskItems.AsNoTracking().Where(item => item.ProjectId == scope.Project).Select(item => item.Id).SingleAsync();
            // Use the existing actual authorized watch API. No new role or
            // Capability Grant is invented to manufacture digest eligibility.
            using (var watch = await memberClient.PutAsJsonAsync($"/api/tasks/{task:D}/watch", new { expectedVersion = 0 }))
                Assert.Equal(HttpStatusCode.OK, watch.StatusCode);
            await PrepareDeadlineAsync(database, scope);
            await SetWorkspaceMemberAsync(database, scope, SecurityCiFixtureSeed.TenantAMemberUserId,
                WorkspaceRole.Member, MembershipStatus.Suspended);
            await OptInAsync(database, scope);
            var ownerJob = await WaitDigestAsync(database, scope, SecurityCiFixtureSeed.TenantAOwnerUserId);
            var ownerEvent = await NotificationEventAsync(database, ownerJob.NotificationId!.Value);
            await owner.WaitEventAsync(ownerEvent);
            await app.WaitDeliveredAsync(ownerEvent);
            controls.ObservePositive(NotificationEvent, RealtimeSubscriptionType.User, "WORKER_DIGEST_ORIGINAL_PRODUCER", owner, ownerEvent);
            controls.ObserveIsolation(NotificationEvent, RealtimeSubscriptionType.User, "WORKER_DIGEST_CROSS_TENANT", owner, ownerEvent, beta, ownerEvent);
            worker.Observe(DigestWorker, "ACTUAL_DIGEST_PUBLICATION", "SUCCEEDED_JOB_FENCED_ATTEMPT_NOTIFICATION_OUTBOX_AND_TYPED_FRAME");
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                Assert.Equal(0, await db.TaskDeadlineDigestJobs.CountAsync(item => item.WorkspaceId == scope.Workspace && item.UserId == SecurityCiFixtureSeed.TenantAMemberUserId));
                Assert.Equal(0, await db.Notifications.CountAsync(item => item.UserId == SecurityCiFixtureSeed.TenantAMemberUserId && item.RelatedEntityType == "TaskDeadlineDigest"));
            }
            worker.Observe(DigestWorker, "CURRENT_WORKSPACE_MEMBER_EXCLUDED", "LIVE_OWNER_DIGEST_WITH_NO_SUSPENDED_MEMBER_JOB_OR_NOTIFICATION");
            await SetWorkspaceMemberAsync(database, scope, SecurityCiFixtureSeed.TenantAMemberUserId,
                WorkspaceRole.Member, MembershipStatus.Active);
            var memberJob = await WaitDigestAsync(database, scope, SecurityCiFixtureSeed.TenantAMemberUserId);
            var memberEvent = await NotificationEventAsync(database, memberJob.NotificationId!.Value);
            await member.WaitEventAsync(memberEvent);
            await app.WaitDeliveredAsync(memberEvent);
            controls.ObservePositive(NotificationEvent, RealtimeSubscriptionType.User, "WORKER_DIGEST_RESTORED_WORKSPACE_MEMBER", member, memberEvent);
            worker.Observe(DigestWorker, "RESTORED_WORKSPACE_DIGEST_PUBLICATION", "FRESH_CURRENT_MEMBER_SUCCEEDED_JOB_AND_TYPED_NOTIFICATION_FRAME");
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                Assert.Equal(1, await db.Notifications.CountAsync(item => item.UserId == SecurityCiFixtureSeed.TenantAOwnerUserId && item.RelatedEntityType == "TaskDeadlineDigest"));
            await controls.SaveAsync();
            await worker.SaveAsync();
        });
    }

    private static HttpRequestMessage Idempotent<T>(HttpMethod method, string path, T body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return request;
    }

    private static async Task<AnnouncementDraftResponse> CreateDraftAsync(HttpClient client, Guid workspace)
    {
        using var request = Idempotent(HttpMethod.Post, "/api/announcement-drafts", new CreateAnnouncementDraftRequest(
            new AnnouncementDraftContentRequest(new(workspace, null, null), "Synthetic worker publication", "Synthetic worker content.")));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var draft = await response.Content.ReadFromJsonAsync<AnnouncementDraftResponse>();
        Assert.NotNull(draft);
        Assert.NotEqual(Guid.Empty, draft.Id);
        return draft;
    }

    private static async Task PublishAsync(HttpClient client, AnnouncementDraftResponse draft)
    {
        using var request = Idempotent(HttpMethod.Post, $"/api/announcement-drafts/{draft.Id:D}/publish", new PublishAnnouncementDraftRequest(draft.Version));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<Guid> WaitPublishedAsync(string database, Guid draft)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            var state = await db.AnnouncementDrafts.AsNoTracking().SingleAsync(item => item.Id == draft);
            if (state.Status == AnnouncementDraftStatus.Published)
            {
                Assert.NotNull(state.PublishedAnnouncementId);
                Assert.NotNull(state.PublishedAtUtc);
                Assert.Null(state.PublicationClaimToken);
                Assert.Null(state.NextPublicationAttemptAtUtc);
                Assert.Null(state.LastPublicationFailureCode);
                Assert.Equal(1, await db.AuditLogs.CountAsync(item => item.Action == "AnnouncementPublished" && item.EntityId == state.PublishedAnnouncementId));
                return state.PublishedAnnouncementId.Value;
            }
            await Task.Delay(50);
        }
        Assert.Fail("Actual registered worker did not publish the authorized synthetic draft.");
        return Guid.Empty;
    }

    private static async Task WaitDeferredAsync(string database, Guid draft, long scheduledVersion)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            var state = await db.AnnouncementDrafts.AsNoTracking().SingleAsync(item => item.Id == draft);
            if (state.LastPublicationFailureCode is not null)
            {
                Assert.Equal("DraftValidationFailed", state.LastPublicationFailureCode);
                Assert.Equal(AnnouncementDraftStatus.Scheduled, state.Status);
                // The real claim lease and deferral each advance the token.
                Assert.Equal(scheduledVersion + 2, state.VersionNo);
                Assert.Equal(1, state.PublicationAttemptCount);
                Assert.Null(state.PublishedAnnouncementId);
                Assert.Null(state.PublishedAtUtc);
                Assert.Null(state.PublicationClaimToken);
                Assert.True(state.NextPublicationAttemptAtUtc > state.ScheduledForUtc);
                return;
            }
            await Task.Delay(50);
        }
        Assert.Fail("Actual worker did not record the expected current-authority deferral.");
    }

    private sealed record PublicationState(int Announcements, int Notifications, int Outbox, int FrozenCohorts, int Audit);

    private static async Task<PublicationState> PublicationStateAsync(string database)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        return new(await db.Announcements.CountAsync(), await db.Notifications.CountAsync(), await db.OutboxEvents.CountAsync(),
            await db.AuditLogs.CountAsync(item => item.Action == AnnouncementDistributionContract.FrozenCohortAuditAction), await db.AuditLogs.CountAsync());
    }

    private static async Task<(Guid AnnouncementEvent, Guid NotificationEvent)> ObservePublicationAsync(string database,
        SecurityArchitectureSignalRFixture app, SecurityArchitectureSignalRControlRecorder? controls,
        Guid announcement, Guid user, RealtimeSocket recipient, string? control, RealtimeSocket? excluded = null)
    {
        Guid notification;
        Guid announcementEvent;
        await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
        {
            notification = await db.Notifications.AsNoTracking().Where(item => item.RelatedEntityId == announcement && item.UserId == user).Select(item => item.Id).SingleAsync();
            announcementEvent = await db.OutboxEvents.AsNoTracking().Where(item => item.AggregateId == announcement && item.EventType == AnnouncementEvent).Select(item => item.Id).SingleAsync();
            Assert.Equal(1, await db.AuditLogs.CountAsync(item => item.EntityId == announcement && item.Action == AnnouncementDistributionContract.FrozenCohortAuditAction));
        }
        var notificationEvent = await NotificationEventAsync(database, notification);
        foreach (var (type, id) in new[] { (AnnouncementEvent, announcementEvent), (NotificationEvent, notificationEvent) })
        {
            await recipient.WaitEventAsync(id);
            await app.WaitDeliveredAsync(id);
            Assert.True(recipient.Received(id, type));
            if (controls is null) continue;
            if (control == "WORKER_ANNOUNCEMENT_CURRENT_AUDIENCE")
                controls.ObserveIsolation(type, RealtimeSubscriptionType.User, control, recipient, id, excluded!, id);
            else
            {
                controls.ObservePositive(type, RealtimeSubscriptionType.User, control!, recipient, id);
                if (excluded is not null)
                    controls.ObserveIsolation(type, RealtimeSubscriptionType.User, "WORKER_ANNOUNCEMENT_CROSS_TENANT", recipient, id, excluded, id);
            }
        }
        return (announcementEvent, notificationEvent);
    }

    private static async Task<Guid> NotificationEventAsync(string database, Guid notification)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        return await db.OutboxEvents.AsNoTracking().Where(item => item.AggregateId == notification && item.EventType == NotificationEvent).Select(item => item.Id).SingleAsync();
    }

    private static async Task SetWorkspaceMemberAsync(string database, Scope scope, Guid user, WorkspaceRole role, MembershipStatus status)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var member = await db.WorkspaceMembers.SingleAsync(item => item.WorkspaceId == scope.Workspace && item.UserId == user);
        member.Role = role;
        member.Status = status;
        await db.SaveChangesAsync();
    }

    private static async Task PrepareDeadlineAsync(string database, Scope scope)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var workspace = await db.Workspaces.SingleAsync(item => item.Id == scope.Workspace);
        workspace.TimeZone = "UTC";
        workspace.DefaultTaskDeadlineDigestLocalTime = TimeOnly.MinValue;
        (await db.TaskItems.SingleAsync(item => item.ProjectId == scope.Project)).DeadlineAt = DateTimeOffset.UtcNow.AddDays(1);
        await db.SaveChangesAsync();
    }

    private static async Task OptInAsync(string database, Scope scope)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var settings = await db.TenantSettings.SingleOrDefaultAsync(item => item.TenantId == scope.Tenant);
        if (settings is null)
        {
            settings = new TenantSettings { TenantId = scope.Tenant };
            db.TenantSettings.Add(settings);
        }
        // Deliberate disposable-tenant opt-in through the existing flag store.
        // No product activation, deployed configuration or authority is changed.
        var flags = JsonSerializer.Deserialize<Dictionary<string, bool>>(settings.FeatureFlagsJson)!;
        Assert.False(flags.GetValueOrDefault("tasks.notificationsV1"));
        flags["tasks.notificationsV1"] = true;
        settings.FeatureFlagsJson = JsonSerializer.Serialize(flags);
        await db.SaveChangesAsync();
    }

    private sealed record DigestState(Guid? NotificationId);

    private static async Task<DigestState> WaitDigestAsync(string database, Scope scope, Guid user)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            var job = await db.TaskDeadlineDigestJobs.AsNoTracking().SingleOrDefaultAsync(item => item.WorkspaceId == scope.Workspace && item.UserId == user);
            if (job?.Status == TaskDeadlineDigestJobStatus.Succeeded)
            {
                Assert.NotNull(job.NotificationId);
                Assert.NotNull(job.CompletedAt);
                Assert.Null(job.ClaimToken);
                Assert.Equal(1, job.AutomaticAttemptCount);
                Assert.Equal(1, await db.TaskDeadlineDigestAttempts.CountAsync(item => item.JobId == job.Id && item.Status == TaskDeadlineDigestAttemptStatus.Succeeded));
                return new(job.NotificationId);
            }
            await Task.Delay(50);
        }
        Assert.Fail("Actual registered worker did not produce the eligible synthetic recipient digest.");
        return new(null);
    }
}
