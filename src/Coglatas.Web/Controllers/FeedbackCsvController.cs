using Coglatas.Application.Common;
using Coglatas.Application.SurveyFeedback.Csv;
using Coglatas.Web.Configuration;
using Coglatas.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Coglatas.Web.Controllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(HttpSecurityPolicy.TenantExportRateLimitPolicy)]
[Route("api/v1/core")]
public sealed class FeedbackCsvController(FeedbackCsvService exports) : ControllerBase
{
    [HttpPost("presentations/{id:guid}/feedback-export")]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> Create(Guid id, FeedbackCsvRequest request, CancellationToken cancellationToken)
    {
        var key = ReadKey();
        if (!key.IsSuccess)
            return Fail(key);

        var result = await exports.RequestAsync(id, request, key.Value, HttpContext.TraceIdentifier, cancellationToken);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status202Accepted, new ApiSuccessEnvelope<FeedbackCsvQueued>(
                result.Value!.RequestId, result.Value.Data, Array.Empty<object>()))
            : Fail(result);
    }

    [HttpGet("presentation-feedback-exports/{id:guid}")]
    public async Task<IActionResult> GetStatus(Guid id, CancellationToken cancellationToken)
    {
        var result = await exports.StatusAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(ApiEnvelope.Success(HttpContext, result.Value!)) : Fail(result);
    }

    [HttpPost("presentation-feedback-exports/{id:guid}/download-grants")]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> CreateGrant(Guid id, CancellationToken cancellationToken)
    {
        var key = ReadKey();
        if (!key.IsSuccess)
            return Fail(key);

        var result = await exports.GrantAsync(id, key.Value, HttpContext.TraceIdentifier, cancellationToken);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, new ApiSuccessEnvelope<FeedbackCsvGrantReceipt>(
                result.Value!.RequestId, result.Value.Data, Array.Empty<object>()))
            : Fail(result);
    }

    [HttpGet("presentation-feedback-exports/{id:guid}/download-grants/{grantId:guid}/content")]
    public async Task<IActionResult> Download(Guid id, Guid grantId, CancellationToken cancellationToken)
    {
        var result = await exports.DownloadAsync(id, grantId, cancellationToken);
        return result.IsSuccess
            ? File(result.Value!.Content, "text/csv; charset=utf-8", "presentation-feedback.csv")
            : Fail(result);
    }

    private Result<string?> ReadKey()
    {
        if (!Request.Headers.TryGetValue("Idempotency-Key", out var keys))
            return Result<string?>.Success(null);

        return keys.Count == 1
            ? Result<string?>.Success(keys[0])
            : Result<string?>.Failure(new ApplicationErrorDetail(
                "InvalidIdempotencyKey", "The Idempotency-Key header is invalid.", Target: "header.Idempotency-Key"));
    }

    private IActionResult Fail<T>(Result<T> result)
    {
        var detail = result.ErrorDetail ?? new ApplicationErrorDetail("UnexpectedError", "The export could not be completed.");
        var status = detail.Code switch
        {
            "AuthenticationRequired" => StatusCodes.Status401Unauthorized,
            "NotFound" => StatusCodes.Status404NotFound,
            "CapabilityDenied" or "RestrictedFieldDenied" => StatusCodes.Status403Forbidden,
            "ConcurrentModification" or "IdempotencyConflict" => StatusCodes.Status409Conflict,
            "ExportExpired" => StatusCodes.Status410Gone,
            "ExportTooLarge" => StatusCodes.Status413PayloadTooLarge,
            "ValidationFailed" or "MissingIdempotencyKey" or "InvalidIdempotencyKey" => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };

        return StatusCode(status, ApiEnvelope.Error(HttpContext, status, detail.Code, detail.Message, detail.Target));
    }
}
