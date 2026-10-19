using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.UiShell;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Web.Configuration;
using Coglatas.Web.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;

namespace Coglatas.Tests.PostgreSql;

public sealed class UiShellLayoutHostedHttpTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task NullLayoutNamesReturn400WithoutWritingOrChangingLayouts() => WithHostAsync(async host =>
    {
        await host.LoginAsync();
        var before = await SnapshotAsync(host);
        // Preserve the minimized Main 45d Schemathesis case, including its existing scope/JSON values.
        var rejected = new SaveUserLayoutRequest("\uD84E\uDF44\0\uDA5C\uDCBF\u0014\u00A8",
            (LayoutScopeType)114719, Guid.Parse("fe9d96c1-b36b-3d5c-8227-eda25d50c450"), "5");
        using (var response = await host.PostAsync("/api/ui/layouts", rejected))
        {
            await AssertNullRejectionAsync(response);
        }
        Assert.Equal(before, await SnapshotAsync(host));

        using var created = await host.PostAsync("/api/ui/layouts", Request("Original"));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var layout = (await created.Content.ReadFromJsonAsync<UserLayoutResponse>())!;
        before = await SnapshotAsync(host);
        using (var response = await SendPutAsync(host.Client, layout.Id, rejected))
        {
            await AssertNullRejectionAsync(response);
        }
        Assert.Equal(before, await SnapshotAsync(host));
    });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task UnicodeLayoutNamesPersistWhileCookieCsrfAndOwnershipBoundariesRemainEnforced() => WithHostAsync(async host =>
    {
        // These Unicode literals protect valid Japanese, supplementary, and combining-character names.
        const string initial = "\u751F\u5F92\u4F1A \U00023B44 e\u0301";
        const string updated = "Council \U0001F338 \u4F1A\u8B70";
        using (var anonymous = await host.PostAsync("/api/ui/layouts", Request(initial)))
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        await host.LoginAsync();
        using (var missingCsrf = await host.PostAsync("/api/ui/layouts", Request(initial), includeCsrf: false))
            Assert.Equal(HttpStatusCode.Forbidden, missingCsrf.StatusCode);
        Assert.Equal("[]", await SnapshotAsync(host));

        using var created = await host.PostAsync("/api/ui/layouts", Request($" {initial} "));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var layout = (await created.Content.ReadFromJsonAsync<UserLayoutResponse>())!;
        Assert.Equal(initial, layout.LayoutName);
        await AssertStoredAsync(host, layout.Id, initial, "{}");
        var before = await SnapshotAsync(host);
        using (var missingCsrf = await SendPutAsync(host.Client, layout.Id, Request(updated), includeCsrf: false))
            Assert.Equal(HttpStatusCode.Forbidden, missingCsrf.StatusCode);
        Assert.Equal(before, await SnapshotAsync(host));

        var foreignLayoutId = Guid.NewGuid();
        await WithDbAsync(host, async db =>
        {
            db.UserLayouts.Add(new UserLayout
            {
                Id = foreignLayoutId, UserId = host.Recipient.Id, Name = "Other owner's layout",
                ScopeType = LayoutScopeType.Global, LayoutJson = "{}"
            });
            await db.SaveChangesAsync();
        });
        before = await SnapshotAsync(host);
        using (var denied = await SendPutAsync(host.Client, foreignLayoutId, Request(updated)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            using var error = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
            Assert.Equal("Layout not found.", error.RootElement.GetProperty("error").GetString());
        }
        Assert.Equal(before, await SnapshotAsync(host));

        using var saved = await SendPutAsync(host.Client, layout.Id, Request(updated) with { LayoutJson = "[]" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(updated, (await saved.Content.ReadFromJsonAsync<UserLayoutResponse>())!.LayoutName);
        await AssertStoredAsync(host, layout.Id, updated, "[]");
        using var listed = await host.Client.GetAsync("/api/ui/layouts");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var visible = await listed.Content.ReadFromJsonAsync<UserLayoutResponse[]>();
        Assert.Equal(updated, Assert.Single(visible!).LayoutName);
    });

    private static Task WithHostAsync(Func<AdmissionHost, Task> test) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var host = await AdmissionHost.CreateAsync(database);
                await test(host);
            });

    private static SaveUserLayoutRequest Request(string name) => new(name, LayoutScopeType.Global, null, "{}");

    private static async Task AssertNullRejectionAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Layout name contains unsupported characters.", body.RootElement.GetProperty("error").GetString());
        Assert.Single(body.RootElement.EnumerateObject());
    }

    private static async Task<HttpResponseMessage> SendPutAsync(HttpClient client, Guid layoutId, SaveUserLayoutRequest body, bool includeCsrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/ui/layouts/{layoutId}") { Content = JsonContent.Create(body) };
        if (includeCsrf)
        {
            using var response = await client.GetAsync("/api/security/csrf-token");
            response.EnsureSuccessStatusCode();
            var csrf = (await response.Content.ReadFromJsonAsync<CsrfTokenResponse>())!;
            request.Headers.Add(SecurityOptions.CsrfHeaderName, csrf.Token);
        }
        return await client.SendAsync(request);
    }

    private static async Task<string> SnapshotAsync(AdmissionHost host)
    {
        var snapshot = "";
        await WithDbAsync(host, async db => snapshot = JsonSerializer.Serialize(await db.UserLayouts.AsNoTracking()
            .OrderBy(layout => layout.Id).Select(layout => new { layout.Id, layout.UserId, layout.Name, layout.LayoutJson, layout.ScopeType, layout.ScopeId, layout.UpdatedAt }).ToListAsync()));
        return snapshot;
    }

    private static Task AssertStoredAsync(AdmissionHost host, Guid id, string name, string json) => WithDbAsync(host, async db =>
    {
        var stored = await db.UserLayouts.AsNoTracking().SingleAsync(layout => layout.Id == id);
        Assert.Equal(name, stored.Name);
        Assert.Equal(json, stored.LayoutJson);
        Assert.Equal(host.Actor.Id, stored.UserId);
    });

    private static async Task WithDbAsync(AdmissionHost host, Func<AppDbContext, Task> action)
    {
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantService>().SetTenant(host.Tenant.Id, host.Tenant.Slug);
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
