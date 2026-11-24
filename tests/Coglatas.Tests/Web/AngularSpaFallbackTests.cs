using System.Net;
using System.Text.Json;
using Coglatas.Web;
using Coglatas.Web.Configuration;
using Coglatas.Web.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Coglatas.Tests.Web;

[Trait("Portability", "CrossPlatform")]
public sealed class AngularSpaFallbackTests : IDisposable
{
    private readonly string _webRootPath = Path.Combine(Path.GetTempPath(), "coglatas-angular-fallback-tests", Guid.NewGuid().ToString("N"));

    public AngularSpaFallbackTests()
    {
        Directory.CreateDirectory(_webRootPath);
    }

    [Theory]
    [InlineData("/app")]
    [InlineData("/app/")]
    [InlineData("/app/login")]
    [InlineData("/app/register/invite")]
    [InlineData("/app/workspaces")]
    [InlineData("/app/dashboard")]
    [InlineData("/app/projects")]
    [InlineData("/app/conversations")]
    [InlineData("/app/admin")]
    [InlineData("/app/account")]
    [InlineData("/app/files")]
    [InlineData("/app/notifications")]
    public void UserFacingRoutesCanFallBackToAngularWhenBuildExists(string path)
    {
        WriteAngularBuild();
        var context = CreateContext(path);

        Assert.True(AngularSpaFallback.CanServeAngularFallback(context.Request, _webRootPath));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/login")]
    [InlineData("/register/invite")]
    [InlineData("/workspaces")]
    [InlineData("/api/not-found")]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/healthz")]
    [InlineData("/metrics")]
    [InlineData("/swagger/index.html")]
    [InlineData("/hangfire/jobs")]
    [InlineData("/signin-google")]
    [InlineData("/auth/callback/google")]
    [InlineData("/favicon.ico")]
    public void BackendOwnedRoutesDoNotFallBackToAngular(string path)
    {
        WriteAngularBuild();
        var context = CreateContext(path);

        Assert.False(AngularSpaFallback.CanServeAngularFallback(context.Request, _webRootPath));
    }

    [Fact]
    public void UserFacingRoutesDoNotFallBackToLegacyWwwrootWithoutAngularMarker()
    {
        File.WriteAllText(Path.Combine(_webRootPath, "index.html"), "<html>legacy</html>");
        var context = CreateContext("/app/login");

        Assert.False(AngularSpaFallback.CanServeAngularFallback(context.Request, _webRootPath));
    }

    [Fact]
    public void StaticAssetMissesDoNotFallBackToAngular()
    {
        WriteAngularBuild();
        var context = CreateContext("/app/assets/missing.js");

        Assert.False(AngularSpaFallback.CanServeAngularFallback(context.Request, _webRootPath));
    }

    [Fact]
    public async Task UnknownApiRoutesReturnJsonNotAngularIndex()
    {
        WriteAngularBuild();
        var context = CreateContext("/api/not-found");
        context.Response.Body = new MemoryStream();

        await AngularSpaFallback.HandleAsync(context, _webRootPath);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        Assert.Equal((int)HttpStatusCode.NotFound, context.Response.StatusCode);
        Assert.Contains("application/json", context.Response.ContentType);
        Assert.Contains("\"code\":\"NotFound\"", body);
        Assert.DoesNotContain("Angular", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Feature", "AQ")]
    public async Task AqRouteFallbackUsesTheDocumentedNotFoundEnvelope()
    {
        WriteAngularBuild();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHttpClient();
        await using var app = builder.Build();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.MapGet("/api/v1/core/events/{eventId:guid}/presentations", Results.NoContent);
        app.MapGet("/api/v1/core/events/{eventId:guid}/feedback-settings", () =>
            Results.Json(new { code = "DomainNotFound" }, statusCode: StatusCodes.Status404NotFound));
        AngularSpaFallback.MapEndpointFallback(app, _webRootPath);
        await app.StartAsync();

        using var client = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient();
        client.BaseAddress = new Uri(app.Urls.Single());
        string[] unmatchedSurveyPaths =
        [
            "/api/v1/core/events/malformed-resource-id/presentations",
            "/api/v1/core/presentations/malformed-resource-id/feedback-settings",
            "/api/v1/public/surveys/synthetic-code/unknown-action"
        ];
        foreach (var path in unmatchedSurveyPaths)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var envelope = body.RootElement;
            Assert.False(string.IsNullOrEmpty(envelope.GetProperty("requestId").GetString()));
            Assert.False(string.IsNullOrEmpty(envelope.GetProperty("traceId").GetString()));
            Assert.Equal((int)HttpStatusCode.NotFound, envelope.GetProperty("status").GetInt32());
            var error = envelope.GetProperty("error");
            Assert.Equal("NotFound", error.GetProperty("code").GetString());
            Assert.Equal("Endpoint not found.", error.GetProperty("message").GetString());
            Assert.Equal(JsonValueKind.Null, error.GetProperty("target").ValueKind);
            Assert.Empty(error.GetProperty("details").EnumerateArray());
            Assert.False(error.GetProperty("redactionApplied").GetBoolean());
        }

        using var ordinary = await client.GetAsync("/api/v1/core/ordinary/not-found");
        Assert.Equal(HttpStatusCode.NotFound, ordinary.StatusCode);
        using var ordinaryBody = JsonDocument.Parse(await ordinary.Content.ReadAsStringAsync());
        Assert.Equal("NotFound", ordinaryBody.RootElement.GetProperty("code").GetString());
        Assert.False(ordinaryBody.RootElement.TryGetProperty("error", out _));

        var resourceId = Guid.NewGuid();
        using var domainNotFound = await client.GetAsync($"/api/v1/core/events/{resourceId:D}/feedback-settings");
        Assert.Equal(HttpStatusCode.NotFound, domainNotFound.StatusCode);
        using var domainBody = JsonDocument.Parse(await domainNotFound.Content.ReadAsStringAsync());
        Assert.Equal("DomainNotFound", domainBody.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AngularFallbackHtmlUsesNoCacheHeaders()
    {
        WriteAngularBuild();
        var context = CreateContext("/app/login");
        context.Response.Body = new MemoryStream();

        await AngularSpaFallback.HandleAsync(context, _webRootPath);

        Assert.Equal("text/html; charset=utf-8", context.Response.ContentType);
        Assert.Equal("no-cache, no-store, must-revalidate", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache", context.Response.Headers.Pragma.ToString());
        Assert.Equal("0", context.Response.Headers.Expires.ToString());
    }

    [Fact]
    public async Task EndpointFallbackPreservesMethodNotAllowedForKnownApiRoutes()
    {
        WriteAngularBuild();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");

        await using var app = builder.Build();
        app.MapGet("/api/example", Results.NoContent);
        AngularSpaFallback.MapEndpointFallback(app, _webRootPath);

        await app.StartAsync();
        try
        {
            var addresses = app.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()!
                .Addresses;
            // ReSharper disable once ShortLivedHttpClient
            using var client = new HttpClient();
            client.BaseAddress = new Uri(addresses.Single());
            using var request = new HttpRequestMessage(new HttpMethod("TRACE"), "/api/example");

            using var response = await client.SendAsync(request);
            using var angularResponse = await client.GetAsync("/app/login");
            using var missingApiResponse = await client.GetAsync("/api/not-found");
            var angularBody = await angularResponse.Content.ReadAsStringAsync();
            var missingApiBody = await missingApiResponse.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
            Assert.Contains("GET", response.Content.Headers.Allow);
            Assert.Equal(HttpStatusCode.OK, angularResponse.StatusCode);
            Assert.Contains("Angular", angularBody);
            Assert.Equal(HttpStatusCode.NotFound, missingApiResponse.StatusCode);
            Assert.Contains("\"code\":\"NotFound\"", missingApiBody);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task CsrfProtectionPreservesMethodNotAllowedWithoutBypassingSupportedCommands()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddAntiforgery();
        builder.Services.Configure<SecurityOptions>(options => options.EnableCsrfProtection = true);

        await using var app = builder.Build();
        app.UseMiddleware<CsrfProtectionMiddleware>();
        app.MapPost("/api/example", Results.NoContent);
        AngularSpaFallback.MapEndpointFallback(app, _webRootPath);

        await app.StartAsync();
        try
        {
            var addresses = app.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()!
                .Addresses;
            // ReSharper disable once ShortLivedHttpClient
            using var client = new HttpClient();
            client.BaseAddress = new Uri(addresses.Single());
            using var unsupportedRequest = new HttpRequestMessage(new HttpMethod("QUERY"), "/api/example");

            using var unsupportedResponse = await client.SendAsync(unsupportedRequest);
            using var supportedResponse = await client.PostAsync("/api/example", content: null);

            Assert.Equal(HttpStatusCode.MethodNotAllowed, unsupportedResponse.StatusCode);
            Assert.Contains("POST", unsupportedResponse.Content.Headers.Allow);
            Assert.Equal(HttpStatusCode.Forbidden, supportedResponse.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Theory]
    [InlineData("/app/main-ABC123.js", "text/javascript; charset=utf-8")]
    [InlineData("/app/styles-ABC123.css", "text/css; charset=utf-8")]
    public void StaticJavaScriptAndCssUseCharsetAndImmutableCache(string path, string contentType)
    {
        var context = CreateContext(path);

        AngularSpaFallback.ApplyStaticFileHeaders(context.Response, context.Request.Path);

        Assert.Equal(contentType, context.Response.ContentType);
        Assert.Equal("public, max-age=31536000, immutable", context.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public void StaticIndexUsesNoCacheHeaders()
    {
        var context = CreateContext("/app/index.html");

        AngularSpaFallback.ApplyStaticFileHeaders(context.Response, context.Request.Path);

        Assert.Equal("text/html; charset=utf-8", context.Response.ContentType);
        Assert.Equal("no-cache, no-store, must-revalidate", context.Response.Headers.CacheControl.ToString());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_webRootPath))
            {
                Directory.Delete(_webRootPath, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void WriteAngularBuild()
    {
        File.WriteAllText(Path.Combine(_webRootPath, "index.html"), "<html>Angular</html>");
        File.WriteAllText(Path.Combine(_webRootPath, AngularSpaFallback.BuildMarkerFileName), "marker");
    }

    private static DefaultHttpContext CreateContext(string path, string method = "GET")
    {
        var context = new DefaultHttpContext
        {
            Request = { Method = method, Path = path }
        };
        return context;
    }
}
