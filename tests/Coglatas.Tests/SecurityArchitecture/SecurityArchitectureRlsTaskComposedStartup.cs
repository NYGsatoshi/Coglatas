using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Web.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

[assembly: HostingStartup(typeof(Coglatas.Tests.SecurityArchitecture.SecurityArchitectureRlsTaskComposedStartup))]

namespace Coglatas.Tests.SecurityArchitecture;

/// <summary>Test-only post-auth scope capture; each real adapter retains its transaction ownership.</summary>
public sealed class SecurityArchitectureRlsTaskComposedStartup : IHostingStartup
{
    public void Configure(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureServices((context, services) =>
        {
            if (!context.Configuration.GetValue<bool>("COGLATAS_SEC_ARCH_RLS_TASK_COMPOSED_PROBE")) return;
            if (!context.HostingEnvironment.IsEnvironment("Test"))
                throw new InvalidOperationException("The test-owned Task composed RLS probe requires the Test environment.");
            services.AddScoped<ComposedTaskRlsProbe>();
            services.AddScoped<ComposedTaskRlsActionFilter>();
            services.AddScoped<ComposedTaskRlsTransactionInterceptor>();
            services.AddDbContext<AppDbContext>((provider, options) =>
                options.AddInterceptors(provider.GetRequiredService<ComposedTaskRlsTransactionInterceptor>()));
            services.Configure<MvcOptions>(options => options.Filters.AddService<ComposedTaskRlsActionFilter>());
        });
    }
}

internal sealed class ComposedTaskRlsProbe
{
    public Guid? TenantId { get; set; }
    public int BoundTransactionCount { get; set; }
}

internal sealed class ComposedTaskRlsTransactionInterceptor(ComposedTaskRlsProbe probe) : DbTransactionInterceptor
{
    public override async ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
        TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        if (probe.TenantId is null) return result;
        await using var command = connection.CreateCommand();
        command.Transaction = result;
        command.CommandText = "SELECT set_config('coglatas.tenant_id',@tenant,true)";
        var tenant = command.CreateParameter();
        tenant.ParameterName = "tenant";
        tenant.Value = probe.TenantId.Value.ToString();
        command.Parameters.Add(tenant);
        await command.ExecuteScalarAsync(cancellationToken);
        probe.BoundTransactionCount++;
        return result;
    }
}

internal sealed class ComposedTaskRlsActionFilter(AppDbContext database, ICurrentTenant tenant,
    ComposedTaskRlsProbe probe, IConfiguration configuration) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var selected = context.ActionDescriptor is ControllerActionDescriptor action &&
            action.ControllerTypeInfo.AsType() == typeof(TaskExecutionController) &&
            action.MethodInfo.Name == nameof(TaskExecutionController.RequestRun);
        if (!selected || !Guid.TryParseExact(http.Request.Headers["X-Sec-Arch-Composed-Capture"], "N", out var capture) ||
            http.User.Identity?.IsAuthenticated != true || !tenant.IsAvailable || tenant.IsPlatformScope || tenant.TenantId == Guid.Empty ||
            !Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var subject) ||
            !Guid.TryParse(http.User.FindFirstValue("session_id"), out var session))
        {
            await next();
            return;
        }
        if (database.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Actual Task adapters must retain their own transaction boundaries.");
        probe.TenantId = tenant.TenantId;
        string? databaseRole = null;
        int? backendPid = null;
        var outsideTransactionContextEmpty = false;
        Exception? operationException = null;
        try
        {
            await database.Database.OpenConnectionAsync(http.RequestAborted);
            await using (var identity = database.Database.GetDbConnection().CreateCommand())
            {
                identity.CommandText = "SELECT current_user,pg_backend_pid(),current_setting('coglatas.tenant_id',true)";
                await using var reader = await identity.ExecuteReaderAsync(http.RequestAborted);
                if (!await reader.ReadAsync(http.RequestAborted)) throw new InvalidOperationException("Missing actual Task operation connection identity.");
                databaseRole = reader.GetString(0);
                backendPid = reader.GetInt32(1);
                if (!reader.IsDBNull(2) && reader.GetString(2).Length != 0)
                    throw new InvalidOperationException("The Task action must begin without persistent connection scope.");
            }
            var executed = await next();
            operationException = executed.Exception;
            if (database.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("An actual Task adapter retained an ambient transaction after invocation.");
            await using var finalIdentity = database.Database.GetDbConnection().CreateCommand();
            finalIdentity.CommandText = "SELECT current_user,pg_backend_pid(),current_setting('coglatas.tenant_id',true)";
            await using var finalReader = await finalIdentity.ExecuteReaderAsync(CancellationToken.None);
            if (!await finalReader.ReadAsync(CancellationToken.None) || finalReader.GetString(0) != databaseRole || finalReader.GetInt32(1) != backendPid)
                throw new InvalidOperationException("The Task invocation changed its actual connection authority.");
            outsideTransactionContextEmpty = finalReader.IsDBNull(2) || finalReader.GetString(2).Length == 0;
            if (!outsideTransactionContextEmpty) throw new InvalidOperationException("Task adapter transaction context leaked after invocation.");
        }
        catch (Exception exception)
        {
            operationException ??= exception;
            throw;
        }
        finally
        {
            try
            {
                var directory = configuration["COGLATAS_SEC_ARCH_RLS_COMPOSED_DIRECTORY"]
                    ?? throw new InvalidOperationException("The test-owned Task capture requires an isolated local directory.");
                Directory.CreateDirectory(directory);
                await using var output = new FileStream(Path.Combine(directory, capture.ToString("N") + ".json"), FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 4096, true);
                var native = FindPostgresException(operationException);
                var permission = native is null ? Match.Empty : Regex.Match(native.MessageText,
                    "^permission denied for table ([a-z_][a-z0-9_]{0,62})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                object observation = new
                {
                    schemaVersion = 1, approval = "DRAFT", ownerApproval = (string?)null,
                    executionScope = "ACTUAL_WEB_ENTRY_POINT_TASK_REQUEST_WITH_TEST_OWNED_POST_AUTH_CONTEXT",
                    tenantId = probe.TenantId, subjectId = subject, sessionId = session, databaseRole, backendPid,
                    boundTransactionCount = probe.BoundTransactionCount, outsideTransactionContextEmpty,
                    exceptionType = operationException?.GetType().Name, nativeSqlState = native?.SqlState,
                    nativeRoutine = native?.Routine, nativeTable = native?.TableName, nativeConstraint = native?.ConstraintName,
                    permissionRejectedTable = permission.Success ? permission.Groups[1].Value : null,
                    ambientTransactionApplied = false, operationalRoleEquivalence = "UNVERIFIED",
                    preAvaloniaVerdict = "PRE-AVALONIA SEC-ARCH: BLOCKED"
                };
                await JsonSerializer.SerializeAsync(output, observation,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }
            finally
            {
                probe.TenantId = null;
                await database.Database.CloseConnectionAsync();
            }
        }
    }

    private static PostgresException? FindPostgresException(Exception? exception)
    {
        for (var depth = 0; exception is not null && depth < 10; depth++, exception = exception.InnerException)
            if (exception is PostgresException native) return native;
        return null;
    }
}
