using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Projects;

/// <summary>
/// Stages one recipient-private Notification intent per logical Task event.
/// The caller owns the Task mutation's single save/transaction boundary.
/// </summary>
public interface ITaskNotificationProducer
{
    Task ProduceAsync(
        TaskNotificationRecipientRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class TaskNotificationProducer(
    ITaskNotificationRecipientPolicy recipientPolicy,
    INotificationService notifications,
    IFeatureFlagService featureFlags) : ITaskNotificationProducer
{
    public async Task ProduceAsync(
        TaskNotificationRecipientRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await featureFlags.IsEnabledAsync(FeatureKeys.TasksNotificationsV1, cancellationToken))
        {
            return;
        }

        var recipients = await recipientPolicy.ResolveAsync(request, cancellationToken);
        if (recipients.RecipientUserIds.Count == 0)
        {
            return;
        }

        var presentation = PresentationFor(request.EventKind);
        var logicalKey = $"task:{request.Task.Id:N}:event:{presentation.SourceGroup}:version:{request.Task.VersionNo}";
        foreach (var recipientUserId in recipients.RecipientUserIds
                     .Where(userId => userId != Guid.Empty)
                     .Distinct()
                     .Order())
        {
            await notifications.StageTaskByLogicalKeyAsync(
                recipientUserId,
                presentation.NotificationType,
                presentation.Title,
                request.Task.Id,
                logicalKey,
                cancellationToken);
        }
    }

    private static TaskNotificationPresentation PresentationFor(TaskNotificationEventKind eventKind) => eventKind switch
    {
        TaskNotificationEventKind.PrimaryAssigneeChanged or TaskNotificationEventKind.ReviewerAssigned =>
            new("TaskAssignmentChanged", NotificationType.TaskAssigned, "Task assignment changed"),
        TaskNotificationEventKind.TaskCommentSignificant or TaskNotificationEventKind.OrdinaryComment =>
            new("TaskCommentSignificant", NotificationType.Mention, "Task comment requires attention"),
        TaskNotificationEventKind.ReviewSubmitted =>
            new("TaskReviewSubmitted", NotificationType.TaskStatusChanged, "Task review requested"),
        TaskNotificationEventKind.ReviewReturned =>
            new("TaskReviewReturned", NotificationType.TaskStatusChanged, "Task review returned"),
        TaskNotificationEventKind.BecameBlocked =>
            new("TaskBecameBlocked", NotificationType.TaskStatusChanged, "Task blocked"),
        TaskNotificationEventKind.MajorDeadlineChanged =>
            new("TaskDeadlineChanged", NotificationType.TaskDueSoon, "Task deadline changed"),
        _ => throw new ArgumentOutOfRangeException(nameof(eventKind), eventKind, "Unknown Task notification event kind.")
    };

    private sealed record TaskNotificationPresentation(
        string SourceGroup,
        NotificationType NotificationType,
        string Title);
}
