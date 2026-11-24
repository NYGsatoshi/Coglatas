using System.Text.Json.Serialization;
using Coglatas.Application.SurveyFeedback;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coglatas.Web.Controllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/v1/core/presentations/{id:guid}")]
public sealed class FeedbackNotesController(FeedbackNotesService notes) : SurveyFeedbackControllerBase
{
    [HttpPost("private-notes")]
    public async Task<IActionResult> CreatePrivate(Guid id, NoteRequest request, CancellationToken cancellationToken) =>
        ToResponse(await notes.CreatePrivateAsync(id, request.Body, cancellationToken), StatusCodes.Status201Created);

    [HttpGet("private-notes")]
    public async Task<IActionResult> ReadPrivate(Guid id, string? cursor, int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        ToResponse(await notes.ReadPrivateAsync(id, cursor, pageSize, cancellationToken));

    [HttpPost("shared-feedback")]
    public async Task<IActionResult> CreateShared(Guid id, NoteRequest request, CancellationToken cancellationToken) =>
        ToResponse(await notes.CreateSharedAsync(id, request.Body, cancellationToken), StatusCodes.Status201Created);

    [HttpGet("shared-feedback")]
    public async Task<IActionResult> ReadShared(Guid id, string? cursor, int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        ToResponse(await notes.ReadSharedAsync(id, cursor, pageSize, cancellationToken));

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record NoteRequest(string Body);
}
