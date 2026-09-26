using Coglatas.Application.Common;
using Coglatas.Application.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coglatas.Web.Controllers;

[ApiController]
[Authorize]
public sealed class ProjectKanbanController(IProjectKanbanService kanban) : ControllerBase
{
    [HttpGet("api/projects/{projectId:guid}/kanban")]
    public async Task<IActionResult> Get(
        Guid projectId,
        [FromQuery] ProjectKanbanQuery query,
        CancellationToken cancellationToken) =>
        ToActionResult(await kanban.GetAsync(projectId, query, cancellationToken));

    [HttpPut("api/projects/{projectId:guid}/kanban/config")]
    public async Task<IActionResult> UpdateConfig(
        Guid projectId,
        UpdateProjectKanbanConfigRequest request,
        CancellationToken cancellationToken) =>
        ToActionResult(await kanban.UpdateConfigAsync(projectId, request, cancellationToken));

    [HttpPost("api/tasks/{taskId:guid}/kanban-move")]
    public async Task<IActionResult> Move(
        Guid taskId,
        MoveTaskOnKanbanRequest request,
        CancellationToken cancellationToken) =>
        ToActionResult(await kanban.MoveAsync(taskId, request, cancellationToken));

    private IActionResult ToActionResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
            return Ok(result.Value);

        var parts = (result.Error ?? "KANBAN_CONFLICT|The request could not be completed.").Split('|', 2);
        var code = parts[0];
        var message = parts.Length == 2 ? parts[1] : "The request could not be completed.";
        var status = code switch
        {
            "KANBAN_NOT_FOUND" => StatusCodes.Status404NotFound,
            "KANBAN_FORBIDDEN" => StatusCodes.Status403Forbidden,
            "KANBAN_STALE_BOARD" or "KANBAN_CONFLICT" or "KANBAN_CONFIG_CONFLICT" or
                "TASK_STALE_VERSION" or "TASK_CONFLICT" => StatusCodes.Status409Conflict,
            "KANBAN_INVALID_POSITION" or "KANBAN_PROJECT_READ_ONLY" or "KANBAN_BOARD_TOO_LARGE" or
                "TASK_TRANSITION_GUARD_FAILED" or "TASK_ASSIGNEE_REQUIRED" or "TASK_REVIEW_REQUIRED" or
                "TASK_BLOCK_REASON_REQUIRED" or "TASK_CANCEL_REASON_REQUIRED" => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status400BadRequest
        };
        return StatusCode(status, new
        {
            requestId = HttpContext.TraceIdentifier,
            error = new
            {
                code,
                message,
                target = (string?)null,
                details = Array.Empty<object>(),
                redactionApplied = false
            }
        });
    }
}
