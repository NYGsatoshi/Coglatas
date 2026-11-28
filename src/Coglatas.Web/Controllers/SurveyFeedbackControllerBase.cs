using Coglatas.Application.Common;
using Coglatas.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace Coglatas.Web.Controllers;

[AqProtectedEndpoint]
[ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status401Unauthorized, "application/json")]
[ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status403Forbidden, "application/json")]
[ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status500InternalServerError, "application/json")]
public abstract class SurveyFeedbackControllerBase : ControllerBase
{
    protected IActionResult ToResponse<T>(Result<T> result, int successStatus = StatusCodes.Status200OK)
    {
        if (result.IsSuccess)
        {
            return StatusCode(successStatus, ApiEnvelope.Success(HttpContext, result.Value));
        }

        var code = result.ErrorDetail?.Code ?? "UnexpectedError";
        var status = code switch
        {
            "AuthenticationRequired" => StatusCodes.Status401Unauthorized,
            "CapabilityDenied" or "RestrictedFieldDenied" => StatusCodes.Status403Forbidden,
            "NotFound" => StatusCodes.Status404NotFound,
            "ValidationFailed" => StatusCodes.Status400BadRequest,
            "ConcurrentModification" or "InvalidStateTransition" or "RowVersionConflict" =>
                StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };
        return StatusCode(status, ApiEnvelope.Error(HttpContext, status, code,
            result.ErrorDetail?.Message ?? "The feedback request failed.",
            result.ErrorDetail?.Target));
    }
}
