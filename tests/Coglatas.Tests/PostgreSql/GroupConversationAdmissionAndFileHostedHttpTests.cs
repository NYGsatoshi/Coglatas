using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Files;
using Coglatas.Application.Messaging;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Web.Configuration;
using Coglatas.Web.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;
using FileAttachmentResponse = Coglatas.Application.Files.AttachmentResponse;

namespace Coglatas.Tests.PostgreSql;

public sealed partial class GroupConversationHostedHttpTests
{
    [PostgreSqlFact]
    public Task CurrentRecipientAdmissionAppliesToInitialCreationAndReentry() => WithScenarioAsync(async scenario =>
    {
        foreach (var state in new[] { "account-suspended", "account-deleted", "tenant-member-suspended", "workspace-member-suspended", "workspace-archived", "workspace-deleted" })
        {
            await ResetRecipientAsync(scenario);
            await scenario.WithDbAsync(db => ChangeRecipientAsync(db, scenario, state));
            using var denied = await scenario.Host.PostAsync("/api/conversations", new CreateConversationRequest(
                ConversationType.Group, "Denied current admission", [scenario.Host.Recipient.Id, scenario.Third.Id], scenario.Host.Workspace.Id));
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            await scenario.Host.AssertStateAsync(0, 0);
        }
        await ResetRecipientAsync(scenario);
        var group = await CreateGroupAsync(scenario);
        using (var remove = await ChangeAsync(scenario.Host.Client, HttpMethod.Delete, $"/api/conversations/{group.Id}/members/{scenario.Host.Recipient.Id}"))
            Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        foreach (var state in new[] { "account-suspended", "account-deleted", "tenant-member-suspended", "workspace-member-suspended", "workspace-archived", "workspace-deleted" })
        {
            await ResetRecipientAsync(scenario);
            await scenario.WithDbAsync(db => ChangeRecipientAsync(db, scenario, state));
            using var denied = await AdmissionHost.PostAsync(scenario.Host.Client, $"/api/conversations/{group.Id}/members", new AddConversationMemberRequest(scenario.Host.Recipient.Id));
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            await scenario.WithDbAsync(async db =>
            {
                var stored = await db.ConversationMembers.AsNoTracking().SingleAsync(row => row.ConversationId == group.Id && row.UserId == scenario.Host.Recipient.Id);
                Assert.NotNull(stored.RemovedAt);
                Assert.Equal(3, await db.ConversationMembers.CountAsync());
                Assert.Equal(0, await db.AuditLogs.CountAsync(row => row.Action == "ConversationMemberAdded"));
            });
        }
        await ResetRecipientAsync(scenario);
        using var allowed = await AdmissionHost.PostAsync(scenario.Host.Client, $"/api/conversations/{group.Id}/members", new AddConversationMemberRequest(scenario.Host.Recipient.Id));
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    });

