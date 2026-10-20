using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coglatas.Domain.Enums;
using Coglatas.Web.Configuration;
using Coglatas.Web.Controllers;
using Microsoft.EntityFrameworkCore;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;

namespace Coglatas.Tests.PostgreSql;

public sealed partial class WorkspaceMemberManagementHostedHttpTests
{
    [PostgreSqlFact]
    public Task TargetOptionsPreserveInactiveStatusAndDisableActiveAdmissionForInactiveIdentity() => WithManagerAsync(async host =>
    {
        foreach (var status in Enum.GetValues<MembershipStatus>())
        {
            await WithDbAsync(host, db => db.WorkspaceMembers.Where(member => member.WorkspaceId == host.Workspace.Id && member.UserId == host.Recipient.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(member => member.Status, status)));
            using var options = await ReadTargetAsync(host, host.Recipient.Id);
            var target = options.RootElement.GetProperty("target");
            Assert.Equal((int)status, target.GetProperty("membershipStatus").GetInt32());
            Assert.True(target.GetProperty("canChangeRole").GetBoolean());
            Assert.True(target.GetProperty("canRemove").GetBoolean());
            using var update = await SendCommandAsync(host, HttpMethod.Patch, MemberRoute(host, host.Recipient.Id), new { role = 4 });
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
            await WithDbAsync(host, async db => Assert.Equal(status, await db.WorkspaceMembers.AsNoTracking()
                .Where(member => member.WorkspaceId == host.Workspace.Id && member.UserId == host.Recipient.Id)
                .Select(member => member.Status).SingleAsync()));
        }
        await WithDbAsync(host, async db =>
        {
            await db.WorkspaceMembers.Where(member => member.WorkspaceId == host.Workspace.Id && member.UserId == host.Recipient.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(member => member.Status, MembershipStatus.Active));
            await db.Users.Where(user => user.Id == host.Recipient.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(user => user.Status, UserStatus.Suspended));
        });
        using (var options = await ReadTargetAsync(host, host.Recipient.Id))
        {
            var target = options.RootElement.GetProperty("target");
            Assert.False(target.GetProperty("canChangeRole").GetBoolean());
            Assert.Empty(target.GetProperty("allowedRoles").EnumerateArray());
            Assert.True(target.GetProperty("canRemove").GetBoolean());
        }
        using (var denied = await SendCommandAsync(host, HttpMethod.Patch, MemberRoute(host, host.Recipient.Id), new { role = 0 }))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, denied.StatusCode);
        using (var removal = await SendCommandAsync(host, HttpMethod.Delete, MemberRoute(host, host.Recipient.Id)))
            Assert.Equal(HttpStatusCode.OK, removal.StatusCode);
        await WithDbAsync(host, db => db.Users.Where(user => user.Id == host.Recipient.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.DeletedAt, DateTimeOffset.UtcNow)));
        using var missing = await host.Client.GetAsync($"{OptionsRoute(host)}?userId={host.Recipient.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    });

    [PostgreSqlFact]
    public Task UndefinedPatchEnumsAndMissingCsrfCannotPersistRoleStatusAuditOrOutbox() => WithManagerAsync(async host =>
    {
        var before = await MutationCountsAsync(host);
        foreach (var body in new object[] { new { role = 999 }, new { role = 0, status = 999 } })
        {
            using var invalid = await SendCommandAsync(host, HttpMethod.Patch, MemberRoute(host, host.Recipient.Id), body);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using (var noCsrf = await SendCommandAsync(host, HttpMethod.Patch, MemberRoute(host, host.Recipient.Id), new { role = 0 }, includeCsrf: false))
            Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        using (var noCsrf = await host.PostAsync($"/api/workspaces/{host.Workspace.Id}/members", new { userId = host.OtherWorkspaceRecipient.Id, role = 0 }, includeCsrf: false))
            Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        Assert.Equal(before, await MutationCountsAsync(host));
        await WithDbAsync(host, async db =>
        {
            var target = await db.WorkspaceMembers.AsNoTracking().SingleAsync(member => member.WorkspaceId == host.Workspace.Id && member.UserId == host.Recipient.Id);
            Assert.Equal(WorkspaceRole.Member, target.Role);
            Assert.Equal(MembershipStatus.Active, target.Status);
            Assert.False(await db.WorkspaceMembers.AnyAsync(member => member.WorkspaceId == host.Workspace.Id && member.UserId == host.OtherWorkspaceRecipient.Id));
        });
    });

    [PostgreSqlFact]
    public Task CurrentPreviewDoesNotReserveTargetEligibilityAndFailedAdmissionLeavesNoPartialWrite() => WithManagerAsync(async host =>
    {
        using (var preview = await host.Client.GetAsync($"{CandidatesRoute(host)}?q=Other"))
        {
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            Assert.Contains(host.OtherWorkspaceRecipient.Id.ToString(), await preview.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }
        await WithDbAsync(host, db => db.TenantUsers.Where(member => member.UserId == host.OtherWorkspaceRecipient.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(member => member.Status, TenantUserStatus.Suspended)));
        var before = await MutationCountsAsync(host);
        using var command = await host.PostAsync($"/api/workspaces/{host.Workspace.Id}/members", new { userId = host.OtherWorkspaceRecipient.Id, role = 3 });
        Assert.Equal(HttpStatusCode.NotFound, command.StatusCode);
        Assert.Equal(before, await MutationCountsAsync(host));
        await WithDbAsync(host, async db => Assert.False(await db.WorkspaceMembers.AnyAsync(member =>
            member.WorkspaceId == host.Workspace.Id && member.UserId == host.OtherWorkspaceRecipient.Id)));
    });

    [PostgreSqlFact]
    public Task CanonicalGeneralTracksAllFiveRolesAndApprovedOwnerChangeAndManagerSelfRemoval() => WithManagerAsync(async host =>
    {
        await WithDbAsync(host, db => db.TenantUsers.Where(member => member.UserId == host.Actor.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(member => member.Role, TenantUserRole.Owner)));
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/workspaces");
        createRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        createRequest.Content = JsonContent.Create(new { name = "Synthetic management canonical general" });
        await AddCsrfAsync(host, createRequest);
        using var creation = await host.Client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
        using var created = JsonDocument.Parse(await creation.Content.ReadAsStringAsync());
        var workspaceId = created.RootElement.GetProperty("data").GetProperty("id").GetGuid();
        Guid conversationId = Guid.Empty;
        await WithDbAsync(host, async db => conversationId = await db.Conversations.AsNoTracking()
            .Where(conversation => conversation.WorkspaceId == workspaceId && conversation.DefaultKind == ConversationDefaultKind.WorkspaceGeneral)
            .Select(conversation => conversation.Id).SingleAsync());
        using (var add = await host.PostAsync($"/api/workspaces/{workspaceId}/members", new { userId = host.OtherWorkspaceRecipient.Id, role = 0 }))
            Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        foreach (var role in Enum.GetValues<WorkspaceRole>())
        {
            using var update = await SendCommandAsync(host, HttpMethod.Patch,
                $"/api/workspaces/{workspaceId}/members/{host.OtherWorkspaceRecipient.Id}", new { role = (int)role });
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
            await WithDbAsync(host, async db =>
            {
                var participant = await db.ConversationMembers.AsNoTracking().SingleAsync(member => member.ConversationId == conversationId && member.UserId == host.OtherWorkspaceRecipient.Id);
                Assert.True(participant.CanRead);
                Assert.Equal(role != WorkspaceRole.ReadOnly, participant.CanPost);
                Assert.Equal(role != WorkspaceRole.ReadOnly, participant.CanCreateThread);
                Assert.False(participant.CanManageMembers);
                Assert.Null(participant.RemovedAt);
            });
        }
        using (var ownerChange = await SendCommandAsync(host, HttpMethod.Patch,
                   $"/api/workspaces/{workspaceId}/members/{host.Actor.Id}", new { role = 1 }))
            Assert.Equal(HttpStatusCode.OK, ownerChange.StatusCode);
        using (var selfRemoval = await SendCommandAsync(host, HttpMethod.Delete, $"/api/workspaces/{workspaceId}/members/{host.Actor.Id}"))
            Assert.Equal(HttpStatusCode.OK, selfRemoval.StatusCode);
        using (var stalePreview = await host.Client.GetAsync($"/api/workspaces/{workspaceId}/members/management/options"))
            Assert.Equal(HttpStatusCode.NotFound, stalePreview.StatusCode);
        await WithDbAsync(host, async db =>
        {
            var participant = await db.ConversationMembers.AsNoTracking().SingleAsync(member => member.ConversationId == conversationId && member.UserId == host.Actor.Id);
            Assert.False(participant.CanRead);
            Assert.False(participant.CanPost);
            Assert.False(participant.CanManageMembers);
            Assert.False(participant.CanCreateThread);
            Assert.NotNull(participant.RemovedAt);
            Assert.True(await db.AuditLogs.AnyAsync(audit => audit.EntityId == workspaceId && audit.Action == "WorkspaceMemberRemoved"));
            Assert.True(await db.OutboxEvents.AnyAsync(outbox => outbox.EventType == "Security.AuthorizationStateChanged.v1"));
        });
    });

    private static string MemberRoute(AdmissionHost host, Guid userId) => $"/api/workspaces/{host.Workspace.Id}/members/{userId}";

    private static async Task<JsonDocument> ReadTargetAsync(AdmissionHost host, Guid userId)
    {
        using var response = await host.Client.GetAsync($"{OptionsRoute(host)}?userId={userId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> SendCommandAsync(AdmissionHost host, HttpMethod method,
        string route, object? body = null, bool includeCsrf = true)
    {
        using var request = new HttpRequestMessage(method, route);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (includeCsrf) await AddCsrfAsync(host, request);
        return await host.Client.SendAsync(request);
    }

    private static async Task AddCsrfAsync(AdmissionHost host, HttpRequestMessage request)
    {
        using var response = await host.Client.GetAsync("/api/security/csrf-token");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        request.Headers.Add(SecurityOptions.CsrfHeaderName, token!.Token);
    }

    private static async Task<(int Audit, int Outbox)> MutationCountsAsync(AdmissionHost host)
    {
        var counts = (Audit: 0, Outbox: 0);
        await WithDbAsync(host, async db => counts = (await db.AuditLogs.CountAsync(), await db.OutboxEvents.CountAsync()));
        return counts;
    }
}
