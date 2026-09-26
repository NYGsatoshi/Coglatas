using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Coglatas.Infrastructure.Persistence;

public sealed class EfMessageIdempotencyCommitCoordinator(AppDbContext dbContext)
    : IMessageIdempotencyCommitCoordinator
{
    public const string ClientRequestIdentityConstraint =
        // PostgreSQL identifiers are limited to 63 bytes. The original
        // 20260718125541 migration persisted this exact truncated index name;
        // changing it would require a separate schema migration.
        "IX_messages_TenantId_ConversationId_AuthorUserId_ClientRequest~";

    public async Task<MessageIdempotencyCommitResult> CommitAsync(
        Message pendingMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pendingMessage);
        if (!pendingMessage.ClientRequestId.HasValue)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new MessageIdempotencyCommitResult(pendingMessage, WasReconciled: false);
        }

        try
        {
            // SaveChanges owns one implicit relational transaction containing
            // the Message, audit, notification, and outbox rows staged by the
            // application use case.
            await dbContext.SaveChangesAsync(cancellationToken);
            return new MessageIdempotencyCommitResult(pendingMessage, WasReconciled: false);
        }
        catch (DbUpdateException exception) when (IsClientRequestIdentityConflict(exception))
        {
            // PostgreSQL has rolled back the losing implicit transaction. Clear
            // every staged side effect before loading the committed winner, so
            // a replay cannot duplicate audit/notification/outbox records.
            dbContext.ChangeTracker.Clear();
            var committed = await dbContext.Messages
                .AsNoTracking()
                .Include(message => message.AuthorUser)
                .Include(message => message.Attachments)
                .ThenInclude(link => link.Attachment)
                .SingleOrDefaultAsync(message =>
                        message.TenantId == pendingMessage.TenantId &&
                        message.ConversationId == pendingMessage.ConversationId &&
                        message.AuthorUserId == pendingMessage.AuthorUserId &&
                        message.ClientRequestId == pendingMessage.ClientRequestId,
                    cancellationToken);
            if (committed is null)
            {
                // The exact constraint was reported but no committed winner is
                // visible. Preserve the original database failure rather than
                // converting an unrelated/provider anomaly into success.
                throw;
            }

            return new MessageIdempotencyCommitResult(committed, WasReconciled: true);
        }
    }

    private static bool IsClientRequestIdentityConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ClientRequestIdentityConstraint
        };
}
