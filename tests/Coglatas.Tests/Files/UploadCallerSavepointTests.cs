using System.Data.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coglatas.Tests.Files;

[Collection("PostgreSqlTaskV1")]
public sealed class UploadCallerSavepointTests
{
    [PostgreSqlFact]
    public Task AcknowledgedSavepointRollbackPreventsLaterUploadPersistenceAndKeepsTrackedAuthority() =>
        WithDatabaseAsync(async database =>
        {
            var fault = new SaveAcknowledgementFailure();
            await using var context = Context(database, fault);
            var authority = NewTenant("authority");
            context.Tenants.Add(authority);
            await context.SaveChangesAsync(CancellationToken.None);
            await using var caller = await context.Database.BeginTransactionAsync(CancellationToken.None);
            var unit = new EfUnitOfWork(context);
            var outcome = unit.BeginUploadSave();
            var upload = NewTenant("upload");
            context.Tenants.Add(upload);
            authority.Name = "Uncommitted upload-side change";
            fault.Enabled = true;

            var error = await Assert.ThrowsAsync<IOException>(() => unit.SaveUploadChangesAsync(outcome, CancellationToken.None));

            Assert.Same(fault.Failure, error);
            Assert.Equal(UploadCommitDisposition.NotCommitted, outcome.Disposition);
            Assert.Same(caller, context.Database.CurrentTransaction);
            Assert.True(unit.TryDiscardFailedUploadChanges(outcome));
            Assert.Same(authority, Assert.Single(context.ChangeTracker.Entries()).Entity);
            Assert.Equal("authority", authority.Name);
            Assert.Equal(EntityState.Unchanged, context.Entry(authority).State);
            Assert.Equal(EntityState.Detached, context.Entry(upload).State);
            fault.Enabled = false;
            Assert.Equal(0, await unit.SaveChangesAsync(CancellationToken.None));
            await caller.CommitAsync(CancellationToken.None);
            await using var verification = Context(database);
            Assert.False(await verification.Tenants.AnyAsync(tenant => tenant.Id == upload.Id, CancellationToken.None));
            Assert.Equal("authority", (await verification.Tenants.SingleAsync(tenant => tenant.Id == authority.Id, CancellationToken.None)).Name);
        });

    [PostgreSqlFact]
    public Task FailedSavepointRollbackRetainsRowsForTheCallersFinalDecision() =>
        WithDatabaseAsync(async database =>
        {
            var save = new SaveAcknowledgementFailure { Enabled = true };
            var rollback = new SavepointRollbackFailure();
            await using var context = Context(database, save, rollback);
            await using var caller = await context.Database.BeginTransactionAsync(CancellationToken.None);
            var unit = new EfUnitOfWork(context);
            var outcome = unit.BeginUploadSave();
            var upload = NewTenant("retained");
            context.Tenants.Add(upload);

            var error = await Assert.ThrowsAsync<IOException>(() => unit.SaveUploadChangesAsync(outcome, CancellationToken.None));

            Assert.Same(save.Failure, error);
            Assert.Equal(1, rollback.Calls);
            Assert.False(rollback.TokenWasCancelable);
            Assert.Equal(UploadCommitDisposition.Unknown, outcome.Disposition);
            Assert.False(unit.TryDiscardFailedUploadChanges(outcome));
            Assert.Same(upload, Assert.Single(context.ChangeTracker.Entries()).Entity);
            Assert.Same(caller, context.Database.CurrentTransaction);
            await caller.CommitAsync(CancellationToken.None);
            await using var verification = Context(database);
            Assert.True(await verification.Tenants.AnyAsync(tenant => tenant.Id == upload.Id, CancellationToken.None));
        });

    [PostgreSqlFact]
    public Task PendingCallerWritesAreNeverDiscardedByAnUploadScope() =>
        WithDatabaseAsync(async database =>
        {
            await using var context = Context(database);
            var authority = NewTenant("authority");
            context.Tenants.Add(authority);
            await context.SaveChangesAsync(CancellationToken.None);
            await using var caller = await context.Database.BeginTransactionAsync(CancellationToken.None);
            authority.Name = "Caller change";
            var unrelated = NewTenant("caller-pending");
            context.Tenants.Add(unrelated);
            var unit = new EfUnitOfWork(context);
            var outcome = unit.BeginUploadSave();

            Assert.False(unit.TryDiscardFailedUploadChanges(outcome));

            Assert.Same(caller, context.Database.CurrentTransaction);
            Assert.Equal(EntityState.Modified, context.Entry(authority).State);
            Assert.Equal(EntityState.Added, context.Entry(unrelated).State);
            Assert.Equal("Caller change", authority.Name);
            Assert.Equal(2, await unit.SaveChangesAsync(CancellationToken.None));
            await caller.CommitAsync(CancellationToken.None);
            await using var verification = Context(database);
            Assert.Equal("Caller change", (await verification.Tenants.SingleAsync(tenant => tenant.Id == authority.Id, CancellationToken.None)).Name);
            Assert.True(await verification.Tenants.AnyAsync(tenant => tenant.Id == unrelated.Id, CancellationToken.None));
        });

