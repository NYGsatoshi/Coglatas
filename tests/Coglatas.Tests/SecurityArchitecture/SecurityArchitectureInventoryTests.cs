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
using Microsoft.AspNetCore.Http;
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
            WHERE n.nspname='public' AND c.relkind IN ('r','p','v','m')
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
        command.CommandText = """
            SELECT current_user, r.rolsuper, r.rolbypassrls
            FROM pg_roles r WHERE r.rolname=current_user
            """;
        await using var roleReader = await command.ExecuteReaderAsync();
        Assert.True(await roleReader.ReadAsync());
        var role = new { category = "MIGRATION_FIXTURE_ROLE", superuser = roleReader.GetBoolean(1),
            bypassRls = roleReader.GetBoolean(2), applicationRoleQualification = "UNVERIFIED" };
        await WritePrivateInventoryAsync("postgres-inventory.json", new
        {
            schemaVersion = 1, modelTableCount = modelTables.Length, objectCount = observedTables.Count,
            objects, role, completion = "UNVERIFIED",
            blindSpots = new[] { "Migration identity is not application-role authorization proof.",
                "No RLS policy or product activation approval is inferred from inventory." }
        });
        });
    }

    private static async Task WritePrivateInventoryAsync(string filename, object value)
    {
        var directory = Environment.GetEnvironmentVariable("COGLATAS_SEC_ARCH_PRIVATE_INVENTORY_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, filename),
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }
}
