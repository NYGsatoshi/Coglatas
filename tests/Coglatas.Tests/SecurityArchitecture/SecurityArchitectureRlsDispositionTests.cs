using Coglatas.Tests.PostgreSql;
using Npgsql;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed class SecurityArchitectureRlsDispositionTests
{
    [PostgreSqlFact]
    public async Task ParentRetentionCascadeIsDistinctFromForbiddenDirectRuleDeletion()
    {
        var root = PostgreSqlTestEnvironment.RequireConnectionString();
        var role = "sec_arch_retention_" + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        try
        {
            await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(root, async database =>
            {
                var alpha = Guid.NewGuid();
                var beta = Guid.NewGuid();
                await SecurityArchitectureRlsRowFixtures.SeedAsync(database, alpha);
                await SecurityArchitectureRlsRowFixtures.SeedAsync(database, beta);
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                    CREATE ROLE "{role}" LOGIN PASSWORD '{password}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
                    GRANT USAGE ON SCHEMA public TO "{role}";
                    GRANT SELECT,DELETE ON security_evaluation_runs,security_evaluation_rule_results TO "{role}";
                    """);
                foreach (var table in new[] { "security_evaluation_runs", "security_evaluation_rule_results" })
                    await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                        ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                        ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                        CREATE POLICY sec_arch_draft_retention ON {table} TO "{role}"
                            USING ("TenantId"::text=current_setting('coglatas.tenant_id',true));
                        """);
                var connectionString = new NpgsqlConnectionStringBuilder(database) { Username = role, Password = password }.ConnectionString;
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                try
                {
                    await using (var transaction = await connection.BeginTransactionAsync())
                    {
                        await SetAsync(connection, beta);
                        var error = await Assert.ThrowsAsync<PostgresException>(() => DeleteAsync(connection, "security_evaluation_rule_results", beta));
                        Assert.Equal("TRIGGER_REJECTION", SecurityArchitectureRlsOperationTests.Classify(error));
                        Assert.Contains("security_evaluation_rule_guard", error.Where);
                        await transaction.RollbackAsync();
                    }
                    // Prove this actual child exists and the permitted database cascade branch runs before testing isolation.
                    await using (var transaction = await connection.BeginTransactionAsync())
                    {
                        await SetAsync(connection, beta);
                        Assert.Equal(1L, await CountAsync(connection, "security_evaluation_rule_results", beta));
                        Assert.Equal(1, await DeleteAsync(connection, "security_evaluation_runs", beta));
                        Assert.Equal(0L, await CountAsync(connection, "security_evaluation_rule_results", beta));
                        await transaction.RollbackAsync();
                    }
                    await using (var transaction = await connection.BeginTransactionAsync())
                    {
                        await SetAsync(connection, alpha);
                        Assert.Equal(0, await DeleteAsync(connection, "security_evaluation_runs", beta));
                        Assert.Equal(1L, await CountAsync(connection, "security_evaluation_rule_results", alpha));
                        await transaction.RollbackAsync();
                    }
                    Assert.Equal(1L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database,
                        "SELECT count(*) FROM security_evaluation_rule_results WHERE \"TenantId\"=@tenant", ("tenant", beta)));
                    var schema = await SecurityArchitectureRlsSchemaIdentity.CaptureAsync(database, "security_evaluation_rule_results");
                    Assert.Contains(await SecurityArchitectureRlsUnavailableOperations.BindAsync(schema.Table, schema),
                        item => item.Operation == "DELETE" && item.AlternateLifecycle == "ParentDeletionCascadeOnlyRetentionAuthorityUnverified");
                }
                finally { NpgsqlConnection.ClearPool(connection); }

                static async Task SetAsync(NpgsqlConnection connection, Guid tenant)
                {
                    await using var command = new NpgsqlCommand("SELECT set_config('coglatas.tenant_id',@tenant,true)", connection);
                    command.Parameters.AddWithValue("tenant", tenant.ToString());
                    await command.ExecuteScalarAsync();
                }
                static async Task<int> DeleteAsync(NpgsqlConnection connection, string table, Guid tenant)
                {
                    await using var command = new NpgsqlCommand("DELETE FROM " + table + " WHERE \"TenantId\"=@tenant", connection);
                    command.Parameters.AddWithValue("tenant", tenant);
                    return await command.ExecuteNonQueryAsync();
                }
                static async Task<long> CountAsync(NpgsqlConnection connection, string table, Guid tenant)
                {
                    await using var command = new NpgsqlCommand("SELECT count(*) FROM " + table + " WHERE \"TenantId\"=@tenant", connection);
                    command.Parameters.AddWithValue("tenant", tenant);
                    return (long)(await command.ExecuteScalarAsync())!;
                }
            });
        }
        finally { await PostgreSqlMigrationTestDatabase.ExecuteAsync(root, $"DROP ROLE IF EXISTS \"{role}\""); }
    }

    [PostgreSqlFact]
    public async Task TriggerForgingRlsErrorTextAndSqlStateCannotQualifyAsPolicyDenial()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            const string insert = """
                INSERT INTO outbox_events ("Id","TenantId","EventType","PayloadSchemaVersion","AggregateType","AggregateId",
                    "OccurredAt","PayloadJson","RoutingJson","Status","AttemptCount","CreatedAt")
                VALUES (gen_random_uuid(),gen_random_uuid(),'Synthetic',1,'Synthetic',gen_random_uuid(),now(),'{}','{}','Pending',0,now())
                """;
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, insert);
            Assert.Equal(1L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, "SELECT count(*) FROM outbox_events"));
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
                CREATE FUNCTION sec_arch_forged_rls_denial() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'new row violates row-level security policy for table outbox_events' USING ERRCODE='42501'; END; $$;
                CREATE TRIGGER sec_arch_forged_rls_denial BEFORE INSERT ON outbox_events
                    FOR EACH ROW EXECUTE FUNCTION sec_arch_forged_rls_denial();
                """);
            var failure = await Assert.ThrowsAsync<PostgresException>(() => PostgreSqlMigrationTestDatabase.ExecuteAsync(database, insert));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
            Assert.Equal("TRIGGER_REJECTION", SecurityArchitectureRlsOperationTests.Classify(failure));
            Assert.False(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database,
                "SELECT relrowsecurity FROM pg_class WHERE oid='public.outbox_events'::regclass"));
            Assert.Equal(1L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, "SELECT count(*) FROM outbox_events"));
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, "DROP TRIGGER sec_arch_forged_rls_denial ON outbox_events");
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, insert);
            Assert.Equal(2L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, "SELECT count(*) FROM outbox_events"));
        });
    }

    [PostgreSqlFact]
    public async Task SourceGuardAndConstraintIdentitiesDetectDisabledAndSemanticallyWeakenedDefinitions()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            const string table = "coglatas_ui_canonical_change_journal";
            var baseline = await SecurityArchitectureRlsSchemaIdentity.CaptureAsync(database, table);
            Assert.Equal(2, baseline.Guards.Count);
            Assert.All(baseline.Guards, guard => Assert.Equal("O", guard.Enabled));
            Assert.Contains(baseline.Constraints, constraint => constraint.Kind == "f" && constraint.Validated);
            Assert.All(baseline.Guards, guard => Assert.Equal(64, guard.FunctionDefinitionDigest.Length));
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
                "ALTER TABLE coglatas_ui_canonical_change_journal DISABLE TRIGGER trg_coglatas_ui_journal_append_only");
            try
            {
                var disabled = await SecurityArchitectureRlsSchemaIdentity.CaptureAsync(database, table);
                Assert.NotEqual(baseline.SchemaDigest, disabled.SchemaDigest);
                Assert.Contains(disabled.Guards, guard => guard.TriggerName == "trg_coglatas_ui_journal_append_only" && guard.Enabled == "D");
            }
            finally
            {
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
                    "ALTER TABLE coglatas_ui_canonical_change_journal ENABLE TRIGGER trg_coglatas_ui_journal_append_only");
            }
            Assert.Equal(baseline.SchemaDigest, (await SecurityArchitectureRlsSchemaIdentity.CaptureAsync(database, table)).SchemaDigest);
            var original = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database,
                "SELECT pg_get_functiondef('public.coglatas_ui_reject_journal_rewrite()'::regprocedure)");
            try
            {
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
                    CREATE OR REPLACE FUNCTION public.coglatas_ui_reject_journal_rewrite() RETURNS trigger LANGUAGE plpgsql AS $$
                    BEGIN IF TG_OP='DELETE' THEN RETURN OLD; END IF; RETURN NEW; END; $$;
                    """);
                var weakened = await SecurityArchitectureRlsSchemaIdentity.CaptureAsync(database, table);
                Assert.NotEqual(baseline.SchemaDigest, weakened.SchemaDigest);
                var expected = baseline.Guards.Single(guard => guard.FunctionName == "coglatas_ui_reject_journal_rewrite");
                var observed = weakened.Guards.Single(guard => guard.FunctionName == expected.FunctionName);
                Assert.Equal(expected.TriggerDefinitionDigest, observed.TriggerDefinitionDigest);
                Assert.NotEqual(expected.FunctionDefinitionDigest, observed.FunctionDefinitionDigest);
            }
            finally { await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, original); }
            Assert.Equal(baseline.SchemaDigest, (await SecurityArchitectureRlsSchemaIdentity.CaptureAsync(database, table)).SchemaDigest);
        });
    }
}
