using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Coglatas.Infrastructure.Persistence;

public sealed class UserRepository(AppDbContext dbContext) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return dbContext.Users.FirstOrDefaultAsync(user => user.Id == id, cancellationToken);
    }

    public Task<User?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
    {
        return dbContext.Users.FirstOrDefaultAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken);
    }

    public async Task<IReadOnlyList<User>> SearchActiveAsync(string query, int take, CancellationToken cancellationToken = default)
    {
        var normalized = query.Trim().ToUpperInvariant();
        return await dbContext.Users.AsNoTracking()
            .Where(user => user.DeletedAt == null && user.Status == UserStatus.Active && user.DisplayName.ToUpper().Contains(normalized))
            .OrderBy(user => user.DisplayName).ThenBy(user => user.Id).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<User>> GetActiveByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0) return [];
        return await dbContext.Users.AsNoTracking().Where(user => ids.Contains(user.Id) && user.DeletedAt == null && user.Status == UserStatus.Active).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        await dbContext.Users.AddAsync(user, cancellationToken);
    }
}

public sealed class InviteRepository(AppDbContext dbContext) : IInviteRepository
{
    public Task<Invite?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        return dbContext.Invites.FirstOrDefaultAsync(invite => invite.TokenHash == tokenHash, cancellationToken);
    }

    public async Task<Invite?> GetByTokenHashForUpdateAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        if (!dbContext.Database.IsRelational() || dbContext.Database.CurrentTransaction is null)
        {
            return await GetByTokenHashAsync(tokenHash, cancellationToken);
        }

        // Resolve through EF first so the normal Tenant query filter remains the
        // visibility boundary. Only a visible Invite is then locked by primary key.
        // Reloading after FOR UPDATE is required because another transaction may
        // have committed AcceptedAt while this transaction was waiting for the lock.
        var invite = await GetByTokenHashAsync(tokenHash, cancellationToken);
        if (invite is null)
        {
            return null;
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM invites WHERE \"Id\" = {invite.Id} FOR UPDATE",
            cancellationToken);
        await dbContext.Entry(invite).ReloadAsync(cancellationToken);
        return invite;
    }
}

public sealed class SessionRepository(AppDbContext dbContext) : ISessionRepository
{
    public async Task AddAsync(Session session, CancellationToken cancellationToken = default)
    {
        await dbContext.Sessions.AddAsync(session, cancellationToken);
    }

    public Task<Session?> GetByIdWithUserAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        return dbContext.Sessions
            .Include(session => session.User)
            .FirstOrDefaultAsync(session => session.Id == sessionId, cancellationToken);
    }

    public async Task<bool> RevokeAsync(Guid sessionId, DateTimeOffset revokedAt, CancellationToken cancellationToken = default)
    {
        var session = await dbContext.Sessions.FirstOrDefaultAsync(item => item.Id == sessionId, cancellationToken);
        if (session is not null && !session.RevokedAt.HasValue)
        {
            session.RevokedAt = revokedAt;
            return true;
        }

        return false;
    }

    public async Task<int> RevokeUserSessionsAsync(Guid userId, DateTimeOffset revokedAt, Guid? exceptSessionId = null, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Sessions
            .Where(session => session.UserId == userId && !session.RevokedAt.HasValue);

        if (exceptSessionId.HasValue)
        {
            query = query.Where(session => session.Id != exceptSessionId.Value);
        }

        var activeSessions = await query.ToListAsync(cancellationToken);
        foreach (var session in activeSessions)
        {
            session.RevokedAt = revokedAt;
        }

        return activeSessions.Count;
    }
}

public sealed class EfUnitOfWork(
    AppDbContext dbContext,
    ITaskExecutionScopeRepository? taskExecutionScopes = null) : IUnitOfWork, ITaskCommandUnitOfWork
{
    public void ClearTaskCommandTracking()
    {
        dbContext.ChangeTracker.Clear();
        taskExecutionScopes?.ClearPendingSourcePolicyDocuments();
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        SaveWithPolicyDocumentsAsync(cancellationToken);

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        if (!dbContext.Database.IsRelational() || dbContext.Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            ClearTaskCommandTracking();
            throw;
        }
    }

    public async Task LockInviteAcceptanceMembershipsAsync(
        Guid tenantId,
        Guid workspaceId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (!dbContext.Database.IsRelational() || dbContext.Database.CurrentTransaction is null)
        {
            return;
        }

        // Keep lock ordering stable across every Invite acceptance. This prevents a
        // concurrent Tenant/Workspace suspension from being read as an older state
        // and then overwritten by acceptance after the administrator commits.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM tenant_users WHERE \"TenantId\" = {tenantId} AND \"UserId\" = {userId} FOR UPDATE",
            cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM workspace_members WHERE \"WorkspaceId\" = {workspaceId} AND \"UserId\" = {userId} FOR UPDATE",
            cancellationToken);
    }

    public async Task<TaskCommandSaveOutcome> SaveTaskCommandAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await SaveWithPolicyDocumentsAsync(cancellationToken);
            return TaskCommandSaveResult.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            ClearTaskCommandTracking();
            return TaskCommandSaveResult.ConcurrencyConflict;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            } postgres)
        {
            ClearTaskCommandTracking();
            return new TaskCommandSaveOutcome(TaskCommandSaveResult.UniqueConflict, postgres.ConstraintName);
        }
        catch
        {
            taskExecutionScopes?.ClearPendingSourcePolicyDocuments();
            throw;
        }
    }

    private async Task<int> SaveWithPolicyDocumentsAsync(CancellationToken cancellationToken)
    {
        if (taskExecutionScopes?.HasPendingSourcePolicyDocuments != true)
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (!dbContext.Database.IsRelational())
        {
            var nonRelationalCount = await dbContext.SaveChangesAsync(cancellationToken);
            await taskExecutionScopes.FlushPendingSourcePolicyDocumentsAsync(cancellationToken);
            return nonRelationalCount;
        }

        if (dbContext.Database.CurrentTransaction is not null)
        {
            var nestedCount = await dbContext.SaveChangesAsync(cancellationToken);
            await taskExecutionScopes.FlushPendingSourcePolicyDocumentsAsync(cancellationToken);
            return nestedCount;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var count = await dbContext.SaveChangesAsync(cancellationToken);
            await taskExecutionScopes.FlushPendingSourcePolicyDocumentsAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return count;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            taskExecutionScopes.ClearPendingSourcePolicyDocuments();
            throw;
        }
    }
}
