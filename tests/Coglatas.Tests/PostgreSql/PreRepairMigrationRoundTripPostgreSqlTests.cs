using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit.Abstractions;

namespace Coglatas.Tests.PostgreSql;

/// <summary>Empty-database rollback before the intentionally retaining repair migrations.</summary>
[Collection("PostgreSqlTaskV1")]
public sealed class PreRepairMigrationRoundTripPostgreSqlTests(ITestOutputHelper output)
{
    private const string FirstRetainingRepair = "20260728010000_CreateMissingTenantSettingsTable";

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task HistoricalPrefixBeforeRetainingRepairsCanRewindAndReapplyOnAnEmptyDatabase()
    {
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                var identity = new NpgsqlConnectionStringBuilder(database);
                Assert.StartsWith("coglatas_taskv1_migration_", identity.Database);
                await using var context = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
                Assert.True(context.Database.IsNpgsql());
                var migrations = context.Database.GetMigrations().ToArray();
                var repairIndex = Array.IndexOf(migrations, FirstRetainingRepair);
                Assert.InRange(repairIndex, 1, migrations.Length - 1);
                var prefix = migrations.Take(repairIndex).ToArray();
                var later = migrations.Skip(repairIndex).ToArray();
                Assert.Empty(await context.Database.GetAppliedMigrationsAsync());
                Assert.Empty((await CatalogAsync(database)).Tables);

                await PostgreSqlMigrationTestDatabase.MigrateAsync(database, prefix[^1]);
                Assert.Equal(prefix, await context.Database.GetAppliedMigrationsAsync());
                Assert.Equal(later, await context.Database.GetPendingMigrationsAsync());
                var original = await CatalogAsync(database);
                Assert.Contains("file_objects", original.Tables);
                Assert.DoesNotContain("tenant_settings", original.Tables);
                Assert.DoesNotContain("export_jobs", original.Tables);
                Assert.NotEmpty(original.Columns);
                Assert.NotEmpty(original.Constraints);
                Assert.NotEmpty(original.Indexes);

                await PostgreSqlMigrationTestDatabase.MigrateAsync(database, Migration.InitialDatabase);
                Assert.Empty(await context.Database.GetAppliedMigrationsAsync());
                Assert.Equal(migrations, await context.Database.GetPendingMigrationsAsync());
                var rewound = await CatalogAsync(database);
                Assert.Equal(new[] { "__EFMigrationsHistory" }, rewound.Tables);
                Assert.DoesNotContain("file_objects", rewound.Tables);

                await PostgreSqlMigrationTestDatabase.MigrateAsync(database, prefix[^1]);
                Assert.Equal(prefix, await context.Database.GetAppliedMigrationsAsync());
                Assert.Equal(later, await context.Database.GetPendingMigrationsAsync());
                var reapplied = await CatalogAsync(database);
                Assert.Equal(original.Tables, reapplied.Tables);
                Assert.Equal(original.Columns, reapplied.Columns);
                Assert.Equal(original.Constraints, reapplied.Constraints);
                Assert.Equal(original.Indexes, reapplied.Indexes);
                output.WriteLine("Historical empty-database prefix: {0} migrations through {1}; later {2} migrations remain pending. Tables: {3}; columns: {4}; constraints: {5}; indexes: {6}. Prefix Up/Down-to-zero/Up executed without applying either retaining repair.",
                    prefix.Length, prefix[^1], later.Length, original.Tables.Count,
                    original.Columns.Count, original.Constraints.Count, original.Indexes.Count);
            });
    }

    private static async Task<Catalog> CatalogAsync(string database)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        return new(
            await RowsAsync(connection, """
                SELECT tablename FROM pg_catalog.pg_tables
                WHERE schemaname = 'public' ORDER BY tablename;
                """),
            await RowsAsync(connection, """
                SELECT jsonb_build_array(table_name, column_name,
                    data_type, udt_schema, udt_name, is_nullable, column_default,
                    character_maximum_length, numeric_precision, numeric_scale,
                    is_identity, identity_generation, is_generated, generation_expression)::text
                FROM information_schema.columns
                WHERE table_schema = 'public' ORDER BY table_name, column_name;
                """),
            await RowsAsync(connection, """
                SELECT jsonb_build_array(c.relname, k.conname, k.contype,
                    pg_catalog.pg_get_constraintdef(k.oid, true),
                    k.condeferrable, k.condeferred, k.convalidated)::text
                FROM pg_catalog.pg_constraint k
                JOIN pg_catalog.pg_class c ON c.oid = k.conrelid
                JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public' ORDER BY c.relname, k.conname;
                """),
            await RowsAsync(connection, """
                SELECT jsonb_build_array(tablename, indexname, indexdef)::text
                FROM pg_catalog.pg_indexes
                WHERE schemaname = 'public' ORDER BY tablename, indexname;
                """));
    }

    private static async Task<IReadOnlyList<string>> RowsAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        return rows;
    }

    private sealed record Catalog(IReadOnlyList<string> Tables, IReadOnlyList<string> Columns,
        IReadOnlyList<string> Constraints, IReadOnlyList<string> Indexes);
}
