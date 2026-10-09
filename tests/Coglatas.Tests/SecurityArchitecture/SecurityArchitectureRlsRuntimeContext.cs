using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Claims;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coglatas.Tests.SecurityArchitecture;

/// <summary>Test-owned context seam. It is not registered by the product composition root.</summary>
internal static class SecurityArchitectureRlsRuntimeContext
{
    internal sealed record VerifiedScope(Guid TenantId, string AuthorityKind, Guid? SubjectId, Guid? SessionId)
    {
        public static VerifiedScope FromValidatedCookie(ClaimsPrincipal principal, ICurrentTenant tenant)
        {
            if (principal.Identity?.IsAuthenticated != true || !tenant.IsAvailable || tenant.IsPlatformScope ||
                !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var subject) ||
                !Guid.TryParse(principal.FindFirstValue("session_id"), out var session))
                throw new InvalidOperationException("A validated cookie and active tenant membership are required.");
            return new(tenant.TenantId, "syntheticAuthenticatedApplication", subject, session);
        }
    }

    internal sealed record TransactionObservation(string AuthorityKind, Guid TransactionId, int BackendProcessId);
    internal sealed class Recorder
    {
        public ConcurrentQueue<TransactionObservation> Transactions { get; } = new();
    }

    public static AppDbContext Create(string connection, VerifiedScope scope, Recorder recorder)
    {
        // Freeze the tenant separately from the request's mutable resolution service.
        var tenant = new CurrentTenantService();
        tenant.SetTenant(scope.TenantId, "synthetic-verified-scope");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection)
            .AddInterceptors(new TransactionContext(scope, recorder)).Options;
        return new AppDbContext(options, tenant);
    }

    private sealed class TransactionContext(VerifiedScope scope, Recorder recorder) : DbTransactionInterceptor
    {
        public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result) =>
            throw new InvalidOperationException("The isolated context fixture requires asynchronous transactions.");

        public override async ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
            TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        {
            if (scope.TenantId == Guid.Empty) throw new InvalidOperationException("An explicit bounded tenant authority is required.");
            await using var command = connection.CreateCommand();
            command.Transaction = result;
            command.CommandText = "SELECT set_config('coglatas.tenant_id',@tenant,true)";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "tenant";
            parameter.Value = scope.TenantId.ToString("D");
            command.Parameters.Add(parameter);
            await command.ExecuteScalarAsync(cancellationToken);
            recorder.Transactions.Enqueue(new(scope.AuthorityKind, eventData.TransactionId,
                ((Npgsql.NpgsqlConnection)connection).ProcessID));
            return result;
        }
    }
}
