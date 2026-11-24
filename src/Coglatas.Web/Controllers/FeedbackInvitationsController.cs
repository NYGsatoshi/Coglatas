using System.Text.Json.Serialization;
using Coglatas.Application.SurveyFeedback;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coglatas.Web.Controllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/v1/core")]
public sealed class FeedbackInvitationsController(FeedbackInvitationService invitations) : SurveyFeedbackControllerBase
{
    [HttpPost("presentations/{id:guid}/feedback-invitations")]
    public async Task<IActionResult> Issue(Guid id, IssueInvitationRequest request, CancellationToken cancellationToken) =>
        ToResponse(await invitations.IssueAsync(id, request.RecipientRef, request.RequestedActiveDurationSeconds,
            cancellationToken), StatusCodes.Status201Created);

    [HttpPost("feedback-invitations/{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, ApproveInvitationRequest request, CancellationToken cancellationToken) =>
        ToResponse(await invitations.DecideAsync(id, "approve", request.ExpectedVersionNo,
            null, null, cancellationToken));

    [HttpPost("feedback-invitations/{id:guid}/accept")]
    public async Task<IActionResult> Accept(Guid id, AcceptInvitationRequest request, CancellationToken cancellationToken) =>
        ToResponse(await invitations.DecideAsync(id, "accept", request.ExpectedVersionNo, request.Consent, null,
            cancellationToken));

    [HttpPost("feedback-invitations/{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id, EndInvitationRequest request, CancellationToken cancellationToken) =>
        ToResponse(await invitations.DecideAsync(id, "revoke", request.ExpectedVersionNo, null, request.Reason,
            cancellationToken));

    [HttpPost("feedback-invitations/{id:guid}/decline")]
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
