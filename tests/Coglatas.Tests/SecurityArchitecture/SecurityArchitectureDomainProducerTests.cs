using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Realtime;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Scope = Coglatas.Tests.SecurityArchitecture.SecurityArchitectureSignalRTests.Scope;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed class SecurityArchitectureDomainProducerTests
{
    private const string Original = "HTTP_DOMAIN_ORIGINAL_PRODUCER";
    private const string Restored = "HTTP_DOMAIN_RESTORED_PROJECT_AUTHORITY";
    private const string Current = "HTTP_DOMAIN_CURRENT_PROJECT_AUTHORITY";
    private const string TaskRoute = "/api/tasks/{taskItemId}";
    private const string AssigneeRoute = "/api/tasks/{taskItemId}/assignee";
    private const string CommentRoute = "/api/tasks/{taskItemId}/comments";
    private const string ProjectRoute = "/api/projects/{projectId}";

    [PostgreSqlFact]
    public async Task ActualProjectTaskFileAndAuthorizationProducersUseCurrentHttpAuthority()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var memberClient = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
            using var ownerClient = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAOwnerEmail);
            using var betaClient = await app.LoginAsync("beta", SecurityCiFixtureSeed.TenantBSlug, SecurityCiFixtureSeed.TenantBOwnerEmail);
            var scope = await SecurityArchitectureSignalRTests.ScopeAsync(database, SecurityCiFixtureSeed.TenantASlug);
            var betaScope = await SecurityArchitectureSignalRTests.ScopeAsync(database, SecurityCiFixtureSeed.TenantBSlug);
            await PrepareCanonicalDefaultAsync(database, scope);
            var controls = SecurityArchitectureSignalRControlRecorder.Create(GetType());
            var http = SecurityArchitectureHttpControlRecorder.Create(GetType(), "ACTUAL_TEST_WEB_ENTRY_POINT_AND_MIGRATED_POSTGRESQL");
            await using var memberProject = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug);
            await using var memberWorkspace = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug);
            await using var memberUser = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug);
            await using var ownerProject = await app.ConnectAsync(ownerClient, "owner", SecurityCiFixtureSeed.TenantASlug);
            await using var ownerWorkspace = await app.ConnectAsync(ownerClient, "owner", SecurityCiFixtureSeed.TenantASlug);
            await using var ownerUser = await app.ConnectAsync(ownerClient, "owner", SecurityCiFixtureSeed.TenantASlug);
            await using var beta = await app.ConnectAsync(betaClient, "beta", SecurityCiFixtureSeed.TenantBSlug);
            await InvokeAsync(memberProject, controls, "SubscribeProject", scope.Project);
            await InvokeAsync(memberWorkspace, controls, "SubscribeWorkspace", scope.Workspace);
            await InvokeAsync(memberUser, controls, "SubscribeUser");
            Assert.True(await ownerProject.SubscribeAsync("SubscribeProject", scope.Project));
            Assert.True(await ownerWorkspace.SubscribeAsync("SubscribeWorkspace", scope.Workspace));
            Assert.True(await ownerUser.SubscribeAsync("SubscribeUser"));
            Assert.True(await beta.SubscribeAsync("SubscribeProject", betaScope.Project));
            Assert.True(await beta.SubscribeAsync("SubscribeWorkspace", betaScope.Workspace));
            Assert.True(await beta.SubscribeAsync("SubscribeUser"));

            var before = await EventIdsAsync(database);
            await UpdateProjectAsync(betaClient, betaScope);
            var betaLive = await EventAsync(database, "Projects.ProjectChanged.v1", betaScope.Project, before);
            await beta.WaitEventAsync(betaLive);
            Assert.True(beta.Received(betaLive, "Projects.ProjectChanged.v1"));
            before = await EventIdsAsync(database);
            using (var ownRole = await ownerClient.PatchAsJsonAsync($"/api/projects/{scope.Project:D}/members/{SecurityCiFixtureSeed.TenantAOwnerUserId:D}", new { role = ProjectRole.Owner }))
                Assert.Equal(HttpStatusCode.OK, ownRole.StatusCode);
            var ownerLive = await WaitAuthorizationAsync(database, app, ownerUser, SecurityCiFixtureSeed.TenantAOwnerUserId, before);
            Assert.True(ownerUser.Received(ownerLive, "Security.AuthorizationStateChanged.v1"));
            Assert.True(await ownerProject.SubscribeAsync("SubscribeProject", scope.Project));
            Assert.True(await ownerWorkspace.SubscribeAsync("SubscribeWorkspace", scope.Workspace));
            Assert.True(await ownerUser.SubscribeAsync("SubscribeUser"));
            before = await EventIdsAsync(database);
            using (var promoted = await ownerClient.PatchAsJsonAsync($"/api/projects/{scope.Project:D}/members/{SecurityCiFixtureSeed.TenantAMemberUserId:D}",
                       new { role = ProjectRole.Manager }))
                Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
            var authorizationEvent = await ObserveAsync(database, app, controls, "Security.AuthorizationStateChanged.v1",
                SecurityCiFixtureSeed.TenantAMemberUserId, before, RealtimeSubscriptionType.User, memberUser, Original, beta: beta);
            controls.ObserveIsolation("Security.AuthorizationStateChanged.v1", RealtimeSubscriptionType.User,
                "HTTP_DOMAIN_AUTHORIZATION_RECIPIENT", memberUser, authorizationEvent, ownerUser, authorizationEvent);
            Assert.True(await memberProject.SubscribeAsync("SubscribeProject", scope.Project));
            Assert.True(await memberWorkspace.SubscribeAsync("SubscribeWorkspace", scope.Workspace));
            Assert.True(await memberUser.SubscribeAsync("SubscribeUser"));

            Guid task;
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                task = await db.TaskItems.AsNoTracking().Where(item => item.ProjectId == scope.Project).Select(item => item.Id).SingleAsync();
            // The existing SEC-02 Task is a supported legacy Task. These actual
            // commands do not claim Task creation or workflow activation coverage.
            before = await EventIdsAsync(database);
            await UpdateTaskAsync(memberClient, database, task, http, "AUTHORIZED_SAME_SCOPE");
            await ObserveAsync(database, app, controls, "Projects.TaskChanged.v1", task, before,
                RealtimeSubscriptionType.Project, memberProject, Original, beta: beta);
            before = await EventIdsAsync(database);
            await SetAssigneeAsync(memberClient, database, task, SecurityCiFixtureSeed.TenantAMemberUserId, http, "AUTHORIZED_SAME_SCOPE");
            await ObserveAsync(database, app, controls, "Projects.TaskAssignmentChanged.v1", task, before,
                RealtimeSubscriptionType.Project, memberProject, Original, beta: beta);
            await ObserveAsync(database, app, controls, "Projects.TaskChanged.v1", task, before,
                RealtimeSubscriptionType.User, memberUser, Original, beta: beta);
            before = await EventIdsAsync(database);
            await CommentAsync(memberClient, task, http, "AUTHORIZED_SAME_SCOPE");
            await ObserveAsync(database, app, controls, "Projects.TaskCommentChanged.v1", task, before,
                RealtimeSubscriptionType.Project, memberProject, Original, beta: beta);
            before = await EventIdsAsync(database);
            await UpdateProjectAsync(memberClient, scope, http, "AUTHORIZED_SAME_SCOPE");
            await ObserveAsync(database, app, controls, "Projects.ProjectChanged.v1", scope.Project, before,
                RealtimeSubscriptionType.Project, memberProject, Original, beta: beta);
            await ObserveAsync(database, app, controls, "Projects.ProjectChanged.v1", scope.Project, before,
                RealtimeSubscriptionType.Workspace, memberWorkspace, Original, beta: beta);
            before = await EventIdsAsync(database);
            var file = await UploadAsync(memberClient, task, http, "AUTHORIZED_SAME_SCOPE");
            await ObserveAsync(database, app, controls, "Files.FileChanged.v1", file, before,
                RealtimeSubscriptionType.Workspace, memberWorkspace, Original, beta: beta);

            before = await EventIdsAsync(database);
            using (var removed = await ownerClient.DeleteAsync($"/api/projects/{scope.Project:D}/members/{SecurityCiFixtureSeed.TenantAMemberUserId:D}"))
                Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
            await WaitAuthorizationAsync(database, app, memberUser, SecurityCiFixtureSeed.TenantAMemberUserId, before);
            await SecurityArchitectureSignalREventTests.InvokeWithReceiptAsync(memberProject, controls, "SubscribeProject", false, "AccessDenied", scope.Project);
            // Workspace and User routes remain legitimately subscribable after
            // Project removal. Re-establish them so later exclusions prove the
            // current Project target boundary rather than missing subscriptions.
            Assert.True(await memberWorkspace.SubscribeAsync("SubscribeWorkspace", scope.Workspace));
            Assert.True(await memberUser.SubscribeAsync("SubscribeUser"));
            await AssertDeniedAsync(memberClient, database, scope, http, HttpMethod.Patch, $"/api/tasks/{task:D}", TaskRoute,
                await TaskBodyAsync(database, task), HttpStatusCode.NotFound, "TASK_NOT_FOUND");
            await AssertDeniedAsync(memberClient, database, scope, http, HttpMethod.Put, $"/api/tasks/{task:D}/assignee", AssigneeRoute,
                new { userId = SecurityCiFixtureSeed.TenantAOwnerUserId, expectedVersion = await VersionAsync(database, task) }, HttpStatusCode.NotFound, "TASK_NOT_FOUND");
            await AssertDeniedAsync(memberClient, database, scope, http, HttpMethod.Post, $"/api/tasks/{task:D}/comments", CommentRoute,
                new { bodyPlainText = "Synthetic denied comment" }, HttpStatusCode.Forbidden, "TASK_FORBIDDEN");
            await AssertDeniedAsync(memberClient, database, scope, http, HttpMethod.Patch, $"/api/projects/{scope.Project:D}", ProjectRoute,
                new { description = "Synthetic denied Project mutation" }, HttpStatusCode.BadRequest, "BadRequest", legacy: true);
            var unchanged = await StateAsync(database, scope);
            using (var deniedUpload = await UploadResponseAsync(memberClient, task))
            {
                Assert.Equal(HttpStatusCode.BadRequest, deniedUpload.StatusCode);
                using var error = JsonDocument.Parse(await deniedUpload.Content.ReadAsStringAsync());
                Assert.Equal("FileMetadataFailed", error.RootElement.GetProperty("error").GetProperty("code").GetString());
                AssertStateEqual(unchanged, await StateAsync(database, scope));
                http.Observe(deniedUpload, "/api/files", "CURRENT_PROJECT_MEMBERSHIP_REVOKED", HttpStatusCode.BadRequest,
                    "FileMetadataFailed", "UNCHANGED_PROJECT_TASK_RELATIONSHIPS_COMMENTS_FILES_AUDIT_OUTBOX");
            }

            before = await EventIdsAsync(database);
            await UpdateTaskAsync(ownerClient, database, task);
            await ObserveAsync(database, app, controls, "Projects.TaskChanged.v1", task, before,
                RealtimeSubscriptionType.Project, ownerProject, Current, memberProject);
            before = await EventIdsAsync(database);
            await SetAssigneeAsync(ownerClient, database, task, SecurityCiFixtureSeed.TenantAOwnerUserId);
            await ObserveAsync(database, app, controls, "Projects.TaskAssignmentChanged.v1", task, before,
                RealtimeSubscriptionType.Project, ownerProject, Current, memberProject);
            await ObserveAsync(database, app, controls, "Projects.TaskChanged.v1", task, before,
                RealtimeSubscriptionType.User, ownerUser, Current, memberUser);
            before = await EventIdsAsync(database);
            await CommentAsync(ownerClient, task);
            await ObserveAsync(database, app, controls, "Projects.TaskCommentChanged.v1", task, before,
                RealtimeSubscriptionType.Project, ownerProject, Current, memberProject);
            before = await EventIdsAsync(database);
            await UpdateProjectAsync(ownerClient, scope);
            await ObserveAsync(database, app, controls, "Projects.ProjectChanged.v1", scope.Project, before,
                RealtimeSubscriptionType.Project, ownerProject, Current, memberProject);
            await ObserveAsync(database, app, controls, "Projects.ProjectChanged.v1", scope.Project, before,
                RealtimeSubscriptionType.Workspace, ownerWorkspace, Current, memberWorkspace);
            before = await EventIdsAsync(database);
            file = await UploadAsync(ownerClient, task);
            await ObserveAsync(database, app, controls, "Files.FileChanged.v1", file, before,
                RealtimeSubscriptionType.Workspace, ownerWorkspace, Current, memberWorkspace);

            before = await EventIdsAsync(database);
            using (var restored = await ownerClient.PostAsJsonAsync($"/api/projects/{scope.Project:D}/members",
                       new { userId = SecurityCiFixtureSeed.TenantAMemberUserId, role = ProjectRole.Manager }))
                Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
            await WaitAuthorizationAsync(database, app, memberUser, SecurityCiFixtureSeed.TenantAMemberUserId, before);
            Assert.True(await memberProject.SubscribeAsync("SubscribeProject", scope.Project));
            Assert.True(await memberWorkspace.SubscribeAsync("SubscribeWorkspace", scope.Workspace));
            Assert.True(await memberUser.SubscribeAsync("SubscribeUser"));
            before = await EventIdsAsync(database);
            await UpdateTaskAsync(memberClient, database, task, http, "AUTHORIZED_RESTORED_SCOPE");
            await ObserveAsync(database, app, controls, "Projects.TaskChanged.v1", task, before,
                RealtimeSubscriptionType.Project, memberProject, Restored);
            before = await EventIdsAsync(database);
            await SetAssigneeAsync(memberClient, database, task, SecurityCiFixtureSeed.TenantAMemberUserId, http, "AUTHORIZED_RESTORED_SCOPE");
            await ObserveAsync(database, app, controls, "Projects.TaskAssignmentChanged.v1", task, before,
                RealtimeSubscriptionType.Project, memberProject, Restored);
            await ObserveAsync(database, app, controls, "Projects.TaskChanged.v1", task, before,
                RealtimeSubscriptionType.User, memberUser, Restored);
            before = await EventIdsAsync(database);
            await CommentAsync(memberClient, task, http, "AUTHORIZED_RESTORED_SCOPE");
            await ObserveAsync(database, app, controls, "Projects.TaskCommentChanged.v1", task, before,
                RealtimeSubscriptionType.Project, memberProject, Restored);
            before = await EventIdsAsync(database);
            await UpdateProjectAsync(memberClient, scope, http, "AUTHORIZED_RESTORED_SCOPE");
            await ObserveAsync(database, app, controls, "Projects.ProjectChanged.v1", scope.Project, before,
                RealtimeSubscriptionType.Project, memberProject, Restored);
            await ObserveAsync(database, app, controls, "Projects.ProjectChanged.v1", scope.Project, before,
                RealtimeSubscriptionType.Workspace, memberWorkspace, Restored);
            before = await EventIdsAsync(database);
            file = await UploadAsync(memberClient, task, http, "AUTHORIZED_RESTORED_SCOPE");
            await ObserveAsync(database, app, controls, "Files.FileChanged.v1", file, before,
                RealtimeSubscriptionType.Workspace, memberWorkspace, Restored);
            await controls.SaveAsync();
            await http.SaveAsync();
        });
    }

    [PostgreSqlFact]
    public async Task ActualAnnouncementAndNotificationProducersPreserveRecipientAndResourceAuthority()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var memberClient = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
            using var ownerClient = await app.LoginAsync("owner", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAOwnerEmail);
            using var betaClient = await app.LoginAsync("beta", SecurityCiFixtureSeed.TenantBSlug, SecurityCiFixtureSeed.TenantBOwnerEmail);
            var scope = await SecurityArchitectureSignalRTests.ScopeAsync(database, SecurityCiFixtureSeed.TenantASlug);
            var controls = SecurityArchitectureSignalRControlRecorder.Create(GetType());
            var http = SecurityArchitectureHttpControlRecorder.Create(GetType(), "ACTUAL_TEST_WEB_ENTRY_POINT_AND_MIGRATED_POSTGRESQL");
            await using var member = await app.ConnectAsync(memberClient, "member", SecurityCiFixtureSeed.TenantASlug);
            await using var owner = await app.ConnectAsync(ownerClient, "owner", SecurityCiFixtureSeed.TenantASlug);
            await using var beta = await app.ConnectAsync(betaClient, "beta", SecurityCiFixtureSeed.TenantBSlug);
            await InvokeAsync(member, controls, "SubscribeUser");
            Assert.True(await owner.SubscribeAsync("SubscribeUser"));
            Assert.True(await beta.SubscribeAsync("SubscribeUser"));
            // Give the foreign connection its own actual authorized User
            // delivery before using it as an excluded peer for these producers.
            var betaScope = await SecurityArchitectureSignalRTests.ScopeAsync(database, SecurityCiFixtureSeed.TenantBSlug);
            await PrepareCanonicalDefaultAsync(database, betaScope);
            var betaBefore = await EventIdsAsync(database);
            using (var ownRole = await betaClient.PatchAsJsonAsync($"/api/projects/{betaScope.Project:D}/members/{SecurityCiFixtureSeed.TenantBOwnerUserId:D}", new { role = ProjectRole.Owner }))
                Assert.Equal(HttpStatusCode.OK, ownRole.StatusCode);
            var betaLive = await WaitAuthorizationAsync(database, app, beta, SecurityCiFixtureSeed.TenantBOwnerUserId, betaBefore);
            Assert.True(beta.Received(betaLive, "Security.AuthorizationStateChanged.v1"));
            Assert.True(await beta.SubscribeAsync("SubscribeUser"));

            Guid announcement;
            var before = await EventIdsAsync(database);
            using (var created = await ownerClient.PostAsJsonAsync("/api/announcements", new
                   { workspaceId = scope.Workspace, title = "Synthetic actual announcement", body = "Synthetic announcement body" }))
            {
                Assert.Equal(HttpStatusCode.OK, created.StatusCode);
                using var payload = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
                announcement = payload.RootElement.GetProperty("id").GetGuid();
            }
            var createdAnnouncement = await EventAsync(database, "Announcements.AnnouncementChanged.v1", announcement, before);
            await member.WaitEventAsync(createdAnnouncement);
            await owner.WaitEventAsync(createdAnnouncement);
            await app.WaitDeliveredAsync(createdAnnouncement);
            before = await EventIdsAsync(database);
            using (var read = await memberClient.PostAsync($"/api/announcements/{announcement:D}/read", null))
            {
                Assert.Equal(HttpStatusCode.OK, read.StatusCode);
                http.Observe(read, "/api/announcements/{announcementId}/read", "AUTHORIZED_SAME_SCOPE", HttpStatusCode.OK);
            }
            var readAnnouncement = await ObserveAsync(database, app, controls, "Announcements.AnnouncementChanged.v1", announcement,
                before, RealtimeSubscriptionType.User, member, Original, beta: beta);
            controls.ObserveIsolation("Announcements.AnnouncementChanged.v1", RealtimeSubscriptionType.User,
                "HTTP_DOMAIN_SAME_TENANT_RECIPIENT", member, readAnnouncement, owner, readAnnouncement);
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                Assert.Equal(1, await db.AnnouncementReads.CountAsync(item => item.AnnouncementId == announcement && item.UserId == SecurityCiFixtureSeed.TenantAMemberUserId));

            before = await EventIdsAsync(database);
            var notification = await MentionAsync(ownerClient, database, scope, SecurityCiFixtureSeed.TenantAMemberUserId);
            var createdNotification = await ObserveAsync(database, app, controls, "Notifications.NotificationCreated.v1", notification,
                before, RealtimeSubscriptionType.User, member, Original, beta: beta);
            controls.ObserveIsolation("Notifications.NotificationCreated.v1", RealtimeSubscriptionType.User,
                "HTTP_DOMAIN_SAME_TENANT_RECIPIENT", member, createdNotification, owner, createdNotification);
            before = await EventIdsAsync(database);
            await ReadNotificationAsync(memberClient, database, notification, http, "AUTHORIZED_SAME_SCOPE");
            var readNotification = await ObserveAsync(database, app, controls, "Notifications.NotificationReadStateChanged.v1", notification,
                before, RealtimeSubscriptionType.User, member, Original, beta: beta);
            controls.ObserveIsolation("Notifications.NotificationReadStateChanged.v1", RealtimeSubscriptionType.User,
                "HTTP_DOMAIN_SAME_TENANT_RECIPIENT", member, readNotification, owner, readNotification);

            // Prove the excluded recipient's same operation against its own
            // actual notification before asserting foreign ownership denial.
            before = await EventIdsAsync(database);
            var ownerNotification = await MentionAsync(memberClient, database, scope, SecurityCiFixtureSeed.TenantAOwnerUserId);
            await owner.WaitEventAsync(await EventAsync(database, "Notifications.NotificationCreated.v1", ownerNotification, before));
            await ReadNotificationAsync(ownerClient, database, ownerNotification);
            await AssertNotificationDeniedAsync(ownerClient, database, scope, notification, http, "SAME_TENANT_RESOURCE");
            before = await EventIdsAsync(database);
            var delayedRead = await MentionAsync(ownerClient, database, scope, SecurityCiFixtureSeed.TenantAMemberUserId);
            await member.WaitEventAsync(await EventAsync(database, "Notifications.NotificationCreated.v1", delayedRead, before));
            await SetConversationReadAsync(database, scope, false);
            await AssertNotificationDeniedAsync(memberClient, database, scope, delayedRead, http, "CURRENT_CONVERSATION_READ_DENIED");
            await SetConversationReadAsync(database, scope, true);
            before = await EventIdsAsync(database);
            await ReadNotificationAsync(memberClient, database, delayedRead, http, "AUTHORIZED_RESTORED_SCOPE");
            await ObserveAsync(database, app, controls, "Notifications.NotificationReadStateChanged.v1", delayedRead,
                before, RealtimeSubscriptionType.User, member, "HTTP_DOMAIN_RESTORED_NOTIFICATION_RESOURCE");
            before = await EventIdsAsync(database);
            notification = await MentionAsync(ownerClient, database, scope, SecurityCiFixtureSeed.TenantAMemberUserId);
            await ObserveAsync(database, app, controls, "Notifications.NotificationCreated.v1", notification,
                before, RealtimeSubscriptionType.User, member, "HTTP_DOMAIN_RESTORED_NOTIFICATION_RESOURCE");

            await SetWorkspaceMemberAsync(database, scope, MembershipStatus.Suspended);
            var unchanged = await CommunicationStateAsync(database, scope);
            using (var denied = await memberClient.PostAsync($"/api/announcements/{announcement:D}/read", null))
            {
                Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
                using var payload = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
                Assert.Equal("Announcement not found.", payload.RootElement.GetProperty("error").GetString());
                AssertStateEqual(unchanged, await CommunicationStateAsync(database, scope));
                http.Observe(denied, "/api/announcements/{announcementId}/read", "CURRENT_WORKSPACE_MEMBERSHIP_REVOKED",
                    HttpStatusCode.NotFound, responseAssertion: "ANNOUNCEMENT_HIDDEN_UNCHANGED_NOTIFICATION_READ_AUDIT_OUTBOX");
            }
            before = await EventIdsAsync(database);
            using (var updated = await ownerClient.PatchAsJsonAsync($"/api/announcements/{announcement:D}", new { body = "Synthetic current audience update" }))
                Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            await ObserveAsync(database, app, controls, "Announcements.AnnouncementChanged.v1", announcement,
                before, RealtimeSubscriptionType.User, owner, "HTTP_DOMAIN_CURRENT_ANNOUNCEMENT_AUDIENCE", member);
            // Audience is recomputed by the producer. This does not claim a
            // generic Announcement resource check for an arbitrary queued User event.
            await SetWorkspaceMemberAsync(database, scope, MembershipStatus.Active);
            before = await EventIdsAsync(database);
            using (var updated = await ownerClient.PatchAsJsonAsync($"/api/announcements/{announcement:D}", new { body = "Synthetic restored audience update" }))
                Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            await ObserveAsync(database, app, controls, "Announcements.AnnouncementChanged.v1", announcement,
                before, RealtimeSubscriptionType.User, member, "HTTP_DOMAIN_RESTORED_ANNOUNCEMENT_AUDIENCE");
            using (var restoredRead = await memberClient.PostAsync($"/api/announcements/{announcement:D}/read", null))
            {
                Assert.Equal(HttpStatusCode.OK, restoredRead.StatusCode);
                http.Observe(restoredRead, "/api/announcements/{announcementId}/read", "AUTHORIZED_RESTORED_SCOPE", HttpStatusCode.OK);
            }
            await controls.SaveAsync();
            await http.SaveAsync();
        });
    }

    private static async Task<Guid> MentionAsync(HttpClient client, string database, Scope scope, Guid recipient)
    {
        using var response = await client.PostAsJsonAsync($"/api/conversations/{scope.Conversation:D}/messages", new
        {
            body = "Synthetic producer mention " + Guid.NewGuid().ToString("N"),
            clientRequestId = Guid.NewGuid(), mentionedUserIds = new[] { recipient }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var message = payload.RootElement.GetProperty("id").GetGuid();
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        return await db.Notifications.AsNoTracking().Where(item => item.RelatedEntityType == "Message" && item.RelatedEntityId == message && item.UserId == recipient)
            .Select(item => item.Id).SingleAsync();
    }

    private static async Task ReadNotificationAsync(HttpClient client, string database, Guid notification,
        SecurityArchitectureHttpControlRecorder? http = null, string control = "AUTHORIZED_SAME_SCOPE")
    {
        long version;
        await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            version = await db.Notifications.AsNoTracking().Where(item => item.Id == notification).Select(item => item.StateVersion).SingleAsync();
        using var response = await client.PatchAsync($"/api/notifications/{notification:D}/read", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
        {
            var saved = await db.Notifications.AsNoTracking().SingleAsync(item => item.Id == notification);
            Assert.True(saved.IsRead);
            Assert.NotNull(saved.ReadAt);
            Assert.True(saved.StateVersion > version);
        }
        http?.Observe(response, "/api/notifications/{notificationId}/read", control, HttpStatusCode.OK);
    }

    private static async Task AssertNotificationDeniedAsync(HttpClient client, string database, Scope scope, Guid notification,
        SecurityArchitectureHttpControlRecorder http, string control)
    {
        var before = await CommunicationStateAsync(database, scope);
        using var response = await client.PatchAsync($"/api/notifications/{notification:D}/read", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("NotificationUpdateFailed", payload.RootElement.GetProperty("error").GetProperty("code").GetString());
        AssertStateEqual(before, await CommunicationStateAsync(database, scope));
        http.Observe(response, "/api/notifications/{notificationId}/read", control, HttpStatusCode.BadRequest, "NotificationUpdateFailed",
            "UNCHANGED_NOTIFICATION_READ_AUDIT_OUTBOX");
    }

    private static async Task SetConversationReadAsync(string database, Scope scope, bool allowed)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var member = await db.ConversationMembers.SingleAsync(item => item.ConversationId == scope.Conversation && item.UserId == SecurityCiFixtureSeed.TenantAMemberUserId);
        member.CanRead = allowed;
        member.CanPost = allowed;
        await db.SaveChangesAsync();
    }

    private static async Task SetWorkspaceMemberAsync(string database, Scope scope, MembershipStatus status)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var member = await db.WorkspaceMembers.SingleAsync(item => item.WorkspaceId == scope.Workspace && item.UserId == SecurityCiFixtureSeed.TenantAMemberUserId);
        member.Status = status;
        await db.SaveChangesAsync();
    }

    private static async Task<StateSnapshot> CommunicationStateAsync(string database, Scope scope)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var notifications = await db.Notifications.AsNoTracking().Where(item => item.TenantId == scope.Tenant).OrderBy(item => item.Id)
            .Select(item => new { item.Id, item.UserId, item.RelatedEntityId, item.IsRead, item.ReadAt, item.StateVersion, item.DeletedAt }).ToArrayAsync();
        var states = await db.NotificationUserStates.AsNoTracking().Where(item => item.TenantId == scope.Tenant).OrderBy(item => item.Id)
            .Select(item => new { item.Id, item.UserId, item.Version }).ToArrayAsync();
        var reads = await db.AnnouncementReads.AsNoTracking().Where(item => item.TenantId == scope.Tenant).OrderBy(item => item.Id)
            .Select(item => new { item.Id, item.AnnouncementId, item.UserId, item.ReadAt }).ToArrayAsync();
        var digest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { notifications, states, reads })));
        return new(digest, await db.OutboxEvents.LongCountAsync(), await db.AuditLogs.LongCountAsync());
    }

    private static Task InvokeAsync(RealtimeSocket socket, SecurityArchitectureSignalRControlRecorder controls, string method, params object[] arguments) =>
        SecurityArchitectureSignalREventTests.InvokeWithReceiptAsync(socket, controls, method, true, "Subscribed", arguments);

    private static async Task<Guid[]> EventIdsAsync(string database)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        return await db.OutboxEvents.AsNoTracking().Select(item => item.Id).ToArrayAsync();
    }

    private static async Task<Guid> EventAsync(string database, string eventType, Guid aggregate, Guid[] before)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        return await db.OutboxEvents.AsNoTracking().Where(item => item.EventType == eventType && item.AggregateId == aggregate && !before.Contains(item.Id))
            .Select(item => item.Id).SingleAsync();
    }

    private static async Task<Guid> WaitAuthorizationAsync(string database, SecurityArchitectureSignalRFixture app,
        RealtimeSocket recipient, Guid aggregate, Guid[] before)
    {
        Guid[] ids;
        await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            ids = await db.OutboxEvents.AsNoTracking().Where(item => item.EventType == "Security.AuthorizationStateChanged.v1" &&
                item.AggregateId == aggregate && !before.Contains(item.Id)).Select(item => item.Id).Take(3).ToArrayAsync();
        Assert.Equal(2, ids.Length);
        // The first current invalidation clears all of the user's routes. Both
        // mutations must finish before an explicitly authorized re-subscription;
        // only the actually received event supplies delivery credit.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (!ids.Any(id => recipient.Received(id, "Security.AuthorizationStateChanged.v1")) && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(50);
        var received = ids.Where(id => recipient.Received(id, "Security.AuthorizationStateChanged.v1")).ToArray();
        Assert.NotEmpty(received);
        foreach (var id in ids) await app.WaitDeliveredAsync(id);
        return received[0];
    }

    private static async Task PrepareCanonicalDefaultAsync(string database, Scope scope)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var conversation = await db.Conversations.SingleAsync(item => item.Id == scope.Conversation);
        // The original SEC-02 graph predates canonical ProjectGeneral. Supply
        // that prerequisite only on this newly added test-owned Conversation,
        // matching the existing provisioner and synchronizer identity checks.
        conversation.Title = "general";
        conversation.Visibility = ConversationVisibility.PublicWithinScope;
        conversation.DefaultKind = ConversationDefaultKind.ProjectGeneral;
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> ObserveAsync(string database, SecurityArchitectureSignalRFixture app,
        SecurityArchitectureSignalRControlRecorder controls, string eventType, Guid aggregate, Guid[] before,
        RealtimeSubscriptionType target, RealtimeSocket recipient, string control, RealtimeSocket? excluded = null, RealtimeSocket? beta = null)
    {
        var id = eventType == "Security.AuthorizationStateChanged.v1"
            ? await WaitAuthorizationAsync(database, app, recipient, aggregate, before)
            : await EventAsync(database, eventType, aggregate, before);
        await recipient.WaitEventAsync(id);
        await app.WaitDeliveredAsync(id);
        if (excluded is null) controls.ObservePositive(eventType, target, control, recipient, id);
        else controls.ObserveIsolation(eventType, target, control, recipient, id, excluded, id);
        if (beta is not null) controls.ObserveIsolation(eventType, target, "HTTP_DOMAIN_CROSS_TENANT", recipient, id, beta, id);
        return id;
    }

    private static async Task<long> VersionAsync(string database, Guid task)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        return await db.TaskItems.AsNoTracking().Where(item => item.Id == task).Select(item => item.VersionNo).SingleAsync();
    }

    private static async Task<object> TaskBodyAsync(string database, Guid task) => new
    {
        title = "Synthetic actual Task producer " + Guid.NewGuid().ToString("N"), priority = TaskPriority.Medium,
        progressPercent = 0, expectedVersion = await VersionAsync(database, task)
    };

    private static async Task UpdateTaskAsync(HttpClient client, string database, Guid task,
        SecurityArchitectureHttpControlRecorder? http = null, string control = "AUTHORIZED_SAME_SCOPE")
    {
        var version = await VersionAsync(database, task);
        using var response = await client.PatchAsJsonAsync($"/api/tasks/{task:D}", await TaskBodyAsync(database, task));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(version + 1, await VersionAsync(database, task));
        http?.Observe(response, TaskRoute, control, HttpStatusCode.OK);
    }

    private static async Task SetAssigneeAsync(HttpClient client, string database, Guid task, Guid assignee,
        SecurityArchitectureHttpControlRecorder? http = null, string control = "AUTHORIZED_SAME_SCOPE")
    {
        var version = await VersionAsync(database, task);
        using var response = await client.PutAsJsonAsync($"/api/tasks/{task:D}/assignee", new { userId = assignee, expectedVersion = version });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var saved = await db.TaskItems.AsNoTracking().SingleAsync(item => item.Id == task);
        Assert.Equal(assignee, saved.PrimaryAssigneeUserId);
        Assert.Equal(version + 1, saved.VersionNo);
        http?.Observe(response, AssigneeRoute, control, HttpStatusCode.OK);
    }

    private static async Task CommentAsync(HttpClient client, Guid task,
        SecurityArchitectureHttpControlRecorder? http = null, string control = "AUTHORIZED_SAME_SCOPE")
    {
        using var response = await client.PostAsJsonAsync($"/api/tasks/{task:D}/comments", new { bodyPlainText = "Synthetic actual comment " + Guid.NewGuid().ToString("N") });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.NotEqual(Guid.Empty, payload.RootElement.GetProperty("id").GetGuid());
        http?.Observe(response, CommentRoute, control, HttpStatusCode.OK);
    }

    private static async Task UpdateProjectAsync(HttpClient client, Scope scope,
        SecurityArchitectureHttpControlRecorder? http = null, string control = "AUTHORIZED_SAME_SCOPE")
    {
        var description = "Synthetic actual Project producer " + Guid.NewGuid().ToString("N");
        using var response = await client.PatchAsJsonAsync($"/api/projects/{scope.Project:D}", new { description });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(description, payload.RootElement.GetProperty("description").GetString());
        http?.Observe(response, ProjectRoute, control, HttpStatusCode.OK);
    }

    private static Task<HttpResponseMessage> UploadResponseAsync(HttpClient client, Guid task)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(((int)AttachmentOwnerType.TaskItem).ToString(System.Globalization.CultureInfo.InvariantCulture)), "OwnerType");
        form.Add(new StringContent(task.ToString("D")), "OwnerId");
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("Synthetic SEC-ARCH actual producer file"));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        form.Add(content, "File", "synthetic.txt");
        return SendAsync();
        async Task<HttpResponseMessage> SendAsync()
        {
            using (form) return await client.PostAsync("/api/files", form);
        }
    }

    private static async Task<Guid> UploadAsync(HttpClient client, Guid task,
        SecurityArchitectureHttpControlRecorder? http = null, string control = "AUTHORIZED_SAME_SCOPE")
    {
        using var response = await UploadResponseAsync(client, task);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var file = payload.RootElement.GetProperty("fileObjectId").GetGuid();
        Assert.NotEqual(Guid.Empty, file);
        http?.Observe(response, "/api/files", control, HttpStatusCode.OK);
        return file;
    }

    private static async Task AssertDeniedAsync(HttpClient client, string database, Scope scope,
        SecurityArchitectureHttpControlRecorder http, HttpMethod method, string path, string route, object body,
        HttpStatusCode status, string code, bool legacy = false)
    {
        var before = await StateAsync(database, scope);
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        using var response = await client.SendAsync(request);
        Assert.Equal(status, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, (legacy ? payload.RootElement : payload.RootElement.GetProperty("error")).GetProperty("code").GetString());
        if (legacy) Assert.Equal("You are not allowed to manage this project.", payload.RootElement.GetProperty("message").GetString());
        AssertStateEqual(before, await StateAsync(database, scope));
        http.Observe(response, route, "CURRENT_PROJECT_MEMBERSHIP_REVOKED", status, code,
            "UNCHANGED_PROJECT_TASK_RELATIONSHIPS_COMMENTS_FILES_AUDIT_OUTBOX");
    }

    private static async Task<StateSnapshot> StateAsync(string database, Scope scope)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var project = await db.Projects.AsNoTracking().Where(item => item.Id == scope.Project)
            .Select(item => new { item.Id, item.Name, item.Description, item.VersionNo, item.Status }).SingleAsync();
        var tasks = await db.TaskItems.AsNoTracking().Where(item => item.ProjectId == scope.Project).OrderBy(item => item.Id)
            .Select(item => new { item.Id, item.Title, item.Description, item.VersionNo, item.PrimaryAssigneeUserId, item.ReviewerUserId,
                item.Priority, item.ProgressPercent, item.PlannedStartDate, item.PlannedEndDate }).ToArrayAsync();
        var assignments = await db.TaskAssignments.AsNoTracking().Where(item => item.TaskItem!.ProjectId == scope.Project).OrderBy(item => item.Id)
            .Select(item => new { item.Id, item.TaskItemId, item.UserId, item.Role }).ToArrayAsync();
        var comments = await db.TaskComments.AsNoTracking().Where(item => item.ProjectId == scope.Project).OrderBy(item => item.Id)
            .Select(item => new { item.Id, item.TaskItemId, item.AuthorUserId, item.BodyPlainText, item.VersionNo }).ToArrayAsync();
        var files = await db.FileObjects.AsNoTracking().Where(item => item.ProjectId == scope.Project).OrderBy(item => item.Id)
            .Select(item => new { item.Id, item.Status, item.SizeBytes, item.UploadedByUserId }).ToArrayAsync();
        var digest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { project, tasks, assignments, comments, files })));
        return new(digest, await db.OutboxEvents.LongCountAsync(), await db.AuditLogs.LongCountAsync());
    }

    private sealed record StateSnapshot(string Digest, long OutboxCount, long AuditCount);

    private static void AssertStateEqual(StateSnapshot expected, StateSnapshot actual)
    {
        Assert.Equal(expected.Digest, actual.Digest);
        Assert.Equal(expected.OutboxCount, actual.OutboxCount);
        Assert.Equal(expected.AuditCount, actual.AuditCount);
    }
}
