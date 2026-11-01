using Npgsql;
using Xunit.Abstractions;

namespace Coglatas.Tests.PostgreSql;

[Collection("PostgreSqlTaskV1")]
public sealed class LogicalRecoveryExpressionPostgreSqlTests(ITestOutputHelper output)
{
    private const string PreviousMigration = "20261009000906_AddSecurityEvaluationRecords";
    private const string StableMigration = "20261101062657_StabilizeLogicalRecoveryConstraintExpressions";
    private const string ActiveAttemptIndex = "IX_task_deadline_digest_attempts_one_active";
    private static readonly string[] CheckNames =
    [
        "CK_projects_activation_provenance", "CK_projects_activation_state", "CK_projects_visibility",
        "CK_security_evaluation_rule_results_execution", "CK_security_evaluation_rule_results_reason",
        "CK_security_evaluation_runs_context", "CK_security_evaluation_runs_lifecycle",
        "CK_security_evaluation_runs_mode", "CK_security_evaluation_runs_reason",
        "CK_task_deadline_digest_attempts_completion", "CK_task_deadline_digest_jobs_completion",
        "CK_task_execution_materialized_sources_media_type", "CK_task_execution_runs_status",
        "CK_task_workflow_template_stages_category"
    ];

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task ForwardMigrationStabilizesEveryObservedCheckAndUniquePredicate()
    {
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await PostgreSqlMigrationTestDatabase.MigrateAsync(database, PreviousMigration);
                await using var connection = new NpgsqlConnection(database);
                await connection.OpenAsync();

                var original = await ReadChecksAsync(connection);
                var originalMismatches = await ReparseMismatchesAsync(connection, original);
                Assert.Equal(CheckNames.Append(ActiveAttemptIndex).Order(), originalMismatches.Order());
                output.WriteLine("Original migration: {0} CHECK/predicate definition mismatches.", originalMismatches.Count);

                await PostgreSqlMigrationTestDatabase.MigrateAsync(database, StableMigration);
                var stable = await ReadChecksAsync(connection);
                Assert.Empty(await ReparseMismatchesAsync(connection, stable));
                Assert.Equal(CheckNames.Order(), stable.Select(check => check.Name).Order());
                output.WriteLine("Forward migration: all14 CHECKs and1 unique predicate reparse byte-identically.");
            });
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task MigrationPreservesTypedValueSemanticsAndDownRestoresOriginalDefinitions()
    {
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await PostgreSqlMigrationTestDatabase.MigrateAsync(database, PreviousMigration);
                await using var connection = new NpgsqlConnection(database);
                await connection.OpenAsync();
                var original = await ReadChecksAsync(connection);
                var expected = await EvaluateCasesAsync(connection, original);
                expected.Add(ActiveAttemptIndex, await EvaluatePredicateCasesAsync(connection));
                var originalPredicate = await ReadPredicateAsync(connection);

                await PostgreSqlMigrationTestDatabase.MigrateAsync(database, StableMigration);
                var stable = await ReadChecksAsync(connection);
                var actual = await EvaluateCasesAsync(connection, stable);
                actual.Add(ActiveAttemptIndex, await EvaluatePredicateCasesAsync(connection));
                foreach (var name in CheckNames.Append(ActiveAttemptIndex))
                {
                    Assert.Contains("true", expected[name]);
                    Assert.Contains("false", expected[name]);
                    Assert.Equal(expected[name], actual[name]);
                    output.WriteLine("{0}: {1} identical typed cases (including NULL/unknown/boundary inputs).", name, actual[name].Count);
                }

                await PostgreSqlMigrationTestDatabase.MigrateAsync(database, PreviousMigration);
                var rolledBack = await ReadChecksAsync(connection);
                Assert.Equal(original.Select(check => check.Definition), rolledBack.Select(check => check.Definition));
                Assert.Equal(originalPredicate, await ReadPredicateAsync(connection));
                var rollbackCases = await EvaluateCasesAsync(connection, rolledBack);
                rollbackCases.Add(ActiveAttemptIndex, await EvaluatePredicateCasesAsync(connection));
                foreach (var name in CheckNames.Append(ActiveAttemptIndex)) Assert.Equal(expected[name], rollbackCases[name]);

                await PostgreSqlMigrationTestDatabase.MigrateAsync(database, StableMigration);
                Assert.Empty(await ReparseMismatchesAsync(connection, await ReadChecksAsync(connection)));
            });
    }

    private static async Task<List<CheckExpression>> ReadChecksAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            SELECT t.relname::text, c.conname::text,
                   pg_get_constraintdef(c.oid, false), pg_get_expr(c.conbin, c.conrelid, false),
                   array_agg(a.attname::text ORDER BY a.attnum),
                   array_agg(format_type(a.atttypid, a.atttypmod) ORDER BY a.attnum)
            FROM pg_constraint c
            JOIN pg_class t ON t.oid = c.conrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ANY(c.conkey)
            WHERE n.nspname = 'public' AND c.contype = 'c' AND c.conname = ANY(@names)
            GROUP BY t.oid, c.oid ORDER BY c.conname;
            """, connection);
        command.Parameters.AddWithValue("names", CheckNames);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<CheckExpression>();
        while (await reader.ReadAsync())
            result.Add(new CheckExpression(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetFieldValue<string[]>(4), reader.GetFieldValue<string[]>(5)));
        Assert.Equal(CheckNames.Length, result.Count);
        return result;
    }

    private static async Task<List<string>> ReparseMismatchesAsync(
        NpgsqlConnection connection, List<CheckExpression> checks)
    {
        var mismatches = new List<string>();
        foreach (var check in checks)
        {
            var temporary = "recovery_check_" + Guid.NewGuid().ToString("N");
            await ExecuteAsync(connection, $"CREATE TEMP TABLE {Identifier(temporary)} (LIKE public.{Identifier(check.Table)});");
            try
            {
                await ExecuteAsync(connection,
                    $"ALTER TABLE pg_temp.{Identifier(temporary)} ADD CONSTRAINT {Identifier(check.Name)} {check.Definition};");
                await using var read = new NpgsqlCommand("""
                    SELECT pg_get_constraintdef(oid, false) FROM pg_constraint
                    WHERE conrelid = to_regclass(@table) AND conname = @name;
                    """, connection);
                read.Parameters.AddWithValue("table", "pg_temp." + temporary);
                read.Parameters.AddWithValue("name", check.Name);
                if (!string.Equals(check.Definition, (string?)await read.ExecuteScalarAsync(), StringComparison.Ordinal))
                    mismatches.Add(check.Name);
            }
            finally { await ExecuteAsync(connection, $"DROP TABLE pg_temp.{Identifier(temporary)};"); }
        }

        var predicate = await ReadPredicateAsync(connection);
        var indexTable = "recovery_index_" + Guid.NewGuid().ToString("N");
        await ExecuteAsync(connection,
            $"CREATE TEMP TABLE {Identifier(indexTable)} (LIKE public.task_deadline_digest_attempts);");
        try
        {
            await ExecuteAsync(connection,
                $"CREATE UNIQUE INDEX recovery_active_attempt ON pg_temp.{Identifier(indexTable)} (\"JobId\") WHERE {predicate};");
            await using var read = new NpgsqlCommand("""
                SELECT pg_get_expr(indpred, indrelid, false) FROM pg_index
                WHERE indexrelid = to_regclass('pg_temp.recovery_active_attempt');
                """, connection);
            if (!string.Equals(predicate, (string?)await read.ExecuteScalarAsync(), StringComparison.Ordinal))
                mismatches.Add(ActiveAttemptIndex);
        }
        finally { await ExecuteAsync(connection, $"DROP TABLE pg_temp.{Identifier(indexTable)};"); }
        return mismatches;
    }

    private static async Task<Dictionary<string, List<string>>> EvaluateCasesAsync(
        NpgsqlConnection connection, List<CheckExpression> checks)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var check in checks)
        {
            var sources = check.Columns.Select((column, index) =>
                "(VALUES " + string.Join(",", Cases(column).Select((value, ordinal) =>
                    $"({ordinal}, {value}::{check.Types[index]})")) +
                $") AS sample{index}(ordinal{index}, {Identifier(column)})");
            var order = string.Join(",", Enumerable.Range(0, check.Columns.Length).Select(index => $"ordinal{index}"));
            await using var query = new NpgsqlCommand(
                $"SELECT ({check.Expression})::text FROM {string.Join(" CROSS JOIN ", sources)} ORDER BY {order};", connection);
            await using var reader = await query.ExecuteReaderAsync();
            var outcomes = new List<string>();
            while (await reader.ReadAsync()) outcomes.Add(reader.IsDBNull(0) ? "NULL" : reader.GetString(0));
            result.Add(check.Name, outcomes);
        }
        return result;
    }

    private static string[] Cases(string column) => column switch
    {
        "Visibility" => TextCases("WorkspaceVisible", "MembersOnly", "Restricted", "WorkspaceVisible ", "workspacevisible", "LegacyUnknown"),
        "ActivationState" => TextCases("LegacyUnknown", "NeverActivated", "Activated", "activated"),
        "EnforcementMode" => TextCases("Disabled", "Shadow", "Enforce", "shadow"),
        "ContextKind" => TextCases("coglatas.committed", "coglatas.candidate", "coglatas.scenario", "coglatas.unknown"),
        "Status" => TextCases("Pending", "Claimed", "Succeeded", "Failed", "Expired", "Deferred", "Completed",
            "Cancelled", "TimedOut", "NotExecuted", "Accepted", "Queued", "Running", "completed"),
        "Outcome" => TextCases("Allow", "Deny", "Unknown", "Quarantine", "allow"),
        "ReasonCode" => TextCases("NotExecuted", "RuleNotExecuted", "EvaluationFailed", "RuleExecutionFailed",
            "EvaluationCancelled", "EvaluationTimedOut", "BindingsVerified", "RevisionBindingVerified",
            "PolicyBindingVerified", "CompilerProvenanceVerified", "PolicyViolation", "MissingEvidence",
            "RevisionEvidenceMissing", "PolicyEvidenceMissing", "CompilerEvidenceMissing", "BindingMismatch",
            "RevisionBindingMismatch", "PolicyBindingMismatch", "CompilerProvenanceMismatch"),
        "MediaType" => TextCases("text/plain", "text/markdown", "text/html", "text/plain "),
        "InternalCategory" => TextCases("Backlog", "Todo", "InProgress", "Review", "Done", "Cancelled", "done"),
        "ActivationVersion" => ["NULL", "-1", "0", "1", "2147483647"],
        "ActivatedAtUtc" or "CompletedAt" or "CreatedAtUtc" or "TerminalAtUtc" =>
            ["NULL", "'2026-10-31T23:59:59Z'", "'2026-11-01T00:00:00Z'", "'2026-11-01T00:00:01Z'"],
        _ => throw new InvalidOperationException("Unqualified recovery constraint column.")
    };

    private static string[] TextCases(params string[] values) =>
        values.Select(value => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'")
            .Concat(["'INVALID'", "''", "NULL"]).ToArray();

    private static string Identifier(string name)
    {
        using var builder = new NpgsqlCommandBuilder();
        return builder.QuoteIdentifier(name);
    }

    private static async Task<List<string>> EvaluatePredicateCasesAsync(NpgsqlConnection connection)
    {
        var predicate = await ReadPredicateAsync(connection);
        await using var typeQuery = new NpgsqlCommand("""
            SELECT format_type(atttypid, atttypmod) FROM pg_attribute
            WHERE attrelid = 'public.task_deadline_digest_attempts'::regclass AND attname = 'Status';
            """, connection);
        var type = Assert.IsType<string>(await typeQuery.ExecuteScalarAsync());
        var values = string.Join(",", Cases("Status").Select((value, ordinal) => $"({ordinal},{value}::{type})"));
        await using var query = new NpgsqlCommand(
            $"SELECT ({predicate})::text FROM (VALUES {values}) AS sample(ordinal,\"Status\") ORDER BY ordinal;", connection);
        await using var reader = await query.ExecuteReaderAsync();
        var result = new List<string>();
        while (await reader.ReadAsync()) result.Add(reader.IsDBNull(0) ? "NULL" : reader.GetString(0));
        return result;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> ReadPredicateAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            SELECT pg_get_expr(indpred, indrelid, false) FROM pg_index
            WHERE indexrelid = to_regclass('public."IX_task_deadline_digest_attempts_one_active"')
              AND indisunique AND indisvalid AND indisready;
            """, connection);
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }

    private sealed record CheckExpression(
        string Table, string Name, string Definition, string Expression, string[] Columns, string[] Types);
}
