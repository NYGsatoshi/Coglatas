using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coglatas.Application.Auth;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Messaging;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;

namespace Coglatas.Tests.PostgreSql;

[Collection("PostgreSqlTaskV1")]
[Trait("Category", "PostgreSQLIntegration")]
public sealed partial class GroupConversationHostedHttpTests
{
    [PostgreSqlFact]
    public Task NumericAndStringGroupCreationRetainPrivateScopeAndOriginalMessageIdentity() => WithScenarioAsync(async scenario =>
    {
        foreach (var type in new object[] { 7, "Group" })
        {
            using var created = await scenario.Host.PostAsync("/api/conversations", new
            {
                type, title = "  Synthetic private Group  ", workspaceId = scenario.Host.Workspace.Id,
                memberUserIds = new[] { scenario.Host.Actor.Id, scenario.Host.Recipient.Id, scenario.Third.Id, scenario.Third.Id }
            });
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            Assert.Equal("Group", json.RootElement.GetProperty("type").GetString());
            var group = (await created.Content.ReadFromJsonAsync<ConversationDetailResponse>())!;
            Assert.Equal(ConversationType.Group, group.Type);
            Assert.Equal("Synthetic private Group", group.Title);
            Assert.Equal(scenario.Host.Workspace.Id, group.WorkspaceId);
            Assert.Null(group.ProjectId);
            Assert.Null(group.ParentConversationId);
            Assert.Null(group.RootConversationId);
            Assert.Equal(3, group.Members.Count);
            Assert.All(group.Members, member => Assert.False(member.CanCreateThread));
            var creator = Assert.Single(group.Members, member => member.UserId == scenario.Host.Actor.Id);
            Assert.Equal(ConversationMemberRole.Admin, creator.Role);
            Assert.True(creator.CanManageMembers);
            await scenario.WithDbAsync(async db =>
            {
                var stored = await db.Conversations.AsNoTracking().SingleAsync(row => row.Id == group.Id);
                Assert.Null(stored.DefaultKind);
                Assert.Null(stored.Visibility);
            });

            var requestId = Guid.NewGuid();
            var body = $"Private Group body {Guid.NewGuid():N}";
            var command = new SendMessageRequest(body, ClientRequestId: requestId);
            Guid messageId;
            using (var sent = await AdmissionHost.PostAsync(scenario.RecipientClient, $"/api/conversations/{group.Id}/messages", command))
            {
                Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
                var message = (await sent.Content.ReadFromJsonAsync<MessageResponse>())!;
                Assert.Equal(requestId, message.ClientRequestId);
                Assert.Equal(scenario.Host.Recipient.Id, message.AuthorUserId);
                messageId = message.Id;
            }
            using (var repeated = await AdmissionHost.PostAsync(scenario.RecipientClient, $"/api/conversations/{group.Id}/messages", command))
            {
                Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
                Assert.Equal(messageId, (await repeated.Content.ReadFromJsonAsync<MessageResponse>())!.Id);
            }
            using var read = await scenario.ThirdClient.GetAsync($"/api/conversations/{group.Id}/messages?limit=100");
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.Contains(body, await read.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            await scenario.WithDbAsync(async db => Assert.Single(await db.Messages.AsNoTracking()
                .Where(message => message.ConversationId == group.Id && message.ClientRequestId == requestId).ToListAsync()));
        }
        await scenario.WithDbAsync(async db => Assert.Equal(2, await db.Conversations.CountAsync(row => row.Type == ConversationType.Group)));
    });

    [PostgreSqlFact]
    public Task GroupShapeAndAdmissionFailuresAreSafeAndDoNotStageSuccessRows() => WithScenarioAsync(async scenario =>
    {
        var workspaceId = scenario.Host.Workspace.Id;
        var validMembers = new[] { scenario.Host.Recipient.Id, scenario.Third.Id };
        foreach (var request in new object[]
        {
            new { type = 7, workspaceId, memberUserIds = Array.Empty<Guid>() },
            new { type = 7, workspaceId, memberUserIds = new[] { scenario.Host.Actor.Id, scenario.Host.Actor.Id } },
            new { type = 7, workspaceId, memberUserIds = new[] { scenario.Host.Recipient.Id } },
            new { type = 7, workspaceId, memberUserIds = (Guid[]?)null },
            new { type = 7, workspaceId, memberUserIds = new[] { Guid.Empty, scenario.Third.Id } },
            new { type = 7, workspaceId, memberUserIds = validMembers, projectId = Guid.NewGuid() },
            new { type = 7, workspaceId, memberUserIds = validMembers, parentConversationId = Guid.NewGuid() },
            new { type = 7, workspaceId = Guid.Empty, memberUserIds = validMembers },
            new { type = 7, memberUserIds = validMembers },
            new { type = 7, workspaceId, memberUserIds = validMembers, title = new string('x', 201) },
            new { type = 7, workspaceId, memberUserIds = validMembers, title = "Rejected\0Group title" },
            new { type = 7, workspaceId, memberUserIds = new[] { scenario.Host.ForeignRecipient.Id, scenario.Third.Id } },
            new { type = 7, workspaceId, memberUserIds = new[] { scenario.Host.OtherWorkspaceRecipient.Id, scenario.Third.Id } },
            new { type = 7, workspaceId, memberUserIds = new[] { Guid.NewGuid(), scenario.Third.Id } }
        })
        {
            using var denied = await scenario.Host.PostAsync("/api/conversations", request);
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            var text = await denied.Content.ReadAsStringAsync();
            Assert.DoesNotContain(scenario.Host.ForeignRecipient.Email, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(scenario.Host.ForeignRecipient.DisplayName, text, StringComparison.Ordinal);
            await scenario.Host.AssertStateAsync(0, 0);
        }
    });

    [PostgreSqlFact]
    public Task OptionalAndBoundaryTitlesDoNotRequireWorkspaceContributionRole() => WithScenarioAsync(async scenario =>
    {
        await scenario.WithDbAsync(async db => await db.WorkspaceMembers.Where(member => member.WorkspaceId == scenario.Host.Workspace.Id && member.UserId == scenario.Host.Actor.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(member => member.Role, WorkspaceRole.ReadOnly)));
        foreach (var title in new string?[] { null, "   ", "  " + new string('x', 200) + "  " })
        {
            using var created = await scenario.Host.PostAsync("/api/conversations", new CreateConversationRequest(
                ConversationType.Group, title, [scenario.Host.Recipient.Id, scenario.Third.Id], scenario.Host.Workspace.Id));
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            var group = (await created.Content.ReadFromJsonAsync<ConversationDetailResponse>())!;
            Assert.Equal(title?.Trim(), group.Title);
            await scenario.WithDbAsync(async db => Assert.Equal(title?.Trim(),
                await db.Conversations.Where(row => row.Id == group.Id).Select(row => row.Title).SingleAsync()));
        }
    });

    private static Task WithScenarioAsync(Func<Scenario, Task> test) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var host = await AdmissionHost.CreateAsync(database);
                await host.LoginAsync();
                await using var scenario = await Scenario.CreateAsync(host);
                await test(scenario);
            });

