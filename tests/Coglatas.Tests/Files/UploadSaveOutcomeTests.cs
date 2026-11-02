using System.Data.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Projects;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Coglatas.Tests.Files;

[Collection("PostgreSqlTaskV1")]
public sealed class UploadSaveOutcomeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpaquePersistenceCannotClaimDurabilityOrRollback(bool fails)
    {
        var persistence = new OpaquePersistence(fails);
        IUnitOfWork unit = persistence;
        var outcome = new UploadSaveOutcome();

        if (fails)
        {
            var error = await Assert.ThrowsAsync<IOException>(() => unit.SaveUploadChangesAsync(outcome));
            Assert.Same(persistence.Failure, error);
        }
        else { Assert.Equal(1, await unit.SaveUploadChangesAsync(outcome)); }

        Assert.Equal(1, persistence.Saves);
        Assert.Equal(UploadCommitDisposition.Unknown, outcome.Disposition);
        Assert.False(unit.TryDiscardFailedUploadChanges());
    }

    [Fact]
    public async Task NonRelationalSaveDoesNotClaimCommitAcknowledgement()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var context = new AppDbContext(options, new PlatformScope());
        context.Tenants.Add(OwnedTenant());
        var outcome = new UploadSaveOutcome();

        Assert.Equal(1, await new EfUnitOfWork(context).SaveUploadChangesAsync(outcome));

        Assert.Equal(UploadCommitDisposition.Unknown, outcome.Disposition);
        Assert.Single(await context.Tenants.ToListAsync());
    }

    [PostgreSqlFact]
    public Task AcknowledgedOwnedCommitIsDurable() => VerifyOwnedAsync("success", UploadCommitDisposition.Committed, true);

    [PostgreSqlFact]
    public Task AcquisitionFailureDiscardsBeforeAuthorizingCleanup() => VerifyOwnedAsync("begin", UploadCommitDisposition.NotCommitted, false);

    [PostgreSqlFact]
    public Task AcknowledgedRollbackDiscardsBeforeAuthorizingCleanup() => VerifyOwnedAsync("save", UploadCommitDisposition.NotCommitted, false);

    [PostgreSqlFact]
    public Task CancellationRollsBackWithAnIndependentToken() => VerifyOwnedAsync("cancel", UploadCommitDisposition.NotCommitted, false);

    [PostgreSqlFact]
    public Task RollbackFailurePreservesPrimaryErrorAndUncertainty() => VerifyOwnedAsync("rollback", UploadCommitDisposition.Unknown, false);

    [PostgreSqlFact]
    public Task CommitStartRemainsUncertainDespiteAcknowledgedRollback() => VerifyOwnedAsync("commit-before", UploadCommitDisposition.Unknown, false);

    [PostgreSqlFact]
    public Task CommitAcknowledgementLossPreservesDurableRowsAndUncertainty() => VerifyOwnedAsync("commit-after", UploadCommitDisposition.Unknown, true);

    private static Task VerifyOwnedAsync(string stage, UploadCommitDisposition expected, bool durable) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                using var cancellation = new CancellationTokenSource();
                var fault = new TransactionFault(stage, cancellation);
                var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database)
                    .AddInterceptors(fault, new SaveFault(fault)).Options;
                await using var context = new AppDbContext(options, new PlatformScope());
                var tenant = OwnedTenant();
                context.Tenants.Add(tenant);
                var policyDocuments = new TaskExecutionScopeRepository(context);
                policyDocuments.StageSourcePolicyDocumentDelete(TaskExecutionSourcePolicyOwnerType.Project, Guid.NewGuid());
                Assert.True(policyDocuments.HasPendingSourcePolicyDocuments);
                var unit = new EfUnitOfWork(context, policyDocuments);
                var outcome = new UploadSaveOutcome();

                if (stage == "success") { Assert.Equal(1, await unit.SaveUploadChangesAsync(outcome, cancellation.Token)); }
                else
                {
                    var error = await Record.ExceptionAsync(() => unit.SaveUploadChangesAsync(outcome, cancellation.Token));
                    Assert.Same(fault.Primary, error);
                }

                Assert.Equal(expected, outcome.Disposition);
                Assert.Null(context.Database.CurrentTransaction);
                Assert.Equal(1, fault.Begins);
                Assert.Equal(stage is "success" or "commit-before" or "commit-after" ? 1 : 0, fault.Commits);
                if (stage is "save" or "cancel" or "rollback" or "commit-before")
                {
                    Assert.Equal(1, fault.Rollbacks);
                    Assert.False(fault.RollbackTokenWasCancelable);
                }
                await using var verification = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                    .UseNpgsql(database).Options, new PlatformScope());
                Assert.Equal(durable, await verification.Tenants.AnyAsync(item => item.Id == tenant.Id));

                if (expected == UploadCommitDisposition.NotCommitted)
                {
                    Assert.Empty(context.ChangeTracker.Entries());
                    Assert.False(policyDocuments.HasPendingSourcePolicyDocuments);
                    Assert.True(unit.TryDiscardFailedUploadChanges());
                    fault.Enabled = false;
                    Assert.Equal(0, await unit.SaveChangesAsync(CancellationToken.None));
                    Assert.False(await verification.Tenants.AnyAsync(item => item.Id == tenant.Id));
                }
            });

    [PostgreSqlFact]
    public Task CallerOwnedTransactionRetainsOwnershipAndUncertainFinalOutcome() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                var fault = new TransactionFault("success", null);
                await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                    .UseNpgsql(database).AddInterceptors(fault).Options, new PlatformScope());
                await using var transaction = await context.Database.BeginTransactionAsync();
                var tenant = OwnedTenant();
                context.Tenants.Add(tenant);
                var unit = new EfUnitOfWork(context);
                var outcome = new UploadSaveOutcome();

                Assert.Equal(1, await unit.SaveUploadChangesAsync(outcome));

                Assert.Equal(UploadCommitDisposition.Unknown, outcome.Disposition);
                Assert.Same(transaction, context.Database.CurrentTransaction);
                Assert.False(unit.TryDiscardFailedUploadChanges());
                Assert.Single(context.ChangeTracker.Entries());
                Assert.Equal(1, fault.Begins);
                Assert.Equal(0, fault.Commits);
                await transaction.RollbackAsync();
                await using var verification = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                    .UseNpgsql(database).Options, new PlatformScope());
                Assert.False(await verification.Tenants.AnyAsync(item => item.Id == tenant.Id));
            });

    private static Tenant OwnedTenant() => new(Guid.NewGuid())
    {
        Name = "Owned upload transaction proof", DisplayName = "Owned upload transaction proof",
        Slug = "owned-upload-proof", Status = TenantStatus.Active
    };

    [PostgreSqlFact]
    public Task AmbientTransactionRetainsItsRollbackAuthority() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                var tenant = OwnedTenant();
                using (var ambient = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
                {
                    await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                        .UseNpgsql(database).Options, new PlatformScope());
                    context.Tenants.Add(tenant);
                    var unit = new EfUnitOfWork(context);
                    var outcome = new UploadSaveOutcome();

                    Assert.Equal(1, await unit.SaveUploadChangesAsync(outcome));

                    Assert.Equal(UploadCommitDisposition.Unknown, outcome.Disposition);
                    Assert.Null(context.Database.CurrentTransaction);
                    Assert.False(unit.TryDiscardFailedUploadChanges());
                    Assert.Single(context.ChangeTracker.Entries());
                    // Omitting Complete belongs to this caller and rolls back its transaction.
                }
                await using var verification = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                    .UseNpgsql(database).Options, new PlatformScope());
                Assert.False(await verification.Tenants.AnyAsync(item => item.Id == tenant.Id));
            });

    [PostgreSqlFact]
    public Task ExplicitlyEnlistedTransactionRetainsItsRollbackAuthority() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                var tenant = OwnedTenant();
                using (var transaction = new System.Transactions.CommittableTransaction())
                {
                    await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                        .UseNpgsql(database).Options, new PlatformScope());
                    await context.Database.OpenConnectionAsync();
                    context.Database.EnlistTransaction(transaction);
                    context.Tenants.Add(tenant);
                    var unit = new EfUnitOfWork(context);
                    var outcome = new UploadSaveOutcome();

                    Assert.Equal(1, await unit.SaveUploadChangesAsync(outcome));

                    Assert.Equal(UploadCommitDisposition.Unknown, outcome.Disposition);
                    Assert.Same(transaction, context.Database.GetEnlistedTransaction());
                    Assert.False(unit.TryDiscardFailedUploadChanges());
                    Assert.Single(context.ChangeTracker.Entries());
                    transaction.Rollback();
                }
                await using var verification = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                    .UseNpgsql(database).Options, new PlatformScope());
                Assert.False(await verification.Tenants.AnyAsync(item => item.Id == tenant.Id));
            });

    [PostgreSqlFact]
    public Task DisposalFailureAfterAcknowledgedCommitCannotAuthorizeDeletion() => VerifyDisposalAsync(false);

    [PostgreSqlFact]
    public Task SecondaryDisposalFailureCannotReplaceTheSaveError() => VerifyDisposalAsync(true);

    private static Task VerifyDisposalAsync(bool saveFails) =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                var disposal = new DisposalFault();
                DisposalTransactionFactory.Current.Value = disposal;
                try
                {
                    var fault = new TransactionFault(saveFails ? "save" : "success", null);
                    var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database)
                        .ReplaceService<IRelationalTransactionFactory, DisposalTransactionFactory>()
                        .AddInterceptors(new SaveFault(fault)).Options;
                    await using var context = new AppDbContext(options, new PlatformScope());
                    var tenant = OwnedTenant();
                    context.Tenants.Add(tenant);
                    var outcome = new UploadSaveOutcome();

                    var error = await Record.ExceptionAsync(() => new EfUnitOfWork(context).SaveUploadChangesAsync(outcome));

                    Assert.Same(saveFails ? fault.Primary : disposal.Failure, error);
                    Assert.Equal(saveFails ? UploadCommitDisposition.NotCommitted : UploadCommitDisposition.Committed,
                        outcome.Disposition);
                    Assert.Equal(1, disposal.Calls);
                    Assert.Null(context.Database.CurrentTransaction);
                    await using var verification = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                        .UseNpgsql(database).Options, new PlatformScope());
                    Assert.Equal(!saveFails, await verification.Tenants.AnyAsync(item => item.Id == tenant.Id));
                }
                finally { DisposalTransactionFactory.Current.Value = null; }
            });

    private sealed class DisposalFault
    {
        public IOException Failure { get; } = new("Synthetic transaction disposal acknowledgement failure.");
        public int Calls { get; set; }
    }

    // The actual provider transaction is disposed first. Only its acknowledgement is faulted.
    private sealed class DisposalTransactionFactory(RelationalTransactionFactoryDependencies dependencies)
        : RelationalTransactionFactory(dependencies)
    {
        public static AsyncLocal<DisposalFault?> Current { get; } = new();

        public override RelationalTransaction Create(IRelationalConnection connection, DbTransaction transaction,
            Guid transactionId, IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger, bool transactionOwned) =>
            new DisposalTransaction(connection, transaction, transactionId, logger, transactionOwned,
                Dependencies.SqlGenerationHelper, Current.Value ?? throw new InvalidOperationException("Missing owned disposal fault."));
    }

    private sealed class DisposalTransaction(IRelationalConnection connection, DbTransaction transaction,
        Guid transactionId, IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger, bool transactionOwned,
        ISqlGenerationHelper sqlGenerationHelper, DisposalFault fault)
        : RelationalTransaction(connection, transaction, transactionId, logger, transactionOwned, sqlGenerationHelper)
    {
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            fault.Calls++;
            if (fault.Calls == 1) throw fault.Failure;
        }
    }

    private sealed class OpaquePersistence(bool fails) : IUnitOfWork
    {
        public IOException Failure { get; } = new("Synthetic opaque save acknowledgement failure.");
        public int Saves { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            return fails ? Task.FromException<int>(Failure) : Task.FromResult(1);
        }
    }

    private sealed class PlatformScope : ICurrentTenant
    {
        public Guid TenantId => Guid.Empty;
        public string TenantSlug => "platform";
        public bool IsAvailable => false;
        public bool IsPlatformScope => true;
    }

    private sealed class SaveFault(TransactionFault fault) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (fault.Enabled && fault.Stage is "save" or "rollback" or "cancel")
            {
                fault.Cancellation?.Cancel();
                throw fault.Primary;
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class TransactionFault(string stage, CancellationTokenSource? cancellation) : DbTransactionInterceptor
    {
        public string Stage => stage;
        public CancellationTokenSource? Cancellation => stage == "cancel" ? cancellation : null;
        public Exception Primary { get; } = stage == "cancel"
            ? new OperationCanceledException("Synthetic cancellation after owned transaction acquisition.")
            : new IOException("Synthetic owned upload transaction failure.");
        public bool Enabled { get; set; } = true;
        public int Begins { get; private set; }
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }
        public bool RollbackTokenWasCancelable { get; private set; }

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            Begins++;
            if (Enabled && stage == "begin") throw Primary;
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Commits++;
            if (Enabled && stage == "commit-before") throw Primary;
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Enabled && stage == "commit-after") throw Primary;
            return Task.CompletedTask;
        }

        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Rollbacks++;
            RollbackTokenWasCancelable = cancellationToken.CanBeCanceled;
            if (Enabled && stage == "rollback") throw new IOException("Synthetic secondary rollback failure.");
            return ValueTask.FromResult(result);
        }
    }
}
