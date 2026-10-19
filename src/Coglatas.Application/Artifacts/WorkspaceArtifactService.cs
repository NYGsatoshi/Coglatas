using System.Text;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Workspaces;

namespace Coglatas.Application.Artifacts;

// Private implementation-review candidate. Not registered by Product composition.
public sealed class WorkspaceArtifactService(
    IWorkspaceArtifactRepository repository,
    IWorkspaceArtifactCursorCodec cursors,
    IWorkspaceAuthorizationService authorization,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    IClock clock) : IWorkspaceArtifactService
{
    public async Task<Result<WorkspaceArtifactPageResponse>> ListAsync(
        Guid workspaceId,
        WorkspaceArtifactListRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = currentUser.UserId ?? Guid.Empty;
        var tenantId = currentTenant.TenantId;
        if (!IsCurrentContext(tenantId, actorId) || workspaceId == Guid.Empty ||
            !await authorization.CanViewWorkspace(actorId, workspaceId, cancellationToken) ||
            !IsCurrentContext(tenantId, actorId))
        {
            return NotFound();
        }

        var rawQuery = request.Query ?? string.Empty;
        var query = rawQuery.Trim();
        if (rawQuery.EnumerateRunes().Any(Rune.IsControl) || !IsValidQuery(query) || request.Sort != "updatedAt" || request.Direction != "desc")
        {
            return Invalid("ArtifactQueryInvalid");
        }

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        WorkspaceArtifactPosition? after = null;
        if (request.Cursor is not null)
        {
            if (!cursors.TryDecode(request.Cursor, out var cursor) || cursor is null ||
                cursor.FormatVersion != 1 || cursor.TenantId != tenantId || cursor.ActorUserId != actorId ||
                cursor.WorkspaceId != workspaceId || cursor.Query != query || cursor.PageSize != pageSize ||
                cursor.Sort != request.Sort || cursor.Direction != request.Direction ||
                cursor.ExpiresAtUtc <= clock.UtcNow || cursor.After is null || cursor.After.Id == Guid.Empty ||
                cursor.After.UpdatedAtUtc.Offset != TimeSpan.Zero)
            {
                return Invalid("ArtifactCursorInvalid");
            }
            after = cursor.After;
        }

        var rows = await repository.ReadAsync(
            new WorkspaceArtifactReadQuery(tenantId, actorId, workspaceId, query, pageSize, after),
            cancellationToken);

        // A previously admitted Workspace or context is not a durable read grant.
        if (!IsCurrentContext(tenantId, actorId) ||
            !await authorization.CanViewWorkspace(actorId, workspaceId, cancellationToken) ||
            !IsCurrentContext(tenantId, actorId))
        {
            return NotFound();
        }

        // The final Workspace authorization can await a committed Project change.
        // Re-read the complete bounded scope once and invalidate a changed page.
        var currentRows = await repository.ReadAsync(
            new WorkspaceArtifactReadQuery(tenantId, actorId, workspaceId, query, pageSize, after),
            cancellationToken);
        if (!IsCurrentContext(tenantId, actorId) || !rows.SequenceEqual(currentRows))
        {
            return NotFound();
        }
        rows = currentRows;

        var hasMore = rows.Count > pageSize;
        var items = rows.Take(pageSize).ToArray();
        string? nextCursor = null;
        if (hasMore)
        {
            var last = items[^1];
            nextCursor = cursors.Encode(new WorkspaceArtifactCursor(
                1, tenantId, actorId, workspaceId, query, request.Sort, request.Direction, pageSize,
                new WorkspaceArtifactPosition((last.UpdatedAt ?? last.CreatedAt).ToUniversalTime(), last.Id),
                clock.UtcNow.AddMinutes(15).ToUniversalTime()));
        }
        return Result<WorkspaceArtifactPageResponse>.Success(new(items, nextCursor, hasMore));
    }

    private bool IsCurrentContext(Guid tenantId, Guid actorId) =>
        currentTenant.IsAvailable && !currentTenant.IsPlatformScope && tenantId != Guid.Empty &&
        currentTenant.TenantId == tenantId && currentUser.IsAuthenticated && actorId != Guid.Empty &&
        currentUser.UserId == actorId;

    private static bool IsValidQuery(string query)
    {
        if (query.EnumerateRunes().Take(201).Count() > 200)
        {
            return false;
        }
        // Reject invalid UTF-16 rather than normalizing malformed cursor identity implicitly.
        for (var index = 0; index < query.Length; index++)
        {
            if (!char.IsSurrogate(query[index])) continue;
            if (!char.IsHighSurrogate(query[index]) || index + 1 >= query.Length ||
                !char.IsLowSurrogate(query[++index])) return false;
        }
        return true;
    }

    private static Result<WorkspaceArtifactPageResponse> NotFound() =>
        Result<WorkspaceArtifactPageResponse>.Failure(new ApplicationErrorDetail("WorkspaceNotFound", "Workspace not found."));

    private static Result<WorkspaceArtifactPageResponse> Invalid(string code) =>
        Result<WorkspaceArtifactPageResponse>.Failure(new ApplicationErrorDetail(code, code));
}
