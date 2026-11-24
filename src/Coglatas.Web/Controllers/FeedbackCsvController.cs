using Coglatas.Application.Common;
using Coglatas.Application.SurveyFeedback.Csv;
using Coglatas.Web.Configuration;
using Coglatas.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Coglatas.Web.Controllers;

[AqProtectedEndpoint]
[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(HttpSecurityPolicy.TenantExportRateLimitPolicy)]
[Route("api/v1/core")]
[ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status401Unauthorized, "application/json")]
[ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status403Forbidden, "application/json")]
[ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status404NotFound, "application/json")]
[ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status500InternalServerError, "application/json")]
[ProducesResponseType<FeedbackCsvController.RateLimitResponse>(StatusCodes.Status429TooManyRequests, "application/json")]
public sealed class FeedbackCsvController(FeedbackCsvService exports) : ControllerBase
{
    [HttpPost("presentations/{id:guid}/feedback-export")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType<ApiSuccessEnvelope<FeedbackCsvQueued>>(StatusCodes.Status202Accepted, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status409Conflict, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status413PayloadTooLarge, "application/json")]
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
    [ProducesResponseType<ApiSuccessEnvelope<FeedbackCsvStatus>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status409Conflict, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status410Gone, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status413PayloadTooLarge, "application/json")]
    public async Task<IActionResult> GetStatus(Guid id, CancellationToken cancellationToken)
    {
        var result = await exports.StatusAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(ApiEnvelope.Success(HttpContext, result.Value!)) : Fail(result);
    }

    [HttpPost("presentation-feedback-exports/{id:guid}/download-grants")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType<ApiSuccessEnvelope<FeedbackCsvGrantReceipt>>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status409Conflict, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status410Gone, "application/json")]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status413PayloadTooLarge, "application/json")]
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
    [ProducesResponseType<string>(StatusCodes.Status200OK, "text/csv",
        Description = "UTF-8 CSV attachment named presentation-feedback.csv.")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status410Gone, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status413PayloadTooLarge, "application/json")]
    public async Task<IActionResult> Download(Guid id, Guid grantId, CancellationToken cancellationToken)
    {
        var result = await exports.DownloadAsync(id, grantId, cancellationToken);
        return result.IsSuccess
            ? File(result.Value!.Content, "text/csv; charset=utf-8", "presentation-feedback.csv")
            : Fail(result);
    }

    // The CSV endpoint uses the shared tenant-export rate-limit response.
    private sealed record RateLimitResponse(string RequestId, RateLimitError Error);
    private sealed record RateLimitError(string Code, string Message);

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
