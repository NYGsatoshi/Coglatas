using Coglatas.Application.Common.Interfaces;

namespace Coglatas.Application.Common;

/// <summary>
/// Fail-closed fallback for minimal hosts that compose Application without persistence.
/// Full Infrastructure registration replaces this service with EfCreateIdempotencyCoordinator.
/// </summary>
internal sealed class UnavailableCreateIdempotencyCoordinator : ICreateIdempotencyCoordinator
{
    public Task<IdempotentCreateResult<T>> ExecuteAsync<T>(
        CreateIdempotencyContext context,
        Func<CancellationToken, Task<T>> stageCreation,
        Func<Guid, CancellationToken, Task<T?>> loadCommittedResource,
        CancellationToken cancellationToken = default)
        where T : class
    {
        return Task.FromResult(new IdempotentCreateResult<T>(
            IdempotentCreateDisposition.ReplayUnavailable,
            null));
    }
}