    [PostgreSqlFact]
    public Task OrdinaryNeverParticipantHasNoAccessAndConcurrentAddsAndReaddsHaveOneSuccess() => WithScenarioAsync(async scenario =>
    {
        var group = await CreateGroupAsync(scenario);
        var original = await SendAsync(scenario.Host.Client, group.Id);
        var fourth = await AddWorkspaceUserAsync(scenario);
        using var client = await Scenario.LoginClientAsync(scenario.Host, fourth.Email);
        await AssertHiddenAsync(client, group.Id, original.Body);
        using (var neverSend = await AdmissionHost.PostAsync(client, $"/api/conversations/{group.Id}/messages", new SendMessageRequest("Never participant denied")))
            Assert.Equal(HttpStatusCode.BadRequest, neverSend.StatusCode);
        using (var neverAdd = await AdmissionHost.PostAsync(client, $"/api/conversations/{group.Id}/members", new AddConversationMemberRequest(fourth.Id)))
            Assert.Equal(HttpStatusCode.BadRequest, neverAdd.StatusCode);
        using (var neverLeave = await AdmissionHost.PostAsync(client, $"/api/conversations/{group.Id}/leave", new { }))
            Assert.Equal(HttpStatusCode.BadRequest, neverLeave.StatusCode);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => AdmissionHost.PostAsync(
                scenario.Host.Client, $"/api/conversations/{group.Id}/members", new AddConversationMemberRequest(fourth.Id))));
            try
            {
                Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
                Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.BadRequest));
            }
            finally { foreach (var response in responses) response.Dispose(); }
            await scenario.WithDbAsync(async db =>
            {
                var participant = await db.ConversationMembers.AsNoTracking().SingleAsync(row => row.ConversationId == group.Id && row.UserId == fourth.Id);
                Assert.Null(participant.LeftAt);
                Assert.Null(participant.RemovedAt);
                Assert.Equal(ConversationMemberRole.Member, participant.Role);
                Assert.False(participant.CanManageMembers);
                Assert.False(participant.CanCreateThread);
                Assert.Equal(attempt + 1, await db.AuditLogs.CountAsync(row => row.Action == "ConversationMemberAdded"));
            });
            using (var history = await client.GetAsync($"/api/conversations/{group.Id}/messages?anchorMessageId={original.Id}"))
            {
                Assert.Equal(HttpStatusCode.OK, history.StatusCode);
                Assert.Contains(original.Body, await history.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
            if (attempt == 0)
            {
                using var remove = await ChangeAsync(scenario.Host.Client, HttpMethod.Delete, $"/api/conversations/{group.Id}/members/{fourth.Id}");
                Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
            }
        }
    });

    [PostgreSqlFact]
    public Task RemovedGroupParticipantCannotUseCurrentFileGrantAndReentryRestoresHistoricalBytes() => WithScenarioAsync(async scenario =>
    {
        var group = await CreateGroupAsync(scenario);
        var original = await SendAsync(scenario.Host.Client, group.Id);
        var bytes = Encoding.UTF8.GetBytes("Synthetic Group attachment " + Guid.NewGuid().ToString("N"));
        using var upload = new MultipartFormDataContent();
        upload.Add(new StringContent(nameof(AttachmentOwnerType.Message)), "ownerType");
        upload.Add(new StringContent(original.Id.ToString()), "ownerId");
        using var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        upload.Add(fileContent, "file", "group-history.txt");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/attachments");
        request.Content = upload;
        using (var tokenResponse = await scenario.Host.Client.GetAsync("/api/security/csrf-token"))
        {
            var token = (await tokenResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>())!;
            request.Headers.Add(SecurityOptions.CsrfHeaderName, token.Token);
        }
        using var uploaded = await scenario.Host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var attachment = (await uploaded.Content.ReadFromJsonAsync<FileAttachmentResponse>())!;
        var entrant = await AddWorkspaceUserAsync(scenario);
        using var historyClient = await Scenario.LoginClientAsync(scenario.Host, entrant.Email);
        using (var beforeJoin = await historyClient.GetAsync($"/api/attachments/{attachment.Id}"))
            Assert.Equal(HttpStatusCode.BadRequest, beforeJoin.StatusCode);
        using (var joined = await AdmissionHost.PostAsync(scenario.Host.Client, $"/api/conversations/{group.Id}/members", new AddConversationMemberRequest(entrant.Id)))
            Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
        using var issue = await AdmissionHost.PostAsync(historyClient, $"/api/attachments/{attachment.Id}/download-grants", new FileDownloadGrantRequest());
        Assert.Equal(HttpStatusCode.OK, issue.StatusCode);
        var grant = (await issue.Content.ReadFromJsonAsync<FileDownloadGrantResponse>())!;
        var route = $"/api/attachment-download-grants/{grant.FileDownloadGrantId}/download";
        using (var allowed = await AdmissionHost.PostAsync(historyClient, route, new FileDownloadGrantTokenRequest(grant.Token)))
        {
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            Assert.Equal(bytes, await allowed.Content.ReadAsByteArrayAsync());
            Assert.True(allowed.Headers.CacheControl!.NoStore);
        }
        using (var remove = await ChangeAsync(scenario.Host.Client, HttpMethod.Delete, $"/api/conversations/{group.Id}/members/{entrant.Id}"))
            Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        string before = null!;
        await scenario.WithDbAsync(async db =>
        {
            var stored = await db.FileDownloadGrants.AsNoTracking().SingleAsync(row => row.Id == grant.FileDownloadGrantId);
            Assert.Equal(entrant.Id, stored.ActorUserId);
            Assert.Null(stored.RevokedAt);
            Assert.NotNull(stored.DownloadedAt);
            Assert.True(stored.ExpiresAt > DateTimeOffset.UtcNow);
            before = JsonSerializer.Serialize(new { stored.DownloadedAt, attachments = await db.Attachments.CountAsync(), files = await db.FileObjects.CountAsync(), messages = await db.Messages.CountAsync() });
        });
        using (var denied = await AdmissionHost.PostAsync(historyClient, route, new FileDownloadGrantTokenRequest(grant.Token)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            var error = await denied.Content.ReadAsStringAsync();
            Assert.DoesNotContain(Encoding.UTF8.GetString(bytes), error, StringComparison.Ordinal);
            Assert.DoesNotContain(original.Body, error, StringComparison.Ordinal);
        }
        using (var metadata = await historyClient.GetAsync($"/api/attachments/{attachment.Id}"))
            Assert.Equal(HttpStatusCode.BadRequest, metadata.StatusCode);
        await scenario.WithDbAsync(async db =>
        {
            var stored = await db.FileDownloadGrants.AsNoTracking().SingleAsync(row => row.Id == grant.FileDownloadGrantId);
            Assert.Equal(before, JsonSerializer.Serialize(new { stored.DownloadedAt, attachments = await db.Attachments.CountAsync(), files = await db.FileObjects.CountAsync(), messages = await db.Messages.CountAsync() }));
        });
        using (var owner = await scenario.Host.Client.GetAsync($"/api/attachments/{attachment.Id}/download"))
        {
            Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
            Assert.Equal(bytes, await owner.Content.ReadAsByteArrayAsync());
        }
        using (var readd = await AdmissionHost.PostAsync(scenario.Host.Client, $"/api/conversations/{group.Id}/members", new AddConversationMemberRequest(entrant.Id)))
            Assert.Equal(HttpStatusCode.OK, readd.StatusCode);
        using var restoredHistory = await historyClient.GetAsync($"/api/attachments/{attachment.Id}/download");
        Assert.Equal(HttpStatusCode.OK, restoredHistory.StatusCode);
        Assert.Equal(bytes, await restoredHistory.Content.ReadAsByteArrayAsync());
    });

    private static async Task<User> AddWorkspaceUserAsync(Scenario scenario)
    {
        var email = "group-invited-" + Guid.NewGuid().ToString("N") + "@example.test";
        await using var passwordScope = scenario.Host.Services.CreateAsyncScope();
        var user = new User { DisplayName = "Ordinary invited Group candidate", Email = email, NormalizedEmail = email.ToUpperInvariant(), Status = UserStatus.Active,
            PasswordHash = passwordScope.ServiceProvider.GetRequiredService<IPasswordHasher>().HashPassword(AdmissionHost.Password) };
        await scenario.WithDbAsync(async db =>
        {
            db.Users.Add(user);
            db.TenantUsers.Add(new TenantUser { UserId = user.Id, Role = TenantUserRole.Member, Status = TenantUserStatus.Active });
            db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = scenario.Host.Workspace.Id, UserId = user.Id, Role = WorkspaceRole.Member, Status = MembershipStatus.Active });
            await db.SaveChangesAsync();
            Assert.Equal(SystemRole.User, user.SystemRole);
        });
        return user;
    }

    private static Task ChangeRecipientAsync(AppDbContext db, Scenario scenario, string state) => state switch
    {
        "account-suspended" => db.Users.Where(row => row.Id == scenario.Host.Recipient.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, UserStatus.Suspended)),
        "account-deleted" => db.Users.Where(row => row.Id == scenario.Host.Recipient.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.DeletedAt, DateTimeOffset.UtcNow)),
        "tenant-member-suspended" => db.TenantUsers.Where(row => row.UserId == scenario.Host.Recipient.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, TenantUserStatus.Suspended)),
        "workspace-member-suspended" => db.WorkspaceMembers.Where(row => row.WorkspaceId == scenario.Host.Workspace.Id && row.UserId == scenario.Host.Recipient.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, MembershipStatus.Suspended)),
        "workspace-archived" => db.Workspaces.Where(row => row.Id == scenario.Host.Workspace.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, WorkspaceStatus.Archived)),
        "workspace-deleted" => db.Workspaces.Where(row => row.Id == scenario.Host.Workspace.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, WorkspaceStatus.Deleted)),
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    private static Task ResetRecipientAsync(Scenario scenario) => scenario.WithDbAsync(async db =>
    {
        await db.Users.IgnoreQueryFilters().Where(row => row.Id == scenario.Host.Recipient.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, UserStatus.Active).SetProperty(row => row.DeletedAt, (DateTimeOffset?)null));
        await db.TenantUsers.Where(row => row.UserId == scenario.Host.Recipient.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, TenantUserStatus.Active));
        await db.Workspaces.Where(row => row.Id == scenario.Host.Workspace.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, WorkspaceStatus.Active));
        await db.WorkspaceMembers.Where(row => row.WorkspaceId == scenario.Host.Workspace.Id && row.UserId == scenario.Host.Recipient.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, MembershipStatus.Active));
    });
}
