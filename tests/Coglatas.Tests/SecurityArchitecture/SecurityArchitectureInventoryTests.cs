using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Domain.Common;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Tests.PostgreSql;
using Coglatas.Web.Controllers;
using Coglatas.Web.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed class SecurityArchitectureInventoryTests
{
    [Fact]
    public async Task ControllerRuntimeAndModelInventoryReportUnclassifiedBoundaries()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Test" });
        builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
        builder.Services.AddAuthorization();
        await using var app = builder.Build();
        app.MapControllers();
        // Enumerating the built route data source materializes the actual MVC route metadata.
        var sources = ((IEndpointRouteBuilder)app).DataSources;
        var endpoints = sources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["UNKNOWN"])
                .Select(method => new
                {
                    route = e.RoutePattern.RawText,
                    method,
                    controller = e.Metadata.GetMetadata<ControllerActionDescriptor>()!.ControllerTypeInfo.FullName,
                    action = e.Metadata.GetMetadata<ControllerActionDescriptor>()!.MethodInfo.Name,
                    allowAnonymous = e.Metadata.GetMetadata<IAllowAnonymous>() is not null,
                    authorization = e.Metadata.GetOrderedMetadata<IAuthorizeData>()
                        .Select(a => new { a.Policy, a.Roles, a.AuthenticationSchemes }).ToArray(),
                    classification = "UNVERIFIED"
                })).OrderBy(e => e.route, StringComparer.Ordinal).ThenBy(e => e.method, StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(endpoints);
        Assert.Contains(endpoints, e => e.route == "api/auth/login" && e.method == "POST");

        var tenant = new CurrentTenantService();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=synthetic_inventory;Username=unused;Password=unused").Options, tenant);
        var tables = db.Model.GetEntityTypes().GroupBy(e => (Schema: e.GetSchema() ?? "public", Table: e.GetTableName()))
            .Where(g => g.Key.Table is not null)
            .Select(g => new
            {
                schema = g.Key.Schema,
                table = g.Key.Table,
                entityTypes = g.Select(e => e.ClrType.FullName).Order(StringComparer.Ordinal).ToArray(),
                tenantEntity = g.Any(e => typeof(ITenantEntity).IsAssignableFrom(e.ClrType)),
                tenantColumn = g.Any(e => e.FindProperty("TenantId") is not null),
                classification = g.Any(e => e.FindProperty("TenantId") is not null)
                    ? "RLS_REQUIRED" : "REQUIRES_OWNER_REVIEW",
                rationale = g.Any(e => e.FindProperty("TenantId") is not null)
                    ? "Tenant-bearing model table; policy semantics and operations still require review."
                    : "No direct tenant property; inspect global, identity, control-plane and parent-derived scope.",
                approval = "DRAFT"
            }).OrderBy(t => t.schema, StringComparer.Ordinal).ThenBy(t => t.table, StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(tables);
        Assert.Contains(tables, t => t.table == "outbox_events" && t.classification == "RLS_REQUIRED");
        Assert.Contains(tables, t => t.table == "file_selection_snapshot_items" && t.classification == "REQUIRES_OWNER_REVIEW");

        var hub = new
        {
            type = typeof(AppHub).FullName,
            authorization = typeof(AppHub).GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).ToArray(),
            methods = typeof(AppHub).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && !m.Name.StartsWith("On", StringComparison.Ordinal))
                .Select(m => new { m.Name, parameters = m.GetParameters().Select(p => p.ParameterType.Name).ToArray() })
                .OrderBy(m => m.Name, StringComparer.Ordinal).ToArray(),
            classification = "UNVERIFIED"
        };
        Assert.Equal(8, hub.methods.Length);
        await WritePrivateInventoryAsync("source-inventory.json", new
        {
            schemaVersion = 1,
            approval = "DRAFT",
            runtimeCatalogScope = "MVC_CONTROLLER_METADATA_ONLY",
            blindSpots = new[] { "Production minimal endpoints/default/fallback configuration and actual host startup are not extracted.",
                "Metadata is not authorization execution.", "SPEC registration/mapping owner approval is pending.",
                "Non-TenantId tables are unresolved, never excluded.", "Hub reflection is not real transport evidence." },
            endpoints, tables, hub
        });
    }

    [PostgreSqlFact]
    public async Task ActualPostgreSqlInventoryMatchesAllModelTablesAndPreservesUnknownRoleState()
    {
        await PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async connectionString =>
        {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString).Options, new CurrentTenantService());
        var modelTables = db.Model.GetEntityTypes().Select(e => e.GetTableName()).OfType<string>()
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.relname, c.relkind::text, c.relrowsecurity, c.relforcerowsecurity,
                   (SELECT count(*) FROM pg_policy p WHERE p.polrelid=c.oid),
                   EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid=c.oid AND a.attname='TenantId' AND NOT a.attisdropped)
            FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname='public' AND c.relkind IN ('r','p','v','m','f')
            ORDER BY c.relname
            """;
        var objects = new List<object>();
        var observedTables = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
            {
                var table = reader.GetString(0);
                observedTables.Add(table);
                objects.Add(new { table, kind = reader.GetString(1), rlsEnabled = reader.GetBoolean(2),
                    rlsForced = reader.GetBoolean(3), policyCount = reader.GetInt64(4),
                    tenantColumn = reader.GetBoolean(5), outcome = "UNVERIFIED" });
            }
        Assert.All(modelTables, table => Assert.Contains(table, observedTables));
        var securityCatalog = await ReadSecurityCatalogAsync(connection);
        Assert.Equal(observedTables.Count, securityCatalog["ownership"].Count);
        command.CommandText = """
            SELECT current_user, r.rolsuper, r.rolbypassrls
            FROM pg_roles r WHERE r.rolname=current_user
            """;
        await using var roleReader = await command.ExecuteReaderAsync();
        Assert.True(await roleReader.ReadAsync());
        Assert.Contains(securityCatalog["roles"], row => Equals(row["role"], roleReader.GetString(0)));
        var role = new { category = "MIGRATION_FIXTURE_ROLE", superuser = roleReader.GetBoolean(1),
            bypassRls = roleReader.GetBoolean(2), applicationRoleQualification = "UNVERIFIED" };
        await WritePrivateInventoryAsync("postgres-inventory.json", new
        {
            schemaVersion = 1, modelTableCount = modelTables.Length, objectCount = observedTables.Count,
            objects, role, securityCatalog, completion = "UNVERIFIED",
            blindSpots = new[] { "Migration identity is not application-role authorization proof.",
                "No RLS policy or product activation approval is inferred from inventory.",
                "Catalogue roles/grants describe only the isolated migration fixture, not operational identities.",
                "Global, identity, parent-derived and non-UUID TenantId policy semantics remain Draft.",
                "Objects outside public and external access edges need independent qualification." }
        });
        });
    }

    private static async Task<Dictionary<string, List<Dictionary<string, object?>>>> ReadSecurityCatalogAsync(DbConnection connection)
    {
        var queries = new Dictionary<string, string>
        {
            ["ownership"] = """
                SELECT c.relname AS object, c.relkind::text AS kind, pg_get_userbyid(c.relowner) AS owner,
                       c.relrowsecurity AS rls_enabled, c.relforcerowsecurity AS rls_forced
                FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='public' AND c.relkind IN ('r','p','v','m','f') ORDER BY c.relname
                """,
            ["roles"] = """
                SELECT rolname AS role, rolsuper AS superuser, rolbypassrls AS bypass_rls,
                       rolcanlogin AS login, rolcreaterole AS create_role, rolcreatedb AS create_database,
                       rolinherit AS inherit, rolreplication AS replication
                FROM pg_roles ORDER BY rolname
                """,
            ["memberships"] = """
                SELECT pg_get_userbyid(roleid) AS role, pg_get_userbyid(member) AS member,
                       pg_get_userbyid(grantor) AS grantor, admin_option, inherit_option, set_option
                FROM pg_auth_members ORDER BY roleid, member, grantor
                """,
            ["table_grants"] = """
                SELECT c.relname AS object, CASE WHEN a.grantee=0 THEN 'PUBLIC' ELSE pg_get_userbyid(a.grantee) END AS grantee,
                       pg_get_userbyid(a.grantor) AS grantor, a.privilege_type, a.is_grantable
                FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                CROSS JOIN LATERAL aclexplode(COALESCE(c.relacl, acldefault('r',c.relowner))) a
                WHERE n.nspname='public' AND c.relkind IN ('r','p','v','m','f')
                ORDER BY c.relname, grantee, a.privilege_type
                """,
            ["schema_grants"] = """
                SELECT n.nspname AS schema, pg_get_userbyid(n.nspowner) AS owner,
                       CASE WHEN a.grantee=0 THEN 'PUBLIC' ELSE pg_get_userbyid(a.grantee) END AS grantee,
                       a.privilege_type, a.is_grantable
                FROM pg_namespace n CROSS JOIN LATERAL aclexplode(COALESCE(n.nspacl, acldefault('n',n.nspowner))) a
                WHERE n.nspname='public' ORDER BY grantee, a.privilege_type
                """,
            ["column_grants"] = """
                SELECT c.relname AS object, col.attname AS column,
                       CASE WHEN a.grantee=0 THEN 'PUBLIC' ELSE pg_get_userbyid(a.grantee) END AS grantee,
                       a.privilege_type, a.is_grantable
                FROM pg_attribute col JOIN pg_class c ON c.oid=col.attrelid JOIN pg_namespace n ON n.oid=c.relnamespace
                CROSS JOIN LATERAL aclexplode(col.attacl) a
                WHERE n.nspname='public' AND NOT col.attisdropped
                ORDER BY c.relname, col.attname, grantee, a.privilege_type
                """,
            ["default_grants"] = """
                SELECT pg_get_userbyid(d.defaclrole) AS owner, COALESCE(n.nspname,'ALL_SCHEMAS') AS schema,
                       d.defaclobjtype::text AS object_type,
                       CASE WHEN a.grantee=0 THEN 'PUBLIC' ELSE pg_get_userbyid(a.grantee) END AS grantee,
                       a.privilege_type, a.is_grantable
                FROM pg_default_acl d LEFT JOIN pg_namespace n ON n.oid=d.defaclnamespace
                CROSS JOIN LATERAL aclexplode(d.defaclacl) a
                ORDER BY owner, schema, object_type, grantee, a.privilege_type
                """,
            ["policies"] = """
                SELECT c.relname AS object, p.polname AS policy, p.polcmd::text AS operation, p.polpermissive AS permissive,
                       array_to_json(ARRAY(SELECT CASE WHEN role=0 THEN 'PUBLIC' ELSE pg_get_userbyid(role) END
                           FROM unnest(p.polroles) role ORDER BY role))::text AS roles,
                       pg_get_expr(p.polqual,p.polrelid) AS using_expression,
                       pg_get_expr(p.polwithcheck,p.polrelid) AS with_check_expression
                FROM pg_policy p JOIN pg_class c ON c.oid=p.polrelid JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='public' ORDER BY c.relname, p.polname
                """,
            ["views"] = """
                SELECT c.relname AS object, c.relkind::text AS kind,
                       COALESCE('security_invoker=true'=ANY(c.reloptions),false) AS security_invoker,
                       COALESCE('security_barrier=true'=ANY(c.reloptions),false) AS security_barrier
                FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='public' AND c.relkind IN ('v','m') ORDER BY c.relname
                """,
            ["partitions"] = """
                SELECT parent.relname AS parent, child.relname AS child,
                       pg_get_expr(child.relpartbound,child.oid) AS bound
                FROM pg_inherits i JOIN pg_class parent ON parent.oid=i.inhparent
                JOIN pg_class child ON child.oid=i.inhrelid JOIN pg_namespace n ON n.oid=child.relnamespace
                WHERE n.nspname='public' ORDER BY parent.relname, child.relname
                """,
            ["functions"] = """
                SELECT p.proname AS function, pg_get_function_identity_arguments(p.oid) AS arguments,
                       pg_get_userbyid(p.proowner) AS owner, p.prosecdef AS security_definer,
                       (SELECT setting FROM unnest(p.proconfig) setting WHERE setting LIKE 'search_path=%' LIMIT 1) AS search_path,
                       CASE WHEN a.grantee=0 THEN 'PUBLIC' ELSE pg_get_userbyid(a.grantee) END AS grantee,
                       a.privilege_type, a.is_grantable
                FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
                CROSS JOIN LATERAL aclexplode(COALESCE(p.proacl, acldefault('f',p.proowner))) a
                WHERE n.nspname='public' ORDER BY p.proname, arguments, grantee
                """,
            ["tenant_columns"] = """
                SELECT c.relname AS object, format_type(a.atttypid,a.atttypmod) AS data_type, a.attnotnull AS required
                FROM pg_attribute a JOIN pg_class c ON c.oid=a.attrelid JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='public' AND a.attname='TenantId' AND NOT a.attisdropped ORDER BY c.relname
                """,
            ["foreign_keys"] = """
                SELECT c.relname AS object, con.conname AS constraint, pg_get_constraintdef(con.oid) AS definition
                FROM pg_constraint con JOIN pg_class c ON c.oid=con.conrelid JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='public' AND con.contype='f' ORDER BY c.relname, con.conname
                """,
            ["indexes"] = """
                SELECT tablename AS object, indexname AS index, indexdef AS definition
                FROM pg_indexes WHERE schemaname='public' ORDER BY tablename,indexname
                """
        };
        var catalog = new Dictionary<string, List<Dictionary<string, object?>>>();
        foreach (var (name, sql) in queries)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (var column = 0; column < reader.FieldCount; column++)
                    row.Add(reader.GetName(column), reader.IsDBNull(column) ? null : reader.GetValue(column));
                rows.Add(row);
            }
            catalog.Add(name, rows);
        }
        return catalog;
    }

    private static async Task WritePrivateInventoryAsync(string filename, object value)
    {
        var directory = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_PRIVATE_INVENTORY_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        await using var output = new FileStream(Path.Combine(directory, filename), FileMode.CreateNew,
            FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(output, value, new JsonSerializerOptions { WriteIndented = true });
    }
}
