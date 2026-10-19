using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Auth;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
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

public sealed class ConversationParticipantHostedHttpTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task DirectParticipantHttpBoundariesRejectNeverAndRemovedMembersAndStaleFileGrantBytes() =>
        WithScenarioAsync(ConversationType.DirectMessage, async scenario =>
        {
            await scenario.AssertReadableAsync(scenario.Host.Client);
            await scenario.AssertReadableAsync(scenario.RecipientClient);
            await scenario.SendAllowedAsync(scenario.RecipientClient, "Recipient positive control");

            var bytes = Encoding.UTF8.GetBytes($"Private attachment {Guid.NewGuid():N}");
            using var upload = new MultipartFormDataContent();
            upload.Add(new StringContent(nameof(AttachmentOwnerType.Message)), "ownerType");
            upload.Add(new StringContent(scenario.MessageId.ToString("D")), "ownerId");
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            upload.Add(content, "file", "participant-control.txt");
            using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, "/api/attachments") { Content = upload };
            using (var csrfResponse = await scenario.Host.Client.GetAsync("/api/security/csrf-token"))
            {
                csrfResponse.EnsureSuccessStatusCode();
                var csrf = await csrfResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>();
                uploadRequest.Headers.Add(SecurityOptions.CsrfHeaderName, csrf!.Token);
            }
            using var uploaded = await scenario.Host.Client.SendAsync(uploadRequest);
            Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
            var attachment = (await uploaded.Content.ReadFromJsonAsync<FileAttachmentResponse>())!;
            using var issued = await AdmissionHost.PostAsync(scenario.RecipientClient,
                $"/api/attachments/{attachment.Id}/download-grants", new FileDownloadGrantRequest());
            Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
            var grant = (await issued.Content.ReadFromJsonAsync<FileDownloadGrantResponse>())!;
            var grantRoute = $"/api/attachment-download-grants/{grant.FileDownloadGrantId}/download";
            using (var allowed = await AdmissionHost.PostAsync(scenario.RecipientClient, grantRoute, new FileDownloadGrantTokenRequest(grant.Token)))
            {
                Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
                Assert.Equal(bytes, await allowed.Content.ReadAsByteArrayAsync());
                Assert.True(allowed.Headers.CacheControl!.NoStore);
            }

            await scenario.AssertDeniedAsync(scenario.NeverParticipantClient);
            await scenario.AssertAttachmentDeniedAsync(scenario.NeverParticipantClient, attachment.Id);
            await scenario.RemoveRecipientAsync();
            await scenario.AssertDeniedAsync(scenario.RecipientClient);
            await scenario.AssertAttachmentDeniedAsync(scenario.RecipientClient, attachment.Id);
            await scenario.AssertGrantIsCurrentAsync(grant.FileDownloadGrantId);
            var before = await scenario.DomainStateAsync();
            using (var stale = await AdmissionHost.PostAsync(scenario.RecipientClient, grantRoute, new FileDownloadGrantTokenRequest(grant.Token)))
            {
                await AssertErrorAsync(stale, "Attachment not found.", scenario.ProtectedMarker);
                Assert.DoesNotContain(Encoding.UTF8.GetString(bytes), await stale.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
            Assert.Equal(before, await scenario.DomainStateAsync());
            using (var ownerDownload = await scenario.Host.Client.GetAsync($"/api/attachments/{attachment.Id}/download"))
            {
                Assert.Equal(HttpStatusCode.OK, ownerDownload.StatusCode);
                Assert.Equal(bytes, await ownerDownload.Content.ReadAsByteArrayAsync());
            }
            await scenario.AssertReadableAsync(scenario.Host.Client);
            await scenario.SendAllowedAsync(scenario.Host.Client, "Owner positive after participant removal");
        });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task WorkspaceChannelHttpSendAndReadsRejectNeverAndRemovedMembers() =>
        WithScenarioAsync(ConversationType.WorkspaceChannel, async scenario =>
        {
            await scenario.AssertReadableAsync(scenario.Host.Client);
            await scenario.AssertReadableAsync(scenario.RecipientClient);
            await scenario.SendAllowedAsync(scenario.Host.Client, "Owner WorkspaceChannel positive control");
            await scenario.SendAllowedAsync(scenario.RecipientClient, "Recipient WorkspaceChannel positive control");
            await scenario.AssertDeniedAsync(scenario.NeverParticipantClient);
            await scenario.RemoveRecipientAsync();
            await scenario.AssertDeniedAsync(scenario.RecipientClient);
            await scenario.AssertReadableAsync(scenario.Host.Client);
            await scenario.SendAllowedAsync(scenario.Host.Client, "Owner WorkspaceChannel positive after removal");
        });

    private static Task WithScenarioAsync(ConversationType type, Func<ParticipantScenario, Task> test) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var host = await AdmissionHost.CreateAsync(database);
                await host.LoginAsync();
                await using var scenario = await ParticipantScenario.CreateAsync(host, type);
                await test(scenario);
            });

    private static async Task AssertErrorAsync(HttpResponseMessage response, string error, string protectedMarker)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(error, json.RootElement.GetProperty("error").GetString());
        Assert.Single(json.RootElement.EnumerateObject());
        Assert.DoesNotContain(protectedMarker, body, StringComparison.Ordinal);
    }

    private sealed class ParticipantScenario(AdmissionHost host, HttpClient recipientClient, HttpClient neverParticipantClient) : IAsyncDisposable
    {
        public AdmissionHost Host => host;
        public HttpClient RecipientClient => recipientClient;
        public HttpClient NeverParticipantClient => neverParticipantClient;
        public Guid ConversationId { get; private set; }
        public Guid MessageId { get; private set; }
        public string ProtectedMarker { get; } = $"Protected participant body {Guid.NewGuid():N}";
        private Guid NeverParticipantId { get; set; }

        public static async Task<ParticipantScenario> CreateAsync(AdmissionHost host, ConversationType type)
        {
            var neverEmail = $"never-participant-{Guid.NewGuid():N}@example.test";
            var never = new User { DisplayName = "Never participant", Email = neverEmail, NormalizedEmail = neverEmail.ToUpperInvariant(), Status = UserStatus.Active };
            await WithDbAsync(host, async db =>
            {
                await using var passwordScope = host.Services.CreateAsyncScope();
                var hasher = passwordScope.ServiceProvider.GetRequiredService<IPasswordHasher>();
                var recipient = await db.Users.SingleAsync(user => user.Id == host.Recipient.Id);
                recipient.PasswordHash = hasher.HashPassword(AdmissionHost.Password);
                never.PasswordHash = hasher.HashPassword(AdmissionHost.Password);
                db.Users.Add(never);
                db.TenantUsers.Add(new TenantUser { UserId = never.Id, Role = TenantUserRole.Member, Status = TenantUserStatus.Active });
                db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = host.Workspace.Id, UserId = never.Id, Role = WorkspaceRole.Member, Status = MembershipStatus.Active });
                await db.SaveChangesAsync();
            });
            var recipientClient = await LoginClientAsync(host, host.Recipient.Email);
            var neverClient = await LoginClientAsync(host, never.Email);
            var scenario = new ParticipantScenario(host, recipientClient, neverClient) { NeverParticipantId = never.Id };
            if (type == ConversationType.DirectMessage)
            {
                using var created = await host.PostAsync("/api/conversations",
                    new CreateConversationRequest(type, null, [host.Recipient.Id], host.Workspace.Id));
                Assert.Equal(HttpStatusCode.OK, created.StatusCode);
                var conversation = (await created.Content.ReadFromJsonAsync<ConversationDetailResponse>())!;
                Assert.Equal(ConversationType.DirectMessage, conversation.Type);
                Assert.Equal(host.Workspace.Id, conversation.WorkspaceId);
                Assert.Null(conversation.ProjectId);
                Assert.Equal(2, conversation.Members.Count);
                scenario.ConversationId = conversation.Id;
                scenario.MessageId = await scenario.SendAllowedAsync(host.Client, scenario.ProtectedMarker);
            }
            else
            {
                // Seed the existing non-General read shape; no creation/provisioning path is activated.
                await WithDbAsync(host, async db =>
                {
                    var conversation = new Conversation { Type = type, WorkspaceId = host.Workspace.Id, Title = scenario.ProtectedMarker, CreatedByUserId = host.Actor.Id };
                    db.Conversations.Add(conversation);
                    foreach (var userId in new[] { host.Actor.Id, host.Recipient.Id })
                        db.ConversationMembers.Add(new ConversationMember { ConversationId = conversation.Id, UserId = userId, CanRead = true, CanPost = true, JoinedAt = DateTimeOffset.UtcNow });
                    var message = new Message { ConversationId = conversation.Id, WorkspaceId = host.Workspace.Id, AuthorUserId = host.Actor.Id, Body = scenario.ProtectedMarker };
                    db.Messages.Add(message);
                    await db.SaveChangesAsync();
                    scenario.ConversationId = conversation.Id;
                    scenario.MessageId = message.Id;
                });
            }
            return scenario;
        }

        private static async Task<HttpClient> LoginClientAsync(AdmissionHost host, string email)
        {
            var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }) { BaseAddress = host.Client.BaseAddress };
            client.DefaultRequestHeaders.Add("X-Tenant-Slug", host.Tenant.Slug);
            using var login = await AdmissionHost.PostAsync(client, "/api/auth/login", new LoginRequest(email, AdmissionHost.Password));
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            return client;
        }

        public async Task AssertReadableAsync(HttpClient client)
        {
            using var detail = await client.GetAsync($"/api/conversations/{ConversationId}");
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            var conversation = (await detail.Content.ReadFromJsonAsync<ConversationDetailResponse>())!;
            Assert.Equal(ConversationId, conversation.Id);
            Assert.Contains(conversation.Members, member => member.UserId == host.Actor.Id);
            foreach (var suffix in new[] { "", $"?anchorMessageId={MessageId}" })
            {
                using var messages = await client.GetAsync($"/api/conversations/{ConversationId}/messages{suffix}");
                Assert.Equal(HttpStatusCode.OK, messages.StatusCode);
                Assert.Contains(ProtectedMarker, await messages.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
        }

        public async Task<Guid> SendAllowedAsync(HttpClient client, string body)
        {
            var requestId = Guid.NewGuid();
            using var sent = await AdmissionHost.PostAsync(client, $"/api/conversations/{ConversationId}/messages", new SendMessageRequest(body, ClientRequestId: requestId));
            Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
            var message = (await sent.Content.ReadFromJsonAsync<MessageResponse>())!;
            Assert.Equal(body, message.Body);
            Assert.Equal(requestId, message.ClientRequestId);
            await WithDbAsync(host, async db => Assert.True(await db.Messages.AnyAsync(row => row.Id == message.Id && row.ClientRequestId == requestId && row.Body == body)));
            return message.Id;
        }

        public async Task AssertDeniedAsync(HttpClient client)
        {
            var before = await DomainStateAsync();
            foreach (var route in new[] { $"/api/conversations/{ConversationId}", $"/api/conversations/{ConversationId}/messages", $"/api/conversations/{ConversationId}/messages?anchorMessageId={MessageId}" })
            {
                using var denied = await client.GetAsync(route);
                await AssertErrorAsync(denied, "Conversation not found.", ProtectedMarker);
            }
            using (var denied = await AdmissionHost.PostAsync(client, $"/api/conversations/{ConversationId}/messages",
                       new SendMessageRequest($"Denied write {Guid.NewGuid():N}", ClientRequestId: Guid.NewGuid())))
                await AssertErrorAsync(denied, "You are not allowed to send messages.", ProtectedMarker);
            Assert.Equal(before, await DomainStateAsync());
        }

        public async Task AssertAttachmentDeniedAsync(HttpClient client, Guid attachmentId)
        {
            var before = await DomainStateAsync();
            using (var metadata = await client.GetAsync($"/api/attachments/{attachmentId}"))
                await AssertErrorAsync(metadata, "Attachment not found.", ProtectedMarker);
            using (var grant = await AdmissionHost.PostAsync(client,
                       $"/api/attachments/{attachmentId}/download-grants", new FileDownloadGrantRequest()))
                await AssertErrorAsync(grant, "Attachment not found.", ProtectedMarker);
            Assert.Equal(before, await DomainStateAsync());
        }

        public Task RemoveRecipientAsync() => WithDbAsync(host, async db =>
        {
            // Direct has no manager-removal API. Persist the canonical removed state explicitly.
            var member = await db.ConversationMembers.SingleAsync(row => row.ConversationId == ConversationId && row.UserId == host.Recipient.Id);
            Assert.Null(member.LeftAt);
            Assert.True(member.CanRead);
            Assert.True(member.CanPost);
            member.RemovedAt = DateTimeOffset.UtcNow;
            member.RemovedByUserId = host.Actor.Id;
            await db.SaveChangesAsync();
            var removed = await db.ConversationMembers.AsNoTracking().SingleAsync(row => row.Id == member.Id);
            Assert.Null(removed.LeftAt);
            Assert.NotNull(removed.RemovedAt);
            Assert.Equal(host.Actor.Id, removed.RemovedByUserId);
            Assert.True(removed.CanRead);
            Assert.True(removed.CanPost);
            await AssertOrdinaryActiveMemberAsync(db, host.Recipient.Id, host.Workspace.Id);
        });

        public Task AssertGrantIsCurrentAsync(Guid grantId) => WithDbAsync(host, async db =>
        {
            var grant = await db.FileDownloadGrants.AsNoTracking().SingleAsync(row => row.Id == grantId);
            Assert.Equal(host.Recipient.Id, grant.ActorUserId);
            Assert.Null(grant.RevokedAt);
            Assert.NotNull(grant.DownloadedAt);
            Assert.True(grant.ExpiresAt > DateTimeOffset.UtcNow);
        });

        public async Task<string> DomainStateAsync()
        {
            string state = null!;
            await WithDbAsync(host, async db =>
            {
                Assert.False(await db.ConversationMembers.AnyAsync(member => member.UserId == NeverParticipantId));
                await AssertOrdinaryActiveMemberAsync(db, NeverParticipantId, host.Workspace.Id);
                await AssertOrdinaryActiveMemberAsync(db, host.Recipient.Id, host.Workspace.Id);
                state = JsonSerializer.Serialize(new
                {
                    Conversations = await db.Conversations.AsNoTracking().OrderBy(row => row.Id).Select(row => new { row.Id, row.Title, row.UpdatedAt }).ToListAsync(),
                    Members = await db.ConversationMembers.AsNoTracking().OrderBy(row => row.Id).Select(row => new { row.Id, row.UserId, row.LeftAt, row.RemovedAt, row.RemovedByUserId, row.CanRead, row.CanPost, row.UpdatedAt }).ToListAsync(),
                    Messages = await db.Messages.AsNoTracking().OrderBy(row => row.Id).Select(row => new { row.Id, row.Body, row.ClientRequestId, row.Version, row.UpdatedAt, row.DeletedAt }).ToListAsync(),
                    Attachments = await db.Attachments.AsNoTracking().OrderBy(row => row.Id).Select(row => new { row.Id, row.FileObjectId, row.DeletedAt }).ToListAsync(),
                    Files = await db.FileObjects.AsNoTracking().OrderBy(row => row.Id).Select(row => new { row.Id, row.Status, row.UpdatedAt }).ToListAsync(),
                    Grants = await db.FileDownloadGrants.AsNoTracking().OrderBy(row => row.Id).Select(row => new { row.Id, row.DownloadedAt, row.RevokedAt }).ToListAsync(),
                    Outbox = await db.OutboxEvents.AsNoTracking().OrderBy(row => row.Id).Select(row => new { row.Id, row.EventType, row.AggregateId }).ToListAsync(),
                    SuccessfulGrantUses = await db.AuditLogs.CountAsync(row => row.Action == "file_download.grant_used")
                });
            });
            return state;
        }

        private static async Task WithDbAsync(AdmissionHost host, Func<AppDbContext, Task> action)
        {
            await using var scope = host.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentTenantService>().SetTenant(host.Tenant.Id, host.Tenant.Slug);
            await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        private static async Task AssertOrdinaryActiveMemberAsync(AppDbContext db, Guid userId, Guid workspaceId)
        {
            Assert.True(await db.Users.AsNoTracking().AnyAsync(user => user.Id == userId && user.Status == UserStatus.Active && user.SystemRole == SystemRole.User && user.DeletedAt == null));
            Assert.True(await db.TenantUsers.AsNoTracking().AnyAsync(member => member.UserId == userId && member.Status == TenantUserStatus.Active && member.Role == TenantUserRole.Member));
            Assert.True(await db.WorkspaceMembers.AsNoTracking().AnyAsync(member => member.UserId == userId && member.WorkspaceId == workspaceId && member.Status == MembershipStatus.Active && member.Role == WorkspaceRole.Member));
        }

        public ValueTask DisposeAsync()
        {
            recipientClient.Dispose();
            neverParticipantClient.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
