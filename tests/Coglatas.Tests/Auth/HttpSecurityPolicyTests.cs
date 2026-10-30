using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Coglatas.Infrastructure.Files;
using Coglatas.Web.Configuration;
using Coglatas.Web.Middleware;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coglatas.Tests.Auth;

public sealed class HttpSecurityPolicyTests
{
    [Fact]
    [Trait("Scope", "SEC-13")]
    public void AuthenticationCookieUsesCanonicalSecureAttributesAndBoundedLifetime()
    {
        var options = new CookieAuthenticationOptions();
        var security = new SecurityOptions { CookieSecurePolicy = CookieSecurePolicy.Always };

        HttpSecurityPolicy.ConfigureAuthenticationCookie(options, security);

        Assert.Equal(".Coglatas.Auth", options.Cookie.Name);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, options.Cookie.SameSite);
        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.Equal(TimeSpan.FromHours(8), options.ExpireTimeSpan);
        Assert.True(options.SlidingExpiration);
    }

    [Fact]
    [Trait("Scope", "SEC-13")]
    public void HstsPolicyIsBoundedWithoutClaimingSiblingDomains()
    {
        var options = new HstsOptions();

        HttpSecurityPolicy.ConfigureHsts(options);

        Assert.Equal(TimeSpan.FromDays(180), options.MaxAge);
        Assert.False(options.IncludeSubDomains);
        Assert.False(options.Preload);
    }

    [Fact]
    [Trait("Scope", "SEC-13")]
    public void AnonymousRateLimitIdentityIgnoresSpoofedForwardedHeader()
    {
        var context = new DefaultHttpContext
        {
            Connection = { RemoteIpAddress = IPAddress.Parse("203.0.113.10") },
            Request = { Headers = { ["X-Forwarded-For"] = "198.51.100.77" } }
        };

        var key = HttpSecurityPolicy.GetRateLimitPartitionKey(context);

        Assert.Equal("ip:203.0.113.10", key);
        Assert.DoesNotContain("198.51.100.77", key, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scope", "SEC-13")]
    public void AuthenticatedRateLimitIdentityUsesServerAuthenticatedUserId()
    {
        var userId = Guid.NewGuid();
        var context = new DefaultHttpContext
        {
            Connection = { RemoteIpAddress = IPAddress.Parse("203.0.113.10") },
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                "cookie"))
        };

        var key = HttpSecurityPolicy.GetRateLimitPartitionKey(context);

        Assert.Equal($"user:{userId:D}", key);
    }

    [Fact]
    [Trait("Scope", "SEC-13")]
    public async Task LoginPolicyRejectsEleventhAnonymousRequestWithRetryAfter()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddRateLimiter(HttpSecurityPolicy.ConfigureRateLimiting);

        var app = builder.Build();
        // Keep the body-wide privacy oracle independent of random Kestrel trace suffixes.
        app.Use((context, next) =>
        {
            context.TraceIdentifier = "sec13-login-probe";
            return next(context);
        });
        app.UseRouting();
        app.UseRateLimiter();
        app.MapGet("/limited-login", () => Results.Ok(new { status = "OK" }))
            .RequireRateLimiting(HttpSecurityPolicy.LoginRateLimitPolicy);

        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()?.Addresses.Single()
                ?? throw new InvalidOperationException("Test server address was not available.");
            // ReSharper disable once ShortLivedHttpClient
            using var client = new HttpClient();
            client.BaseAddress = new Uri(address);

            for (var requestIndex = 0; requestIndex < 10; requestIndex++)
            {
                using var accepted = await client.GetAsync("/limited-login");
                Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            }

            using var rejected = await client.GetAsync("/limited-login");
            Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
            Assert.True(rejected.Headers.TryGetValues("Retry-After", out var retryAfter));
            Assert.True(int.TryParse(retryAfter.Single(), out var retryAfterSeconds));
            Assert.InRange(retryAfterSeconds, 1, 60);

            var body = await rejected.Content.ReadAsStringAsync();
            Assert.Contains("RateLimitExceeded", body, StringComparison.Ordinal);
            Assert.DoesNotContain("ip:", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("user:", body, StringComparison.OrdinalIgnoreCase);
            using var rejection = JsonDocument.Parse(body);
            Assert.Equal("sec13-login-probe", rejection.RootElement.GetProperty("requestId").GetString());
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Scope", "SEC-13")]
    public async Task RateLimitRejectionPreservesOpaqueTraceWithoutDisclosingPartition(bool authenticated)
    {
        const string opaqueTrace = "sec13-opaque-IP:";
        var userId = Guid.NewGuid();
        var options = new RateLimiterOptions();
        HttpSecurityPolicy.ConfigureRateLimiting(options);
        await using var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = false
        });
        using var acceptedLease = limiter.AttemptAcquire();
        using var rejectedLease = limiter.AttemptAcquire();
        Assert.True(acceptedLease.IsAcquired);
        Assert.False(rejectedLease.IsAcquired);

        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var responseBody = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            TraceIdentifier = opaqueTrace,
            Connection = { RemoteIpAddress = IPAddress.Parse("203.0.113.10") },
            Response = { Body = responseBody, StatusCode = options.RejectionStatusCode }
        };
        if (authenticated)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "cookie"));
        }
        var partitionKey = HttpSecurityPolicy.GetRateLimitPartitionKey(context);

        Assert.NotNull(options.OnRejected);
        await options.OnRejected(new OnRejectedContext { HttpContext = context, Lease = rejectedLease },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.True(int.TryParse(context.Response.Headers["Retry-After"].ToString(), out var retryAfterSeconds));
        Assert.InRange(retryAfterSeconds, 1, 60);
        var body = Encoding.UTF8.GetString(responseBody.ToArray());
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal(new[] { "error", "requestId" },
            root.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(opaqueTrace, root.GetProperty("requestId").GetString());
        var error = root.GetProperty("error");
        Assert.Equal(JsonValueKind.Object, error.ValueKind);
        Assert.Equal(new[] { "code", "message" },
            error.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal("RateLimitExceeded", error.GetProperty("code").GetString());
        Assert.Equal("Too many requests. Retry after the indicated interval.", error.GetProperty("message").GetString());
        Assert.DoesNotContain(partitionKey, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("203.0.113.10", body, StringComparison.Ordinal);
        Assert.DoesNotContain(userId.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Scope", "SEC-13")]
    public void ProductionConfigurationRejectsWildcardHostsUnsafeCorsAndDisabledControls()
    {
        var security = new SecurityOptions
        {
            AllowedCorsOrigins = ["*", "http://localhost:4200"],
            AllowCorsCredentials = true,
            EnableCsrfProtection = false,
            EnableRateLimiting = false
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "*"
            })
            .Build();

        var errors = HttpSecurityPolicy.GetConfigurationErrors(
            security,
            new FileStorageOptions(),
            configuration,
            isProduction: true);

        Assert.Contains(errors, error => error.Contains("AllowedCorsOrigins", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("localhost/loopback", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("EnableCsrfProtection", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("EnableRateLimiting", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("AllowedHosts", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Scope", "SEC-13")]
    public void ProductionConfigurationAcceptsExplicitHttpsOriginAndHostAllowlist()
    {
        var security = new SecurityOptions
        {
            AllowedCorsOrigins = ["https://console.example.test"],
            AllowCorsCredentials = true,
            MaxRequestBodySizeBytes = 64L * 1024 * 1024,
            MaxMultipartBodySizeBytes = 64L * 1024 * 1024,
            EnableCsrfProtection = true,
            EnableRateLimiting = true
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "portal.example.test"
            })
            .Build();

        var errors = HttpSecurityPolicy.GetConfigurationErrors(
            security,
            new FileStorageOptions(),
            configuration,
            isProduction: true);

        Assert.Empty(errors);
    }

    [Fact]
    [Trait("Scope", "SEC-13")]
    public async Task OversizedKnownLengthRequestReturnsCanonical413WithoutExecutingEndpoint()
    {
        var nextCalled = false;
        var middleware = new RequestBodyLimitMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            Options.Create(new SecurityOptions { MaxRequestBodySizeBytes = 1024 }));
        var context = new DefaultHttpContext
        {
            Request = { ContentLength = 1025 },
            Response = { Body = new MemoryStream() }
        };

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        var body = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
        Assert.Contains("RequestTooLarge", body, StringComparison.Ordinal);
        Assert.DoesNotContain("1024", body, StringComparison.Ordinal);
    }
}
