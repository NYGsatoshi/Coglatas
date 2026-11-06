using System.Text.Json;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.PostgreSql;

public sealed class OutboxClaimBoundaryPostgreSqlTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 0, 0, 0, TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task ClaimSkipsASeparatelyLockedRowAndPersistsOnlyItsOwnClaim()
    {
        await WithDatabaseAsync(async (connection, alpha, _) =>
        {
            var locked = Event(alpha, OutboxEventStatus.Pending);
            var available = Event(alpha, OutboxEventStatus.RetryScheduled);
            available.NextAttemptAt = Now;
            await SeedAsync(connection, locked, available);
            await using var owner = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection);
            await using var transaction = await owner.Database.BeginTransactionAsync();
            await owner.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM outbox_events WHERE \"Id\" = {locked.Id} FOR UPDATE");
            await using (var worker = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection))
            {
                var claimed = Assert.Single(await new OutboxEventRepository(worker)
                    .ClaimDueAsync("synthetic-worker", Now, 100, TimeSpan.FromMinutes(1)));
                Assert.Equal(available.Id, claimed.Id);
                Assert.NotNull(claimed.LockToken);
                Assert.Equal("synthetic-worker", claimed.LockOwner);
                Assert.Equal(Now, claimed.LockedAt);
                Assert.Null(claimed.NextAttemptAt);
            }
            await using (var observed = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection))
            {
                Assert.Equal(OutboxEventStatus.Pending, (await observed.OutboxEvents.SingleAsync(e => e.Id == locked.Id)).Status);
                var claimed = await observed.OutboxEvents.SingleAsync(e => e.Id == available.Id);
                Assert.Equal(OutboxEventStatus.Processing, claimed.Status);
                Assert.NotNull(claimed.LockToken);
            }
            await transaction.RollbackAsync();
        });
    }

    [PostgreSqlFact]
    public async Task WrongClaimAndForeignTenantCannotAcknowledgeFailOrReleasePersistedWork()
    {
        await WithDatabaseAsync(async (connection, alpha, beta) =>
        {
            var item = Event(alpha, OutboxEventStatus.Processing);
            await SeedAsync(connection, item);
            var before = await SnapshotAsync(connection, item.Id);
            foreach (var foreign in new[] { false, true })
            {
                var scope = new CurrentTenantService();
                scope.SetTenant(foreign ? beta : alpha, "synthetic");
                await using var request = new AppDbContext(
                    new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options, scope);
                var repository = new OutboxEventRepository(request);
                var token = foreign ? item.LockToken!.Value : Guid.NewGuid();
                Assert.False(await repository.MarkDeliveredAsync(item.Id, token, Now, "Synthetic"));
                Assert.False(await repository.MarkFailureAsync(item.Id, token, Now, true, Now, "Synthetic", "Synthetic", 10));
                Assert.False(await repository.ReleaseAsync(item.Id, token, Now));
                Assert.Empty(request.ChangeTracker.Entries<OutboxEvent>());
                Assert.Equal(before, await SnapshotAsync(connection, item.Id));
            }
        });
    }

    [PostgreSqlFact]
    public async Task DeliveryAndReleasePersistTheirOwnOutcomeAndInvalidateTheOldClaim()
    {
        await WithDatabaseAsync(async (connection, alpha, _) =>
        {
            foreach (var deliver in new[] { true, false })
            {
                var item = Event(alpha, OutboxEventStatus.Processing);
                await SeedAsync(connection, item);
                var persisted = await ReadAsync(connection, item.Id);
                var token = item.LockToken!.Value;
                await using (var worker = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection))
                {
                    var repository = new OutboxEventRepository(worker);
                    Assert.True(deliver
                        ? await repository.MarkDeliveredAsync(item.Id, token, Now, "AuthorizationSuppressed")
                        : await repository.ReleaseAsync(item.Id, token, Now.AddMinutes(1)));
                    Assert.False(await repository.MarkDeliveredAsync(item.Id, token, Now, "Stale"));
                    Assert.False(await repository.ReleaseAsync(item.Id, token, Now));
                }
                await using var observed = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection);
                var actual = await observed.OutboxEvents.SingleAsync(e => e.Id == item.Id);
                Assert.Equal(deliver ? OutboxEventStatus.Delivered : OutboxEventStatus.Pending, actual.Status);
                Assert.Equal(deliver ? Now : null, actual.DeliveredAt);
                Assert.Equal(deliver ? null : Now.AddMinutes(1), actual.NextAttemptAt);
                Assert.Equal(deliver ? "AuthorizationSuppressed" : item.LastErrorCode, actual.LastErrorCode);
                Assert.Equal(deliver ? null : item.LastErrorSummary, actual.LastErrorSummary);
                Assert.Equal(item.AttemptCount, actual.AttemptCount);
                Assert.Equal(persisted.PayloadJson, actual.PayloadJson);
                Assert.Equal(persisted.RoutingJson, actual.RoutingJson);
                AssertClearedLock(actual);
            }
        });
    }

    [PostgreSqlFact]
    public async Task FailurePersistsBoundedDiagnosticsAndRetryOrTerminalStateFromTheCurrentClaim()
    {
        await WithDatabaseAsync(async (connection, alpha, _) =>
        {
            foreach (var terminal in new[] { false, true })
            {
                var item = Event(alpha, OutboxEventStatus.Processing);
                item.AttemptCount = terminal ? 9 : 0;
                await SeedAsync(connection, item);
                var persisted = await ReadAsync(connection, item.Id);
                await using (var worker = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection))
                {
                    Assert.True(await new OutboxEventRepository(worker).MarkFailureAsync(item.Id, item.LockToken!.Value,
                        Now, true, null, terminal ? "   " : new string('c', 105), new string('s', 1005), 10));
                }
                await using var observed = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection);
                var actual = await observed.OutboxEvents.SingleAsync(e => e.Id == item.Id);
                Assert.Equal(terminal ? OutboxEventStatus.DeadLetter : OutboxEventStatus.RetryScheduled, actual.Status);
                Assert.Equal(terminal ? 10 : 1, actual.AttemptCount);
                Assert.Equal(terminal ? Now : null, actual.DeadLetteredAt);
                Assert.Equal(terminal ? null : Now, actual.NextAttemptAt);
                Assert.Equal(terminal ? "Unknown" : new string('c', 100), actual.LastErrorCode);
                Assert.Equal(new string('s', 1000), actual.LastErrorSummary);
                Assert.Equal(persisted.PayloadJson, actual.PayloadJson);
                Assert.Equal(persisted.RoutingJson, actual.RoutingJson);
                AssertClearedLock(actual);
            }
        });
    }

    [PostgreSqlFact]
    public async Task RecoveryChangesOnlyExpiredProcessingClaimsAndHonorsTheAttemptLimit()
    {
        await WithDatabaseAsync(async (connection, alpha, _) =>
        {
            var retry = Event(alpha, OutboxEventStatus.Processing);
            var terminal = Event(alpha, OutboxEventStatus.Processing);
            terminal.AttemptCount = 9;
            var boundary = Event(alpha, OutboxEventStatus.Processing);
            boundary.LockedAt = Now.AddMinutes(-1);
            var pending = Event(alpha, OutboxEventStatus.Pending);
            await SeedAsync(connection, retry, terminal, boundary, pending);
            var boundaryBefore = await SnapshotAsync(connection, boundary.Id);
            var pendingBefore = await SnapshotAsync(connection, pending.Id);
            await using (var worker = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection))
            {
                var repository = new OutboxEventRepository(worker);
                Assert.Equal(2, await repository.RecoverStaleLocksAsync(Now.AddMinutes(-1), Now, 10));
                Assert.Equal(0, await repository.RecoverStaleLocksAsync(Now.AddMinutes(-1), Now, 10));
            }
            Assert.Equal(boundaryBefore, await SnapshotAsync(connection, boundary.Id));
            Assert.Equal(pendingBefore, await SnapshotAsync(connection, pending.Id));
            await using var observed = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection);
            var retried = await observed.OutboxEvents.SingleAsync(e => e.Id == retry.Id);
            var exhausted = await observed.OutboxEvents.SingleAsync(e => e.Id == terminal.Id);
            Assert.Equal(OutboxEventStatus.RetryScheduled, retried.Status);
            Assert.Equal(1, retried.AttemptCount);
            Assert.Equal(Now, retried.NextAttemptAt);
            Assert.Null(retried.DeadLetteredAt);
            Assert.Equal(OutboxEventStatus.DeadLetter, exhausted.Status);
            Assert.Equal(10, exhausted.AttemptCount);
            Assert.Equal(Now, exhausted.DeadLetteredAt);
            Assert.Null(exhausted.NextAttemptAt);
            foreach (var item in new[] { retried, exhausted })
            {
                Assert.Equal("StaleProcessingLock", item.LastErrorCode);
                Assert.Equal("The dispatcher processing lock expired before completion.", item.LastErrorSummary);
                AssertClearedLock(item);
            }
        });
    }

    private static void AssertClearedLock(OutboxEvent item)
    {
        Assert.Null(item.LockedAt);
        Assert.Null(item.LockOwner);
        Assert.Null(item.LockToken);
    }

    private static OutboxEvent Event(Guid tenant, OutboxEventStatus status) => new(Guid.NewGuid())
    {
        TenantId = tenant, EventType = "Synthetic.ClaimBoundary.v1", AggregateType = "Synthetic",
        AggregateId = Guid.NewGuid(), PayloadSchemaVersion = 1, OccurredAt = Now,
        PayloadJson = "{\"synthetic\":true}", RoutingJson = "[]", Status = status,
        LockedAt = status == OutboxEventStatus.Processing ? Now.AddMinutes(-2) : null,
        LockOwner = status == OutboxEventStatus.Processing ? "synthetic-old-worker" : null,
        LockToken = status == OutboxEventStatus.Processing ? Guid.NewGuid() : null,
        LastErrorCode = "PreviousSynthetic", LastErrorSummary = "Previous synthetic diagnostic."
    };

    private static async Task SeedAsync(string connection, params OutboxEvent[] events)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection);
        db.OutboxEvents.AddRange(events);
        await db.SaveChangesAsync();
    }

    private static async Task<OutboxEvent> ReadAsync(string connection, Guid eventId)
    {
        await using var observed = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection);
        return await observed.OutboxEvents.AsNoTracking().SingleAsync(e => e.Id == eventId);
    }

    private static async Task<string> SnapshotAsync(string connection, Guid eventId) =>
        JsonSerializer.Serialize(await ReadAsync(connection, eventId));

    private static Task WithDatabaseAsync(Func<string, Guid, Guid, Task> test) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async connection =>
            {
                await using var setup = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connection);
                var alpha = new Tenant { Name = "Synthetic Alpha", DisplayName = "Synthetic Alpha", Slug = "claim-alpha" };
                var beta = new Tenant { Name = "Synthetic Beta", DisplayName = "Synthetic Beta", Slug = "claim-beta" };
                setup.AddRange(alpha, beta);
                await setup.SaveChangesAsync();
                await test(connection, alpha.Id, beta.Id);
            });
}
