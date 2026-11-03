using Coglatas.Application.Auth;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Coglatas.Tests.PostgreSql;

// Native service/persistence controls; these do not simulate application-role RLS or HTTP interleaving.
public sealed class PlatformCurrentAuthorityPostgreSqlTests(ITestOutputHelper output)
{
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

    private static Task WithFixtureAsync(Func<Fixture, Task> scenario) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var fixture = await Fixture.CreateAsync(database);
                await scenario(fixture);
            });

    private sealed class Fixture(string database, AppDbContext db, CurrentTenantService tenant,
        User user, Session session, Tenant target) : IAsyncDisposable
    {
        private readonly FixedClock _clock = new();
        public User User { get; } = user;
        public User TrackedUser => db.ChangeTracker.Entries<User>().Single(entry => entry.Entity.Id == user.Id).Entity;
        public Tenant Tenant { get; } = target;
        public RecordingAudit Audit { get; } = new();
        public TenantService Service => new(new TenantRepository(db),
            new TenantAuthorizationService(new TenantRepository(db)), tenant,
            new Principal(user.Id, session.Id), Audit, new EfUnitOfWork(db), Sessions, new TenancyOptions());
        private UserSessionService Sessions => new(new SessionRepository(db), new TenantRepository(db),
            Audit, _clock, new EfUnitOfWork(db));

        public AppDbContext OpenContext() => new(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).Options, tenant);

        public async Task WarmSessionAsync()
        {
            db.ChangeTracker.Clear();
            var validation = await Sessions.ValidateSessionAsync(user.Id, session.Id, null, false);
            Assert.True(validation.IsValid, validation.FailureReason);
            var tracked = db.ChangeTracker.Entries<User>().Single(entry => entry.Entity.Id == user.Id).Entity;
            Assert.Equal(SystemRole.PlatformAdmin, tracked.SystemRole);
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
                ExpiresAt = new FixedClock().UtcNow.AddHours(1), LastSeenAt = new FixedClock().UtcNow };
            var target = new Tenant { Name = "Synthetic native target", DisplayName = "Synthetic native target",
                Slug = "synthetic-" + nonce, Status = TenantStatus.Active };
            db.Users.Add(user);
            db.Sessions.Add(session);
            db.Tenants.Add(target);
            await db.SaveChangesAsync();
            return new Fixture(database, db, tenant, user, session, target);
        }

        public ValueTask DisposeAsync() => db.DisposeAsync();
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

    private sealed class RecordingAudit : IAuditLogger
    {
        public int Calls { get; private set; }
        public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }
}
