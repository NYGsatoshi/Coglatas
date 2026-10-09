using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Coglatas.SecurityArchitecture;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coglatas.Tests.SecurityArchitecture;

// This is an isolated protocol/identity fixture, not a product service or authentication adapter.
public sealed class SecurityArchitectureServiceTests
{
    private sealed record Credential(string Principal, string Issuer, string Audience,
        string[] Scopes, string Tenant, DateTimeOffset ExpiresAtUtc, string Id);

    [Theory]
    [InlineData("wrong-identity", HttpStatusCode.Unauthorized)]
    [InlineData("wrong-audience", HttpStatusCode.Forbidden)]
    [InlineData("wrong-issuer", HttpStatusCode.Unauthorized)]
    [InlineData("missing-credential", HttpStatusCode.Unauthorized)]
    [InlineData("expired-credential", HttpStatusCode.Unauthorized)]
    [InlineData("excess-scope", HttpStatusCode.Forbidden)]
    [InlineData("missing-scope", HttpStatusCode.Forbidden)]
    [InlineData("cross-tenant-spoof", HttpStatusCode.Forbidden)]
    [InlineData("revoked-credential", HttpStatusCode.Unauthorized)]
    [InlineData("unintended-edge", HttpStatusCode.NotFound)]
    public async Task RealTlsServiceRejectsInvalidIdentityAndScopeWithoutEffects(string mutation, HttpStatusCode expected)
    {
        await using var fixture = await ServiceFixture.StartAsync();
        var alpha = fixture.CreateCredential("alpha");
        var beta = fixture.CreateCredential("beta");
        Assert.Equal(HttpStatusCode.OK, await fixture.SendAsync(alpha));
        Assert.Equal(HttpStatusCode.OK, await fixture.SendAsync(beta));
        Assert.Equal(2, fixture.Effects);
        var altered = mutation switch
        {
            "wrong-identity" => alpha with { Principal = "unknown-service" },
            "wrong-audience" => alpha with { Audience = "wrong-service" },
            "wrong-issuer" => alpha with { Issuer = "untrusted-issuer" },
            "expired-credential" => alpha with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1) },
            "excess-scope" => alpha with { Scopes = ["synthetic.execute", "synthetic.admin"] },
            "missing-scope" => alpha with { Scopes = [] },
            "cross-tenant-spoof" => alpha with { Tenant = "beta" },
            _ => alpha
        };
        if (mutation == "revoked-credential") fixture.Revoke(alpha.Id);
        Assert.Equal(expected, await fixture.SendAsync(mutation == "missing-credential" ? null : altered,
            mutation == "unintended-edge" ? "/synthetic/unintended" : "/synthetic/execute"));
        Assert.Equal(2, fixture.Effects);
        // The same live server still accepts an independently authorized control after the denial.
        Assert.Equal(HttpStatusCode.OK, await fixture.SendAsync(beta));
        Assert.Equal(3, fixture.Effects);
    }

    [Fact]
    public async Task RealTlsListenerRejectsPlainHttpAndWrongCertificateTrust()
    {
        await using var fixture = await ServiceFixture.StartAsync();
        Assert.Equal(HttpStatusCode.OK, await fixture.SendAsync(fixture.CreateCredential("alpha")));
        using var http = fixture.CreateClient("plain");
        var plain = new UriBuilder(fixture.Address) { Scheme = "http" }.Uri;
        try
        {
            using var response = await http.PostAsync(new Uri(plain, "/synthetic/execute"), new StringContent(""));
            Assert.False(response.IsSuccessStatusCode);
        }
        catch (HttpRequestException) { /* TLS-only Kestrel may close a non-TLS connection. */ }
        Assert.Equal(1, fixture.Effects);
        using var untrusted = fixture.CreateClient("untrusted");
        await Assert.ThrowsAsync<HttpRequestException>(() => untrusted.PostAsync(
            new Uri(fixture.Address, "/synthetic/execute"), new StringContent("")));
        Assert.Equal(1, fixture.Effects);
        Assert.Equal(HttpStatusCode.OK, await fixture.SendAsync(fixture.CreateCredential("beta")));
    }

    [Fact]
    public async Task DestinationAndNetworkFixtureDetectsBroadOrUnintendedRules()
    {
        await using var fixture = await ServiceFixture.StartAsync();
        Assert.Equal(HttpStatusCode.OK, await fixture.SendAsync(fixture.CreateCredential("alpha")));
        Assert.True(ExactNetworkRule(fixture.Address, fixture.Address, "127.0.0.1/32"));
        Assert.False(ExactNetworkRule(fixture.Address, fixture.Address, "0.0.0.0/0"));
        Assert.False(ExactNetworkRule(new Uri("https://127.0.0.1:1"), fixture.Address, "127.0.0.1/32"));
        Assert.False(ExactNetworkRule(new Uri("http://127.0.0.1:1"), fixture.Address, "127.0.0.1/32"));
        // This validates synthetic policy structure, not a deployed Kubernetes/network firewall.
    }

    private static bool ExactNetworkRule(Uri destination, Uri allowed, string cidr) =>
        destination.Scheme == "https" && destination == allowed && destination.Host == "127.0.0.1" && cidr == "127.0.0.1/32";

    private sealed class ServiceFixture(WebApplication app, X509Certificate2 certificate,
        Dictionary<string, byte[]> keys, ConcurrentDictionary<string, byte> revoked) : IAsyncDisposable
    {
        private int _effects;
        public int Effects => Volatile.Read(ref _effects);
        public Uri Address { get; private set; } = null!;
        public HttpClient CreateClient(string name) => app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(name);
        public void Revoke(string id) => revoked.TryAdd(id, 0);

        public Credential CreateCredential(string principal) => new(principal, "synthetic-issuer", "synthetic-service",
            ["synthetic.execute"], principal, DateTimeOffset.UtcNow.AddMinutes(5), Guid.NewGuid().ToString("N"));

        public static async Task<ServiceFixture> StartAsync()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            // Schannel needs an imported key association. DefaultKeySet cleans up the temporary key
            // on disposal; do not install a certificate or use PersistKeySet.
            var encoded = generated.Export(X509ContentType.Pkcs12, "");
            var certificate = X509CertificateLoader.LoadPkcs12(encoded, "");
            CryptographicOperations.ZeroMemory(encoded);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Test" });
            builder.Logging.ClearProviders();
            builder.Services.AddHttpClient("plain", c => c.Timeout = TimeSpan.FromSeconds(5));
            builder.Services.AddHttpClient("untrusted", c => c.Timeout = TimeSpan.FromSeconds(5))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => false });
            builder.Services.AddHttpClient("trusted", c => c.Timeout = TimeSpan.FromSeconds(5))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (_, remote, _, errors) => remote is not null &&
                        (errors & SslPolicyErrors.RemoteCertificateNameMismatch) == 0 &&
                        remote.RawData.AsSpan().SequenceEqual(certificate.RawData) &&
                        remote.NotBefore.ToUniversalTime() <= DateTime.UtcNow && remote.NotAfter.ToUniversalTime() > DateTime.UtcNow
                });
            builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
            var app = builder.Build();
            var fixture = new ServiceFixture(app, certificate,
                new() { ["alpha"] = RandomNumberGenerator.GetBytes(32), ["beta"] = RandomNumberGenerator.GetBytes(32) }, new());
            app.MapPost("/synthetic/execute", (HttpContext context) =>
            {
                var token = context.Request.Headers.Authorization.ToString();
                var credential = fixture.Authenticate(token);
                if (credential is null) return Results.Unauthorized();
                if (credential.Audience != "synthetic-service" || credential.Scopes.Length != 1 ||
                    credential.Scopes[0] != "synthetic.execute" || credential.Tenant != credential.Principal)
                    return Results.StatusCode(403);
                Interlocked.Increment(ref fixture._effects);
                return Results.Ok(new { result = "synthetic-accepted" });
            });
            try
            {
                await app.StartAsync();
                fixture.Address = new(app.Services.GetRequiredService<IServer>().Features
                    .Get<IServerAddressesFeature>()!.Addresses.Single());
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        private Credential? Authenticate(string token)
        {
            if (!token.StartsWith("Bearer ", StringComparison.Ordinal) || token.Length > 4096) return null;
            var parts = token[7..].Split('.');
            if (parts.Length != 2) return null;
            try
            {
                var payload = Convert.FromBase64String(parts[0]);
                var signature = Convert.FromBase64String(parts[1]);
                var credential = ContractJson.Read<Credential>(Encoding.UTF8.GetString(payload));
                if (!keys.TryGetValue(credential.Principal, out var key) ||
                    !CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(key, payload), signature) ||
                    credential.Issuer != "synthetic-issuer" || credential.ExpiresAtUtc <= DateTimeOffset.UtcNow ||
                    revoked.ContainsKey(credential.Id)) return null;
                return credential;
            }
            catch (Exception error) when (error is FormatException or JsonException) { return null; }
        }

        public async Task<HttpStatusCode> SendAsync(Credential? credential, string path = "/synthetic/execute")
        {
            using var client = CreateClient("trusted");
            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(Address, path));
            if (credential is not null)
            {
                var payload = JsonSerializer.SerializeToUtf8Bytes(credential, ContractJson.Options);
                var key = keys.GetValueOrDefault(credential.Principal) ?? keys["alpha"];
                message.Headers.Authorization = new("Bearer", Convert.ToBase64String(payload) + "." +
                    Convert.ToBase64String(HMACSHA256.HashData(key, payload)));
            }
            using var response = await client.SendAsync(message);
            return response.StatusCode;
        }

        public async ValueTask DisposeAsync()
        {
            await app.DisposeAsync();
            certificate.Dispose();
            foreach (var key in keys.Values) CryptographicOperations.ZeroMemory(key);
        }
    }
}
