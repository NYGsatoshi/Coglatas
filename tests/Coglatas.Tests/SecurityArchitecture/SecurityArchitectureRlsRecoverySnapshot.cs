using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace Coglatas.Tests.SecurityArchitecture;

/// <summary>Test-owned canonical catalogue/data identities; row contents and function bodies never leave PostgreSQL.</summary>
internal static class SecurityArchitectureRlsRecoverySnapshot
{
    internal sealed record Table(string Name, long RowCount, string DataDigest, string NativeSchemaDigest, string SecurityDigest);
    internal sealed record Snapshot(IReadOnlyList<Table> Tables, string NativeSchemaDigest, string AuthorityDigest);

    internal static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    internal static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    internal static async Task<Snapshot> CaptureAsync(string database, string fixtureRole)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        await ExecuteAsync(connection, "SET LOCAL TIME ZONE 'UTC'");
        var names = new List<string>();
        await using (var command = new NpgsqlCommand("""
            SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname='public' AND c.relkind IN ('r','p') ORDER BY c.relname COLLATE "C"
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        // Keep a consistent snapshot and prevent concurrent DDL/row writers during identity capture.
        await ExecuteAsync(connection, "LOCK TABLE " + string.Join(",", names.Select(name => "public." + Quote(name))) + " IN SHARE MODE");
        var tables = new List<Table>();
        foreach (var name in names)
        {
            long count;
            string data;
            await using (var command = new NpgsqlCommand("SELECT count(*),encode(sha256(convert_to(" +
                "coalesce(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text COLLATE \"C\")::text,'[]'),'UTF8')),'hex') FROM public." + Quote(name) + " t", connection))
            await using (var reader = await command.ExecuteReaderAsync())
            {
                await reader.ReadAsync();
                count = reader.GetInt64(0);
                data = reader.GetString(1);
            }
            var native = await ScalarAsync(connection, """
                SELECT encode(sha256(convert_to(jsonb_build_object(
                  'kind',c.relkind,'owner',pg_get_userbyid(c.relowner),'persistence',c.relpersistence,
                  'options',c.reloptions,'replicaIdentity',c.relreplident,
                  'columns',(SELECT coalesce(jsonb_agg(jsonb_build_array(a.attnum,a.attname,format_type(a.atttypid,a.atttypmod),
                    a.attnotnull,a.attidentity,a.attgenerated,a.attcollation::regcollation::text,
                    pg_get_expr(d.adbin,d.adrelid)) ORDER BY a.attnum),'[]'::jsonb)
                    FROM pg_attribute a LEFT JOIN pg_attrdef d ON d.adrelid=a.attrelid AND d.adnum=a.attnum
                    WHERE a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped),
                  'constraints',(SELECT coalesce(jsonb_agg(jsonb_build_array(k.conname,k.contype,k.convalidated,k.condeferrable,
                    k.condeferred,pg_get_constraintdef(k.oid,true)) ORDER BY k.conname COLLATE "C"),'[]'::jsonb)
                    FROM pg_constraint k WHERE k.conrelid=c.oid),
                  'indexes',(SELECT coalesce(jsonb_agg(jsonb_build_array(i.relname,x.indisvalid,x.indisready,pg_get_indexdef(x.indexrelid))
                    ORDER BY i.relname COLLATE "C"),'[]'::jsonb) FROM pg_index x JOIN pg_class i ON i.oid=x.indexrelid WHERE x.indrelid=c.oid),
                  'triggers',(SELECT coalesce(jsonb_agg(jsonb_build_array(t.tgname,t.tgenabled,t.tgisinternal,
                    pg_get_triggerdef(t.oid,true),encode(sha256(convert_to(pg_get_functiondef(t.tgfoid),'UTF8')),'hex'))
                    ORDER BY t.tgname COLLATE "C"),'[]'::jsonb) FROM pg_trigger t WHERE t.tgrelid=c.oid)
                )::text,'UTF8')),'hex') FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='public' AND c.relname=@table
                """, ("table", name));
            var security = await ScalarAsync(connection, """
                SELECT encode(sha256(convert_to(jsonb_build_object(
                  'enabled',c.relrowsecurity,'forced',c.relforcerowsecurity,
                  'policies',(SELECT coalesce(jsonb_agg(jsonb_build_array(p.polname,p.polcmd,p.polpermissive,
                    (SELECT jsonb_agg(CASE WHEN r=0 THEN 'PUBLIC' ELSE pg_get_userbyid(r) END ORDER BY r)
                     FROM unnest(p.polroles) r),pg_get_expr(p.polqual,p.polrelid),pg_get_expr(p.polwithcheck,p.polrelid))
                    ORDER BY p.polname COLLATE "C"),'[]'::jsonb) FROM pg_policy p WHERE p.polrelid=c.oid),
                  'acl',(SELECT coalesce(jsonb_agg(jsonb_build_array(pg_get_userbyid(x.grantor),
                    CASE WHEN x.grantee=0 THEN 'PUBLIC' ELSE pg_get_userbyid(x.grantee) END,x.privilege_type,x.is_grantable)
                    ORDER BY x.grantor,x.grantee,x.privilege_type),'[]'::jsonb)
                    FROM aclexplode(CASE WHEN cardinality(c.relacl)=0 THEN NULL ELSE coalesce(c.relacl,acldefault('r',c.relowner)) END) x),
                  'columnAcl',(SELECT coalesce(jsonb_agg(jsonb_build_array(a.attname,pg_get_userbyid(x.grantor),
                    CASE WHEN x.grantee=0 THEN 'PUBLIC' ELSE pg_get_userbyid(x.grantee) END,x.privilege_type,x.is_grantable)
                    ORDER BY a.attnum,x.grantor,x.grantee,x.privilege_type),'[]'::jsonb)
                    FROM pg_attribute a CROSS JOIN LATERAL aclexplode(CASE WHEN cardinality(a.attacl)>0 THEN a.attacl END) x
                    WHERE a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped)
                )::text,'UTF8')),'hex') FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='public' AND c.relname=@table
                """, ("table", name));
            tables.Add(new(name, count, data, native, security));
        }
        var functions = await ScalarAsync(connection, """
            SELECT encode(sha256(convert_to(coalesce(jsonb_agg(jsonb_build_array(p.oid::regprocedure::text,
                pg_get_userbyid(p.proowner),p.prosecdef,p.proconfig,
                encode(sha256(convert_to(pg_get_functiondef(p.oid),'UTF8')),'hex')) ORDER BY p.oid::regprocedure::text COLLATE "C")::text,'[]'),'UTF8')),'hex')
            FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public' AND p.prokind IN ('f','p')
            """);
        var authority = await ScalarAsync(connection, """
            SELECT encode(sha256(convert_to(jsonb_build_object(
              'schema',(SELECT jsonb_build_array(pg_get_userbyid(n.nspowner),
                (SELECT jsonb_agg(jsonb_build_array(x.grantor,x.grantee,x.privilege_type,x.is_grantable)
                 ORDER BY x.grantor,x.grantee,x.privilege_type) FROM aclexplode(CASE WHEN cardinality(n.nspacl)=0 THEN NULL ELSE coalesce(n.nspacl,acldefault('n',n.nspowner)) END) x))
                FROM pg_namespace n WHERE n.nspname='public'),
              'defaults',(SELECT coalesce(jsonb_agg(jsonb_build_array(d.defaclrole,d.defaclnamespace,d.defaclobjtype,d.defaclacl::text)
                ORDER BY d.defaclrole,d.defaclnamespace,d.defaclobjtype),'[]'::jsonb) FROM pg_default_acl d),
              'roles',(SELECT coalesce(jsonb_agg(jsonb_build_array(r.rolname,r.rolsuper,r.rolbypassrls,r.rolcreatedb,r.rolcreaterole,
                r.rolinherit,r.rolcanlogin,r.rolconfig,(SELECT coalesce(jsonb_agg(jsonb_build_array(m.roleid,m.admin_option,m.inherit_option,m.set_option)
                  ORDER BY m.roleid),'[]'::jsonb) FROM pg_auth_members m WHERE m.member=r.oid)) ORDER BY r.rolname COLLATE "C"),'[]'::jsonb)
                FROM pg_roles r WHERE r.rolname=@role OR r.oid IN
                  (SELECT relowner FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public')),
              'functionAcl',(SELECT coalesce(jsonb_agg(jsonb_build_array(p.oid::regprocedure::text,x.grantor,x.grantee,x.privilege_type,x.is_grantable)
                ORDER BY p.oid::regprocedure::text COLLATE "C",x.grantor,x.grantee,x.privilege_type),'[]'::jsonb)
                FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace CROSS JOIN LATERAL aclexplode(CASE WHEN cardinality(p.proacl)=0 THEN NULL ELSE coalesce(p.proacl,acldefault('f',p.proowner)) END) x
                WHERE n.nspname='public')
            )::text,'UTF8')),'hex')
            """, ("role", fixtureRole));
        await transaction.CommitAsync();
        return new(tables, Digest(functions + JsonSerializer.Serialize(tables.Select(table => new { table.Name, table.NativeSchemaDigest }))), authority);
    }

    internal static bool SameDataAndNativeSchema(Snapshot expected, Snapshot observed) =>
        expected.NativeSchemaDigest == observed.NativeSchemaDigest && expected.Tables.Select(table =>
            (table.Name, table.RowCount, table.DataDigest, table.NativeSchemaDigest)).SequenceEqual(observed.Tables.Select(table =>
            (table.Name, table.RowCount, table.DataDigest, table.NativeSchemaDigest)));

    internal static bool SameAll(Snapshot expected, Snapshot observed) => SameDataAndNativeSchema(expected, observed) &&
        expected.AuthorityDigest == observed.AuthorityDigest && expected.Tables.SequenceEqual(observed.Tables);

    internal static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> ScalarAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
