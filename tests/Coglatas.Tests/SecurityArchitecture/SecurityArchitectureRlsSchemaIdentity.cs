using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coglatas.Tests.PostgreSql;

namespace Coglatas.Tests.SecurityArchitecture;

/// <summary>Hashes source schema identities without retaining or exporting SQL/function bodies.</summary>
internal static class SecurityArchitectureRlsSchemaIdentity
{
    internal sealed record Guard(string TriggerName, string Enabled, int CommandMask, string TriggerDefinitionDigest,
        string FunctionSchema, string FunctionName, string IdentityArguments, string FunctionDefinitionDigest);
    internal sealed record Constraint(string ConstraintName, string Kind, string ConstraintTable, string? ReferencedTable,
        string Relationship, bool Validated, bool Deferrable, bool Deferred, string DefinitionDigest);
    internal sealed record Snapshot(string Table, string SchemaDigest, IReadOnlyList<Guard> Guards, IReadOnlyList<Constraint> Constraints);

    public static async Task<Snapshot> CaptureAsync(string database, string table)
    {
        var guards = await PostgreSqlMigrationTestDatabase.QueryAsync(database, """
            SELECT t.tgname,t.tgenabled::text,t.tgtype::int,pg_get_triggerdef(t.oid),fn.nspname,p.proname,
                pg_get_function_identity_arguments(p.oid),pg_get_functiondef(p.oid)
            FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace
            JOIN pg_proc p ON p.oid=t.tgfoid JOIN pg_namespace fn ON fn.oid=p.pronamespace
            WHERE n.nspname='public' AND c.relname=@table AND NOT t.tgisinternal ORDER BY t.tgname
            """, reader => new Guard(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), Digest(reader.GetString(3)),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), Digest(reader.GetString(7))), ("table", table));
        var constraints = await PostgreSqlMigrationTestDatabase.QueryAsync(database, """
            SELECT k.conname,k.contype::text,c.relname,referenced.relname,
                CASE WHEN c.relname=@table THEN 'OWNED' ELSE 'REFERENCING' END,
                k.convalidated,k.condeferrable,k.condeferred,pg_get_constraintdef(k.oid)
            FROM pg_constraint k JOIN pg_class c ON c.oid=k.conrelid JOIN pg_namespace n ON n.oid=c.relnamespace
            LEFT JOIN pg_class referenced ON referenced.oid=k.confrelid
            LEFT JOIN pg_namespace referenced_schema ON referenced_schema.oid=referenced.relnamespace
            WHERE n.nspname='public' AND (c.relname=@table OR
                (k.contype='f' AND referenced_schema.nspname='public' AND referenced.relname=@table))
            ORDER BY c.relname,k.conname
            """, reader => new Constraint(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.GetBoolean(5),
                reader.GetBoolean(6), reader.GetBoolean(7), Digest(reader.GetString(8))), ("table", table));
        return new(table, Digest(JsonSerializer.Serialize(new { guards, constraints },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })), guards, constraints);
    }

    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
