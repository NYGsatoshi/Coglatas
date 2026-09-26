namespace Coglatas.Tests.PostgreSql;

internal static class PostgreSqlTestEnvironment
{
    private const string ConnectionStringEnvironmentVariable = "POSTGRES_TEST_CONNECTION_STRING";

    public static string RequireConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(connectionString)) return connectionString;

        const string message = "POSTGRES_TEST_CONNECTION_STRING is required to execute PostgreSQL integration tests.";
        if (PostgreSqlFactAttribute.IsCi())
            throw new InvalidOperationException(message);

        throw new InvalidOperationException($"{message} This test should have been marked skipped during discovery.");
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class PostgreSqlFactAttribute : FactAttribute
{
    private const string ConnectionStringEnvironmentVariable = "POSTGRES_TEST_CONNECTION_STRING";

    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)) && !IsCi())
            Skip = "POSTGRES_TEST_CONNECTION_STRING is required to execute PostgreSQL integration tests. Set it locally to run this category.";
    }

    internal static bool IsCi() =>
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);
}
