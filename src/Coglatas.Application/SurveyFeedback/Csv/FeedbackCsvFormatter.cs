using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Common;
using Coglatas.Application.Security.Redaction;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.SurveyFeedback.Csv;

public static class FeedbackCsvFormatter
{
    internal const int MaxRows = 5_000;
    private const int MaxBytes = 2 * 1024 * 1024;
    private static readonly UTF8Encoding CsvEncoding = new(encoderShouldEmitUTF8Identifier: true);
    private static readonly JsonSerializerOptions SnapshotOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AggregateColumns = new(
        [
            "questionKey", "questionLabel", "responseCount", "eligibleDenominator",
            "choiceKey", "choiceLabel", "choiceCount", "numericMean"
        ], StringComparer.Ordinal);
    private static readonly HashSet<string> DetailColumns = new(
        [
            "submittedAtUtc", "questionKey", "questionLabel", "answerValue", "respondentName"
        ], StringComparer.Ordinal);

    public static bool IsValidRequest(FeedbackCsvRequest request)
    {
        var allowed = request.Mode switch
        {
            "aggregate" => AggregateColumns,
            "detail" => DetailColumns,
            _ => null
        };
        return allowed is not null && request.Columns is { Count: > 0 } columns &&
            columns.Count <= allowed.Count &&
            columns.Distinct(StringComparer.Ordinal).Count() == columns.Count &&
            columns.All(allowed.Contains);
    }

    public static Result<FeedbackCsvFile> Format(FeedbackCsvRequest request, FeedbackCsvSnapshot source,
        IRedactionService redaction, AuthorizationContext context)
    {
        if (!IsValidRequest(request))
            return SurveyFeedbackService.Invalid<FeedbackCsvFile>();

        var columns = request.Columns!;
        if (context.AuthorizationState != RedactionAuthorizationState.Allowed ||
            columns.Any(column => !AllowsColumn(context.FieldAccessPolicy, column)))
        {
            return RestrictedField();
        }

        if (source.Targets.Count > MaxRows || source.Submissions.Count > MaxRows || source.Answers.Count > MaxRows)
            return TooLarge();

        // The source adapter already removes hidden answers. Keep the output projection
        // fail-closed against alternate adapters and unexpected persisted state.
        var answers = source.Answers.Where(answer => !answer.IsHidden).ToArray();
        var rows = request.Mode == "aggregate"
            ? AggregateRows(source.Submissions.Count, answers, columns)
            : DetailRows(source.Submissions, answers, columns);

        using var output = new MemoryStream();
        output.Write(CsvEncoding.GetPreamble());
        if (!TryWrite(output, columns))
            return TooLarge();

        var count = 0;
        foreach (var row in rows)
        {
            if (++count > MaxRows)
                return TooLarge();

            var cells = row.ToArray();
            var projection = columns.Zip(cells).ToDictionary(
                pair => RedactionField(pair.First),
                pair => pair.Second, StringComparer.Ordinal);
            var protectedRow = redaction.Redact(context, projection, RedactionProfile.ExportRow);
            if (protectedRow.RedactionApplied ||
                protectedRow.Value is not IReadOnlyDictionary<string, string> safe ||
                columns.Any(column => !safe.ContainsKey(RedactionField(column))))
                return RestrictedField();

            if (!TryWrite(output, columns.Select(column => safe[RedactionField(column)])))
                return TooLarge();
        }

        var content = output.ToArray();
        return Result<FeedbackCsvFile>.Success(new FeedbackCsvFile(
            content, DescribeSource(source, content)));
    }

    private static string RedactionField(string column) => column == "answerValue" ? "body" : column;

    private static bool AllowsColumn(FieldAccessPolicySnapshot policy, string column) =>
        column != "respondentName" && policy.Allows(
            column is "answerValue" ? CanonicalDataClassification.Confidential : CanonicalDataClassification.Internal,
            column);

    private static Result<FeedbackCsvFile> RestrictedField() =>
        Result<FeedbackCsvFile>.Failure(new ApplicationErrorDetail(
            "RestrictedFieldDenied", "This export field is unavailable."));

