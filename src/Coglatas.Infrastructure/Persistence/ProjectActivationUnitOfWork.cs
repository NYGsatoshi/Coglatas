using System.Data;
using Coglatas.Application.Common;
using Coglatas.Application.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>
/// Owns the complete WPC-02D activation transaction. SERIALIZABLE gives one
/// coherent database snapshot for current authorization, Project state and
/// configured workflow defaults/templates, then commits every staged effect once.
/// </summary>
public sealed class ProjectActivationUnitOfWork(AppDbContext dbContext) : IProjectActivationUnitOfWork
{
    public async Task<Result> ExecuteActivationAsync(
        Func<CancellationToken, Task<Result>> operation,
        CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            return DependencyFailure("A nested Project activation transaction is not allowed.");
        }

        IDbContextTransaction? transaction = null;
        try
        {
            transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            var result = await operation(cancellationToken);
            if (!result.IsSuccess)
            {
                await RollbackAndClearAsync(transaction);
                return result;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RollbackAndClearAsync(transaction);
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            await RollbackAndClearAsync(transaction);
            return ConcurrentModification();
        }
        catch (Exception exception) when (ContainsPostgresConflict(exception))
        {
            // EF Core/Npgsql can add provider/ORM wrappers around a PostgreSQL
            // serialization, unique-key or deadlock error. The SQLSTATE is the
            // authoritative signal; preserve the WPC concurrency contract even
            // when PostgresException is not the immediate caught exception.
            await RollbackAndClearAsync(transaction);
            return ConcurrentModification();
        }
        catch (DbUpdateException)
        {
            await RollbackAndClearAsync(transaction);
            return DependencyFailure("Project activation persistence failed.");
        }
        catch (NpgsqlException)
        {
            // Includes connection/open/begin/commit provider failures that occur
            // outside SaveChangesAsync. They are dependency failures, not 500s.
            await RollbackAndClearAsync(transaction);
            return DependencyFailure("Project activation persistence is unavailable.");
        }
        catch (TimeoutException)
        {
            await RollbackAndClearAsync(transaction);
            return DependencyFailure("Project activation persistence is unavailable.");
        }
        catch (InvalidOperationException)
        {
            await RollbackAndClearAsync(transaction);
            return DependencyFailure("Project activation persistence failed.");
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private async Task RollbackAndClearAsync(IDbContextTransaction? transaction)
    {
        if (transaction is not null)
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // The original failure remains authoritative.
            }
        }

        // Prevent any losing/failed activation graph from leaking into later work
        // in this scoped context, including failures before a transaction starts.
        dbContext.ChangeTracker.Clear();
    }

    private static bool ContainsPostgresConflict(Exception exception)
    {
        if (exception is PostgresException postgres && IsConflict(postgres))
        {
            return true;
        }

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                if (ContainsPostgresConflict(inner))
                {
                    return true;
                }
            }

            return false;
        }

        return exception.InnerException is not null &&
               ContainsPostgresConflict(exception.InnerException);
    }

    private static bool IsConflict(PostgresException exception) =>
        exception.SqlState is
            "23505" or // unique_violation
            "40001" or // serialization_failure
            "40P01";   // deadlock_detected

    private static Result ConcurrentModification() =>
        Result.Failure(new ApplicationErrorDetail(
            "ConcurrentModification",
            "Project activation raced another current mutation. Refetch the Project before retrying.",
            Target: "project"));

    private static Result DependencyFailure(string message) =>
        Result.Failure(new ApplicationErrorDetail(
            "DependencyUnavailable",
            message));
}
