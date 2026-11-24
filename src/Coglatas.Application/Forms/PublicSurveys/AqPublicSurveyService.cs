using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Forms.PublicSurveys;

public sealed class AqPublicSurveyService(IAqPublicSurveyStore store, ICurrentUser currentUser, IClock clock)
{
    public const int DefaultPageSize = 25;
    public const int MaximumPageSize = 100;
    private static readonly JsonSerializerOptions PublicFieldOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public Task<AqPublicProgram?> ReadProgramAsync(string code, string? cursor, int pageSize, CancellationToken cancellationToken) =>
        store.ReadProgramAsync(Hash(code), cursor, pageSize, cancellationToken);

    public async Task<AqPublicForm?> ReadFormAsync(string code, CancellationToken cancellationToken)
    {
        var survey = await store.ReadTargetAsync(Hash(code), GetSession(), cancellationToken);
        if (survey is null)
        {
            return null;
        }
        var fields = survey.Fields.Select(ToPublicField).ToArray();
        return new AqPublicForm(
            survey.Definition.Title, survey.Schema.VersionRef, survey.Schema.SchemaDigest, fields,
            survey.Settings.NameMode switch
            {
                SurveyNameMode.Anonymous => "anonymous",
                SurveyNameMode.OptionalName => "optionalName",
                SurveyNameMode.RequiredName => "requiredName",
                _ => throw new InvalidOperationException("Unknown name mode.")
            },
            CanOptIn(survey),
            IsOpen(survey, clock.UtcNow) ? "open" : "closed",
            survey.Target.LocalMarkerKey,
            survey.Settings.NameMode == SurveyNameMode.Anonymous
                ? "回答は、閲覧権限のある発表者や審査員などに表示されます。自由記述には個人情報を書かないでください。"
                : "回答は、閲覧権限のある発表者や審査員などに表示されます。入力した氏名や連携したアカウントの情報は、本人情報の閲覧権限を持つ担当者にのみ表示されます。自由記述には個人情報を書かないでください。");
    }

    public Task<AqPublicCommandResult> SubmitAsync(
        string code, string? idempotencyKey, AqPublicSubmissionInput input,
        string requestId, CancellationToken cancellationToken)
    {
        if (idempotencyKey is null)
        {
            return Task.FromResult(AqPublicCommandResult.Reject("MissingIdempotencyKey"));
        }
        if (idempotencyKey.Length is < 8 or > 128 ||
            idempotencyKey.All(character => character == ' ') ||
            idempotencyKey.Any(character => character is < ' ' or > '~'))
        {
            return Task.FromResult(AqPublicCommandResult.Reject("InvalidIdempotencyKey"));
        }
        var session = input.AccountOptIn ? GetSession() : null;
        return store.SubmitAsync(
            Hash(code), Hash(idempotencyKey), session,
            (survey, receipt) => Decide(survey, receipt, session, input, requestId),
            cancellationToken);
    }

    private AqPublicWriteDecision Decide(
        AqPublicSurveyTarget survey, SubmissionIdempotency? receipt,
        AqPublicSession? session, AqPublicSubmissionInput input, string requestId)
    {
        if (input.AccountOptIn && session is null)
        {
            return Reject("AuthenticationRequired");
        }
        if (!TryCanonicalize(input, out var canonicalAnswers))
        {
            return Reject("ValidationFailed");
        }

        var linkedAccount = input.AccountOptIn && survey.AccountEligible ? session?.UserId : null;
        var intent = JsonSerializer.Serialize(new
        {
            input.VersionRef, input.SchemaDigest, input.Name, input.AccountOptIn, linkedAccount,
            Answers = canonicalAnswers.OrderBy(pair => pair.Key, StringComparer.Ordinal)
        });
        var intentDigest = Hash(intent);
        var now = clock.UtcNow;
        if (receipt is not null && receipt.CanReplayAt(now))
        {
            if (!string.Equals(receipt.IntentDigest, intentDigest, StringComparison.Ordinal))
            {
                return Reject("IdempotencyConflict");
            }

            var original = JsonSerializer.Deserialize<AqPublicReceipt>(receipt.ReceiptJson) ??
                throw new InvalidOperationException("Committed submission receipt has no request identity.");
            return new AqPublicWriteDecision(AqPublicCommandResult.Accept(original));
        }

        if (!string.Equals(input.VersionRef, survey.Schema.VersionRef, StringComparison.Ordinal) ||
            !string.Equals(input.SchemaDigest, survey.Schema.SchemaDigest, StringComparison.Ordinal) ||
            !IsOpen(survey, now))
        {
            return Reject("InvalidStateTransition");
        }
        if (input.AccountOptIn && !CanOptIn(survey))
        {
            return Reject("ValidationFailed");
        }
        if (survey.Settings.NameMode == SurveyNameMode.Anonymous && input.Name is not null ||
            survey.Settings.NameMode == SurveyNameMode.RequiredName && string.IsNullOrWhiteSpace(input.Name) ||
            input.Name is not null && (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 256))
        {
            return Reject("ValidationFailed");
        }
        if (!TryValidateAnswers(survey.Fields, canonicalAnswers, out var answers))
        {
            return Reject("ValidationFailed");
        }
        var acceptedAt = clock.UtcNow;
        if (!IsOpen(survey, acceptedAt) || acceptedAt < survey.Schema.PublishedAtUtc)
        {
            return Reject("InvalidStateTransition");
        }
        var plan = new AqPublicWritePlan(
            intentDigest, requestId, input.Name, linkedAccount, answers, acceptedAt);
        return new AqPublicWriteDecision(AqPublicCommandResult.Accept(new AqPublicReceipt(requestId)), plan);
    }

