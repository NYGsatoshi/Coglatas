using Coglatas.Application.Common;
using Coglatas.Application.Projects;
using Coglatas.Application.Security.Redaction;
using Coglatas.Web.Models;
using Coglatas.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coglatas.Web.Controllers;

[ApiController]
[Authorize]
public sealed class WorkspaceProjectsController(ICanonicalProjectCreateService projectCreate) : ControllerBase
{
    [HttpGet("api/workspaces/{workspaceId:guid}/projects/create-options")]
    public async Task<IActionResult> GetCreateOptions(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var result = await projectCreate.GetCreateOptionsAsync(workspaceId, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiEnvelope.Success(
                HttpContext,
                CanonicalRedactionProjection.Apply(
                    HttpContext,
                    result.Value!,
                    RedactionProfile.UiDetail,
                    "ProjectCreateOptions",
                    RedactionAuthorizationState.Allowed)))
            : ToWpcError(result.ErrorDetail, result.Error, "Project create options could not be evaluated.");
    }

    [HttpPost("api/workspaces/{workspaceId:guid}/projects")]
    public async Task<IActionResult> Create(
        Guid workspaceId,
        CanonicalCreateProjectRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await projectCreate.CreateAsync(workspaceId, request, idempotencyKey, cancellationToken);
        if (result.IsSuccess)
        {
            var value = result.Value!;
            return Created(
                $"/api/projects/{value.Id}",
                ApiEnvelope.Success(
                    HttpContext,
                    CanonicalRedactionProjection.Apply(
                        HttpContext,
                        value,
                        RedactionProfile.UiDetail,
                        "ProjectCreate",
                        RedactionAuthorizationState.Allowed)));
        }

        return ToWpcError(result.ErrorDetail, result.Error, "Project creation failed.");
    }

    private IActionResult ToWpcError(
        ApplicationErrorDetail? detail,
        string? fallbackError,
        string fallbackMessage)
    {
        var sourceCode = detail?.Code ?? "ValidationFailed";
        var code = sourceCode;
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
            code,
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
