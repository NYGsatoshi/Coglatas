using System.Data;
using System.Net;
using System.Net.Http.Json;
using Coglatas.Application.Auth;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Tests.PostgreSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed partial class SecurityArchitectureRlsRuntimeTests
{
    [PostgreSqlFact]
    public Task RealSerializationConflictRetriesWholeTransactionWithFrozenTenantAndNoPartialWrites() => VerifyRetryAsync("none");

    [PostgreSqlFact]
    public Task PersistedSessionRevocationStopsRetryBeforeAnotherScopedTransaction() => VerifyRetryAsync("session");

    [PostgreSqlFact]
    public Task PersistedMembershipRevocationStopsRetryBeforeAnotherScopedTransaction() => VerifyRetryAsync("membership");

    private sealed record RetryObservation(int Attempts, int AuthorityChecks, string? AuthorityFailureReason,
        int StagedWrites, int Rollbacks, int Commits, string? SqlState, string? ServerRoutine, Guid RetryEventId);

    private static async Task VerifyRetryAsync(string revocation)
    {
        await WithFixtureAsync(async fixture =>
        {
            await using var server = await StartAsync(fixture);
            var address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var handler = new HttpClientHandler();
            handler.CookieContainer = new();
            handler.AllowAutoRedirect = false;
            using var client = new HttpClient(handler);
            client.BaseAddress = new Uri(address);
            client.Timeout = TimeSpan.FromSeconds(20);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/fixture/sign-in?key=" + fixture.SignInKey, null)).StatusCode);
            using var response = await RequestAsync(client, "/api/fixture-retry/" + revocation, "alpha");
            Assert.True(response.StatusCode == (revocation == "none" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized),
                "Unexpected fixture response: " + await response.Content.ReadAsStringAsync());
            var observation = (await response.Content.ReadFromJsonAsync<RetryObservation>())!;
            Assert.Equal(2, observation.Attempts);
            Assert.Equal(2, observation.AuthorityChecks);
            Assert.Equal(1, observation.Rollbacks);
            Assert.Equal(PostgresErrorCodes.SerializationFailure, observation.SqlState);
            // PostgreSQL generated the conflict; a user-raised SQLSTATE is not accepted as retry evidence.
            Assert.NotNull(observation.ServerRoutine);
            Assert.NotEqual("exec_stmt_raise", observation.ServerRoutine);
            Assert.Equal(revocation == "none" ? 2 : 1, observation.StagedWrites);
            Assert.Equal(revocation == "none" ? 1 : 0, observation.Commits);
            Assert.Equal(revocation == "none" ? 1L : 0L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(fixture.Database,
                "SELECT count(*) FROM outbox_events WHERE \"Id\"=@id", ("id", observation.RetryEventId)));
            Assert.Equal(revocation == "none" ? 2 : 1, await PostgreSqlMigrationTestDatabase.ScalarAsync<int>(fixture.Database,
                "SELECT \"AttemptCount\" FROM outbox_events WHERE \"Id\"=@id", ("id", fixture.AlphaEvent)));
            Assert.Equal(0, await PostgreSqlMigrationTestDatabase.ScalarAsync<int>(fixture.Database,
                "SELECT \"AttemptCount\" FROM outbox_events WHERE \"Id\"=@id", ("id", fixture.BetaEvent)));
            Assert.Equal(revocation == "session" ? "SessionRevoked" : revocation == "membership" ? "TenantMembershipInactive" : null,
                observation.AuthorityFailureReason);
            Assert.Equal(revocation == "none" ? 2 : 1, fixture.Recorder.Transactions.Count);
            await AssertResetAsync(fixture.Application, fixture.Recorder);
            await WritePrivateAsync(fixture, "retry-" + revocation,
                ["ActualPostgresSerializationConflict", "WholeTransactionRollback", "FreshPersistedAuthorityPerAttempt",
                    "FrozenValidatedTenant", "NoPartialOrDuplicateWrites", "ConnectionReuseAndTransactionReset"],
                ["ProductRetryCompositionUnverified", "AuthenticationRoleAuthorityRequiresOwnerReview"], observation);
        });
    }

    private static void AddRetryEndpoint(WebApplication app, Fixture fixture)
    {
        app.Use(async (http, next) =>
        {
            try { await next(http); }
            catch (Exception error) when (http.Request.Path.StartsWithSegments("/api/fixture-retry"))
            {
                var chain = new List<object>();
                for (Exception? current = error; current is not null; current = current.InnerException)
                    chain.Add(new { type = current.GetType().FullName, sqlState = (current as PostgresException)?.SqlState,
                        routine = (current as PostgresException)?.Routine, message = current is PostgresException ? null : current.Message });
                http.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await http.Response.WriteAsJsonAsync(new { unexpectedFixtureError = chain });
            }
        });
        app.MapGet("/api/fixture-retry/{revocation}", async (string revocation, HttpContext http, ICurrentTenantAccessor tenant) =>
        {
            if (revocation is not ("none" or "session" or "membership")) return Results.NotFound();
            var frozen = SecurityArchitectureRlsRuntimeContext.VerifiedScope.FromValidatedCookie(http.User, tenant);
            await using var strategyContext = SecurityArchitectureRlsRuntimeContext.Create(fixture.Application, frozen,
                fixture.Recorder, retryingStrategy: true);
            var strategy = strategyContext.Database.CreateExecutionStrategy();
            var attempts = 0;
            var authorityChecks = 0;
            var stagedWrites = 0;
            var rollbacks = 0;
            var commits = 0;
            string? failureReason = null;
            string? sqlState = null;
            string? routine = null;
            var retryEvent = Guid.NewGuid();
            await strategy.ExecuteAsync(async () =>
            {
                attempts++;
                // Each attempt uses a fresh authentication context. Tracked session/membership rows cannot conceal revocation.
                using var freshAuthority = app.Services.CreateScope();
                freshAuthority.ServiceProvider.GetRequiredService<ICurrentTenantAccessor>().SetTenant(frozen.TenantId, "synthetic-revalidated-scope");
                var current = await freshAuthority.ServiceProvider.GetRequiredService<IUserSessionService>().ValidateSessionAsync(
                    frozen.SubjectId!.Value, frozen.SessionId!.Value, frozen.TenantId, true, http.RequestAborted);
                authorityChecks++;
                if (!current.IsValid)
                {
                    failureReason = current.FailureReason;
                    return;
                }
                await using var context = SecurityArchitectureRlsRuntimeContext.Create(fixture.Application, frozen, fixture.Recorder,
                    retryingStrategy: true);
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, http.RequestAborted);
                try
                {
                    var existing = await context.OutboxEvents.SingleAsync(item => item.Id == fixture.AlphaEvent, http.RequestAborted);
                    var staged = Event(frozen.TenantId);
                    staged.Id = retryEvent;
                    context.OutboxEvents.Add(staged);
                    await context.SaveChangesAsync(http.RequestAborted);
                    stagedWrites++;
                    if (attempts == 1)
                    {
                        // A separate committed writer invalidates this transaction's real SERIALIZABLE snapshot.
                        await PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database,
                            "UPDATE outbox_events SET \"AttemptCount\"=\"AttemptCount\"+1 WHERE \"Id\"=@id", ("id", fixture.AlphaEvent));
                    }
                    existing.AttemptCount++;
                    await context.SaveChangesAsync(http.RequestAborted);
                    await transaction.CommitAsync(http.RequestAborted);
                    commits++;
                }
                catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure } postgres)
                {
                    sqlState = postgres.SqlState;
                    routine = postgres.Routine;
                    await transaction.RollbackAsync(CancellationToken.None);
                    rollbacks++;
                    // Request resolution is mutable, but the captured verified tenant must remain unchanged across retries.
                    tenant.SetTenant(fixture.Beta, "synthetic-mutated-request-resolution");
                    if (revocation == "session")
                        await PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database,
                            "UPDATE sessions SET \"RevokedAt\"=now() WHERE \"Id\"=@id", ("id", fixture.Session));
                    if (revocation == "membership")
                        await PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database,
                            "UPDATE tenant_users SET \"Status\"='Suspended' WHERE \"TenantId\"=@tenant AND \"UserId\"=@user",
                            ("tenant", fixture.Alpha), ("user", fixture.Subject));
                    throw;
                }
            });
            var result = new RetryObservation(attempts, authorityChecks, failureReason, stagedWrites, rollbacks, commits, sqlState, routine, retryEvent);
            return Results.Json(result, statusCode: failureReason is null ? StatusCodes.Status200OK : StatusCodes.Status401Unauthorized);
        }).RequireAuthorization();
    }
}
