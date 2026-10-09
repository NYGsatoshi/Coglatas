using Coglatas.Web.Configuration;
using Coglatas.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Coglatas.Tests.Web;

public sealed class RealtimeOriginMiddlewareTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("https://app.example.test", true)]
    [InlineData("https://APP.example.test:443", true)]
    [InlineData("http://app.example.test", false)]
    [InlineData("https://app.example.test:444", false)]
    [InlineData("https://app.example.test.foreign.test", false)]
    [InlineData("https://foreign.example.test", false)]
    [InlineData("null", false)]
    [InlineData("", false)]
    [InlineData("https://user@app.example.test", false)]
    [InlineData("https://app.example.test/path", false)]
    [InlineData("https://app.example.test?query=value", false)]
    [InlineData("https://app.example.test#fragment", false)]
    [InlineData("https://*.example.test", false)]
    [InlineData("https://app.example.test, https://foreign.example.test", false)]
    public async Task HubOriginUsesExactSchemeHostAndPort(string? origin, bool allowed)
    {
        foreach (var path in new[] { "/hubs/app", "/hubs/app/negotiate" })
        {
            var context = Context(path);
            if (origin is not null) context.Request.Headers.Origin = origin;
            await AssertDecisionAsync(context, new SecurityOptions(), allowed);
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task ApprovedCrossOriginHubRequiresCredentialedCorsPermission(bool credentials, bool allowed)
    {
        var context = Context("/hubs/app");
        context.Request.Headers.Origin = "https://console.example.test";
        await AssertDecisionAsync(context, new SecurityOptions
        { AllowedCorsOrigins = ["https://console.example.test"], AllowCorsCredentials = credentials }, allowed);
    }

    [Fact]
    public async Task DuplicateOriginsAndUntrustedForwardedHostCannotSelectAnAuthority()
    {
        var duplicate = Context("/hubs/app");
        duplicate.Request.Headers.Origin = new StringValues(["https://app.example.test", "https://foreign.example.test"]);
        await AssertDecisionAsync(duplicate, new SecurityOptions(), false);
        var forwarded = Context("/hubs/app");
        forwarded.Request.Headers.Origin = "https://foreign.example.test";
        forwarded.Request.Headers["X-Forwarded-Host"] = "foreign.example.test";
        forwarded.Request.Headers["X-Forwarded-Proto"] = "https";
        await AssertDecisionAsync(forwarded, new SecurityOptions(), false);
    }

    [Fact]
    public async Task OtherApiAndSimilarPrefixPathsKeepTheirExistingHttpBoundary()
    {
        foreach (var path in new[] { "/api/auth/me", "/hubs/application" })
        {
            var context = Context(path);
            context.Request.Headers.Origin = "https://foreign.example.test";
            await AssertDecisionAsync(context, new SecurityOptions(), true);
        }
    }

    private static DefaultHttpContext Context(string path) => new()
    {
        Request =
        {
            Scheme = "https",
            Host = new HostString("app.example.test"),
            Path = path
        }
    };

    private static async Task AssertDecisionAsync(HttpContext context, SecurityOptions options, bool allowed)
    {
        var continued = false;
        var middleware = new RealtimeOriginMiddleware(_ =>
        {
            continued = true;
            return Task.CompletedTask;
        }, Options.Create(options));
        await middleware.InvokeAsync(context);
        Assert.Equal(allowed, continued);
        Assert.Equal(allowed ? StatusCodes.Status200OK : StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }
}
