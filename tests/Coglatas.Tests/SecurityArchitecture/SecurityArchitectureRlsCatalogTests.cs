using System.Text.Json;
using Coglatas.Domain.Entities;
using Coglatas.Tests.PostgreSql;
using Npgsql;

namespace Coglatas.Tests.SecurityArchitecture;

/// <summary>Installs draft fixture policies; catalogue agreement is not all-table authorization evidence.</summary>
public sealed class SecurityArchitectureRlsCatalogTests
{
    [PostgreSqlFact]
    public async Task DraftPolicyCatalogueCoversTenantColumnsAndParentsAndRejectsStructuralDrift()
    {
        var root = PostgreSqlTestEnvironment.RequireConnectionString();
        var role = "sec_arch_catalog_" + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        try
        {
            await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(root, async database =>
            {
                var tables = await TenantTablesAsync(database);
                Assert.Equal(103, tables.Count);
                Assert.Equal(102, tables.Count(row => row.Type == "uuid"));
                Assert.Single(tables, row => row.Type == "text");
                var predicates = tables.ToDictionary(row => row.Table,
                    _ => "\"TenantId\"::text=current_setting('coglatas.tenant_id',true)", StringComparer.Ordinal);
                predicates.Add("file_selection_snapshot_items", """
                    EXISTS (SELECT 1 FROM public.file_selection_snapshots parent
                        WHERE parent."Id"="SelectionSnapshotId")
                    """);
                predicates.Add("coglatas_ui_canonical_change_journal", """
                    EXISTS (SELECT 1 FROM public.coglatas_ui_canonical_revision_heads parent
                        WHERE parent."ScopeKey"=coglatas_ui_canonical_change_journal."ScopeKey")
                    """);
                Assert.Equal(105, predicates.Count);
                var alpha = Guid.NewGuid();
                var beta = Guid.NewGuid();
                await SeedControlsAsync(database, alpha, beta);
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                    CREATE ROLE {Quote(role)} LOGIN PASSWORD '{password}' NOSUPERUSER NOBYPASSRLS
                        NOCREATEDB NOCREATEROLE NOINHERIT;
                    GRANT USAGE ON SCHEMA public TO {Quote(role)};
                    """);
                foreach (var (table, predicate) in predicates)
                    await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, InstallPolicy(table, predicate, role));

                var app = new NpgsqlConnectionStringBuilder(database)
                { Username = role, Password = password, MaxPoolSize = 1, Multiplexing = false }.ConnectionString;
                try
                {
                    var baseline = await PolicySnapshotAsync(database, role);
                    Assert.Equal(105, baseline.Count);
                    Assert.Equal(predicates.Keys.Order(StringComparer.Ordinal), baseline.Select(row => row.Table));
                    Assert.All(baseline, row =>
                    {
                        Assert.True(row.Enabled && row.Forced && row.NonOwner);
                        Assert.Equal("*", row.Command);
                        Assert.True(row.RoleMatches);
                        Assert.NotEqual("true", row.Using);
                        Assert.Equal(row.Using, row.WithCheck);
                    });
                    Assert.True(await CatalogueMatchesAsync(database, role, baseline));
                    await AssertLiveRowsAsync(app, alpha);
                    await AssertLiveRowsAsync(app, beta);

                    // Each mutation must invalidate the independently retained prepared catalogue.
                    await MutateAsync(database, role, baseline,
                        "ALTER TABLE public.outbox_events DISABLE ROW LEVEL SECURITY",
                        "ALTER TABLE public.outbox_events ENABLE ROW LEVEL SECURITY");
                    await MutateAsync(database, role, baseline,
                        "ALTER TABLE public.outbox_events NO FORCE ROW LEVEL SECURITY",
                        "ALTER TABLE public.outbox_events FORCE ROW LEVEL SECURITY");
                    await MutateAsync(database, role, baseline,
                        "ALTER POLICY sec_arch_draft_tenant ON public.audit_logs USING (true) WITH CHECK (true)",
                        "ALTER POLICY sec_arch_draft_tenant ON public.audit_logs USING (" + predicates["audit_logs"] +
                        ") WITH CHECK (" + predicates["audit_logs"] + ")");
                    await MutateAsync(database, role, baseline,
                        "DROP POLICY sec_arch_draft_tenant ON public.file_selection_snapshot_items",
                        CreatePolicy("file_selection_snapshot_items", predicates["file_selection_snapshot_items"], role));
                    await MutateAsync(database, role, baseline,
                        $"CREATE POLICY sec_arch_extra_allow ON public.audit_logs TO {Quote(role)} USING (true)",
                        "DROP POLICY sec_arch_extra_allow ON public.audit_logs", async () =>
                            Assert.Equal(2L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(app,
                                "SELECT count(*) FROM public.audit_logs")));
                    await MutateAsync(database, role, baseline,
                        $"GRANT UPDATE ON public.audit_logs TO {Quote(role)}",
                        $"REVOKE UPDATE ON public.audit_logs FROM {Quote(role)}");
                    await MutateAsync(database, role, baseline,
                        $"GRANT UPDATE (\"Action\") ON public.audit_logs TO {Quote(role)}",
                        $"REVOKE UPDATE (\"Action\") ON public.audit_logs FROM {Quote(role)}");
                    await MutateAsync(database, role, baseline,
                        "GRANT SELECT ON public.users TO PUBLIC", "REVOKE SELECT ON public.users FROM PUBLIC");
                    await MutateAsync(database, role, baseline,
                        $"GRANT SELECT ON public.users TO {Quote(role)}",
                        $"REVOKE SELECT ON public.users FROM {Quote(role)}");
                    await MutateAsync(database, role, baseline,
                        $"ALTER ROLE {Quote(role)} BYPASSRLS", $"ALTER ROLE {Quote(role)} NOBYPASSRLS", async () =>
                            Assert.Equal(2L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(app,
                                "SELECT count(*) FROM public.outbox_events")));
                    await MutateAsync(database, role, baseline,
                        $"ALTER TABLE public.outbox_events OWNER TO {Quote(role)}",
                        "ALTER TABLE public.outbox_events OWNER TO " + Quote(new NpgsqlConnectionStringBuilder(database).Username
                            ?? throw new InvalidOperationException("Fixture migration identity is required.")) +
                        $"; GRANT SELECT ON public.outbox_events TO {Quote(role)}");
                    await AssertLiveRowsAsync(app, alpha);
                    await AssertLiveRowsAsync(app, beta);
                    await WritePrivateSnapshotAsync(baseline);
                }
                finally
                {
                    await using var connection = new NpgsqlConnection(app);
                    NpgsqlConnection.ClearPool(connection);
                }
            });
        }
        finally
        {
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(root, "DROP ROLE IF EXISTS " + Quote(role));
        }
    }

    private sealed record TenantTable(string Table, string Type);
    private sealed record PolicyRow(string Table, bool Enabled, bool Forced, bool NonOwner,
        string Command, bool RoleMatches, string Using, string WithCheck);

    private static async Task<List<TenantTable>> TenantTablesAsync(string database)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.relname,format_type(a.atttypid,a.atttypmod)
            FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            JOIN pg_attribute a ON a.attrelid=c.oid
            WHERE n.nspname='public' AND c.relkind IN ('r','p')
                AND a.attname='TenantId' AND NOT a.attisdropped ORDER BY c.relname
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<TenantTable>();
        while (await reader.ReadAsync()) rows.Add(new(reader.GetString(0), reader.GetString(1)));
        return rows;
    }

    private static async Task<List<PolicyRow>> PolicySnapshotAsync(string database, string role)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.relname,c.relrowsecurity,c.relforcerowsecurity,c.relowner<>r.oid,
                p.polcmd::text,p.polroles=ARRAY[r.oid],pg_get_expr(p.polqual,p.polrelid),pg_get_expr(p.polwithcheck,p.polrelid)
            FROM pg_policy p JOIN pg_class c ON c.oid=p.polrelid
            JOIN pg_namespace n ON n.oid=c.relnamespace CROSS JOIN pg_roles r
            WHERE n.nspname='public' AND p.polname='sec_arch_draft_tenant' AND r.rolname=@role ORDER BY c.relname
            """;
        command.Parameters.AddWithValue("role", role);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<PolicyRow>();
        while (await reader.ReadAsync())
            rows.Add(new(reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2), reader.GetBoolean(3),
                reader.GetString(4), reader.GetBoolean(5), reader.GetString(6), reader.GetString(7)));
        return rows;
    }

    private static async Task<bool> CatalogueMatchesAsync(string database, string role, IReadOnlyList<PolicyRow> expected)
    {
        if (!(await PolicySnapshotAsync(database, role)).SequenceEqual(expected)) return false;
        return await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database, """
            SELECT NOT r.rolsuper AND NOT r.rolbypassrls AND NOT r.rolcreatedb AND NOT r.rolcreaterole AND NOT r.rolinherit
                AND NOT EXISTS (SELECT 1 FROM pg_auth_members m WHERE m.member=r.oid)
                AND NOT has_schema_privilege(r.oid,'public','CREATE')
                AND (SELECT count(*) FROM pg_policy p JOIN pg_class c ON c.oid=p.polrelid
                    JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public')=105
                AND (SELECT count(*) FROM information_schema.table_privileges WHERE grantee=@role
                    AND table_schema='public' AND privilege_type='SELECT')=105
                AND NOT EXISTS (SELECT 1 FROM information_schema.table_privileges WHERE grantee=@role
                    AND (table_schema<>'public' OR privilege_type<>'SELECT'))
                AND NOT EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                    WHERE n.nspname='public' AND c.relkind IN ('r','p') AND (
                        has_table_privilege(r.oid,c.oid,'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                        OR has_any_column_privilege(r.oid,c.oid,'INSERT,UPDATE,REFERENCES')
                        OR (has_table_privilege(r.oid,c.oid,'SELECT') AND NOT EXISTS
                            (SELECT 1 FROM pg_policy p WHERE p.polrelid=c.oid AND p.polname='sec_arch_draft_tenant'))))
            FROM pg_roles r WHERE r.rolname=@role
            """, ("role", role));
    }

    private static async Task MutateAsync(string database, string role, IReadOnlyList<PolicyRow> baseline,
        string mutation, string restore, Func<Task>? observeMutation = null)
    {
        await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, mutation);
        try
        {
            if (observeMutation is not null) await observeMutation();
            Assert.False(await CatalogueMatchesAsync(database, role, baseline));
        }
        finally { await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, restore); }
        Assert.True(await CatalogueMatchesAsync(database, role, baseline));
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    private static string CreatePolicy(string table, string predicate, string role) =>
        $"CREATE POLICY sec_arch_draft_tenant ON public.{Quote(table)} TO {Quote(role)} USING ({predicate}) WITH CHECK ({predicate});";
    private static string InstallPolicy(string table, string predicate, string role) => $"""
        ALTER TABLE public.{Quote(table)} ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.{Quote(table)} FORCE ROW LEVEL SECURITY;
        GRANT SELECT ON public.{Quote(table)} TO {Quote(role)};
        {CreatePolicy(table, predicate, role)}
        """;

