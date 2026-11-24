using System.Text.Json.Serialization;
using Coglatas.Application.SurveyFeedback;
using Coglatas.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coglatas.Web.Controllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/v1/core/feedback")]
public sealed class FeedbackModerationController(FeedbackModerationService moderation) : SurveyFeedbackControllerBase
{
    [HttpPost("answers/{id:guid}/reports")]
    [ProducesResponseType<ApiSuccessEnvelope<FeedbackReportDto>>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status409Conflict, "application/json")]
    public async Task<IActionResult> Report(Guid id, ReportRequest request, CancellationToken cancellationToken) =>
        ToResponse(await moderation.ReportAsync(id, request.Reason, cancellationToken), StatusCodes.Status201Created);

    [HttpPatch("answers/{id:guid}/visibility")]
    [ProducesResponseType<ApiSuccessEnvelope<FeedbackVisibilityDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status409Conflict, "application/json")]
    public async Task<IActionResult> Visibility(Guid id, VisibilityRequest request, CancellationToken cancellationToken) =>
        ToResponse(await moderation.ChangeVisibilityAsync(id, request.Action, request.Reason, request.ExpectedVersionNo,
            cancellationToken));

    [HttpGet("answers/{id:guid}/moderation-detail")]
    [ProducesResponseType<ApiSuccessEnvelope<HiddenAnswerModerationDetail>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status404NotFound, "application/json")]
    public async Task<IActionResult> HiddenDetail(Guid id, CancellationToken cancellationToken) =>
        ToResponse(await moderation.HiddenDetailAsync(id, cancellationToken));

    [HttpGet("/api/v1/core/presentations/{id:guid}/feedback-reports")]
    [ProducesResponseType<ApiSuccessEnvelope<FeedbackPage<FeedbackReportQueueRow>>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status404NotFound, "application/json")]
    public async Task<IActionResult> ReportQueue(Guid id, string? state, string? cursor, int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        ToResponse(await moderation.ReportQueueAsync(id, state, cursor, pageSize, cancellationToken));

    [HttpGet("reports/{id:guid}")]
    [ProducesResponseType<ApiSuccessEnvelope<FeedbackReportDetail>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status404NotFound, "application/json")]
    public async Task<IActionResult> ReportDetail(Guid id, CancellationToken cancellationToken) =>
        ToResponse(await moderation.ReportDetailAsync(id, cancellationToken));

    [HttpPost("reports/{id:guid}/resolve")]
    [ProducesResponseType<ApiSuccessEnvelope<FeedbackReportDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status409Conflict, "application/json")]
    public async Task<IActionResult> Resolve(Guid id, ResolveReportRequest request, CancellationToken cancellationToken) =>
        ToResponse(await moderation.ResolveAsync(id, request.Resolution, request.Reason, request.ExpectedVersionNo,
            cancellationToken));

    [HttpPatch("submissions/{id:guid}/read-state")]
    [ProducesResponseType<ApiSuccessEnvelope<FeedbackReaderState>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status409Conflict, "application/json")]
    public async Task<IActionResult> ReadState(Guid id, ReaderStateRequest request, CancellationToken cancellationToken) =>
        ToResponse(await moderation.SetReadStateAsync(id, request.ObservedRevision, request.IsRead, cancellationToken));

    [HttpPost("submissions/{id:guid}/void")]
    [ProducesResponseType<ApiSuccessEnvelope<FeedbackSubmissionDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status409Conflict, "application/json")]
    public async Task<IActionResult> Void(Guid id, VersionedReasonRequest request, CancellationToken cancellationToken) =>
        ToResponse(await moderation.VoidAsync(id, request.Reason, request.ExpectedVersionNo, cancellationToken));

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ReportRequest(string Reason);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record VisibilityRequest(long ExpectedVersionNo, string Action, string Reason);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ResolveReportRequest(long ExpectedVersionNo, string Resolution, string Reason);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ReaderStateRequest(
        [property: JsonRequired] long ObservedRevision,
        [property: JsonRequired] bool IsRead);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record VersionedReasonRequest(long ExpectedVersionNo, string Reason);
}
