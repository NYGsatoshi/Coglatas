using System.Globalization;
using System.Text.Json;
using Coglatas.Application.Forms.PublicSurveys;
using Coglatas.Web.Configuration;
using Coglatas.Web.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Coglatas.Web.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/public")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AqPublicSurveyController(
    AqPublicSurveyService surveys,
    IConfiguration configuration,
    IAntiforgery antiforgery) : ControllerBase
{
    private static readonly JsonSerializerOptions SubmissionOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        AllowDuplicateProperties = false
    };

    [HttpGet("events/{code}/presentations")]
    [EnableRateLimiting(HttpSecurityPolicy.AqPublicReadRateLimitPolicy)]
    [ProducesResponseType<ApiSuccessEnvelope<AqPublicProgram>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProgram(string code, CancellationToken cancellationToken)
    {
        if (!IsEnabled())
        {
            return Fail("NotFound");
        }

        var pageSize = AqPublicSurveyService.DefaultPageSize;
        if (Request.Query.TryGetValue("pageSize", out var sizeValue) &&
            (sizeValue.Count != 1 || !int.TryParse(sizeValue[0], NumberStyles.None, CultureInfo.InvariantCulture, out pageSize) ||
                pageSize is < 1 or > AqPublicSurveyService.MaximumPageSize))
        {
            return Fail("ValidationFailed");
        }
        string? cursor = null;
        if (Request.Query.TryGetValue("cursor", out var cursorValue))
        {
            if (cursorValue.Count != 1 || string.IsNullOrWhiteSpace(cursorValue[0]))
            {
                return Fail("ValidationFailed");
            }
            cursor = cursorValue[0];
        }

        try
        {
            var program = await surveys.ReadProgramAsync(code, cursor, pageSize, cancellationToken);
            return program is null ? Fail("NotFound") : Ok(ApiEnvelope.Success(HttpContext, program));
        }
        catch (AqInvalidProgramCursorException)
        {
            return Fail("ValidationFailed");
        }
    }

    [HttpGet("surveys/{code}")]
    [EnableRateLimiting(HttpSecurityPolicy.AqPublicReadRateLimitPolicy)]
    [ProducesResponseType<ApiSuccessEnvelope<AqPublicForm>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetForm(string code, CancellationToken cancellationToken)
    {
        if (!IsEnabled())
        {
            return Fail("NotFound");
        }

        var form = await surveys.ReadFormAsync(code, cancellationToken);
        return form is null ? Fail("NotFound") : Ok(ApiEnvelope.Success(HttpContext, form));
    }

    [HttpPost("surveys/{code}/submissions")]
    [EnableRateLimiting(HttpSecurityPolicy.AqPublicSubmitRateLimitPolicy)]
    [ProducesResponseType<ApiSuccessEnvelope<AqPublicAcceptance>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Submit(string code, CancellationToken cancellationToken)
    {
        if (!IsEnabled())
        {
            return Fail("NotFound");
        }

        // Defense for hosts that compose the controller without the standard
        // middleware. ASP.NET Core reuses a validation already performed there.
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
        {
            return Fail("CsrfRejected");
        }

        if (!Request.HasJsonContentType())
        {
            return Fail("UnsupportedMediaType");
        }
        AqPublicSubmissionInput? body;
        try
        {
            using var document = await JsonDocument.ParseAsync(
                Request.Body,
                new JsonDocumentOptions { AllowDuplicateProperties = false },
                cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                document.RootElement.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.Null)
            {
                return Fail("ValidationFailed");
            }
            body = document.RootElement.Deserialize<AqPublicSubmissionInput>(SubmissionOptions);
        }
        catch (JsonException exception)
        {
            // Parser errors have no path. DTO-shape errors use the validation envelope.
            return Fail(exception.Path is null ? "MalformedJson" : "ValidationFailed");
        }

        if (body is null)
        {
            return Fail("ValidationFailed");
        }
        if (Request.Headers.TryGetValue("Idempotency-Key", out var keys) && keys.Count != 1)
        {
            return Fail("InvalidIdempotencyKey");
        }

        var result = await surveys.SubmitAsync(code, keys.Count == 1 ? keys[0] : null, body, HttpContext.TraceIdentifier, cancellationToken);
        return result.Receipt is { } receipt
            ? StatusCode(StatusCodes.Status201Created,
                new ApiSuccessEnvelope<AqPublicAcceptance>(receipt.RequestId, new AqPublicAcceptance(true), []))
            : Fail(result.ErrorCode!);
    }

    private bool IsEnabled() => configuration.GetValue<bool>("AQ:EnablePublicSurveys");

    private IActionResult Fail(string code)
    {
        var (status, message) = code switch
        {
            "NotFound" => (404, "The public survey resource was not found."),
            "AuthenticationRequired" => (401, "A current session is required for account linkage."),
            "CsrfRejected" => (403, "A valid CSRF token is required."),
            "MissingIdempotencyKey" => (400, "An Idempotency-Key header is required."),
            "InvalidIdempotencyKey" => (400, "The Idempotency-Key header is invalid."),
            "MalformedJson" => (400, "The request body is not valid JSON."),
            "UnsupportedMediaType" => (415, "The request Content-Type is not supported."),
            "IdempotencyConflict" => (409, "The key has already been used for a different submission."),
            "ConcurrentModification" => (409, "A matching request is in progress. Retry with the same key."),
            "InvalidStateTransition" => (409, "The form has changed or is closed for submission."),
            "ValidationFailed" => (400, "The public survey request is invalid."),
            _ => throw new InvalidOperationException("Unknown public survey result.")
        };
        return StatusCode(status, ApiEnvelope.Error(HttpContext, status, code, message));
    }
}

public sealed record AqPublicAcceptance(bool Accepted);