    private static async Task SeedControlsAsync(string database, Guid alpha, Guid beta)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        foreach (var tenant in new[] { alpha, beta })
        {
            db.Tenants.Add(new Tenant(tenant) { Name = "Synthetic", DisplayName = "Synthetic", Slug = "synthetic-" + tenant.ToString("N") });
            db.AuditLogs.Add(new AuditLog { TenantId = tenant, Action = "Synthetic", EntityType = "Synthetic" });
            db.OutboxEvents.Add(new OutboxEvent(Guid.NewGuid())
            {
                TenantId = tenant, EventType = "Synthetic", PayloadSchemaVersion = 1,
                AggregateType = "Synthetic", AggregateId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow,
                PayloadJson = "{}", RoutingJson = "{}"
            });
        }
        await db.SaveChangesAsync();
    }

    private static async Task AssertLiveRowsAsync(string app, Guid tenant)
    {
        await using var connection = new NpgsqlConnection(app);
        await connection.OpenAsync();
        await AssertMissingContextAsync(connection);
        await using var transaction = await connection.BeginTransactionAsync();
        await using var context = connection.CreateCommand();
        context.CommandText = "SELECT set_config('coglatas.tenant_id',@tenant,true)";
        context.Parameters.AddWithValue("tenant", tenant.ToString());
        await context.ExecuteScalarAsync();
        foreach (var table in new[] { "outbox_events", "audit_logs" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT \"TenantId\" FROM public." + Quote(table);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(tenant, reader.GetGuid(0));
            Assert.False(await reader.ReadAsync());
        }
        await transaction.RollbackAsync();
        await AssertMissingContextAsync(connection);
    }

    private static async Task AssertMissingContextAsync(NpgsqlConnection connection)
    {
        foreach (var table in new[] { "outbox_events", "audit_logs" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM public." + Quote(table);
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }
    }

    private static async Task WritePrivateSnapshotAsync(IReadOnlyList<PolicyRow> rows)
    {
        var directory = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_PRIVATE_INVENTORY_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        await using var assembly = File.OpenRead(typeof(SecurityArchitectureRlsCatalogTests).Assembly.Location);
        var assemblyDigest = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(assembly)).ToLowerInvariant();
        Directory.CreateDirectory(directory);
        await using var output = new FileStream(Path.Combine(directory, "draft-rls-policy-catalogue.json"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(output, new
        {
            SchemaVersion = 1, Approval = "DRAFT", OwnerApproval = (string?)null,
            ExecutedAtUtc = DateTimeOffset.UtcNow,
            TestAssemblyDigest = assemblyDigest,
            ExecutionScope = "ISOLATED_SYNTHETIC_CATALOGUE", StructuralPolicyCount = rows.Count,
            ProductRlsAppliedCount = 0, AllTableRowVerification = "UNVERIFIED",
            AllTableOperationAndRoleApproval = "UNVERIFIED", ApplicationRoleEquivalence = "UNVERIFIED",
            PreAvaloniaVerdict = "PRE-AVALONIA SEC-ARCH: BLOCKED", Policies = rows,
            LiveReadControlTables = new[] { "outbox_events", "audit_logs" },
            BlindSpots = new[] { "Remaining tables lack live rows in this catalogue fixture.",
                "Fixture SELECT grants do not approve product application or worker access.",
                "The SQL role can change custom tenant settings; arbitrary SQL compromise is outside this scoped guarantee.",
                "Identity/bootstrap classification, applicable writes, host UI context and product activation remain owner-gated." }
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
