using Coglatas.Application.Artifacts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coglatas.Web.Controllers;

// Deliberately excluded from Product MVC discovery and DI. Local review hosts opt in explicitly.
[NonController]
[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WorkspaceArtifactsReviewController(IWorkspaceArtifactService artifacts) : ControllerBase
{
    [HttpGet("api/workspaces/{workspaceId:guid}/artifacts")]
    public async Task<IActionResult> List(
        Guid workspaceId,
        [FromQuery] string? query = null,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? cursor = null,
        [FromQuery] string sort = "updatedAt",
        [FromQuery] string direction = "desc",
        CancellationToken cancellationToken = default)
    {
        var result = await artifacts.ListAsync(workspaceId,
            new WorkspaceArtifactListRequest(query, pageSize, cursor, sort, direction), cancellationToken);
        if (result.IsSuccess) return Ok(result.Value);
        return result.ErrorDetail?.Code == "WorkspaceNotFound"
            ? NotFound(new { error = "Workspace not found." })
            : BadRequest(new { error = result.ErrorDetail?.Code ?? "ArtifactQueryInvalid" });
    }
}
