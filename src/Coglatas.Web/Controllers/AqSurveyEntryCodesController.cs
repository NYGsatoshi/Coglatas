using Coglatas.Application.Common;
using Coglatas.Application.Forms.SurveyManagement;
using Coglatas.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coglatas.Web.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/core")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AqSurveyEntryCodesController(AqSurveyManagementService service, IConfiguration configuration) : ControllerBase
{
    [HttpPost("events/{eventId:guid}/program-code/rotate")]
    public async Task<IActionResult> RotateProgram(Guid eventId, AqChangeCodeRequest request, CancellationToken ct) =>
        Respond(await service.ChangeProgramCodeAsync(eventId, request, rotate: true, ct));

    [HttpPost("events/{eventId:guid}/program-code/revoke")]
    public async Task<IActionResult> RevokeProgram(Guid eventId, AqChangeCodeRequest request, CancellationToken ct) =>
        Respond(await service.ChangeProgramCodeAsync(eventId, request, rotate: false, ct));

    [HttpPost("survey-targets/{targetId:guid}/public-code/rotate")]
    public async Task<IActionResult> RotateTarget(Guid targetId, AqChangeCodeRequest request, CancellationToken ct) =>
        Respond(await service.ChangeTargetCodeAsync(targetId, request, rotate: true, ct));

    [HttpPost("survey-targets/{targetId:guid}/public-code/revoke")]
    public async Task<IActionResult> RevokeTarget(Guid targetId, AqChangeCodeRequest request, CancellationToken ct) =>
        Respond(await service.ChangeTargetCodeAsync(targetId, request, rotate: false, ct));

    [HttpGet("survey-campaigns/{campaignId:guid}/qr-export")]
    public async Task<IActionResult> ExportQr(Guid campaignId, string format, Guid? targetId, CancellationToken ct)
    {
        if (format is not ("png" or "svg" or "pdf") || (format != "pdf" && !targetId.HasValue))
            return Respond(Result<byte[]>.Failure(new ApplicationErrorDetail("ValidationFailed", "Select a QR format and any required target.")));
        var selected = await service.ListQrEntriesAsync(campaignId, targetId, ct);
        if (!selected.IsSuccess)
            return Respond(selected);
        if (!TryGetOrigin(out var origin))
            return Respond(Result<byte[]>.Failure(new ApplicationErrorDetail("InvalidStateTransition", "A trusted public application origin is required.")));

        var entries = selected.Value!.Select(row => (row.Title, new Uri(origin, "/app/aq/surveys/" + row.EntryRef).AbsoluteUri)).ToArray();
        return format switch
        {
            "png" => File(AqQrDocuments.Png(entries[0].AbsoluteUri), "image/png", "survey-qr.png"),
            "svg" => File(AqQrDocuments.Svg(entries[0].AbsoluteUri), "image/svg+xml", "survey-qr.svg"),
            _ => File(AqQrDocuments.Pdf(entries), "application/pdf", "survey-qr.pdf")
        };
    }

    private bool TryGetOrigin(out Uri origin)
    {
        origin = null!;
        if (!Request.Host.HasValue || !Uri.TryCreate($"{Request.Scheme}://{Request.Host.ToUriComponent()}", UriKind.Absolute, out var candidate)
            || candidate.UserInfo.Length != 0 || candidate.AbsolutePath != "/")
            return false;
        var approved = (configuration["AllowedHosts"] ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(candidate.IdnHost, StringComparer.OrdinalIgnoreCase);
        if (candidate.Scheme != Uri.UriSchemeHttps && !(candidate.IsLoopback && candidate.Scheme == Uri.UriSchemeHttp)
            || (!approved && !candidate.IsLoopback))
            return false;

        origin = candidate;
        return true;
    }

    private IActionResult Respond<T>(Result<T> result)
    {
        if (result.IsSuccess)
            return Ok(ApiEnvelope.Success(HttpContext, result.Value));
        var error = result.ErrorDetail;
        var status = error?.Code switch
        {
            "AuthenticationRequired" => StatusCodes.Status401Unauthorized,
            "CapabilityDenied" => StatusCodes.Status403Forbidden,
            "NotFound" => StatusCodes.Status404NotFound,
            "RowVersionConflict" or "InvalidStateTransition" or "ConcurrentModification" => StatusCodes.Status409Conflict,
            "ExportTooLarge" => StatusCodes.Status413PayloadTooLarge,
            _ => StatusCodes.Status400BadRequest
        };
        return StatusCode(status, ApiEnvelope.Error(HttpContext, status,
            error?.Code ?? "ValidationFailed", error?.Message ?? "Invalid survey entry request.", error?.Target));
    }
}
