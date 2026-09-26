using Coglatas.Application.Common;
using Coglatas.Application.Security.Redaction;
using Coglatas.Application.Tenancy;
using Coglatas.Web.Models;
using Coglatas.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coglatas.Web.Controllers;

[ApiController]
[Authorize]
[Route("api/tenant/capability-grants")]
public sealed class CapabilityGrantsController(ICapabilityGrantService capabilityGrants) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var result = await capabilityGrants.ListAsync(cancellationToken);
        return result.IsSuccess
            ? Ok(ApiEnvelope.Success(
                HttpContext,
                CanonicalRedactionProjection.Apply(
                    HttpContext,
                    result.Value!,
                    RedactionProfile.UiList,
                    "CapabilityGrants",
                    RedactionAuthorizationState.Allowed)))
            : ToWpcError(result.ErrorDetail, result.Error, "Capability grants could not be listed.");
    }

    [HttpPost]
    public async Task<IActionResult> Grant(
        GrantCapabilityRequest request,
        CancellationToken cancellationToken)
    {
        var result = await capabilityGrants.GrantAsync(request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiEnvelope.Success(
                HttpContext,
                CanonicalRedactionProjection.Apply(
                    HttpContext,
                    result.Value!,
                    RedactionProfile.UiDetail,
                    "CapabilityGrantUpdate",
                    RedactionAuthorizationState.Allowed)))
            : ToWpcError(result.ErrorDetail, result.Error, "Capability grant update failed.");
    }

    [HttpPost("{grantId:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid grantId, CancellationToken cancellationToken)
    {
        var result = await capabilityGrants.RevokeAsync(grantId, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiEnvelope.Success(
                HttpContext,
                CanonicalRedactionProjection.Apply(
                    HttpContext,
                    result.Value!,
                    RedactionProfile.UiDetail,
                    "CapabilityGrantRevoke",
                    RedactionAuthorizationState.Allowed)))
            : ToWpcError(result.ErrorDetail, result.Error, "Capability grant revocation failed.");
    }

    private IActionResult ToWpcError(
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
            "ConcurrentModification" => StatusCodes.Status409Conflict,
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
        return StatusCode(status, payload);
    }
}
