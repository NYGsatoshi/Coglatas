using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Web.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

[assembly: HostingStartup(typeof(Coglatas.Tests.SecurityArchitecture.SecurityArchitectureRlsComposedHostStartup))]

namespace Coglatas.Tests.SecurityArchitecture;

/// <summary>Explicitly loaded test-assembly experiment; no product startup or authentication service is replaced.</summary>
public sealed class SecurityArchitectureRlsComposedHostStartup : IHostingStartup
{
    public void Configure(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureServices((context, services) =>
        {
            if (!context.Configuration.GetValue<bool>("COGLATAS_SEC_ARCH_RLS_COMPOSED_PROBE")) return;
            if (!context.HostingEnvironment.IsEnvironment("Test"))
                throw new InvalidOperationException("The test-owned composed RLS probe requires the Test environment.");
            services.AddScoped<ComposedRlsProbe>();
            services.AddScoped<ComposedRlsActionFilter>();
            services.AddScoped<ComposedRlsTransactionInterceptor>();
            services.AddDbContext<AppDbContext>((provider, options) =>
                options.AddInterceptors(provider.GetRequiredService<ComposedRlsTransactionInterceptor>()));
            services.Configure<MvcOptions>(options => options.Filters.AddService<ComposedRlsActionFilter>());
        });
    }
}

internal sealed class ComposedRlsProbe
{
    public Guid? TenantId { get; set; }
    public Guid? SubjectId { get; set; }
    public Guid? SessionId { get; set; }
    public int BoundTransactionCount { get; set; }
}

internal sealed class ComposedRlsTransactionInterceptor(ComposedRlsProbe probe) : DbTransactionInterceptor
{
    public override async ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
        TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        // Authentication/bootstrap operations have no post-auth probe authority.
        // Their reads/writes and concrete role design remain independently unqualified.
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

internal sealed class ComposedRlsActionFilter(AppDbContext database, ICurrentTenant tenant,
    ComposedRlsProbe probe, IConfiguration configuration) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var selected = context.ActionDescriptor is ControllerActionDescriptor action &&
            (action.ControllerTypeInfo.AsType() == typeof(WorkspacesController) && action.MethodInfo.Name == nameof(WorkspacesController.Get) ||
             action.ControllerTypeInfo.AsType() == typeof(MessageNotificationPreferencesController));
        if (!selected || !Guid.TryParseExact(http.Request.Headers["X-Sec-Arch-Composed-Capture"], "N", out var capture))
        {
            await next();
            return;
        }
        if (http.User.Identity?.IsAuthenticated != true || !tenant.IsAvailable || tenant.IsPlatformScope ||
            !Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var subject) ||
            !Guid.TryParse(http.User.FindFirstValue("session_id"), out var session))
        {
            await next();
            return;
        }
        if (database.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("The selected composed-host probe requires explicit transaction ownership.");
        probe.TenantId = tenant.TenantId;
        probe.SubjectId = subject;
        probe.SessionId = session;
        var committed = false;
        var rolledBack = false;
        string? databaseRole = null;
        int? backendPid = null;
        await using var transaction = await database.Database.BeginTransactionAsync(http.RequestAborted);
        try
        {
            await using var identity = database.Database.GetDbConnection().CreateCommand();
            identity.CommandText = "SELECT current_user,pg_backend_pid(),current_setting('coglatas.tenant_id',true)";
            await using (var reader = await identity.ExecuteReaderAsync(http.RequestAborted))
            {
                if (!await reader.ReadAsync(http.RequestAborted)) throw new InvalidOperationException("Missing observed transaction identity.");
                databaseRole = reader.GetString(0);
                backendPid = reader.GetInt32(1);
                if (reader.GetString(2) != probe.TenantId.Value.ToString() || probe.BoundTransactionCount != 1)
                    throw new InvalidOperationException("The actual operation connection differs from its post-auth tenant context.");
            }
            var executed = await next();
            if (http.Request.Headers["X-Sec-Arch-Composed-Exception"] == "true")
                throw new InvalidOperationException("Deliberate test-owned exception after the actual selected action.");
            if (executed.Exception is not null && !executed.ExceptionHandled)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                rolledBack = true;
            }
            else
            {
                await transaction.CommitAsync(http.RequestAborted);
                committed = true;
            }
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            rolledBack = true;
            throw;
        }
        finally
        {
            var directory = configuration["COGLATAS_SEC_ARCH_RLS_COMPOSED_DIRECTORY"]
                ?? throw new InvalidOperationException("A selected composed-host capture requires an isolated local directory.");
            Directory.CreateDirectory(directory);
            await using var output = new FileStream(Path.Combine(directory, capture.ToString("N") + ".json"), FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await JsonSerializer.SerializeAsync(output, new
            {
                schemaVersion = 1, approval = "DRAFT", ownerApproval = (string?)null,
                executionScope = "ACTUAL_WEB_ENTRY_POINT_WITH_TEST_OWNED_SELECTED_ACTION_CONTEXT",
                probe.TenantId, probe.SubjectId, probe.SessionId, probe.BoundTransactionCount,
                databaseRole, backendPid, committed, rolledBack, operationalRoleEquivalence = "UNVERIFIED",
                preAvaloniaVerdict = "PRE-AVALONIA SEC-ARCH: BLOCKED"
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            probe.TenantId = null;
        }
    }
}
