using Coglatas.Application.Auth;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Integrations;
using Coglatas.Application.Security.Redaction;
using Coglatas.Application.Tenancy;
using Coglatas.Application.TenantAdministration;
using Coglatas.Application.TenantExports;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Infrastructure.Audit;
using Coglatas.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Coglatas.Tests.PostgreSql;

// Native service/persistence controls; these do not simulate application-role RLS or HTTP interleaving.
public sealed class PlatformCurrentAuthorityPostgreSqlTests(ITestOutputHelper output)
{
    private static readonly string[] TenantMutationActions =
        ["PlatformAdminAccessedTenantDetail", "TenantActivated", "TenantSuspended", "TenantUpdated"];

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task CookieValidatedRoleRevocationDeniesPlatformTenantListing() => WithFixtureAsync(async fixture =>
    {
        await fixture.WarmSessionAsync();
        await using (var revoker = fixture.OpenContext())
        {
            var user = await revoker.Users.SingleAsync(item => item.Id == fixture.User.Id);
            user.SystemRole = SystemRole.User;
            await revoker.SaveChangesAsync();
        }

        var result = await fixture.Service.ListPlatformTenantsAsync();
        await using (var observer = fixture.OpenContext())
        {
            var persisted = await observer.Users.AsNoTracking().SingleAsync(item => item.Id == fixture.User.Id);
            output.WriteLine($"Synthetic authority: persisted={persisted.SystemRole}; tracked={fixture.TrackedUser.SystemRole}; success={result.IsSuccess}.");
            Assert.Equal(SystemRole.User, persisted.SystemRole);
        }
        Assert.False(result.IsSuccess);
        Assert.Equal("PlatformAdmin access is required.", result.Error);
        Assert.Null(result.Value);
        Assert.Equal(0, fixture.Audit.Calls);
    });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task ActivePlatformAdministratorRetainsTenantListing() => WithFixtureAsync(async fixture =>
    {
        await fixture.WarmSessionAsync();
        var result = await fixture.Service.ListPlatformTenantsAsync();
        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains(result.Value!, tenant => tenant.Id == fixture.Tenant.Id);
        Assert.Equal(0, fixture.Audit.Calls);
    });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task CommittedPrincipalLifecycleChangesDenyPlatformReadsAndMutationsWithoutEffects()
    {
        foreach (var change in new[] { "normal", "staff", "teacher", "admin", "operator", "suspended", "archived", "deleted" })
        {
            output.WriteLine("Synthetic current platform authority control: " + change);
            await WithFixtureAsync(async fixture =>
            {
                await fixture.WarmSessionAsync();
                await using (var revoker = fixture.OpenContext())
                {
                    var user = await revoker.Users.SingleAsync(item => item.Id == fixture.User.Id);
                    switch (change)
                    {
                        case "normal": user.SystemRole = SystemRole.User; break;
                        case "staff": user.SystemRole = SystemRole.Staff; break;
                        case "teacher": user.SystemRole = SystemRole.Teacher; break;
                        case "admin": user.SystemRole = SystemRole.Admin; break;
                        case "operator": user.SystemRole = SystemRole.PlatformOperator; break;
                        case "suspended": user.Status = UserStatus.Suspended; break;
                        case "archived": user.Status = UserStatus.Archived; break;
                        case "deleted": user.MarkDeleted(fixture.Clock.UtcNow); break;
                    }
                    await revoker.SaveChangesAsync();
                }

                var service = fixture.Service;
                AssertDenied(await service.ListPlatformTenantsAsync());
                AssertDenied(await service.GetPlatformTenantAsync(fixture.Tenant.Id));
                AssertDenied(await service.UpdateTenantAsync(fixture.Tenant.Id,
                    new UpdateTenantRequest("Denied change", "Denied display", null, null)));
                AssertDenied(await service.CreateTenantAsync(new CreateTenantRequest("Denied tenant", "denied-tenant", null, null, null)));
                AssertDenied(await service.SuspendTenantAsync(fixture.Tenant.Id));
                AssertDenied(await service.ActivateTenantAsync(fixture.Tenant.Id));
                AssertDenied(await service.ArchiveTenantAsync(fixture.Tenant.Id));
                Assert.Equal(0, fixture.Audit.Calls);
                await using var read = fixture.OpenContext();
                var target = await read.Tenants.AsNoTracking().SingleAsync(item => item.Id == fixture.Tenant.Id);
                Assert.Equal("Synthetic native target", target.Name);
                Assert.Equal("Synthetic native target", target.DisplayName);
                Assert.Equal(TenantStatus.Active, target.Status);
                Assert.Null(target.DeletedAt);
                Assert.False(await read.Tenants.AnyAsync(item => item.Slug == "denied-tenant"));
                Assert.Empty(await read.AuditLogs.ToListAsync());
            });
        }
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task HealthyPlatformAliasRetainsTenantMutationAndActualScopedAudit() => WithFixtureAsync(async fixture =>
    {
        await fixture.WarmSessionAsync();
        Assert.Equal(SystemRole.PlatformAdmin, SystemRole.SystemAdmin);
        var changed = await fixture.Service.UpdateTenantAsync(fixture.Tenant.Id,
            new UpdateTenantRequest(" Renamed target ", " Renamed display ", null, null));
        Assert.True(changed.IsSuccess, changed.Error);
        Assert.True((await fixture.Service.SuspendTenantAsync(fixture.Tenant.Id)).IsSuccess);
        Assert.True((await fixture.Service.ActivateTenantAsync(fixture.Tenant.Id)).IsSuccess);
        Assert.True((await fixture.Service.GetPlatformTenantAsync(fixture.Tenant.Id)).IsSuccess);
        await using var read = fixture.OpenContext();
        var target = await read.Tenants.AsNoTracking().SingleAsync(item => item.Id == fixture.Tenant.Id);
        Assert.Equal("Renamed target", target.Name);
        Assert.Equal("Renamed display", target.DisplayName);
        Assert.Equal(TenantStatus.Active, target.Status);
        var audit = await read.AuditLogs.OrderBy(item => item.Action).ToListAsync();
        Assert.Equal(TenantMutationActions, audit.Select(item => item.Action));
        foreach (var item in audit)
        {
            Assert.Equal(fixture.Tenant.Id, item.TenantId);
            Assert.Equal(fixture.User.Id, item.ActorUserId);
            Assert.Equal(fixture.Tenant.Id, item.EntityId);
        }
    });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task CurrentReaderPreservesPendingTrackedProfileAndSessionPersistence() => WithFixtureAsync(async fixture =>
    {
        await fixture.WarmSessionAsync();
        var repository = new TenantRepository(fixture.Db);
        var tracked = await repository.GetUserAsync(fixture.User.Id);
        Assert.Same(fixture.TrackedUser, tracked);
        tracked!.DisplayName = "Pending profile change";
        fixture.Db.ChangeTracker.DetectChanges();
        Assert.Equal(EntityState.Modified, fixture.Db.Entry(tracked).State);
        var snapshot = await repository.GetCurrentUserAsync(fixture.User.Id);
        Assert.NotSame(tracked, snapshot);
        Assert.Equal("Synthetic platform administrator", snapshot!.DisplayName);
        Assert.True(await fixture.Authorization.IsPlatformAdminAsync(fixture.User.Id));
        Assert.Same(tracked, fixture.TrackedUser);
        Assert.Equal("Pending profile change", tracked.DisplayName);
        Assert.Equal(EntityState.Modified, fixture.Db.Entry(tracked).State);
        await using (var beforeSave = fixture.OpenContext())
        {
            Assert.Equal("Synthetic platform administrator", (await beforeSave.Users.SingleAsync(item => item.Id == fixture.User.Id)).DisplayName);
            Assert.Equal(fixture.Clock.UtcNow, (await beforeSave.Sessions.SingleAsync()).LastSeenAt);
        }
        await fixture.Db.SaveChangesAsync();
        await using var read = fixture.OpenContext();
        Assert.Equal("Pending profile change", (await read.Users.SingleAsync(item => item.Id == fixture.User.Id)).DisplayName);
        Assert.Empty(await read.AuditLogs.ToListAsync());
    });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task PendingLocalRoleChangesCannotReplacePersistedAuthorityInEitherDirection() => WithFixtureAsync(async fixture =>
    {
        await fixture.WarmSessionAsync();
        var tracked = fixture.TrackedUser;
        tracked.SystemRole = SystemRole.User;
        Assert.True(await fixture.Authorization.IsPlatformAdminAsync(fixture.User.Id));
        Assert.Equal(SystemRole.User, tracked.SystemRole);
        await using (var read = fixture.OpenContext())
            Assert.Equal(SystemRole.PlatformAdmin, (await read.Users.SingleAsync(item => item.Id == fixture.User.Id)).SystemRole);

        await fixture.Db.SaveChangesAsync();
        tracked.SystemRole = SystemRole.PlatformAdmin;
        Assert.False(await fixture.Authorization.IsPlatformAdminAsync(fixture.User.Id));
        AssertDenied(await fixture.Service.ListPlatformTenantsAsync());
        Assert.Equal(SystemRole.PlatformAdmin, tracked.SystemRole);
        await using var persisted = fixture.OpenContext();
        Assert.Equal(SystemRole.User, (await persisted.Users.SingleAsync(item => item.Id == fixture.User.Id)).SystemRole);
        Assert.Empty(await persisted.AuditLogs.ToListAsync());
    });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task CurrentReaderUsesExactIdentityAndCancellationWithoutOtherAdministratorFallback() => WithFixtureAsync(async fixture =>
    {
        await fixture.WarmSessionAsync();
        await using (var demoter = fixture.OpenContext())
        {
            (await demoter.Users.SingleAsync(item => item.Id == fixture.User.Id)).SystemRole = SystemRole.User;
            demoter.Users.Add(new User { DisplayName = "Other administrator", Email = Guid.NewGuid().ToString("N") + "@example.invalid",
                SystemRole = SystemRole.PlatformAdmin, Status = UserStatus.Active });
            await demoter.SaveChangesAsync();
        }
        Assert.False(await fixture.Authorization.IsPlatformAdminAsync(fixture.User.Id));
        Assert.False(await fixture.Authorization.IsPlatformAdminAsync(Guid.Empty));
        Assert.False(await fixture.Authorization.IsPlatformAdminAsync(Guid.NewGuid()));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Authorization.IsPlatformAdminAsync(fixture.User.Id, cancelled.Token));
        Assert.Equal(0, fixture.Audit.Calls);
    });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task CurrentPlatformOverridePreservesIntegrationAndExportThenDeniesDemotedMember() => WithFixtureAsync(async fixture =>
    {
        fixture.CurrentTenant.SetTenant(fixture.Tenant.Id, fixture.Tenant.Slug);
        fixture.Db.TenantUsers.Add(new TenantUser { UserId = fixture.User.Id, Role = TenantUserRole.Member, Status = TenantUserStatus.Active });
        fixture.Db.TenantSettings.Add(new TenantSettings { DisplayName = "Synthetic settings", FeatureFlagsJson = "{\"TenantExport\":false}" });
        await fixture.Db.SaveChangesAsync();
        await fixture.WarmSessionAsync();
        var plans = new TenantPlanRepository(fixture.Db);
        var features = new FeatureFlagService(plans, fixture.CurrentTenant);
        var integrations = new IntegrationService(new IntegrationRepository(fixture.Db), fixture.Authorization, features,
            fixture.CurrentTenant, fixture.CurrentUser, fixture.Clock, new Sha256TokenHasher(), fixture.Audit, new EfUnitOfWork(fixture.Db));
        var exports = new TenantExportService(new TenantExportRepository(fixture.Db, new CanonicalRedactionService()),
            fixture.Authorization, features, fixture.CurrentTenant, fixture.CurrentUser, fixture.Clock, fixture.Audit, new EfUnitOfWork(fixture.Db));
        Assert.False(await features.IsEnabledAsync(FeatureKeys.TenantExport));
        var integration = await integrations.CreateIntegrationAsync(new CreateIntegrationAccountRequest(IntegrationProvider.Other, "Synthetic integration", "{}"));
        Assert.True(integration.IsSuccess, integration.Error);
        var export = await exports.ExportAsync(new TenantExportRequest());
        Assert.True(export.IsSuccess, export.Error);
        Assert.NotEmpty(export.Value!.Content);
        Assert.Equal(ExportJobStatus.Completed, export.Value.Status);
        Assert.True((await exports.GetJobAsync(export.Value.ExportJobId)).IsSuccess);
        int auditCount;
        await using (var before = fixture.OpenContext()) auditCount = await before.AuditLogs.CountAsync();
        await using (var demoter = fixture.OpenContext())
        {
            (await demoter.Users.SingleAsync(item => item.Id == fixture.User.Id)).SystemRole = SystemRole.User;
            await demoter.SaveChangesAsync();
        }
        var deniedIntegration = await integrations.UpdateIntegrationAsync(integration.Value!.Id,
            new UpdateIntegrationAccountRequest("Denied integration mutation", null, null));
        Assert.False(deniedIntegration.IsSuccess);
        Assert.Equal("Forbidden", deniedIntegration.ErrorDetail!.Code);
        Assert.False((await exports.ExportAsync(new TenantExportRequest())).IsSuccess);
        Assert.Equal("Export job not found.", (await exports.GetJobAsync(export.Value.ExportJobId)).Error);
        await using var after = fixture.OpenContext();
        Assert.Equal("Synthetic integration", (await after.IntegrationAccounts.SingleAsync()).DisplayName);
        Assert.Single(await after.ExportJobs.ToListAsync());
        Assert.Equal(auditCount, await after.AuditLogs.CountAsync());
    });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task PersistedRoleRevocationDeniesEveryPlatformAdministrationEntryPoint() => WithFixtureAsync(async fixture =>
    {
        fixture.CurrentTenant.SetTenant(fixture.Tenant.Id, fixture.Tenant.Slug);
        var plan = new Plan { Name = "Existing synthetic plan", Status = PlanStatus.Active };
        fixture.Db.Plans.Add(plan);
        await fixture.Db.SaveChangesAsync();
        await fixture.WarmSessionAsync();
        await using (var demoter = fixture.OpenContext())
        {
            (await demoter.Users.SingleAsync(item => item.Id == fixture.User.Id)).SystemRole = SystemRole.User;
            await demoter.SaveChangesAsync();
        }

        var service = fixture.Administration;
        var request = new UpsertPlanRequest("Unpermitted plan", null, null, null, null, null, null, null, null, null);
        var errors = new[]
        {
            (await service.GetPlatformOverviewAsync()).Error,
            (await service.GetPlatformUsageAsync()).Error,
            (await service.GetPlatformTenantUsageAsync(fixture.Tenant.Id)).Error,
            (await service.ListPlansAsync()).Error,
            (await service.CreatePlanAsync(request)).Error,
            (await service.UpdatePlanAsync(plan.Id, request)).Error,
            (await service.ArchivePlanAsync(plan.Id)).Error,
            (await service.GetTenantSubscriptionAsync(fixture.Tenant.Id)).Error,
            (await service.UpdateTenantSubscriptionAsync(fixture.Tenant.Id,
                new UpdateSubscriptionRequest(plan.Id, SubscriptionStatus.Active, null, null, null))).Error
        };

        Assert.All(errors, error => Assert.Equal("PlatformAdmin access is required.", error));
        Assert.Equal(SystemRole.PlatformAdmin, fixture.CurrentUser.SystemRole);
        Assert.Equal(SystemRole.PlatformAdmin, fixture.TrackedUser.SystemRole);
        Assert.Equal(0, fixture.Audit.Calls);
        await using var read = fixture.OpenContext();
        var persistedPlan = Assert.Single(await read.Plans.AsNoTracking().ToListAsync());
        Assert.Equal("Existing synthetic plan", persistedPlan.Name);
        Assert.Equal(PlanStatus.Active, persistedPlan.Status);
        Assert.Empty(await read.Subscriptions.ToListAsync());
        Assert.Empty(await read.AuditLogs.ToListAsync());
    });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task DemotedPlatformPrincipalCannotReplaceCurrentTenantManagerAuthority() => WithFixtureAsync(async fixture =>
    {
        fixture.CurrentTenant.SetTenant(fixture.Tenant.Id, fixture.Tenant.Slug);
        await fixture.WarmSessionAsync();
        await using (var demoter = fixture.OpenContext())
        {
            (await demoter.Users.SingleAsync(item => item.Id == fixture.User.Id)).SystemRole = SystemRole.User;
            await demoter.SaveChangesAsync();
        }

        var service = fixture.Administration;
        Assert.False((await service.GetCurrentTenantOverviewAsync()).IsSuccess);
        Assert.False((await service.GetCurrentTenantSettingsAsync()).IsSuccess);
        Assert.False((await service.GetCurrentTenantUsageAsync()).IsSuccess);
        Assert.Equal(SystemRole.PlatformAdmin, fixture.CurrentUser.SystemRole);
        await using var read = fixture.OpenContext();
        Assert.Empty(await read.TenantSettings.ToListAsync());
        Assert.Empty(await read.AuditLogs.ToListAsync());
        Assert.Equal(0, fixture.Audit.Calls);
    });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task PersistedPlatformAdministratorRetainsPlanSubscriptionAndTenantOverride() => WithFixtureAsync(async fixture =>
    {
        fixture.CurrentTenant.SetTenant(fixture.Tenant.Id, fixture.Tenant.Slug);
        await fixture.WarmSessionAsync();
        var service = fixture.Administration;
        Assert.True((await service.GetPlatformOverviewAsync()).IsSuccess);
        Assert.True((await service.GetPlatformUsageAsync()).IsSuccess);
        Assert.True((await service.GetPlatformTenantUsageAsync(fixture.Tenant.Id)).IsSuccess);
        var created = await service.CreatePlanAsync(new UpsertPlanRequest(
            "Synthetic platform plan", null, null, null, null, null, null, null, null, PlanStatus.Active));
        Assert.True(created.IsSuccess, created.Error);
        Assert.Contains((await service.ListPlansAsync()).Value!, plan => plan.Id == created.Value!.Id);
        Assert.True((await service.UpdatePlanAsync(created.Value!.Id, new UpsertPlanRequest(
            "Updated platform plan", null, null, null, null, null, null, null, null, null))).IsSuccess);
        Assert.True((await service.UpdateTenantSubscriptionAsync(fixture.Tenant.Id,
            new UpdateSubscriptionRequest(created.Value.Id, SubscriptionStatus.Active, null, null, null))).IsSuccess);
        Assert.True((await service.GetTenantSubscriptionAsync(fixture.Tenant.Id)).IsSuccess);
        Assert.True((await service.ArchivePlanAsync(created.Value.Id)).IsSuccess);

        fixture.CurrentTenant.SetTenant(fixture.Tenant.Id, fixture.Tenant.Slug);
        Assert.True((await service.GetCurrentTenantOverviewAsync()).IsSuccess);
        Assert.True((await service.GetCurrentTenantSettingsAsync()).IsSuccess);
        Assert.True((await service.GetCurrentTenantUsageAsync()).IsSuccess);
        await using var read = fixture.OpenContext();
        Assert.Empty(await read.TenantUsers.ToListAsync());
        Assert.Single(await read.TenantSettings.ToListAsync());
        Assert.Equal(4, await read.AuditLogs.CountAsync());
    });

