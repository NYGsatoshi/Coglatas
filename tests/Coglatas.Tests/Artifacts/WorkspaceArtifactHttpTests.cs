using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Coglatas.Application.Artifacts;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Workspaces;
using Coglatas.Web.Controllers;
using Coglatas.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coglatas.Tests.Artifacts;

// Real loopback HTTP/cookie adapter with controlled Application dependencies, not Product auth/PostgreSQL proof.
public sealed class WorkspaceArtifactHttpTests
{
    [Fact]
    public async Task NormalMvcDiscoveryNeverActivatesReviewController()
    {
        await using var host = await ReviewHost.StartAsync(includeCandidate: false);
        var feature = new ControllerFeature();
        host.App.Services.GetRequiredService<ApplicationPartManager>().PopulateFeature(feature);
        Assert.DoesNotContain(typeof(WorkspaceArtifactsReviewController).GetTypeInfo(), feature.Controllers);
        Assert.Contains(typeof(ArtifactsController).GetTypeInfo(), feature.Controllers);
        using var response = await host.Client.GetAsync(host.Path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, host.Fixture.Repository.Calls);
    }

    [Fact]
    public async Task AuthorizedPageUsesServerIdentityBoundCursorNoStoreAndCurrentDtoOnly()
    {
        await using var host = await ReviewHost.StartAsync();
        await host.LoginAsync(Guid.NewGuid());
        host.Fixture.Repository.Rows = [host.Fixture.Item(1), host.Fixture.Item(2)];
        using var response = await host.Client.GetAsync(host.Path + "?pageSize=1&tenantId=" + Guid.NewGuid() + "&actorUserId=" + Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("totalCount").ValueKind);
        Assert.True(json.RootElement.GetProperty("hasMore").GetBoolean());
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(host.Fixture.Repository.Rows[0].Id, item.GetProperty("id").GetGuid());
        Assert.Equal(new[] { "artifactType", "createdAt", "createdByUserId", "currentVersionId", "description", "id", "projectId", "status", "title", "updatedAt" },
            item.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(host.Fixture.Tenant.TenantId, host.Fixture.Repository.LastQuery!.TenantId);
        Assert.Equal(host.ActorId, host.Fixture.Repository.LastQuery.ActorUserId);
        Assert.True(host.Fixture.Codec.TryDecode(json.RootElement.GetProperty("nextCursor").GetString()!, out var cursor));
        Assert.Equal(host.ActorId, cursor!.ActorUserId);
    }

    [Fact]
    public async Task CookieActorReplacementCannotReplayOtherActorsAuthenticatedCursor()
    {
        await using var host = await ReviewHost.StartAsync();
        await host.LoginAsync(Guid.NewGuid());
        host.Fixture.Repository.Rows = [host.Fixture.Item(1), host.Fixture.Item(2)];
        using var first = await host.Client.GetAsync(host.Path + "?pageSize=1");
        Assert.Equal(2, host.Fixture.Repository.Calls);
        using var json = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("nextCursor").GetString()!;
        await host.LoginAsync(Guid.NewGuid());
        var callsBeforeInvalidCursor = host.Fixture.Repository.Calls;
        using var denied = await host.Client.GetAsync(host.Path + "?pageSize=1&cursor=" + Uri.EscapeDataString(token));
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        Assert.True(denied.Headers.CacheControl!.NoStore);
        Assert.Equal("{\"error\":\"ArtifactCursorInvalid\"}", await denied.Content.ReadAsStringAsync());
        Assert.Equal(callsBeforeInvalidCursor, host.Fixture.Repository.Calls);
    }

    [Fact]
    public async Task DeniedWorkspaceHidesCursorValidityAndProtectedMetadata()
    {
        await using var host = await ReviewHost.StartAsync();
        await host.LoginAsync(Guid.NewGuid());
        host.Fixture.Repository.Rows = [host.Fixture.Item(1)];
        host.Fixture.Authorization.Allowed = false;
        using var denied = await host.Client.GetAsync(host.Path + "?cursor=invalid");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.True(denied.Headers.CacheControl!.NoStore);
        Assert.Equal("{\"error\":\"Workspace not found.\"}", await denied.Content.ReadAsStringAsync());
        Assert.Equal(0, host.Fixture.Repository.Calls);
    }

    [Fact]
    public async Task AnonymousRequestChallengesBeforeApplicationOrCursorParsing()
    {
        await using var host = await ReviewHost.StartAsync();
        using var response = await host.Client.GetAsync(host.Path + "?cursor=invalid");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, host.Fixture.Repository.Calls);
    }

    private sealed class CandidateFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) =>
            feature.Controllers.Add(typeof(WorkspaceArtifactsReviewController).GetTypeInfo());
    }

    private sealed class ReviewHost : IAsyncDisposable
    {
        internal WebApplication App { get; }
        internal HttpClient Client { get; }
        internal WorkspaceArtifactReviewFixture Fixture { get; }
        internal Guid ActorId { get; private set; }
        internal string Path => $"/api/workspaces/{Fixture.Authorization.WorkspaceId}/artifacts";
        private ReviewHost(WebApplication app, HttpClient client, WorkspaceArtifactReviewFixture fixture)
        { App = app; Client = client; Fixture = fixture; }

        internal static async Task<ReviewHost> StartAsync(bool includeCandidate = true)
        {
            var fixture = new WorkspaceArtifactReviewFixture();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Test" });
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton<IDataProtectionProvider>(fixture.Keys);
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
            {
                options.Events.OnRedirectToLogin = context =>
                { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            });
            builder.Services.AddAuthorization();
            builder.Services.AddScoped<ICurrentUser, CurrentUserService>();
            builder.Services.AddSingleton<ICurrentTenant>(fixture.Tenant);
            builder.Services.AddSingleton<IClock>(fixture.Clock);
            builder.Services.AddSingleton<IWorkspaceArtifactRepository>(fixture.Repository);
            builder.Services.AddSingleton<IWorkspaceAuthorizationService>(fixture.Authorization);
            builder.Services.AddSingleton<IWorkspaceArtifactCursorCodec>(fixture.Codec);
            builder.Services.AddScoped<IWorkspaceArtifactService, WorkspaceArtifactService>();
            var mvc = builder.Services.AddControllers().AddApplicationPart(typeof(ArtifactsController).Assembly);
            if (includeCandidate) mvc.ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new CandidateFeatureProvider()));
            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapPost("/_review/login/{actor:guid}", async (Guid actor, HttpContext context) =>
            {
                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor.ToString())], CookieAuthenticationDefaults.AuthenticationScheme)));
                return Results.NoContent();
            });
            app.MapControllers();
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(address) };
            return new(app, client, fixture);
        }

        internal async Task LoginAsync(Guid actorId)
        {
            using var response = await Client.PostAsync($"/_review/login/{actorId}", null);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            ActorId = actorId;
        }

        public async ValueTask DisposeAsync()
        { Client.Dispose(); await App.DisposeAsync(); }
    }
}
