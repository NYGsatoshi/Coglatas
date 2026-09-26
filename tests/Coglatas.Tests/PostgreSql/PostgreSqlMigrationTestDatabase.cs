using Coglatas.Application.Common.Tenancy;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Coglatas.Tests.PostgreSql;

/// <summary>Creates one isolated PostgreSQL database per migration scenario.</summary>
internal static class PostgreSqlMigrationTestDatabase
{
    public static async Task WithTemporaryDatabaseAsync(string connectionString, Func<string, Task> scenario)
    {
        var databaseName = $"coglatas_taskv1_migration_{Guid.NewGuid():N}";
        var source = new NpgsqlConnectionStringBuilder(connectionString);
        var temporary = new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName }.ConnectionString;

        await using var admin = new NpgsqlConnection(source.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin))
            await create.ExecuteNonQueryAsync();

        try
        {
            await scenario(temporary);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    public static async Task MigrateAsync(string connectionString, string? targetMigration = null)
    {
        await using var context = CreatePlatformContext(connectionString);
        await context.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    public static AppDbContext CreatePlatformContext(string connectionString)
    {
        var tenant = new CurrentTenantService();
        tenant.SetPlatformScope();
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options, tenant);
    }

    public static async Task ExecuteAsync(string connectionString, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<T> ScalarAsync<T>(string connectionString, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    public static async Task<List<T>> QueryAsync<T>(string connectionString, string sql, Func<NpgsqlDataReader, T> map, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync()) rows.Add(map(reader));
        return rows;
    }
}