    [PostgreSqlFact]
    public Task PreSaveDenialKeepsUnrelatedTrackedReadsAndTheCallerTransaction() =>
        WithDatabaseAsync(async database =>
        {
            await using var context = Context(database);
            var authority = NewTenant("authority");
            context.Tenants.Add(authority);
            await context.SaveChangesAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            await using var caller = await context.Database.BeginTransactionAsync(CancellationToken.None);
            var unit = new EfUnitOfWork(context);
            var outcome = unit.BeginUploadSave();
            var unrelatedRead = await context.Tenants.SingleAsync(tenant => tenant.Id == authority.Id, CancellationToken.None);

            Assert.True(unit.TryDiscardFailedUploadChanges(outcome));

            Assert.Same(unrelatedRead, Assert.Single(context.ChangeTracker.Entries()).Entity);
            Assert.Same(caller, context.Database.CurrentTransaction);
            Assert.Equal(0, await unit.SaveChangesAsync(CancellationToken.None));
            await caller.CommitAsync(CancellationToken.None);
            await using var verification = Context(database);
            Assert.Equal(authority.Id, (await verification.Tenants.SingleAsync(tenant => tenant.Id == authority.Id, CancellationToken.None)).Id);
        });

    [PostgreSqlFact]
    public Task SuccessfulCallerSaveCannotAuthorizeCleanupBeforeTheCallerCommits() =>
        WithDatabaseAsync(async database =>
        {
            await using var context = Context(database);
            await using var caller = await context.Database.BeginTransactionAsync(CancellationToken.None);
            var unit = new EfUnitOfWork(context);
            var outcome = unit.BeginUploadSave();
            var upload = NewTenant("successful-caller-save");
            context.Tenants.Add(upload);

            Assert.Equal(1, await unit.SaveUploadChangesAsync(outcome, CancellationToken.None));

            Assert.Equal(UploadCommitDisposition.Unknown, outcome.Disposition);
            Assert.False(unit.TryDiscardFailedUploadChanges(outcome));
            Assert.Same(caller, context.Database.CurrentTransaction);
            await caller.CommitAsync(CancellationToken.None);
            await using var verification = Context(database);
            Assert.True(await verification.Tenants.AnyAsync(tenant => tenant.Id == upload.Id, CancellationToken.None));
        });

    [PostgreSqlFact]
    public async Task SavepointLifecycleFailuresRetainUnknownAndPreserveTheCallersFinalDecision()
    {
        foreach (var stage in new[] { "create", "release-after" })
        {
            await WithDatabaseAsync(async database =>
            {
                var fault = new SavepointLifecycleFailure(stage);
                await using var context = Context(database, fault);
                await using var caller = await context.Database.BeginTransactionAsync(CancellationToken.None);
                var unit = new EfUnitOfWork(context);
                var outcome = unit.BeginUploadSave();
                var upload = NewTenant(stage);
                context.Tenants.Add(upload);

                var error = await Assert.ThrowsAsync<IOException>(() => unit.SaveUploadChangesAsync(outcome, CancellationToken.None));

                Assert.Same(fault.Failure, error);
                Assert.Equal(UploadCommitDisposition.Unknown, outcome.Disposition);
                Assert.False(unit.TryDiscardFailedUploadChanges(outcome));
                Assert.Same(caller, context.Database.CurrentTransaction);
                Assert.Same(upload, Assert.Single(context.ChangeTracker.Entries()).Entity);
                if (stage == "release-after")
                {
                    Assert.Equal(1, await context.Tenants.CountAsync(tenant => tenant.Id == upload.Id, CancellationToken.None));
                    await caller.CommitAsync(CancellationToken.None);
                }
                else await caller.RollbackAsync(CancellationToken.None);
                await using var verification = Context(database);
                Assert.Equal(stage == "release-after", await verification.Tenants.AnyAsync(tenant => tenant.Id == upload.Id, CancellationToken.None));
            });
        }
    }

    private static Task WithDatabaseAsync(Func<string, Task> scenario) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(PostgreSqlTestEnvironment.RequireConnectionString(), scenario);

    private static AppDbContext Context(string database, params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).AddInterceptors(interceptors).Options, new PlatformScope());

    private sealed class PlatformScope : ICurrentTenant
    {
        public Guid TenantId => Guid.Empty;
        public string TenantSlug => "platform";
        public bool IsAvailable => false;
        public bool IsPlatformScope => true;
    }

    private static Tenant NewTenant(string name) => new(Guid.NewGuid())
    {
        Name = name, DisplayName = name, Slug = "synthetic-" + Guid.NewGuid().ToString("N"), Status = TenantStatus.Active
    };

    private sealed class SaveAcknowledgementFailure : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public IOException Failure { get; } = new("Synthetic save acknowledgement failure inside the caller transaction.");
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) => Enabled ? throw Failure : ValueTask.FromResult(result);
    }

    private sealed class SavepointRollbackFailure : DbTransactionInterceptor
    {
        public int Calls { get; private set; }
        public bool TokenWasCancelable { get; private set; }
        public override ValueTask<InterceptionResult> RollingBackToSavepointAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Calls++;
            TokenWasCancelable = cancellationToken.CanBeCanceled;
            throw new IOException("Synthetic secondary savepoint rollback failure.");
        }
    }

    private sealed class SavepointLifecycleFailure(string stage) : DbTransactionInterceptor
    {
        private int _releases;
        public IOException Failure { get; } = new("Synthetic savepoint lifecycle acknowledgement failure.");
        public override ValueTask<InterceptionResult> CreatingSavepointAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            stage == "create" ? throw Failure : ValueTask.FromResult(result);

        public override Task ReleasedSavepointAsync(DbTransaction transaction, TransactionEventData eventData,
            CancellationToken cancellationToken = default)
        {
            // EF releases its automatic savepoint first, then the upload releases its own.
            if (Interlocked.Increment(ref _releases) == 2 && stage == "release-after") throw Failure;
            return Task.CompletedTask;
        }
    }
}
