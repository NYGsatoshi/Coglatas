using System.Security.Cryptography;
using System.Text.Json;
using Coglatas.Application.SurveyFeedback;
using Microsoft.AspNetCore.DataProtection;

namespace Coglatas.Web.SurveyFeedback;

public sealed class ProtectedSurveyFeedbackCursor(IDataProtectionProvider dataProtection) : ISurveyFeedbackCursor
{
    private readonly ITimeLimitedDataProtector _protector =
        dataProtection.CreateProtector("SurveyFeedbackCursor.v1").ToTimeLimitedDataProtector();

    public string Write(FeedbackCursorScope scope, FeedbackPagePosition position) =>
        _protector.Protect(JsonSerializer.Serialize(new CursorPayload(scope, position)), TimeSpan.FromMinutes(15));

    public bool TryRead(string cursor, FeedbackCursorScope scope, out FeedbackPagePosition? position)
    {
        position = null;
        if (string.IsNullOrWhiteSpace(cursor) || cursor.Length > 4096)
        {
            return false;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<CursorPayload>(_protector.Unprotect(cursor));
            if (payload?.Scope != scope || payload.Position.Id == Guid.Empty)
            {
                return false;
            }

            position = payload.Position;
            return true;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            return false;
        }
    }

    private sealed record CursorPayload(FeedbackCursorScope Scope, FeedbackPagePosition Position);
}
