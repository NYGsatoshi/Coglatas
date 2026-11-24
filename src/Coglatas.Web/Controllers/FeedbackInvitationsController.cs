using System.Text.Json.Serialization;
using Coglatas.Application.SurveyFeedback;
using Coglatas.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coglatas.Web.Controllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/v1/core")]
public sealed class FeedbackInvitationsController(FeedbackInvitationService invitations) : SurveyFeedbackControllerBase
{
    [HttpGet("me/feedback-invitations")]
    [ProducesResponseType<ApiSuccessEnvelope<FeedbackPage<FeedbackInvitationSummary>>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status400BadRequest, "application/json")]
    public async Task<IActionResult> List(string? cursor, string? direction, string? status, int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        ToResponse(await invitations.ListAsync(cursor, direction, status, pageSize, cancellationToken));

    [HttpGet("events/{id:guid}/feedback-invitations/approval-queue")]
    [ProducesResponseType<ApiSuccessEnvelope<FeedbackPage<FeedbackInvitationApprovalRow>>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status404NotFound, "application/json")]
    public async Task<IActionResult> ApprovalQueue(Guid id, string? cursor, int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        ToResponse(await invitations.ApprovalQueueAsync(id, cursor, pageSize, cancellationToken));

    [HttpGet("presentations/{id:guid}/feedback-invitation-recipients")]
    [ProducesResponseType<ApiSuccessEnvelope<FeedbackPage<EligibleFeedbackRecipient>>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status404NotFound, "application/json")]
    public async Task<IActionResult> EligibleRecipients(Guid id, string? cursor, int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        ToResponse(await invitations.EligibleRecipientsAsync(id, cursor, pageSize, cancellationToken));

    [HttpPost("presentations/{id:guid}/feedback-invitations")]
    [ProducesResponseType<ApiSuccessEnvelope<FeedbackInvitationDto>>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status409Conflict, "application/json")]
    public async Task<IActionResult> Issue(Guid id, IssueInvitationRequest request, CancellationToken cancellationToken) =>
        ToResponse(await invitations.IssueAsync(id, request.RecipientRef, request.RequestedActiveDurationSeconds,
            cancellationToken), StatusCodes.Status201Created);

    [HttpPost("feedback-invitations/{id:guid}/approve")]
    [ProducesResponseType<ApiSuccessEnvelope<FeedbackInvitationDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status409Conflict, "application/json")]
    public async Task<IActionResult> Approve(Guid id, ApproveInvitationRequest request, CancellationToken cancellationToken) =>
        ToResponse(await invitations.DecideAsync(id, "approve", request.ExpectedVersionNo,
            null, null, cancellationToken));

    [HttpPost("feedback-invitations/{id:guid}/accept")]
    [ProducesResponseType<ApiSuccessEnvelope<FeedbackInvitationDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status409Conflict, "application/json")]
    public async Task<IActionResult> Accept(Guid id, AcceptInvitationRequest request, CancellationToken cancellationToken) =>
        ToResponse(await invitations.DecideAsync(id, "accept", request.ExpectedVersionNo, request.Consent, null,
            cancellationToken));

    [HttpPost("feedback-invitations/{id:guid}/revoke")]
    [ProducesResponseType<ApiSuccessEnvelope<FeedbackInvitationDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status409Conflict, "application/json")]
    public async Task<IActionResult> Revoke(Guid id, EndInvitationRequest request, CancellationToken cancellationToken) =>
        ToResponse(await invitations.DecideAsync(id, "revoke", request.ExpectedVersionNo, null, request.Reason,
            cancellationToken));

    [HttpPost("feedback-invitations/{id:guid}/decline")]
    [ProducesResponseType<ApiSuccessEnvelope<FeedbackInvitationDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status409Conflict, "application/json")]
    public async Task<IActionResult> Decline(Guid id, EndInvitationRequest request, CancellationToken cancellationToken) =>
        ToResponse(await invitations.DecideAsync(id, "decline", request.ExpectedVersionNo, null, request.Reason,
            cancellationToken));

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record IssueInvitationRequest(Guid RecipientRef, int? RequestedActiveDurationSeconds);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ApproveInvitationRequest(long ExpectedVersionNo);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record AcceptInvitationRequest(long ExpectedVersionNo, bool Consent);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record EndInvitationRequest(long ExpectedVersionNo, string? Reason);
}
