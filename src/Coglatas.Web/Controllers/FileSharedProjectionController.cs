using Coglatas.Application.Files;
using Coglatas.Application.Security.Redaction;
using Coglatas.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coglatas.Web.Controllers;

/// <summary>Private implementation-review candidate; default MVC discovery must not activate it.</summary>
[NonController]
[ApiController]
[Authorize]
public sealed class FileSharedProjectionController(IFileSharedProjectionService projection) : ControllerBase
{
    [HttpGet("api/files/shared")]
    public async Task<IActionResult> List(
        [FromQuery] Guid workspaceId,
        [FromQuery] string? direction,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Append("Vary", "Cookie");
        var result = await projection.ListAsync(workspaceId, direction, page, pageSize, cancellationToken);
        if (result.IsSuccess)
        {
            return Ok(CanonicalRedactionProjection.Apply(HttpContext, result.Value!, RedactionProfile.FileMetadata,
                "SharedFileList", RedactionAuthorizationState.Allowed));
        }
        var status = result.ErrorDetail?.Code switch
        {
            "AuthenticationRequired" => StatusCodes.Status401Unauthorized,
            "SharedFileScopeUnavailable" => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status400BadRequest
        };
        return StatusCode(status, CanonicalErrorEnvelope.FromResult(HttpContext, status,
            result.ErrorDetail, result.Error, "SharedFileListFailed"));
    }
}
