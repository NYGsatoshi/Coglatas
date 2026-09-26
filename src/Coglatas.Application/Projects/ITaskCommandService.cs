using Coglatas.Application.Common;

namespace Coglatas.Application.Projects;

public interface ITaskCommandService
{
    Task<Result<CanonicalTaskResponse>> GetAsync(Guid taskId, CancellationToken cancellationToken = default);
    Task<Result<CanonicalTaskResponse>> UpdateDetailsAsync(Guid taskId, TaskUpdateDetailsRequest request, CancellationToken cancellationToken = default);
    Task<Result<GanttEditCommandResponse>> UpdateScheduleAsync(Guid taskId, TaskScheduleUpdateRequest request, CancellationToken cancellationToken = default);
    Task<Result<GanttEditCommandResponse>> UpdateProgressAsync(Guid taskId, TaskProgressUpdateRequest request, CancellationToken cancellationToken = default);
    Task<Result<TaskRelationshipsResponse>> GetRelationshipsAsync(Guid taskId, CancellationToken cancellationToken = default);
    Task<Result<TaskCommandResponse>> TransitionAsync(Guid taskId, TaskTransitionRequest request, CancellationToken cancellationToken = default);
    Task<Result<TaskCommandResponse>> CancelAsync(Guid taskId, TaskReviewRequest request, CancellationToken cancellationToken = default);
    Task<Result<TaskCommandResponse>> ReopenAsync(Guid taskId, TaskReviewRequest request, CancellationToken cancellationToken = default);
    Task<Result<TaskCommandResponse>> SetBlockedStateAsync(Guid taskId, TaskBlockedStateRequest request, CancellationToken cancellationToken = default);
    Task<Result<TaskCommandResponse>> SetAssigneeAsync(Guid taskId, TaskRelationshipUserRequest request, CancellationToken cancellationToken = default);
    Task<Result<TaskCommandResponse>> SetTargetGroupAsync(Guid taskId, TaskTargetGroupRequest request, CancellationToken cancellationToken = default);
    Task<Result<TaskCommandResponse>> AddCollaboratorAsync(Guid taskId, TaskCollaboratorRequest request, CancellationToken cancellationToken = default);
    Task<Result<TaskCommandResponse>> RemoveCollaboratorAsync(Guid taskId, Guid collaboratorUserId, long expectedVersion, CancellationToken cancellationToken = default);
    Task<Result<TaskCommandResponse>> SetReviewerAsync(Guid taskId, TaskRelationshipUserRequest request, CancellationToken cancellationToken = default);
    Task<Result<TaskCommandResponse>> SubmitReviewAsync(Guid taskId, TaskReviewRequest request, CancellationToken cancellationToken = default);
    Task<Result<TaskCommandResponse>> AcceptReviewAsync(Guid taskId, TaskReviewRequest request, CancellationToken cancellationToken = default);
    Task<Result<TaskCommandResponse>> ReturnReviewAsync(Guid taskId, TaskReviewRequest request, CancellationToken cancellationToken = default);
    Task<Result<TaskCommandResponse>> OverrideCompleteAsync(Guid taskId, TaskReviewRequest request, CancellationToken cancellationToken = default);
    Task<Result<TaskCommandResponse>> ClaimAsync(Guid taskId, TaskClaimRequest request, CancellationToken cancellationToken = default);
    Task<Result<TaskCommandResponse>> RestoreAsync(Guid taskId, TaskRestoreRequest request, CancellationToken cancellationToken = default);
    Task<Result<TaskCommandResponse>> DeleteAsync(Guid taskId, TaskDeleteRequest request, CancellationToken cancellationToken = default);
    Task<Result<TaskWatchStateResponse>> GetWatchStateAsync(Guid taskId, CancellationToken cancellationToken = default);
    Task<Result<TaskWatchStateResponse>> WatchAsync(Guid taskId, TaskWatchRequest request, CancellationToken cancellationToken = default);
    Task<Result<TaskWatchStateResponse>> UnwatchAsync(Guid taskId, TaskWatchRequest request, CancellationToken cancellationToken = default);
}
