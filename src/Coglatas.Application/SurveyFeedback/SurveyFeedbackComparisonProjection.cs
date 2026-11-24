using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.SurveyFeedback;

public sealed record ApprovedFeedbackComparisonPolicy(Guid TenantId, Guid EventId, string ComparisonRef,
    long VersionNo, int MinimumSampleSize, IReadOnlyList<ApprovedFeedbackComparisonCohort> Cohorts,
    IReadOnlyList<ApprovedFeedbackComparisonField> Fields);

public sealed record ApprovedFeedbackComparisonCohort(string Label, IReadOnlyList<Guid> PresentationIds);

public sealed record ApprovedFeedbackComparisonField(string FieldKey, Guid CompatibilityRootFieldId,
    SurveyFieldType FieldType);

public sealed record FeedbackComparisonSample(Guid SubmissionId, string FieldKey, Guid CompatibilityRootFieldId,
    SurveyFieldType FieldType, string ValueJson);

public sealed record FeedbackComparison(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FeedbackComparisonProjection? Comparison = null);

public sealed record FeedbackComparisonProjection(IReadOnlyList<FeedbackComparisonCohort> Cohorts);

public sealed record FeedbackComparisonCohort(string CohortLabel, IReadOnlyList<FeedbackComparisonCell> Cells);

public sealed record FeedbackComparisonCell(string FieldKey, bool Suppressed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? SampleSize = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<FeedbackOptionCount>? Distribution = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Mean = null);

public static class SurveyFeedbackComparisonProjection
{
    private const int MaximumPresentations = 100;
    private const int MaximumFields = 100;

    public static bool Matches([NotNullWhen(true)] ApprovedFeedbackComparisonPolicy? policy,
        Guid tenantId, Guid eventId, string? requestedReference) =>
        policy is not null && policy.TenantId == tenantId && policy.EventId == eventId &&
        (requestedReference is null || string.Equals(requestedReference, policy.ComparisonRef, StringComparison.Ordinal)) &&
        IsValid(policy);

    public static bool IsValid(ApprovedFeedbackComparisonPolicy policy)
    {
        if (policy.TenantId == Guid.Empty || policy.EventId == Guid.Empty || policy.VersionNo <= 0 ||
            string.IsNullOrWhiteSpace(policy.ComparisonRef) || policy.MinimumSampleSize < 2 ||
            policy.Cohorts is not { Count: >= 1 and <= MaximumPresentations } ||
            policy.Fields is not { Count: >= 1 and <= MaximumFields })
        {
            return false;
        }

        if (policy.Cohorts.Any(cohort => cohort is null || string.IsNullOrWhiteSpace(cohort.Label) ||
                cohort.PresentationIds is not { Count: >= 2 and <= MaximumPresentations }) ||
            policy.Cohorts.Select(cohort => cohort.Label).Distinct(StringComparer.Ordinal).Count() != policy.Cohorts.Count)
        {
            return false;
        }

        var presentationIds = policy.Cohorts.SelectMany(cohort => cohort.PresentationIds).ToArray();
        if (presentationIds.Length > MaximumPresentations || presentationIds.Contains(Guid.Empty) ||
            presentationIds.Distinct().Count() != presentationIds.Length)
        {
            return false;
        }

        return policy.Fields.All(field => field is not null && !string.IsNullOrWhiteSpace(field.FieldKey) &&
                   field.CompatibilityRootFieldId != Guid.Empty &&
                   field.FieldType is SurveyFieldType.Number or SurveyFieldType.Scale or
                                      SurveyFieldType.SingleChoice or SurveyFieldType.MultipleChoice) &&
               policy.Fields.Select(field => field.FieldKey).Distinct(StringComparer.Ordinal).Count() == policy.Fields.Count;
    }