    private static AqPublicWriteDecision Reject(string code) => new(AqPublicCommandResult.Reject(code));

    private static bool CanOptIn(AqPublicSurveyTarget survey) =>
        survey.AccountEligible && survey.Settings.NameMode != SurveyNameMode.Anonymous &&
        survey.Settings.AccountBinding == SurveyAccountBinding.ExplicitOptIn;

    private static bool IsOpen(AqPublicSurveyTarget survey, DateTimeOffset now) =>
        survey.ParentCanReceive && !survey.Settings.ReceptionStopped && survey.Campaign.IsEnabled && survey.Target.IsEnabled &&
        survey.Settings.StartsAtUtc <= now && now < survey.Settings.EndsAtUtc &&
        (survey.Target.StartsAtUtc is not { } start || start <= now) &&
        (survey.Target.EndsAtUtc is not { } end || now < end);

    private AqPublicSession? GetSession() =>
        currentUser.IsAuthenticated && currentUser.UserId is { } userId && currentUser.SessionId is { } sessionId
            ? new AqPublicSession(userId, sessionId)
            : null;

    public static string Hash(string input) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    private static AqPublicField ToPublicField(FormFieldDefinition field)
    {
        var type = field.FieldType switch
        {
            SurveyFieldType.Text => "text",
            SurveyFieldType.Number => "number",
            SurveyFieldType.Scale => "scale",
            SurveyFieldType.Date => "date",
            SurveyFieldType.Boolean => "boolean",
            SurveyFieldType.SingleChoice => "singleChoice",
            SurveyFieldType.MultipleChoice => "multipleChoice",
            _ => throw new InvalidOperationException("Unknown published question type.")
        };
        var published = field.ToSurveyField();
        var options = published.Options.Count == 0
            ? null
            : published.Options.Select(option => new AqPublicOption(option.OptionKey, option.Label)).ToArray();
        JsonElement? constraints = null;
        if (published.Constraints is { } rules)
        {
            var projected = JsonSerializer.SerializeToElement(rules, PublicFieldOptions);
            if (projected.EnumerateObject().Any())
            {
                constraints = projected;
            }
        }
        return new AqPublicField(field.FieldKey, field.Label, field.IsRequired, type, options, constraints);
    }

    private static bool TryCanonicalize(AqPublicSubmissionInput input, out Dictionary<string, string> answers)
    {
        answers = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(input.VersionRef) || input.SchemaDigest is not { Length: 64 } digest ||
            digest.Any(character => character is not (>= 'a' and <= 'f') and not (>= '0' and <= '9')) ||
            input.Answers is null)
        {
            return false;
        }

        foreach (var answer in input.Answers)
        {
            if (answer is null || string.IsNullOrEmpty(answer.FieldKey) ||
                !TryCanonicalizeValue(answer.Value, out var serialized) ||
                !answers.TryAdd(answer.FieldKey, serialized))
            {
                return false;
            }
        }
        return true;
    }

    private static bool TryCanonicalizeValue(JsonElement value, out string serialized)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                serialized = JsonSerializer.Serialize(value.GetString());
                return true;
            case JsonValueKind.True:
            case JsonValueKind.False:
                serialized = value.GetBoolean() ? "true" : "false";
                return true;
            case JsonValueKind.Number when value.TryGetDecimal(out var numeric):
                serialized = numeric.ToString("G29", CultureInfo.InvariantCulture);
                return true;
            case JsonValueKind.Array:
                var keys = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var option in value.EnumerateArray())
                {
                    if (option.ValueKind != JsonValueKind.String ||
                        option.GetString() is not { Length: > 0 } key || !keys.Add(key))
                    {
                        serialized = string.Empty;
                        return false;
                    }
                }
                serialized = JsonSerializer.Serialize(keys);
                return true;
            default:
                serialized = string.Empty;
                return false;
        }
    }

    private static bool TryValidateAnswers(
        IReadOnlyList<FormFieldDefinition> fields,
        IReadOnlyDictionary<string, string> values,
        out Dictionary<string, JsonElement> answers)
    {
        answers = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (values.Count == 0 || values.Count > fields.Count)
        {
            return false;
        }
        foreach (var field in fields)
        {
            if (!values.TryGetValue(field.FieldKey, out var serialized))
            {
                if (field.IsRequired)
                {
                    return false;
                }
                continue;
            }

            using var value = JsonDocument.Parse(serialized);
            if (!field.ToSurveyField().Accepts(value.RootElement))
            {
                return false;
            }
            answers.Add(field.FieldKey, value.RootElement.Clone());
        }
        return answers.Count == values.Count;
    }
}
