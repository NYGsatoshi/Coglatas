using System.Data;
using System.Globalization;
using Coglatas.Application.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>
/// Persists the tenant/user Message notification preference beside the active
/// tenant membership. The column is intentionally a narrow schema sidecar so
/// this UI-scoping fix does not widen the TenantUser domain contract.
/// </summary>
public sealed class MessageNotificationPreferenceStore(AppDbContext dbContext) : IMessageNotificationPreferenceStore
{
    public async Task<bool?> GetEnabledAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT "MessageNotificationsEnabled"
                FROM tenant_users
                WHERE "TenantId" = @tenantId
                  AND "UserId" = @userId
                  AND "Status" = 'Active'
                LIMIT 1
                """;
            AddParameter(command, "tenantId", tenantId);
            AddParameter(command, "userId", userId);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value is null or DBNull
                ? null
                : Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<bool> SetEnabledAsync(
        Guid tenantId,
        Guid userId,
        bool enabled,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE tenant_users
                SET "MessageNotificationsEnabled" = @enabled,
                    "UpdatedAt" = @updatedAt
                WHERE "TenantId" = @tenantId
                  AND "UserId" = @userId
                  AND "Status" = 'Active'
                """;
            AddParameter(command, "enabled", enabled);
            AddParameter(command, "updatedAt", updatedAt);
            AddParameter(command, "tenantId", tenantId);
            AddParameter(command, "userId", userId);
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
