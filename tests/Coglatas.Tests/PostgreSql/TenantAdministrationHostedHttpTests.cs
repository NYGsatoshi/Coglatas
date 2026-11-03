using System.Net;
using System.Net.Http.Json;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.TenantAdministration;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Web.Configuration;
using Coglatas.Web.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;

namespace Coglatas.Tests.PostgreSql;

// Loopback Kestrel, actual PostgreSQL, password login, saved cookies and CSRF.
// Uses the existing synthetic Development host; no deployed HTTPS or RLS claim.
public sealed class TenantAdministrationHostedHttpTests
{
    private static readonly string[] ProtectedRoutes = ["/api/tenant/overview", "/api/tenant/settings", "/api/tenant/usage"];

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task CurrentTenantManagementPreservesOwnerAdminAndRejectsForeignOrDemotedActor() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var host = await AdmissionHost.CreateAsync(database);
                await WithDbAsync(host, async db =>
                {
                    (await db.TenantUsers.SingleAsync(row => row.UserId == host.Actor.Id)).Role = TenantUserRole.Owner;
                    await db.SaveChangesAsync();
                });
                await host.LoginAsync();
                foreach (var role in new[] { TenantUserRole.Owner, TenantUserRole.Admin })
                {
                    await WithDbAsync(host, async db =>
                    {
                        (await db.TenantUsers.SingleAsync(row => row.UserId == host.Actor.Id)).Role = role;
                        await db.SaveChangesAsync();
                    });
                    using var changed = await PatchSettingsAsync(host.Client, AdministrationFixture.SettingsRequest);
                    Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
                    var settings = await changed.Content.ReadFromJsonAsync<TenantSettingsResponse>();
                    Assert.Equal(host.Tenant.Id, settings!.TenantId);
                    Assert.Equal("Changed settings", settings.DisplayName);
                    foreach (var route in ProtectedRoutes)
                    {
                        using var response = await host.Client.GetAsync(route);
                        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    }
                }

                var foreignSlug = "";
                var foreignTenantId = Guid.Empty;
                await WithDbAsync(host, async db =>
                {
                    var foreign = await db.TenantUsers.IgnoreQueryFilters().Include(row => row.Tenant)
                        .SingleAsync(row => row.UserId == host.ForeignRecipient.Id);
                    foreignTenantId = foreign.TenantId;
                    foreignSlug = foreign.Tenant!.Slug;
                });
                host.Client.DefaultRequestHeaders.Remove("X-Tenant-Slug");
                host.Client.DefaultRequestHeaders.Add("X-Tenant-Slug", foreignSlug);
                using (var foreign = await host.Client.GetAsync("/api/tenant/settings"))
                {
                    Assert.Equal(HttpStatusCode.Unauthorized, foreign.StatusCode);
                    Assert.DoesNotContain("Changed settings", await foreign.Content.ReadAsStringAsync());
                }
                host.Client.DefaultRequestHeaders.Remove("X-Tenant-Slug");
                host.Client.DefaultRequestHeaders.Add("X-Tenant-Slug", host.Tenant.Slug);
                await host.LoginAsync();
                await WithDbAsync(host, async db =>
                {
                    (await db.TenantUsers.SingleAsync(row => row.UserId == host.Actor.Id)).Role = TenantUserRole.Member;
                    await db.SaveChangesAsync();
                });
                using var denied = await PatchSettingsAsync(host.Client, AdministrationFixture.SettingsRequest with { DisplayName = "Denied HTTP mutation" });
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
                await WithDbAsync(host, async db =>
                {
                    Assert.Equal("Changed settings", (await db.TenantSettings.SingleAsync()).DisplayName);
                    Assert.Equal(2, await db.AuditLogs.CountAsync(row => row.Action == "TenantSettingsChanged"));
                    Assert.Empty(await db.TenantSettings.IgnoreQueryFilters().Where(row => row.TenantId == foreignTenantId).ToListAsync());
                });
            });

    private static async Task WithDbAsync(AdmissionHost host, Func<AppDbContext, Task> action)
    {
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantService>().SetTenant(host.Tenant.Id, host.Tenant.Slug);
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static async Task<HttpResponseMessage> PatchSettingsAsync(HttpClient client, UpdateTenantSettingsRequest settings)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/tenant/settings");
        request.Content = JsonContent.Create(settings);
        using var csrf = await client.GetAsync("/api/security/csrf-token");
        csrf.EnsureSuccessStatusCode();
        var token = await csrf.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        request.Headers.Add(SecurityOptions.CsrfHeaderName, token!.Token);
        return await client.SendAsync(request);
    }
}
