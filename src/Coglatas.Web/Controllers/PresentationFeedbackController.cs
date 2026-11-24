using Coglatas.Application.SurveyFeedback;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coglatas.Web.Controllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/v1/core")]
public sealed class PresentationFeedbackController(SurveyFeedbackService feedback, SurveyFeedbackInboxService inbox) : SurveyFeedbackControllerBase
{
    [HttpGet("me/presentation-feedback")]
    public async Task<IActionResult> ListWorkspaces(string? cursor, int pageSize = 25,
        string? relation = null, CancellationToken cancellationToken = default) =>
        ToResponse(await feedback.WorkspacesAsync(cursor, pageSize, relation, cancellationToken));

    [HttpGet("feedback/inbox")]
    public async Task<IActionResult> GetInbox(string? cursor, int pageSize = 25, Guid? presentationId = null,
        Guid? personRef = null, string? fieldKey = null, bool? isRead = null, bool? hasReport = null,
        string? visibility = null, string? reportStatus = null, CancellationToken cancellationToken = default) =>
        ToResponse(await inbox.ReadAsync(new FeedbackInboxFilters(presentationId, personRef, fieldKey, isRead,
            hasReport, visibility, reportStatus), cursor, pageSize, cancellationToken));

    [HttpGet("events/{id:guid}/feedback-summary")]
    public async Task<IActionResult> GetComparison(Guid id, CancellationToken cancellationToken) =>
        ToResponse(await feedback.ComparisonAsync(id, cancellationToken));

    [HttpGet("presentations/{id:guid}/feedback-summary")]
    public async Task<IActionResult> GetSummary(Guid id, string? fieldKey, CancellationToken cancellationToken) =>
        ToResponse(await feedback.SummaryAsync(id, fieldKey, cancellationToken));

    [HttpGet("presentations/{id:guid}/feedback")]
    public async Task<IActionResult> GetFeedback(Guid id, string? cursor, int pageSize = 25,
        string? fieldKey = null, string? visibility = null, CancellationToken cancellationToken = default) =>
        ToResponse(await feedback.FeedbackAsync(id, cursor, pageSize, fieldKey, visibility, cancellationToken));

}
