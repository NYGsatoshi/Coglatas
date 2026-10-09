using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Auth;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Audit;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Coglatas.Web.Middleware;
using Coglatas.Web.Security;
using Coglatas.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Coglatas.Tests.SecurityArchitecture;

/// <summary>Opt-in partial HTTP/worker composition. Product startup and role authority remain unverified.</summary>
public sealed class SecurityArchitectureRlsRuntimeTests
{
    [PostgreSqlFact]
    public async Task ValidatedCookieAndCurrentMembershipBindIsolatedEfAndRawSqlTransactions()
    {
        await WithFixtureAsync(async fixture =>
        {
            await using var server = await StartAsync(fixture);
            var address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var handler = new HttpClientHandler { CookieContainer = new(), AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(15) };
            Assert.Equal(HttpStatusCode.Unauthorized, (await RequestAsync(client, "/api/fixture/count", "alpha")).StatusCode);
            var signIn = await client.PostAsync("/fixture/sign-in?key=" + fixture.SignInKey, null);
            Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);

            Assert.Equal(new Counts(1, 1), await CountsAsync(client, "alpha"));
            var verifiedTransactions = fixture.Recorder.Transactions.Count;
            Assert.Equal(HttpStatusCode.Unauthorized, (await RequestAsync(client, "/api/fixture/count", "beta")).StatusCode);
            Assert.Equal(verifiedTransactions, fixture.Recorder.Transactions.Count);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/fixture/sign-in?key=" + fixture.SignInKey, null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await RequestAsync(client, "/api/fixture/count", null)).StatusCode);
            Assert.Equal(verifiedTransactions, fixture.Recorder.Transactions.Count);
            // Cookie rejection signs out the client, so reissue the same fixture principal for the next control.
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/fixture/sign-in?key=" + fixture.SignInKey, null)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await RequestAsync(client, "/api/fixture/own-insert", "alpha")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await RequestAsync(client, "/api/fixture/foreign-insert", "alpha")).StatusCode);
            Assert.Equal(new Counts(1, 1), await CountsAsync(client, "alpha"));
            Assert.Equal(HttpStatusCode.InternalServerError, (await RequestAsync(client, "/api/fixture/rollback", "alpha")).StatusCode);
            Assert.Equal(new Counts(1, 1), await CountsAsync(client, "alpha"));
            Assert.Equal(HttpStatusCode.NoContent, (await RequestAsync(client, "/api/fixture/commit", "alpha")).StatusCode);
            Assert.Equal(new Counts(2, 2), await CountsAsync(client, "alpha"));
            Assert.Equal(2L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(fixture.Database,
                "SELECT count(*) FROM outbox_events WHERE \"TenantId\"=@tenant", ("tenant", fixture.Alpha)));

            // Current persisted membership is required for a legitimate tenant switch and checked again on every request.
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database, """
                INSERT INTO tenant_users ("Id","TenantId","UserId","Role","Status","JoinedAt","CreatedAt")
                VALUES (@id,@tenant,@user,'Member','Active',now(),now())
                """, ("id", Guid.NewGuid()), ("tenant", fixture.Beta), ("user", fixture.Subject));
            Assert.Equal(new Counts(1, 1), await CountsAsync(client, "beta"));
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database,
                "UPDATE tenant_users SET \"Status\"='Suspended' WHERE \"TenantId\"=@tenant AND \"UserId\"=@user",
                ("tenant", fixture.Beta), ("user", fixture.Subject));
            verifiedTransactions = fixture.Recorder.Transactions.Count;
            Assert.Equal(HttpStatusCode.Unauthorized, (await RequestAsync(client, "/api/fixture/count", "beta")).StatusCode);
            Assert.Equal(verifiedTransactions, fixture.Recorder.Transactions.Count);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/fixture/sign-in?key=" + fixture.SignInKey, null)).StatusCode);
            Assert.Equal(new Counts(2, 2), await CountsAsync(client, "alpha"));
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database,
                "UPDATE sessions SET \"RevokedAt\"=now() WHERE \"Id\"=@session", ("session", fixture.Session));
            verifiedTransactions = fixture.Recorder.Transactions.Count;
            Assert.Equal(HttpStatusCode.Unauthorized, (await RequestAsync(client, "/api/fixture/count", "alpha")).StatusCode);
            Assert.Equal(verifiedTransactions, fixture.Recorder.Transactions.Count);
            using var forged = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = new Uri(address) };
            forged.DefaultRequestHeaders.Add("Cookie", "sec_arch_context=forged-unprotected-cookie");
            Assert.Equal(HttpStatusCode.Unauthorized, (await RequestAsync(forged, "/api/fixture/count", "alpha")).StatusCode);
            Assert.Equal(verifiedTransactions, fixture.Recorder.Transactions.Count);
            Assert.All(fixture.Recorder.Transactions, item => Assert.Equal("syntheticAuthenticatedApplication", item.AuthorityKind));
            await AssertResetAsync(fixture.Application, fixture.Recorder);
            await WritePrivateAsync(fixture, "application", new[]
            {
                "SignedCookieValidation", "CurrentMembershipValidation", "UnauthorizedTenantSelection",
                "MissingTenantContext", "EfAndRawSqlSameScope", "RawSqlForeignInsertRlsDenial",
                "RollbackAfterException", "CommitPersistence", "TenantSwitchWithCurrentMembership",
                "MembershipRevocationBeforeScopedTransaction", "SessionRevocationBeforeScopedTransaction",
                "ForgedCookieRejection", "ConnectionReuseAndTransactionReset"
            }, []);
        });
    }

    [PostgreSqlFact]
    public async Task ActualOutboxRepositoryRunsUnderBoundedSyntheticWorkerAndExposesUnscopedAndMutableContextLimits()
    {
        await WithFixtureAsync(async fixture =>
        {
            var scope = new SecurityArchitectureRlsRuntimeContext.VerifiedScope(fixture.Alpha, "syntheticBoundedWorker", null, null);
            await using (var context = SecurityArchitectureRlsRuntimeContext.Create(fixture.Worker, scope, fixture.Recorder))
            {
                var repository = new OutboxEventRepository(context);
                var claimed = await repository.ClaimDueAsync("synthetic-worker", DateTimeOffset.UtcNow, 10, TimeSpan.FromMinutes(5));
                var own = Assert.Single(claimed);
                Assert.Equal(fixture.Alpha, own.TenantId);
                Assert.Equal(OutboxEventStatus.Processing, own.Status);
                // The current repository's delivery read has no transaction of its own. The prototype fails closed.
                context.ChangeTracker.Clear();
                Assert.False(await repository.MarkDeliveredAsync(own.Id, own.LockToken!.Value, DateTimeOffset.UtcNow, "Synthetic"));
                await using var transaction = await context.Database.BeginTransactionAsync();
                Assert.True(await repository.MarkDeliveredAsync(own.Id, own.LockToken!.Value, DateTimeOffset.UtcNow, "Synthetic"));
                await transaction.CommitAsync();
            }
            Assert.Equal("Delivered", await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(fixture.Database,
                "SELECT \"Status\" FROM outbox_events WHERE \"Id\"=@id", ("id", fixture.AlphaEvent)));
            Assert.Equal("Pending", await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(fixture.Database,
                "SELECT \"Status\" FROM outbox_events WHERE \"Id\"=@id", ("id", fixture.BetaEvent)));
            var betaScope = scope with { TenantId = fixture.Beta };
            Guid betaLockToken;
            await using (var context = SecurityArchitectureRlsRuntimeContext.Create(fixture.Worker, betaScope, fixture.Recorder))
            {
                var claimed = Assert.Single(await new OutboxEventRepository(context).ClaimDueAsync("synthetic-beta-worker",
                    DateTimeOffset.UtcNow, 10, TimeSpan.FromMinutes(5)));
                Assert.Equal(fixture.BetaEvent, claimed.Id);
                betaLockToken = claimed.LockToken!.Value;
            }
            await using (var context = SecurityArchitectureRlsRuntimeContext.Create(fixture.Worker, scope, fixture.Recorder))
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                // This is a real Processing row with its current valid lock token, not an unrelated status/token rejection.
                Assert.False(await new OutboxEventRepository(context).MarkDeliveredAsync(fixture.BetaEvent, betaLockToken,
                    DateTimeOffset.UtcNow, "Synthetic"));
                await transaction.CommitAsync();
            }
            await using (var context = SecurityArchitectureRlsRuntimeContext.Create(fixture.Worker, betaScope, fixture.Recorder))
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                Assert.True(await new OutboxEventRepository(context).MarkDeliveredAsync(fixture.BetaEvent, betaLockToken,
                    DateTimeOffset.UtcNow, "Synthetic"));
                await transaction.CommitAsync();
            }
            // A worker retry/recovery operation needs an explicitly owned context transaction as well.
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database, """
                UPDATE outbox_events SET "Status"='Processing',"LockedAt"=now()-interval '1 hour',
                    "LockOwner"='synthetic-stale',"LockToken"=@token WHERE "Id"=@id
                """, ("token", Guid.NewGuid()), ("id", fixture.AlphaEvent));
            await using (var context = SecurityArchitectureRlsRuntimeContext.Create(fixture.Worker, scope, fixture.Recorder))
            {
                var repository = new OutboxEventRepository(context);
                Assert.Equal(0, await repository.RecoverStaleLocksAsync(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow, 10));
                await using var transaction = await context.Database.BeginTransactionAsync();
                Assert.Equal(1, await repository.RecoverStaleLocksAsync(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow, 10));
                await transaction.CommitAsync();
            }
            // The same database role can change a mutable GUC. Record the demonstrated trust limit rather than claiming containment.
            await using (var connection = new NpgsqlConnection(fixture.Worker))
            {
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync();
                await using var set = new NpgsqlCommand("SELECT set_config('coglatas.tenant_id',@tenant,true)", connection, transaction);
                set.Parameters.AddWithValue("tenant", fixture.Beta.ToString());
                await set.ExecuteScalarAsync();
                await using var command = new NpgsqlCommand("SELECT count(*) FROM outbox_events WHERE \"Id\"=@id", connection, transaction);
                command.Parameters.AddWithValue("id", fixture.BetaEvent);
                Assert.Equal(1L, await command.ExecuteScalarAsync());
                await transaction.RollbackAsync();
            }
            Assert.All(fixture.Recorder.Transactions, item => Assert.Equal("syntheticBoundedWorker", item.AuthorityKind));
            await AssertResetAsync(fixture.Worker, fixture.Recorder);
            await WritePrivateAsync(fixture, "worker", new[]
            {
                "ActualRepositoryClaimOwnTenant", "ActualRepositoryDeliveryWithOwnedTransaction",
                "ForeignEventMutationDenied", "StaleLockRecoveryWithOwnedTransaction", "ConnectionReuseAndTransactionReset"
            }, new[] { "UnscopedRepositoryReadsFailClosed", "MutableGucCanSelectArbitraryTenant", "PlatformWorkerDiscoveryAuthorityUnverified" });
        });
    }

    private sealed record Counts(int EfCount, int RawCount);
    private sealed record Fixture(string Database, string Auth, string Application, string Worker, Guid Alpha, Guid Beta,
        Guid Subject, Guid Session, Guid AlphaEvent, Guid BetaEvent, string SignInKey,
        SecurityArchitectureRlsRuntimeContext.Recorder Recorder, string[] Roles);

    private static async Task WithFixtureAsync(Func<Fixture, Task> operation)
    {
        var root = PostgreSqlTestEnvironment.RequireConnectionString();
        var roles = new[] { "sec_arch_auth_" + Guid.NewGuid().ToString("N"), "sec_arch_http_" + Guid.NewGuid().ToString("N"), "sec_arch_worker_" + Guid.NewGuid().ToString("N") };
        var password = Guid.NewGuid().ToString("N");
        try
        {
            await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(root, async database =>
            {
                var alpha = new Tenant { Name = "Synthetic Alpha", DisplayName = "Synthetic Alpha", Slug = "alpha" };
                var beta = new Tenant { Name = "Synthetic Beta", DisplayName = "Synthetic Beta", Slug = "beta" };
                var subject = new User { DisplayName = "Synthetic Subject", Email = "synthetic@example.invalid", NormalizedEmail = "SYNTHETIC@EXAMPLE.INVALID", PasswordHash = "synthetic-unused" };
                var session = new Session { UserId = subject.Id, SessionKeyHash = "synthetic-unused", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), LastSeenAt = DateTimeOffset.UtcNow };
                var alphaEvent = Event(alpha.Id);
                var betaEvent = Event(beta.Id);
                await using (var seed = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    seed.AddRange(alpha, beta, subject, session, alphaEvent, betaEvent,
                        new TenantUser { TenantId = alpha.Id, UserId = subject.Id, Status = TenantUserStatus.Active, JoinedAt = DateTimeOffset.UtcNow });
                    await seed.SaveChangesAsync();
                }
                foreach (var role in roles)
                    await PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
                        $"CREATE ROLE \"{role}\" LOGIN PASSWORD '{password}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT; GRANT USAGE ON SCHEMA public TO \"{role}\";");
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                    GRANT SELECT ON users,sessions,tenants,tenant_users TO "{roles[0]}";
                    GRANT UPDATE ON sessions TO "{roles[0]}";
                    GRANT INSERT ON audit_logs,security_events TO "{roles[0]}";
                    GRANT SELECT,INSERT,UPDATE,DELETE ON outbox_events TO "{roles[1]}","{roles[2]}";
                    GRANT SELECT ("Id","Status","DeletedAt") ON tenants TO "{roles[1]}","{roles[2]}";
                    ALTER TABLE outbox_events ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE outbox_events FORCE ROW LEVEL SECURITY;
                    CREATE POLICY sec_arch_draft_runtime_context ON outbox_events TO "{roles[1]}","{roles[2]}"
                        USING ("TenantId"::text=current_setting('coglatas.tenant_id',true))
                        WITH CHECK ("TenantId"::text=current_setting('coglatas.tenant_id',true));
                    """);
                Assert.Equal(3L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, """
                    SELECT count(*) FROM pg_roles r WHERE rolname=ANY(@roles) AND NOT rolsuper AND NOT rolbypassrls
                        AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolinherit
                        AND NOT EXISTS(SELECT 1 FROM pg_auth_members WHERE member=r.oid)
                        AND NOT EXISTS(SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                            WHERE n.nspname='public' AND c.relowner=r.oid)
                    """, ("roles", roles)));
                Assert.False(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database,
                    "SELECT has_table_privilege(@role,'users','SELECT')", ("role", roles[2])));
                Assert.False(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database,
                    "SELECT has_table_privilege(@role,'users','SELECT')", ("role", roles[1])));
                var fixture = new Fixture(database, Connection(database, roles[0], password), Connection(database, roles[1], password),
                    Connection(database, roles[2], password), alpha.Id, beta.Id, subject.Id, session.Id, alphaEvent.Id, betaEvent.Id,
                    Guid.NewGuid().ToString("N"), new(), roles);
                try { await operation(fixture); }
                finally
                {
                    foreach (var connection in new[] { fixture.Auth, fixture.Application, fixture.Worker })
                    {
                        using var pooled = new NpgsqlConnection(connection);
                        NpgsqlConnection.ClearPool(pooled);
                    }
                }
            });
        }
        finally { await PostgreSqlMigrationTestDatabase.ExecuteAsync(root, "DROP ROLE IF EXISTS " + string.Join(",", roles.Select(role => "\"" + role + "\""))); }
    }

    private static string Connection(string database, string role, string password) =>
        new NpgsqlConnectionStringBuilder(database) { Username = role, Password = password, MaxPoolSize = 1, Multiplexing = false }.ConnectionString;

    private static OutboxEvent Event(Guid tenant) => new(Guid.NewGuid())
    { TenantId = tenant, EventType = "Synthetic", AggregateType = "Synthetic", AggregateId = Guid.NewGuid(), PayloadSchemaVersion = 1, OccurredAt = DateTimeOffset.UtcNow, PayloadJson = "{}", RoutingJson = "{}" };

    private static async Task<WebApplication> StartAsync(Fixture fixture)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Test" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentTenantAccessor, CurrentTenantService>();
        builder.Services.AddScoped<ICurrentTenant>(provider => provider.GetRequiredService<ICurrentTenantAccessor>());
        builder.Services.AddScoped<ICurrentUser, CurrentUserService>();
        builder.Services.AddScoped<AppDbContext>(provider => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.Auth).Options,
            provider.GetRequiredService<ICurrentTenant>()));
        builder.Services.AddScoped<ISessionRepository, SessionRepository>();
        builder.Services.AddScoped<ITenantRepository, TenantRepository>();
        builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        builder.Services.AddScoped<IAuditLogger, DbAuditLogger>();
        builder.Services.AddSingleton<IClock, Coglatas.Infrastructure.Security.SystemClock>();
        builder.Services.AddScoped<IUserSessionService, UserSessionService>();
        builder.Services.AddScoped<ITenantResolver, HttpTenantResolver>();
        builder.Services.Configure<TenancyOptions>(options =>
        {
            options.AppMode = AppMode.SaaS;
            options.TenantResolutionStrategy = TenantResolutionStrategy.HeaderForDevelopmentOnly;
            options.AllowDevelopmentHeaderTenantResolution = true;
        });
        builder.Services.AddScoped<DbSessionCookieAuthenticationEvents>();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.EventsType = typeof(DbSessionCookieAuthenticationEvents);
            options.Cookie.Name = "sec_arch_context";
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });
        builder.Services.AddAuthorization();
        var app = builder.Build();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapPost("/fixture/sign-in", async (HttpContext http) =>
        {
            if (http.Request.Query["key"] != fixture.SignInKey) return Results.NotFound();
            // The private fixture mints a signed principal; it does not claim to verify the product password-login flow.
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, fixture.Subject.ToString()), new Claim("session_id", fixture.Session.ToString())
            }, CookieAuthenticationDefaults.AuthenticationScheme));
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            return Results.NoContent();
        });
        app.MapGet("/api/fixture/{mode}", async (string mode, HttpContext http, ICurrentTenant tenant) =>
        {
            if (!tenant.IsAvailable || tenant.IsPlatformScope) return Results.StatusCode(StatusCodes.Status403Forbidden);
            var scope = SecurityArchitectureRlsRuntimeContext.VerifiedScope.FromValidatedCookie(http.User, tenant);
            await using var context = SecurityArchitectureRlsRuntimeContext.Create(fixture.Application, scope, fixture.Recorder);
            await using var transaction = await context.Database.BeginTransactionAsync(http.RequestAborted);
            try
            {
                if (mode is "foreign-insert" or "own-insert")
                {
                    var targetTenant = mode == "own-insert" ? scope.TenantId : fixture.Beta;
                    Assert.Equal(1, await context.Database.ExecuteSqlInterpolatedAsync($$"""
                        INSERT INTO outbox_events ("Id","TenantId","EventType","PayloadSchemaVersion","AggregateType","AggregateId","OccurredAt","PayloadJson","RoutingJson","Status","AttemptCount","CreatedAt")
                        VALUES ({{Guid.NewGuid()}},{{targetTenant}},'Synthetic',1,'Synthetic',{{Guid.NewGuid()}},now(),'{}','{}','Pending',0,now())
                        """));
                    if (mode == "own-insert")
                    {
                        await transaction.RollbackAsync();
                        return Results.NoContent();
                    }
                    throw new InvalidOperationException("The foreign fixture insert unexpectedly succeeded.");
                }
                if (mode is "rollback" or "commit")
                {
                    context.OutboxEvents.Add(Event(scope.TenantId));
                    await context.SaveChangesAsync();
                    if (mode == "rollback") throw new FixtureRollbackException();
                    await transaction.CommitAsync();
                    return Results.NoContent();
                }
                var ef = await context.OutboxEvents.IgnoreQueryFilters().AsNoTracking().CountAsync();
                await using var command = context.Database.GetDbConnection().CreateCommand();
                command.Transaction = transaction.GetDbTransaction();
                command.CommandText = "SELECT count(*) FROM outbox_events";
                var raw = checked((int)(long)(await command.ExecuteScalarAsync())!);
                await transaction.CommitAsync();
                return Results.Json(new Counts(ef, raw));
            }
            catch (PostgresException error) when (SecurityArchitectureRlsOperationTests.Classify(error) == "RLS_WITH_CHECK")
            {
                await transaction.RollbackAsync();
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (FixtureRollbackException)
            {
                await transaction.RollbackAsync();
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }
        }).RequireAuthorization();
        await app.StartAsync();
        return app;
    }

    private sealed class FixtureRollbackException : Exception;

    private static async Task<HttpResponseMessage> RequestAsync(HttpClient client, string path, string? tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (tenant is not null) request.Headers.Add("X-Tenant-Slug", tenant);
        return await client.SendAsync(request);
    }

    private static async Task<Counts> CountsAsync(HttpClient client, string tenant)
    {
        using var response = await RequestAsync(client, "/api/fixture/count", tenant);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Counts>())!;
    }

    private static async Task AssertResetAsync(string connectionString, SecurityArchitectureRlsRuntimeContext.Recorder recorder)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        Assert.Contains(recorder.Transactions, item => item.BackendProcessId == connection.ProcessID);
        await using var identity = new NpgsqlCommand("SELECT current_user", connection);
        Assert.Equal(new NpgsqlConnectionStringBuilder(connectionString).Username, await identity.ExecuteScalarAsync());
        await using var command = new NpgsqlCommand("SELECT count(*) FROM outbox_events", connection);
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    private static async Task WritePrivateAsync(Fixture fixture, string adapter, IReadOnlyList<string> verifiedControls, IReadOnlyList<string> observedLimits)
    {
        var directory = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_PRIVATE_INVENTORY_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        var candidate = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_CANDIDATE_SHA");
        if (candidate is not null && (candidate.Length != 40 || candidate.Any(character => !Uri.IsHexDigit(character))))
            throw new InvalidOperationException("The candidate SHA must be a full hexadecimal commit identity.");
        var environment = new { dotnetVersion = Environment.Version.ToString(), npgsqlVersion = typeof(NpgsqlConnection).Assembly.GetName().Version!.ToString(),
            postgresVersion = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(fixture.Database, "SHOW server_version"), fixture = "isolated-migrated-postgresql" };
        var digest = static (byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
        var roles = await PostgreSqlMigrationTestDatabase.QueryAsync(fixture.Database, """
            SELECT rolname,rolsuper,rolbypassrls,rolcreatedb,rolcreaterole,rolinherit,
                (SELECT count(*)::int FROM pg_auth_members WHERE member=r.oid),
                (SELECT count(*)::int FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relowner=r.oid)
            FROM pg_roles r WHERE rolname=ANY(@roles) ORDER BY rolname
            """, reader => new
            {
                roleKind = reader.GetString(0) == fixture.Roles[0] ? "syntheticAuthentication" :
                    reader.GetString(0) == fixture.Roles[1] ? "syntheticAuthenticatedApplication" : "syntheticBoundedWorker",
                databaseRole = reader.GetString(0), isSuperuser = reader.GetBoolean(1), bypassRls = reader.GetBoolean(2),
                canCreateDb = reader.GetBoolean(3), canCreateRole = reader.GetBoolean(4), inheritsRoles = reader.GetBoolean(5),
                membershipCount = reader.GetInt32(6), protectedTableOwnershipCount = reader.GetInt32(7)
            }, ("roles", fixture.Roles));
        var receipt = new
        {
            schemaVersion = 1, approval = "DRAFT", ownerApproval = (string?)null, candidateSha = candidate,
            testAssemblyDigest = digest(await File.ReadAllBytesAsync(typeof(SecurityArchitectureRlsRuntimeTests).Assembly.Location)),
            environment, environmentFingerprint = digest(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(environment))),
            executionScope = "ISOLATED_PARTIAL_HTTP_AND_WORKER_COMPOSITION", productRlsAppliedCount = 0,
            productStartupQualification = "UNVERIFIED", applicationRoleEquivalence = "UNVERIFIED", workerRoleEquivalence = "UNVERIFIED",
            preAvaloniaVerdict = "PRE-AVALONIA SEC-ARCH: BLOCKED", adapter, roles, verifiedControls, observedLimits,
            transactionCount = fixture.Recorder.Transactions.Count, physicalBackendCount = fixture.Recorder.Transactions.Select(item => item.BackendProcessId).Distinct().Count(),
            sourceSchemaIdentity = await SecurityArchitectureRlsSchemaIdentity.CaptureAsync(fixture.Database, "outbox_events"),
            blindSpots = new[] { "NarrowAuthenticationAuthorityRequiresOwnerReview", "IdentityAndBootstrapTablesRemainUnprotectedInThisFixture", "OperationalWorkerPlatformDiscoveryRequiresOwnerReview", "MutableTenantGucDoesNotContainArbitrarySql", "ProductControllersAndStartupAreNotComposedByThisFixture", "ConnectionRetryAndAllApplicationAdaptersRemainUnverified" }
        };
        Directory.CreateDirectory(directory);
        await using var output = new FileStream(Path.Combine(directory, "draft-rls-runtime-context-" + adapter + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(output, receipt, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
    }
}
