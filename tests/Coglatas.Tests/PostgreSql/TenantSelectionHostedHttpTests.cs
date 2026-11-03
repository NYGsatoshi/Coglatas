using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coglatas.Application.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.SecurityArchitecture;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Coglatas.Tests.PostgreSql;

public sealed class TenantSelectionHostedHttpTests
{
    private const string TenantCookieName = "coglatas_tenant";
    private const string AuthenticationCookieName = ".Coglatas.Auth";

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task SelectionUsesCurrentMembershipWithoutChangingCookiesOrSessionOnDenial() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database, sessionTenantResolution: true);
                var actorId = SecurityCiFixtureSeed.TenantAMemberUserId;
                await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
                var alpha = await db.Tenants.SingleAsync(tenant => tenant.Slug == SecurityCiFixtureSeed.TenantASlug);
                var beta = await db.Tenants.SingleAsync(tenant => tenant.Slug == SecurityCiFixtureSeed.TenantBSlug);
                var foreign = NewTenant("foreign", TenantStatus.Active);
                var invited = NewTenant("invited", TenantStatus.Active);
                var suspended = NewTenant("suspended", TenantStatus.Suspended);
                var archived = NewTenant("archived", TenantStatus.Archived);
                archived.MarkDeleted(DateTimeOffset.UtcNow);
                db.Tenants.AddRange(foreign, invited, suspended, archived);
                var betaMembership = Membership(beta, actorId, TenantUserStatus.Active);
                betaMembership.Role = TenantUserRole.Staff;
                db.TenantUsers.AddRange(
                    betaMembership,
                    Membership(foreign, SecurityCiFixtureSeed.TenantBOwnerUserId, TenantUserStatus.Active),
                    Membership(invited, actorId, TenantUserStatus.Invited),
                    Membership(suspended, actorId, TenantUserStatus.Active),
                    Membership(archived, actorId, TenantUserStatus.Active));
                await db.SaveChangesAsync();

                using var client = await app.LoginAsync("member", alpha.Slug, SecurityCiFixtureSeed.TenantAMemberEmail);
                var session = await ReadSessionAsync(db, actorId);
                Assert.Null(session.RevokedAt);
                await AssertCurrentAsync(client, alpha, TenantUserRole.Member);
                await AssertListedAsync(client, alpha.Id, beta.Id);

                using (var selected = await client.PostAsJsonAsync("/api/tenants/switch", new SwitchTenantRequest(beta.Id)))
                {
                    Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
                    var tenant = await selected.Content.ReadFromJsonAsync<TenantResponse>();
                    Assert.Equal(beta.Id, tenant!.Id);
                    var cookie = Assert.Single(Cookies(selected));
                    Assert.Equal(TenantCookieName, cookie.Name.Value);
                    Assert.Equal(beta.Slug, cookie.Value.Value);
                    Assert.True(cookie.HttpOnly);
                }
                await AssertCurrentAsync(client, beta, TenantUserRole.Staff);

                await AssertSwitchDeniedAsync(client, foreign.Id, "Tenant membership is required.");
                await AssertSwitchDeniedAsync(client, Guid.NewGuid(), "Tenant membership is required.");
                await AssertSwitchDeniedAsync(client, invited.Id, "Tenant membership is required.");
                await AssertSwitchDeniedAsync(client, suspended.Id, "Tenant is not available.");
                await AssertSwitchDeniedAsync(client, archived.Id, "Tenant is not available.");
                await AssertCurrentAsync(client, beta, TenantUserRole.Staff);
                await AssertSessionUnchangedAsync(db, actorId, session);

                betaMembership.Status = TenantUserStatus.Suspended;
                await db.SaveChangesAsync();
                await AssertListedAsync(client, alpha.Id);
                await AssertSwitchDeniedAsync(client, beta.Id, "Tenant membership is required.");
                await AssertSessionUnchangedAsync(db, actorId, session);

                // Current requires active membership in the selected Tenant.
                // My and Switch already exercised the authenticated recovery routes.
                using (var staleCurrent = await client.GetAsync("/api/tenants/current"))
                {
                    Assert.Equal(HttpStatusCode.Unauthorized, staleCurrent.StatusCode);
                    var expiredAuthCookie = Assert.Single(Cookies(staleCurrent), cookie => cookie.Name.Value == AuthenticationCookieName);
                    Assert.True(string.IsNullOrEmpty(expiredAuthCookie.Value.Value));
                    Assert.DoesNotContain(Cookies(staleCurrent), cookie => cookie.Name.Value == TenantCookieName);
                }
                using var afterSignOut = await client.GetAsync("/api/tenants/my");
                Assert.Equal(HttpStatusCode.Unauthorized, afterSignOut.StatusCode);
                await AssertSessionUnchangedAsync(db, actorId, session);
            });

    private static Tenant NewTenant(string slug, TenantStatus status) =>
        new() { Name = slug, Slug = slug, DisplayName = slug, Status = status };

    private static TenantUser Membership(Tenant tenant, Guid userId, TenantUserStatus status) =>
        new() { TenantId = tenant.Id, UserId = userId, Status = status, JoinedAt = DateTimeOffset.UtcNow };

    private static async Task AssertCurrentAsync(HttpClient client, Tenant tenant, TenantUserRole role)
    {
        using var response = await client.GetAsync("/api/tenants/current");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var current = await response.Content.ReadFromJsonAsync<CurrentTenantResponse>();
        Assert.NotNull(current);
        Assert.Equal(tenant.Id, current.TenantId);
        Assert.Equal(tenant.Slug, current.TenantSlug);
        Assert.Equal(tenant.DisplayName, current.DisplayName);
        Assert.Equal(TenantStatus.Active, current.Status);
        Assert.Equal(role, current.CurrentUserRole);
        Assert.True(current.IsAvailable);
        Assert.False(current.IsPlatformScope);
        Assert.True(current.AllowTenantSwitching);
    }

    private static async Task AssertListedAsync(HttpClient client, params Guid[] expectedIds)
    {
        using var response = await client.GetAsync("/api/tenants/my");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listed = await response.Content.ReadFromJsonAsync<TenantResponse[]>();
        Assert.NotNull(listed);
        Assert.Equal(expectedIds.Order(), listed.Select(tenant => tenant.Id).Order());
        Assert.All(listed, tenant => Assert.Equal(TenantStatus.Active, tenant.Status));
        Assert.Empty(Cookies(response));
    }

    private static async Task AssertSwitchDeniedAsync(HttpClient client, Guid tenantId, string error)
    {
        using var response = await client.PostAsJsonAsync("/api/tenants/switch", new SwitchTenantRequest(tenantId));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(error, body.RootElement.GetProperty("error").GetString());
        Assert.Empty(Cookies(response));
    }

    private static IList<SetCookieHeaderValue> Cookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues(HeaderNames.SetCookie, out var values)
            ? SetCookieHeaderValue.ParseList(values.ToList())
            : [];

    private static async Task AssertSessionUnchangedAsync(AppDbContext db, Guid actorId, SessionState expected)
    {
        var current = await ReadSessionAsync(db, actorId);
        Assert.Equal(expected.Id, current.Id);
        Assert.Equal(expected.ExpiresAt, current.ExpiresAt);
        Assert.Equal(expected.RevokedAt, current.RevokedAt);
    }

    // LastSeen may advance as part of normal cookie validation.
    private static Task<SessionState> ReadSessionAsync(AppDbContext db, Guid actorId) =>
        db.Sessions.Where(session => session.UserId == actorId)
            .Select(session => new SessionState(session.Id, session.ExpiresAt, session.RevokedAt))
            .SingleAsync();

    private sealed record SessionState(Guid Id, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt);
}
