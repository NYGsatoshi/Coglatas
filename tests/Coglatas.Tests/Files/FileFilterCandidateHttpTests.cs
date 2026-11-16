using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Security.Redaction;
using Coglatas.Web.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coglatas.Tests.Files;

/// <summary>Loopback MVC/cookie fixture with real FileService and InMemory persistence, not full backend qualification.</summary>
public sealed class FileFilterCandidateHttpTests
{
    [Fact]
    public async Task ProductionDefaultMarkerIsAbsentAndLegacyGetKeepsItsOriginalEnvelope()
    {
        await using var f = await FileFilterFixture.CreateAsync();
        var attachment = f.Add("private-http-sentinel.txt"); await f.Db.SaveChangesAsync();
        await using var host = await Host.Start(f, enabled: false);
        using var anonymous = await host.Client.GetAsync($"/api/files?workspaceId={f.Workspace.Id}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        await host.Login();
        using var legacy = await host.Client.GetAsync($"/api/files?workspaceId={f.Workspace.Id}&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        using var json = JsonDocument.Parse(await legacy.Content.ReadAsStringAsync());
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(attachment.Id, item.GetProperty("id").GetGuid());
        Assert.False(item.TryGetProperty("attachmentId", out _));
        Assert.Equal("[redacted:file]", item.GetProperty("originalFileName").GetString());
        using var unavailable = await host.Client.GetAsync($"/api/files?workspaceId={f.Workspace.Id}&scope=workspace");
        Assert.Equal(HttpStatusCode.NotFound, unavailable.StatusCode);
        Assert.DoesNotContain("private-http-sentinel", await unavailable.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ExplicitIsolatedHostReturnsOptInIdentitiesWithoutDroppingLegacyFieldsOrRedaction()
    {
        await using var f = await FileFilterFixture.CreateAsync();
        var attachment = f.Add("protected-http-sentinel.txt", f.Project.Id, f.Task);
        await f.Db.SaveChangesAsync();
        await using var host = await Host.Start(f, enabled: true); await host.Login();
        using var response = await host.Client.GetAsync($"/api/files?workspaceId={f.Workspace.Id}&scope=project&projectId={f.Project.Id}&atRoot=true");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Contains("Cookie", response.Headers.Vary);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(attachment.Id, item.GetProperty("id").GetGuid());
        Assert.Equal(attachment.Id, item.GetProperty("attachmentId").GetGuid());
        Assert.Equal(f.Project.Id, item.GetProperty("projectId").GetGuid());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("folderId").ValueKind);
        foreach (var name in new[] { "scanStatus", "uploadedByUserId", "uploadedByDisplayName", "createdAt", "updatedAt", "deletedAt", "accessState", "externalRecipientCount", "canManageSharing", "sharingVersion" })
            Assert.True(item.TryGetProperty(name, out _), name);
        Assert.DoesNotContain("protected-http-sentinel", body);
        Assert.False(item.GetProperty("canManageSharing").GetBoolean());
        Assert.Equal(1, doc.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("scope=unknown")]
    [InlineData("scope=")]
    [InlineData("scope=workspace&scope=project")]
    [InlineData("projectId=")]
    [InlineData("folderId=")]
    [InlineData("atRoot=")]
    [InlineData("scope=project")]
    [InlineData("projectId=untrusted-query-sentinel")]
    [InlineData("folderId=untrusted-query-sentinel")]
    [InlineData("atRoot=untrusted-query-sentinel")]
    [InlineData("sort=untrusted-query-sentinel")]
    [InlineData("direction=untrusted-query-sentinel")]
    public async Task InvalidNewQueryHasSafe400WithoutEchoingQueryValues(string query)
    {
        await using var f = await FileFilterFixture.CreateAsync();
        await using var host = await Host.Start(f, enabled: true); await host.Login();
        using var response = await host.Client.GetAsync($"/api/files?workspaceId={f.Workspace.Id}&{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ValidationFailed", body);
        Assert.DoesNotContain("untrusted-query-sentinel", body);
    }

    [Fact]
    public async Task RealServiceAuthorizationLossUsesSafe404WithoutItemsCountOrScopeNames()
    {
        await using var f = await FileFilterFixture.CreateAsync();
        f.Add("denied-http-sentinel.txt", f.Project.Id, f.Task); await f.Db.SaveChangesAsync();
        await using var host = await Host.Start(f, enabled: true); await host.Login();
        f.ProjectAuthority.Allowed = false;
        using var denied = await host.Client.GetAsync($"/api/files?workspaceId={f.Workspace.Id}&scope=project&projectId={f.Project.Id}");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        var body = await denied.Content.ReadAsStringAsync();
        Assert.DoesNotContain("denied-http-sentinel", body);
        Assert.DoesNotContain(f.Project.Id.ToString(), body);
        Assert.DoesNotContain("totalCount", body);
        Assert.DoesNotContain("items", body);
    }

    private sealed class Host(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client => client;
        public async Task Login()
        {
            using var response = await client.PostAsync("/synthetic-review-login", null);
            response.EnsureSuccessStatusCode();
        }
        public static async Task<Host> Start(FileFilterFixture fixture, bool enabled)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(fixture.Actor);
            builder.Services.AddSingleton<ICurrentTenant>(fixture.CurrentTenant);
            builder.Services.AddSingleton<IRedactionService>(new CanonicalFileMetadataRedactionService(new CanonicalRedactionService()));
            builder.Services.AddControllers().AddApplicationPart(typeof(FilesController).Assembly).AddControllersAsServices();
            builder.Services.AddTransient(_ => new FilesController(fixture.Service, null!, null!, null!, enabled ? new Marker() : null));
            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
                options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; });
            builder.Services.AddAuthorization();
            var app = builder.Build();
            app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
            app.MapPost("/synthetic-review-login", async context =>
                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, fixture.ActorId.ToString()) }, "SyntheticReview"))));
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new Host(app, new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(address) });
        }
        public async ValueTask DisposeAsync() { client.Dispose(); await app.DisposeAsync(); }
        private sealed class Marker : IFileFilterReviewHost { }
    }
}
