using System.Data;
using System.Linq.Expressions;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Forms.SurveyManagement;
using Coglatas.Domain.Common;
using Coglatas.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Coglatas.Infrastructure.Persistence;

public sealed class AqSurveyManagementRepository(AppDbContext db, ICurrentTenant currentTenant) : IAqSurveyManagementStore
{
    private bool _writing;

    public Task<T?> FindAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken)
        where T : Entity, ITenantEntity =>
        Rows<T>().SingleOrDefaultAsync(predicate, cancellationToken);

    public async Task<IReadOnlyList<T>> PageAsync<T>(
        Expression<Func<T, bool>> predicate, Guid? after, int take, CancellationToken cancellationToken)
        where T : Entity, ITenantEntity
    {
        var rows = Rows<T>().Where(predicate);
        if (after is { } id)
            rows = rows.Where(item => item.Id.CompareTo(id) > 0);
        return await rows.OrderBy(item => item.Id).Take(take).ToListAsync(cancellationToken);
    }

    public Task<bool> AnyAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken)
        where T : Entity, ITenantEntity =>
        Rows<T>().AnyAsync(predicate, cancellationToken);

    public Task<FormVersion?> FindCommonVersionAsync(Guid formId, Guid versionId, CancellationToken cancellationToken) =>
        Rows<FormVersion>().Include(x => x.Fields)
            .SingleOrDefaultAsync(x => x.Id == versionId && x.FormDefinitionId == formId, cancellationToken);

    public Task<TargetQuestionSetVersion?> FindAdditionalVersionAsync(Guid targetId, Guid versionId, CancellationToken cancellationToken) =>
        Rows<TargetQuestionSetVersion>().Include(x => x.Fields)
            .SingleOrDefaultAsync(x => x.Id == versionId && x.SurveyTargetId == targetId, cancellationToken);

    public void Add<T>(T entity) where T : Entity, ITenantEntity
    {
        RequireMutationScope(entity);
        db.Set<T>().Add(entity);
    }

    public void Remove<T>(T entity) where T : Entity, ITenantEntity
    {
        RequireMutationScope(entity);
        db.Set<T>().Remove(entity);
    }

    public Task FlushAsync(CancellationToken cancellationToken)
    {
        if (!_writing)
            throw new InvalidOperationException("Survey management changes require a transaction.");
        return db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Result<T>> WriteAsync<T>(Func<CancellationToken, Task<Result<T>>> command, CancellationToken cancellationToken)
    {
        if (_writing || db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Survey management requires an owned transaction.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        _writing = true;
        var committed = false;
        try
        {
            var result = await command(cancellationToken);
            if (result.IsSuccess)
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                committed = true;
            }
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<T>.Failure(new ApplicationErrorDetail("RowVersionConflict", "Reload the current record before updating it."));
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation })
        {
            return ConcurrentWrite<T>();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            return ConcurrentWrite<T>();
        }
        catch (Exception exception) when (exception is ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return Result<T>.Failure(new ApplicationErrorDetail("ValidationFailed", "The survey settings do not satisfy the domain rules."));
        }
        finally
        {
            _writing = false;
            if (!committed)
                db.ChangeTracker.Clear();
        }
    }

    private IQueryable<T> Rows<T>() where T : Entity, ITenantEntity
    {
        var tenantId = currentTenant.TenantId;
        var tenantAvailable = currentTenant.IsAvailable && !currentTenant.IsPlatformScope;
        var rows = db.Set<T>().Where(x => tenantAvailable && EF.Property<Guid>(x, nameof(ITenantEntity.TenantId)) == tenantId);
        return _writing ? rows : rows.AsNoTracking();
    }

    private void RequireMutationScope(ITenantEntity entity)
    {
        if (!_writing || !currentTenant.IsAvailable || currentTenant.IsPlatformScope || currentTenant.TenantId != entity.TenantId)
            throw new InvalidOperationException("Survey management requires the current tenant transaction.");
    }

    private static Result<T> ConcurrentWrite<T>() =>
        Result<T>.Failure(new ApplicationErrorDetail("ConcurrentModification", "The survey resource changed. Refresh and retry."));
}