    public static FeedbackComparisonCohort Project(ApprovedFeedbackComparisonPolicy policy,
        ApprovedFeedbackComparisonCohort cohort, IReadOnlyList<FeedbackComparisonSample> samples)
    {
        var fields = samples.ToLookup(sample => sample.FieldKey, StringComparer.Ordinal);
        return new FeedbackComparisonCohort(cohort.Label, policy.Fields
            .Select(field => ProjectCell(field, fields[field.FieldKey].ToArray(), policy.MinimumSampleSize)).ToArray());
    }

    private static FeedbackComparisonCell ProjectCell(ApprovedFeedbackComparisonField field,
        FeedbackComparisonSample[] samples, int minimum)
    {
        var suppressed = new FeedbackComparisonCell(field.FieldKey, Suppressed: true);
        if (samples.Length < minimum ||
            samples.Any(sample => sample.SubmissionId == Guid.Empty ||
                sample.CompatibilityRootFieldId != field.CompatibilityRootFieldId || sample.FieldType != field.FieldType) ||
            samples.Select(sample => sample.SubmissionId).Distinct().Count() != samples.Length)
        {
            return suppressed;
        }

        try
        {
            return field.FieldType is SurveyFieldType.Number or SurveyFieldType.Scale
                ? ProjectMean(field, samples, suppressed)
                : ProjectDistribution(field, samples, minimum, suppressed);
        }
        catch (JsonException)
        {
            return suppressed;
        }
    }

    private static FeedbackComparisonCell ProjectMean(ApprovedFeedbackComparisonField field,
        FeedbackComparisonSample[] samples, FeedbackComparisonCell suppressed)
    {
        var mean = 0m;
        var count = 0;
        foreach (var sample in samples)
        {
            using var document = JsonDocument.Parse(sample.ValueJson);
            if (document.RootElement.ValueKind != JsonValueKind.Number || !document.RootElement.TryGetDecimal(out var number))
            {
                return suppressed;
            }

            count++;
            mean = count == 1 ? number : SurveyFeedbackAggregation.AppendToAverage(mean, number, count);
        }

        return new FeedbackComparisonCell(field.FieldKey, Suppressed: false, count, Mean: mean);
    }

    private static FeedbackComparisonCell ProjectDistribution(ApprovedFeedbackComparisonField field,
        FeedbackComparisonSample[] samples, int minimum, FeedbackComparisonCell suppressed)
    {
        var choices = new Dictionary<string, long>(StringComparer.Ordinal);
        var patterns = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var sample in samples)
        {
            using var document = JsonDocument.Parse(sample.ValueJson);
            var keys = ReadOptions(document.RootElement, field.FieldType);
            if (keys is null)
            {
                return suppressed;
            }

            foreach (var key in keys)
            {
                choices[key] = choices.GetValueOrDefault(key) + 1;
            }

            var pattern = JsonSerializer.Serialize(keys.Order(StringComparer.Ordinal));
            patterns[pattern] = patterns.GetValueOrDefault(pattern) + 1;
        }

        // Sparse option buckets or complements can disclose small groups by subtraction.
        // For multiple selections also require support for complete selection patterns.
        if (choices.Count == 0 || choices.Values.Any(count => count < minimum || samples.Length - count < minimum) ||
            patterns.Values.Any(count => count < minimum))
        {
            return suppressed;
        }

        var distribution = choices.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new FeedbackOptionCount(item.Key, item.Value)).ToArray();
        return new FeedbackComparisonCell(field.FieldKey, Suppressed: false, samples.Length, distribution);
    }

    private static string[]? ReadOptions(JsonElement answer, SurveyFieldType fieldType)
    {
        if (fieldType == SurveyFieldType.SingleChoice && answer.ValueKind == JsonValueKind.String)
        {
            var key = answer.GetString();
            return string.IsNullOrWhiteSpace(key) ? null : [key];
        }
        if (fieldType != SurveyFieldType.MultipleChoice || answer.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in answer.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } key ||
                string.IsNullOrWhiteSpace(key) || !values.Add(key))
            {
                return null;
            }
        }

        return values.ToArray();
    }
}
