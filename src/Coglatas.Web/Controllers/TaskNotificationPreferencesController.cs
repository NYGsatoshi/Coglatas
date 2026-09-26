using Coglatas.Application.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coglatas.Web.Controllers;

[ApiController]
[Authorize]
public sealed class TaskNotificationPreferencesController(
    ITaskNotificationPreferenceService preferences) : ControllerBase
{
    [HttpGet("api/me/workspaces/{workspaceId:guid}/task-notification-preferences")]
    public async Task<IActionResult> Get(Guid workspaceId, CancellationToken cancellationToken)
    {
        return ToActionResult(await preferences.GetAsync(workspaceId, cancellationToken));
    }

    [HttpPatch("api/me/workspaces/{workspaceId:guid}/task-notification-preferences")]
    public async Task<IActionResult> Update(
        Guid workspaceId,
        UpdateTaskNotificationPreferenceRequest request,
        CancellationToken cancellationToken)
    {
        return ToActionResult(await preferences.UpdateAsync(workspaceId, request, cancellationToken));
    }

    private IActionResult ToActionResult(TaskNotificationPreferenceResult result)
    {
        if (result.IsSuccess)
        {
            Response.Headers.ETag = FormatEtag(result.Value!.Version);
            return Ok(result.Value);
        }

        var detail = result.ErrorDetail!;
        var status = detail.Code switch
        {
            TaskNotificationPreferenceService.AuthenticationRequiredCode => StatusCodes.Status401Unauthorized,
            TaskNotificationPreferenceService.NotFoundCode => StatusCodes.Status404NotFound,
            TaskNotificationPreferenceService.InvalidLocalTimeCode => StatusCodes.Status400BadRequest,
            TaskNotificationPreferenceService.VersionConflictCode => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        if (result.CurrentVersion.HasValue)
        {
            Response.Headers.ETag = FormatEtag(result.CurrentVersion.Value);
        }

        return StatusCode(status, new
        {
            requestId = HttpContext.TraceIdentifier,
            error = new
            {
                code = detail.Code,
                message = detail.Message,
                target = detail.Code switch
                {
                    TaskNotificationPreferenceService.InvalidLocalTimeCode => "deadlineDigestLocalTime",
                    TaskNotificationPreferenceService.VersionConflictCode => "expectedVersion",
                    _ => null
                },
                details = Array.Empty<object>(),
                redactionApplied = detail.Code == TaskNotificationPreferenceService.NotFoundCode
            },
            currentVersion = result.CurrentVersion
        });
    }

    private static string FormatEtag(long version) => $"\"{version}\"";
}
