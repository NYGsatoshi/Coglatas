namespace Coglatas.Application.Notifications;

public interface ITaskNotificationPreferenceService
{
    Task<TaskNotificationPreferenceResult> GetAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    Task<TaskNotificationPreferenceResult> UpdateAsync(
        Guid workspaceId,
        UpdateTaskNotificationPreferenceRequest request,
        CancellationToken cancellationToken = default);
}
