using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coglatas.Application.Common;
using Coglatas.Application.Messaging;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Web.Configuration;
using Coglatas.Web.Controllers;
using Microsoft.EntityFrameworkCore;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;

namespace Coglatas.Tests.PostgreSql;

public sealed partial class GroupConversationHostedHttpTests
{
    [PostgreSqlFact]
    public Task MembershipManagersMayRemoveAdministratorsAndReaddWithFullHistoryButNoInheritedManagement() => WithScenarioAsync(async scenario =>
    {
        var group = await CreateGroupAsync(scenario);
        var message = await SendAsync(scenario.Host.Client, group.Id);
        await scenario.WithDbAsync(async db =>
        {
            await db.ConversationMembers.Where(member => member.ConversationId == group.Id && member.UserId == scenario.Host.Recipient.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(member => member.Role, ConversationMemberRole.ReadOnly)
                    .SetProperty(member => member.CanManageMembers, true));
        });
        using (var send = await AdmissionHost.PostAsync(scenario.RecipientClient, $"/api/conversations/{group.Id}/messages", new SendMessageRequest("ReadOnly must not post")))
            Assert.Equal(HttpStatusCode.Forbidden, send.StatusCode);
        using (var removed = await ChangeAsync(scenario.RecipientClient, HttpMethod.Delete, $"/api/conversations/{group.Id}/members/{scenario.Host.Actor.Id}"))
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        await AssertHiddenAsync(scenario.Host.Client, group.Id, message.Body);
        DateTimeOffset removedAt = default;
        await scenario.WithDbAsync(async db =>
        {
            var stored = await db.ConversationMembers.AsNoTracking().SingleAsync(member => member.ConversationId == group.Id && member.UserId == scenario.Host.Actor.Id);
            Assert.Equal(ConversationMemberRole.Admin, stored.Role);
            Assert.NotNull(stored.RemovedAt);
            Assert.Equal(scenario.Host.Recipient.Id, stored.RemovedByUserId);
            removedAt = stored.RemovedAt.Value;
        });
        using (var repeated = await ChangeAsync(scenario.RecipientClient, HttpMethod.Delete, $"/api/conversations/{group.Id}/members/{scenario.Host.Actor.Id}"))
            Assert.Equal(HttpStatusCode.BadRequest, repeated.StatusCode);
        await scenario.WithDbAsync(async db => Assert.Equal(removedAt,
            await db.ConversationMembers.Where(member => member.ConversationId == group.Id && member.UserId == scenario.Host.Actor.Id).Select(member => member.RemovedAt).SingleAsync()));
        using (var readded = await AdmissionHost.PostAsync(scenario.RecipientClient, $"/api/conversations/{group.Id}/members", new AddConversationMemberRequest(scenario.Host.Actor.Id)))
        {
            Assert.Equal(HttpStatusCode.OK, readded.StatusCode);
            var member = (await readded.Content.ReadFromJsonAsync<ConversationMemberResponse>())!;
            Assert.Equal(ConversationMemberRole.Member, member.Role);
            Assert.False(member.CanManageMembers);
            Assert.False(member.CanCreateThread);
            Assert.Null(member.LeftAt);
            Assert.Null(member.RemovedAt);
        }
        using (var history = await scenario.Host.Client.GetAsync($"/api/conversations/{group.Id}/messages?anchorMessageId={message.Id}"))
        {
            Assert.Equal(HttpStatusCode.OK, history.StatusCode);
            Assert.Contains(message.Body, await history.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        using (var duplicate = await AdmissionHost.PostAsync(scenario.RecipientClient, $"/api/conversations/{group.Id}/members", new AddConversationMemberRequest(scenario.Host.Actor.Id)))
            Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        using (var ordinaryManage = await AdmissionHost.PostAsync(scenario.Host.Client, $"/api/conversations/{group.Id}/members", new AddConversationMemberRequest(scenario.Third.Id)))
            Assert.Equal(HttpStatusCode.BadRequest, ordinaryManage.StatusCode);
        using (var selfRemoved = await ChangeAsync(scenario.RecipientClient, HttpMethod.Delete, $"/api/conversations/{group.Id}/members/{scenario.Host.Recipient.Id}"))
            Assert.Equal(HttpStatusCode.OK, selfRemoved.StatusCode);
        using (var members = await scenario.ThirdClient.GetAsync($"/api/conversations/{group.Id}/members"))
        {
            Assert.Equal(HttpStatusCode.OK, members.StatusCode);
            var historical = (await members.Content.ReadFromJsonAsync<ConversationMemberResponse[]>())!;
            Assert.Equal(3, historical.Length);
            var formerManager = Assert.Single(historical, member => member.UserId == scenario.Host.Recipient.Id);
            Assert.Equal(scenario.Host.Recipient.Email, formerManager.Email);
            Assert.Equal(ConversationMemberRole.ReadOnly, formerManager.Role);
            Assert.NotNull(formerManager.RemovedAt);
        }
        await scenario.WithDbAsync(async db => Assert.Equal(ConversationType.Group,
            await db.Conversations.Where(conversation => conversation.Id == group.Id).Select(conversation => conversation.Type).SingleAsync()));
    });

    [PostgreSqlFact]
    public Task SoleAdministratorMayLeaveActiveWorkspaceGroupWithoutPromotingRemainingMembers() => WithScenarioAsync(async scenario =>
    {
        var group = await CreateGroupAsync(scenario);
        var groupId = group.Id;
        var workspaceId = group.WorkspaceId;
        var actorId = scenario.Host.Actor.Id;
        var recipientId = scenario.Host.Recipient.Id;
        var thirdId = scenario.Third.Id;
        await scenario.WithDbAsync(async db =>
        {
            Assert.Equal(WorkspaceStatus.Active, await db.Workspaces.Where(workspace => workspace.Id == workspaceId).Select(workspace => workspace.Status).SingleAsync());
            var members = await db.ConversationMembers.AsNoTracking().Where(member => member.ConversationId == groupId).ToListAsync();
            Assert.Equal(3, members.Count);
            Assert.All(members, member => { Assert.Null(member.LeftAt); Assert.Null(member.RemovedAt); });
            var manager = Assert.Single(members, member => member.Role == ConversationMemberRole.Admin || member.CanManageMembers);
            Assert.Equal(actorId, manager.UserId);
            Assert.Equal(ConversationMemberRole.Admin, manager.Role);
        });
        var original = await SendAsync(scenario.Host.Client, groupId);

        using (var left = await AdmissionHost.PostAsync(scenario.Host.Client, $"/api/conversations/{groupId}/leave", new { }))
            Assert.Equal(HttpStatusCode.OK, left.StatusCode);

        await scenario.WithDbAsync(async db =>
        {
            Assert.Equal(WorkspaceStatus.Active, await db.Workspaces.Where(workspace => workspace.Id == workspaceId).Select(workspace => workspace.Status).SingleAsync());
            Assert.Equal(MembershipStatus.Active, await db.WorkspaceMembers.Where(member => member.WorkspaceId == workspaceId && member.UserId == actorId).Select(member => member.Status).SingleAsync());
            var stored = await db.Conversations.AsNoTracking().SingleAsync(conversation => conversation.Id == groupId);
            Assert.Equal(ConversationType.Group, stored.Type);
            Assert.Equal(workspaceId, stored.WorkspaceId);
            var members = await db.ConversationMembers.AsNoTracking().Where(member => member.ConversationId == groupId).ToListAsync();
            Assert.Equal(3, members.Count);
            var formerAdmin = Assert.Single(members, member => member.UserId == actorId);
            Assert.NotNull(formerAdmin.LeftAt);
            Assert.Null(formerAdmin.RemovedAt);
            var remaining = members.Where(member => member.LeftAt == null && member.RemovedAt == null).ToList();
            Assert.Equal(2, remaining.Count);
            Assert.Contains(remaining, member => member.UserId == recipientId);
            Assert.Contains(remaining, member => member.UserId == thirdId);
            Assert.All(remaining, member =>
            {
                Assert.Equal(ConversationMemberRole.Member, member.Role);
                Assert.False(member.CanManageMembers);
            });
        });
        await AssertHiddenAsync(scenario.Host.Client, groupId, original.Body);
        using var history = await scenario.RecipientClient.GetAsync($"/api/conversations/{groupId}/messages");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var page = await history.Content.ReadFromJsonAsync<PagedResponse<MessageResponse>>();
        Assert.NotNull(page);
        var retained = Assert.Single(page.Items);
        Assert.Equal(original.Id, retained.Id);
        Assert.Equal(original.Body, retained.Body);
        Assert.Equal(actorId, retained.AuthorUserId);
        Assert.Equal(original.ClientRequestId, retained.ClientRequestId);
    });

    [PostgreSqlFact]
    public Task ArchivedWorkspaceKeepsPrivateHistoryAndDirectPostingRulesButRequiresActiveScopeForMembershipChanges() => WithScenarioAsync(async scenario =>
    {
        var group = await CreateGroupAsync(scenario);
        var original = await SendAsync(scenario.Host.Client, group.Id);
        var selfRemoval = await CreateGroupAsync(scenario);
        using (var selfRemoved = await ChangeAsync(scenario.Host.Client, HttpMethod.Delete, $"/api/conversations/{selfRemoval.Id}/members/{scenario.Host.Actor.Id}"))
            Assert.Equal(HttpStatusCode.OK, selfRemoved.StatusCode);
        await scenario.WithDbAsync(async db =>
        {
            await db.Workspaces.Where(workspace => workspace.Id == group.WorkspaceId).ExecuteUpdateAsync(update => update.SetProperty(workspace => workspace.Status, WorkspaceStatus.Archived));
            await db.WorkspaceMembers.Where(member => member.WorkspaceId == group.WorkspaceId && member.UserId == scenario.Host.Actor.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(member => member.Role, WorkspaceRole.ReadOnly));
        });
        using (var history = await scenario.RecipientClient.GetAsync($"/api/conversations/{group.Id}/messages"))
        {
            Assert.Equal(HttpStatusCode.OK, history.StatusCode);
            Assert.Contains(original.Body, await history.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        await SendAsync(scenario.Host.Client, group.Id);
        using (var remove = await ChangeAsync(scenario.Host.Client, HttpMethod.Delete, $"/api/conversations/{group.Id}/members/{scenario.Third.Id}"))
            Assert.Equal(HttpStatusCode.BadRequest, remove.StatusCode);
        using (var add = await AdmissionHost.PostAsync(scenario.Host.Client, $"/api/conversations/{group.Id}/members", new AddConversationMemberRequest(scenario.Third.Id)))
            Assert.Equal(HttpStatusCode.BadRequest, add.StatusCode);
        using (var left = await AdmissionHost.PostAsync(scenario.Host.Client, $"/api/conversations/{group.Id}/leave", new { }))
            Assert.Equal(HttpStatusCode.OK, left.StatusCode);
        using (var repeated = await AdmissionHost.PostAsync(scenario.Host.Client, $"/api/conversations/{group.Id}/leave", new { }))
            Assert.Equal(HttpStatusCode.BadRequest, repeated.StatusCode);
        await AssertHiddenAsync(scenario.Host.Client, group.Id, original.Body);
        await scenario.WithDbAsync(async db =>
        {
            Assert.Equal(ConversationType.Group, await db.Conversations.Where(row => row.Id == group.Id).Select(row => row.Type).SingleAsync());
            Assert.Equal(2, await db.ConversationMembers.CountAsync(member => member.ConversationId == group.Id && member.LeftAt == null && member.RemovedAt == null));
            Assert.False(await db.ConversationMembers.AnyAsync(member => member.ConversationId == group.Id && member.LeftAt == null && member.RemovedAt == null && (member.Role == ConversationMemberRole.Admin || member.CanManageMembers)));
        });
        foreach (var client in new[] { scenario.RecipientClient, scenario.ThirdClient })
        {
            using var left = await AdmissionHost.PostAsync(client, $"/api/conversations/{group.Id}/leave", new { });
            Assert.Equal(HttpStatusCode.OK, left.StatusCode);
        }
        await scenario.WithDbAsync(async db =>
        {
            Assert.Equal(ConversationType.Group, await db.Conversations.Where(row => row.Id == group.Id).Select(row => row.Type).SingleAsync());
            Assert.Equal(0, await db.ConversationMembers.CountAsync(member => member.ConversationId == group.Id && member.LeftAt == null && member.RemovedAt == null));
        });
    });

    [PostgreSqlFact]
    public Task LegacyThreadFlagsDoNotActivateGroupThreadsOrModerationAndReadsDoNotRewriteMembers() => WithScenarioAsync(async scenario =>
    {
        var group = await CreateGroupAsync(scenario);
        var original = await SendAsync(scenario.RecipientClient, group.Id);
        Guid legacyThread = Guid.Empty;
        await scenario.WithDbAsync(async db =>
        {
            await db.ConversationMembers.Where(member => member.ConversationId == group.Id).ExecuteUpdateAsync(update => update.SetProperty(member => member.CanCreateThread, true));
            var thread = new Conversation { WorkspaceId = group.WorkspaceId, Type = ConversationType.Thread, ParentConversationId = group.Id, RootConversationId = group.Id, CreatedByUserId = scenario.Host.Actor.Id };
            db.Conversations.Add(thread);
            db.ConversationMembers.Add(new ConversationMember { ConversationId = thread.Id, UserId = scenario.Host.Actor.Id, Role = ConversationMemberRole.Admin, CanRead = true, CanPost = true, CanCreateThread = true, JoinedAt = DateTimeOffset.UtcNow });
            db.Messages.Add(new Message { WorkspaceId = group.WorkspaceId, ConversationId = group.Id, AuthorUserId = scenario.Host.Actor.Id, ThreadRootMessageId = original.Id, Body = "Held legacy reply marker" });
            await db.SaveChangesAsync();
            legacyThread = thread.Id;
        });
        using (var detail = await scenario.Host.Client.GetAsync($"/api/conversations/{group.Id}"))
        {
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            Assert.All((await detail.Content.ReadFromJsonAsync<ConversationDetailResponse>())!.Members, member => Assert.False(member.CanCreateThread));
        }
        using (var members = await scenario.Host.Client.GetAsync($"/api/conversations/{group.Id}/members"))
            Assert.All((await members.Content.ReadFromJsonAsync<ConversationMemberResponse[]>())!, member => Assert.False(member.CanCreateThread));
        foreach (var route in new[] { $"/api/messages/{original.Id}/thread", $"/api/conversations/{legacyThread}", $"/api/conversations/{legacyThread}/messages" })
        {
            using var denied = await scenario.Host.Client.GetAsync(route);
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            Assert.DoesNotContain("Held legacy reply marker", await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        foreach (var operation in new[] { "lock", "unlock", "archive" })
        {
            using var denied = await AdmissionHost.PostAsync(scenario.Host.Client, $"/api/conversations/{group.Id}/{operation}", new { });
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        }
        using (var update = await ChangeAsync(scenario.Host.Client, HttpMethod.Patch, $"/api/conversations/{group.Id}", new UpdateConversationRequest("Denied rename")))
            Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        using (var thread = await scenario.Host.PostAsync("/api/conversations", new CreateConversationRequest(ConversationType.Thread, "Denied child", [], group.WorkspaceId, ParentConversationId: group.Id)))
            Assert.Equal(HttpStatusCode.BadRequest, thread.StatusCode);
        using (var reply = await AdmissionHost.PostAsync(scenario.Host.Client, $"/api/messages/{original.Id}/thread/messages", new SendThreadMessageRequest("Denied reply despite existing reply")))
            Assert.Equal(HttpStatusCode.BadRequest, reply.StatusCode);
        using (var moderateOther = await ChangeAsync(scenario.Host.Client, HttpMethod.Delete, $"/api/messages/{original.Id}"))
            Assert.Equal(HttpStatusCode.BadRequest, moderateOther.StatusCode);
        await scenario.WithDbAsync(async db => await db.ConversationMembers.Where(row => row.ConversationId == group.Id && row.UserId == scenario.Host.Actor.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Role, ConversationMemberRole.ReadOnly).SetProperty(row => row.CanPost, false)));
        using (var report = await AdmissionHost.PostAsync(scenario.Host.Client, $"/api/messages/{original.Id}/report", new MessageReportRequest("spam")))
            Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        using (var report = await AdmissionHost.PostAsync(scenario.Host.Client, $"/api/conversations/{group.Id}/report", new ConversationReportRequest("spam")))
            Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        using (var edit = await ChangeAsync(scenario.RecipientClient, HttpMethod.Patch, $"/api/messages/{original.Id}", new UpdateMessageRequest("Author correction")))
            Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        using (var authorDelete = await ChangeAsync(scenario.RecipientClient, HttpMethod.Delete, $"/api/messages/{original.Id}"))
            Assert.Equal(HttpStatusCode.OK, authorDelete.StatusCode);
        await scenario.WithDbAsync(async db =>
        {
            Assert.True(await db.ConversationMembers.Where(member => member.ConversationId == group.Id).AllAsync(member => member.CanCreateThread));
            var stored = await db.Conversations.AsNoTracking().SingleAsync(row => row.Id == group.Id);
            Assert.Equal(group.Title, stored.Title);
            Assert.False(stored.IsLocked);
            Assert.False(stored.IsArchived);
            var repo = new MessagingRepository(db);
            var readable = await repo.FilterReadableConversationIdsAsync(scenario.Host.Actor.Id, [group.Id, legacyThread]);
            Assert.Contains(group.Id, readable);
            Assert.DoesNotContain(legacyThread, readable);
        });
    });

    [PostgreSqlFact]
    public Task CommittedParticipantAndWorkspaceRevocationDenyBodyReplayAndLeaveWithoutChangingSuccessState() => WithScenarioAsync(async scenario =>
    {
        var group = await CreateGroupAsync(scenario);
        var original = await SendAsync(scenario.RecipientClient, group.Id);
        var expectedMessages = 1;
        foreach (var boundary in new[] { "participant-removed", "workspace-suspended" })
        {
            await scenario.WithDbAsync(async db =>
            {
                if (boundary == "participant-removed")
                    await db.ConversationMembers.Where(member => member.ConversationId == group.Id && member.UserId == scenario.Host.Recipient.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(member => member.RemovedAt, DateTimeOffset.UtcNow));
                else
                {
                    await db.ConversationMembers.Where(member => member.ConversationId == group.Id && member.UserId == scenario.Host.Recipient.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(member => member.RemovedAt, (DateTimeOffset?)null));
                    await db.WorkspaceMembers.Where(member => member.WorkspaceId == group.WorkspaceId && member.UserId == scenario.Host.Recipient.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(member => member.Status, MembershipStatus.Suspended));
                }
                var participant = await db.ConversationMembers.AsNoTracking().SingleAsync(member => member.ConversationId == group.Id && member.UserId == scenario.Host.Recipient.Id);
                Assert.Null(participant.LeftAt);
                Assert.True(participant.CanRead);
                Assert.True(participant.CanPost);
            });
            await AssertHiddenAsync(scenario.RecipientClient, group.Id, original.Body);
            foreach (var command in new[] { new SendMessageRequest(original.Body, ClientRequestId: original.ClientRequestId), new SendMessageRequest("Denied new Group send") })
            {
                using var send = await AdmissionHost.PostAsync(scenario.RecipientClient, $"/api/conversations/{group.Id}/messages", command);
                Assert.Equal(HttpStatusCode.BadRequest, send.StatusCode);
                Assert.DoesNotContain(original.Body, await send.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
            using (var leave = await AdmissionHost.PostAsync(scenario.RecipientClient, $"/api/conversations/{group.Id}/leave", new { }))
                Assert.Equal(HttpStatusCode.BadRequest, leave.StatusCode);
            await scenario.WithDbAsync(async db =>
            {
                Assert.Null(await db.ConversationMembers.Where(member => member.ConversationId == group.Id && member.UserId == scenario.Host.Recipient.Id).Select(member => member.LeftAt).SingleAsync());
                Assert.Equal(expectedMessages, await db.Messages.CountAsync(message => message.ConversationId == group.Id));
            });
            await SendAsync(scenario.Host.Client, group.Id);
            expectedMessages++;
        }
    });

    [PostgreSqlFact]
    public Task CookieAndCsrfGuardsApplyBeforeGroupCreationAndAuthenticatedWrites() => WithScenarioAsync(async scenario =>
    {
        using var handler = new HttpClientHandler();
        handler.AllowAutoRedirect = false;
        handler.CookieContainer = new CookieContainer();
        using var anonymous = new HttpClient(handler);
        anonymous.BaseAddress = scenario.Host.Client.BaseAddress;
        anonymous.DefaultRequestHeaders.Add("X-Tenant-Slug", scenario.Host.Tenant.Slug);
        using (var create = await AdmissionHost.PostAsync(anonymous, "/api/conversations", new { type = 7, workspaceId = scenario.Host.Workspace.Id, memberUserIds = new[] { scenario.Host.Recipient.Id, scenario.Third.Id } }))
            Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        using (var missingToken = await scenario.Host.PostAsync("/api/conversations", new { type = 7, workspaceId = scenario.Host.Workspace.Id, memberUserIds = new[] { scenario.Host.Recipient.Id, scenario.Third.Id } }, includeCsrf: false))
            Assert.Equal(HttpStatusCode.Forbidden, missingToken.StatusCode);
        await scenario.Host.AssertStateAsync(0, 0);
        var group = await CreateGroupAsync(scenario);
        using (var send = await AdmissionHost.PostAsync(scenario.Host.Client, $"/api/conversations/{group.Id}/messages", new SendMessageRequest("Missing CSRF body"), includeCsrf: false))
            Assert.Equal(HttpStatusCode.Forbidden, send.StatusCode);
        using (var add = await AdmissionHost.PostAsync(scenario.Host.Client, $"/api/conversations/{group.Id}/members", new AddConversationMemberRequest(scenario.Third.Id), includeCsrf: false))
            Assert.Equal(HttpStatusCode.Forbidden, add.StatusCode);
        using (var remove = await scenario.Host.Client.DeleteAsync($"/api/conversations/{group.Id}/members/{scenario.Third.Id}"))
            Assert.Equal(HttpStatusCode.Forbidden, remove.StatusCode);
        using (var leave = await AdmissionHost.PostAsync(scenario.Host.Client, $"/api/conversations/{group.Id}/leave", new { }, includeCsrf: false))
            Assert.Equal(HttpStatusCode.Forbidden, leave.StatusCode);
        await scenario.WithDbAsync(async db =>
        {
            Assert.Empty(await db.Messages.ToListAsync());
            Assert.Equal(3, await db.ConversationMembers.CountAsync());
            Assert.False(await db.ConversationMembers.AnyAsync(member => member.LeftAt != null || member.RemovedAt != null));
        });
        await SendAsync(scenario.Host.Client, group.Id);
    });

    private static async Task<ConversationDetailResponse> CreateGroupAsync(Scenario scenario)
    {
        using var response = await scenario.Host.PostAsync("/api/conversations", new CreateConversationRequest(
            ConversationType.Group, "Synthetic private Group", [scenario.Host.Recipient.Id, scenario.Third.Id], scenario.Host.Workspace.Id));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ConversationDetailResponse>())!;
    }

    private static async Task<MessageResponse> SendAsync(HttpClient client, Guid conversationId)
    {
        using var response = await AdmissionHost.PostAsync(client, $"/api/conversations/{conversationId}/messages", new SendMessageRequest("Group protected marker " + Guid.NewGuid().ToString("N"), ClientRequestId: Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MessageResponse>())!;
    }

    private static async Task AssertHiddenAsync(HttpClient client, Guid group, string marker)
    {
        foreach (var route in new[] { $"/api/conversations/{group}", $"/api/conversations/{group}/messages", $"/api/conversations/{group}/members", $"/api/conversations/{group}/state" })
        {
            using var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(marker, body, StringComparison.Ordinal);
            using var json = JsonDocument.Parse(body);
            Assert.Single(json.RootElement.EnumerateObject());
            Assert.True(json.RootElement.TryGetProperty("error", out _));
        }
    }

    private static async Task<HttpResponseMessage> ChangeAsync(HttpClient client, HttpMethod method, string route, object? body = null)
    {
        using var request = new HttpRequestMessage(method, route);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var token = await client.GetAsync("/api/security/csrf-token");
        token.EnsureSuccessStatusCode();
        var csrf = (await token.Content.ReadFromJsonAsync<CsrfTokenResponse>())!;
        request.Headers.Add(SecurityOptions.CsrfHeaderName, csrf.Token);
        return await client.SendAsync(request);
    }
}
