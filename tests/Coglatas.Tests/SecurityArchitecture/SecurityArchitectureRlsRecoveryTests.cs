using System.Security.Cryptography;
using System.Text.Json;
using Coglatas.Tests.PostgreSql;
using Npgsql;
using static Coglatas.Tests.SecurityArchitecture.SecurityArchitectureRlsRecoverySnapshot;

namespace Coglatas.Tests.SecurityArchitecture;

/// <summary>Disposable deny-all preparation/recovery mechanics, never an activation migration or semantic policy proposal.</summary>
public sealed class SecurityArchitectureRlsRecoveryTests
{
    private const string Policy = "sec_arch_recovery_probe";

    [PostgreSqlFact]
    public async Task FailedAllTablePreparationRollsBackEveryRowGuardPolicyGrantAndRole()
    {
        await ScenarioAsync(async (database, role, tables, baseline) =>
        {
            var failure = await TransactionAsync(database, async connection =>
            {
                await CreateRoleAsync(connection, role);
                for (var index = 0; index < tables.Count; index++)
                {
                    await PrepareTableAsync(connection, role, tables[index]);
                    if (index == tables.Count / 2)
                        await ExecuteAsync(connection, CreatePolicy(tables[index], role)); // Deliberate duplicate DDL after real work.
                }
            }, commit: false);
            Assert.Equal(PostgresErrorCodes.DuplicateObject, failure);
            var restored = await CaptureAsync(database, role);
            Assert.True(SameAll(baseline, restored), "Failed transactional preparation must restore every canonical identity.");
            await WritePrivateAsync(database, "failed-preparation", role, tables, baseline, restored, failure, [], []);
        });
    }

    [PostgreSqlFact]
    public async Task SuccessfulPreparationAndFailedThenSuccessfulRecoveryPreserveAllMigratedDataAndNativeGuards()
    {
        await ScenarioAsync(async (database, role, tables, baseline) =>
        {
            Assert.Null(await TransactionAsync(database, connection => PrepareAsync(connection, role, tables), commit: true));
            var prepared = await CaptureAsync(database, role);
            Assert.True(SameDataAndNativeSchema(baseline, prepared), "Preparation cannot change rows, native triggers, constraints, indexes or functions.");
            Assert.True(await PreparedAsync(database, role, tables));
            foreach (var table in baseline.Tables.Where(table => !tables.Contains(table.Name, StringComparer.Ordinal)))
                Assert.Equal(table.SecurityDigest, prepared.Tables.Single(observed => observed.Name == table.Name).SecurityDigest);
            Assert.Equal(0L, await AsRoleRowsAsync(database, role, tables[0]));
            var failure = await TransactionAsync(database, async connection =>
            {
                foreach (var table in tables.Take(tables.Count / 2)) await RecoverTableAsync(connection, role, table);
                await ExecuteAsync(connection, "SELECT 1/0");
            }, commit: false);
            Assert.Equal(PostgresErrorCodes.DivisionByZero, failure);
            var rolledBackRecovery = await CaptureAsync(database, role);
            Assert.True(SameAll(prepared, rolledBackRecovery), "Failed recovery must leave the complete prepared state intact.");
            Assert.Null(await TransactionAsync(database, connection => RecoverAsync(connection, role, tables), commit: true));
            var recovered = await CaptureAsync(database, role);
            Assert.True(SameAll(baseline, recovered), "Successful reversal must exactly restore canonical data/schema/authority identities.");
            await WritePrivateAsync(database, "successful-preparation-and-recovery", role, tables, baseline, recovered, failure, [],
                [new("CommittedPreparation", prepared), new("FailedRecoveryRolledBack", rolledBackRecovery)]);
        });
    }

