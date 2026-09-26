using Coglatas.Application.Common;
using Coglatas.Application.Projects;
using Coglatas.Application.Security.Redaction;
using Coglatas.Web.Models;
using Coglatas.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coglatas.Web.Controllers;

/// <summary>
/// Canonical Task-create surface for the routed Project Task-create flow.
/// The legacy <c>/api/projects/{projectId}/tasks</c> command remains intact
/// for compatibility and is intentionally not used by this controller.
/// </summary>
[ApiController]
[Authorize]
public sealed class ProjectTaskCreateController(ICanonicalTaskCreateService taskCreate) : ControllerBase
{
    [HttpGet("api/projects/{projectId:guid}/tasks/create-options")]
    public async Task<IActionResult> GetCreateOptions(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var result = await taskCreate.GetCreateOptionsAsync(projectId, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiEnvelope.Success(
                HttpContext,
                CanonicalRedactionProjection.Apply(
                    HttpContext,
                    result.Value!,
                    RedactionProfile.UiDetail,
                    "TaskCreateOptions",
                    RedactionAuthorizationState.Allowed)))
            : ToCanonicalError(result.ErrorDetail, result.Error, "Task create options could not be evaluated.");
    }

    [HttpPost("api/projects/{projectId:guid}/tasks/create")]
    public async Task<IActionResult> Create(
        Guid projectId,
        CanonicalCreateTaskRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await taskCreate.CreateAsync(projectId, request, idempotencyKey, cancellationToken);
        if (!result.IsSuccess)
        {
            return ToCanonicalError(result.ErrorDetail, result.Error, "Task creation failed.");
        }

        var value = result.Value!;
        return Created(
            $"/api/tasks/{value.TaskId}",
            ApiEnvelope.Success(
                HttpContext,
                CanonicalRedactionProjection.Apply(
                    HttpContext,
                    value,
                    RedactionProfile.UiDetail,
                    "TaskCreate",
                    RedactionAuthorizationState.Allowed)));
    }

    private IActionResult ToCanonicalError(
        ApplicationErrorDetail? detail,
        string? fallbackError,
        string fallbackMessage)
    {
        var sourceCode = detail?.Code ?? "ValidationFailed";
        var message = detail?.Message ?? fallbackError ?? fallbackMessage;
        var status = sourceCode switch
        {
            "AuthenticationRequired" => StatusCodes.Status401Unauthorized,
            "CapabilityDenied" or "TenantMembershipRequired" => StatusCodes.Status403Forbidden,
            "NotFound" => StatusCodes.Status404NotFound,
            "IdempotencyConflict" or "ConcurrentModification" or "InvalidStateTransition" => StatusCodes.Status409Conflict,
            "DependencyUnavailable" => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };
        var payload = ApiEnvelope.Error(
            HttpContext,
            status,
            sourceCode,
            message,
            detail?.Target,
            CanonicalErrorExposurePolicy.IsSensitive(sourceCode));

        return status switch
        {
            StatusCodes.Status401Unauthorized => Unauthorized(payload),
            StatusCodes.Status404NotFound => NotFound(payload),
            StatusCodes.Status409Conflict => Conflict(payload),
            StatusCodes.Status400BadRequest => BadRequest(payload),
            _ => StatusCode(status, payload)
        };
    }
}
