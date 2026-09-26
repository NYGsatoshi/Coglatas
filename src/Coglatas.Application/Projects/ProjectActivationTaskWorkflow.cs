using Coglatas.Application.Common;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Projects;

public sealed record ProjectActivationTaskWorkflowStage(
    string Name,
    TaskStageCategory Category,
    long SortKey,
    int? WipWarningLimit,
    bool IsInitialStage,
    bool IsTerminalStage);

public sealed record ProjectActivationTaskWorkflow(
    string SourceIdentity,
    string DisplayName,
    bool ReviewEnforcementEnabled,
    IReadOnlyList<ProjectActivationTaskWorkflowStage> Stages);

/// <summary>
/// Abstraction for configured workflow sources. The canonical specifications
/// define the precedence, but the current persistence model has no normative
/// Workspace/Tenant template storage shape. Implementations may supply those
/// sources without changing activation command semantics.
/// </summary>
public interface IConfiguredProjectTaskWorkflowSource
{
    Task<ProjectActivationTaskWorkflow?> FindWorkspaceTemplateAsync(
        Project project,
        CancellationToken cancellationToken = default);

    Task<ProjectActivationTaskWorkflow?> FindTenantDefaultAsync(
        Project project,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Current adapter for repositories that have no configured template storage.
/// It deliberately returns no configured source so resolution reaches the
/// immutable canonical fallback rather than guessing configuration state.
/// </summary>
public sealed class NoConfiguredProjectTaskWorkflowSource : IConfiguredProjectTaskWorkflowSource
{
    public Task<ProjectActivationTaskWorkflow?> FindWorkspaceTemplateAsync(
        Project project,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ProjectActivationTaskWorkflow?>(null);

    public Task<ProjectActivationTaskWorkflow?> FindTenantDefaultAsync(
        Project project,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ProjectActivationTaskWorkflow?>(null);
}

public interface IProjectTaskWorkflowResolver
{
    Task<Result<ProjectActivationTaskWorkflow>> ResolveAsync(
        Project project,
        CancellationToken cancellationToken = default);
}

public sealed class ProjectTaskWorkflowResolver(
    IConfiguredProjectTaskWorkflowSource configuredSource) : IProjectTaskWorkflowResolver
{
    public const string CanonicalFallbackIdentity = "CanonicalTaskWorkflow/v1";

    public async Task<Result<ProjectActivationTaskWorkflow>> ResolveAsync(
        Project project,
        CancellationToken cancellationToken = default)
    {
        if (project.Id == Guid.Empty || project.TenantId == Guid.Empty || project.WorkspaceId == Guid.Empty)
        {
            return Failure("InvalidActivationScope", "Project workflow resolution scope is invalid.");
        }

        var workspaceTemplate = await configuredSource.FindWorkspaceTemplateAsync(project, cancellationToken);
        if (workspaceTemplate is not null)
        {
            return ValidateResolved(workspaceTemplate, "WorkspaceTaskWorkflowTemplate");
        }

        var tenantDefault = await configuredSource.FindTenantDefaultAsync(project, cancellationToken);
        if (tenantDefault is not null)
        {
            return ValidateResolved(tenantDefault, "TenantTaskWorkflowDefault");
        }

        return Result<ProjectActivationTaskWorkflow>.Success(CanonicalFallback());
    }

    private static Result<ProjectActivationTaskWorkflow> ValidateResolved(
        ProjectActivationTaskWorkflow workflow,
        string sourceName)
    {
        if (string.IsNullOrWhiteSpace(workflow.SourceIdentity) ||
            string.IsNullOrWhiteSpace(workflow.DisplayName) ||
            !IsCompatibleStages(workflow.Stages))
        {
            return Failure(
                "InvalidTaskWorkflow",
                $"Configured {sourceName} is invalid or incompatible with activation.");
        }

        return Result<ProjectActivationTaskWorkflow>.Success(workflow);
    }

    internal static bool IsCompatibleStages(IReadOnlyCollection<ProjectActivationTaskWorkflowStage> stages)
    {
        if (stages.Count == 0 ||
            stages.Count(stage => stage.IsInitialStage) != 1 ||
            !stages.Any(stage => stage.IsTerminalStage) ||
            stages.Any(stage => string.IsNullOrWhiteSpace(stage.Name)) ||
            stages.Select(stage => stage.SortKey).Distinct().Count() != stages.Count ||
            stages.Select(stage => stage.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != stages.Count)
        {
            return false;
        }

        return stages.All(stage => Enum.IsDefined(typeof(TaskStageCategory), stage.Category));
    }

    private static ProjectActivationTaskWorkflow CanonicalFallback() => new(
        CanonicalFallbackIdentity,
        "Default",
        ReviewEnforcementEnabled: true,
        [
            new("Backlog", TaskStageCategory.Backlog, 1000L, null, true, false),
            new("Todo", TaskStageCategory.Todo, 2000L, null, false, false),
            new("In Progress", TaskStageCategory.InProgress, 3000L, null, false, false),
            new("Review", TaskStageCategory.Review, 4000L, null, false, false),
            new("Done", TaskStageCategory.Done, 5000L, null, false, true),
            new("Cancelled", TaskStageCategory.Cancelled, 6000L, null, false, true)
        ]);

    private static Result<ProjectActivationTaskWorkflow> Failure(string code, string message) =>
        Result<ProjectActivationTaskWorkflow>.Failure(new ApplicationErrorDetail(code, message));
}

public interface IProjectActivationWorkflowStore
{
    Task<TaskWorkflowDefinition?> GetDefinitionAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskWorkflowStage>> ListStagesAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task AddDefinitionAsync(
        TaskWorkflowDefinition definition,
        CancellationToken cancellationToken = default);

    Task AddStageAsync(
        TaskWorkflowStage stage,
        CancellationToken cancellationToken = default);
}

public interface IProjectTaskWorkflowActivationProvisioner
{
    Task<Result> StageAsync(
        Project project,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reuses a compatible existing Project workflow. Only when no workflow exists
/// does activation resolve and stage a new one. This keeps legacy workflows
/// intact and prevents silent regeneration.
/// </summary>
public sealed class ProjectTaskWorkflowActivationProvisioner(
    IProjectActivationWorkflowStore store,
    IProjectTaskWorkflowResolver resolver) : IProjectTaskWorkflowActivationProvisioner
{
    public async Task<Result> StageAsync(
        Project project,
        CancellationToken cancellationToken = default)
    {
        var existing = await store.GetDefinitionAsync(project.Id, cancellationToken);
        if (existing is not null)
        {
            var existingStages = await store.ListStagesAsync(project.Id, cancellationToken);
            if (!IsCompatibleExisting(project, existing, existingStages))
            {
                return Failure(
                    "InvalidTaskWorkflow",
                    "Existing Project Task workflow is invalid or incompatible with activation.");
            }

            return Result.Success();
        }

        var resolution = await resolver.ResolveAsync(project, cancellationToken);
        if (!resolution.IsSuccess || resolution.Value is null)
        {
            return resolution.ErrorDetail is not null
                ? Result.Failure(resolution.ErrorDetail)
                : Result.Failure(resolution.Error ?? "Task workflow resolution failed.");
        }

        var workflow = resolution.Value;
        var definition = new TaskWorkflowDefinition
        {
            TenantId = project.TenantId,
            WorkspaceId = project.WorkspaceId,
            ProjectId = project.Id,
            Name = workflow.DisplayName,
            ReviewEnforcementEnabled = workflow.ReviewEnforcementEnabled,
            VersionNo = 1
        };
        await store.AddDefinitionAsync(definition, cancellationToken);

        foreach (var stage in workflow.Stages.OrderBy(stage => stage.SortKey))
        {
            await store.AddStageAsync(new TaskWorkflowStage
            {
                TenantId = project.TenantId,
                WorkspaceId = project.WorkspaceId,
                ProjectId = project.Id,
                DefinitionId = definition.Id,
                Name = stage.Name,
                InternalCategory = stage.Category,
                SortKey = stage.SortKey,
                WipWarningLimit = stage.WipWarningLimit,
                IsInitialStage = stage.IsInitialStage,
                IsTerminalStage = stage.IsTerminalStage,
                VersionNo = 1
            }, cancellationToken);
        }

        return Result.Success();
    }

    private static bool IsCompatibleExisting(
        Project project,
        TaskWorkflowDefinition definition,
        IReadOnlyCollection<TaskWorkflowStage> stages)
    {
        if (definition.TenantId != project.TenantId ||
            definition.WorkspaceId != project.WorkspaceId ||
            definition.ProjectId != project.Id ||
            stages.Any(stage =>
                stage.TenantId != project.TenantId ||
                stage.WorkspaceId != project.WorkspaceId ||
                stage.ProjectId != project.Id ||
                stage.DefinitionId != definition.Id))
        {
            return false;
        }

        return ProjectTaskWorkflowResolver.IsCompatibleStages(
            stages.Select(stage => new ProjectActivationTaskWorkflowStage(
                stage.Name,
                stage.InternalCategory,
                stage.SortKey,
                stage.WipWarningLimit,
                stage.IsInitialStage,
                stage.IsTerminalStage)).ToArray());
    }

    private static Result Failure(string code, string message) =>
        Result.Failure(new ApplicationErrorDetail(code, message));
}