    private static void AssertDenied(Result result)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal("PlatformAdmin access is required.", result.Error);
    }

    private static void AssertDenied<T>(Result<T> result)
    {
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal("PlatformAdmin access is required.", result.Error);
    }

    private static Task WithFixtureAsync(Func<Fixture, Task> scenario) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var fixture = await Fixture.CreateAsync(database);
                await scenario(fixture);
            });

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _database;
        private readonly Session _session;
        public AppDbContext Db { get; }
        public CurrentTenantService CurrentTenant { get; }
        public FixedClock Clock { get; } = new();
        public User User { get; }
        public User TrackedUser => Db.ChangeTracker.Entries<User>().Single(entry => entry.Entity.Id == User.Id).Entity;
        public Tenant Tenant { get; }
        public Principal CurrentUser { get; }
        public RecordingAudit Audit { get; }
        public TenantAuthorizationService Authorization => new(new TenantRepository(Db));
        public TenantService Service => new(new TenantRepository(Db), Authorization, CurrentTenant,
            CurrentUser, Audit, new EfUnitOfWork(Db), Sessions, new TenancyOptions());
        public TenantAdministrationService Administration
        {
            get
            {
                var plans = new TenantPlanRepository(Db);
                return new TenantAdministrationService(plans, new TenantRepository(Db),
                    new FeatureFlagService(plans, CurrentTenant), new QuotaService(plans),
                    CurrentTenant, CurrentUser, Clock, Audit, new EfUnitOfWork(Db), null!);
            }
        }
        private UserSessionService Sessions => new(new SessionRepository(Db), new TenantRepository(Db),
            Audit, Clock, new EfUnitOfWork(Db));

        private Fixture(string database, AppDbContext db, CurrentTenantService tenant, User user, Session session, Tenant target)
        {
            _database = database;
            _session = session;
            Db = db;
            CurrentTenant = tenant;
            User = user;
            Tenant = target;
            CurrentUser = new Principal(user.Id, session.Id);
            Audit = new RecordingAudit(new DbAuditLogger(db, Clock, CurrentUser, tenant));
        }

        public AppDbContext OpenContext() => new(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_database).Options, CurrentTenant);

        public async Task WarmSessionAsync()
        {
            Db.ChangeTracker.Clear();
            var validation = await Sessions.ValidateSessionAsync(User.Id, _session.Id, null, false);
            Assert.True(validation.IsValid, validation.FailureReason);
            Assert.Equal(SystemRole.PlatformAdmin, TrackedUser.SystemRole);
            await using var read = OpenContext();
            Assert.Equal(Clock.UtcNow, (await read.Sessions.SingleAsync()).LastSeenAt);
        }

        public static async Task<Fixture> CreateAsync(string database)
        {
            var tenant = new CurrentTenantService();
            tenant.SetPlatformScope();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).Options, tenant);
            var nonce = Guid.NewGuid().ToString("N");
            var user = new User { DisplayName = "Synthetic platform administrator", Email = nonce + "@example.test",
                NormalizedEmail = nonce.ToUpperInvariant() + "@EXAMPLE.TEST", PasswordHash = Guid.NewGuid().ToString("N"),
                SystemRole = SystemRole.PlatformAdmin, Status = UserStatus.Active };
            var session = new Session { UserId = user.Id, SessionKeyHash = Guid.NewGuid().ToString("N"),
                ExpiresAt = new FixedClock().UtcNow.AddHours(1), LastSeenAt = new FixedClock().UtcNow.AddHours(-1) };
            var target = new Tenant { Name = "Synthetic native target", DisplayName = "Synthetic native target",
                Slug = "synthetic-" + nonce, Status = TenantStatus.Active };
            db.Users.Add(user);
            db.Sessions.Add(session);
            db.Tenants.Add(target);
            await db.SaveChangesAsync();
            return new Fixture(database, db, tenant, user, session, target);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class Principal(Guid userId, Guid sessionId) : ICurrentUser
    {
        public Guid? UserId => userId;
        public Guid? SessionId => sessionId;
        public string? Email => null;
        public SystemRole? SystemRole => Coglatas.Domain.Enums.SystemRole.PlatformAdmin;
        public bool IsAuthenticated => true;
    }

    private sealed class RecordingAudit(IAuditLogger logger) : IAuditLogger
    {
        public int Calls { get; private set; }
        public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
        {
            Calls++;
            return logger.LogAsync(entry, cancellationToken);
        }
    }
}
