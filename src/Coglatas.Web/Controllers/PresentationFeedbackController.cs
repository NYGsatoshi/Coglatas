using Coglatas.Application.SurveyFeedback;
using Coglatas.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coglatas.Web.Controllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/v1/core")]
public sealed class PresentationFeedbackController(SurveyFeedbackService feedback, SurveyFeedbackInboxService inbox,
    SurveyFeedbackComparisonService comparison) : SurveyFeedbackControllerBase
{
    [HttpGet("me/presentation-feedback")]
    [ProducesResponseType<ApiSuccessEnvelope<FeedbackPage<PresentationWorkspace>>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status400BadRequest, "application/json")]
    public async Task<IActionResult> ListWorkspaces(string? cursor, int pageSize = 25,
        string? relation = null, CancellationToken cancellationToken = default) =>
        ToResponse(await feedback.WorkspacesAsync(cursor, pageSize, relation, cancellationToken));

    [HttpGet("feedback/inbox")]
    [ProducesResponseType<ApiSuccessEnvelope<FeedbackInboxPage>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status404NotFound, "application/json")]
    public async Task<IActionResult> GetInbox(string? cursor, int pageSize = 25, Guid? presentationId = null,
        Guid? personRef = null, string? fieldKey = null, bool? isRead = null, bool? hasReport = null,
        string? visibility = null, string? reportStatus = null, CancellationToken cancellationToken = default) =>
        ToResponse(await inbox.ReadAsync(new FeedbackInboxFilters(presentationId, personRef, fieldKey, isRead,
            hasReport, visibility, reportStatus), cursor, pageSize, cancellationToken));

    [HttpGet("events/{id:guid}/feedback-summary")]
    [ProducesResponseType<ApiSuccessEnvelope<FeedbackComparison>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status404NotFound, "application/json")]
    public async Task<IActionResult> GetComparison(Guid id, string? comparisonRef, CancellationToken cancellationToken) =>
        ToResponse(await comparison.CompareAsync(id, comparisonRef, cancellationToken));

    [HttpGet("presentations/{id:guid}/feedback-summary")]
    [ProducesResponseType<ApiSuccessEnvelope<FeedbackSummary>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status404NotFound, "application/json")]
    public async Task<IActionResult> GetSummary(Guid id, string? fieldKey, CancellationToken cancellationToken) =>
        ToResponse(await feedback.SummaryAsync(id, fieldKey, cancellationToken));

    [HttpGet("presentations/{id:guid}/feedback")]
    [ProducesResponseType<ApiSuccessEnvelope<FeedbackPage<FeedbackRow>>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status404NotFound, "application/json")]
    public async Task<IActionResult> GetFeedback(Guid id, string? cursor, int pageSize = 25,
        string? fieldKey = null, string? visibility = null, CancellationToken cancellationToken = default) =>
        ToResponse(await feedback.FeedbackAsync(id, cursor, pageSize, fieldKey, visibility, cancellationToken));

}
