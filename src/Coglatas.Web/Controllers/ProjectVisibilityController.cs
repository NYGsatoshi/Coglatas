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
public sealed class ProjectVisibilityController(IProjectVisibilityService visibility) : ControllerBase
{
    [HttpPut("api/projects/{projectId:guid}/visibility")]
    public async Task<IActionResult> Update(
        Guid projectId,
        ProjectVisibilityMutationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await visibility.UpdateAsync(projectId, request, cancellationToken);
        if (result.IsSuccess)
        {
            return Ok(ApiEnvelope.Success(
                HttpContext,
                CanonicalRedactionProjection.Apply(
                    HttpContext,
                    result.Value!,
                    RedactionProfile.UiDetail,
                    "ProjectVisibility",
                    RedactionAuthorizationState.Allowed)));
        }

        return ToWpcError(result.ErrorDetail, result.Error);
    }

    private IActionResult ToWpcError(ApplicationErrorDetail? detail, string? fallbackError)
    {
        var sourceCode = detail?.Code ?? "ValidationFailed";
        var message = detail?.Message ?? fallbackError ?? "Project visibility mutation failed.";
        var status = sourceCode switch
        {
            "AuthenticationRequired" => StatusCodes.Status401Unauthorized,
            "CapabilityDenied" or "TenantMembershipRequired" => StatusCodes.Status403Forbidden,
            "NotFound" => StatusCodes.Status404NotFound,
            "ConcurrentModification" or "InvalidStateTransition" => StatusCodes.Status409Conflict,
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
