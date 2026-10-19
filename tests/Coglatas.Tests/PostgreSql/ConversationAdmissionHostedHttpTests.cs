using System.Net;
using System.Net.Http.Json;
using Coglatas.Application;
using Coglatas.Application.Auth;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Messaging;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Web.Configuration;
using Coglatas.Web.Controllers;
using Coglatas.Web.Extensions;
using Coglatas.Web.Middleware;
using Coglatas.Web.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coglatas.Tests.PostgreSql;

public sealed class ConversationAdmissionHostedHttpTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task CookieAndCsrfPipelineRejectsOutOfScopeAdmissionWithoutPersistenceAndPreservesDirectCreation()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var host = await AdmissionHost.CreateAsync(database);
                var request = new CreateConversationRequest(ConversationType.DirectMessage, null, [host.Recipient.Id], host.Workspace.Id);
                using (var anonymous = await host.PostAsync("/api/conversations", request))
                    Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

                await host.LoginAsync();
                using (var withoutCsrf = await host.PostAsync("/api/conversations", request, includeCsrf: false))
                    Assert.Equal(HttpStatusCode.Forbidden, withoutCsrf.StatusCode);
                await host.AssertStateAsync(0, 0);

                string? maskedError = null;
                foreach (var deniedId in new[] { host.ForeignRecipient.Id, host.OtherWorkspaceRecipient.Id, Guid.NewGuid() })
                {
                    using var denied = await host.PostAsync("/api/conversations", request with { MemberUserIds = [deniedId] });
                    Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
                    var body = await denied.Content.ReadAsStringAsync();
                    maskedError ??= body;
                    Assert.Equal(maskedError, body);
                    Assert.DoesNotContain(deniedId.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain(host.ForeignRecipient.DisplayName, body, StringComparison.Ordinal);
                    Assert.DoesNotContain(host.ForeignRecipient.Email, body, StringComparison.OrdinalIgnoreCase);
                    await host.AssertStateAsync(0, 0);
                }

                using (var unsupportedGroup = await host.PostAsync("/api/conversations", request with { Type = ConversationType.Group }))
                    Assert.Equal(HttpStatusCode.BadRequest, unsupportedGroup.StatusCode);
                using (var unsupportedGroupName = await host.PostAsync("/api/conversations", new
                {
                    type = "Group", workspaceId = host.Workspace.Id, memberUserIds = new[] { host.Recipient.Id }
                }))
                    Assert.Equal(HttpStatusCode.BadRequest, unsupportedGroupName.StatusCode);
                await host.AssertStateAsync(0, 0);

                using (var allowed = await host.PostAsync("/api/conversations", request))
                    Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
                using (var reused = await host.PostAsync("/api/conversations/direct", new CreateDirectConversationRequest(host.Recipient.Id)))
                    Assert.Equal(HttpStatusCode.OK, reused.StatusCode);
                await host.AssertStateAsync(1, 2);

                await host.SuspendRecipientTenantMembershipAsync();
                using (var revoked = await host.PostAsync("/api/conversations", request))
                    Assert.Equal(HttpStatusCode.BadRequest, revoked.StatusCode);
                using (var revokedDirect = await host.PostAsync("/api/conversations/direct", new CreateDirectConversationRequest(host.Recipient.Id)))
                    Assert.Equal(HttpStatusCode.BadRequest, revokedDirect.StatusCode);
                await host.AssertStateAsync(1, 2);
            });
    }

    private sealed class AdmissionHost(WebApplication app, HttpClient client, string temporaryRoot) : IAsyncDisposable
    {
        private const string Password = "SyntheticAdmission!234";
        private Tenant Tenant { get; set; } = null!;
        public Workspace Workspace { get; private set; } = null!;
        private User Actor { get; set; } = null!;
        public User Recipient { get; private set; } = null!;
        public User ForeignRecipient { get; private set; } = null!;
        public User OtherWorkspaceRecipient { get; private set; } = null!;

        public static async Task<AdmissionHost> CreateAsync(string database)
        {
            var temporaryRoot = Path.Combine(Path.GetTempPath(), "coglatas-admission-http", Guid.NewGuid().ToString("N"));
            var keyPath = Path.Combine(temporaryRoot, "keys");
            Directory.CreateDirectory(keyPath);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database,
                ["Tenancy:AppMode"] = "SaaS",
                ["Tenancy:TenantResolutionStrategy"] = "HeaderForDevelopmentOnly",
                ["Tenancy:AllowDevelopmentHeaderTenantResolution"] = "true",
                ["Tenancy:DevelopmentTenantHeaderName"] = "X-Tenant-Slug",
                ["Security:CookieSecurePolicy"] = "SameAsRequest",
                ["Security:RequireHttps"] = "false",
                ["Security:EnableHsts"] = "false",
                ["Security:EnableCsrfProtection"] = "true",
                ["Security:EnableRateLimiting"] = "false",
                ["Security:LoginLockoutEnabled"] = "false",
                ["FileStorage:Provider"] = "LocalFileSystem",
                ["FileStorage:RootPath"] = Path.Combine(temporaryRoot, "files")
            });
            builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyPath));
            builder.Services.AddApplication().AddInfrastructure(builder.Configuration).AddWebServices(builder.Configuration);
            builder.Services.AddControllers().AddApplicationPart(typeof(ConversationsController).Assembly);
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
            {
                options.Cookie.Name = $".Coglatas.Admission.{Guid.NewGuid():N}";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.EventsType = typeof(DbSessionCookieAuthenticationEvents);
            });
            builder.Services.AddAuthorization();
            var app = builder.Build();
            app.UseMiddleware<GlobalExceptionHandlingMiddleware>();
            app.UseMiddleware<SecurityHeadersMiddleware>();
            app.UseMiddleware<TenantResolutionMiddleware>();
            app.UseAuthentication();
            app.Services.GetRequiredService<CsrfProtectionState>().MarkMiddlewareActive();
            app.UseMiddleware<CsrfProtectionMiddleware>();
            app.UseAuthorization();
            app.MapControllers();
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false })
            {
                BaseAddress = new Uri(address)
            };
            var host = new AdmissionHost(app, client, temporaryRoot);
            await host.SeedAsync();
            client.DefaultRequestHeaders.Add("X-Tenant-Slug", host.Tenant.Slug);
            return host;
        }

        public async Task LoginAsync()
        {
            using var response = await PostAsync("/api/auth/login", new LoginRequest(Actor.Email, Password));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        public async Task<HttpResponseMessage> PostAsync<T>(string route, T body, bool includeCsrf = true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, route);
            request.Content = JsonContent.Create(body);
            if (includeCsrf)
            {
                using var tokenResponse = await client.GetAsync("/api/security/csrf-token");
                tokenResponse.EnsureSuccessStatusCode();
                var payload = await tokenResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>();
                request.Headers.Add(SecurityOptions.CsrfHeaderName, payload!.Token);
            }
            return await client.SendAsync(request);
        }

        public async Task AssertStateAsync(int conversations, int participants)
        {
            await using var scope = app.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentTenantService>().SetTenant(Tenant.Id, Tenant.Slug);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(conversations, await db.Conversations.CountAsync());
            Assert.Equal(participants, await db.ConversationMembers.CountAsync());
            Assert.Equal(conversations, await db.AuditLogs.CountAsync(log => log.Action == "ConversationCreated"));
        }

        public async Task SuspendRecipientTenantMembershipAsync()
        {
            await using var scope = app.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentTenantService>().SetTenant(Tenant.Id, Tenant.Slug);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.TenantUsers.Where(member => member.UserId == Recipient.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(member => member.Status, TenantUserStatus.Suspended));
        }

        private async Task SeedAsync()
        {
            await using var scope = app.Services.CreateAsyncScope();
            var currentTenant = scope.ServiceProvider.GetRequiredService<CurrentTenantService>();
            currentTenant.SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            Actor = NewUser("Actor");
            Actor.PasswordHash = hasher.HashPassword(Password);
            Recipient = NewUser("Recipient");
            ForeignRecipient = NewUser("Foreign recipient");
            OtherWorkspaceRecipient = NewUser("Other workspace recipient");
            Tenant = new Tenant { Name = "Admission HTTP", DisplayName = "Admission HTTP", Slug = $"admission-http-{Guid.NewGuid():N}" };
            var foreignTenant = new Tenant { Name = "Foreign HTTP", DisplayName = "Foreign HTTP", Slug = $"foreign-http-{Guid.NewGuid():N}" };
            db.Tenants.AddRange(Tenant, foreignTenant);
            db.Users.AddRange(Actor, Recipient, ForeignRecipient, OtherWorkspaceRecipient);
            await db.SaveChangesAsync();
            currentTenant.SetTenant(Tenant.Id, Tenant.Slug);
            Workspace = new Workspace { Name = "Admission HTTP workspace", Slug = $"http-workspace-{Guid.NewGuid():N}", CreatedByUserId = Actor.Id };
            var otherWorkspace = new Workspace { Name = "Other HTTP workspace", Slug = $"other-http-workspace-{Guid.NewGuid():N}", CreatedByUserId = Actor.Id };
            db.Workspaces.AddRange(Workspace, otherWorkspace);
            foreach (var user in new[] { Actor, Recipient, OtherWorkspaceRecipient })
            {
                db.TenantUsers.Add(new TenantUser { UserId = user.Id, Role = TenantUserRole.Member, Status = TenantUserStatus.Active });
                db.WorkspaceMembers.Add(new WorkspaceMember
                {
                    WorkspaceId = user == OtherWorkspaceRecipient ? otherWorkspace.Id : Workspace.Id,
                    UserId = user.Id, Role = WorkspaceRole.Member, Status = MembershipStatus.Active
                });
            }
            await db.SaveChangesAsync();
            currentTenant.SetTenant(foreignTenant.Id, foreignTenant.Slug);
            db.TenantUsers.Add(new TenantUser { UserId = ForeignRecipient.Id, Role = TenantUserRole.Member, Status = TenantUserStatus.Active });
            await db.SaveChangesAsync();
        }

        private static User NewUser(string name)
        {
            var email = $"admission-http-{Guid.NewGuid():N}@example.test";
            return new User { DisplayName = name, Email = email, NormalizedEmail = email.ToUpperInvariant(), Status = UserStatus.Active };
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }
}
