using System.Security.Cryptography;
using System.Text;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

public sealed class EfCreateIdempotencyCoordinator(
    AppDbContext dbContext,
    ITaskExecutionScopeRepository? taskExecutionScopes = null) : ICreateIdempotencyCoordinator
{
    public async Task<IdempotentCreateResult<T>> ExecuteAsync<T>(
        CreateIdempotencyContext context,
        Func<CancellationToken, Task<T>> stageCreation,
        Func<Guid, CancellationToken, Task<T?>> loadCommittedResource,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(stageCreation);
        ArgumentNullException.ThrowIfNull(loadCommittedResource);
        Validate(context);

        var keyHash = Hash(context.ClientRequestIdentity);
        var existing = await FindAsync(context, keyHash, cancellationToken);
        if (existing is not null)
        {
            return await ReconcileAsync(existing, context, loadCommittedResource, cancellationToken);
        }

        if (!dbContext.Database.IsRelational())
        {
            return await ExecuteWithoutExplicitTransactionAsync(
                context,
                keyHash,
                stageCreation,
                loadCommittedResource,
                cancellationToken);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.IdempotencyRecords.Add(NewRecord(context, keyHash));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            taskExecutionScopes?.ClearPendingSourcePolicyDocuments();

            var winner = await FindAsync(context, keyHash, cancellationToken);
            if (winner is null)
            {
                throw new InvalidOperationException(
                    "The idempotency claim could not be persisted or reconciled.",
                    exception);
            }

            return await ReconcileAsync(winner, context, loadCommittedResource, cancellationToken);
        }

        try
        {
            var value = await stageCreation(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (taskExecutionScopes?.HasPendingSourcePolicyDocuments == true)
            {
                await taskExecutionScopes.FlushPendingSourcePolicyDocumentsAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return new IdempotentCreateResult<T>(IdempotentCreateDisposition.Created, value);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            taskExecutionScopes?.ClearPendingSourcePolicyDocuments();
            throw;
        }
    }

    private async Task<IdempotentCreateResult<T>> ExecuteWithoutExplicitTransactionAsync<T>(
        CreateIdempotencyContext context,
        string keyHash,
        Func<CancellationToken, Task<T>> stageCreation,
        Func<Guid, CancellationToken, Task<T?>> loadCommittedResource,
        CancellationToken cancellationToken)
        where T : class
    {
        var existing = await FindAsync(context, keyHash, cancellationToken);
        if (existing is not null)
        {
            return await ReconcileAsync(existing, context, loadCommittedResource, cancellationToken);
        }

        dbContext.IdempotencyRecords.Add(NewRecord(context, keyHash));
        try
        {
            var value = await stageCreation(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (taskExecutionScopes?.HasPendingSourcePolicyDocuments == true)
            {
                await taskExecutionScopes.FlushPendingSourcePolicyDocumentsAsync(cancellationToken);
            }
            return new IdempotentCreateResult<T>(IdempotentCreateDisposition.Created, value);
        }
        catch
        {
            dbContext.ChangeTracker.Clear();
            taskExecutionScopes?.ClearPendingSourcePolicyDocuments();
            throw;
        }
    }

    private async Task<IdempotentCreateResult<T>> ReconcileAsync<T>(
        IdempotencyRecord existing,
        CreateIdempotencyContext context,
        Func<Guid, CancellationToken, Task<T?>> loadCommittedResource,
        CancellationToken cancellationToken)
        where T : class
    {
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(existing.RequestHash),
                Encoding.ASCII.GetBytes(context.RequestHash)))
        {
            return new IdempotentCreateResult<T>(IdempotentCreateDisposition.RequestMismatch, null);
        }

        if (!string.Equals(existing.ResourceType, context.ResourceType, StringComparison.Ordinal))
        {
            return new IdempotentCreateResult<T>(IdempotentCreateDisposition.ReplayUnavailable, null);
        }

        var resource = await loadCommittedResource(existing.ResourceId, cancellationToken);
        return resource is null
            ? new IdempotentCreateResult<T>(IdempotentCreateDisposition.ReplayUnavailable, null)
            : new IdempotentCreateResult<T>(IdempotentCreateDisposition.Replayed, resource);
    }

    private Task<IdempotencyRecord?> FindAsync(
        CreateIdempotencyContext context,
        string keyHash,
        CancellationToken cancellationToken)
    {
        return dbContext.IdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(record =>
                    record.TenantId == context.TenantId &&
                    record.ActorUserId == context.ActorUserId &&
                    record.Operation == context.Operation &&
                    record.KeyHash == keyHash,
                cancellationToken);
    }

    private static IdempotencyRecord NewRecord(CreateIdempotencyContext context, string keyHash) => new()
    {
        TenantId = context.TenantId,
        ActorUserId = context.ActorUserId,
        Operation = context.Operation,
        KeyHash = keyHash,
        RequestHash = context.RequestHash,
        ResourceType = context.ResourceType,
        ResourceId = context.ResourceId
    };

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void Validate(CreateIdempotencyContext context)
    {
        if (context.TenantId == Guid.Empty || context.ActorUserId == Guid.Empty || context.ResourceId == Guid.Empty)
            throw new ArgumentException("Idempotency scope identifiers must be non-empty.", nameof(context));
        if (string.IsNullOrWhiteSpace(context.Operation) || context.Operation.Length > 100)
            throw new ArgumentException("Idempotency operation is invalid.", nameof(context));
        if (string.IsNullOrWhiteSpace(context.ClientRequestIdentity) || context.ClientRequestIdentity.Length > 128)
            throw new ArgumentException("Client request identity is invalid.", nameof(context));
        if (context.RequestHash.Length != 64)
            throw new ArgumentException("Request fingerprint is invalid.", nameof(context));
        if (string.IsNullOrWhiteSpace(context.ResourceType) || context.ResourceType.Length > 80)
            throw new ArgumentException("Idempotency resource type is invalid.", nameof(context));
    }
}
