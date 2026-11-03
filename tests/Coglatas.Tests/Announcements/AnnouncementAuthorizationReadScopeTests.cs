using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Announcements;

public sealed class AnnouncementAuthorizationReadScopeTests
{
    [Theory]
    [InlineData(QueryTrackingBehavior.TrackAll, false)]
    [InlineData(QueryTrackingBehavior.NoTracking, false)]
    [InlineData(QueryTrackingBehavior.NoTrackingWithIdentityResolution, false)]
    [InlineData(QueryTrackingBehavior.TrackAll, true)]
    [InlineData(QueryTrackingBehavior.NoTracking, true)]
    [InlineData(QueryTrackingBehavior.NoTrackingWithIdentityResolution, true)]
    public async Task CurrentReadsRestoreTrackingAndPreservePendingWritesOnSuccessAndFailure(
        QueryTrackingBehavior previousTracking, bool fail)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"announcement-authority-scope-{Guid.NewGuid():N}").Options;
        await using var db = new AppDbContext(options, new CurrentTenantService());
        var actor = new User { DisplayName = "Persisted", SystemRole = SystemRole.Teacher };
        db.Users.Add(actor);
        await db.SaveChangesAsync();
        actor.DisplayName = "Caller pending";
        db.ChangeTracker.DetectChanges();
        await using (var writer = new AppDbContext(options, new CurrentTenantService()))
        {
            var current = await writer.Users.SingleAsync();
            current.SystemRole = SystemRole.NormalUser;
            await writer.SaveChangesAsync();
        }

        db.ChangeTracker.QueryTrackingBehavior = previousTracking;
        var expectedFailure = new InvalidOperationException("Synthetic authorization read failure");
        var repository = new AnnouncementRepository(db, new FixedClock(), new CurrentTenantService());
        using var cancellation = new CancellationTokenSource();
        var operation = repository.ReadCurrentAuthorizationAsync(async token =>
        {
            await Task.CompletedTask.WaitAsync(token);
            Assert.Equal(cancellation.Token, token);
            Assert.Equal(QueryTrackingBehavior.NoTracking, db.ChangeTracker.QueryTrackingBehavior);
            var current = await new UserRepository(db).GetByIdAsync(actor.Id, token);
            Assert.NotNull(current);
            Assert.NotSame(actor, current);
            Assert.Equal(SystemRole.NormalUser, current.SystemRole);
            Assert.Equal("Persisted", current.DisplayName);
            if (fail) throw expectedFailure;
            return current.Id;
        }, cancellation.Token);

        if (fail) Assert.Same(expectedFailure, await Assert.ThrowsAsync<InvalidOperationException>(() => operation));
        else Assert.Equal(actor.Id, await operation);
        Assert.Equal(previousTracking, db.ChangeTracker.QueryTrackingBehavior);
        Assert.Equal(EntityState.Modified, db.Entry(actor).State);
        Assert.Equal("Caller pending", actor.DisplayName);
        Assert.Equal(SystemRole.Teacher, actor.SystemRole);
        Assert.Same(actor, Assert.Single(db.ChangeTracker.Entries<User>()).Entity);
        Assert.Equal("Persisted", (await db.Users.AsNoTracking().SingleAsync()).DisplayName);
    }

    [Theory]
    [InlineData(QueryTrackingBehavior.TrackAll)]
    [InlineData(QueryTrackingBehavior.NoTracking)]
    [InlineData(QueryTrackingBehavior.NoTrackingWithIdentityResolution)]
    public async Task CancelledReadRestoresTrackingWithoutSavingOrDetaching(QueryTrackingBehavior previousTracking)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"announcement-authority-cancel-{Guid.NewGuid():N}").Options;
        await using var db = new AppDbContext(options, new CurrentTenantService());
        var pending = new User { DisplayName = "Caller pending" };
        db.Users.Add(pending);
        db.ChangeTracker.QueryTrackingBehavior = previousTracking;
        var repository = new AnnouncementRepository(db, new FixedClock(), new CurrentTenantService());
        using var cancellation = new CancellationTokenSource();
        var operation = repository.ReadCurrentAuthorizationAsync(async token =>
        {
            Assert.Equal(QueryTrackingBehavior.NoTracking, db.ChangeTracker.QueryTrackingBehavior);
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return true;
        }, cancellation.Token);

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(previousTracking, db.ChangeTracker.QueryTrackingBehavior);
        Assert.Equal(EntityState.Added, db.Entry(pending).State);
        Assert.Same(pending, Assert.Single(db.ChangeTracker.Entries<User>()).Entity);
        Assert.Empty(await db.Users.AsNoTracking().ToListAsync());
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task NativeReadPreservesCallerTransactionPendingMutationAndRollback() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).Options;
                var tenant = new CurrentTenantService();
                await using var db = new AppDbContext(options, tenant);
                var actor = new User { DisplayName = "Persisted", Email = "scope@example.test", SystemRole = SystemRole.Teacher };
                db.Users.Add(actor);
                await db.SaveChangesAsync();
                await using var transaction = await db.Database.BeginTransactionAsync();
                actor.DisplayName = "Caller pending";
                db.ChangeTracker.DetectChanges();
                await using (var writer = new AppDbContext(options, tenant))
                {
                    var current = await writer.Users.SingleAsync();
                    current.SystemRole = SystemRole.NormalUser;
                    await writer.SaveChangesAsync();
                }

                var repository = new AnnouncementRepository(db, new FixedClock(), tenant);
                var role = await repository.ReadCurrentAuthorizationAsync(async token =>
                    (await new UserRepository(db).GetByIdAsync(actor.Id, token))!.SystemRole);

                Assert.Equal(SystemRole.NormalUser, role);
                Assert.Same(transaction, db.Database.CurrentTransaction);
                Assert.Equal(QueryTrackingBehavior.TrackAll, db.ChangeTracker.QueryTrackingBehavior);
                Assert.Equal(EntityState.Modified, db.Entry(actor).State);
                Assert.Equal("Persisted", (await db.Users.AsNoTracking().SingleAsync()).DisplayName);
                await db.SaveChangesAsync();
                Assert.Equal("Caller pending", (await db.Users.AsNoTracking().SingleAsync()).DisplayName);
                await transaction.RollbackAsync();
                await using var verifier = new AppDbContext(options, tenant);
                var persisted = await verifier.Users.AsNoTracking().SingleAsync();
                Assert.Equal("Persisted", persisted.DisplayName);
                Assert.Equal(SystemRole.NormalUser, persisted.SystemRole);
            });

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);
    }
}
