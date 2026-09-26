using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.Realtime;

/// <summary>
/// Creates the catalog's invalidation-only events in the same unit of work as
/// the business mutation.  These helpers intentionally contain no display
/// data: HTTP remains the authority for every refreshed projection.
/// </summary>
public interface IBusinessInvalidationPublisher
{
    Task TaskChangedAsync(TaskItem task, Guid actorUserId, string change, IEnumerable<string>? changedFields = null, IEnumerable<Guid>? affectedUserIds = null, CancellationToken cancellationToken = default);

    Task TaskAssignmentChangedAsync(
        TaskItem task,
        Guid actorUserId,
        string change,
        IEnumerable<Guid>? affectedUserIds = null,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    Task TaskCommentChangedAsync(
        TaskItem task,
        TaskComment comment,
        Guid actorUserId,
        string change,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    Task ProjectChangedAsync(Project project, Guid actorUserId, string change, CancellationToken cancellationToken = default);
    Task AnnouncementChangedAsync(Announcement announcement, Guid actorUserId, string change, IEnumerable<Guid> audienceUserIds, CancellationToken cancellationToken = default);
    Task FileChangedAsync(FileObject fileObject, Attachment attachment, Guid actorUserId, string change, CancellationToken cancellationToken = default);
}

public sealed class BusinessInvalidationPublisher(
    ITransactionalOutbox outbox,
    ICurrentTenant currentTenant,
    IClock clock) : IBusinessInvalidationPublisher
{
    public Task TaskChangedAsync(TaskItem task, Guid actorUserId, string change, IEnumerable<string>? changedFields = null, IEnumerable<Guid>? affectedUserIds = null, CancellationToken cancellationToken = default)
    {
        // TaskItem owns a persisted optimistic-concurrency token.  The durable
        // event must carry that aggregate version, not a process-local clock
        // token, so consumers can compare it with the authoritative reload.
        var version = task.VersionNo;
        var targets = new List<RealtimeRoutingTarget> { new(RealtimeSubscriptionType.Project, task.ProjectId) };
        targets.AddRange(
            (affectedUserIds ?? [])
            .Where(userId => userId != Guid.Empty)
            .Distinct()
            .OrderBy(userId => userId)
            .Select(userId => new RealtimeRoutingTarget(RealtimeSubscriptionType.User, userId)));
        return EnqueueAsync("Projects.TaskChanged.v1", "Task", task.Id, actorUserId, new
        {
            projectId = task.ProjectId,
            taskId = task.Id,
            taskVersion = version,
            change,
            changedFields = (changedFields ?? []).Distinct(StringComparer.Ordinal).ToArray(),
            requiresRefetch = true
        }, targets, version, cancellationToken);
    }

    public Task TaskAssignmentChangedAsync(
        TaskItem task,
        Guid actorUserId,
        string change,
        IEnumerable<Guid>? affectedUserIds = null,
        CancellationToken cancellationToken = default)
    {
        var version = task.VersionNo;
        // Retain the compatible affected-user argument for callers, but keep
        // the new Assignment semantic event Project-only until PR07-D adds
        // Task-specific dispatch and replay authorization.
        var targets = new List<RealtimeRoutingTarget>
        {
            new(RealtimeSubscriptionType.Project, task.ProjectId)
        };
        return EnqueueAsync("Projects.TaskAssignmentChanged.v1", "Task", task.Id, actorUserId, new
        {
            projectId = task.ProjectId,
            taskId = task.Id,
            taskVersion = version,
            change,
            requiresRefetch = true
        }, targets, version, cancellationToken);
    }

    public Task TaskCommentChangedAsync(
        TaskItem task,
        TaskComment comment,
        Guid actorUserId,
        string change,
        CancellationToken cancellationToken = default)
    {
        var version = task.VersionNo;
        return EnqueueAsync("Projects.TaskCommentChanged.v1", "Task", task.Id, actorUserId, new
        {
            projectId = task.ProjectId,
            taskId = task.Id,
            taskVersion = version,
            commentId = comment.Id,
            commentVersion = comment.VersionNo,
            change,
            requiresRefetch = true
        }, [new RealtimeRoutingTarget(RealtimeSubscriptionType.Project, task.ProjectId)], version, cancellationToken);
    }

    public Task ProjectChangedAsync(Project project, Guid actorUserId, string change, CancellationToken cancellationToken = default)
    {
        // Project owns the persisted revision used by its bounded projections.
        // Advance it before queuing the event so the durable hint and the
        // authoritative HTTP snapshot use exactly the same ordering token.
        project.VersionNo = Math.Max(1L, checked(project.VersionNo + 1L));
        var version = project.VersionNo;
        return EnqueueAsync("Projects.ProjectChanged.v1", "Project", project.Id, actorUserId, new
        {
            workspaceId = project.WorkspaceId,
            projectId = project.Id,
            projectVersion = version,
            change,
            requiresRefetch = true
        }, [
            new RealtimeRoutingTarget(RealtimeSubscriptionType.Project, project.Id),
            new RealtimeRoutingTarget(RealtimeSubscriptionType.Workspace, project.WorkspaceId)
        ], version, cancellationToken);
    }

    public Task AnnouncementChangedAsync(Announcement announcement, Guid actorUserId, string change, IEnumerable<Guid> audienceUserIds, CancellationToken cancellationToken = default)
    {
        var version = VersionToken();
        var recipients = audienceUserIds.Where(id => id != Guid.Empty).Distinct().Select(id => new RealtimeRoutingTarget(RealtimeSubscriptionType.User, id)).ToArray();
        return EnqueueAsync("Announcements.AnnouncementChanged.v1", "Announcement", announcement.Id, actorUserId, new
        {
            announcementId = announcement.Id,
            announcementVersion = version,
            change,
            requiresRefetch = true
        }, recipients, version, cancellationToken);
    }

    public Task FileChangedAsync(FileObject fileObject, Attachment attachment, Guid actorUserId, string change, CancellationToken cancellationToken = default)
    {
        var version = VersionToken();
        return EnqueueAsync("Files.FileChanged.v1", "File", fileObject.Id, actorUserId, new
        {
            workspaceId = attachment.WorkspaceId,
            fileId = fileObject.Id,
            fileVersion = version,
            change,
            requiresRefetch = true
        }, [new RealtimeRoutingTarget(RealtimeSubscriptionType.Workspace, attachment.WorkspaceId)], version, cancellationToken);
    }

    private async Task EnqueueAsync(string eventType, string aggregateType, Guid aggregateId, Guid actorUserId, object payload, IReadOnlyCollection<RealtimeRoutingTarget> targets, long aggregateVersion, CancellationToken cancellationToken)
    {
        if (!currentTenant.IsAvailable || aggregateId == Guid.Empty || targets.Count == 0)
        {
            return;
        }

        var now = clock.UtcNow;
        var result = await outbox.EnqueueAsync(new DurableEventEnvelope(
            Guid.NewGuid(), eventType, RealtimeEventCatalog.PayloadSchemaVersion1, now,
            currentTenant.TenantId, aggregateType, aggregateId, aggregateVersion,
            new RealtimeActor("User", actorUserId), null, null, JsonSerializer.SerializeToElement(payload)), targets, cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.Error ?? "Business invalidation could not be queued.");
        }
    }

    // These aggregates currently have no persisted concurrency/version column.
    // The committed UTC tick is the approved ordering token until one exists.
    private long VersionToken() => clock.UtcNow.UtcDateTime.Ticks;
}
