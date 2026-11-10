using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit.Abstractions;

namespace Coglatas.Tests.PostgreSql;

/// <summary>Selected catalog contracts on an empty, disposable migration database.</summary>
[Collection("PostgreSqlTaskV1")]
public sealed partial class PostRepairMigrationRoundTripPostgreSqlTests(ITestOutputHelper output)
{
    private const string RetainedRepairBoundary = "20260728020000_CreateMissingTenantPlatformTables";

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task RetainedRepairBoundaryAndLatestSchemaSurviveARealMigrationRoundTrip()
    {
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                // Every migration call uses the helper's new disposable database.
                // Historical table-preservation repairs remain in the applied prefix.
                var identity = new NpgsqlConnectionStringBuilder(database);
                Assert.StartsWith("coglatas_taskv1_migration_", identity.Database);
                await using var context = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
                Assert.True(context.Database.IsNpgsql());
                var migrations = context.Database.GetMigrations().ToArray();
                Assert.NotEmpty(migrations);
                Assert.Empty(await context.Database.GetAppliedMigrationsAsync());
                Assert.Empty((await CatalogAsync(database)).Tables);

                var boundaryIndex = Array.IndexOf(migrations, RetainedRepairBoundary);
                Assert.InRange(boundaryIndex, 0, migrations.Length - 2);
                var retained = migrations.Take(boundaryIndex + 1).ToArray();
                var later = migrations.Skip(boundaryIndex + 1).ToArray();
                await PostgreSqlMigrationTestDatabase.MigrateAsync(database, RetainedRepairBoundary);
                Assert.Equal(retained, await context.Database.GetAppliedMigrationsAsync());
                Assert.Equal(later, await context.Database.GetPendingMigrationsAsync());
                var boundaryCatalog = await CatalogAsync(database);
                Assert.Contains("tenant_settings", boundaryCatalog.Tables);
                Assert.Contains("file_objects", boundaryCatalog.Tables);

                await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
                Assert.Equal(migrations, await context.Database.GetAppliedMigrationsAsync());
                Assert.Empty(await context.Database.GetPendingMigrationsAsync());
                var original = await CatalogAsync(database);
                Assert.True(original.Tables.Count > 1);
                Assert.NotEmpty(original.Columns);
                Assert.NotEmpty(original.Constraints);
                Assert.NotEmpty(original.Indexes);
                Assert.Equal(original.Columns.Count, original.PhysicalColumnOrdinals.Count);

                await PostgreSqlMigrationTestDatabase.MigrateAsync(database, RetainedRepairBoundary);
                Assert.Equal(retained, await context.Database.GetAppliedMigrationsAsync());
                Assert.Equal(later, await context.Database.GetPendingMigrationsAsync());
                var reverted = await CatalogAsync(database);
                Assert.Equal(boundaryCatalog.Tables, reverted.Tables);
                Assert.Equal(boundaryCatalog.Columns, reverted.Columns);
                Assert.Equal(boundaryCatalog.Constraints, reverted.Constraints);
                Assert.Equal(boundaryCatalog.Indexes, reverted.Indexes);
                Assert.Equal(reverted.Columns.Count, reverted.PhysicalColumnOrdinals.Count);

                await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
                Assert.Equal(migrations, await context.Database.GetAppliedMigrationsAsync());
                Assert.Empty(await context.Database.GetPendingMigrationsAsync());
                Assert.False(context.Database.HasPendingModelChanges());
                var reapplied = await CatalogAsync(database);
                Assert.Equal(original.Tables, reapplied.Tables);
                Assert.Equal(original.Columns, reapplied.Columns);
                Assert.Equal(original.Constraints, reapplied.Constraints);
                Assert.Equal(original.Indexes, reapplied.Indexes);
                Assert.Equal(reapplied.Columns.Count, reapplied.PhysicalColumnOrdinals.Count);
                output.WriteLine("Actual migrations: {0}; latest tables: {1}; columns: {2}; constraints: {3}; indexes: {4}. Both later-migration Up passes and their intervening Down pass executed on the same disposable database, retaining the historical repair boundary.",
                    migrations.Length, original.Tables.Count, original.Columns.Count,
                    original.Constraints.Count, original.Indexes.Count);
                output.WriteLine("Physical ordinal identities (retained as separate observations, not logical column contracts): boundary={0}; latest={1}.",
                    boundaryCatalog.PhysicalColumnOrdinals.SequenceEqual(reverted.PhysicalColumnOrdinals),
                    original.PhysicalColumnOrdinals.SequenceEqual(reapplied.PhysicalColumnOrdinals));
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
                """),
            await RowsAsync(connection, """
                SELECT jsonb_build_array(table_name, column_name, ordinal_position)::text
                FROM information_schema.columns
                WHERE table_schema = 'public' ORDER BY table_name, column_name;
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
        IReadOnlyList<string> Constraints, IReadOnlyList<string> Indexes,
        IReadOnlyList<string> PhysicalColumnOrdinals);
}
