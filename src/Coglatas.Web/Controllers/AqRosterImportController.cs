using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Common;
using Coglatas.Application.Forms.SurveyManagement;
using Coglatas.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualBasic.FileIO;

namespace Coglatas.Web.Controllers;

[AqProtectedEndpoint]
[ApiController]
[Authorize]
[Route("api/v1/core/events/{eventId:guid}/presentations")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status400BadRequest, "application/json")]
[ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status401Unauthorized, "application/json")]
[ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status403Forbidden, "application/json")]
[ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status404NotFound, "application/json")]
[ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status500InternalServerError, "application/json")]
public sealed class AqRosterImportController(
    AqSurveyManagementService management,
    IDataProtectionProvider protection) : ControllerBase
{
    private const int MaximumBytes = 64 * 1024;
    private static readonly string[] Columns = ["presentationId", "role", "personRef", "pendingRosterRef"];
    private readonly IAqRosterPreviewCodec previewCodec = new PreviewCodec(protection);

    [HttpPost("import-preview")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumBytes + 4096)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumBytes)]
    [ProducesResponseType<ApiSuccessEnvelope<AqRosterPreview>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    public async Task<IActionResult> Preview(Guid eventId, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length is <= 0 or > MaximumBytes || Request.Form.Files.Count != 1 || Request.Form.Count != 0)
            return Invalid("Provide one bounded CSV file.");
        var rows = new List<AqRosterInput>();
        var diagnostics = new List<AqRosterDiagnostic>();
        using var stream = file.OpenReadStream();
        using var parser = new TextFieldParser(stream, new UTF8Encoding(false, true), detectEncoding: false)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false,
        };
        parser.SetDelimiters(",");
        try
        {
            var header = parser.ReadFields();
            if (header is not { Length: 4 })
                return Invalid("The roster CSV header is invalid.");
            header[0] = header[0].TrimStart('\uFEFF');
            if (!header.SequenceEqual(Columns))
                return Invalid("The roster CSV header is invalid.");

            for (var row = 2; !parser.EndOfData; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (row > 65)
                    return Invalid("The roster CSV contains too many rows.");
                var fields = parser.ReadFields();
                if (fields is not { Length: 4 } || !Guid.TryParseExact(fields[0], "D", out var presentationId)
                    || presentationId == Guid.Empty || fields[1] is not ("presenter" or "reviewer")
                    || (string.IsNullOrEmpty(fields[2]) == string.IsNullOrEmpty(fields[3])))
                {
                    diagnostics.Add(new(row, "InvalidRosterRow"));
                    continue;
                }
                if (fields[2].Length > 0 && !Guid.TryParseExact(fields[2], "D", out _))
                {
                    diagnostics.Add(new(row, "InvalidPersonReference"));
                    continue;
                }
                rows.Add(new(row, presentationId, fields[1],
                    fields[2].Length == 0 ? null : Guid.Parse(fields[2]),
                    fields[3].Length == 0 ? null : fields[3]));
            }
        }
        catch (Exception error) when (error is MalformedLineException or DecoderFallbackException)
        {
            return Invalid("The roster CSV must use well-formed UTF-8 and quoted CSV syntax.");
        }

        return Respond(await management.PreviewRosterAsync(eventId, rows, diagnostics, previewCodec, cancellationToken));
    }

    [HttpPost("import-commit")]
    [RequiresAqProtectedIdempotencyKey]
    [RequestSizeLimit(MaximumBytes)]
    [ProducesResponseType<ApiSuccessEnvelope<AqRosterReceipt>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ApiErrorEnvelope>(StatusCodes.Status409Conflict, "application/json")]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    public async Task<IActionResult> Commit(Guid eventId, AqCommitRosterRequest request, CancellationToken cancellationToken)
    {
        var key = AqProtectedIdempotencyKey.Read(Request.Headers);
        if (key.ErrorDetail is { } error)
            return Failure(StatusCodes.Status400BadRequest, error.Code, error.Message, error.Target);

        return Respond(await management.CommitRosterAsync(eventId, request, key.Value!, previewCodec, cancellationToken));
    }

    private IActionResult Respond<T>(Result<T> result)
    {
        if (result.IsSuccess)
            return Ok(ApiEnvelope.Success(HttpContext, result.Value));
        var error = result.ErrorDetail;
        var status = error?.Code switch
        {
            "AuthenticationRequired" => StatusCodes.Status401Unauthorized,
            "NotFound" => StatusCodes.Status404NotFound,
            "CapabilityDenied" => StatusCodes.Status403Forbidden,
            "InvalidStateTransition" or "RowVersionConflict" or "ConcurrentModification" or "IdempotencyConflict" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
        return Failure(status, error?.Code ?? "ValidationFailed", error?.Message ?? "The roster request was invalid.");
    }

    private IActionResult Invalid(string message) => Failure(StatusCodes.Status400BadRequest, "ValidationFailed", message);

    private IActionResult Failure(int status, string code, string message, string? target = null) =>
        StatusCode(status, ApiEnvelope.Error(HttpContext, status, code, message, target));

    private sealed class PreviewCodec(IDataProtectionProvider protection) : IAqRosterPreviewCodec
    {
        private readonly IDataProtector protector = protection.CreateProtector("Coglatas.AQ.RosterPreview.v1");

        public string Protect(AqRosterPreviewState preview) => protector.Protect(JsonSerializer.Serialize(preview));

        public AqRosterPreviewState? Unprotect(string value)
        {
            try
            {
                return JsonSerializer.Deserialize<AqRosterPreviewState>(protector.Unprotect(value));
            }
            catch (Exception error) when (error is CryptographicException or JsonException or FormatException)
            {
                return null;
            }
        }
    }
}
