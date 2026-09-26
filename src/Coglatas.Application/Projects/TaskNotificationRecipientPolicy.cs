using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.Projects;

/// <summary>
/// Stable semantic groups used by Task notification producers.  A producer
/// selects the business event; this policy remains the sole authority for
/// expanding that event into recipients.
/// </summary>
public enum TaskNotificationEventKind
{
    OrdinaryComment = 0,
    PrimaryAssigneeChanged = 1,
    ReviewerAssigned = 2,
    TaskCommentSignificant = 3,
    ReviewSubmitted = 4,
    ReviewReturned = 5,
    BecameBlocked = 6,
    MajorDeadlineChanged = 7
}

public sealed record TaskNotificationRecipientRequest(
    TaskItem Task,
    TaskNotificationEventKind EventKind,
    Guid? ActorUserId = null,
    Guid? PreviousPrimaryAssigneeUserId = null,
    Guid? NewPrimaryAssigneeUserId = null,
    Guid? NewReviewerUserId = null,
    IReadOnlyCollection<Guid>? ValidDirectMentionUserIds = null,
    bool IsImportantComment = false,
    TaskDeadlineChangeClassification DeadlineChangeClassification = TaskDeadlineChangeClassification.None);

public sealed record TaskNotificationRecipientResult(
    IReadOnlyList<Guid> MandatoryRecipientUserIds,
    IReadOnlyList<Guid> WatchDerivedRecipientUserIds,
    IReadOnlyList<Guid> RecipientUserIds);

public interface ITaskNotificationRecipientPolicy
{
    Task<TaskNotificationRecipientResult> ResolveAsync(
        TaskNotificationRecipientRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Central recipient policy for immediate Task notifications.  Mandatory
/// recipients and the optional Watch layer stay separate until their final,
/// deduplicated union so an explicit Watch opt-out can never suppress a
/// mandatory business notification.
/// </summary>
public sealed class TaskNotificationRecipientPolicy(
    IProjectRepository projects,
    IUserRepository users,
    IProjectAuthorizationService projectAuthorization) : ITaskNotificationRecipientPolicy
{
    private static readonly TaskNotificationRecipientResult Empty = new([], [], []);

    public async Task<TaskNotificationRecipientResult> ResolveAsync(
        TaskNotificationRecipientRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Task);

        var mandatory = new HashSet<Guid>();
        var isNotifiableEvent = await AddMandatoryRecipientsAsync(request, mandatory, cancellationToken);
        if (!isNotifiableEvent)
            return Empty;

        var watchDerived = (await projects.ListWatchStatesAsync(request.Task.Id, cancellationToken))
            .Where(state => state.IsWatching && !state.IsExplicitOptOut)
            .Select(state => state.UserId)
            .Where(IsUsableId)
            .ToHashSet();

        var actorUserId = request.ActorUserId;
        if (actorUserId.HasValue)
        {
            mandatory.Remove(actorUserId.Value);
            watchDerived.Remove(actorUserId.Value);
        }

        watchDerived.ExceptWith(mandatory);

        var candidates = mandatory.Concat(watchDerived).Distinct().ToArray();
        if (candidates.Length == 0)
            return Empty;

        var activeUserIds = (await users.GetActiveByIdsAsync(candidates, cancellationToken))
            .Select(user => user.Id)
            .Where(IsUsableId)
            .ToHashSet();

        var currentlyAuthorized = new HashSet<Guid>();
        foreach (var userId in candidates.Where(activeUserIds.Contains))
        {
            if (await projectAuthorization.CanViewProject(userId, request.Task.ProjectId, cancellationToken))
                currentlyAuthorized.Add(userId);
        }

        mandatory.IntersectWith(currentlyAuthorized);
        watchDerived.IntersectWith(currentlyAuthorized);
        watchDerived.ExceptWith(mandatory);

        var mandatoryResult = Sort(mandatory);
        var watchResult = Sort(watchDerived);
        var allResult = Sort(mandatory.Concat(watchDerived));
        return new TaskNotificationRecipientResult(mandatoryResult, watchResult, allResult);
    }

    private async Task<bool> AddMandatoryRecipientsAsync(
        TaskNotificationRecipientRequest request,
        HashSet<Guid> recipients,
        CancellationToken cancellationToken)
    {
        switch (request.EventKind)
        {
            case TaskNotificationEventKind.OrdinaryComment:
                return false;

            case TaskNotificationEventKind.PrimaryAssigneeChanged:
                Add(recipients, request.PreviousPrimaryAssigneeUserId);
                Add(recipients, request.NewPrimaryAssigneeUserId);
                return recipients.Count > 0;

            case TaskNotificationEventKind.ReviewerAssigned:
                Add(recipients, request.NewReviewerUserId);
                return recipients.Count > 0;

            case TaskNotificationEventKind.TaskCommentSignificant:
                foreach (var userId in request.ValidDirectMentionUserIds ?? [])
                    Add(recipients, userId);

                if (request.IsImportantComment)
                {
                    Add(recipients, request.Task.PrimaryAssigneeUserId);
                    Add(recipients, request.Task.ReviewerUserId);
                    foreach (var collaborator in await projects.ListCollaboratorsAsync(request.Task.Id, cancellationToken))
                        Add(recipients, collaborator.UserId);
                }

                return request.IsImportantComment || recipients.Count > 0;

            case TaskNotificationEventKind.ReviewSubmitted:
                Add(recipients, request.Task.ReviewerUserId);
                return true;

            case TaskNotificationEventKind.ReviewReturned:
                Add(recipients, request.Task.PrimaryAssigneeUserId);
                return true;

            case TaskNotificationEventKind.BecameBlocked:
                Add(recipients, request.Task.PrimaryAssigneeUserId);
                Add(recipients, request.Task.ReviewerUserId);
                return true;

            case TaskNotificationEventKind.MajorDeadlineChanged:
                if (request.DeadlineChangeClassification == TaskDeadlineChangeClassification.None)
                    return false;
                Add(recipients, request.Task.PrimaryAssigneeUserId);
                Add(recipients, request.Task.ReviewerUserId);
                return true;

            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.EventKind, "Unsupported Task notification event kind.");
        }
    }

    private static void Add(ISet<Guid> recipients, Guid? userId)
    {
        if (userId.HasValue && IsUsableId(userId.Value))
            recipients.Add(userId.Value);
    }

    private static bool IsUsableId(Guid userId) => userId != Guid.Empty;

    private static IReadOnlyList<Guid> Sort(IEnumerable<Guid> recipients) =>
        recipients.Distinct().OrderBy(userId => userId).ToArray();
}
