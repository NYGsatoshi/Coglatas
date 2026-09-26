namespace Coglatas.Application.Common.Interfaces;

public sealed record CreateIdempotencyContext(
    Guid TenantId,
    Guid ActorUserId,
    string Operation,
    string ClientRequestIdentity,
    string RequestHash,
    string ResourceType,
    Guid ResourceId);

public enum IdempotentCreateDisposition
{
    Created,
    Replayed,
    RequestMismatch,
    ReplayUnavailable
}

public sealed record IdempotentCreateResult<T>(
    IdempotentCreateDisposition Disposition,
    T? Value)
    where T : class;

/// <summary>
/// Owns the persistence transaction and uniqueness race for retry-safe create
/// operations. The staged creation callback must not save independently.
/// </summary>
public interface ICreateIdempotencyCoordinator
{
    Task<IdempotentCreateResult<T>> ExecuteAsync<T>(
        CreateIdempotencyContext context,
        Func<CancellationToken, Task<T>> stageCreation,
        Func<Guid, CancellationToken, Task<T?>> loadCommittedResource,
        CancellationToken cancellationToken = default)
        where T : class;
}
