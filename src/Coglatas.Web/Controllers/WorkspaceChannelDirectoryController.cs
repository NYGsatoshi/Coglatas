using Coglatas.Application.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coglatas.Web.Controllers;

[ApiController]
[Authorize]
[Route("api/workspaces/{workspaceId:guid}/chat-channels")]
public sealed class WorkspaceChannelDirectoryController(IWorkspaceChannelDirectoryService channels) : ApiResultControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid workspaceId, [FromQuery] WorkspaceChannelDirectoryQuery query,
        CancellationToken cancellationToken) =>
        ToActionResult(await channels.ListAsync(workspaceId, query, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(Guid workspaceId, CreateWorkspaceChannelRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken) =>
        ToActionResult(await channels.CreateAsync(workspaceId, request, idempotencyKey, cancellationToken));

    [HttpPost("{conversationId:guid}/join")]
    public async Task<IActionResult> Join(Guid workspaceId, Guid conversationId, CancellationToken cancellationToken) =>
        ToActionResult(await channels.JoinAsync(workspaceId, conversationId, cancellationToken));
}
