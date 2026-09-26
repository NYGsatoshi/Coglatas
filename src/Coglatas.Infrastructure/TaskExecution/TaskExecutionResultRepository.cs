using System.Data;
using System.Data.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Projects;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Coglatas.Infrastructure.TaskExecution;

public sealed class TaskExecutionResultRepository(
    AppDbContext dbContext,
    ICurrentTenant currentTenant) : ITaskExecutionResultRepository
{
    public Task<TaskExecutionPersistedResult?> GetByRunAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        if (!HasTenant() || runId == Guid.Empty)
        {
            return Task.FromResult<TaskExecutionPersistedResult?>(null);
        }

        return WithConnectionAsync(async (connection, transaction) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT
                    "Id", "TenantId", "WorkspaceId", "ProjectId", "TaskItemId",
                    "TaskExecutionRunId", "SchemaVersion", "Status", "Title",
                    "BodyMarkdown", "ContentSha256", "CompletedAtUtc", "CreatedAtUtc"
                FROM task_execution_results
                WHERE "TenantId" = @tenantId AND "TaskExecutionRunId" = @runId
                LIMIT 1;
                """;
            AddParameter(command, "tenantId", currentTenant.TenantId);
            AddParameter(command, "runId", runId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new TaskExecutionPersistedResult(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetGuid(4),
                reader.GetGuid(5),
                reader.GetInt32(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetFieldValue<DateTimeOffset>(11),
                reader.GetFieldValue<DateTimeOffset>(12));
        }, cancellationToken);
    }

    public Task<IReadOnlyList<TaskExecutionResultSourceReference>> ListSourceReferencesAsync(
        Guid resultId,
        CancellationToken cancellationToken = default)
    {
        if (!HasTenant() || resultId == Guid.Empty)
        {
            return Task.FromResult<IReadOnlyList<TaskExecutionResultSourceReference>>([]);
        }

        return WithConnectionAsync<IReadOnlyList<TaskExecutionResultSourceReference>>(async (connection, transaction) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT source."Id", source."FileObjectId", source."AttachmentId",
                       source."ContentSha256", source."MediaType", source."MaterializedByteCount"
                FROM task_execution_result_sources link
                JOIN task_execution_materialized_sources source
                  ON source."Id" = link."MaterializedSourceId"
                 AND source."TenantId" = link."TenantId"
                WHERE link."TenantId" = @tenantId
                  AND link."TaskExecutionResultId" = @resultId
                ORDER BY link."Ordinal", link."Id";
                """;
            AddParameter(command, "tenantId", currentTenant.TenantId);
            AddParameter(command, "resultId", resultId);

            var items = new List<TaskExecutionResultSourceReference>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new TaskExecutionResultSourceReference(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetGuid(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetInt64(5)));
            }

            return items;
        }, cancellationToken);
    }

    private bool HasTenant() =>
        currentTenant.IsAvailable &&
        !currentTenant.IsPlatformScope &&
        currentTenant.TenantId != Guid.Empty;

    private async Task<T> WithConnectionAsync<T>(
        Func<DbConnection, DbTransaction?, Task<T>> action,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            var transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            return await action(connection, transaction);
        }
        finally
        {
            if (shouldClose)
            {
                await dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    internal static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
