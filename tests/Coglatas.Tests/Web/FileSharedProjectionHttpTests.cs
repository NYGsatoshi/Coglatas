using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Files;
using Coglatas.Application.Security.Redaction;
using Coglatas.Domain.Enums;
using Coglatas.Tests.Files;
using Coglatas.Web.Controllers;
using Coglatas.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coglatas.Tests.Web;

/// <summary>Loopback HTTP/model-binding tests with synthetic auth and EF InMemory, not PostgreSQL/cookie qualification.</summary>
public sealed class FileSharedProjectionHttpTests
{
    [Fact]
    public async Task CandidateRequiresExplicitTestHostDiscoveryAndDoesNotCreateStarredRoute()
    {
        await using var f = await FileSharedProjectionFixture.CreateAsync();
        await using var app = await ReviewApp.CreateAsync(f, enableCandidate: false);
        Assert.Equal(HttpStatusCode.NotFound, (await app.GetAsync(f.Owner.Id, $"/api/files/shared?workspaceId={f.Workspace.Id}&direction=withMe")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.GetAsync(f.Owner.Id, $"/api/files/starred?workspaceId={f.Workspace.Id}")).StatusCode);
    }

    [Fact]
    public async Task AuthorizedDirectionHasSafeShapeNoRecipientMetadataAndNoCacheStorage()
    {
        await using var f = await FileSharedProjectionFixture.CreateAsync();
        var file = await f.AddFileAsync("synthetic-private-name.txt");
        await f.GrantAsync(file, f.Recipient.Id);
        await using var app = await ReviewApp.CreateAsync(f);
        var response = await app.GetAsync(f.Owner.Id, $"/api/files/shared?workspaceId={f.Workspace.Id}&direction=byMe");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.True(response.Headers.CacheControl.Private);
        Assert.Contains("Cookie", response.Headers.Vary);
        var text = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(text);
        Assert.Equal(1, json.RootElement.GetProperty("totalCount").GetInt32());
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(file.Id, item.GetProperty("id").GetGuid());
        Assert.Equal(file.FileObjectId, item.GetProperty("fileObjectId").GetGuid());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("externalRecipientCount").ValueKind);
        Assert.False(item.TryGetProperty("recipientUserId", out _));
        Assert.False(item.TryGetProperty("grantedByUserId", out _));
        Assert.False(item.TryGetProperty("starredAtUtc", out _));
        Assert.DoesNotContain(f.Recipient.Id.ToString(), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("synthetic-private-name.txt", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("&direction=all")]
    [InlineData("&direction=byMe&actorUserId=00000000-0000-0000-0000-000000000001")]
    [InlineData("&direction=byMe&projectId=00000000-0000-0000-0000-000000000001")]
    public async Task MissingUnsupportedAndAuthoritySelectingParametersFailClosed(string query)
    {
        await using var f = await FileSharedProjectionFixture.CreateAsync();
        await using var app = await ReviewApp.CreateAsync(f);
        var response = await app.GetAsync(f.Owner.Id, $"/api/files/shared?workspaceId={f.Workspace.Id}{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("fileObjectId", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NoAuthenticationAndInaccessibleWorkspaceYieldSafeDenials()
    {
        await using var f = await FileSharedProjectionFixture.CreateAsync();
        await using var app = await ReviewApp.CreateAsync(f);
        var path = $"/api/files/shared?workspaceId={f.Workspace.Id}&direction=byMe";
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.GetAsync(null, path)).StatusCode);
        var response = await app.GetAsync(f.Owner.Id, $"/api/files/shared?workspaceId={f.OtherWorkspace.Id}&direction=byMe");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(f.OtherWorkspace.Id.ToString(), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("totalCount", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HugePageUsesWideOffsetAndDoesNotReportAHiddenCount()
    {
        await using var f = await FileSharedProjectionFixture.CreateAsync();
        var file = await f.AddFileAsync("page.txt");
        await f.GrantAsync(file, f.Recipient.Id);
        await using var app = await ReviewApp.CreateAsync(f);
        var response = await app.GetAsync(f.Owner.Id,
            $"/api/files/shared?workspaceId={f.Workspace.Id}&direction=byMe&page=2147483647&pageSize=2147483647");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(1, json.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(100, json.RootElement.GetProperty("pageSize").GetInt32());
    }

    private sealed class ReviewApp(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public async Task<HttpResponseMessage> GetAsync(Guid? actor, string path)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (actor.HasValue) request.Headers.Add("X-Synthetic-Actor", actor.Value.ToString("D"));
            return await client.SendAsync(request);
        }

        public static async Task<ReviewApp> CreateAsync(FileSharedProjectionFixture fixture, bool enableCandidate = true)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Test" });
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            var mvc = builder.Services.AddControllers(options => options.Filters.Add<StrictQueryParameterFilter>())
                .AddApplicationPart(typeof(FileSharedProjectionController).Assembly);
            if (enableCandidate) mvc.ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new ReviewControllerProvider()));
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton<ICurrentTenant>(fixture.TenantContext);
            builder.Services.AddScoped<ICurrentUser, RequestActor>();
            builder.Services.AddSingleton<IRedactionService>(new CanonicalFileMetadataRedactionService(new CanonicalRedactionService()));
            builder.Services.AddScoped<IFileSharedProjectionService>(services => fixture.Service(services.GetRequiredService<ICurrentUser>()));
            builder.Services.AddAuthentication("SyntheticShared")
                .AddScheme<AuthenticationSchemeOptions, SyntheticAuthHandler>("SyntheticShared", _ => { });
            builder.Services.AddAuthorization();
            var web = builder.Build();
            web.UseAuthentication();
            web.UseAuthorization();
            web.MapControllers();
            await web.StartAsync();
            var address = web.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new ReviewApp(web, new HttpClient { BaseAddress = new Uri(address) });
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed class ReviewControllerProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Clear();
            feature.Controllers.Add(typeof(FileSharedProjectionController).GetTypeInfo());
        }
    }

    private sealed class RequestActor(IHttpContextAccessor context) : ICurrentUser
    {
        public Guid? UserId => Guid.TryParse(context.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        public Guid? SessionId => null;
        public string? Email => null;
        public SystemRole? SystemRole => global::Coglatas.Domain.Enums.SystemRole.User;
        public bool IsAuthenticated => context.HttpContext?.User.Identity?.IsAuthenticated == true;
    }

    private sealed class SyntheticAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Guid.TryParse(Request.Headers["X-Synthetic-Actor"], out var actor))
                return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor.ToString("D"))], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