    private sealed class Scenario(AdmissionHost host, User third, HttpClient recipientClient, HttpClient thirdClient) : IAsyncDisposable
    {
        public AdmissionHost Host => host;
        public User Third => third;
        public HttpClient RecipientClient => recipientClient;
        public HttpClient ThirdClient => thirdClient;

        public static async Task<Scenario> CreateAsync(AdmissionHost host)
        {
            var email = $"group-third-{Guid.NewGuid():N}@example.test";
            var third = new User { DisplayName = "Synthetic Group third member", Email = email, NormalizedEmail = email.ToUpperInvariant(), Status = UserStatus.Active };
            await using (var scope = host.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<CurrentTenantService>().SetTenant(host.Tenant.Id, host.Tenant.Slug);
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
                third.PasswordHash = hasher.HashPassword(AdmissionHost.Password);
                var recipient = await db.Users.SingleAsync(user => user.Id == host.Recipient.Id);
                recipient.PasswordHash = hasher.HashPassword(AdmissionHost.Password);
                db.Users.Add(third);
                db.TenantUsers.Add(new TenantUser { UserId = third.Id, Role = TenantUserRole.Member, Status = TenantUserStatus.Active });
                db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = host.Workspace.Id, UserId = third.Id, Role = WorkspaceRole.Member, Status = MembershipStatus.Active });
                await db.SaveChangesAsync();
            }
            var recipientClient = await LoginClientAsync(host, host.Recipient.Email);
            try
            {
                var thirdClient = await LoginClientAsync(host, third.Email);
                return new Scenario(host, third, recipientClient, thirdClient);
            }
            catch
            {
                recipientClient.Dispose();
                throw;
            }
        }

        public static async Task<HttpClient> LoginClientAsync(AdmissionHost host, string email)
        {
            var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }) { BaseAddress = host.Client.BaseAddress };
            try
            {
                client.DefaultRequestHeaders.Add("X-Tenant-Slug", host.Tenant.Slug);
                using var login = await AdmissionHost.PostAsync(client, "/api/auth/login", new LoginRequest(email, AdmissionHost.Password));
                Assert.Equal(HttpStatusCode.OK, login.StatusCode);
                return client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        public async Task WithDbAsync(Func<AppDbContext, Task> action)
        {
            await using var scope = host.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentTenantService>().SetTenant(host.Tenant.Id, host.Tenant.Slug);
            await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        public ValueTask DisposeAsync()
        {
            recipientClient.Dispose();
            thirdClient.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
