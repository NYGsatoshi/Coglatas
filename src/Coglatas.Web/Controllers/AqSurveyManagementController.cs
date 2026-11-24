using Coglatas.Application.Common;
using Coglatas.Application.Forms.SurveyManagement;
using Coglatas.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coglatas.Web.Controllers;

[AqProtectedEndpoint]
[ApiController]
[Authorize]
[Route("api/v1/core")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AqSurveyManagementController(AqSurveyManagementService service) : ControllerBase
{
    [HttpGet("events/{eventId:guid}/presentations")]
    public async Task<IActionResult> Presentations(Guid eventId, CancellationToken ct, string? cursor = null, int pageSize = 25) =>
        Respond(await service.ListPresentationsAsync(eventId, cursor, pageSize, ct));

    [HttpPost("events/{eventId:guid}/presentations")]
    public async Task<IActionResult> CreatePresentation(Guid eventId, AqPresentationCreateRequest request, CancellationToken ct) =>
        Respond(await service.CreatePresentationAsync(eventId, request, ct), StatusCodes.Status201Created);

    [HttpGet("events/{eventId:guid}/presentations/{presentationId:guid}")]
    public async Task<IActionResult> Presentation(Guid eventId, Guid presentationId, CancellationToken ct) =>
        Respond(await service.GetPresentationAsync(eventId, presentationId, ct));

    [HttpPatch("events/{eventId:guid}/presentations/{presentationId:guid}")]
    public async Task<IActionResult> UpdatePresentation(Guid eventId, Guid presentationId,
        AqPresentationUpdateRequest request, CancellationToken ct) =>
        Respond(await service.UpdatePresentationAsync(eventId, presentationId, request, ct));

    [HttpGet("presentations/{presentationId:guid}/participants")]
    public async Task<IActionResult> Participants(Guid presentationId, CancellationToken ct) =>
        Respond(await service.GetParticipantsAsync(presentationId, ct));

    [HttpPut("presentations/{presentationId:guid}/participants")]
    public async Task<IActionResult> SetParticipants(Guid presentationId, AqParticipantUpdateRequest request, CancellationToken ct) =>
        Respond(await service.SetParticipantsAsync(presentationId, request, ct));

    [HttpGet("events/{eventId:guid}/feedback-settings")]
    public async Task<IActionResult> EventSettings(Guid eventId, CancellationToken ct) =>
        Respond(await service.GetEventSettingsAsync(eventId, ct));

    [HttpPatch("events/{eventId:guid}/feedback-settings")]
    public async Task<IActionResult> UpdateEventSettings(Guid eventId, AqSettingsUpdateRequest request, CancellationToken ct) =>
        Respond(await service.UpdateEventSettingsAsync(eventId, request, ct));

    [HttpGet("presentations/{presentationId:guid}/feedback-settings")]
    public async Task<IActionResult> PresentationResults(Guid presentationId, CancellationToken ct) =>
        Respond(await service.GetPresentationResultsAsync(presentationId, ct));

    [HttpPatch("presentations/{presentationId:guid}/feedback-settings")]
    public async Task<IActionResult> UpdatePresentationResults(Guid presentationId,
        AqPresentationResultsUpdateRequest request, CancellationToken ct) =>
        Respond(await service.UpdatePresentationResultsAsync(presentationId, request, ct));

    [HttpGet("forms")]
    public async Task<IActionResult> Forms([FromQuery] Guid eventId, CancellationToken ct, string? cursor = null, int pageSize = 25) =>
        Respond(await service.ListFormsAsync(eventId, cursor, pageSize, ct));

    [HttpPost("forms")]
    public async Task<IActionResult> CreateForm(AqCreateFormRequest request, CancellationToken ct) =>
        Respond(await service.CreateFormAsync(request, ct), StatusCodes.Status201Created);

    [HttpGet("forms/{formId:guid}/versions")]
    public async Task<IActionResult> CommonVersions(Guid formId, CancellationToken ct, string? cursor = null, int pageSize = 25) =>
        Respond(await service.ListCommonVersionsAsync(formId, cursor, pageSize, ct));

    [HttpGet("forms/{formId:guid}/versions/{versionId:guid}")]
    public async Task<IActionResult> CommonVersion(Guid formId, Guid versionId, CancellationToken ct) =>
        Respond(await service.GetCommonVersionAsync(formId, versionId, ct));

    [HttpPost("forms/{formId:guid}/versions")]
    public async Task<IActionResult> PublishCommonVersion(Guid formId, AqPublishQuestionVersionRequest request, CancellationToken ct) =>
        Respond(await service.PublishCommonVersionAsync(formId, request, ct), StatusCodes.Status201Created);

    [HttpGet("events/{eventId:guid}/survey-campaigns")]
    public async Task<IActionResult> Campaigns(Guid eventId, CancellationToken ct, string? cursor = null, int pageSize = 25) =>
        Respond(await service.ListCampaignsAsync(eventId, cursor, pageSize, ct));

    [HttpPost("events/{eventId:guid}/survey-campaigns")]
    public async Task<IActionResult> CreateCampaign(Guid eventId, AqCreateCampaignRequest request, CancellationToken ct) =>
        Respond(await service.CreateCampaignAsync(eventId, request, ct), StatusCodes.Status201Created);

    [HttpGet("survey-campaigns/{campaignId:guid}")]
    public async Task<IActionResult> Campaign(Guid campaignId, CancellationToken ct) =>
        Respond(await service.GetCampaignAsync(campaignId, ct));

    [HttpPatch("survey-campaigns/{campaignId:guid}")]
    public async Task<IActionResult> UpdateCampaign(Guid campaignId, AqUpdateCampaignRequest request, CancellationToken ct) =>
        Respond(await service.UpdateCampaignAsync(campaignId, request, ct));

    [HttpGet("survey-campaigns/{campaignId:guid}/targets")]
    public async Task<IActionResult> Targets(Guid campaignId, CancellationToken ct, string? cursor = null, int pageSize = 25) =>
        Respond(await service.ListTargetsAsync(campaignId, cursor, pageSize, ct));

    [HttpPost("survey-campaigns/{campaignId:guid}/targets")]
    public async Task<IActionResult> CreateTarget(Guid campaignId, AqCreateTargetRequest request, CancellationToken ct) =>
        Respond(await service.CreateTargetAsync(campaignId, request, ct), StatusCodes.Status201Created);

    [HttpGet("survey-targets/{targetId:guid}")]
    public async Task<IActionResult> Target(Guid targetId, CancellationToken ct) =>
        Respond(await service.GetTargetAsync(targetId, ct));

    [HttpPatch("survey-targets/{targetId:guid}")]
    public async Task<IActionResult> UpdateTarget(Guid targetId, AqUpdateTargetRequest request, CancellationToken ct) =>
        Respond(await service.UpdateTargetAsync(targetId, request, ct));

    [HttpGet("survey-targets/{targetId:guid}/question-set-versions")]
    public async Task<IActionResult> TargetVersions(Guid targetId, CancellationToken ct, string? cursor = null, int pageSize = 25) =>
        Respond(await service.ListTargetVersionsAsync(targetId, cursor, pageSize, ct));

    [HttpGet("survey-targets/{targetId:guid}/question-set-versions/{versionId:guid}")]
    public async Task<IActionResult> TargetVersion(Guid targetId, Guid versionId, CancellationToken ct) =>
        Respond(await service.GetTargetVersionAsync(targetId, versionId, ct));

    [HttpPost("survey-targets/{targetId:guid}/question-set-versions")]
    public async Task<IActionResult> PublishTargetVersion(Guid targetId, AqPublishQuestionVersionRequest request, CancellationToken ct) =>
        Respond(await service.PublishTargetVersionAsync(targetId, request, ct), StatusCodes.Status201Created);

    private IActionResult Respond<T>(Result<T> result, int successStatus = StatusCodes.Status200OK)
    {
        if (result.IsSuccess)
            return StatusCode(successStatus, ApiEnvelope.Success(HttpContext, result.Value));

        var error = result.ErrorDetail;
        var status = error?.Code switch
        {
            "AuthenticationRequired" => StatusCodes.Status401Unauthorized,
            "CapabilityDenied" or "RestrictedFieldDenied" => StatusCodes.Status403Forbidden,
            "NotFound" => StatusCodes.Status404NotFound,
            "RowVersionConflict" or "InvalidStateTransition" or "ConcurrentModification" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        return StatusCode(status, ApiEnvelope.Error(HttpContext, status,
            error?.Code ?? "ValidationFailed", error?.Message ?? "Invalid survey management request.", error?.Target));
    }
}