    [PostgreSqlFact]
    public async Task LiveRecoveryGuardsDetectMissingPoliciesBroadGrantsBypassAndPolicyWeakening()
    {
        await ScenarioAsync(async (database, role, tables, baseline) =>
        {
            Assert.Null(await TransactionAsync(database, connection => PrepareAsync(connection, role, tables), commit: true));
            var prepared = await CaptureAsync(database, role);
            Assert.True(await PreparedAsync(database, role, tables));
            var first = "public." + Quote(tables[0]);
            var controls = new[]
            {
                ("MissingPolicy", "DROP POLICY " + Policy + " ON " + first),
                ("BroadTruncateGrant", "GRANT TRUNCATE ON " + first + " TO " + Quote(role)),
                ("RoleBypass", "ALTER ROLE " + Quote(role) + " BYPASSRLS"),
                ("PolicyWeakening", "ALTER POLICY " + Policy + " ON " + first + " USING (true) WITH CHECK (true)"),
                ("DisabledRls", "ALTER TABLE " + first + " DISABLE ROW LEVEL SECURITY")
            };
            var detected = new List<string>();
            foreach (var (kind, sql) in controls)
            {
                await using var connection = new NpgsqlConnection(database);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync();
                await ExecuteAsync(connection, sql);
                Assert.False(await PreparedAsync(connection, role, tables), "A mutated preparation cannot satisfy the recovery gate.");
                if (kind is "RoleBypass" or "PolicyWeakening" or "DisabledRls")
                {
                    await ExecuteAsync(connection, "SET LOCAL ROLE " + Quote(role));
                    await using var command = new NpgsqlCommand("SELECT count(*) FROM " + first, connection);
                    Assert.True((long)(await command.ExecuteScalarAsync())! > 0, "Bypass mutation needs an actual seeded exposure control.");
                }
                await transaction.RollbackAsync();
                Assert.True(SameAll(prepared, await CaptureAsync(database, role)), "Mutation rollback must restore its complete retained state.");
                detected.Add(kind);
            }
            Assert.Null(await TransactionAsync(database, connection => RecoverAsync(connection, role, tables), commit: true));
            var recovered = await CaptureAsync(database, role);
            Assert.True(SameAll(baseline, recovered));
            await WritePrivateAsync(database, "live-recovery-mutations", role, tables, baseline, recovered, null, detected,
                [new("CommittedPreparation", prepared)]);
        });
    }

    [PostgreSqlFact]
    public async Task CommittedIncompleteRecoveryAndChangedRowsCannotMatchTheOriginalIdentity()
    {
        await ScenarioAsync(async (database, role, tables, baseline) =>
        {
            Assert.Null(await TransactionAsync(database, connection => PrepareAsync(connection, role, tables), commit: true));
            Assert.Null(await TransactionAsync(database, async connection =>
            {
                foreach (var table in tables.Skip(1)) await RecoverTableAsync(connection, role, table);
                // Deliberately retain the first table's policy, flags, grants and role.
            }, commit: true));
            var incomplete = await CaptureAsync(database, role);
            Assert.True(SameDataAndNativeSchema(baseline, incomplete));
            Assert.False(SameAll(baseline, incomplete));
            Assert.Null(await TransactionAsync(database, connection => RecoverAsync(connection, role, tables), commit: true));
            var recovered = await CaptureAsync(database, role);
            Assert.True(SameAll(baseline, recovered));
            await using (var connection = new NpgsqlConnection(database))
            {
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync();
                await ExecuteAsync(connection, "UPDATE public.tenants SET \"Name\"='Changed synthetic recovery fixture'");
                // Hash inside this transaction; a row mutation must not be hidden by unchanged table counts.
                await using var command = new NpgsqlCommand("SELECT encode(sha256(convert_to(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text COLLATE \"C\")::text,'UTF8')),'hex') FROM public.tenants t", connection);
                Assert.NotEqual(baseline.Tables.Single(table => table.Name == "tenants").DataDigest, (string)(await command.ExecuteScalarAsync())!);
                await transaction.RollbackAsync();
            }
            Assert.True(SameAll(baseline, await CaptureAsync(database, role)));
            await WritePrivateAsync(database, "incomplete-recovery-and-row-mutation", role, tables, baseline, recovered, null,
                ["CommittedIncompleteRecovery", "RowContentChangedWithEqualCounts"], [new("CommittedIncompleteRecovery", incomplete)]);
        });
    }

