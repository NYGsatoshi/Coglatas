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
            await SubscribeAllAsync(member, alphaScope);
            await SubscribeAllAsync(owner, alphaScope);
            await SubscribeAllAsync(beta, betaScope);
            var observations = new List<object>();
            var events = RealtimeEventCatalog.EventTypes.Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(15, events.Length);
            foreach (var eventType in events)
            {
                var positive = await EnqueueAsync(database, alphaScope, SecurityCiFixtureSeed.TenantAMemberUserId, eventType);
                await member.WaitEventAsync(positive);
                await app.WaitDeliveredAsync(positive);
                var foreign = await EnqueueAsync(database, betaScope, SecurityCiFixtureSeed.TenantBOwnerUserId, eventType);
                await beta.WaitEventAsync(foreign);
                await app.WaitDeliveredAsync(foreign);
                Assert.False(member.Received(foreign));
                Assert.False(beta.Received(positive));
                observations.Add(new { eventType, control = "LIVE_POSITIVE_AND_CROSS_TENANT_NON_DELIVERY", runtimeOutcome = "PASS" });
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
            foreach (var eventType in events.Where(item => item != "Security.AuthorizationStateChanged.v1"))
            {
                var denied = await EnqueueAsync(database, alphaScope, SecurityCiFixtureSeed.TenantAMemberUserId, eventType);
                await app.WaitDeliveredAsync(denied);
                var positive = await EnqueueAsync(database, alphaScope, SecurityCiFixtureSeed.TenantAOwnerUserId, eventType);
                await owner.WaitEventAsync(positive);
                await app.WaitDeliveredAsync(positive);
                Assert.False(member.Received(denied));
                observations.Add(new { eventType, control = "COMMITTED_TENANT_MEMBERSHIP_REVOCATION_WITH_LIVE_PEER", runtimeOutcome = "PASS" });
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

    private static async Task SubscribeAllAsync(RealtimeSocket socket, Scope scope)
    {
        Assert.True(await socket.SubscribeAsync("SubscribeUser"));
        Assert.True(await socket.SubscribeAsync("SubscribeConversation", scope.Conversation));
        Assert.True(await socket.SubscribeAsync("SubscribeWorkspace", scope.Workspace));
        Assert.True(await socket.SubscribeAsync("SubscribeProject", scope.Project));
    }

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
            await SubscribeAllAsync(member, scope);
            await SubscribeAllAsync(owner, scope);
            foreach (var target in new[] { RealtimeSubscriptionType.Project, RealtimeSubscriptionType.Workspace })
            {
                var initial = await EnqueueAsync(database, scope, SecurityCiFixtureSeed.TenantAMemberUserId,
                    "Projects.ProjectChanged.v1", targetOverride: target);
                await member.WaitEventAsync(initial);
                await owner.WaitEventAsync(initial);
                await app.WaitDeliveredAsync(initial);
            }
            Assert.True(await member.SubscribeAsync("UnsubscribeProject", scope.Project));
            Assert.True(await member.SubscribeAsync("UnsubscribeWorkspace", scope.Workspace));
            Assert.True(await member.SubscribeAsync("UnsubscribeProject", scope.Project));
            Assert.True(await member.SubscribeAsync("UnsubscribeWorkspace", scope.Workspace));
            foreach (var target in new[] { RealtimeSubscriptionType.Project, RealtimeSubscriptionType.Workspace })
            {
                var denied = await EnqueueAsync(database, scope, SecurityCiFixtureSeed.TenantAMemberUserId,
                    "Projects.ProjectChanged.v1", targetOverride: target);
                await owner.WaitEventAsync(denied);
                await app.WaitDeliveredAsync(denied);
                Assert.False(member.Received(denied));
            }
            Assert.True(await member.SubscribeAsync("SubscribeUser"));
            Assert.True(await member.SubscribeAsync("SubscribeProject", scope.Project));
            Assert.True(await member.SubscribeAsync("SubscribeWorkspace", scope.Workspace));
            foreach (var target in new[] { RealtimeSubscriptionType.Project, RealtimeSubscriptionType.Workspace })
            {
                var final = await EnqueueAsync(database, scope, SecurityCiFixtureSeed.TenantAMemberUserId,
                    "Projects.ProjectChanged.v1", targetOverride: target);
                await member.WaitEventAsync(final);
                await owner.WaitEventAsync(final);
                await app.WaitDeliveredAsync(final);
            }
        });
    }

    private static async Task<Guid> EnqueueAsync(string database, Scope scope, Guid recipient, string eventType,
        Guid? forgedAffectedUser = null, RealtimeSubscriptionType? targetOverride = null)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var taskId = await db.TaskItems.AsNoTracking().Where(item => item.ProjectId == scope.Project)
            .Select(item => item.Id).SingleAsync();
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
            Assert.Equal("Projects.ProjectChanged.v1", eventType);
            Assert.Contains(targetOverride.Value, new[] { RealtimeSubscriptionType.Project, RealtimeSubscriptionType.Workspace });
            targetType = targetOverride.Value;
            targetId = targetType == RealtimeSubscriptionType.Project ? scope.Project : scope.Workspace;
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
}
