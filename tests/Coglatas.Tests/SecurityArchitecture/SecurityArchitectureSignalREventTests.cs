using System.Text.Json;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Scope = Coglatas.Tests.SecurityArchitecture.SecurityArchitectureSignalRTests.Scope;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed class SecurityArchitectureSignalREventTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [PostgreSqlFact]
    public async Task EveryDeclaredEventHasLiveTenantAndCurrentMembershipControls()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var memberClient = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
            using var ownerClient = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAOwnerEmail);
            using var betaClient = await app.LoginAsync("beta", SecurityCiFixtureSeed.TenantBSlug, SecurityCiFixtureSeed.TenantBOwnerEmail);
            await using var member = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug);
            await using var owner = await app.ConnectAsync(ownerClient, "owner", SecurityCiFixtureSeed.TenantASlug);
            await using var beta = await app.ConnectAsync(betaClient, "beta", SecurityCiFixtureSeed.TenantBSlug);
            var alphaScope = await SecurityArchitectureSignalRTests.ScopeAsync(database, SecurityCiFixtureSeed.TenantASlug);
            var betaScope = await SecurityArchitectureSignalRTests.ScopeAsync(database, SecurityCiFixtureSeed.TenantBSlug);
            var controls = SecurityArchitectureSignalRControlRecorder.Create(GetType());
            await SubscribeAllAsync(member, alphaScope, controls);
            await SubscribeAllAsync(owner, alphaScope);
            await SubscribeAllAsync(beta, betaScope);
            var observations = new List<object>();
            var events = RealtimeEventCatalog.EventTypes.Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(15, events.Length);
            foreach (var route in CatalogueRoutes())
            {
                var eventType = route.EventType;
                var positive = await EnqueueAsync(database, alphaScope, SecurityCiFixtureSeed.TenantAMemberUserId, eventType, targetOverride: route.Override);
                await member.WaitEventAsync(positive);
                await app.WaitDeliveredAsync(positive);
                var foreign = await EnqueueAsync(database, betaScope, SecurityCiFixtureSeed.TenantBOwnerUserId, eventType, targetOverride: route.Override);
                await beta.WaitEventAsync(foreign);
                await app.WaitDeliveredAsync(foreign);
                Assert.False(member.Received(foreign));
                Assert.False(beta.Received(positive));
                controls.ObserveIsolation(eventType, route.Target, "CROSS_TENANT", beta, foreign, member, foreign);
                observations.Add(new { eventType, subscriptionType = route.Target.ToString(), control = "LIVE_POSITIVE_AND_CROSS_TENANT_NON_DELIVERY", runtimeOutcome = "PASS" });
                if (route.Target == RealtimeSubscriptionType.User)
                {
                    // User routes are recipient-specific even when both users have
                    // the same Tenant and all resource subscriptions.
                    Assert.True(await member.SubscribeAsync("SubscribeUser"));
                    Assert.True(await owner.SubscribeAsync("SubscribeUser"));
                    var ownerPositive = await EnqueueAsync(database, alphaScope, SecurityCiFixtureSeed.TenantAOwnerUserId, eventType, targetOverride: route.Override);
                    await owner.WaitEventAsync(ownerPositive);
                    await app.WaitDeliveredAsync(ownerPositive);
                    controls.ObserveIsolation(eventType, RealtimeSubscriptionType.User, "SAME_TENANT_RECIPIENT", owner, ownerPositive, member, ownerPositive);
                    Assert.False(owner.Received(positive));
                }
            }
            // The metadata-only invalidation event deliberately removes subscriptions.
            // Restore legitimate subscriptions before checking other current-state controls.
            await SubscribeAllAsync(member, alphaScope);
            await SubscribeAllAsync(owner, alphaScope);
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                var membership = await db.TenantUsers.SingleAsync(item => item.TenantId == alphaScope.Tenant &&
                    item.UserId == SecurityCiFixtureSeed.TenantAMemberUserId);
                membership.Status = TenantUserStatus.Suspended;
                await db.SaveChangesAsync();
            }
            var replayed = new List<(EventRoute Route, Guid Denied)>();
            foreach (var route in CatalogueRoutes().Where(item => item.EventType != "Security.AuthorizationStateChanged.v1"))
            {
                var eventType = route.EventType;
                var denied = await EnqueueAsync(database, alphaScope, SecurityCiFixtureSeed.TenantAMemberUserId, eventType, targetOverride: route.Override);
                await app.WaitDeliveredAsync(denied);
                var positive = await EnqueueAsync(database, alphaScope, SecurityCiFixtureSeed.TenantAOwnerUserId, eventType, targetOverride: route.Override);
                await owner.WaitEventAsync(positive);
                await app.WaitDeliveredAsync(positive);
                Assert.False(member.Received(denied));
                controls.ObserveIsolation(eventType, route.Target, "CURRENT_TENANT_MEMBERSHIP", owner, positive, member, denied);
                observations.Add(new { eventType, control = "COMMITTED_TENANT_MEMBERSHIP_REVOCATION_WITH_LIVE_PEER", runtimeOutcome = "PASS" });
                await ReplayAsync(database, app, denied);
                if (route.Target == RealtimeSubscriptionType.User)
                {
                    await ReplayAsync(database, app, positive);
                    await owner.WaitEventAsync(positive, minimumCount: 2);
                }
                else await owner.WaitEventAsync(denied, minimumCount: 2);
                controls.ObserveIsolation(eventType, route.Target, "REPOSITORY_REPLAY_CURRENT_TENANT_MEMBERSHIP", owner,
                    route.Target == RealtimeSubscriptionType.User ? positive : denied, member, denied);
                replayed.Add((route, denied));
            }
            // Invalidation has no protected payload and may reach an invalidated
            // recipient. Its distinct contract still requires exact recipient identity.
            var forged = await EnqueueAsync(database, alphaScope, SecurityCiFixtureSeed.TenantAOwnerUserId,
                "Security.AuthorizationStateChanged.v1", forgedAffectedUser: SecurityCiFixtureSeed.TenantAMemberUserId);
            await app.WaitDeliveredAsync(forged);
            Assert.False(owner.Received(forged));
            Assert.True(await owner.SubscribeAsync("SubscribeUser"));
            var final = await EnqueueAsync(database, alphaScope, SecurityCiFixtureSeed.TenantAOwnerUserId,
                "Security.AuthorizationStateChanged.v1");
            await owner.WaitEventAsync(final);
            await app.WaitDeliveredAsync(final);
            observations.Add(new { eventType = "Security.AuthorizationStateChanged.v1",
                control = "RECIPIENT_MISMATCH_WITH_VALID_INVALIDATION_DELIVERY", runtimeOutcome = "PASS" });
            controls.ObserveIsolation("Security.AuthorizationStateChanged.v1", RealtimeSubscriptionType.User,
                "INVALIDATION_RECIPIENT_MISMATCH", owner, final, owner, forged);
            Assert.True(await owner.SubscribeAsync("SubscribeUser"));
            await ReplayAsync(database, app, forged);
            Assert.True(await owner.SubscribeAsync("SubscribeUser"));
            await ReplayAsync(database, app, final);
            await owner.WaitEventAsync(final, minimumCount: 2);
            controls.ObserveIsolation("Security.AuthorizationStateChanged.v1", RealtimeSubscriptionType.User,
                "REPOSITORY_REPLAY_INVALIDATION_RECIPIENT_MISMATCH", owner, final, owner, forged);
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                var membership = await db.TenantUsers.SingleAsync(item => item.TenantId == alphaScope.Tenant &&
                    item.UserId == SecurityCiFixtureSeed.TenantAMemberUserId);
                membership.Status = TenantUserStatus.Active;
                await db.SaveChangesAsync();
            }
            await SubscribeAllAsync(member, alphaScope);
            await SubscribeAllAsync(owner, alphaScope);
            foreach (var (route, denied) in replayed)
            {
                await ReplayAsync(database, app, denied);
                await member.WaitEventAsync(denied);
                controls.ObservePositive(route.EventType, route.Target, "REPOSITORY_REPLAY_RESTORED_TENANT_MEMBERSHIP", member, denied);
            }
            await controls.SaveAsync();
            await SecurityArchitectureInventoryTests.WritePrivateInventoryAsync("signalr-event-execution.json", new
            {
                schemaVersion = 1, eventTypeCount = events.Length, observations,
                fixture = "SYNTHETIC_DURABLE_ENVELOPES_THROUGH_PRODUCT_POSTGRESQL_OUTBOX_AND_WEBSOCKET_DISPATCH",
                businessProducerOutcome = "UNVERIFIED", specMappingOutcome = "UNVERIFIED", approval = "DRAFT",
                blindSpots = new[] { "Catalogue delivery controls do not execute every business producer or payload/role/capability combination.",
                    "Workflow catalogue inclusion does not establish an active business publisher.",
                    "Persisted target references are synthetic; no operational deployment or product RLS is qualified." }
            });
        });
    }

    internal static async Task SubscribeAllAsync(RealtimeSocket socket, Scope scope, SecurityArchitectureSignalRControlRecorder? controls = null)
    {
        foreach (var (method, arguments) in new (string Method, object[] Arguments)[]
        {
            ("SubscribeUser", []), ("SubscribeConversation", [scope.Conversation]),
            ("SubscribeWorkspace", [scope.Workspace]), ("SubscribeProject", [scope.Project])
        })
        {
            var result = await socket.InvokeAsync(method, arguments);
            Assert.True(result.GetProperty("allowed").GetBoolean());
            Assert.Equal("Subscribed", result.GetProperty("code").GetString());
            controls?.ObserveInvocation(method, result, expectedAllowed: true, "Subscribed");
        }
    }

    internal static async Task InvokeWithReceiptAsync(RealtimeSocket socket, SecurityArchitectureSignalRControlRecorder controls,
        string method, bool allowed, string code, params object[] arguments) =>
        controls.ObserveInvocation(method, await socket.InvokeAsync(method, arguments), allowed, code);

    [PostgreSqlFact]
    public async Task ProjectAndWorkspaceUnsubscriptionOnlyRemovesCallingConnection()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var memberClient = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
            using var ownerClient = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAOwnerEmail);
            await using var member = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug);
            await using var owner = await app.ConnectAsync(ownerClient, "owner", SecurityCiFixtureSeed.TenantASlug);
            var scope = await SecurityArchitectureSignalRTests.ScopeAsync(database, SecurityCiFixtureSeed.TenantASlug);
            var controls = SecurityArchitectureSignalRControlRecorder.Create(GetType());
            var targets = new[] { RealtimeSubscriptionType.Project, RealtimeSubscriptionType.Workspace, RealtimeSubscriptionType.Conversation };
            await SubscribeAllAsync(member, scope, controls);
            await SubscribeAllAsync(owner, scope);
            foreach (var target in targets)
            {
                var initial = await EnqueueAsync(database, scope, SecurityCiFixtureSeed.TenantAMemberUserId,
                    EventFor(target), targetOverride: target);
                await member.WaitEventAsync(initial);
                await owner.WaitEventAsync(initial);
                await app.WaitDeliveredAsync(initial);
            }
            foreach (var target in targets)
            {
                await InvokeWithReceiptAsync(member, controls, "Unsubscribe" + target, true, "Unsubscribed", ResourceFor(scope, target));
                await InvokeWithReceiptAsync(member, controls, "Unsubscribe" + target, true, "NotSubscribed", ResourceFor(scope, target));
            }
            foreach (var target in targets)
            {
                var denied = await EnqueueAsync(database, scope, SecurityCiFixtureSeed.TenantAMemberUserId,
                    EventFor(target), targetOverride: target);
                await owner.WaitEventAsync(denied);
                await app.WaitDeliveredAsync(denied);
                controls.ObserveIsolation(EventFor(target), target, "CALLING_CONNECTION_UNSUBSCRIBED", owner, denied, member, denied);
            }
            Assert.True(await member.SubscribeAsync("SubscribeUser"));
            Assert.True(await member.SubscribeAsync("SubscribeProject", scope.Project));
            Assert.True(await member.SubscribeAsync("SubscribeWorkspace", scope.Workspace));
            Assert.True(await member.SubscribeAsync("SubscribeConversation", scope.Conversation));
            foreach (var target in targets)
            {
                var final = await EnqueueAsync(database, scope, SecurityCiFixtureSeed.TenantAMemberUserId,
                    EventFor(target), targetOverride: target);
                await member.WaitEventAsync(final);
                await owner.WaitEventAsync(final);
                await app.WaitDeliveredAsync(final);
            }
            await controls.SaveAsync();
        });
    }

    internal static async Task<Guid> EnqueueAsync(string database, Scope scope, Guid recipient, string eventType,
        Guid? forgedAffectedUser = null, RealtimeSubscriptionType? targetOverride = null)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var taskId = eventType.StartsWith("Projects.Task", StringComparison.Ordinal) ||
                     eventType.StartsWith("Notifications.", StringComparison.Ordinal)
            ? await db.TaskItems.AsNoTracking().Where(item => item.ProjectId == scope.Project)
                .Select(item => item.Id).SingleAsync()
            : Guid.Empty;
        var aggregateId = Guid.NewGuid();
        var aggregateType = "Message";
        var targetType = RealtimeSubscriptionType.Conversation;
        var targetId = scope.Conversation;
        object payload = new { synthetic = true };
        switch (eventType)
        {
            case "Messaging.MessageCreated.v1":
            case "Messaging.MessageUpdated.v1":
            case "Messaging.MessageDeleted.v1":
            case "Messaging.ThreadChanged.v1":
                break;
            case "Messaging.ConversationUnreadChanged.v1":
                aggregateType = "ConversationReadState";
                targetType = RealtimeSubscriptionType.User;
                targetId = recipient;
                payload = new { conversationId = scope.Conversation, unreadCount = 1 };
                break;
            case "Projects.TaskChanged.v1":
            case "Projects.TaskAssignmentChanged.v1":
            case "Projects.TaskWorkflowChanged.v1":
            case "Projects.TaskCommentChanged.v1":
                aggregateType = "Task";
                aggregateId = taskId;
                targetType = RealtimeSubscriptionType.Project;
                targetId = scope.Project;
                payload = new { taskId, projectId = scope.Project, taskVersion = 1, requiresRefetch = true };
                break;
            case "Projects.ProjectChanged.v1":
                aggregateType = "Project";
                aggregateId = scope.Project;
                targetType = RealtimeSubscriptionType.Project;
                targetId = scope.Project;
                payload = new { projectId = scope.Project, workspaceId = scope.Workspace, projectVersion = 1, requiresRefetch = true };
                break;
            case "Files.FileChanged.v1":
                aggregateType = "File";
                aggregateId = await db.Attachments.AsNoTracking().Where(item => item.WorkspaceId == scope.Workspace)
                    .Select(item => item.FileObjectId).SingleAsync();
                targetType = RealtimeSubscriptionType.Workspace;
                targetId = scope.Workspace;
                payload = new { fileId = aggregateId, workspaceId = scope.Workspace, fileVersion = 1, requiresRefetch = true };
                break;
            case "Announcements.AnnouncementChanged.v1":
                aggregateType = "Announcement";
                targetType = RealtimeSubscriptionType.User;
                targetId = recipient;
                payload = new { announcementId = aggregateId, requiresRefetch = true };
                break;
            case "Notifications.NotificationCreated.v1":
            case "Notifications.NotificationReadStateChanged.v1":
                aggregateType = "Notification";
                targetType = RealtimeSubscriptionType.User;
                targetId = recipient;
                var notification = new Notification { TenantId = scope.Tenant, UserId = recipient,
                    Title = "Synthetic catalogue target", RelatedEntityType = "TaskItem", RelatedEntityId = taskId,
                    StateVersion = 1, CreatedAt = DateTimeOffset.UtcNow, IsRead = eventType.Contains("ReadState", StringComparison.Ordinal) };
                aggregateId = notification.Id;
                db.Notifications.Add(notification);
                payload = eventType == "Notifications.NotificationCreated.v1"
                    ? new { notificationId = aggregateId, stateVersion = 1, requiresRefetch = true }
                    : new { notificationId = aggregateId, change = "read", stateVersion = 1 };
                break;
            case "Security.AuthorizationStateChanged.v1":
                aggregateType = "User";
                aggregateId = recipient;
                targetType = RealtimeSubscriptionType.User;
                targetId = recipient;
                payload = new { affectedUserId = forgedAffectedUser ?? recipient, requiresRefetch = true };
                break;
            default:
                throw new InvalidOperationException("Declared event is missing its executable fixture.");
        }
        if (targetOverride.HasValue)
        {
            if (targetOverride == RealtimeSubscriptionType.User)
                Assert.Equal("Projects.TaskChanged.v1", eventType);
            else
            {
                Assert.Equal(EventFor(targetOverride.Value), eventType);
                Assert.Contains(targetOverride.Value, new[] { RealtimeSubscriptionType.Project, RealtimeSubscriptionType.Workspace, RealtimeSubscriptionType.Conversation });
            }
            targetType = targetOverride.Value;
            targetId = targetType switch { RealtimeSubscriptionType.Project => scope.Project,
                RealtimeSubscriptionType.Workspace => scope.Workspace, RealtimeSubscriptionType.User => recipient, _ => scope.Conversation };
        }
        var id = Guid.NewGuid();
        var envelope = new DurableEventEnvelope(id, eventType, 1, DateTimeOffset.UtcNow, scope.Tenant,
            aggregateType, aggregateId, 1, RealtimeActor.System(), null, null, JsonSerializer.SerializeToElement(payload));
        db.OutboxEvents.Add(new OutboxEvent(id) { TenantId = scope.Tenant, EventType = eventType, PayloadSchemaVersion = 1,
            AggregateType = aggregateType, AggregateId = aggregateId, AggregateVersion = 1, OccurredAt = envelope.OccurredAt,
            PayloadJson = JsonSerializer.Serialize(envelope, JsonOptions),
            RoutingJson = JsonSerializer.Serialize(new[] { new RealtimeRoutingTarget(targetType, targetId) }, JsonOptions) });
        await db.SaveChangesAsync();
        return id;
    }

    [PostgreSqlFact]
    public async Task SameTenantHiddenResourcesRejectSubscriptionAndDeliveryWithLivePeers()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var memberClient = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
            using var ownerClient = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAOwnerEmail);
            await using var member = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug);
            await using var owner = await app.ConnectAsync(ownerClient, "owner", SecurityCiFixtureSeed.TenantASlug);
            var visible = await SecurityArchitectureSignalRTests.ScopeAsync(database, SecurityCiFixtureSeed.TenantASlug);
            var hidden = await HiddenScopeAsync(database, visible.Tenant);
            var controls = SecurityArchitectureSignalRControlRecorder.Create(GetType());
            await SubscribeAllAsync(member, visible, controls);
            await SubscribeAllAsync(owner, hidden);
            foreach (var target in new[] { RealtimeSubscriptionType.Workspace, RealtimeSubscriptionType.Project, RealtimeSubscriptionType.Conversation })
            {
                var result = await member.InvokeAsync("Subscribe" + target, ResourceFor(hidden, target));
                Assert.False(result.GetProperty("allowed").GetBoolean());
                Assert.Equal("AccessDenied", result.GetProperty("code").GetString());
                Assert.DoesNotContain(result.EnumerateObject(), property => property.Name is not ("allowed" or "code"));
                controls.ObserveInvocation("Subscribe" + target, result, expectedAllowed: false, "AccessDenied");
                var denied = await EnqueueAsync(database, hidden, SecurityCiFixtureSeed.TenantAOwnerUserId,
                    EventFor(target), targetOverride: target);
                await owner.WaitEventAsync(denied);
                await app.WaitDeliveredAsync(denied);
                controls.ObserveIsolation(EventFor(target), target, "SAME_TENANT_HIDDEN_RESOURCE", owner, denied, member, denied);
                // The excluded connection must still receive its own authorized resource.
                var legitimate = await EnqueueAsync(database, visible, SecurityCiFixtureSeed.TenantAMemberUserId,
                    EventFor(target), targetOverride: target);
                await member.WaitEventAsync(legitimate);
                await app.WaitDeliveredAsync(legitimate);
            }
            await controls.SaveAsync();
        });
    }

    [PostgreSqlFact]
    public async Task CurrentResourceReadChangesPreventEveryApplicableCatalogueDeliveryAndRestore()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var memberClient = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
            using var ownerClient = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAOwnerEmail);
            await using var member = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug);
            await using var owner = await app.ConnectAsync(ownerClient, "owner", SecurityCiFixtureSeed.TenantASlug);
            var scope = await SecurityArchitectureSignalRTests.ScopeAsync(database, SecurityCiFixtureSeed.TenantASlug);
            var controls = SecurityArchitectureSignalRControlRecorder.Create(GetType());
            var resourceEvents = CatalogueRoutes().Where(item => item.EventType is not
                ("Announcements.AnnouncementChanged.v1" or "Security.AuthorizationStateChanged.v1")).ToArray();
            await SubscribeAllAsync(member, scope, controls);
            await SubscribeAllAsync(owner, scope);
            Assert.Equal(15, resourceEvents.Length);
            foreach (var boundary in new[] { RealtimeSubscriptionType.Workspace, RealtimeSubscriptionType.Project, RealtimeSubscriptionType.Conversation })
            {
                var events = boundary == RealtimeSubscriptionType.Conversation
                    ? resourceEvents.Where(item => item.EventType.StartsWith("Messaging.", StringComparison.Ordinal)).ToArray()
                    : resourceEvents;
                Assert.Equal(boundary == RealtimeSubscriptionType.Conversation ? 5 : 15, events.Length);
                foreach (var route in events)
                    await AssertRecipientPositiveAsync(database, app, scope, member, owner, route);

                await ChangeResourceReadAsync(database, scope, boundary, authorized: false);
                var deniedEvents = new List<(EventRoute Route, Guid Event)>();
                foreach (var route in events)
                {
                    var eventType = route.EventType;
                    var denied = await EnqueueAsync(database, scope, SecurityCiFixtureSeed.TenantAMemberUserId, eventType, targetOverride: route.Override);
                    var positive = denied;
                    if (route.Target == RealtimeSubscriptionType.User)
                        positive = await EnqueueAsync(database, scope, SecurityCiFixtureSeed.TenantAOwnerUserId, eventType, targetOverride: route.Override);
                    await owner.WaitEventAsync(positive);
                    await app.WaitDeliveredAsync(positive);
                    await app.WaitDeliveredAsync(denied);
                    controls.ObserveIsolation(eventType, route.Target, "CURRENT_" + boundary.ToString().ToUpperInvariant() + "_READ",
                        owner, positive, member, denied);
                    await ReplayAsync(database, app, denied);
                    if (route.Target == RealtimeSubscriptionType.User) await ReplayAsync(database, app, positive);
                    await owner.WaitEventAsync(positive, minimumCount: 2);
                    controls.ObserveIsolation(eventType, route.Target, "REPOSITORY_REPLAY_CURRENT_" + boundary.ToString().ToUpperInvariant() + "_READ",
                        owner, positive, member, denied);
                    deniedEvents.Add((route, denied));
                }
                await InvokeWithReceiptAsync(member, controls, "Subscribe" + boundary, false, "AccessDenied", ResourceFor(scope, boundary));
                // Parent authority remains required even when the child membership still exists.
                if (boundary is RealtimeSubscriptionType.Workspace or RealtimeSubscriptionType.Project)
                    await InvokeWithReceiptAsync(member, controls, "SubscribeConversation", false, "AccessDenied", scope.Conversation);

                await ChangeResourceReadAsync(database, scope, boundary, authorized: true);
                await SubscribeAllAsync(member, scope);
                foreach (var route in events)
                    await AssertRecipientPositiveAsync(database, app, scope, member, owner, route);
                foreach (var (route, denied) in deniedEvents)
                {
                    await ReplayAsync(database, app, denied);
                    await member.WaitEventAsync(denied);
                    controls.ObservePositive(route.EventType, route.Target,
                        "REPOSITORY_REPLAY_RESTORED_" + boundary.ToString().ToUpperInvariant() + "_READ", member, denied);
                }
            }
            await controls.SaveAsync();
        });
    }

    private static async Task AssertRecipientPositiveAsync(string database, SecurityArchitectureSignalRFixture app,
        Scope scope, RealtimeSocket member, RealtimeSocket owner, EventRoute route)
    {
        var positive = await EnqueueAsync(database, scope, SecurityCiFixtureSeed.TenantAMemberUserId, route.EventType, targetOverride: route.Override);
        await member.WaitEventAsync(positive);
        await app.WaitDeliveredAsync(positive);
        if (route.Target != RealtimeSubscriptionType.User) await owner.WaitEventAsync(positive);
    }

    internal static async Task ReplayAsync(string database, SecurityArchitectureSignalRFixture app, Guid eventId)
    {
        var started = DateTimeOffset.UtcNow;
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var before = await db.OutboxEvents.AsNoTracking().Where(item => item.Id == eventId)
            .Select(item => new { item.TenantId, item.EventType, item.AggregateType, item.AggregateId, item.PayloadJson, item.RoutingJson }).SingleAsync();
        // Direct fixture requeue, not a grant of manual operator replay authority.
        Assert.True(await new OutboxEventRepository(db).ReplayAsync(eventId, started));
        var after = await db.OutboxEvents.AsNoTracking().Where(item => item.Id == eventId)
            .Select(item => new { item.TenantId, item.EventType, item.AggregateType, item.AggregateId, item.PayloadJson, item.RoutingJson }).SingleAsync();
        Assert.True(before.Equals(after), "Replay must preserve durable identity, payload and routing.");
        await app.WaitDeliveredAsync(eventId, started);
    }

    private static async Task ChangeResourceReadAsync(string database, Scope scope, RealtimeSubscriptionType boundary, bool authorized)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var user = SecurityCiFixtureSeed.TenantAMemberUserId;
        switch (boundary)
        {
            case RealtimeSubscriptionType.Workspace:
                var workspaceMember = await db.WorkspaceMembers.SingleAsync(item => item.WorkspaceId == scope.Workspace && item.UserId == user);
                workspaceMember.Status = authorized ? MembershipStatus.Active : MembershipStatus.Suspended;
                break;
            case RealtimeSubscriptionType.Project:
                if (authorized)
                    db.ProjectMembers.Add(new ProjectMember { TenantId = scope.Tenant, ProjectId = scope.Project,
                        UserId = user, Role = ProjectRole.Contributor, JoinedAt = DateTimeOffset.UtcNow });
                else
                    db.ProjectMembers.Remove(await db.ProjectMembers.SingleAsync(item => item.ProjectId == scope.Project && item.UserId == user));
                break;
            case RealtimeSubscriptionType.Conversation:
                var conversationMember = await db.ConversationMembers.SingleAsync(item => item.ConversationId == scope.Conversation && item.UserId == user);
                conversationMember.CanRead = authorized;
                break;
            default:
                throw new InvalidOperationException("Unexpected resource read boundary.");
        }
        await db.SaveChangesAsync();
    }

    private static async Task<Scope> HiddenScopeAsync(string database, Guid tenant)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var owner = SecurityCiFixtureSeed.TenantAOwnerUserId;
        var workspace = new Workspace { TenantId = tenant, Name = "Synthetic hidden transport Workspace",
            Slug = "transport-hidden-" + Guid.NewGuid().ToString("N"), CreatedByUserId = owner, Status = WorkspaceStatus.Active };
        var project = new Project { TenantId = tenant, WorkspaceId = workspace.Id, OwnerUserId = owner,
            CreatedByUserId = owner, Name = "Synthetic hidden transport Project", Slug = "transport-hidden-" + Guid.NewGuid().ToString("N"),
            Visibility = ProjectVisibility.MembersOnly, Status = ProjectStatus.Active, ActivationState = ProjectActivationState.Activated,
            ActivatedAtUtc = DateTimeOffset.UtcNow, ActivationVersion = 1, VersionNo = 1 };
        var conversation = new Conversation { TenantId = tenant, WorkspaceId = workspace.Id, ProjectId = project.Id,
            Type = ConversationType.ProjectChannel, Visibility = ConversationVisibility.Private,
            Title = "Synthetic hidden transport Conversation", CreatedByUserId = owner };
        db.AddRange(workspace, project, conversation,
            new WorkspaceMember { TenantId = tenant, WorkspaceId = workspace.Id, UserId = owner,
                Role = WorkspaceRole.Owner, Status = MembershipStatus.Active, JoinedAt = DateTimeOffset.UtcNow },
            new ProjectMember { TenantId = tenant, ProjectId = project.Id, UserId = owner, Role = ProjectRole.Owner, JoinedAt = DateTimeOffset.UtcNow },
            new ConversationMember { TenantId = tenant, ConversationId = conversation.Id, UserId = owner,
                Role = ConversationMemberRole.Admin, CanRead = true, CanPost = true, JoinedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return new(tenant, workspace.Id, project.Id, conversation.Id);
    }

    private static Guid ResourceFor(Scope scope, RealtimeSubscriptionType target) => target switch
    {
        RealtimeSubscriptionType.Workspace => scope.Workspace,
        RealtimeSubscriptionType.Project => scope.Project,
        RealtimeSubscriptionType.Conversation => scope.Conversation,
        _ => throw new InvalidOperationException("Unexpected resource subscription.")
    };

    private static string EventFor(RealtimeSubscriptionType target) => target == RealtimeSubscriptionType.Conversation
        ? "Messaging.MessageUpdated.v1" : "Projects.ProjectChanged.v1";

    internal sealed record EventRoute(string EventType, RealtimeSubscriptionType Target, RealtimeSubscriptionType? Override = null);

    internal static EventRoute[] CatalogueRoutes()
    {
        var routes = RealtimeEventCatalog.EventTypes.Order(StringComparer.Ordinal)
            .Select(item => new EventRoute(item, TargetFor(item))).Concat(new[]
            {
                new EventRoute("Projects.TaskChanged.v1", RealtimeSubscriptionType.User, RealtimeSubscriptionType.User),
                new EventRoute("Projects.ProjectChanged.v1", RealtimeSubscriptionType.Workspace, RealtimeSubscriptionType.Workspace)
            }).OrderBy(item => item.EventType, StringComparer.Ordinal).ThenBy(item => item.Target).ToArray();
        Assert.Equal(17, routes.Length);
        return routes;
    }

    private static RealtimeSubscriptionType TargetFor(string eventType) => eventType switch
    {
        "Messaging.ConversationUnreadChanged.v1" or "Announcements.AnnouncementChanged.v1" or
            "Notifications.NotificationCreated.v1" or "Notifications.NotificationReadStateChanged.v1" or
            "Security.AuthorizationStateChanged.v1" => RealtimeSubscriptionType.User,
        "Files.FileChanged.v1" => RealtimeSubscriptionType.Workspace,
        _ when eventType.StartsWith("Projects.", StringComparison.Ordinal) => RealtimeSubscriptionType.Project,
        _ => RealtimeSubscriptionType.Conversation
    };
}
