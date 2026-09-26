using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Coglatas.Infrastructure.Persistence;

public sealed class EfMessageFollowUpCommitCoordinator(AppDbContext dbContext)
    : IMessageFollowUpCommitCoordinator
{
    public const string SavedMessageIdentityConstraint =
        "IX_message_follow_ups_TenantId_UserId_MessageId";

    public async Task<MessageFollowUpCommitResult> SaveAsync(
        MessageFollowUp pendingFollowUp,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pendingFollowUp);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new MessageFollowUpCommitResult(pendingFollowUp, WasReconciled: false);
        }
        catch (DbUpdateException exception) when (IsSavedMessageIdentityConflict(exception))
        {
            // PostgreSQL rolled back the losing implicit transaction. Clear the
            // staged audit row with the duplicate marker before loading the
            // committed winner so a retry remains one logical operation.
            dbContext.ChangeTracker.Clear();
            var committed = await dbContext.MessageFollowUps
                .AsNoTracking()
                .SingleOrDefaultAsync(item =>
                        item.TenantId == pendingFollowUp.TenantId &&
                        item.UserId == pendingFollowUp.UserId &&
                        item.MessageId == pendingFollowUp.MessageId,
                    cancellationToken);
            if (committed is null)
            {
                throw;
            }

            return new MessageFollowUpCommitResult(committed, WasReconciled: true);
        }
    }

    public async Task<bool> RemoveAsync(
        MessageFollowUp pendingFollowUp,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pendingFollowUp);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent completion won. The failed implicit transaction also
            // rolled back its staged audit row; discard it before confirming the
            // exact private identity is already absent.
            dbContext.ChangeTracker.Clear();
            var stillExists = await dbContext.MessageFollowUps
                .AsNoTracking()
                .AnyAsync(item =>
                        item.TenantId == pendingFollowUp.TenantId &&
                        item.UserId == pendingFollowUp.UserId &&
                        item.MessageId == pendingFollowUp.MessageId,
                    cancellationToken);
            if (stillExists)
            {
                throw;
            }

            return false;
        }
    }

    private static bool IsSavedMessageIdentityConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: SavedMessageIdentityConstraint
        };
}
