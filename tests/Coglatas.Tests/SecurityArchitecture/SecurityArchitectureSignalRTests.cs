using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed class SecurityArchitectureSignalRTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [PostgreSqlFact]
    public async Task ProductTransportRejectsUnapprovedOriginsWithAuthenticatedLiveControls()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var client = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
            var endpoint = new UriBuilder(app.Address) { Scheme = "ws", Path = "/hubs/app" }.Uri;
            var scope = await ScopeAsync(database, SecurityCiFixtureSeed.TenantASlug);
            await using var control = app.CreateSocket("member", SecurityCiFixtureSeed.TenantASlug);
            control.Options.SetRequestHeader("Origin", app.Address.GetLeftPart(UriPartial.Authority));
            await control.ConnectAsync(endpoint);
            var controls = SecurityArchitectureSignalRControlRecorder.Create(GetType());
            await SecurityArchitectureSignalREventTests.InvokeWithReceiptAsync(control, controls, "SubscribeConversation", true, "Subscribed", scope.Conversation);
            var initial = await EnqueueAsync(database, scope);
            await control.WaitEventAsync(initial);
            await app.WaitDeliveredAsync(initial);
            controls.ObservePositive("Messaging.MessageUpdated.v1", RealtimeSubscriptionType.Conversation, "CURRENT_ORIGIN_INITIAL_DELIVERY", control, initial);
            using (var allowed = new HttpRequestMessage(HttpMethod.Post, "/hubs/app/negotiate?negotiateVersion=1"))
            {
                allowed.Headers.Add("Origin", app.Address.GetLeftPart(UriPartial.Authority));
                using var response = await client.SendAsync(allowed);
                controls.ObserveOriginNegotiation(response, "AUTHORIZED_ORIGIN", HttpStatusCode.OK, control, initial);
            }

            foreach (var (origin, category) in new[] { ("https://foreign.example.test", "FOREIGN_ORIGIN"),
                         ("null", "NULL_ORIGIN"), ("https://foreign.example.test/path", "ORIGIN_WITH_PATH") })
            {
                using var negotiate = new HttpRequestMessage(HttpMethod.Post, "/hubs/app/negotiate?negotiateVersion=1");
                negotiate.Headers.Add("Origin", origin);
                using var response = await client.SendAsync(negotiate);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                controls.ObserveOriginNegotiation(response, category, HttpStatusCode.Forbidden, control, initial);
                await using var rejected = app.CreateSocket("member", SecurityCiFixtureSeed.TenantASlug);
                rejected.Options.CollectHttpResponseDetails = true;
                rejected.Options.SetRequestHeader("Origin", origin);
                await Assert.ThrowsAsync<System.Net.WebSockets.WebSocketException>(() => rejected.ConnectAsync(endpoint));
                Assert.Equal(HttpStatusCode.Forbidden, rejected.UpgradeStatusCode);
                controls.ObserveOriginUpgrade(rejected, category, HttpStatusCode.Forbidden, control, initial);
            }
            var final = await EnqueueAsync(database, scope);
            await control.WaitEventAsync(final);
            await app.WaitDeliveredAsync(final);
            controls.ObservePositive("Messaging.MessageUpdated.v1", RealtimeSubscriptionType.Conversation, "CURRENT_ORIGIN_FINAL_DELIVERY", control, final);
            // A present Origin is a browser boundary, not an authentication credential.
            await using var anonymous = new RealtimeSocket();
            anonymous.Options.CollectHttpResponseDetails = true;
            anonymous.Options.SetRequestHeader("X-Tenant-Slug", SecurityCiFixtureSeed.TenantASlug);
            anonymous.Options.SetRequestHeader("Origin", app.Address.GetLeftPart(UriPartial.Authority));
            await Assert.ThrowsAsync<System.Net.WebSockets.WebSocketException>(() => anonymous.ConnectAsync(endpoint));
            controls.ObserveOriginUpgrade(anonymous, "AUTHORIZED_ORIGIN_ANONYMOUS", HttpStatusCode.Unauthorized, control, final);
            await controls.SaveAsync();
        });
    }

    [PostgreSqlFact]
    public async Task ProductTransportApprovedOriginRetainsSessionAndResourceAuthorization()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database, approvedOrigin: true);
            using var client = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
            using var negotiate = new HttpRequestMessage(HttpMethod.Post, "/hubs/app/negotiate?negotiateVersion=1");
            negotiate.Headers.Add("Origin", "https://console.example.test");
            using var response = await client.SendAsync(negotiate);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var endpoint = new UriBuilder(app.Address) { Scheme = "ws", Path = "/hubs/app" }.Uri;
            var alpha = await ScopeAsync(database, SecurityCiFixtureSeed.TenantASlug);
            var beta = await ScopeAsync(database, SecurityCiFixtureSeed.TenantBSlug);
            await using var control = app.CreateSocket("member", SecurityCiFixtureSeed.TenantASlug);
            control.Options.SetRequestHeader("Origin", "https://console.example.test");
            await control.ConnectAsync(endpoint);
            var controls = SecurityArchitectureSignalRControlRecorder.Create(GetType());
            await SecurityArchitectureSignalREventTests.InvokeWithReceiptAsync(control, controls, "SubscribeConversation", true, "Subscribed", alpha.Conversation);
            var initial = await EnqueueAsync(database, alpha);
            await control.WaitEventAsync(initial);
            await app.WaitDeliveredAsync(initial);
            controls.ObservePositive("Messaging.MessageUpdated.v1", RealtimeSubscriptionType.Conversation, "CURRENT_ORIGIN_INITIAL_DELIVERY", control, initial);
            controls.ObserveOriginNegotiation(response, "AUTHORIZED_ORIGIN", HttpStatusCode.OK, control, initial);
            await SecurityArchitectureSignalREventTests.InvokeWithReceiptAsync(control, controls, "SubscribeConversation", false, "AccessDenied", beta.Conversation);
            await using var anonymous = new RealtimeSocket();
            anonymous.Options.CollectHttpResponseDetails = true;
            anonymous.Options.SetRequestHeader("Origin", "https://console.example.test");
            anonymous.Options.SetRequestHeader("X-Tenant-Slug", SecurityCiFixtureSeed.TenantASlug);
            await Assert.ThrowsAsync<System.Net.WebSockets.WebSocketException>(() => anonymous.ConnectAsync(endpoint));
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.UpgradeStatusCode);
            controls.ObserveOriginUpgrade(anonymous, "AUTHORIZED_ORIGIN_ANONYMOUS", HttpStatusCode.Unauthorized, control, initial);
            var final = await EnqueueAsync(database, alpha);
            await control.WaitEventAsync(final);
            await app.WaitDeliveredAsync(final);
            controls.ObservePositive("Messaging.MessageUpdated.v1", RealtimeSubscriptionType.Conversation, "CURRENT_ORIGIN_FINAL_DELIVERY", control, final);
            await controls.SaveAsync();
        });
    }

    [PostgreSqlFact]
    public async Task ProductTransportRejectsForeignSubscriptionsAndDeliveryWithLiveControls()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var alphaClient = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
            using var betaClient = await app.LoginAsync("beta", SecurityCiFixtureSeed.TenantBSlug, SecurityCiFixtureSeed.TenantBOwnerEmail);
            await using var alpha = await app.ConnectAsync(alphaClient, "member", SecurityCiFixtureSeed.TenantASlug);
            await using var beta = await app.ConnectAsync(betaClient, "beta", SecurityCiFixtureSeed.TenantBSlug);
            var a = await ScopeAsync(database, SecurityCiFixtureSeed.TenantASlug);
            var b = await ScopeAsync(database, SecurityCiFixtureSeed.TenantBSlug);
            var controls = SecurityArchitectureSignalRControlRecorder.Create(GetType());
            await SecurityArchitectureSignalREventTests.SubscribeAllAsync(alpha, a, controls);
            await SecurityArchitectureSignalREventTests.InvokeWithReceiptAsync(alpha, controls, "SubscribeTenant", true, "Subscribed");
            Assert.True(await beta.SubscribeAsync("SubscribeConversation", b.Conversation));
            var initial = await EnqueueAsync(database, a);
            await alpha.WaitEventAsync(initial);
            await app.WaitDeliveredAsync(initial);
            Assert.False(beta.Received(initial));
            await SecurityArchitectureSignalREventTests.InvokeWithReceiptAsync(alpha, controls, "SubscribeWorkspace", false, "AccessDenied", b.Workspace);
            await SecurityArchitectureSignalREventTests.InvokeWithReceiptAsync(alpha, controls, "SubscribeProject", false, "AccessDenied", b.Project);
            await SecurityArchitectureSignalREventTests.InvokeWithReceiptAsync(alpha, controls, "SubscribeConversation", false, "AccessDenied", b.Conversation);
            Assert.False(await alpha.SubscribeAsync("SubscribeProject", Guid.NewGuid()));
            await Assert.ThrowsAsync<InvalidOperationException>(() => alpha.InvokeAsync("JoinGroup", "tenant:" + b.Tenant));
            var foreign = await EnqueueAsync(database, b);
            await beta.WaitEventAsync(foreign);
            await app.WaitDeliveredAsync(foreign);
            var control = await EnqueueAsync(database, a);
            await alpha.WaitEventAsync(control);
            await app.WaitDeliveredAsync(control);
            Assert.False(alpha.Received(foreign));
            controls.ObserveIsolation("Messaging.MessageUpdated.v1", RealtimeSubscriptionType.Conversation,
                "CROSS_TENANT", beta, foreign, alpha, foreign);
            await SecurityArchitectureSignalREventTests.InvokeWithReceiptAsync(alpha, controls, "UnsubscribeConversation", true, "Unsubscribed", a.Conversation);
            Assert.True(await alpha.SubscribeAsync("SubscribeConversation", a.Conversation));

            // A GET WebSocket upgrade has no CSRF side effect; product cookie authorization must deny it.
            await using var unauthenticated = new RealtimeSocket();
            unauthenticated.Options.SetRequestHeader("X-Tenant-Slug", SecurityCiFixtureSeed.TenantASlug);
            var endpoint = new UriBuilder(app.Address) { Scheme = "ws", Path = "/hubs/app" }.Uri;
            await Assert.ThrowsAsync<System.Net.WebSockets.WebSocketException>(() => unauthenticated.ConnectAsync(endpoint));
            // Auth/me is identity-only; Hub access requires current selected-tenant membership.
            await using var switched = app.CreateSocket("member", SecurityCiFixtureSeed.TenantBSlug);
            await Assert.ThrowsAsync<System.Net.WebSockets.WebSocketException>(() => switched.ConnectAsync(endpoint));
            var final = await EnqueueAsync(database, a);
            await alpha.WaitEventAsync(final);
            await app.WaitDeliveredAsync(final);
            await controls.SaveAsync();
        });
    }

    [PostgreSqlFact]
    public async Task ProductTransportReauthorizesRevokedConversationAndReplayedEvents()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var memberClient = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
            using var ownerClient = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAOwnerEmail);
            await using var member = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug);
            await using var owner = await app.ConnectAsync(ownerClient, "owner", SecurityCiFixtureSeed.TenantASlug);
            var scope = await ScopeAsync(database, SecurityCiFixtureSeed.TenantASlug);
            var controls = SecurityArchitectureSignalRControlRecorder.Create(GetType());
            await SecurityArchitectureSignalREventTests.InvokeWithReceiptAsync(member, controls, "SubscribeConversation", true, "Subscribed", scope.Conversation);
            Assert.True(await owner.SubscribeAsync("SubscribeConversation", scope.Conversation));
            var initial = await EnqueueAsync(database, scope);
            await member.WaitEventAsync(initial);
            await owner.WaitEventAsync(initial);
            await app.WaitDeliveredAsync(initial);
            controls.ObservePositive("Messaging.MessageUpdated.v1", RealtimeSubscriptionType.Conversation, "CURRENT_CONVERSATION_INITIAL_DELIVERY", member, initial);

            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                var participant = await db.ConversationMembers.SingleAsync(m => m.ConversationId == scope.Conversation && m.UserId == SecurityCiFixtureSeed.TenantAMemberUserId);
                participant.CanRead = false;
                participant.CanPost = false;
                await db.SaveChangesAsync();
            }
            await SecurityArchitectureSignalREventTests.InvokeWithReceiptAsync(member, controls, "SubscribeConversation", false, "AccessDenied", scope.Conversation);
            // This is a fixture state mutation, not authorization by the manual replay service.
            var denied = await EnqueueAsync(database, scope);
            await owner.WaitEventAsync(denied);
            await app.WaitDeliveredAsync(denied);
            Assert.False(member.Received(denied));
            controls.ObserveIsolation("Messaging.MessageUpdated.v1", RealtimeSubscriptionType.Conversation, "CURRENT_CONVERSATION_READ", owner, denied, member, denied);
            var replayStartedAtUtc = DateTimeOffset.UtcNow;
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                var replay = new OutboxEventRepository(db);
                Assert.True(await replay.ReplayAsync(denied, DateTimeOffset.UtcNow));
                await db.SaveChangesAsync();
            }
            await app.WaitDeliveredAsync(denied, replayStartedAtUtc);
            await owner.WaitEventAsync(denied, minimumCount: 2);
            controls.ObserveIsolation("Messaging.MessageUpdated.v1", RealtimeSubscriptionType.Conversation, "REPOSITORY_REPLAY_CURRENT_CONVERSATION_READ", owner, denied, member, denied);
            var final = await EnqueueAsync(database, scope);
            await owner.WaitEventAsync(final);
            await app.WaitDeliveredAsync(final);
            Assert.False(member.Received(denied));
            Assert.False(member.Received(final));
            controls.ObserveIsolation("Messaging.MessageUpdated.v1", RealtimeSubscriptionType.Conversation, "CURRENT_CONVERSATION_FINAL_READ", owner, final, member, final);
            // A denied client still responds on its live authenticated transport.
            await SecurityArchitectureSignalREventTests.InvokeWithReceiptAsync(member, controls, "SubscribeUser", true, "Subscribed");
            await using var reconnected = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug);
            await SecurityArchitectureSignalREventTests.InvokeWithReceiptAsync(reconnected, controls, "SubscribeConversation", false, "AccessDenied", scope.Conversation);
            await SecurityArchitectureSignalREventTests.InvokeWithReceiptAsync(reconnected, controls, "SubscribeUser", true, "Subscribed");
            await controls.SaveAsync();
        });
    }

    [PostgreSqlFact]
    public Task ProductTransportSessionInvalidationPreventsDelayedDeliveryAndReconnect() =>
        AssertInvalidSessionAsync(expired: false, nameof(ProductTransportSessionInvalidationPreventsDelayedDeliveryAndReconnect));

    [PostgreSqlFact]
    public Task ProductTransportExpiredSessionPreventsDelayedDeliveryAndReconnect() =>
        AssertInvalidSessionAsync(expired: true, nameof(ProductTransportExpiredSessionPreventsDelayedDeliveryAndReconnect));

    private static async Task AssertInvalidSessionAsync(bool expired, string verifier)
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var memberClient = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
            using var ownerClient = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAOwnerEmail);
            await using var member = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug);
            await using var owner = await app.ConnectAsync(ownerClient, "owner", SecurityCiFixtureSeed.TenantASlug);
            var scope = await ScopeAsync(database, SecurityCiFixtureSeed.TenantASlug);
            var controls = SecurityArchitectureSignalRControlRecorder.Create(typeof(SecurityArchitectureSignalRTests), verifier);
            await SecurityArchitectureSignalREventTests.SubscribeAllAsync(member, scope, controls);
            await SecurityArchitectureSignalREventTests.SubscribeAllAsync(owner, scope);
            var routes = SecurityArchitectureSignalREventTests.CatalogueRoutes()
                .Where(item => item.EventType != "Security.AuthorizationStateChanged.v1").ToArray();
            Assert.Equal(16, routes.Length);
            foreach (var route in routes)
            {
                var initial = await SecurityArchitectureSignalREventTests.EnqueueAsync(database, scope,
                    SecurityCiFixtureSeed.TenantAMemberUserId, route.EventType, targetOverride: route.Override);
                await member.WaitEventAsync(initial);
                await app.WaitDeliveredAsync(initial);
            }
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                var sessions = await db.Sessions.Where(s => s.UserId == SecurityCiFixtureSeed.TenantAMemberUserId).ToListAsync();
                Assert.NotEmpty(sessions);
                foreach (var session in sessions)
                    if (expired) session.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
                    else session.RevokedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }
            foreach (var route in routes)
            {
                var denied = await SecurityArchitectureSignalREventTests.EnqueueAsync(database, scope,
                    SecurityCiFixtureSeed.TenantAMemberUserId, route.EventType, targetOverride: route.Override);
                var positive = denied;
                if (route.Target == RealtimeSubscriptionType.User)
                    positive = await SecurityArchitectureSignalREventTests.EnqueueAsync(database, scope,
                        SecurityCiFixtureSeed.TenantAOwnerUserId, route.EventType, targetOverride: route.Override);
                await owner.WaitEventAsync(positive);
                await app.WaitDeliveredAsync(positive);
                await app.WaitDeliveredAsync(denied);
                controls.ObserveIsolation(route.EventType, route.Target, expired ? "CURRENT_SESSION_EXPIRY" : "CURRENT_SESSION_REVOCATION",
                    owner, positive, member, denied);
            }
            // Preserve the revoked cookie before the HTTP rejection expires it in the client jar.
            await using var rejected = app.CreateSocket("member", SecurityCiFixtureSeed.TenantASlug);
            using var response = await memberClient.GetAsync("/api/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var endpoint = new UriBuilder(app.Address) { Scheme = "ws", Path = "/hubs/app" }.Uri;
            await Assert.ThrowsAsync<System.Net.WebSockets.WebSocketException>(() => rejected.ConnectAsync(endpoint));
            var control = await EnqueueAsync(database, scope);
            await owner.WaitEventAsync(control);
            await app.WaitDeliveredAsync(control);
            // A real fresh login issues a new valid session; invalidated sessions
            // remain invalid. The original socket cannot adopt the new cookie.
            using var freshClient = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
            await using var fresh = await app.ConnectAsync(freshClient, "member", SecurityCiFixtureSeed.TenantASlug);
            await SecurityArchitectureSignalREventTests.SubscribeAllAsync(fresh, scope);
            foreach (var route in routes)
            {
                var restored = await SecurityArchitectureSignalREventTests.EnqueueAsync(database, scope,
                    SecurityCiFixtureSeed.TenantAMemberUserId, route.EventType, targetOverride: route.Override);
                await fresh.WaitEventAsync(restored);
                await app.WaitDeliveredAsync(restored);
                controls.ObserveIsolation(route.EventType, route.Target, "FRESH_SESSION_DELIVERY_OLD_SESSION_EXCLUDED",
                    fresh, restored, member, restored);
            }
            await controls.SaveAsync();
        });
    }

    [PostgreSqlFact]
    public async Task ProductTransportPreservesReadButRejectsPostingAfterRoleDowngrade()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var client = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
            await using var socket = await app.ConnectAsync(client, "member", SecurityCiFixtureSeed.TenantASlug);
            var scope = await ScopeAsync(database, SecurityCiFixtureSeed.TenantASlug);
            var controls = SecurityArchitectureSignalRControlRecorder.Create(GetType());
            var httpControls = SecurityArchitectureHttpControlRecorder.Create(GetType(), "ACTUAL_TEST_WEB_ENTRY_POINT_AND_MIGRATED_POSTGRESQL");
            await SecurityArchitectureSignalREventTests.InvokeWithReceiptAsync(socket, controls, "SubscribeConversation", true, "Subscribed", scope.Conversation);
            Guid messageId;
            using (var sent = await client.PostAsJsonAsync($"/api/conversations/{scope.Conversation:D}/messages",
                       new { body = "Synthetic positive transport control", clientRequestId = Guid.NewGuid() }))
            {
                Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
                using var payload = JsonDocument.Parse(await sent.Content.ReadAsStringAsync());
                messageId = payload.RootElement.GetProperty("id").GetGuid();
                httpControls.Observe(sent, "/api/conversations/{conversationId}/messages", "AUTHORIZED_SAME_SCOPE", HttpStatusCode.OK);
            }
            Guid createdEvent;
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                createdEvent = await db.OutboxEvents.AsNoTracking()
                    .Where(e => e.AggregateId == messageId && e.EventType == "Messaging.MessageCreated.v1")
                    .Select(e => e.Id).SingleAsync();
            await socket.WaitEventAsync(createdEvent);
            await app.WaitDeliveredAsync(createdEvent);
            controls.ObservePositive("Messaging.MessageCreated.v1", RealtimeSubscriptionType.Conversation, "CURRENT_HTTP_BUSINESS_MESSAGE_CREATED_DELIVERY", socket, createdEvent);
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                var participant = await db.ConversationMembers.SingleAsync(m => m.ConversationId == scope.Conversation && m.UserId == SecurityCiFixtureSeed.TenantAMemberUserId);
                participant.Role = ConversationMemberRole.ReadOnly;
                participant.CanPost = false;
                await db.SaveChangesAsync();
            }
            long before;
            long outboxBefore;
            long auditBefore;
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                before = await db.Messages.LongCountAsync(m => m.ConversationId == scope.Conversation);
                outboxBefore = await db.OutboxEvents.LongCountAsync();
                auditBefore = await db.AuditLogs.LongCountAsync(log => log.Action == "communication.message_post_denied" &&
                    log.ActorUserId == SecurityCiFixtureSeed.TenantAMemberUserId);
            }
            using (var denied = await client.PostAsJsonAsync($"/api/conversations/{scope.Conversation:D}/messages",
                       new { body = "Synthetic denied post", clientRequestId = Guid.NewGuid() }))
            {
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
                using var payload = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
                Assert.Equal("You are not allowed to send messages.", payload.RootElement.GetProperty("error").GetString());
                await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    Assert.Equal(before, await db.Messages.LongCountAsync(m => m.ConversationId == scope.Conversation));
                    Assert.Equal(outboxBefore, await db.OutboxEvents.LongCountAsync());
                    Assert.Equal(auditBefore + 1, await db.AuditLogs.LongCountAsync(log => log.Action == "communication.message_post_denied" &&
                        log.ActorUserId == SecurityCiFixtureSeed.TenantAMemberUserId));
                }
                httpControls.Observe(denied, "/api/conversations/{conversationId}/messages", "CURRENT_RESOURCE_ROLE_DENIED", HttpStatusCode.Forbidden,
                    responseAssertion: "PERMISSION_ERROR_NO_MESSAGE_OR_OUTBOX_WITH_NEW_DENIAL_AUDIT");
            }
            await SecurityArchitectureSignalREventTests.InvokeWithReceiptAsync(socket, controls, "SubscribeConversation", true, "Subscribed", scope.Conversation);
            var control = await EnqueueAsync(database, scope);
            await socket.WaitEventAsync(control);
            await app.WaitDeliveredAsync(control);
            controls.ObservePositive("Messaging.MessageUpdated.v1", RealtimeSubscriptionType.Conversation, "CURRENT_READ_ONLY_ROLE_DELIVERY", socket, control);
            await httpControls.SaveAsync();
            await controls.SaveAsync();
        });
    }

    [PostgreSqlFact]
    public async Task ProductTransportReconnectUsesCurrentHttpCatchUpAuthority()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var memberClient = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
            using var ownerClient = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAOwnerEmail);
            var scope = await ScopeAsync(database, SecurityCiFixtureSeed.TenantASlug);
            var controls = SecurityArchitectureSignalRControlRecorder.Create(GetType());
            var httpControls = SecurityArchitectureHttpControlRecorder.Create(GetType(), "ACTUAL_TEST_WEB_ENTRY_POINT_AND_MIGRATED_POSTGRESQL");
            await using var owner = await app.ConnectAsync(ownerClient, "owner", SecurityCiFixtureSeed.TenantASlug);
            Assert.True(await owner.SubscribeAsync("SubscribeConversation", scope.Conversation));
            await using (var beforeDisconnect = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug))
            {
                await SecurityArchitectureSignalREventTests.InvokeWithReceiptAsync(beforeDisconnect, controls, "SubscribeConversation", true, "Subscribed", scope.Conversation);
                var initial = await EnqueueAsync(database, scope);
                await beforeDisconnect.WaitEventAsync(initial);
                await owner.WaitEventAsync(initial);
                await app.WaitDeliveredAsync(initial);
            }
            var missed = await PostMessageAsync(ownerClient, database, scope);
            await owner.WaitEventAsync(missed.Event);
            await app.WaitDeliveredAsync(missed.Event);
            await using var reconnected = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug);
            await SecurityArchitectureSignalREventTests.InvokeWithReceiptAsync(reconnected, controls, "SubscribeConversation", true, "Subscribed", scope.Conversation);
            using (var catchUp = await memberClient.GetAsync($"/api/conversations/{scope.Conversation:D}/messages"))
            {
                Assert.Equal(HttpStatusCode.OK, catchUp.StatusCode);
                using var payload = JsonDocument.Parse(await catchUp.Content.ReadAsStringAsync());
                Assert.Contains(payload.RootElement.GetProperty("items").EnumerateArray(),
                    item => item.GetProperty("id").GetGuid() == missed.Message);
                httpControls.Observe(catchUp, "/api/conversations/{conversationId}/messages", "AUTHORIZED_SAME_SCOPE", HttpStatusCode.OK);
            }
            var connected = await PostMessageAsync(ownerClient, database, scope);
            await reconnected.WaitEventAsync(connected.Event);
            await owner.WaitEventAsync(connected.Event);
            await app.WaitDeliveredAsync(connected.Event);
            controls.ObservePositive("Messaging.MessageCreated.v1", RealtimeSubscriptionType.Conversation, "RECONNECT_HTTP_BUSINESS_MESSAGE_CREATED_DELIVERY", reconnected, connected.Event);
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                var membership = await db.ConversationMembers.SingleAsync(item => item.ConversationId == scope.Conversation &&
                    item.UserId == SecurityCiFixtureSeed.TenantAMemberUserId);
                membership.CanRead = false;
                membership.CanPost = false;
                await db.SaveChangesAsync();
            }
            using (var denied = await memberClient.GetAsync($"/api/conversations/{scope.Conversation:D}/messages"))
            {
                // The legacy ApiResultControllerBase maps this hidden-resource
                // failure to 400; retain that behavior and check the actual denial.
                Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
                using var payload = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
                Assert.Equal("Conversation not found.", payload.RootElement.GetProperty("error").GetString());
                httpControls.Observe(denied, "/api/conversations/{conversationId}/messages", "CURRENT_CONVERSATION_READ_DENIED", HttpStatusCode.BadRequest,
                    responseAssertion: "CONVERSATION_HIDDEN_ERROR");
            }
            await SecurityArchitectureSignalREventTests.InvokeWithReceiptAsync(reconnected, controls, "SubscribeConversation", false, "AccessDenied", scope.Conversation);
            var revoked = await PostMessageAsync(ownerClient, database, scope);
            await owner.WaitEventAsync(revoked.Event);
            await app.WaitDeliveredAsync(revoked.Event);
            Assert.False(reconnected.Received(revoked.Event));
            controls.ObserveIsolation("Messaging.MessageCreated.v1", RealtimeSubscriptionType.Conversation, "RECONNECT_CURRENT_CONVERSATION_READ", owner, revoked.Event, reconnected, revoked.Event);
            await SecurityArchitectureSignalREventTests.InvokeWithReceiptAsync(reconnected, controls, "SubscribeUser", true, "Subscribed");
            await httpControls.SaveAsync();
            await controls.SaveAsync();
        });
    }

    [PostgreSqlFact]
    public async Task ProductTransportTenantCookieSwitchCannotRetargetExistingOrNewSubscriptions()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database, sessionTenantResolution: true);
            using var client = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
            var alphaScope = await ScopeAsync(database, SecurityCiFixtureSeed.TenantASlug);
            var betaScope = await ScopeAsync(database, SecurityCiFixtureSeed.TenantBSlug);
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                db.TenantUsers.Add(new TenantUser { TenantId = betaScope.Tenant, UserId = SecurityCiFixtureSeed.TenantAMemberUserId,
                    Role = TenantUserRole.Member, Status = TenantUserStatus.Active, JoinedAt = DateTimeOffset.UtcNow });
                db.WorkspaceMembers.Add(new WorkspaceMember { TenantId = betaScope.Tenant, WorkspaceId = betaScope.Workspace,
                    UserId = SecurityCiFixtureSeed.TenantAMemberUserId, Role = WorkspaceRole.Member,
                    Status = MembershipStatus.Active, JoinedAt = DateTimeOffset.UtcNow });
                db.ProjectMembers.Add(new ProjectMember { TenantId = betaScope.Tenant, ProjectId = betaScope.Project,
                    UserId = SecurityCiFixtureSeed.TenantAMemberUserId, Role = ProjectRole.Contributor, JoinedAt = DateTimeOffset.UtcNow });
                db.ConversationMembers.Add(new ConversationMember { TenantId = betaScope.Tenant, ConversationId = betaScope.Conversation,
                    UserId = SecurityCiFixtureSeed.TenantAMemberUserId, Role = ConversationMemberRole.Member,
                    CanRead = true, CanPost = true, JoinedAt = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync();
            }
            await using var alpha = await app.ConnectAsync(client, "member", SecurityCiFixtureSeed.TenantASlug);
            Assert.True(await alpha.SubscribeAsync("SubscribeConversation", alphaScope.Conversation));
            var initial = await EnqueueAsync(database, alphaScope);
            await alpha.WaitEventAsync(initial);
            await app.WaitDeliveredAsync(initial);
            using (var switched = await client.PostAsJsonAsync("/api/tenants/switch", new { tenantId = betaScope.Tenant }))
                Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
            using (var current = await client.GetAsync("/api/tenants/current"))
            {
                Assert.Equal(HttpStatusCode.OK, current.StatusCode);
                using var payload = JsonDocument.Parse(await current.Content.ReadAsStringAsync());
                Assert.Equal(betaScope.Tenant, payload.RootElement.GetProperty("tenantId").GetGuid());
            }
            await using var beta = await app.ConnectAsync(client, "member", SecurityCiFixtureSeed.TenantBSlug);
            Assert.True(await beta.SubscribeAsync("SubscribeConversation", betaScope.Conversation));
            Assert.False(await beta.SubscribeAsync("SubscribeConversation", alphaScope.Conversation));
            Assert.False(await alpha.SubscribeAsync("SubscribeConversation", betaScope.Conversation));
            var controls = SecurityArchitectureSignalRControlRecorder.Create(GetType());
            var routes = SecurityArchitectureSignalREventTests.CatalogueRoutes();
            await SecurityArchitectureSignalREventTests.SubscribeAllAsync(alpha, alphaScope, controls);
            await SecurityArchitectureSignalREventTests.SubscribeAllAsync(beta, betaScope);
            foreach (var route in routes)
            {
                var alphaEvent = await SecurityArchitectureSignalREventTests.EnqueueAsync(database, alphaScope,
                    SecurityCiFixtureSeed.TenantAMemberUserId, route.EventType, targetOverride: route.Override);
                await alpha.WaitEventAsync(alphaEvent);
                await app.WaitDeliveredAsync(alphaEvent);
                var betaEvent = await SecurityArchitectureSignalREventTests.EnqueueAsync(database, betaScope,
                    SecurityCiFixtureSeed.TenantAMemberUserId, route.EventType, targetOverride: route.Override);
                await beta.WaitEventAsync(betaEvent);
                await app.WaitDeliveredAsync(betaEvent);
                controls.ObserveIsolation(route.EventType, route.Target, "TENANT_SWITCH_CONNECTION_PINNING", beta, betaEvent, alpha, betaEvent);
                Assert.False(beta.Received(alphaEvent));
            }
            // Metadata invalidation is last and removes subscriptions. Restore
            // them while both memberships are valid before testing revocation.
            await SecurityArchitectureSignalREventTests.SubscribeAllAsync(alpha, alphaScope);
            await SecurityArchitectureSignalREventTests.SubscribeAllAsync(beta, betaScope);
            // Existing connections stay pinned to their originally resolved tenant.
            // A switch is not global revocation of a user's other valid memberships.
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                var membership = await db.TenantUsers.SingleAsync(item => item.TenantId == alphaScope.Tenant &&
                    item.UserId == SecurityCiFixtureSeed.TenantAMemberUserId);
                membership.Status = TenantUserStatus.Suspended;
                await db.SaveChangesAsync();
            }
            foreach (var route in routes.Where(item => item.EventType != "Security.AuthorizationStateChanged.v1"))
            {
                var betaControl = await SecurityArchitectureSignalREventTests.EnqueueAsync(database, betaScope,
                    SecurityCiFixtureSeed.TenantAMemberUserId, route.EventType, targetOverride: route.Override);
                await beta.WaitEventAsync(betaControl);
                await app.WaitDeliveredAsync(betaControl);
                var revoked = await SecurityArchitectureSignalREventTests.EnqueueAsync(database, alphaScope,
                    SecurityCiFixtureSeed.TenantAMemberUserId, route.EventType, targetOverride: route.Override);
                await app.WaitDeliveredAsync(revoked);
                controls.ObserveIsolation(route.EventType, route.Target, "TENANT_SWITCH_CURRENT_ORIGINAL_TENANT_MEMBERSHIP",
                    beta, betaControl, alpha, revoked);
            }
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                var membership = await db.TenantUsers.SingleAsync(item => item.TenantId == alphaScope.Tenant &&
                    item.UserId == SecurityCiFixtureSeed.TenantAMemberUserId);
                membership.Status = TenantUserStatus.Active;
                await db.SaveChangesAsync();
            }
            await SecurityArchitectureSignalREventTests.SubscribeAllAsync(alpha, alphaScope);
            foreach (var route in routes.Where(item => item.EventType != "Security.AuthorizationStateChanged.v1"))
            {
                var restored = await SecurityArchitectureSignalREventTests.EnqueueAsync(database, alphaScope,
                    SecurityCiFixtureSeed.TenantAMemberUserId, route.EventType, targetOverride: route.Override);
                await alpha.WaitEventAsync(restored);
                await app.WaitDeliveredAsync(restored);
                controls.ObserveIsolation(route.EventType, route.Target, "TENANT_SWITCH_RESTORED_ORIGINAL_TENANT_MEMBERSHIP",
                    alpha, restored, beta, restored);
            }
            await controls.SaveAsync();
        });
    }

    private static async Task<(Guid Message, Guid Event)> PostMessageAsync(HttpClient client, string database, Scope scope)
    {
        using var response = await client.PostAsJsonAsync($"/api/conversations/{scope.Conversation:D}/messages",
            new { body = "Synthetic reconnect transport control " + Guid.NewGuid().ToString("N"), clientRequestId = Guid.NewGuid() });
        Assert.True(response.IsSuccessStatusCode, "Synthetic positive message operation returned HTTP " + (int)response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var message = payload.RootElement.GetProperty("id").GetGuid();
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var eventId = await db.OutboxEvents.AsNoTracking().Where(item => item.AggregateId == message &&
            item.EventType == "Messaging.MessageCreated.v1").Select(item => item.Id).SingleAsync();
        return (message, eventId);
    }

    internal sealed record Scope(Guid Tenant, Guid Workspace, Guid Project, Guid Conversation);

    internal static async Task<Scope> ScopeAsync(string database, string tenantSlug)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Slug == tenantSlug);
        var workspace = await db.Workspaces.AsNoTracking().SingleAsync(w => w.TenantId == tenant.Id && w.Slug.StartsWith("sec02-"));
        var project = await db.Projects.AsNoTracking().SingleAsync(p => p.WorkspaceId == workspace.Id && p.Slug.StartsWith("sec02-"));
        // Existing SEC-02 ProjectLinked canaries exercise a disabled legacy type. Add a supported
        // private ProjectChannel for transport controls without changing those original canaries.
        var owner = tenantSlug == SecurityCiFixtureSeed.TenantASlug
            ? SecurityCiFixtureSeed.TenantAOwnerUserId : SecurityCiFixtureSeed.TenantBOwnerUserId;
        var conversation = new Conversation
        {
            TenantId = tenant.Id, WorkspaceId = workspace.Id, ProjectId = project.Id,
            Type = ConversationType.ProjectChannel, Visibility = ConversationVisibility.Private,
            Title = "SEC-ARCH Synthetic Transport", CreatedByUserId = owner
        };
        db.Conversations.Add(conversation);
        db.ConversationMembers.Add(new ConversationMember
        {
            TenantId = tenant.Id, ConversationId = conversation.Id, UserId = owner,
            Role = ConversationMemberRole.Admin, CanRead = true, CanPost = true,
            CanManageMembers = true, CanCreateThread = true, JoinedAt = DateTimeOffset.UtcNow
        });
        if (tenantSlug == SecurityCiFixtureSeed.TenantASlug)
            db.ConversationMembers.Add(new ConversationMember
            {
                TenantId = tenant.Id, ConversationId = conversation.Id, UserId = SecurityCiFixtureSeed.TenantAMemberUserId,
                Role = ConversationMemberRole.Member, CanRead = true, CanPost = true,
                CanCreateThread = true, JoinedAt = DateTimeOffset.UtcNow
            });
        await db.SaveChangesAsync();
        return new(tenant.Id, workspace.Id, project.Id, conversation.Id);
    }

    private static async Task<Guid> EnqueueAsync(string database, Scope scope)
    {
        var id = Guid.NewGuid();
        var aggregate = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var envelope = new DurableEventEnvelope(id, "Messaging.MessageUpdated.v1", 1, now, scope.Tenant,
            "Message", aggregate, 1, RealtimeActor.System(), null, null,
            JsonSerializer.SerializeToElement(new { synthetic = true }));
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        db.OutboxEvents.Add(new OutboxEvent(id)
        {
            TenantId = scope.Tenant, EventType = envelope.EventType, PayloadSchemaVersion = 1,
            AggregateType = envelope.AggregateType, AggregateId = aggregate, OccurredAt = now,
            PayloadJson = JsonSerializer.Serialize(envelope, JsonOptions),
            RoutingJson = JsonSerializer.Serialize(new[] { new RealtimeRoutingTarget(RealtimeSubscriptionType.Conversation, scope.Conversation) }, JsonOptions)
        });
        await db.SaveChangesAsync();
        return id;
    }
}
