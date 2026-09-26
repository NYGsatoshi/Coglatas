using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Projects;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Planning;

public sealed class PlanningService(
    IPlanningRepository planning,
    IProjectAuthorizationService projectAuthorization,
    IProjectRepository projects,
    IWorkspaceRepository workspaces,
    ITaskWorkspaceTimeZoneResolver timeZones,
    ICurrentUser currentUser,
    IClock clock) : IPlanningService
{
    public const int MaximumGanttItems = 500;

    public async Task<Result<ProjectGanttResponse>> GetGanttAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId))
        {
            return Failure<ProjectGanttResponse>("GANTT_AUTHENTICATION_REQUIRED", "Authentication is required.");
        }

        var project = await projects.GetProjectAsync(projectId, cancellationToken);
        if (project is null ||
            project.DeletedAt.HasValue ||
            project.Status is ProjectStatus.Archived or ProjectStatus.Deleted ||
            !await projectAuthorization.CanViewProject(userId, projectId, cancellationToken))
        {
            return Failure<ProjectGanttResponse>("GANTT_PROJECT_NOT_FOUND", "Project not found.");
        }

        var workspaceMember = await workspaces.GetMemberAsync(project.WorkspaceId, userId, cancellationToken);
        var projectMember = await projects.GetMemberAsync(projectId, userId, cancellationToken);
        var canManage =
            await projectAuthorization.CanManageProject(userId, projectId, cancellationToken) &&
            workspaceMember is not { Status: MembershipStatus.Active, Role: WorkspaceRole.ReadOnly };
        var canContributeToOwnedTasks =
            workspaceMember is { Status: MembershipStatus.Active } &&
            workspaceMember.Role.CanContribute() &&
            projectMember?.Role == ProjectRole.Contributor;
        var timeZone = await timeZones.ResolveAsync(project.TenantId, project.WorkspaceId, cancellationToken);
        var read = await planning.GetGanttAsync(
            projectId,
            userId,
            canManage,
            canContributeToOwnedTasks,
            timeZone.Id,
            MaximumGanttItems,
            cancellationToken);

        if (read.ItemLimitExceeded)
        {
            return Failure<ProjectGanttResponse>(
                "GANTT_ITEM_LIMIT_EXCEEDED",
                $"The Project schedule exceeds the supported limit of {MaximumGanttItems} work items.");
        }

        if (read.DependencyLimitExceeded)
        {
            return Failure<ProjectGanttResponse>(
                "GANTT_DEPENDENCY_LIMIT_EXCEEDED",
                "The Project dependency graph exceeds the supported schedule limit.");
        }

        return read.Snapshot is null
            ? Failure<ProjectGanttResponse>("GANTT_PROJECT_NOT_FOUND", "Project not found.")
            : Result<ProjectGanttResponse>.Success(read.Snapshot);
    }

    public async Task<Result<ProjectDashboardResponse>> GetDashboardAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId) || !await projectAuthorization.CanViewProject(userId, projectId, cancellationToken))
        {
            return Result<ProjectDashboardResponse>.Failure("Project not found.");
        }

        var response = await planning.GetDashboardAsync(projectId, Today, cancellationToken);
        return response is null
            ? Result<ProjectDashboardResponse>.Failure("Project not found.")
            : Result<ProjectDashboardResponse>.Success(response);
    }

    public async Task<Result<MyTasksProjectionPage>> ListMyTasksAsync(MyTasksQuery query, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId))
        {
            return Failure<MyTasksProjectionPage>("MY_TASKS_AUTHENTICATION_REQUIRED", "Authentication is required.");
        }

        if (!IsValid(query))
        {
            return Failure<MyTasksProjectionPage>("MY_TASKS_INVALID_QUERY", "One or more My Tasks query values are invalid.");
        }

        var scoped = await ResolveScopeAsync(userId, query, cancellationToken);
        if (!scoped.IsSuccess)
        {
            return Failure<MyTasksProjectionPage>(scoped.ErrorCode!, scoped.ErrorMessage!);
        }

        if (query.ProjectId.HasValue && !await planning.CanViewMyTasksProjectAsync(userId, query.ProjectId.Value, cancellationToken))
        {
            return Failure<MyTasksProjectionPage>("MY_TASKS_PROJECT_NOT_FOUND", "Project not found.");
        }

        return Result<MyTasksProjectionPage>.Success(await planning.ListMyTasksAsync(userId, scoped.Query!, clock.UtcNow, cancellationToken));
    }

    public async Task<Result<MyTasksCountsResponse>> GetMyTaskCountsAsync(MyTasksQuery query, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId))
        {
            return Failure<MyTasksCountsResponse>("MY_TASKS_AUTHENTICATION_REQUIRED", "Authentication is required.");
        }

        if (!IsValid(query))
        {
            return Failure<MyTasksCountsResponse>("MY_TASKS_INVALID_QUERY", "One or more My Tasks query values are invalid.");
        }

        var scoped = await ResolveScopeAsync(userId, query, cancellationToken);
        if (!scoped.IsSuccess)
        {
            return Failure<MyTasksCountsResponse>(scoped.ErrorCode!, scoped.ErrorMessage!);
        }

        if (query.ProjectId.HasValue && !await planning.CanViewMyTasksProjectAsync(userId, query.ProjectId.Value, cancellationToken))
        {
            return Failure<MyTasksCountsResponse>("MY_TASKS_PROJECT_NOT_FOUND", "Project not found.");
        }

        return Result<MyTasksCountsResponse>.Success(await planning.GetMyTaskCountsAsync(userId, scoped.Query!, clock.UtcNow, cancellationToken));
    }

    public async Task<Result<ProjectWorkloadResponse>> GetWorkloadAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (!TryCurrentUser(out var userId) || !await projectAuthorization.CanViewProject(userId, projectId, cancellationToken))
        {
            return Result<ProjectWorkloadResponse>.Failure("Project not found.");
        }

        var response = await planning.GetWorkloadAsync(projectId, Today, cancellationToken);
        return response is null
            ? Result<ProjectWorkloadResponse>.Failure("Project not found.")
            : Result<ProjectWorkloadResponse>.Success(response);
    }

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    private async Task<ScopeResolution> ResolveScopeAsync(Guid userId, MyTasksQuery query, CancellationToken cancellationToken)
    {
        var accessibleWorkspaceIds = await planning.ListAccessibleWorkspaceIdsAsync(userId, cancellationToken);
        if (query.Scope == MyTasksScope.AllWorkspaces)
        {
            return ScopeResolution.Success(query with { WorkspaceId = null });
        }

        if (query.WorkspaceId.HasValue)
        {
            return accessibleWorkspaceIds.Contains(query.WorkspaceId.Value)
                ? ScopeResolution.Success(query)
                : ScopeResolution.Failure(
                    "MY_TASKS_WORKSPACE_FORBIDDEN",
                    "The selected Workspace is not available.");
        }

        // There is no ambient, server-owned active-workspace value in the legacy session.
        // Selecting the sole accessible workspace is safe; multiple workspaces require the
        // client to send its explicit active Workspace selection rather than guessing.
        return accessibleWorkspaceIds.Count == 1
            ? ScopeResolution.Success(query with { WorkspaceId = accessibleWorkspaceIds[0] })
            : ScopeResolution.Failure(
                "MY_TASKS_INVALID_WORKSPACE_SCOPE",
                "An explicit active Workspace is required for the current Workspace scope.");
    }

    private static bool IsValid(MyTasksQuery query) =>
        Enum.IsDefined(query.View) &&
        Enum.IsDefined(query.Scope) &&
        (!query.StageCategory.HasValue || Enum.IsDefined(query.StageCategory.Value)) &&
        (!query.Priority.HasValue || Enum.IsDefined(query.Priority.Value)) &&
        (!query.TimeGroup.HasValue || Enum.IsDefined(query.TimeGroup.Value)) &&
        (!query.Status.HasValue || Enum.IsDefined(query.Status.Value));

    private static Result<T> Failure<T>(string code, string message) =>
        Result<T>.Failure(new ApplicationErrorDetail(code, message));

    private sealed record ScopeResolution(
        MyTasksQuery? Query,
        string? ErrorCode,
        string? ErrorMessage)
    {
        public bool IsSuccess => Query is not null;

        public static ScopeResolution Success(MyTasksQuery query) => new(query, null, null);

        public static ScopeResolution Failure(string code, string message) => new(null, code, message);
    }

    private bool TryCurrentUser(out Guid userId)
    {
        userId = currentUser.UserId ?? Guid.Empty;
        return currentUser.IsAuthenticated && currentUser.UserId.HasValue;
    }
}