    private static IEnumerable<IEnumerable<string>> AggregateRows(
        int count, FeedbackCsvAnswer[] answers, IReadOnlyList<string> columns)
    {
        var definitions = answers.GroupBy(answer => answer.FieldKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var summary = SurveyFeedbackAggregation.Summarize(new FeedbackSummaryInput(count, answers.Select(answer =>
            new FeedbackValueSnapshot(answer.Id, answer.FieldKey, answer.FieldType.ToString(),
                (answer.CompatibilityRootFieldId ?? answer.FieldId).ToString("D"), answer.ValueJson, false)).ToArray()));

        if (summary.Questions.Count == 0)
        {
            yield return columns.Select(column => column == "responseCount" ? Number(summary.Responses) : "");
            yield break;
        }

        foreach (var question in summary.Questions)
        {
            var field = definitions[question.FieldKey];
            var options = (JsonSerializer.Deserialize<SurveyOption[]>(field.OptionsJson, SnapshotOptions) ?? [])
                .ToDictionary(option => option.OptionKey, option => option.Label, StringComparer.Ordinal);
            IEnumerable<FeedbackOptionCount?> distribution = question.Distribution is { Count: > 0 } entries
                ? entries.Cast<FeedbackOptionCount?>()
                : [null];

            foreach (var choice in distribution)
            {
                yield return columns.Select(column => column switch
                {
                    "questionKey" => ProtectText(question.FieldKey),
                    "questionLabel" => ProtectText(field.Label),
                    "responseCount" => Number(summary.Responses),
                    "eligibleDenominator" => Number(question.Denominator),
                    "choiceKey" => ProtectText(choice?.OptionKey ?? ""),
                    "choiceLabel" => choice is null ? "" : ProtectText(options.GetValueOrDefault(choice.OptionKey, "")),
                    "choiceCount" => choice is null ? "" : Number(choice.Responses),
                    "numericMean" => question.Mean?.ToString(CultureInfo.InvariantCulture) ?? "",
                    _ => ""
                });
            }
        }
    }

    private static IEnumerable<IEnumerable<string>> DetailRows(
        IReadOnlyList<FeedbackCsvSubmission> submissions, FeedbackCsvAnswer[] answers, IReadOnlyList<string> columns)
    {
        var bySubmission = submissions.ToDictionary(item => item.Id);
        foreach (var answer in answers)
        {
            if (!bySubmission.TryGetValue(answer.SubmissionId, out var submission))
                continue;

            yield return columns.Select(column => column switch
            {
                "submittedAtUtc" => submission.SubmittedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                "questionKey" => ProtectText(answer.FieldKey),
                "questionLabel" => ProtectText(answer.Label),
                "answerValue" => FormatAnswer(answer),
                _ => ""
            });
        }
    }

    private static string FormatAnswer(FeedbackCsvAnswer answer)
    {
        using var document = JsonDocument.Parse(answer.ValueJson);
        var value = document.RootElement;
        return value.ValueKind switch
        {
            JsonValueKind.String => ProtectText(value.GetString() ?? ""),
            JsonValueKind.Number when value.TryGetDecimal(out var number) =>
                number.ToString(CultureInfo.InvariantCulture),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Array => ProtectText(value.GetRawText()),
            _ => ""
        };
    }

    private static string ProtectText(string value)
    {
        foreach (var character in value)
        {
            if (character is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n' or
                '\uFF1D' or '\uFF0B' or '\uFF0D' or '\uFF20' or '\u2212')
                return "'" + value;

            if (!char.IsWhiteSpace(character) && !char.IsControl(character) &&
                char.GetUnicodeCategory(character) != UnicodeCategory.Format)
                break;
        }

        return value;
    }

    private static bool TryWrite(MemoryStream output, IEnumerable<string> cells)
    {
        var row = string.Join(',', cells.Select(cell => "\"" + cell.Replace("\"", "\"\"") + "\"")) + "\r\n";
        var required = CsvEncoding.GetByteCount(row);
        if (output.Length + required > MaxBytes)
            return false;

        output.Write(CsvEncoding.GetBytes(row));
        return true;
    }

    private static string DescribeSource(FeedbackCsvSnapshot source, byte[] content)
    {
        using var version = new MemoryStream();
        using var writer = new BinaryWriter(version, Encoding.UTF8, leaveOpen: true);
        writer.Write("survey-feedback-csv/v1");
        writer.Write(source.PresentationVersion);
        writer.Write(source.EventSettingsVersion);
        writer.Write(source.PresentationSettingsVersion);
        writer.Write(source.Targets.Count);
        foreach (var target in source.Targets.OrderBy(item => item.Id))
        {
            writer.Write(target.Id.ToByteArray());
            writer.Write(target.VersionNo);
            writer.Write(target.ResultsStopped);
        }
        writer.Write(source.Submissions.Count);
        foreach (var submission in source.Submissions.OrderBy(item => item.Id))
        {
            writer.Write(submission.Id.ToByteArray());
            writer.Write(submission.VersionNo);
        }
        writer.Write(source.Answers.Count);
        foreach (var answer in source.Answers.OrderBy(item => item.Id))
        {
            writer.Write(answer.Id.ToByteArray());
            writer.Write(answer.FieldId.ToByteArray());
            writer.Write((answer.CompatibilityRootFieldId ?? answer.FieldId).ToByteArray());
            writer.Write(answer.VisibilityVersionNo);
            writer.Write(answer.IsHidden);
        }

        writer.Write(SHA256.HashData(content));
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(version.GetBuffer().AsSpan(0, (int)version.Length)));
    }

    private static Result<FeedbackCsvFile> TooLarge() =>
        Result<FeedbackCsvFile>.Failure(new ApplicationErrorDetail("ExportTooLarge", "The export exceeds the supported bounds."));

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
