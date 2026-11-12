using System.Data;
using System.Text.Json;
using Coglatas.Application.Projects;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Coglatas.Tests.PostgreSql;

[Collection("PostgreSqlTaskV1")]
public sealed class TaskExecutionSourcePolicyTransactionPostgreSqlTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task MutablePolicyOverlaysAndFlushesRespectCallerCommitAndRollback() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async connectionString =>
            {
                foreach (var ownerType in new[] { TaskExecutionSourcePolicyOwnerType.Project, TaskExecutionSourcePolicyOwnerType.Task })
                {
                    var initial = Document(ownerType);
                    await using (var seed = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connectionString))
                    {
                        var repository = new TaskExecutionScopeRepository(seed);
                        repository.StageSourcePolicyDocument(initial);
                        Assert.True(repository.HasPendingSourcePolicyDocuments);
                        AssertDocument(initial, await repository.GetSourcePolicyDocumentAsync(ownerType, initial.OwnerId));
                        Assert.Null(await ReadAsync(connectionString, ownerType, initial.OwnerId));
                        await repository.FlushPendingSourcePolicyDocumentsAsync();
                        Assert.False(repository.HasPendingSourcePolicyDocuments);
                    }
                    AssertDocument(initial, await ReadAsync(connectionString, ownerType, initial.OwnerId));

                    var replacement = initial with
                    {
                        ProjectScopeVersion = initial.ProjectScopeVersion + 1,
                        TaskOverrideVersion = initial.TaskOverrideVersion + 1,
                        Policy = TaskExecutionSourcePolicyV2.FromLegacy(false, true)
                    };
                    foreach (var commit in new[] { false, true })
                    {
                        await using var writer = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connectionString);
                        await using var transaction = await writer.Database.BeginTransactionAsync();
                        var repository = new TaskExecutionScopeRepository(writer);
                        repository.StageSourcePolicyDocumentDelete(ownerType, initial.OwnerId);
                        Assert.Null(await repository.GetSourcePolicyDocumentAsync(ownerType, initial.OwnerId));
                        repository.StageSourcePolicyDocument(replacement);
                        Assert.True(repository.HasPendingSourcePolicyDocuments);
                        AssertDocument(replacement, await repository.GetSourcePolicyDocumentAsync(ownerType, initial.OwnerId));
                        AssertDocument(initial, await ReadAsync(connectionString, ownerType, initial.OwnerId));

                        await repository.FlushPendingSourcePolicyDocumentsAsync();
                        Assert.False(repository.HasPendingSourcePolicyDocuments);
                        Assert.Same(transaction, writer.Database.CurrentTransaction);
                        Assert.Equal(ConnectionState.Open, writer.Database.GetDbConnection().State);
                        AssertDocument(replacement, await repository.GetSourcePolicyDocumentAsync(ownerType, initial.OwnerId));
                        AssertDocument(initial, await ReadAsync(connectionString, ownerType, initial.OwnerId));

                        if (commit) await transaction.CommitAsync();
                        else await transaction.RollbackAsync();
                        AssertDocument(commit ? replacement : initial, await ReadAsync(connectionString, ownerType, initial.OwnerId));
                    }

                    foreach (var commit in new[] { false, true })
                    {
                        await using var writer = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connectionString);
                        await using var transaction = await writer.Database.BeginTransactionAsync();
                        var repository = new TaskExecutionScopeRepository(writer);
                        repository.StageSourcePolicyDocument(replacement);
                        repository.StageSourcePolicyDocumentDelete(ownerType, initial.OwnerId);
                        Assert.True(repository.HasPendingSourcePolicyDocuments);
                        Assert.Null(await repository.GetSourcePolicyDocumentAsync(ownerType, initial.OwnerId));
                        await repository.FlushPendingSourcePolicyDocumentsAsync();
                        Assert.False(repository.HasPendingSourcePolicyDocuments);
                        Assert.Same(transaction, writer.Database.CurrentTransaction);
                        Assert.Equal(ConnectionState.Open, writer.Database.GetDbConnection().State);
                        Assert.Null(await repository.GetSourcePolicyDocumentAsync(ownerType, initial.OwnerId));
                        AssertDocument(replacement, await ReadAsync(connectionString, ownerType, initial.OwnerId));

                        if (commit) await transaction.CommitAsync();
                        else await transaction.RollbackAsync();
                        var stored = await ReadAsync(connectionString, ownerType, initial.OwnerId);
                        if (commit) Assert.Null(stored);
                        else AssertDocument(replacement, stored);
                    }
                }
            });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    public Task ImmutableRunSnapshotRejectsDeletionAndReplacementWithoutChangingStoredPolicy() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async connectionString =>
            {
                var original = Document(TaskExecutionSourcePolicyOwnerType.Run);
                await using (var seed = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connectionString))
                {
                    var repository = new TaskExecutionScopeRepository(seed);
                    repository.StageSourcePolicyDocument(original);
                    await repository.FlushPendingSourcePolicyDocumentsAsync();
                    Assert.False(repository.HasPendingSourcePolicyDocuments);
                }
                var originalJson = await PolicyJsonAsync(connectionString, original.OwnerId);
                AssertDocument(original, await ReadAsync(connectionString, original.OwnerType, original.OwnerId));

                await using (var writer = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connectionString))
                {
                    var repository = new TaskExecutionScopeRepository(writer);
                    var denial = Assert.Throws<InvalidOperationException>(() =>
                        repository.StageSourcePolicyDocumentDelete(original.OwnerType, original.OwnerId));
                    Assert.Equal("Run source-policy snapshots are immutable.", denial.Message);
                    Assert.False(repository.HasPendingSourcePolicyDocuments);
                    Assert.Equal(originalJson, await PolicyJsonAsync(connectionString, original.OwnerId));

                    await using var transaction = await writer.Database.BeginTransactionAsync();
                    repository.StageSourcePolicyDocument(original with { Policy = TaskExecutionSourcePolicyV2.FromLegacy(false, true) });
                    Assert.True(repository.HasPendingSourcePolicyDocuments);
                    var conflict = await Assert.ThrowsAsync<PostgresException>(() => repository.FlushPendingSourcePolicyDocumentsAsync());
                    Assert.Equal(PostgresErrorCodes.UniqueViolation, conflict.SqlState);
                    Assert.Same(transaction, writer.Database.CurrentTransaction);
                    Assert.True(repository.HasPendingSourcePolicyDocuments);
                    repository.ClearPendingSourcePolicyDocuments();
                    Assert.False(repository.HasPendingSourcePolicyDocuments);
                    await transaction.RollbackAsync();
                }
                AssertDocument(original, await ReadAsync(connectionString, original.OwnerType, original.OwnerId));
                Assert.Equal(originalJson, await PolicyJsonAsync(connectionString, original.OwnerId));
            });

    private static TaskExecutionSourcePolicyDocument Document(TaskExecutionSourcePolicyOwnerType ownerType)
    {
        var projectId = Guid.NewGuid();
        var taskId = ownerType == TaskExecutionSourcePolicyOwnerType.Project ? (Guid?)null : Guid.NewGuid();
        var ownerId = ownerType == TaskExecutionSourcePolicyOwnerType.Project ? projectId :
            ownerType == TaskExecutionSourcePolicyOwnerType.Task ? taskId!.Value : Guid.NewGuid();
        return new TaskExecutionSourcePolicyDocument(ownerType, ownerId, Guid.NewGuid(), Guid.NewGuid(),
            projectId, taskId, 9_000_000_000L, taskId.HasValue ? 9_000_000_003L : null,
            TaskExecutionSourcePolicyV2.FromLegacy(true, false));
    }

    private static async Task<TaskExecutionSourcePolicyDocument?> ReadAsync(
        string connectionString, TaskExecutionSourcePolicyOwnerType ownerType, Guid ownerId)
    {
        await using var reader = PostgreSqlMigrationTestDatabase.CreatePlatformContext(connectionString);
        Assert.Equal(ConnectionState.Closed, reader.Database.GetDbConnection().State);
        var document = await new TaskExecutionScopeRepository(reader).GetSourcePolicyDocumentAsync(ownerType, ownerId);
        Assert.Equal(ConnectionState.Closed, reader.Database.GetDbConnection().State);
        return document;
    }

    private static Task<string> PolicyJsonAsync(string connectionString, Guid ownerId) =>
        PostgreSqlMigrationTestDatabase.ScalarAsync<string>(connectionString,
            "SELECT \"PolicyJson\"::text FROM task_execution_source_policy_documents WHERE \"OwnerType\"='Run' AND \"OwnerId\"=@ownerId;",
            ("ownerId", ownerId));

    private static void AssertDocument(TaskExecutionSourcePolicyDocument expected, TaskExecutionSourcePolicyDocument? actual)
    {
        var stored = Assert.IsType<TaskExecutionSourcePolicyDocument>(actual);
        Assert.Equal(expected.OwnerType, stored.OwnerType);
        Assert.Equal(expected.OwnerId, stored.OwnerId);
        Assert.Equal(expected.TenantId, stored.TenantId);
        Assert.Equal(expected.WorkspaceId, stored.WorkspaceId);
        Assert.Equal(expected.ProjectId, stored.ProjectId);
        Assert.Equal(expected.TaskItemId, stored.TaskItemId);
        Assert.Equal(expected.ProjectScopeVersion, stored.ProjectScopeVersion);
        Assert.Equal(expected.TaskOverrideVersion, stored.TaskOverrideVersion);
        Assert.Equal(JsonSerializer.Serialize(expected.Policy), JsonSerializer.Serialize(stored.Policy));
    }
}
