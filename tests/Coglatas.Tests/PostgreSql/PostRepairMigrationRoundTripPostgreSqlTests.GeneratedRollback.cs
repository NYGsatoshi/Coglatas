using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Coglatas.Tests.PostgreSql;

public sealed partial class PostRepairMigrationRoundTripPostgreSqlTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task FullGeneratedRollbackStopsAtRetainedDependenciesAndCallerRollbackPreservesDatabase() =>
        PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                var identity = new NpgsqlConnectionStringBuilder(database);
                Assert.StartsWith("coglatas_taskv1_migration_", identity.Database);
                await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
                await using var context = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
                Assert.True(context.Database.IsNpgsql());
                var migrations = context.Database.GetMigrations().ToArray();
                Assert.Contains(RetainedRepairBoundary, migrations);
                Assert.Equal(migrations, await context.Database.GetAppliedMigrationsAsync());
                Assert.Empty(await context.Database.GetPendingMigrationsAsync());

                var tenant = Assert.Single(await context.Tenants.ToListAsync());
                tenant.DisplayName = "Synthetic retained rollback transaction tenant";
                await context.SaveChangesAsync();
                var before = await CatalogAsync(database);
                Assert.Contains("tenant_settings", before.Tables);
                Assert.Contains("file_objects", before.Tables);
                await using var connection = new NpgsqlConnection(database);
                await connection.OpenAsync();
                var beforeTenants = await RowsAsync(connection,
                    "SELECT to_jsonb(t)::text FROM public.tenants AS t ORDER BY to_jsonb(t)::text;");
                Assert.Single(beforeTenants);
                var beforeHistory = await RowsAsync(connection,
                    "SELECT to_jsonb(h)::text FROM public.\"__EFMigrationsHistory\" AS h ORDER BY to_jsonb(h)::text;");
                Assert.Equal(migrations.Length, beforeHistory.Count);

                // Generate the entire range, preserving the historical data-retaining Down
                // implementations. This is not a claim that every SQL command can execute.
                var script = context.GetService<IMigrator>().GenerateScript(migrations[^1],
                    Migration.InitialDatabase, MigrationsSqlGenerationOptions.NoTransactions);
                Assert.Contains("DROP TABLE", script);
                Assert.DoesNotContain("START TRANSACTION", script, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("BEGIN TRANSACTION", script, StringComparison.OrdinalIgnoreCase);
                var previousPosition = -1;
                foreach (var migration in migrations.Reverse())
                {
                    var position = script.IndexOf("\"MigrationId\" = '" + migration + "'", StringComparison.Ordinal);
                    Assert.True(position > previousPosition, "Rollback history deletion is missing or out of order: " + migration);
                    previousPosition = position;
                }
                var scriptSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script))).ToLowerInvariant();

                // The test owns this transaction. The generated script has no outer
                // transaction commands, so the test explicitly rolls back its failure.
                await using (var transaction = await connection.BeginTransactionAsync())
                {
                    await using var rollback = new NpgsqlCommand(script, connection, transaction) { CommandTimeout = 90 };
                    var dependency = await Assert.ThrowsAsync<PostgresException>(() => rollback.ExecuteNonQueryAsync());
                    Assert.Equal("2BP01", dependency.SqlState);
                    await using var aborted = new NpgsqlCommand("SELECT 1", connection, transaction);
                    var failedTransaction = await Assert.ThrowsAsync<PostgresException>(() => aborted.ExecuteScalarAsync());
                    Assert.Equal("25P02", failedTransaction.SqlState);
                    await transaction.RollbackAsync();
                }

                var after = await CatalogAsync(database);
                Assert.Equal(before.Tables, after.Tables);
                Assert.Equal(before.Columns, after.Columns);
                Assert.Equal(before.Constraints, after.Constraints);
                Assert.Equal(before.Indexes, after.Indexes);
                Assert.Equal(before.PhysicalColumnOrdinals, after.PhysicalColumnOrdinals);
                Assert.Equal(beforeTenants, await RowsAsync(connection,
                    "SELECT to_jsonb(t)::text FROM public.tenants AS t ORDER BY to_jsonb(t)::text;"));
                Assert.Equal(beforeHistory, await RowsAsync(connection,
                    "SELECT to_jsonb(h)::text FROM public.\"__EFMigrationsHistory\" AS h ORDER BY to_jsonb(h)::text;"));
                Assert.Equal(migrations, await context.Database.GetAppliedMigrationsAsync());
                Assert.Empty(await context.Database.GetPendingMigrationsAsync());
                Assert.False(context.Database.HasPendingModelChanges());
                output.WriteLine("Generated full rollback from {0} migrations: SHA256 {1}. Actual SQL dependency refusal 2BP01 and failed-transaction 25P02 were observed; explicit caller rollback preserved selected catalog, physical ordinals, Tenant and full migration-history rows. No full downgrade or product migration atomicity claim.",
                    migrations.Length, scriptSha256);
            });
}
