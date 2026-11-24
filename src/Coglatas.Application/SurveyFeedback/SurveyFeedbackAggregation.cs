using System.Text.Json;

namespace Coglatas.Application.SurveyFeedback;

public static class SurveyFeedbackAggregation
{
    public static FeedbackSummary Summarize(FeedbackSummaryInput input)
    {
        var questions = new List<FeedbackQuestionSummary>();
        foreach (var group in input.Values.GroupBy(value => value.FieldKey, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var first = group.First();
            if (group.Any(value => value.CompatibilityStamp != first.CompatibilityStamp ||
                                   value.FieldType != first.FieldType))
            {
                // A shared key alone is insufficient evidence of semantic compatibility.
                continue;
            }

            var count = 0L;
            var mean = 0m;
            Dictionary<string, long>? distribution = null;
            foreach (var value in group)
            {
                using var document = JsonDocument.Parse(value.ValueJson);
                var answer = document.RootElement;
                if (answer.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                count++;
                if (first.FieldType is "Number" or "Scale" && answer.ValueKind == JsonValueKind.Number &&
                    answer.TryGetDecimal(out var number))
                {
                    // Average without adding two potentially large signed values first.
                    mean = count == 1 ? number : AppendToAverage(mean, number, count);
                }
                else if (first.FieldType == "SingleChoice" && answer.ValueKind is JsonValueKind.String)
                {
                    Increment(distribution ??= new(StringComparer.Ordinal), answer.GetString()!);
                }
                else if (first.FieldType == "MultipleChoice" && answer.ValueKind is JsonValueKind.Array)
                {
                    distribution ??= new(StringComparer.Ordinal);
                    var options = answer.EnumerateArray()
                        .Where(option => option.ValueKind == JsonValueKind.String)
                        .Select(option => option.GetString()!)
                        .Distinct(StringComparer.Ordinal);
                    foreach (var option in options)
                    {
                        Increment(distribution, option);
                    }
                }
            }

            var average = count > 0 && first.FieldType is ("Number" or "Scale") ? mean : (decimal?)null;
            var optionCounts = distribution?.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new FeedbackOptionCount(item.Key, item.Value))
                .ToArray();
            questions.Add(new FeedbackQuestionSummary(group.Key, count, optionCounts, average));
        }

        return new FeedbackSummary(input.Count, questions);
    }

    internal static decimal AppendToAverage(decimal mean, decimal number, long totalCount) =>
        (mean - mean / totalCount) + number / totalCount;

    private static void Increment(Dictionary<string, long> distribution, string key) =>
        distribution[key] = distribution.GetValueOrDefault(key) + 1;
}
