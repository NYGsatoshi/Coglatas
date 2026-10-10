using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coglatas.Tests.PostgreSql;
using Npgsql;
using NpgsqlTypes;

namespace Coglatas.Tests.SecurityArchitecture;

/// <summary>Isolated draft row probes; no product policy or operational role is changed.</summary>
public sealed class SecurityArchitectureRlsOperationTests
{
    [PostgreSqlFact]
    public async Task MigratedSourceRowsRequireRealPositiveControlsBeforeEveryDraftPolicyDenial()
    {
        var root = PostgreSqlTestEnvironment.RequireConnectionString();
        var role = "sec_arch_rows_" + Guid.NewGuid().ToString("N");
        var deniedRole = "sec_arch_denied_" + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        try
        {
            await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(root, async database =>
            {
                var alpha = Guid.NewGuid();
                var beta = Guid.NewGuid();
                await SecurityArchitectureRlsRowFixtures.SeedAsync(database, alpha);
                await SecurityArchitectureRlsRowFixtures.SeedAsync(database, beta);
                var tables = await TablesAsync(database);
                Assert.Equal(105, tables.Count);
                var missing = new List<string>();
                foreach (var table in tables)
                    if (await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database,
                        "SELECT count(*) FROM public." + Quote(table.Table)) == 0) missing.Add(table.Table);
                Assert.True(missing.Count == 0, "Missing source fixtures: " + string.Join(",", missing));
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                    CREATE ROLE {Quote(role)} LOGIN PASSWORD '{password}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
                    CREATE ROLE {Quote(deniedRole)} LOGIN PASSWORD '{password}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
                    GRANT USAGE ON SCHEMA public TO {Quote(role)},{Quote(deniedRole)};
                    """);
                foreach (var table in tables)
                    await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                        ALTER TABLE public.{Quote(table.Table)} ENABLE ROW LEVEL SECURITY;
                        ALTER TABLE public.{Quote(table.Table)} FORCE ROW LEVEL SECURITY;
                        GRANT SELECT,INSERT,UPDATE,DELETE ON public.{Quote(table.Table)} TO {Quote(role)};
                        CREATE POLICY sec_arch_draft_rows ON public.{Quote(table.Table)} TO {Quote(role)}
                            USING ({table.Predicate}) WITH CHECK ({table.Predicate});
                        """);
                var app = Connection(database, role, password);
                var denied = Connection(database, deniedRole, password);
                var results = new List<TableResult>();
                try
                {
                    foreach (var table in tables)
                        results.Add(await VerifyTableAsync(database, app, denied, role, table, alpha, beta));
                    await AssertPoolResetAsync(app, tables, alpha, beta);
                    var roles = await VerifyRolesAsync(database, role, deniedRole);
                    Assert.Equal(19, results.Sum(result => result.SourceUnavailableOperations.Count));
                    Assert.Equal(33, results.Sum(result => result.SourceGuardedProbes.Count));
                    Assert.All(results, result =>
                    {
                        var source = Assert.Single(tables, table => table.Table == result.Table);
                        Assert.Equal(source.TenantIdentityKind, result.TenantIdentityKind);
                        Assert.Equal(Digest(source.Predicate), result.PolicyDigest);
                        Assert.Equal("SEEDED", result.FixtureStatus);
                        Assert.Equal(source.TenantIdentityKind == "PARENT" ? "PARENT_REASSIGNMENT" : "TENANT_REASSIGNMENT", result.OwnershipProbeKind);
                        Assert.Equal(result.Table, result.SourceSchemaIdentity.Table);
                        Assert.Equal(result.SourceMutationGuards.Order(StringComparer.Ordinal),
                            result.SourceSchemaIdentity.Guards.Select(guard => guard.TriggerName).Order(StringComparer.Ordinal));
                        foreach (var disposition in result.SourceUnavailableOperations)
                        {
                            var direct = Assert.Single(result.Operations, operation =>
                                operation.Operation == disposition.Operation && operation.Situation == "sameScope");
                            Assert.Equal("UNVERIFIED", direct.Result);
                            Assert.Equal(disposition.ReasonCode, direct.ReasonCode);
                        }
                        foreach (var disposition in result.SourceGuardedProbes)
                        {
                            var observed = Assert.Single(result.Operations, operation =>
                                operation.Operation == disposition.Operation && operation.Situation == disposition.Situation);
                            Assert.Equal("UNVERIFIED", observed.Result);
                            Assert.Equal("SourceMutationGuard", observed.ReasonCode);
                            Assert.Equal("TRIGGER_REJECTION", observed.ObservedMechanism);
                            Assert.True(observed.PositiveControlAffectedRows > 0);
                            Assert.Equal(0, observed.AffectedRows);
                            Assert.Equal(disposition.Guard.FunctionName, observed.SourceRejectionIdentity!.GuardFunctionName);
                        }
                        var controls = result.VerificationControls;
                        Assert.True(controls.PermissivePolicyExposureRows > 0);
                        Assert.Equal(0, controls.RestoredCrossTenantRows);
                        Assert.Equal("GRANT_DENIAL", controls.RevokedSelectMechanism);
                        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, controls.RevokedSelectSqlState);
                        Assert.True(controls.RestoredSameScopeRows > 0);
                        Assert.True(controls.ForbiddenTruncateGrantDetected);
                    });
                    Assert.All(results, result => Assert.True(result.Operations.Single(operation =>
                        operation.Operation == "SELECT" && operation.Situation == "sameScope").AffectedRows > 0,
                        "A migrated row fixture is required for every proposed table."));
                    Assert.DoesNotContain(results.SelectMany(result => result.Operations), operation => operation.Result == "FAIL" || operation.Result == "ERROR");
                    Assert.All(results.SelectMany(result => result.Operations), operation =>
                    {
                        Assert.Equal(operation.Situation == "unauthorizedRole" ? "syntheticUnauthorized" : "syntheticApplication", operation.RoleKind);
                        Assert.Equal(roles.Single(observed => observed.RoleKind == operation.RoleKind).DatabaseRole, operation.DatabaseRole);
                        if (operation.Result == "PASS")
                        {
                            Assert.True(operation.PositiveControlAffectedRows > 0);
                            Assert.Equal(operation.ExpectedMechanism, operation.ObservedMechanism);
                            if (operation.ObservedMechanism is "RLS_WITH_CHECK" or "GRANT_DENIAL")
                                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, operation.SqlState);
                            else
                                Assert.Null(operation.SqlState);
                        }
                        if (operation.ObservedMechanism is "TRIGGER_REJECTION" or "CONSTRAINT_REJECTION")
                        {
                            var identity = Assert.IsType<RejectionIdentity>(operation.SourceRejectionIdentity);
                            if (operation.ObservedMechanism == "TRIGGER_REJECTION")
                            {
                                Assert.Null(identity.NativeConstraintName);
                                Assert.Matches("^[A-Za-z_][A-Za-z0-9_]{0,62}$", identity.GuardFunctionName!);
                                if (identity.GuardFunctionSchema is not null)
                                    Assert.Matches("^[A-Za-z_][A-Za-z0-9_]{0,62}$", identity.GuardFunctionSchema);
                            }
                            else
                            {
                                Assert.NotNull(identity.NativeConstraintName);
                                Assert.Null(identity.GuardFunctionName);
                                Assert.Null(identity.GuardFunctionSchema);
                            }
                        }
                        if (operation.ObservedMechanism == "UNSUPPORTED_OPERATION")
                            Assert.Equal("OwnershipReassignmentRequiresUpdate", operation.ReasonCode);
                    });
                    await WritePrivateAsync(database, roles, results);
                }
                finally
                {
                    ClearPool(app);
                    ClearPool(denied);
                }
            });
        }
        finally
        {
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(root, $"DROP ROLE IF EXISTS {Quote(role)},{Quote(deniedRole)}");
        }
    }

    private sealed record TenantTable(string Table, string TenantIdentityKind, string Predicate, string ScopeColumn);
    private sealed record RejectionIdentity(string? NativeConstraintName, string? GuardFunctionSchema, string? GuardFunctionName);
    private sealed record Observation(string Mechanism, string? SqlState, int AffectedRows, string? ReasonCode, string DatabaseRole,
        RejectionIdentity? SourceRejectionIdentity = null);
    private sealed record OperationResult(string Operation, string RoleKind, string DatabaseRole, string Situation, string ExpectedMechanism,
        string ObservedMechanism, string? SqlState, int PositiveControlAffectedRows, int AffectedRows, string Result, string? ReasonCode,
        RejectionIdentity? SourceRejectionIdentity = null);
    private sealed record VerificationControls(int PermissivePolicyExposureRows, int RestoredCrossTenantRows,
        string RevokedSelectMechanism, string? RevokedSelectSqlState, int RestoredSameScopeRows, bool ForbiddenTruncateGrantDetected);
    private sealed record TableResult(string Table, string TenantIdentityKind, string PolicyDigest, string FixtureStatus,
        IReadOnlyList<OperationResult> Operations, VerificationControls VerificationControls, IReadOnlyList<string> SourceMutationGuards,
        string OwnershipProbeKind, SecurityArchitectureRlsSchemaIdentity.Snapshot SourceSchemaIdentity,
        IReadOnlyList<SecurityArchitectureRlsUnavailableOperations.Disposition> SourceUnavailableOperations,
        IReadOnlyList<SecurityArchitectureRlsGuardedProbes.Probe> SourceGuardedProbes);
    private sealed record RoleObservation(string RoleKind, string DatabaseRole, bool IsSuperuser, bool BypassRls,
        bool CanCreateDb, bool CanCreateRole, bool InheritsRoles, int MembershipCount, int ProtectedTableOwnershipCount);
    private sealed record Column(string Name, string Type, bool Generated, bool Primary, bool Foreign, bool Unique);
    private sealed record RowCommand(string Sql, IReadOnlyList<(string Name, object Value, string Type)> Parameters);

    private static async Task<TableResult> VerifyTableAsync(string database, string app, string denied, string role,
        TenantTable table, Guid alpha, Guid beta)
    {
        var columns = await ColumnsAsync(database, table.Table);
        var first = columns.First(column => column.Primary).Name;
        var select = "SELECT count(*) FROM public." + Quote(table.Table);
        var alphaWhere = await ScopedWhereAsync(database, table, alpha);
        var betaWhere = await ScopedWhereAsync(database, table, beta);
        var update = table.Table switch
        {
            "security_evaluation_runs" => "\"Status\"='Failed',\"ReasonCode\"='EvaluationFailed',\"TerminalAtUtc\"=now()",
            "coglatas_ui_canonical_revision_heads" => "\"Revision\"=\"Revision\"+1",
            _ => Quote(first) + "=" + Quote(first)
        };
        var insert = await CloneInsertAsync(database, table, columns, alphaWhere);
        var insertTenant = Guid.NewGuid();
        if (table.Table == "file_versions") insertTenant = alpha;
        else
        {
            var sourceFixture = await SecurityArchitectureRlsRowFixtures.SeedAsync(database, insertTenant, table.Table);
            insert = new RowCommand(sourceFixture.Insert!.Sql, sourceFixture.Insert.Parameters);
        }
        var operations = new List<OperationResult>();
        foreach (var action in new[] { "SELECT", "INSERT", "UPDATE", "DELETE" })
        {
            var operationTenant = action is "INSERT" or "DELETE" ? insertTenant : alpha;
            if (action == "DELETE") await ExecuteFixtureAsync(database, insert);
            var deleteWhere = action == "DELETE" ? await ScopedWhereAsync(database, table, insertTenant) : alphaWhere;
            var ownSql = action switch
            {
                "SELECT" => select + " WHERE " + alphaWhere,
                "INSERT" => insert.Sql,
                "UPDATE" => "UPDATE public." + Quote(table.Table) + " SET " + update + " WHERE " + alphaWhere,
                _ => "DELETE FROM public." + Quote(table.Table) + " WHERE " + deleteWhere
            };
            var positive = await ObserveAsync(app, operationTenant.ToString(), action, ownSql, action == "INSERT" ? insert.Parameters : []);
            var sourceReason = SecurityArchitectureRlsUnavailableOperations.Reason(table.Table, action);
            if (positive.AffectedRows == 0 && sourceReason is not null)
            {
                Assert.Contains(positive.Mechanism, new[] { "TRIGGER_REJECTION", "CONSTRAINT_REJECTION" });
                positive = positive with { ReasonCode = sourceReason };
            }
            operations.Add(CreateResult(action, "sameScope", "ALLOWED", positive, positive.AffectedRows));
            foreach (var situation in new[] { "crossTenant", "missingContext", "invalidContext", "unauthorizedRole" })
            {
                var tenant = situation switch { "missingContext" => null, "invalidContext" => "invalid-context", "crossTenant" => beta.ToString(), _ => operationTenant.ToString() };
                var observed = await ObserveAsync(situation == "unauthorizedRole" ? denied : app, tenant, action, ownSql,
                    action == "INSERT" ? insert.Parameters : []);
                var expected = situation == "unauthorizedRole" ? "GRANT_DENIAL" : action == "INSERT" ? "RLS_WITH_CHECK" : "RLS_FILTER";
                if (positive.AffectedRows == 0 && sourceReason is not null) observed = observed with { ReasonCode = sourceReason };
                operations.Add(CreateResult(action, situation, expected, observed, positive.AffectedRows));
            }
            if (action == "UPDATE")
            {
                var foreignValue = table.TenantIdentityKind == "PARENT" ?
                    table.Table == "file_selection_snapshot_items" ? "(SELECT \"Id\" FROM public.file_selection_snapshots WHERE " + ScopeWhere(new("file_selection_snapshots", "UUID", "", "TenantId"), beta) + ")" : "'" + beta.ToString("N") + "'" : "'" + beta + "'";
                // Parent lookup uses a seeded literal: selecting the hidden parent would turn this into an unrelated NULL constraint error.
                if (table.Table == "file_selection_snapshot_items")
                    foreignValue = "'" + await PostgreSqlMigrationTestDatabase.ScalarAsync<Guid>(database,
                        "SELECT \"Id\" FROM public.file_selection_snapshots WHERE \"TenantId\"=@tenant", ("tenant", beta)) + "'";
                var observed = await ObserveAsync(app, alpha.ToString(), action,
                    "UPDATE public." + Quote(table.Table) + " SET " + Quote(table.ScopeColumn) + "=" + foreignValue + " WHERE " + alphaWhere, []);
                if (positive.AffectedRows == 0 && sourceReason is not null) observed = observed with { ReasonCode = sourceReason };
                operations.Add(CreateResult(action, "wrongOwnership", "RLS_WITH_CHECK", observed, positive.AffectedRows));
            }
            else operations.Add(new(action, "syntheticApplication", positive.DatabaseRole, "wrongOwnership", "UNSUPPORTED_OPERATION", "UNSUPPORTED_OPERATION", null,
                positive.AffectedRows, 0, "UNVERIFIED", "OwnershipReassignmentRequiresUpdate"));
        }
        // Every policy mutation must expose actual foreign rows and then restore its live control.
        Observation exposure;
        await PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
            "ALTER POLICY sec_arch_draft_rows ON public." + Quote(table.Table) + " USING (true) WITH CHECK (true)");
        try
        {
            exposure = await ObserveAsync(app, alpha.ToString(), "SELECT", select + " WHERE " + betaWhere, []);
            Assert.True(exposure.Mechanism == "ALLOWED" && exposure.AffectedRows > 0, "The all-table policy mutation must expose a real foreign row.");
        }
        finally
        {
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, "ALTER POLICY sec_arch_draft_rows ON public." + Quote(table.Table) +
                " USING (" + table.Predicate + ") WITH CHECK (" + table.Predicate + ")");
        }
        var restored = await ObserveAsync(app, alpha.ToString(), "SELECT", select + " WHERE " + betaWhere, []);
        Assert.Equal("RLS_FILTER", restored.Mechanism);
        Observation revoked;
        await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, "REVOKE SELECT ON public." + Quote(table.Table) + " FROM " + Quote(role));
        try
        {
            revoked = await ObserveAsync(app, alpha.ToString(), "SELECT", select + " WHERE " + alphaWhere, []);
            Assert.Equal("GRANT_DENIAL", revoked.Mechanism);
        }
        finally { await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, "GRANT SELECT ON public." + Quote(table.Table) + " TO " + Quote(role)); }
        var restoredPositive = await ObserveAsync(app, alpha.ToString(), "SELECT", select + " WHERE " + alphaWhere, []);
        Assert.True(restoredPositive.AffectedRows > 0);
        Assert.False(await TruncateGrantedAsync(database, role, table.Table));
        await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, "GRANT TRUNCATE ON public." + Quote(table.Table) + " TO " + Quote(role));
        bool broadGrantDetected;
        try { broadGrantDetected = await TruncateGrantedAsync(database, role, table.Table); Assert.True(broadGrantDetected); }
        finally { await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, "REVOKE TRUNCATE ON public." + Quote(table.Table) + " FROM " + Quote(role)); }
        Assert.False(await TruncateGrantedAsync(database, role, table.Table));
        var guards = await PostgreSqlMigrationTestDatabase.QueryAsync(database, """
            SELECT t.tgname FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname='public' AND c.relname=@table AND NOT t.tgisinternal ORDER BY t.tgname
            """, reader => reader.GetString(0), ("table", table.Table));
        var schema = await SecurityArchitectureRlsSchemaIdentity.CaptureAsync(database, table.Table);
        return new(table.Table, table.TenantIdentityKind, Digest(table.Predicate), "SEEDED", operations,
            new(exposure.AffectedRows, restored.AffectedRows, revoked.Mechanism, revoked.SqlState, restoredPositive.AffectedRows, broadGrantDetected), guards,
            table.TenantIdentityKind == "PARENT" ? "PARENT_REASSIGNMENT" : "TENANT_REASSIGNMENT",
            schema, await SecurityArchitectureRlsUnavailableOperations.BindAsync(table.Table, schema),
            await SecurityArchitectureRlsGuardedProbes.BindAsync(table.Table, schema));
    }

    private static OperationResult CreateResult(string action, string situation, string expected, Observation observed, int positive)
    {
        var matches = observed.Mechanism == expected && (expected == "ALLOWED" ? observed.AffectedRows > 0 : observed.AffectedRows == 0);
        var result = observed.Mechanism switch
        {
            "TRIGGER_REJECTION" or "CONSTRAINT_REJECTION" or "MISSING_FIXTURE" or "UNSUPPORTED_OPERATION" => "UNVERIFIED",
            "UNEXPECTED_ERROR" => "ERROR",
            _ => positive <= 0 ? "UNVERIFIED" : matches ? "PASS" : "FAIL"
        };
        return new(action, situation == "unauthorizedRole" ? "syntheticUnauthorized" : "syntheticApplication", observed.DatabaseRole, situation, expected,
            observed.Mechanism, observed.SqlState, positive, observed.AffectedRows, result, observed.ReasonCode, observed.SourceRejectionIdentity);
    }

    private static async Task<Observation> ObserveAsync(string connectionString, string? tenant, string action, string sql,
        IReadOnlyList<(string Name, object Value, string Type)> parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var identityCommand = new NpgsqlCommand("SELECT current_user", connection);
        var databaseRole = (string)(await identityCommand.ExecuteScalarAsync())!;
        Assert.Equal(new NpgsqlConnectionStringBuilder(connectionString).Username, databaseRole);
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            if (tenant is not null)
            {
                await using var context = new NpgsqlCommand("SELECT set_config('coglatas.tenant_id',@tenant,true)", connection);
                context.Parameters.AddWithValue("tenant", tenant);
                await context.ExecuteScalarAsync();
            }
            await using var command = new NpgsqlCommand(sql, connection);
            foreach (var (name, value, type) in parameters)
            {
                var parameter = new NpgsqlParameter(name, value);
                if (type == "jsonb") parameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
                command.Parameters.Add(parameter);
            }
            var count = action == "SELECT" ? checked((int)(long)(await command.ExecuteScalarAsync())!) : await command.ExecuteNonQueryAsync();
            return new(count == 0 ? "RLS_FILTER" : "ALLOWED", null, count, null, databaseRole);
        }
        catch (PostgresException error)
        {
            var mechanism = Classify(error);
            var function = Regex.Match(error.Where ?? "", @"PL/pgSQL function (?:(?<schema>[a-z0-9_]+)\.)?(?<function>[a-z0-9_]+)\(", RegexOptions.CultureInvariant);
            var identity = mechanism is "TRIGGER_REJECTION" or "CONSTRAINT_REJECTION" ?
                new RejectionIdentity(error.ConstraintName,
                    function.Success && function.Groups["schema"].Success ? function.Groups["schema"].Value : null,
                    function.Success ? function.Groups["function"].Value : null) : null;
            return new(mechanism, error.SqlState, 0, error.ConstraintName ?? (mechanism == "TRIGGER_REJECTION" ? "SourceMutationGuard" : null), databaseRole, identity);
        }
        finally { await transaction.RollbackAsync(); }
    }

    internal static string Classify(PostgresException error) => error.SqlState switch
    {
        PostgresErrorCodes.InsufficientPrivilege when error.Routine == "exec_stmt_raise" => "TRIGGER_REJECTION",
        PostgresErrorCodes.InsufficientPrivilege when error.Routine == "ExecWithCheckOptions" => "RLS_WITH_CHECK",
        PostgresErrorCodes.InsufficientPrivilege when error.Routine is "aclcheck_error" or "aclcheck_error_col" => "GRANT_DENIAL",
        PostgresErrorCodes.RaiseException => "TRIGGER_REJECTION",
        // The canonical UI guards deliberately raise check_violation; SQLSTATE alone does not identify a native constraint.
        PostgresErrorCodes.CheckViolation when error.Routine == "exec_stmt_raise" => "TRIGGER_REJECTION",
        PostgresErrorCodes.CheckViolation or PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.RestrictViolation or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.NotNullViolation => "CONSTRAINT_REJECTION",
        _ => "UNEXPECTED_ERROR"
    };

    private static async Task<RowCommand> CloneInsertAsync(string database, TenantTable table, IReadOnlyList<Column> columns, string where)
    {
        var writable = columns.Where(column => !column.Generated).ToArray();
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT " + string.Join(",", writable.Select(column => Quote(column.Name))) +
            " FROM public." + Quote(table.Table) + " WHERE " + where + " LIMIT 1", connection);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("A migrated source row fixture is missing for " + table.Table);
        var parameters = new List<(string Name, object Value, string Type)>();
        for (var i = 0; i < writable.Length; i++)
        {
            var column = writable[i];
            object value = reader.GetValue(i);
            if (!column.Foreign && (column.Primary || column.Unique) && column.Name != "TenantId")
            {
                if (value is Guid) value = Guid.NewGuid();
                else if (value is int number) value = number + 1;
                else if (value is long number64) value = number64 + 1;
                else if (value is string text && text.Length > 0) value = text[..^1] + (text[^1] == 'x' ? "y" : "x");
            }
            parameters.Add(("p" + i, value, column.Type));
        }
        return new("INSERT INTO public." + Quote(table.Table) + " (" + string.Join(",", writable.Select(column => Quote(column.Name))) +
            ") VALUES (" + string.Join(",", parameters.Select(parameter => "@" + parameter.Name)) + ")", parameters);
    }

    private static Task<List<Column>> ColumnsAsync(string database, string table) => PostgreSqlMigrationTestDatabase.QueryAsync(database, """
        SELECT a.attname,t.typname,a.attgenerated<>'',
            EXISTS(SELECT 1 FROM pg_constraint k WHERE k.conrelid=c.oid AND k.contype='p' AND a.attnum=ANY(k.conkey)),
            EXISTS(SELECT 1 FROM pg_constraint k WHERE k.conrelid=c.oid AND k.contype='f' AND a.attnum=ANY(k.conkey)),
            EXISTS(SELECT 1 FROM pg_index i WHERE i.indrelid=c.oid AND i.indisunique AND a.attnum=ANY(i.indkey))
        FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace JOIN pg_attribute a ON a.attrelid=c.oid
        JOIN pg_type t ON t.oid=a.atttypid WHERE n.nspname='public' AND c.relname=@table AND a.attnum>0 AND NOT a.attisdropped ORDER BY a.attnum
        """, reader => new Column(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), reader.GetBoolean(3), reader.GetBoolean(4), reader.GetBoolean(5)), ("table", table));

    private static async Task<List<TenantTable>> TablesAsync(string database)
    {
        var tables = await PostgreSqlMigrationTestDatabase.QueryAsync(database, """
            SELECT c.relname,t.typname FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            JOIN pg_attribute a ON a.attrelid=c.oid JOIN pg_type t ON t.oid=a.atttypid
            WHERE n.nspname='public' AND c.relkind IN ('r','p') AND a.attname='TenantId' AND NOT a.attisdropped ORDER BY c.relname
            """, reader => new TenantTable(reader.GetString(0), reader.GetString(1) == "uuid" ? "UUID" : "TEXT",
                "\"TenantId\"::text=current_setting('coglatas.tenant_id',true)", "TenantId"));
        Assert.Equal(103, tables.Count);
        tables.Add(new("file_selection_snapshot_items", "PARENT",
            "EXISTS (SELECT 1 FROM public.file_selection_snapshots parent WHERE parent.\"Id\"=\"SelectionSnapshotId\")", "SelectionSnapshotId"));
        tables.Add(new("coglatas_ui_canonical_change_journal", "PARENT",
            "EXISTS (SELECT 1 FROM public.coglatas_ui_canonical_revision_heads parent WHERE parent.\"ScopeKey\"=coglatas_ui_canonical_change_journal.\"ScopeKey\")", "ScopeKey"));
        return tables.OrderBy(table => table.Table, StringComparer.Ordinal).ToList();
    }

    private static string ScopeWhere(TenantTable table, Guid tenant) => table.Table switch
    {
        "file_selection_snapshot_items" => "\"SelectionSnapshotId\" IN (SELECT \"Id\" FROM public.file_selection_snapshots WHERE \"TenantId\"='" + tenant + "')",
        "coglatas_ui_canonical_change_journal" => "\"ScopeKey\"='" + tenant.ToString("N") + "'",
        _ => "\"TenantId\"::text='" + tenant + "'"
    };
    private static async Task<string> ScopedWhereAsync(string database, TenantTable table, Guid tenant) => table.Table == "file_selection_snapshot_items"
        ? "\"SelectionSnapshotId\"='" + await PostgreSqlMigrationTestDatabase.ScalarAsync<Guid>(database,
            "SELECT \"Id\" FROM public.file_selection_snapshots WHERE \"TenantId\"=@tenant", ("tenant", tenant)) + "'"
        : ScopeWhere(table, tenant);

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    private static string Connection(string database, string role, string password) => new NpgsqlConnectionStringBuilder(database)
        { Username = role, Password = password, MaxPoolSize = 1, Multiplexing = false }.ConnectionString;
    private static void ClearPool(string connectionString)
    {
        using var connection = new NpgsqlConnection(connectionString);
        NpgsqlConnection.ClearPool(connection);
    }
    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static Task<bool> TruncateGrantedAsync(string database, string role, string table) =>
        PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database, "SELECT has_table_privilege(@role,@table,'TRUNCATE')",
            ("role", role), ("table", "public." + Quote(table)));
    private static async Task ExecuteFixtureAsync(string database, RowCommand insert)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(insert.Sql, connection);
        foreach (var (name, value, type) in insert.Parameters)
        {
            var parameter = new NpgsqlParameter(name, value);
            if (type == "jsonb") parameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
            command.Parameters.Add(parameter);
        }
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task AssertPoolResetAsync(string app, IReadOnlyList<TenantTable> tables, Guid alpha, Guid beta)
    {
        int backend;
        await using (var connection = new NpgsqlConnection(app))
        {
            await connection.OpenAsync();
            backend = connection.ProcessID;
            await using var transaction = await connection.BeginTransactionAsync();
            await using var context = new NpgsqlCommand("SELECT set_config('coglatas.tenant_id',@tenant,true)", connection);
            context.Parameters.AddWithValue("tenant", alpha.ToString());
            await context.ExecuteScalarAsync();
            await transaction.CommitAsync();
        }
        await using (var connection = new NpgsqlConnection(app))
        {
            await connection.OpenAsync();
            Assert.Equal(backend, connection.ProcessID);
            foreach (var table in tables)
            {
                await using var command = new NpgsqlCommand("SELECT count(*) FROM public." + Quote(table.Table), connection);
                Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
            }
        }
        foreach (var table in tables)
            Assert.True((await ObserveAsync(app, beta.ToString(), "SELECT", "SELECT count(*) FROM public." + Quote(table.Table), [])).AffectedRows > 0);
    }

    private static async Task<IReadOnlyList<RoleObservation>> VerifyRolesAsync(string database, string role, string deniedRole)
    {
        var roles = await PostgreSqlMigrationTestDatabase.QueryAsync(database, """
            SELECT rolname,rolsuper,rolbypassrls,rolcreatedb,rolcreaterole,rolinherit,
                (SELECT count(*)::int FROM pg_auth_members WHERE member=r.oid),
                (SELECT count(*)::int FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relowner=r.oid)
            FROM pg_roles r WHERE rolname=@app OR rolname=@denied ORDER BY rolname
            """, reader => new RoleObservation(reader.GetString(0) == role ? "syntheticApplication" : "syntheticUnauthorized",
                reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2), reader.GetBoolean(3), reader.GetBoolean(4), reader.GetBoolean(5), reader.GetInt32(6), reader.GetInt32(7)),
            ("app", role), ("denied", deniedRole));
        Assert.Equal(2, roles.Count);
        Assert.All(roles, observed => Assert.True(!observed.IsSuperuser && !observed.BypassRls && !observed.CanCreateDb && !observed.CanCreateRole &&
            !observed.InheritsRoles && observed.MembershipCount == 0 && observed.ProtectedTableOwnershipCount == 0));
        return roles;
    }

    private static async Task WritePrivateAsync(string database, IReadOnlyList<RoleObservation> roles, IReadOnlyList<TableResult> tables)
    {
        var directory = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_PRIVATE_INVENTORY_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        await using var assembly = File.OpenRead(typeof(SecurityArchitectureRlsOperationTests).Assembly.Location);
        var assemblyDigest = Convert.ToHexString(await SHA256.HashDataAsync(assembly)).ToLowerInvariant();
        var environment = new
        {
            dotnetVersion = Environment.Version.ToString(), npgsqlVersion = typeof(NpgsqlConnection).Assembly.GetName().Version!.ToString(),
            postgresVersion = await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database, "SHOW server_version"),
            fixture = "isolated-migrated-postgresql"
        };
        var candidate = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_CANDIDATE_SHA");
        if (candidate is null || candidate.Length != 40 || candidate.Any(character => !Uri.IsHexDigit(character))) candidate = null;
        Directory.CreateDirectory(directory);
        await using var output = new FileStream(Path.Combine(directory, "draft-rls-operation-matrix.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(output, new
        {
            schemaVersion = 1, approval = "DRAFT", ownerApproval = (string?)null, candidateSha = candidate, testAssemblyDigest = assemblyDigest,
            executedAtUtc = DateTimeOffset.UtcNow, environment, environmentFingerprint = Digest(JsonSerializer.Serialize(environment)),
            executionScope = "ISOLATED_SYNTHETIC_MODEL_ROWS", productRlsAppliedCount = 0,
            applicationRoleEquivalence = "UNVERIFIED", workerRoleEquivalence = "UNVERIFIED", preAvaloniaVerdict = "PRE-AVALONIA SEC-ARCH: BLOCKED",
            roles, tables, blindSpots = new[] { "Policies and fixture CRUD grants are draft hypotheses, not owner-approved normative roles.",
                "Custom tenant settings are mutable by a role executing arbitrary SQL.", "Host authentication/context, worker operations, bootstrap and product activation remain unqualified.",
                "Constraint/trigger-blocked probes and missing positive operations do not count as RLS denial evidence.",
                "Wrong-ownership probes cover tenant/parent reassignment; same-tenant resource, subject and capability semantics remain unverified.",
                "Revocation controls observe current database SELECT privileges; they do not certify session/membership/role revocation integration." }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
    }
}
