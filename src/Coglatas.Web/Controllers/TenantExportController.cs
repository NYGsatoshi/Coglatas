using Coglatas.Application.Security.Redaction;
using Coglatas.Application.TenantExports;
using Coglatas.Web.Configuration;
using Coglatas.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Coglatas.Web.Controllers;

[ApiController]
[Authorize]
public sealed class TenantExportController(ITenantExportService tenantExports) : ControllerBase
{
    [HttpPost("api/tenant/export")]
    [EnableRateLimiting(HttpSecurityPolicy.TenantExportRateLimitPolicy)]
    public async Task<IActionResult> Export(TenantExportRequest request, CancellationToken cancellationToken)
    {
        var result = await tenantExports.ExportAsync(request, cancellationToken);
        if (!result.IsSuccess)
        {
            return BadRequest(CanonicalErrorEnvelope.FromSensitiveResult(
                HttpContext,
                StatusCodes.Status400BadRequest,
                result.ErrorDetail,
                result.Error,
                "TenantExportFailed"));
        }

        // The ZIP's individual rows have already passed through ExportRow
        // redaction during the service/repository build. Applying a profile to
        // this byte wrapper would not protect its serialized contents.
        var file = result.Value!;
        Response.Headers["X-Export-Job-Id"] = file.ExportJobId.ToString();
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("api/tenant/export/{exportJobId:guid}")]
    public async Task<IActionResult> GetJob(Guid exportJobId, CancellationToken cancellationToken)
    {
        var result = await tenantExports.GetJobAsync(exportJobId, cancellationToken);
        return result.IsSuccess
            ? Ok(CanonicalRedactionProjection.Apply(
                HttpContext,
                result.Value!,
                RedactionProfile.UiDetail,
                "TenantExport",
                RedactionAuthorizationState.Allowed))
            : BadRequest(CanonicalErrorEnvelope.FromSensitiveResult(
                HttpContext,
                StatusCodes.Status400BadRequest,
                result.ErrorDetail,
                result.Error,
                "TenantExportJobFailed"));
    }
}
