using Coglatas.Domain.Entities;
using Coglatas.Tests.PostgreSql;
using Npgsql;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed class SecurityArchitectureRlsTests
{
    [PostgreSqlFact]
    public async Task SyntheticOutboxAndAuditRlsRequireContextAndRejectTenantMutationAndBypass()
    {
        var root = PostgreSqlTestEnvironment.RequireConnectionString();
        var role = "sec_arch_app_" + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        try
        {
            await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(root, async database =>
            {
                await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
                var alpha = new Tenant { Name = "Synthetic Alpha", DisplayName = "Synthetic Alpha", Slug = "sec-arch-alpha" };
                var beta = new Tenant { Name = "Synthetic Beta", DisplayName = "Synthetic Beta", Slug = "sec-arch-beta" };
                db.Tenants.AddRange(alpha, beta);
                var alphaEvent = Event(alpha.Id);
                var betaEvent = Event(beta.Id);
                db.OutboxEvents.AddRange(alphaEvent, betaEvent);
                db.AuditLogs.AddRange(Audit(alpha.Id), Audit(beta.Id));
                await db.SaveChangesAsync();
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
                    $"""
                    CREATE ROLE "{role}" LOGIN PASSWORD '{password}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
                    GRANT USAGE ON SCHEMA public TO "{role}";
                    GRANT SELECT, INSERT, UPDATE, DELETE ON outbox_events TO "{role}";
                    GRANT SELECT, INSERT ON audit_logs TO "{role}";
                    ALTER TABLE outbox_events ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE outbox_events FORCE ROW LEVEL SECURITY;
                    CREATE POLICY sec_arch_synthetic_tenant ON outbox_events
                        USING ("TenantId"::text = current_setting('coglatas.tenant_id', true))
                        WITH CHECK ("TenantId"::text = current_setting('coglatas.tenant_id', true));
                    ALTER TABLE audit_logs ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE audit_logs FORCE ROW LEVEL SECURITY;
                    CREATE POLICY sec_arch_synthetic_tenant ON audit_logs
                        USING ("TenantId"::text = current_setting('coglatas.tenant_id', true))
                        WITH CHECK ("TenantId"::text = current_setting('coglatas.tenant_id', true));
                    """);
                var app = new NpgsqlConnectionStringBuilder(database)
                {
                    Username = role, Password = password, MaxPoolSize = 1, Multiplexing = false
                }.ConnectionString;
                try
                {
                    Assert.True(await RoleAndPolicySafeAsync(database, role));
                    await using (var connection = new NpgsqlConnection(app))
                    {
                        await connection.OpenAsync();
                        Assert.Equal(0L, await CountAsync(connection, "outbox_events"));
                        await using (var transaction = await connection.BeginTransactionAsync())
                        {
                            await SetTenantAsync(connection, alpha.Id.ToString());
                            Assert.Equal(1L, await CountAsync(connection, "outbox_events"));
                            Assert.Equal(1L, await CountAsync(connection, "audit_logs"));
                            Assert.Equal(0, await ExecuteAsync(connection,
                                "UPDATE outbox_events SET \"AttemptCount\"=1 WHERE \"Id\"=@id", ("id", betaEvent.Id)));
                            Assert.Equal(0, await ExecuteAsync(connection,
                                "DELETE FROM outbox_events WHERE \"Id\"=@id", ("id", betaEvent.Id)));
                            Assert.Equal(1, await InsertEventAsync(connection, alpha.Id));
                            await transaction.RollbackAsync();
                        }
                        Assert.Equal(0L, await CountAsync(connection, "outbox_events"));
                        await AssertRlsErrorAsync(connection, alpha.Id,
                            "UPDATE outbox_events SET \"TenantId\"=@tenant WHERE \"Id\"=@id",
                            ("tenant", beta.Id), ("id", alphaEvent.Id));
                        await AssertRlsErrorAsync(connection, alpha.Id,
                            """
                            INSERT INTO outbox_events ("Id","TenantId","EventType","PayloadSchemaVersion","AggregateType",
                                "AggregateId","OccurredAt","PayloadJson","RoutingJson","Status","AttemptCount","CreatedAt")
                            VALUES (@id,@tenant,'Synthetic',1,'Synthetic',@id,now(),'{}','{}','Pending',0,now())
                            """, ("id", Guid.NewGuid()), ("tenant", beta.Id));
                        await using (var transaction = await connection.BeginTransactionAsync())
                        {
                            await SetTenantAsync(connection, "invalid-context");
                            Assert.Equal(0L, await CountAsync(connection, "outbox_events"));
                            await transaction.RollbackAsync();
                        }
                        await using (var transaction = await connection.BeginTransactionAsync())
                        {
                            await SetTenantAsync(connection, beta.Id.ToString());
                            Assert.Equal(1L, await CountAsync(connection, "outbox_events"));
                            await transaction.CommitAsync();
                        }
                        Assert.Equal(0L, await CountAsync(connection, "outbox_events"));
                    }
                    // MaxPoolSize=1 forces reuse; transaction-local context must not survive close/reopen.
                    await using (var reused = new NpgsqlConnection(app))
                    {
                        await reused.OpenAsync();
                        Assert.Equal(0L, await CountAsync(reused, "outbox_events"));
                        await using var transaction = await reused.BeginTransactionAsync();
                        await SetTenantAsync(reused, alpha.Id.ToString());
                        Assert.Equal(1L, await CountAsync(reused, "outbox_events"));
                        await transaction.RollbackAsync();
                    }

                    Assert.Equal(2L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(
                        database, "SELECT count(*) FROM outbox_events"));
                    // Deliberate mutations prove that absence of protections is observed, not false-green.
                    await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"ALTER ROLE \"{role}\" BYPASSRLS");
                    Assert.False(await RoleAndPolicySafeAsync(database, role));
                    Assert.Equal(2L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(app, "SELECT count(*) FROM outbox_events"));
                    await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"ALTER ROLE \"{role}\" NOBYPASSRLS");
                    await PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
                        "ALTER POLICY sec_arch_synthetic_tenant ON outbox_events USING (true) WITH CHECK (true)");
                    Assert.False(await RoleAndPolicySafeAsync(database, role));
                    Assert.Equal(2L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(app, "SELECT count(*) FROM outbox_events"));
                    await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
                        ALTER POLICY sec_arch_synthetic_tenant ON outbox_events
                        USING ("TenantId"::text = current_setting('coglatas.tenant_id', true))
                        WITH CHECK ("TenantId"::text = current_setting('coglatas.tenant_id', true))
                        """);
                    await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, "ALTER TABLE outbox_events DISABLE ROW LEVEL SECURITY");
                    Assert.False(await RoleAndPolicySafeAsync(database, role));
                    Assert.Equal(2L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(app, "SELECT count(*) FROM outbox_events"));
                    await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, "ALTER TABLE outbox_events ENABLE ROW LEVEL SECURITY");
                    await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"GRANT SELECT ON users TO \"{role}\"");
                    Assert.False(await RoleAndPolicySafeAsync(database, role));
                    await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"REVOKE SELECT ON users FROM \"{role}\"");
                    Assert.True(await RoleAndPolicySafeAsync(database, role));
                    await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, $"REVOKE SELECT ON outbox_events FROM \"{role}\"");
                    var revoked = await Assert.ThrowsAsync<PostgresException>(() =>
                        PostgreSqlMigrationTestDatabase.ScalarAsync<long>(app, "SELECT count(*) FROM outbox_events"));
                    Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, revoked.SqlState);
                }
                finally
                {
                    using var pooled = new NpgsqlConnection(app);
                    NpgsqlConnection.ClearPool(pooled);
                }
            });
        }
        finally
        {
            // The temporary database is dropped first, releasing its grants/policy dependencies.
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(root, $"DROP ROLE IF EXISTS \"{role}\"");
        }
    }

    private static OutboxEvent Event(Guid tenant) => new(Guid.NewGuid())
    {
        TenantId = tenant, EventType = "Synthetic", PayloadSchemaVersion = 1,
        AggregateType = "Synthetic", AggregateId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow,
        PayloadJson = "{}", RoutingJson = "{}"
    };
    private static AuditLog Audit(Guid tenant) => new()
    { TenantId = tenant, Action = "Synthetic", EntityType = "Synthetic", CreatedAt = DateTimeOffset.UtcNow };

    private static Task<bool> RoleAndPolicySafeAsync(string database, string role) =>
        PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database, """
            SELECT NOT r.rolsuper AND NOT r.rolbypassrls AND NOT r.rolcreaterole AND NOT r.rolcreatedb
                AND NOT has_table_privilege(r.oid,'users','SELECT')
                AND NOT EXISTS(SELECT 1 FROM pg_auth_members WHERE member=r.oid)
                AND (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                    WHERE n.nspname='public' AND c.relname IN ('outbox_events','audit_logs')
                    AND c.relrowsecurity AND c.relforcerowsecurity AND c.relowner<>r.oid
                    AND (SELECT count(*) FROM pg_policy p WHERE p.polrelid=c.oid)=1
                    AND EXISTS(SELECT 1 FROM pg_policy p WHERE p.polrelid=c.oid
                        AND p.polname='sec_arch_synthetic_tenant' AND p.polpermissive AND p.polcmd='*'
                        AND p.polroles='{0}'::oid[]
                        AND pg_get_expr(p.polqual,c.oid)='(("TenantId")::text = current_setting(''coglatas.tenant_id''::text, true))'
                        AND pg_get_expr(p.polwithcheck,c.oid)='(("TenantId")::text = current_setting(''coglatas.tenant_id''::text, true))'))=2
            FROM pg_roles r WHERE r.rolname=@role
            """, ("role", role));

    private static async Task SetTenantAsync(NpgsqlConnection connection, string tenant)
    {
        await using var command = new NpgsqlCommand("SELECT set_config('coglatas.tenant_id',@tenant,true)", connection);
        command.Parameters.AddWithValue("tenant", tenant);
        await command.ExecuteScalarAsync();
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string table)
    {
        Assert.Contains(table, new[] { "outbox_events", "audit_logs" });
        await using var command = new NpgsqlCommand("SELECT count(*) FROM " + table, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static Task<int> InsertEventAsync(NpgsqlConnection connection, Guid tenant) => ExecuteAsync(connection, """
        INSERT INTO outbox_events ("Id","TenantId","EventType","PayloadSchemaVersion","AggregateType",
            "AggregateId","OccurredAt","PayloadJson","RoutingJson","Status","AttemptCount","CreatedAt")
        VALUES (@id,@tenant,'Synthetic',1,'Synthetic',@id,now(),'{}','{}','Pending',0,now())
        """, ("id", Guid.NewGuid()), ("tenant", tenant));

    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertRlsErrorAsync(NpgsqlConnection connection, Guid tenant, string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var transaction = await connection.BeginTransactionAsync();
        await SetTenantAsync(connection, tenant.ToString());
        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, sql, parameters));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
        await transaction.RollbackAsync();
    }
}
