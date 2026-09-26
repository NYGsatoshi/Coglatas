using Coglatas.Application.Audit;
using Coglatas.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coglatas.Web.Controllers;

[ApiController]
[Authorize]
public sealed class AuditClaimsEvidenceController(IAuditClaimsEvidenceService claimsEvidence) : ControllerBase
{
    [HttpGet("api/admin/audit/claims-evidence")]
    public async Task<IActionResult> Get(
        [FromQuery] Guid artifactVersionId,
        CancellationToken cancellationToken)
    {
        var result = await claimsEvidence.GetAsync(artifactVersionId, cancellationToken);
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        var status = result.ErrorDetail?.Code switch
        {
            "AuthenticationRequired" => StatusCodes.Status401Unauthorized,
            "CapabilityDenied" or "TenantMembershipRequired" => StatusCodes.Status403Forbidden,
            "ArtifactVersionNotFound" => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status400BadRequest
        };

        return StatusCode(status, CanonicalErrorEnvelope.FromSensitiveResult(
            HttpContext,
            status,
            result.ErrorDetail,
            result.Error,
            "AuditClaimsEvidenceFailed"));
    }
}
