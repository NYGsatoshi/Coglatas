using Coglatas.Domain.Entities;

namespace Coglatas.Application.Common.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<User?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<User>> SearchActiveAsync(string query, int take, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<User>>([]);

    Task<IReadOnlyList<User>> GetActiveByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<User>>([]);

    Task AddAsync(User user, CancellationToken cancellationToken = default);
}

public interface IInviteRepository
{
    Task<Invite?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the invite for an acceptance mutation while holding a provider-level
    /// write lock for the surrounding transaction. Non-relational/test providers
    /// may fall back to the ordinary tracked read.
    /// </summary>
    Task<Invite?> GetByTokenHashForUpdateAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        GetByTokenHashAsync(tokenHash, cancellationToken);
}

public interface ISessionRepository
{
    Task AddAsync(Session session, CancellationToken cancellationToken = default);

    Task<Session?> GetByIdWithUserAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(Guid sessionId, DateTimeOffset revokedAt, CancellationToken cancellationToken = default);

    Task<int> RevokeUserSessionsAsync(Guid userId, DateTimeOffset revokedAt, Guid? exceptSessionId = null, CancellationToken cancellationToken = default);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a complete application operation in one database transaction.
    /// The default keeps lightweight/non-relational test implementations source
    /// compatible; relational infrastructure overrides it with a real transaction.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default) =>
        operation(cancellationToken);

    /// <summary>
    /// Serializes Invite acceptance against concurrent administrative Tenant and
    /// Workspace membership lifecycle changes. Relational infrastructure locks
    /// existing membership rows in a fixed order inside the acceptance transaction;
    /// lightweight test implementations may safely no-op.
    /// </summary>
    Task LockInviteAcceptanceMembershipsAsync(
        Guid tenantId,
        Guid workspaceId,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

/// <summary>
/// Task commands use this narrowly-scoped persistence boundary so an EF optimistic
/// concurrency failure can be classified without assigning Task error codes to
/// unrelated aggregates.
/// </summary>
public interface ITaskCommandUnitOfWork : IUnitOfWork
{
    Task<TaskCommandSaveOutcome> SaveTaskCommandAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears entities loaded while recovering from a failed task-command save.
    /// Recovery must reauthorize against authoritative data, but must not leave
    /// that data tracked in the losing request scope.
    /// </summary>
    void ClearTaskCommandTracking() { }
}

public enum TaskCommandSaveResult
{
    Saved,
    ConcurrencyConflict,
    UniqueConflict
}

/// <summary>
/// The Task command persistence result.  PostgreSQL supplies the violated
/// constraint for unique conflicts so callers can apply only the domain mapping
/// that owns that constraint.
/// </summary>
public sealed record TaskCommandSaveOutcome(TaskCommandSaveResult Result, string? ConstraintName = null)
{
    public bool IsSaved => Result == TaskCommandSaveResult.Saved;

    public static implicit operator TaskCommandSaveOutcome(TaskCommandSaveResult result) => new(result);

    public static bool operator ==(TaskCommandSaveOutcome? outcome, TaskCommandSaveResult result) =>
        outcome?.Result == result;

    public static bool operator !=(TaskCommandSaveOutcome? outcome, TaskCommandSaveResult result) =>
        outcome?.Result != result;
}
