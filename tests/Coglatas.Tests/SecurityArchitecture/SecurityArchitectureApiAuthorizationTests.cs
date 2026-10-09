using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed class SecurityArchitectureApiAuthorizationTests
{
    [PostgreSqlFact]
    public async Task EveryComposedProtectedHttpEndpointRejectsAnonymousRequestsAfterValidCsrf()
    {
        var controls = SecurityArchitectureHttpControlRecorder.Create(GetType(), "ACTUAL_TEST_WEB_ENTRY_POINT_AND_MIGRATED_POSTGRESQL");
        var inventory = await SecurityArchitectureApiInventoryTests.ObserveAsync(savePrivateOutput: false);
        var protectedEndpoints = inventory.GetProperty("endpoints").EnumerateArray()
            .Where(row => row.GetProperty("authorizationRequired").GetBoolean() &&
                          row.GetProperty("kind").GetString() != "HUB")
            .Select(row => new
            {
                surfaceId = row.GetProperty("surfaceId").GetString()!,
                path = row.GetProperty("normalizedPath").GetString()!,
                method = row.GetProperty("method").GetString()!,
                contentTypes = row.GetProperty("requestContentTypes").EnumerateArray()
                    .Select(item => item.GetString()).ToArray()
            }).ToArray();
        Assert.Equal(381, protectedEndpoints.Length);
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var member = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug,
                SecurityCiFixtureSeed.TenantAMemberEmail);
            using var anonymous = await app.CreateAnonymousClientAsync(SecurityCiFixtureSeed.TenantASlug);
            using (var positive = await member.GetAsync("/api/auth/me"))
            {
                Assert.Equal(HttpStatusCode.OK, positive.StatusCode);
                controls.Observe(positive, "/api/auth/me", "AUTHORIZED_SESSION_PIPELINE", HttpStatusCode.OK);
            }
            using (var csrf = await anonymous.GetAsync("/api/security/csrf-token"))
                Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
            var observations = new List<object>();
            foreach (var endpoint in protectedEndpoints)
            {
                // Route values are syntactically valid. Authentication must reject
                // before resource lookup/model validation; a CSRF 403 is not credit.
                var path = Regex.Replace(endpoint.path, @"\{[^}]+\}",
                    "11111111-1111-4111-8111-111111111111");
                using var request = new HttpRequestMessage(new HttpMethod(endpoint.method), path);
                if (endpoint.method is "POST" or "PUT" or "PATCH" or "DELETE")
                {
                    if (endpoint.contentTypes.Contains("multipart/form-data"))
                        request.Content = new MultipartFormDataContent();
                    else
                        request.Content = JsonContent.Create(new { });
                }
                using var response = await anonymous.SendAsync(request);
                Assert.True(response.StatusCode == HttpStatusCode.Unauthorized,
                    "Anonymous authentication control failed for " + endpoint.surfaceId +
                    "; observed HTTP " + (int)response.StatusCode);
                observations.Add(new { endpoint.surfaceId, endpoint.path, endpoint.method,
                    observedStatus = (int)response.StatusCode, control = "ANONYMOUS_WITH_VALID_CSRF",
                    runtimeOutcome = "PASS", resourceAuthorizationOutcome = "UNVERIFIED" });
                controls.Observe(response, endpoint.path, "ANONYMOUS_WITH_VALID_CSRF", HttpStatusCode.Unauthorized);
            }
            using (var positive = await member.GetAsync("/api/auth/me"))
                Assert.Equal(HttpStatusCode.OK, positive.StatusCode);
            await SecurityArchitectureInventoryTests.WritePrivateInventoryAsync("api-anonymous-execution.json", new
            {
                schemaVersion = 1, environment = "ACTUAL_TEST_WEB_ENTRY_POINT_AND_MIGRATED_POSTGRESQL",
                endpointCount = observations.Count, observations,
                approval = "DRAFT", contractCompletion = "UNVERIFIED",
                blindSpots = new[] { "This exhausts non-anonymous effective HTTP authorization policies, including role-only policies.",
                    "AllowAnonymous application-owned authentication, resource/tenant/capability and RLS integration remain separate controls.",
                    "Positive auth/me establishes the host/session pipeline, not a successful operation for every endpoint." }
            });
        });
        await controls.SaveAsync();
    }
}
