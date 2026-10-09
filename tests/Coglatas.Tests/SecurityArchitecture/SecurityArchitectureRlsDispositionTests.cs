using Coglatas.Tests.PostgreSql;
using Npgsql;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed class SecurityArchitectureRlsDispositionTests
{
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
