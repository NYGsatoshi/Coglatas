using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coglatas.Application.Auth;
using Coglatas.Application.Projects;
using Coglatas.Application.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed class SecurityArchitectureApiCurrentAuthorityTests
{
    [PostgreSqlFact]
    public async Task AnonymousBypassHandlersAreClassifiedAndApplicationOwnedAuthenticationStillRejects()
    {
        var controls = SecurityArchitectureHttpControlRecorder.Create(GetType(), "ACTUAL_TEST_WEB_ENTRY_POINT_AND_MIGRATED_POSTGRESQL");
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var anonymous = await app.CreateAnonymousClientAsync(SecurityCiFixtureSeed.TenantASlug);
            using var member = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
            const string id = "11111111-1111-4111-8111-111111111111";
            var commands = new (string Method, string Route, string Path, object? Body, string Code)[]
            {
                ("GET", "/api/projects/{projectId}/gantt", $"/api/projects/{id}/gantt", null, "GANTT_AUTHENTICATION_REQUIRED"),
                ("GET", "/api/tasks/{taskItemId}/dependencies", $"/api/tasks/{id}/dependencies", null, "TASK_DEPENDENCY_AUTHENTICATION_REQUIRED"),
                ("POST", "/api/tasks/{taskItemId}/dependencies", $"/api/tasks/{id}/dependencies",
                    new { predecessorTaskId = id, dependencyType = "FinishToStart", expectedVersion = 1 }, "TASK_DEPENDENCY_AUTHENTICATION_REQUIRED"),
                ("DELETE", "/api/tasks/{taskItemId}/dependencies/{dependencyId}", $"/api/tasks/{id}/dependencies/{id}?expectedVersion=1", null, "TASK_DEPENDENCY_AUTHENTICATION_REQUIRED"),
                ("PATCH", "/api/tasks/{taskItemId}/progress", $"/api/tasks/{id}/progress", new TaskProgressUpdateRequest(50, 1), "GANTT_AUTHENTICATION_REQUIRED"),
                ("PATCH", "/api/tasks/{taskItemId}/schedule", $"/api/tasks/{id}/schedule", new TaskScheduleUpdateRequest(null, null, null, 1), "GANTT_AUTHENTICATION_REQUIRED")
            };
            foreach (var command in commands)
            {
                using var request = new HttpRequestMessage(new HttpMethod(command.Method), command.Path);
                if (command.Body is not null) request.Content = JsonContent.Create(command.Body);
                using var response = await anonymous.SendAsync(request);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.Equal(command.Code, await ErrorCodeAsync(response));
                controls.Observe(response, command.Route, "ANONYMOUS_WITH_VALID_CSRF", HttpStatusCode.Unauthorized, command.Code);
            }
            // Public credential/token rejection is not a protected-endpoint denial.
            using (var response = await anonymous.PostAsJsonAsync("/api/auth/login", new { email = SecurityCiFixtureSeed.TenantAMemberEmail, password = "synthetic-invalid-password" }))
                controls.Observe(response, "/api/auth/login", "PUBLIC_CREDENTIAL_REJECTED", HttpStatusCode.Unauthorized);
            var absentToken = new string('f', 64);
            using (var response = await anonymous.PostAsJsonAsync("/api/auth/register-by-invite",
                       new RegisterByInviteRequest(absentToken, "Synthetic", "absent@example.test", "synthetic-invalid-password")))
                controls.Observe(response, "/api/auth/register-by-invite", "PUBLIC_CREDENTIAL_REJECTED", HttpStatusCode.NotFound);
            using (var response = await anonymous.PostAsJsonAsync("/api/invites/accept", new AcceptInviteRequest(absentToken, "Synthetic", "synthetic-invalid-password")))
                controls.Observe(response, "/api/invites/accept", "PUBLIC_CREDENTIAL_REJECTED", HttpStatusCode.NotFound);
            using (var response = await anonymous.GetAsync("/api/invites/validate?token=" + absentToken))
                controls.Observe(response, "/api/invites/validate", "PUBLIC_CREDENTIAL_REJECTED", HttpStatusCode.NotFound);
            foreach (var path in new[] { "/api/auth/status", "/api/security/csrf-token", "/api/ui/runtime-config.js", "/health/live", "/health/realtime", "/health/task-deadline-digests" })
            {
                using var response = await anonymous.GetAsync(path);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                if (path == "/api/auth/status")
                {
                    using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    Assert.False(body.RootElement.GetProperty("isAuthenticated").GetBoolean());
                }
                controls.Observe(response, path, "PUBLIC_HANDLER_RESPONSE", HttpStatusCode.OK);
            }
            using (var response = await anonymous.GetAsync("/health/ready"))
            {
                Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable });
                controls.Observe(response, "/health/ready", "PUBLIC_HANDLER_RESPONSE", response.StatusCode);
            }
            using (var response = await member.GetAsync("/api/auth/me"))
                controls.Observe(response, "/api/auth/me", "AUTHORIZED_SESSION_PIPELINE", HttpStatusCode.OK);
        });
        await controls.SaveAsync();
    }

    [PostgreSqlFact]
    public async Task PersistedProjectCreateCapabilityChangesDenyRealHttpWithoutCreationEffects()
    {
        var controls = SecurityArchitectureHttpControlRecorder.Create(GetType(), "ACTUAL_TEST_WEB_ENTRY_POINT_AND_MIGRATED_POSTGRESQL");
        const string route = "/api/workspaces/{workspaceId}/projects";
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            await using var app = await SecurityArchitectureSignalRFixture.StartAsync(database);
            using var member = await app.LoginAsync("member", SecurityCiFixtureSeed.TenantASlug, SecurityCiFixtureSeed.TenantAMemberEmail);
            Guid workspaceId, betaWorkspaceId;
            var grantId = Guid.NewGuid();
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                var workspace = await db.Workspaces.SingleAsync(w => w.Slug == SecurityCiFixtureSeed.TenantAWorkspaceSlug);
                workspaceId = workspace.Id;
                var tenantId = workspace.TenantId;
                betaWorkspaceId = await db.Workspaces.Where(w => w.Slug == SecurityCiFixtureSeed.TenantBWorkspaceSlug).Select(w => w.Id).SingleAsync();
                db.Set<CapabilityGrant>().Add(new CapabilityGrant { Id = grantId, TenantId = tenantId,
                    SubjectUserId = SecurityCiFixtureSeed.TenantAMemberUserId, GrantedByUserId = SecurityCiFixtureSeed.TenantAOwnerUserId,
                    CapabilityKey = CapabilityKeys.ProjectCreate, ScopeType = CapabilityScopeType.Workspace, ScopeId = workspaceId,
                    GrantedAt = DateTimeOffset.UtcNow.AddMinutes(-1), ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) });
                await db.SaveChangesAsync();
            }
            async Task<HttpResponseMessage> CreateAsync(HttpClient client)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/workspaces/{workspaceId:D}/projects");
                request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
                request.Content = JsonContent.Create(new CanonicalCreateProjectRequest("SEC-ARCH synthetic create " + Guid.NewGuid().ToString("N")));
                return await client.SendAsync(request);
            }
            using (var response = await CreateAsync(member))
                controls.Observe(response, route, "AUTHORIZED_SAME_SCOPE", HttpStatusCode.Created);
            var mutations = new (string Control, Action<CapabilityGrant> Mutate)[]
            {
                ("CURRENT_CAPABILITY_REVOKED", grant => grant.RevokedAt = DateTimeOffset.UtcNow),
                ("CURRENT_CAPABILITY_EXPIRED", grant => grant.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)),
                ("CURRENT_CAPABILITY_NOT_YET_VALID", grant => grant.GrantedAt = DateTimeOffset.UtcNow.AddHours(1)),
                ("CURRENT_CAPABILITY_WRONG_SCOPE", grant => grant.ScopeId = betaWorkspaceId),
                ("CURRENT_CAPABILITY_WRONG_SUBJECT", grant => grant.SubjectUserId = SecurityCiFixtureSeed.TenantARestrictedUserId),
                ("CURRENT_CAPABILITY_UNKNOWN_KEY", grant => grant.CapabilityKey = "sec-arch.synthetic.unknown")
            };
            foreach (var mutation in mutations)
            {
                await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    var grant = await db.Set<CapabilityGrant>().SingleAsync(g => g.Id == grantId);
                    grant.RevokedAt = null; grant.GrantedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
                    grant.ExpiresAt = DateTimeOffset.UtcNow.AddHours(1); grant.ScopeId = workspaceId;
                    grant.SubjectUserId = SecurityCiFixtureSeed.TenantAMemberUserId; grant.CapabilityKey = CapabilityKeys.ProjectCreate;
                    mutation.Mutate(grant); grant.VersionNo++;
                    await db.SaveChangesAsync();
                }
                var before = await CountsAsync(database);
                using (var response = await CreateAsync(member))
                {
                    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                    Assert.Equal("CapabilityDenied", await ErrorCodeAsync(response));
                    controls.Observe(response, route, mutation.Control, HttpStatusCode.Forbidden, "CapabilityDenied");
                }
                Assert.Equal(before, await CountsAsync(database));
                await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    var grant = await db.Set<CapabilityGrant>().SingleAsync(g => g.Id == grantId);
                    grant.RevokedAt = null; grant.GrantedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
                    grant.ExpiresAt = DateTimeOffset.UtcNow.AddHours(1); grant.ScopeId = workspaceId;
                    grant.SubjectUserId = SecurityCiFixtureSeed.TenantAMemberUserId; grant.CapabilityKey = CapabilityKeys.ProjectCreate;
                    grant.VersionNo++; await db.SaveChangesAsync();
                }
                using var restored = await CreateAsync(member);
                controls.Observe(restored, route, "AUTHORIZED_RESTORED_SCOPE", HttpStatusCode.Created);
            }
        });
        await controls.SaveAsync();
    }

    private static async Task<(int Projects, int Outbox, int Audit)> CountsAsync(string database)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        return (await db.Projects.CountAsync(), await db.OutboxEvents.CountAsync(), await db.AuditLogs.CountAsync());
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("error").GetProperty("code").GetString();
    }
}