    private static async Task ScenarioAsync(Func<string, string, IReadOnlyList<string>, Snapshot, Task> scenario)
    {
        var root = PostgreSqlTestEnvironment.RequireConnectionString();
        var role = "sec_arch_recovery_" + Guid.NewGuid().ToString("N");
        try
        {
            await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(root, async database =>
            {
                await SecurityArchitectureRlsRowFixtures.SeedAsync(database, Guid.NewGuid());
                await SecurityArchitectureRlsRowFixtures.SeedAsync(database, Guid.NewGuid());
                var tables = await PostgreSqlMigrationTestDatabase.QueryAsync(database, """
                    SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                    WHERE n.nspname='public' AND c.relkind IN ('r','p') AND
                    (EXISTS(SELECT 1 FROM pg_attribute a WHERE a.attrelid=c.oid AND a.attname='TenantId' AND NOT a.attisdropped)
                     OR c.relname IN ('file_selection_snapshot_items','coglatas_ui_canonical_change_journal'))
                    ORDER BY c.relname COLLATE "C"
                    """, reader => reader.GetString(0));
                Assert.Equal(105, tables.Count);
                Assert.DoesNotContain("users", tables);
                Assert.DoesNotContain("sessions", tables);
                Assert.DoesNotContain("tenants", tables);
                var baseline = await CaptureAsync(database, role);
                Assert.Equal(114, baseline.Tables.Count);
                Assert.All(baseline.Tables.Where(table => tables.Contains(table.Name, StringComparer.Ordinal)), table => Assert.True(table.RowCount > 0));
                Assert.Equal(0L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, """
                    SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                    WHERE n.nspname='public' AND (c.relrowsecurity OR c.relforcerowsecurity)
                    """));
                await scenario(database, role, tables, baseline);
            });
        }
        finally { await PostgreSqlMigrationTestDatabase.ExecuteAsync(root, "DROP ROLE IF EXISTS " + Quote(role)); }
    }

    private static Task CreateRoleAsync(NpgsqlConnection connection, string role) => ExecuteAsync(connection,
        "CREATE ROLE " + Quote(role) + " NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT; GRANT USAGE ON SCHEMA public TO " + Quote(role));
    private static string CreatePolicy(string table, string role) => "CREATE POLICY " + Policy + " ON public." + Quote(table) +
        " TO " + Quote(role) + " USING (false) WITH CHECK (false)";
    private static Task PrepareTableAsync(NpgsqlConnection connection, string role, string table) => ExecuteAsync(connection,
        "ALTER TABLE public." + Quote(table) + " ENABLE ROW LEVEL SECURITY; ALTER TABLE public." + Quote(table) +
        " FORCE ROW LEVEL SECURITY; GRANT SELECT ON public." + Quote(table) + " TO " + Quote(role) + "; " + CreatePolicy(table, role));
    private static async Task PrepareAsync(NpgsqlConnection connection, string role, IReadOnlyList<string> tables)
    {
        await CreateRoleAsync(connection, role);
        foreach (var table in tables) await PrepareTableAsync(connection, role, table);
    }
    private static Task RecoverTableAsync(NpgsqlConnection connection, string role, string table) => ExecuteAsync(connection,
        "DROP POLICY IF EXISTS " + Policy + " ON public." + Quote(table) + "; ALTER TABLE public." + Quote(table) +
        " DISABLE ROW LEVEL SECURITY; ALTER TABLE public." + Quote(table) + " NO FORCE ROW LEVEL SECURITY; REVOKE SELECT ON public." + Quote(table) + " FROM " + Quote(role));
    private static async Task RecoverAsync(NpgsqlConnection connection, string role, IReadOnlyList<string> tables)
    {
        foreach (var table in tables) await RecoverTableAsync(connection, role, table);
        await ExecuteAsync(connection, "REVOKE USAGE ON SCHEMA public FROM " + Quote(role) + "; DROP ROLE " + Quote(role));
    }

    private static async Task<string?> TransactionAsync(string database, Func<NpgsqlConnection, Task> action, bool commit)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await action(connection);
            if (commit) await transaction.CommitAsync(); else await transaction.RollbackAsync();
            return null;
        }
        catch (PostgresException error)
        {
            // Prove the actual aborted state before explicitly rolling the transaction back.
            var aborted = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, "SELECT 1"));
            Assert.Equal(PostgresErrorCodes.InFailedSqlTransaction, aborted.SqlState);
            await transaction.RollbackAsync();
            return error.SqlState;
        }
    }

    private static async Task<bool> PreparedAsync(string database, string role, IReadOnlyList<string> tables)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        return await PreparedAsync(connection, role, tables);
    }
    private static async Task<bool> PreparedAsync(NpgsqlConnection connection, string role, IReadOnlyList<string> tables)
    {
        await using var command = new NpgsqlCommand("""
            SELECT NOT r.rolsuper AND NOT r.rolbypassrls AND NOT r.rolcreatedb AND NOT r.rolcreaterole AND NOT r.rolinherit AND NOT r.rolcanlogin
              AND NOT EXISTS(SELECT 1 FROM pg_auth_members WHERE member=r.oid)
              AND NOT has_schema_privilege(r.oid,'public','CREATE')
              AND (SELECT count(*) FROM pg_policy p JOIN pg_class c ON c.oid=p.polrelid JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='public')=@count
              AND (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                JOIN pg_policy p ON p.polrelid=c.oid WHERE n.nspname='public' AND c.relname=ANY(@tables)
                AND c.relrowsecurity AND c.relforcerowsecurity AND c.relowner<>r.oid AND p.polname='sec_arch_recovery_probe'
                AND p.polcmd='*' AND p.polpermissive AND p.polroles=ARRAY[r.oid]
                AND pg_get_expr(p.polqual,p.polrelid)='false' AND pg_get_expr(p.polwithcheck,p.polrelid)='false')=@count
              AND NOT EXISTS(SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='public' AND c.relkind IN ('r','p') AND
                (has_table_privilege(r.oid,c.oid,'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN') OR
                 has_table_privilege(r.oid,c.oid,'SELECT')<>(c.relname=ANY(@tables))))
            FROM pg_roles r WHERE r.rolname=@role
            """, connection);
        command.Parameters.AddWithValue("role", role);
        command.Parameters.AddWithValue("tables", tables.ToArray());
        command.Parameters.AddWithValue("count", tables.Count);
        return await command.ExecuteScalarAsync() is true;
    }
    private static async Task<long> AsRoleRowsAsync(string database, string role, string table)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(connection, "SET LOCAL ROLE " + Quote(role));
        await using var command = new NpgsqlCommand("SELECT count(*) FROM public." + Quote(table), connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private sealed record Checkpoint(string Stage, Snapshot Identity);
    private static async Task WritePrivateAsync(string database, string name, string role, IReadOnlyList<string> tables,
        Snapshot baseline, Snapshot recovered, string? injectedFailure, IReadOnlyList<string> detectedMutations, IReadOnlyList<Checkpoint> checkpoints)
    {
        var directory = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_PRIVATE_INVENTORY_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        await using var assembly = File.OpenRead(typeof(SecurityArchitectureRlsRecoveryTests).Assembly.Location);
        var assemblyDigest = Convert.ToHexString(await SHA256.HashDataAsync(assembly)).ToLowerInvariant();
        var candidate = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_CANDIDATE_SHA");
        if (candidate is null || candidate.Length != 40 || candidate.Any(character => !Uri.IsHexDigit(character))) candidate = null;
        await using var output = new FileStream(Path.Combine(directory, "draft-rls-recovery-" + name + ".json"), FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(output, new
        {
            schemaVersion = 1, approval = "DRAFT", ownerApproval = (string?)null, candidateSha = candidate,
            testAssemblyDigest = assemblyDigest, executedAtUtc = DateTimeOffset.UtcNow,
            postgresVersion = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database, "SHOW server_version"),
            fixture = "ISOLATED_MIGRATED_SYNTHETIC_ROWS", testRole = role, tableCount = baseline.Tables.Count,
            proposedPreparationTableCount = tables.Count, productRlsAppliedCount = 0,
            policySemantics = "DENY_ALL_DDL_RECOVERY_PROBE_ONLY", injectedFailure, detectedMutations,
            allDataAndNativeSchemaPreserved = SameDataAndNativeSchema(baseline, recovered), completeCanonicalRecovery = SameAll(baseline, recovered),
            baseline, recovered, checkpoints, identityRootPolicies = "NOT_PREPARED_OWNER_HOLD", applicationWorkerEquivalence = "UNVERIFIED",
            preAvaloniaVerdict = "PRE-AVALONIA SEC-ARCH: BLOCKED",
            limits = new[] { "Canonical multiset hashes do not establish backup/PITR or operational recovery.",
                "Effective ACL normalization treats explicit and implicit identical owner grants alike; physical catalogue bytes/OIDs are not compared.",
                "Empty table identities are recorded, never counted as seeded operation evidence.",
                "Deny-all SQL only verifies atomic DDL preservation/reversal, not application or worker policy semantics.",
                "Identity/root policy, runtime integration, privileges and activation require separate owner review.",
                "Exact candidate/build/producer provenance needs independent reconciliation." }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
    }
}
