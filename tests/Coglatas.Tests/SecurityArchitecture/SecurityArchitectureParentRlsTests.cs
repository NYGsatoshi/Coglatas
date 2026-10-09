using Coglatas.Tests.PostgreSql;
using Npgsql;

namespace Coglatas.Tests.SecurityArchitecture;

/// <summary>Exercises draft synthetic policies on migrated tables; it does not activate product RLS.</summary>
public sealed class SecurityArchitectureParentRlsTests
{
    [PostgreSqlFact]
    public async Task SnapshotItemsRequireVisibleParentForReadAndEveryMutation()
    {
        await WithApplicationRoleAsync(async (database, connection) =>
        {
            var alpha = Guid.NewGuid();
            var beta = Guid.NewGuid();
            var alphaSnapshot = Guid.NewGuid();
            var betaSnapshot = Guid.NewGuid();
            var alphaFile = Guid.NewGuid();
            var betaFile = Guid.NewGuid();
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
                INSERT INTO file_selection_snapshots
                    ("Id","TenantId","ActorUserId","WorkspaceId","NormalizedQuery","FileKind",
                     "OnlyMyUploads","ExpiresAt","ConsumptionVersion","CreatedAt")
                VALUES (@alphaSnapshot,@alpha,@alphaFile,@alphaFile,'','All',false,now()+interval '1 hour',0,now()),
                       (@betaSnapshot,@beta,@betaFile,@betaFile,'','All',false,now()+interval '1 hour',0,now());
                INSERT INTO file_selection_snapshot_items ("SelectionSnapshotId","FileObjectId")
                VALUES (@alphaSnapshot,@alphaFile),(@betaSnapshot,@betaFile);
                ALTER TABLE file_selection_snapshots ENABLE ROW LEVEL SECURITY;
                ALTER TABLE file_selection_snapshots FORCE ROW LEVEL SECURITY;
                CREATE POLICY sec_arch_synthetic_parent ON file_selection_snapshots
                    USING ("TenantId"::text=current_setting('coglatas.tenant_id',true))
                    WITH CHECK ("TenantId"::text=current_setting('coglatas.tenant_id',true));
                ALTER TABLE file_selection_snapshot_items ENABLE ROW LEVEL SECURITY;
                ALTER TABLE file_selection_snapshot_items FORCE ROW LEVEL SECURITY;
                CREATE POLICY sec_arch_synthetic_child ON file_selection_snapshot_items
                    USING (EXISTS (SELECT 1 FROM file_selection_snapshots parent
                        WHERE parent."Id"="SelectionSnapshotId"))
                    WITH CHECK (EXISTS (SELECT 1 FROM file_selection_snapshots parent
                        WHERE parent."Id"="SelectionSnapshotId"));
                """, ("alpha", alpha), ("beta", beta), ("alphaSnapshot", alphaSnapshot),
                ("betaSnapshot", betaSnapshot), ("alphaFile", alphaFile), ("betaFile", betaFile));
            var role = connection.UserName ?? throw new InvalidOperationException("Synthetic application role is missing.");
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
                $"GRANT SELECT, INSERT, UPDATE, DELETE ON file_selection_snapshots, file_selection_snapshot_items TO \"{role}\"");
            await AssertProtectedAsync(database, role, "file_selection_snapshots", "file_selection_snapshot_items");
            Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT count(*) FROM file_selection_snapshot_items"));
            await InTenantAsync(connection, alpha.ToString(), async () =>
            {
                Assert.Equal(1L, await ScalarAsync<long>(connection, "SELECT count(*) FROM file_selection_snapshot_items"));
                Assert.Equal(alphaSnapshot, await ScalarAsync<Guid>(connection,
                    "SELECT \"SelectionSnapshotId\" FROM file_selection_snapshot_items"));
                Assert.Equal(0, await ExecuteAsync(connection,
                    "UPDATE file_selection_snapshot_items SET \"FileObjectId\"=@file WHERE \"SelectionSnapshotId\"=@parent",
                    ("file", Guid.NewGuid()), ("parent", betaSnapshot)));
                Assert.Equal(0, await ExecuteAsync(connection,
                    "DELETE FROM file_selection_snapshot_items WHERE \"SelectionSnapshotId\"=@parent", ("parent", betaSnapshot)));
                var file = Guid.NewGuid();
                Assert.Equal(1, await ExecuteAsync(connection,
                    "INSERT INTO file_selection_snapshot_items VALUES (@parent,@file)", ("parent", alphaSnapshot), ("file", file)));
                var nextFile = Guid.NewGuid();
                Assert.Equal(1, await ExecuteAsync(connection,
                    "UPDATE file_selection_snapshot_items SET \"FileObjectId\"=@next WHERE \"FileObjectId\"=@file",
                    ("next", nextFile), ("file", file)));
                Assert.Equal(1, await ExecuteAsync(connection,
                    "DELETE FROM file_selection_snapshot_items WHERE \"FileObjectId\"=@file", ("file", nextFile)));
            });
            await DeniedAsync(connection, alpha.ToString(), PostgresErrorCodes.InsufficientPrivilege,
                "INSERT INTO file_selection_snapshot_items VALUES (@parent,@file)", ("parent", betaSnapshot), ("file", Guid.NewGuid()));
            await DeniedAsync(connection, "", PostgresErrorCodes.InsufficientPrivilege,
                "INSERT INTO file_selection_snapshot_items VALUES (@parent,@file)", ("parent", alphaSnapshot), ("file", Guid.NewGuid()));
            await DeniedAsync(connection, alpha.ToString(), PostgresErrorCodes.InsufficientPrivilege,
                "UPDATE file_selection_snapshot_items SET \"SelectionSnapshotId\"=@beta WHERE \"SelectionSnapshotId\"=@alpha",
                ("beta", betaSnapshot), ("alpha", alphaSnapshot));
            await DeniedAsync(connection, alpha.ToString(), PostgresErrorCodes.InsufficientPrivilege,
                "INSERT INTO file_selection_snapshot_items VALUES (@parent,@file)", ("parent", Guid.NewGuid()), ("file", Guid.NewGuid()));
            await DeniedAsync(connection, alpha.ToString(), PostgresErrorCodes.InsufficientPrivilege,
                "UPDATE file_selection_snapshots SET \"TenantId\"=@beta WHERE \"Id\"=@id", ("beta", beta), ("id", alphaSnapshot));
            await InTenantAsync(connection, beta.ToString(), async () =>
            {
                Assert.Equal(1L, await ScalarAsync<long>(connection, "SELECT count(*) FROM file_selection_snapshot_items"));
                Assert.Equal(betaSnapshot, await ScalarAsync<Guid>(connection,
                    "SELECT \"SelectionSnapshotId\" FROM file_selection_snapshot_items"));
            });
            await InTenantAsync(connection, "invalid-context", async () =>
                Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT count(*) FROM file_selection_snapshot_items")));
            Assert.Equal(2L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database,
                "SELECT count(*) FROM file_selection_snapshot_items"));
            // A permissive child policy must visibly leak the foreign row even while its parent stays protected.
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
                "ALTER POLICY sec_arch_synthetic_child ON file_selection_snapshot_items USING (true) WITH CHECK (true)");
            try
            {
                await InTenantAsync(connection, alpha.ToString(), async () =>
                    Assert.False(await SingleScopeAsync(connection, "file_selection_snapshot_items", "SelectionSnapshotId", alphaSnapshot)));
            }
            finally
            {
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
                    ALTER POLICY sec_arch_synthetic_child ON file_selection_snapshot_items
                    USING (EXISTS (SELECT 1 FROM file_selection_snapshots parent WHERE parent."Id"="SelectionSnapshotId"))
                    WITH CHECK (EXISTS (SELECT 1 FROM file_selection_snapshots parent WHERE parent."Id"="SelectionSnapshotId"))
                    """);
            }
            await InTenantAsync(connection, alpha.ToString(), async () =>
                Assert.True(await SingleScopeAsync(connection, "file_selection_snapshot_items", "SelectionSnapshotId", alphaSnapshot)));
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, "ALTER TABLE file_selection_snapshots DISABLE ROW LEVEL SECURITY");
            try
            {
                await InTenantAsync(connection, alpha.ToString(), async () =>
                    Assert.False(await SingleScopeAsync(connection, "file_selection_snapshot_items", "SelectionSnapshotId", alphaSnapshot)));
            }
            finally
            {
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, "ALTER TABLE file_selection_snapshots ENABLE ROW LEVEL SECURITY");
            }
            await InTenantAsync(connection, alpha.ToString(), async () =>
                Assert.True(await SingleScopeAsync(connection, "file_selection_snapshot_items", "SelectionSnapshotId", alphaSnapshot)));
        });
    }

    [PostgreSqlFact]
    public async Task TextTenantJournalPreservesAppendOnlyRulesAndDistinguishesTriggerFromRlsDenial()
    {
        await WithApplicationRoleAsync(async (database, connection) =>
        {
            var alpha = "synthetic-alpha-" + Guid.NewGuid();
            var beta = "synthetic-beta-" + Guid.NewGuid();
            var alphaScope = Guid.NewGuid().ToString("N");
            var betaScope = Guid.NewGuid().ToString("N");
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
                INSERT INTO coglatas_ui_canonical_revision_heads ("ScopeKey","TenantId","WorkspaceId","Revision")
                VALUES (@alphaScope,@alpha,'synthetic-alpha',0),(@betaScope,@beta,'synthetic-beta',0);
                UPDATE coglatas_ui_canonical_revision_heads SET "Revision"=1;
                INSERT INTO coglatas_ui_canonical_change_journal ("ScopeKey","Revision","ChangedDomains")
                VALUES (@alphaScope,1,1),(@betaScope,1,1);
                ALTER TABLE coglatas_ui_canonical_revision_heads ENABLE ROW LEVEL SECURITY;
                ALTER TABLE coglatas_ui_canonical_revision_heads FORCE ROW LEVEL SECURITY;
                CREATE POLICY sec_arch_synthetic_parent ON coglatas_ui_canonical_revision_heads
                    USING ("TenantId"=current_setting('coglatas.tenant_id',true))
                    WITH CHECK ("TenantId"=current_setting('coglatas.tenant_id',true));
                ALTER TABLE coglatas_ui_canonical_change_journal ENABLE ROW LEVEL SECURITY;
                ALTER TABLE coglatas_ui_canonical_change_journal FORCE ROW LEVEL SECURITY;
                CREATE POLICY sec_arch_synthetic_child ON coglatas_ui_canonical_change_journal
                    USING (EXISTS (SELECT 1 FROM coglatas_ui_canonical_revision_heads parent
                        WHERE parent."ScopeKey"=coglatas_ui_canonical_change_journal."ScopeKey"))
                    WITH CHECK (EXISTS (SELECT 1 FROM coglatas_ui_canonical_revision_heads parent
                        WHERE parent."ScopeKey"=coglatas_ui_canonical_change_journal."ScopeKey"));
                """, ("alpha", alpha), ("beta", beta), ("alphaScope", alphaScope), ("betaScope", betaScope));
            var role = connection.UserName ?? throw new InvalidOperationException("Synthetic application role is missing.");
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                GRANT SELECT, INSERT, UPDATE ON coglatas_ui_canonical_revision_heads TO "{role}";
                GRANT SELECT, INSERT ON coglatas_ui_canonical_change_journal TO "{role}";
                """);
            await AssertProtectedAsync(database, role, "coglatas_ui_canonical_revision_heads", "coglatas_ui_canonical_change_journal");
            var backend = connection.ProcessID;
            Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT count(*) FROM coglatas_ui_canonical_change_journal"));
            await InTenantAsync(connection, alpha, async () =>
            {
                Assert.Equal(1L, await ScalarAsync<long>(connection, "SELECT count(*) FROM coglatas_ui_canonical_change_journal"));
                Assert.Equal(alphaScope, await ScalarAsync<string>(connection,
                    "SELECT \"ScopeKey\" FROM coglatas_ui_canonical_change_journal"));
                Assert.Equal(1, await ExecuteAsync(connection, """
                    INSERT INTO coglatas_ui_canonical_revision_heads ("ScopeKey","TenantId","WorkspaceId","Revision")
                    VALUES (@scope,@tenant,'synthetic-new',0)
                    """, ("scope", Guid.NewGuid().ToString("N")), ("tenant", alpha)));
                Assert.Equal(0, await ExecuteAsync(connection,
                    "UPDATE coglatas_ui_canonical_revision_heads SET \"Revision\"=2 WHERE \"ScopeKey\"=@scope", ("scope", betaScope)));
                Assert.Equal(1, await ExecuteAsync(connection,
                    "UPDATE coglatas_ui_canonical_revision_heads SET \"Revision\"=2 WHERE \"ScopeKey\"=@scope", ("scope", alphaScope)));
                Assert.Equal(1, await ExecuteAsync(connection,
                    "INSERT INTO coglatas_ui_canonical_change_journal (\"ScopeKey\",\"Revision\",\"ChangedDomains\") VALUES (@scope,2,1)",
                    ("scope", alphaScope)));
            });
            await DeniedAsync(connection, alpha, PostgresErrorCodes.InsufficientPrivilege, """
                INSERT INTO coglatas_ui_canonical_revision_heads ("ScopeKey","TenantId","WorkspaceId","Revision")
                VALUES (@scope,@tenant,'synthetic-foreign',0)
                """, ("scope", Guid.NewGuid().ToString("N")), ("tenant", beta));
            await DeniedAsync(connection, "", PostgresErrorCodes.InsufficientPrivilege, """
                INSERT INTO coglatas_ui_canonical_revision_heads ("ScopeKey","TenantId","WorkspaceId","Revision")
                VALUES (@scope,@tenant,'synthetic-no-context',0)
                """, ("scope", Guid.NewGuid().ToString("N")), ("tenant", alpha));
            // The existing invoker trigger runs before WITH CHECK and cannot see the foreign head.
            await DeniedAsync(connection, alpha, PostgresErrorCodes.CheckViolation,
                "INSERT INTO coglatas_ui_canonical_change_journal (\"ScopeKey\",\"Revision\",\"ChangedDomains\") VALUES (@scope,2,1)",
                ("scope", betaScope));
            // Isolate causality in this disposable database, then restore the exact existing trigger.
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
                "ALTER TABLE coglatas_ui_canonical_change_journal DISABLE TRIGGER trg_coglatas_ui_journal_insert_matches_head");
            try
            {
                await DeniedAsync(connection, alpha, PostgresErrorCodes.InsufficientPrivilege,
                    "INSERT INTO coglatas_ui_canonical_change_journal (\"ScopeKey\",\"Revision\",\"ChangedDomains\") VALUES (@scope,2,1)",
                    ("scope", betaScope));
            }
            finally
            {
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
                    "ALTER TABLE coglatas_ui_canonical_change_journal ENABLE TRIGGER trg_coglatas_ui_journal_insert_matches_head");
            }
            Assert.Equal("O", await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(database,
                "SELECT tgenabled::text FROM pg_trigger WHERE tgname='trg_coglatas_ui_journal_insert_matches_head'"));
            await DeniedAsync(connection, alpha, PostgresErrorCodes.InsufficientPrivilege,
                "UPDATE coglatas_ui_canonical_change_journal SET \"ChangedDomains\"=2 WHERE \"ScopeKey\"=@scope", ("scope", alphaScope));
            await DeniedAsync(connection, alpha, PostgresErrorCodes.InsufficientPrivilege,
                "DELETE FROM coglatas_ui_canonical_change_journal WHERE \"ScopeKey\"=@scope", ("scope", alphaScope));
            var appendOnlyError = await Assert.ThrowsAsync<PostgresException>(() => PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
                "UPDATE coglatas_ui_canonical_change_journal SET \"ChangedDomains\"=2 WHERE \"ScopeKey\"=@scope", ("scope", alphaScope)));
            Assert.Equal(PostgresErrorCodes.CheckViolation, appendOnlyError.SqlState);
            await InTenantAsync(connection, beta, async () =>
            {
                Assert.Equal(1L, await ScalarAsync<long>(connection, "SELECT count(*) FROM coglatas_ui_canonical_change_journal"));
                Assert.Equal(betaScope, await ScalarAsync<string>(connection,
                    "SELECT \"ScopeKey\" FROM coglatas_ui_canonical_change_journal"));
            }, commit: true);
            Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT count(*) FROM coglatas_ui_canonical_change_journal"));
            await InTenantAsync(connection, alpha.ToUpperInvariant(), async () =>
                Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT count(*) FROM coglatas_ui_canonical_change_journal")));
            await connection.CloseAsync();
            await connection.OpenAsync();
            Assert.Equal(backend, connection.ProcessID);
            Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT count(*) FROM coglatas_ui_canonical_change_journal"));
            Assert.Equal(2L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database,
                "SELECT count(*) FROM coglatas_ui_canonical_change_journal"));
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
                "ALTER POLICY sec_arch_synthetic_child ON coglatas_ui_canonical_change_journal USING (true) WITH CHECK (true)");
            try
            {
                await InTenantAsync(connection, alpha, async () =>
                    Assert.False(await SingleScopeAsync(connection, "coglatas_ui_canonical_change_journal", "ScopeKey", alphaScope)));
            }
            finally
            {
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
                    ALTER POLICY sec_arch_synthetic_child ON coglatas_ui_canonical_change_journal
                    USING (EXISTS (SELECT 1 FROM coglatas_ui_canonical_revision_heads parent
                        WHERE parent."ScopeKey"=coglatas_ui_canonical_change_journal."ScopeKey"))
                    WITH CHECK (EXISTS (SELECT 1 FROM coglatas_ui_canonical_revision_heads parent
                        WHERE parent."ScopeKey"=coglatas_ui_canonical_change_journal."ScopeKey"))
                    """);
            }
            await InTenantAsync(connection, alpha, async () =>
                Assert.True(await SingleScopeAsync(connection, "coglatas_ui_canonical_change_journal", "ScopeKey", alphaScope)));
        });
    }

    private static async Task WithApplicationRoleAsync(Func<string, NpgsqlConnection, Task> scenario)
    {
        var root = PostgreSqlTestEnvironment.RequireConnectionString();
        var role = "sec_arch_parent_" + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        try
        {
            await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(root, async database =>
            {
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"""
                    CREATE ROLE "{role}" LOGIN PASSWORD '{password}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
                    GRANT USAGE ON SCHEMA public TO "{role}";
                    """);
                var app = new NpgsqlConnectionStringBuilder(database)
                { Username = role, Password = password, MaxPoolSize = 1, Multiplexing = false }.ConnectionString;
                await using var connection = new NpgsqlConnection(app);
                try
                {
                    await connection.OpenAsync();
                    await scenario(database, connection);
                }
                finally
                {
                    await connection.CloseAsync();
                    NpgsqlConnection.ClearPool(connection);
                }
            });
        }
        finally
        {
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(root, $"DROP ROLE IF EXISTS \"{role}\"");
        }
    }

    private static async Task AssertProtectedAsync(string database, string role, params string[] tables)
    {
        Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database, """
            SELECT NOT rolsuper AND NOT rolbypassrls AND NOT rolcreaterole AND NOT rolcreatedb
                AND NOT EXISTS (SELECT 1 FROM pg_auth_members WHERE member=r.oid)
                AND NOT has_table_privilege(r.oid,'users','SELECT')
            FROM pg_roles r WHERE rolname=@role
            """, ("role", role)));
        foreach (var table in tables)
            Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database, """
                SELECT c.relrowsecurity AND c.relforcerowsecurity AND owner.rolname<>@role
                    AND (SELECT count(*) FROM pg_policy p WHERE p.polrelid=c.oid)=1
                FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace JOIN pg_roles owner ON owner.oid=c.relowner
                WHERE n.nspname='public' AND c.relname=@table
                """, ("role", role), ("table", table)));
    }

    private static async Task InTenantAsync(NpgsqlConnection connection, string tenant, Func<Task> action, bool commit = false)
    {
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(connection, "SELECT set_config('coglatas.tenant_id',@tenant,true)", ("tenant", tenant));
        await action();
        if (commit) await transaction.CommitAsync();
        else await transaction.RollbackAsync();
    }

    private static Task DeniedAsync(NpgsqlConnection connection, string tenant, string sqlState, string sql,
        params (string Name, object Value)[] parameters) => InTenantAsync(connection, tenant, async () =>
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, sql, parameters));
        Assert.Equal(sqlState, error.SqlState);
    });

    private static async Task<bool> SingleScopeAsync<T>(NpgsqlConnection connection, string table, string column, T expected)
    {
        Assert.Contains((table, column), new[]
        {
            ("file_selection_snapshot_items", "SelectionSnapshotId"),
            ("coglatas_ui_canonical_change_journal", "ScopeKey")
        });
        await using var command = new NpgsqlCommand($"SELECT \"{column}\" FROM {table}", connection);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() && Equals(expected, reader.GetValue(0)) && !await reader.ReadAsync();
    }

    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
