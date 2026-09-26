using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Files;
using Coglatas.Application.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coglatas.Web.Controllers;

[ApiController]
[Authorize]
public sealed class TaskExecutionController(
    ITaskExecutionScopeService executionScopes,
    ITaskExecutionRuntime? runtime = null,
    ICurrentTenant? currentTenant = null,
    ITaskExecutionResultService? executionResults = null,
    ITaskExecutionScopeRepository? executionScopeRepository = null,
    IFileAuthorizationService? fileAuthorization = null,
    ICurrentUser? currentUser = null,
    ITaskExecutionInterventionService? interventions = null) : ControllerBase
{
    [HttpGet("api/projects/{projectId:guid}/execution-scope")]
    public async Task<IActionResult> GetProjectScope(Guid projectId, CancellationToken cancellationToken)
    {
        var result = await executionScopes.GetProjectScopeAsync(projectId, cancellationToken);
        if (!result.IsSuccess || result.Value is not { } response)
            return ToActionResult(result);

        return Ok(await RedactUnauthorizedProjectFileRulesAsync(projectId, response, cancellationToken));
    }

    [HttpPut("api/projects/{projectId:guid}/execution-scope")]
    public async Task<IActionResult> UpdateProjectScope(Guid projectId, UpdateProjectExecutionScopeRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await executionScopes.UpdateProjectScopeAsync(projectId, request, cancellationToken));

    [HttpGet("api/tasks/{taskItemId:guid}/execution-scope")]
    public async Task<IActionResult> GetTaskScope(Guid taskItemId, CancellationToken cancellationToken) =>
        ToActionResult(await executionScopes.GetTaskScopeAsync(taskItemId, cancellationToken));

    [HttpPut("api/tasks/{taskItemId:guid}/execution-scope-override")]
    public async Task<IActionResult> UpdateTaskOverride(Guid taskItemId, UpdateTaskExecutionScopeOverrideRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await executionScopes.UpdateTaskOverrideAsync(taskItemId, request, cancellationToken));

    [HttpDelete("api/tasks/{taskItemId:guid}/execution-scope-override")]
    public async Task<IActionResult> ClearTaskOverride(Guid taskItemId, ClearTaskExecutionScopeOverrideRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await executionScopes.ClearTaskOverrideAsync(taskItemId, request, cancellationToken));

    [HttpGet("api/tasks/{taskItemId:guid}/execution-result")]
    public async Task<IActionResult> GetLatestResult(Guid taskItemId, CancellationToken cancellationToken) =>
        executionResults is null ? ResultUnavailable() : ToActionResult(await executionResults.GetLatestAsync(taskItemId, cancellationToken));

    [HttpGet("api/tasks/{taskItemId:guid}/execution-runs/{runId:guid}/result")]
    public async Task<IActionResult> GetResult(Guid taskItemId, Guid runId, CancellationToken cancellationToken) =>
        executionResults is null ? ResultUnavailable() : ToActionResult(await executionResults.GetAsync(taskItemId, runId, cancellationToken));

    [HttpPost("api/tasks/{taskItemId:guid}/execution-runs")]
    public async Task<IActionResult> RequestRun(
        Guid taskItemId,
        RequestTaskExecutionRunRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        _ = request;
        var result = await executionScopes.RequestRunAsync(taskItemId, idempotencyKey, cancellationToken);
        if (!result.IsSuccess || result.Value is not { } accepted)
            return ToActionResult(result);

        var response = accepted;
        if (CanExecuteRuntime())
        {
            await runtime!.ExecuteAsync(new TaskExecutionRuntimeHandle(
                accepted.Id,
                currentTenant!.TenantId,
                accepted.RuntimeContractVersion), CancellationToken.None);

            var refreshed = await executionScopes.GetTaskScopeAsync(taskItemId, CancellationToken.None);
            if (refreshed.IsSuccess && refreshed.Value is { LatestRun: { } latest } && latest.Id == accepted.Id)
                response = latest;
        }

        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPost("api/tasks/{taskItemId:guid}/execution-runs/{runId:guid}/stop")]
    public async Task<IActionResult> StopRun(
        Guid taskItemId,
        Guid runId,
        StopTaskExecutionRunRequest request,
        CancellationToken cancellationToken)
    {
        _ = request;
        if (interventions is null)
            return InterventionUnavailable();

        return ToActionResult(await interventions.StopAsync(taskItemId, runId, cancellationToken));
    }

    [HttpPost("api/tasks/{taskItemId:guid}/execution-runs/{runId:guid}/correct-direction")]
    public async Task<IActionResult> CorrectDirection(
        Guid taskItemId,
        Guid runId,
        CorrectTaskExecutionDirectionRequest request,
        CancellationToken cancellationToken)
    {
        _ = request;
        if (interventions is null)
            return InterventionUnavailable();

        var result = await interventions.CorrectDirectionAsync(taskItemId, runId, cancellationToken);
        if (!result.IsSuccess || result.Value is not { ResumedRun: { } resumed } response)
            return ToActionResult(result);

        if (CanExecuteRuntime())
        {
            await runtime!.ExecuteAsync(new TaskExecutionRuntimeHandle(
                resumed.Id,
                currentTenant!.TenantId,
                resumed.RuntimeContractVersion), CancellationToken.None);

            var refreshed = await executionScopes.GetTaskScopeAsync(taskItemId, CancellationToken.None);
            if (refreshed.IsSuccess && refreshed.Value is { LatestRun: { } latest } && latest.Id == resumed.Id)
                response = response with { ResumedRun = latest };
        }

        return Ok(response);
    }

    private bool CanExecuteRuntime() =>
        runtime is not null &&
        currentTenant is { IsAvailable: true, IsPlatformScope: false } &&
        currentTenant.TenantId != Guid.Empty;

    private async Task<ProjectExecutionScopeResponse> RedactUnauthorizedProjectFileRulesAsync(
        Guid projectId,
        ProjectExecutionScopeResponse response,
        CancellationToken cancellationToken)
    {
        var policy = response.Policy.PolicyV2;
        if (policy is null || !policy.Items.Any(rule => rule.Kind == TaskExecutionSourceKind.ProjectFile))
            return response;

        if (executionScopeRepository is null || fileAuthorization is null ||
            currentUser is not { IsAuthenticated: true, UserId: { } actor } || actor == Guid.Empty)
        {
            return RedactProjectFileRules(response, policy, [], canManage: false);
        }

        var visibleSourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attachment in await executionScopeRepository.ListProjectSourceAttachmentsAsync(projectId, cancellationToken))
        {
            if (attachment.FileObject is not { } fileObject)
                continue;
            if (!await fileAuthorization.CanViewAttachment(actor, attachment, cancellationToken))
                continue;

            visibleSourceIds.Add(TaskExecutionSourcePolicyV2.ProjectFileSourceId(fileObject.Id));
        }

        var allProjectFileRulesVisible = policy.Items
            .Where(rule => rule.Kind == TaskExecutionSourceKind.ProjectFile)
            .All(rule => visibleSourceIds.Contains(rule.SourceId));

        return RedactProjectFileRules(
            response,
            policy,
            visibleSourceIds,
            canManage: response.CanManage && allProjectFileRulesVisible);
    }

    private static ProjectExecutionScopeResponse RedactProjectFileRules(
        ProjectExecutionScopeResponse response,
        TaskExecutionSourcePolicyV2 policy,
        IReadOnlyCollection<string> visibleSourceIds,
        bool canManage)
    {
        var visible = visibleSourceIds.ToHashSet(StringComparer.Ordinal);
        var redactedPolicy = policy with
        {
            Items = policy.Items
                .Where(rule => rule.Kind != TaskExecutionSourceKind.ProjectFile || visible.Contains(rule.SourceId))
                .ToList()
        };

        return response with
        {
            CanManage = canManage,
            Policy = response.Policy with
            {
                WebEnabled = redactedPolicy.WebEnabled,
                ProjectFilesEnabled = redactedPolicy.ProjectFilesEnabled,
                PolicyV2 = redactedPolicy
            }
        };
    }

    private IActionResult ResultUnavailable() =>
        ToActionResult(Result<TaskExecutionResultResponse>.Failure(new ApplicationErrorDetail(
            "TASK_EXECUTION_RESULT_UNAVAILABLE",
            "The execution result is temporarily unavailable.")));

    private IActionResult InterventionUnavailable() =>
        ToActionResult(Result<TaskExecutionInterventionResponse>.Failure(new ApplicationErrorDetail(
            "TASK_EXECUTION_INTERVENTION_UNAVAILABLE",
            "Task execution intervention is temporarily unavailable.")));

    private IActionResult ToActionResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
            return Ok(result.Value);

        var detail = result.ErrorDetail;
        var code = detail?.Code ?? "TASK_EXECUTION_REQUEST_FAILED";
        var message = detail?.Message ?? "The execution scope request could not be completed.";
        var status = code switch
        {
            "TASK_EXECUTION_NOT_FOUND" or "TASK_EXECUTION_RESULT_NOT_FOUND" => StatusCodes.Status404NotFound,
            "TASK_EXECUTION_STALE_VERSION" or
            "TASK_EXECUTION_IDEMPOTENCY_CONFLICT" or
            "TASK_EXECUTION_INTERVENTION_NOT_AVAILABLE" => StatusCodes.Status409Conflict,
            "TASK_EXECUTION_PERSISTENCE_UNAVAILABLE" or
            "TASK_EXECUTION_REPLAY_UNAVAILABLE" or
            "TASK_EXECUTION_RESULT_UNAVAILABLE" or
            "TASK_EXECUTION_INTERVENTION_UNAVAILABLE" => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };

        return StatusCode(status, new
        {
            requestId = HttpContext.TraceIdentifier,
            error = new
            {
                code,
                message,
                target = detail?.Target,
                details = Array.Empty<object>(),
                redactionApplied = code is "TASK_EXECUTION_NOT_FOUND" or "TASK_EXECUTION_RESULT_NOT_FOUND"
            }
        });
    }
}
