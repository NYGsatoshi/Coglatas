using System.Net;
using System.Text.Json;
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

public sealed class TenantSettingsTextBoundaryPostgreSqlTests
{
    [PostgreSqlFact]
    public Task MalformedUtf16IsRejectedBeforeTrackedOrPersistedSettingsChange() =>
        WithDatabaseAsync(async database =>
        {
            await using var fixture = await AdministrationFixture.CreateAsync(database);
            var before = await SnapshotAsync(fixture.Db);
            foreach (var value in new[] { "\uD800", "\uDC00", "x\uD800y", "x\uDC00y" })
            {
                foreach (var locale in new[] { true, false })
                {
                    var request = locale
                        ? AdministrationFixture.SettingsRequest with { DefaultLocale = value }
                        : AdministrationFixture.SettingsRequest with { TimeZone = value };
                    var result = await fixture.Service.UpdateCurrentTenantSettingsAsync(request);
                    Assert.False(result.IsSuccess);
                    Assert.Equal("Locale and timezone text must be valid UTF-16.", result.Error);
                    Assert.DoesNotContain(fixture.Db.ChangeTracker.Entries(),
                        e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
                    await using var observed = fixture.OpenContext();
                    Assert.Equal(before, await SnapshotAsync(observed));
                }
            }
        });

    [PostgreSqlFact]
    public Task OversizedLocaleAndTimeZoneAreRejectedBeforeTrackedOrPersistedSettingsChange() =>
        WithDatabaseAsync(async database =>
        {
            await using var fixture = await AdministrationFixture.CreateAsync(database);
            var before = await SnapshotAsync(fixture.Db);
            foreach (var locale in new[] { true, false })
            {
                var request = Oversized(locale);
                var result = await fixture.Service.UpdateCurrentTenantSettingsAsync(request);
                Assert.False(result.IsSuccess);
                Assert.NotEmpty(result.Error!);
                Assert.DoesNotContain(fixture.Db.ChangeTracker.Entries(),
                    e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
                await using var observed = fixture.OpenContext();
                Assert.Equal(before, await SnapshotAsync(observed));
            }
        });

    [PostgreSqlFact]
    public Task ExactStoredTextBoundsAndPaddedOrOmittedValuesRemainSupported() =>
        WithDatabaseAsync(async database =>
        {
            await using var fixture = await AdministrationFixture.CreateAsync(database);
            foreach (var supplementary in new[] { false, true })
            {
                var locale = supplementary ? string.Concat(Enumerable.Repeat("\U0001F338", 20)) : new string('l', 20);
                var zone = supplementary ? string.Concat(Enumerable.Repeat("\U0001F338", 80)) : new string('z', 80);
                var changed = await fixture.Service.UpdateCurrentTenantSettingsAsync(
                    AdministrationFixture.SettingsRequest with { DefaultLocale = " " + locale + " ", TimeZone = " " + zone + " " });
                Assert.True(changed.IsSuccess, changed.Error);
                Assert.Equal(locale, changed.Value!.DefaultLocale);
                Assert.Equal(zone, changed.Value.TimeZone);
                var omitted = await fixture.Service.UpdateCurrentTenantSettingsAsync(
                    AdministrationFixture.SettingsRequest with { DefaultLocale = null, TimeZone = null });
                Assert.True(omitted.IsSuccess, omitted.Error);
                await using var observed = fixture.OpenContext();
                var actual = await observed.TenantSettings.SingleAsync();
                Assert.Equal(locale, actual.DefaultLocale);
                Assert.Equal(zone, actual.TimeZone);
            }
            await using var audit = fixture.OpenContext();
            Assert.Equal(4, await audit.AuditLogs.CountAsync(e => e.Action == "TenantSettingsChanged"));
        });

    [PostgreSqlFact]
    public Task VendorJsonHttpReturns400WithoutSettingsOrAuditEffectsAndKeepsAuthorizationFirst() =>
        WithDatabaseAsync(async database =>
        {
            await using var host = await AdmissionHost.CreateAsync(database);
            await WithDbAsync(host, async db =>
            {
                (await db.TenantUsers.SingleAsync(e => e.UserId == host.Actor.Id)).Role = TenantUserRole.Owner;
                await db.SaveChangesAsync();
            });
            await host.LoginAsync();
            using (var initial = await host.Client.GetAsync("/api/tenant/settings"))
            {
                Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
            }
            var before = "";
            await WithDbAsync(host, async db => before = await SnapshotAsync(db));
            foreach (var locale in new[] { true, false })
            {
                using var response = await PatchAsync(host.Client, Oversized(locale));
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.DoesNotContain("InternalServerError", await response.Content.ReadAsStringAsync());
                await WithDbAsync(host, async db => Assert.Equal(before, await SnapshotAsync(db)));
            }
            using (var valid = await PatchAsync(host.Client, AdministrationFixture.SettingsRequest))
            {
                Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
            }
            await WithDbAsync(host, async db =>
            {
                (await db.TenantUsers.SingleAsync(e => e.UserId == host.Actor.Id)).Role = TenantUserRole.Member;
                await db.SaveChangesAsync();
                before = await SnapshotAsync(db);
            });
            using var denied = await PatchAsync(host.Client, Oversized(locale: true));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            await WithDbAsync(host, async db => Assert.Equal(before, await SnapshotAsync(db)));
        });

    private static UpdateTenantSettingsRequest Oversized(bool locale) => locale
        ? AdministrationFixture.SettingsRequest with { DefaultLocale = new string('l', 21) }
        : AdministrationFixture.SettingsRequest with { TimeZone = new string('z', 81) };

    private static async Task<string> SnapshotAsync(AppDbContext db)
    {
        var settings = await db.TenantSettings.AsNoTracking().Select(s => new
        {
            s.Id, s.TenantId, s.DisplayName, s.LogoFileId, s.ThemeColor, s.DefaultLocale, s.TimeZone,
            s.InvitationMode, s.StorageQuotaBytes, s.UserLimit, s.ProjectLimit, s.FileUploadLimitBytes,
            s.FeatureFlagsJson, s.NotificationSettingsJson, s.CreatedAt, s.UpdatedAt
        }).SingleAsync();
        var audit = await db.AuditLogs.AsNoTracking().Where(e => e.Action == "TenantSettingsChanged")
            .OrderBy(e => e.Id).Select(e => new { e.Id, e.Action, e.ActorUserId, e.EntityId, e.CreatedAt }).ToListAsync();
        return JsonSerializer.Serialize(new { settings, audit });
    }

    private static async Task WithDbAsync(AdmissionHost host, Func<AppDbContext, Task> action)
    {
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantService>().SetTenant(host.Tenant.Id, host.Tenant.Slug);
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, UpdateTenantSettingsRequest settings)
    {
        using var csrf = await client.GetAsync("/api/security/csrf-token");
        csrf.EnsureSuccessStatusCode();
        var token = await csrf.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/tenant/settings");
        request.Content = JsonContent.Create(settings);
        request.Content.Headers.ContentType = new("application/vnd.coglatas+json");
        request.Headers.Add(SecurityOptions.CsrfHeaderName, token!.Token);
        return await client.SendAsync(request);
    }

    private static Task WithDatabaseAsync(Func<string, Task> test) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), test);
}
