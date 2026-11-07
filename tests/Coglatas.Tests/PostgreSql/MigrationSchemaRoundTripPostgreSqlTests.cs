using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit.Abstractions;

namespace Coglatas.Tests.PostgreSql;

/// <summary>Selected catalog contracts on an empty, disposable migration database.</summary>
[Collection("PostgreSqlTaskV1")]
public sealed class MigrationSchemaRoundTripPostgreSqlTests(ITestOutputHelper output)
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task FreshDatabaseCanRevertAllMigrationsAndReapplyTheSameCatalogContracts()
    {
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                // This destructive target is restricted to the helper's newly
                // created database, never its administrative connection.
                var identity = new NpgsqlConnectionStringBuilder(database);
                Assert.StartsWith("coglatas_taskv1_migration_", identity.Database);
                await using var context = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
                Assert.True(context.Database.IsNpgsql());
                var migrations = context.Database.GetMigrations().ToArray();
                Assert.NotEmpty(migrations);
                Assert.Empty(await context.Database.GetAppliedMigrationsAsync());
                Assert.Empty((await CatalogAsync(database)).Tables);

                await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
                Assert.Equal(migrations, await context.Database.GetAppliedMigrationsAsync());
                Assert.Empty(await context.Database.GetPendingMigrationsAsync());
                var original = await CatalogAsync(database);
                Assert.True(original.Tables.Count > 1);
                Assert.NotEmpty(original.Columns);
                Assert.NotEmpty(original.Constraints);
                Assert.NotEmpty(original.Indexes);

                await PostgreSqlMigrationTestDatabase.MigrateAsync(database, Migration.InitialDatabase);
                Assert.Empty(await context.Database.GetAppliedMigrationsAsync());
                Assert.Equal(migrations, await context.Database.GetPendingMigrationsAsync());
                Assert.Equal("__EFMigrationsHistory", Assert.Single((await CatalogAsync(database)).Tables));

                await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
                Assert.Equal(migrations, await context.Database.GetAppliedMigrationsAsync());
                Assert.Empty(await context.Database.GetPendingMigrationsAsync());
                Assert.False(context.Database.HasPendingModelChanges());
                var reapplied = await CatalogAsync(database);
                Assert.Equal(original.Tables, reapplied.Tables);
                Assert.Equal(original.Columns, reapplied.Columns);
                Assert.Equal(original.Constraints, reapplied.Constraints);
                Assert.Equal(original.Indexes, reapplied.Indexes);
                output.WriteLine("Actual migrations: {0}; latest tables: {1}; columns: {2}; constraints: {3}; indexes: {4}. Both Up passes and the intervening Down pass executed on the same disposable database.",
                    migrations.Length, original.Tables.Count, original.Columns.Count,
                    original.Constraints.Count, original.Indexes.Count);
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
                SELECT jsonb_build_array(table_name, column_name, ordinal_position,
                    data_type, udt_schema, udt_name, is_nullable, column_default,
                    character_maximum_length, numeric_precision, numeric_scale,
                    is_identity, identity_generation, is_generated, generation_expression)::text
                FROM information_schema.columns
                WHERE table_schema = 'public' ORDER BY table_name, ordinal_position;
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
