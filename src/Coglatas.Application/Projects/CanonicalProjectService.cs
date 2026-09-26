using Coglatas.Application.Common;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Projects;

/// <summary>
/// Final Project application boundary. Existing ProjectService retains the
/// compatibility implementation for non-membership operations; the three
/// member mutations are routed through the canonical atomic membership service.
/// </summary>
public sealed class CanonicalProjectService(
    ProjectService inner,
    IProjectMembershipService membership) : IProjectService
{
    public Task<Result<PagedResponse<ProjectResponse>>> ListAsync(ProjectListQuery query, CancellationToken cancellationToken = default) => inner.ListAsync(query, cancellationToken);
    public Task<Result<ProjectResponse>> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken = default) => inner.CreateAsync(request, cancellationToken);
    public Task<Result<ProjectResponse>> GetAsync(Guid projectId, CancellationToken cancellationToken = default) => inner.GetAsync(projectId, cancellationToken);
    public Task<Result<ProjectResponse>> UpdateAsync(Guid projectId, UpdateProjectRequest request, CancellationToken cancellationToken = default) => inner.UpdateAsync(projectId, request, cancellationToken);
    public Task<Result> ArchiveAsync(Guid projectId, CancellationToken cancellationToken = default) => inner.ArchiveAsync(projectId, cancellationToken);
    public Task<Result> RestoreAsync(Guid projectId, CancellationToken cancellationToken = default) => inner.RestoreAsync(projectId, cancellationToken);
    public Task<Result<IReadOnlyList<ProjectMemberResponse>>> ListMembersAsync(Guid projectId, CancellationToken cancellationToken = default) => inner.ListMembersAsync(projectId, cancellationToken);
    public Task<Result<ProjectMemberResponse>> AddMemberAsync(Guid projectId, AddProjectMemberRequest request, CancellationToken cancellationToken = default) => membership.AddAsync(projectId, request, cancellationToken);
    public Task<Result<ProjectMemberResponse>> UpdateMemberAsync(Guid projectId, Guid userId, UpdateProjectMemberRequest request, CancellationToken cancellationToken = default) => membership.UpdateAsync(projectId, userId, request, cancellationToken);
    public Task<Result> RemoveMemberAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default) => membership.RemoveAsync(projectId, userId, cancellationToken);
    public Task<Result<PagedResponse<MilestoneResponse>>> ListMilestonesAsync(Guid projectId, ProjectChildListQuery query, CancellationToken cancellationToken = default) => inner.ListMilestonesAsync(projectId, query, cancellationToken);
    public Task<Result<MilestoneResponse>> GetMilestoneAsync(Guid milestoneId, CancellationToken cancellationToken = default) => inner.GetMilestoneAsync(milestoneId, cancellationToken);
    public Task<Result<MilestoneResponse>> CreateMilestoneAsync(Guid projectId, CreateMilestoneRequest request, CancellationToken cancellationToken = default) => inner.CreateMilestoneAsync(projectId, request, cancellationToken);
    public Task<Result<MilestoneResponse>> UpdateMilestoneAsync(Guid milestoneId, UpdateMilestoneRequest request, CancellationToken cancellationToken = default) => inner.UpdateMilestoneAsync(milestoneId, request, cancellationToken);
    public Task<Result> DeleteMilestoneAsync(Guid milestoneId, CancellationToken cancellationToken = default) => inner.DeleteMilestoneAsync(milestoneId, cancellationToken);
    public Task<Result<PagedResponse<TaskItemResponse>>> ListTasksAsync(Guid projectId, TaskListQuery query, CancellationToken cancellationToken = default) => inner.ListTasksAsync(projectId, query, cancellationToken);
    public Task<Result<TaskItemResponse>> CreateTaskAsync(Guid projectId, CreateTaskItemRequest request, CancellationToken cancellationToken = default) => inner.CreateTaskAsync(projectId, request, cancellationToken);
    public Task<Result<TaskItemResponse>> GetTaskAsync(Guid taskItemId, CancellationToken cancellationToken = default) => inner.GetTaskAsync(taskItemId, cancellationToken);
    public Task<Result<TaskItemResponse>> UpdateTaskAsync(Guid taskItemId, UpdateTaskItemRequest request, CancellationToken cancellationToken = default) => inner.UpdateTaskAsync(taskItemId, request, cancellationToken);
    public Task<Result> DeleteTaskAsync(Guid taskItemId, CancellationToken cancellationToken = default) => inner.DeleteTaskAsync(taskItemId, cancellationToken);
    public Task<Result<IReadOnlyList<TaskAssignmentResponse>>> ListAssignmentsAsync(Guid taskItemId, CancellationToken cancellationToken = default) => inner.ListAssignmentsAsync(taskItemId, cancellationToken);
    public Task<Result<TaskAssignmentResponse>> AddAssignmentAsync(Guid taskItemId, AddTaskAssignmentRequest request, CancellationToken cancellationToken = default) => inner.AddAssignmentAsync(taskItemId, request, cancellationToken);
    public Task<Result<TaskAssignmentResponse>> UpdateAssignmentAsync(Guid assignmentId, UpdateTaskAssignmentRequest request, CancellationToken cancellationToken = default) => inner.UpdateAssignmentAsync(assignmentId, request, cancellationToken);
    public Task<Result> DeleteAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken = default) => inner.DeleteAssignmentAsync(assignmentId, cancellationToken);
    public Task<Result<IReadOnlyList<TaskDependencyResponse>>> ListDependenciesAsync(Guid taskItemId, CancellationToken cancellationToken = default) => inner.ListDependenciesAsync(taskItemId, cancellationToken);
    public Task<Result<TaskDependencyResponse>> AddDependencyAsync(Guid taskItemId, AddTaskDependencyRequest request, CancellationToken cancellationToken = default) => inner.AddDependencyAsync(taskItemId, request, cancellationToken);
    public Task<Result> DeleteDependencyAsync(Guid taskItemId, Guid dependencyId, long expectedVersion, CancellationToken cancellationToken = default) => inner.DeleteDependencyAsync(taskItemId, dependencyId, expectedVersion, cancellationToken);
    public Task<Result<PagedResponse<CommentResponse>>> ListCommentsAsync(CommentTargetType targetType, Guid targetId, ProjectChildListQuery query, CancellationToken cancellationToken = default) => inner.ListCommentsAsync(targetType, targetId, query, cancellationToken);
    public Task<Result<CommentResponse>> AddCommentAsync(CreateCommentRequest request, CancellationToken cancellationToken = default) => inner.AddCommentAsync(request, cancellationToken);
    public Task<Result<CommentResponse>> UpdateCommentAsync(Guid commentId, UpdateCommentRequest request, CancellationToken cancellationToken = default) => inner.UpdateCommentAsync(commentId, request, cancellationToken);
    public Task<Result> DeleteCommentAsync(Guid commentId, CancellationToken cancellationToken = default) => inner.DeleteCommentAsync(commentId, cancellationToken);
}
