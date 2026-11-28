using System.Text.Json;
using Coglatas.Application.SurveyFeedback;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Aq;

public sealed class AqFeedbackComparisonTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid EventId = Guid.Parse("20000000-0000-4000-8000-000000000001");
    private static readonly Guid RootFieldId = Guid.Parse("30000000-0000-4000-8000-000000000001");
    private static readonly Guid LeftPresentationId = Guid.Parse("40000000-0000-4000-8000-000000000001");
    private static readonly Guid RightPresentationId = Guid.Parse("40000000-0000-4000-8000-000000000002");
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public void AnUnavailableOrMismatchedPolicyOmitsTheComparison()
    {
        var policy = Policy(SurveyFieldType.Number);
        Assert.False(SurveyFeedbackComparisonProjection.Matches(null, TenantId, EventId, null));
        Assert.False(SurveyFeedbackComparisonProjection.Matches(policy, Guid.NewGuid(), EventId, null));
        Assert.False(SurveyFeedbackComparisonProjection.Matches(policy, TenantId, Guid.NewGuid(), null));
        Assert.False(SurveyFeedbackComparisonProjection.Matches(policy, TenantId, EventId, "other-reference"));
        Assert.True(SurveyFeedbackComparisonProjection.Matches(policy, TenantId, EventId, policy.ComparisonRef));
        Assert.Equal("{}", JsonSerializer.Serialize(new FeedbackComparison(), WebJson));
    }

    [Fact]
    public void ApprovedDisjointCohortsAndKnownLineageAreRequired()
    {
        var policy = Policy(SurveyFieldType.Number);
        Assert.True(SurveyFeedbackComparisonProjection.IsValid(policy));
        Assert.False(SurveyFeedbackComparisonProjection.IsValid(policy with { MinimumSampleSize = 1 }));
        Assert.False(SurveyFeedbackComparisonProjection.IsValid(policy with
        {
            Cohorts =
            [
                .. policy.Cohorts,
                new ApprovedFeedbackComparisonCohort("Second", [LeftPresentationId, Guid.NewGuid()])
            ]
        }));
        Assert.False(SurveyFeedbackComparisonProjection.IsValid(policy with
        {
            Fields = [new ApprovedFeedbackComparisonField("score", Guid.Empty, SurveyFieldType.Number)]
        }));
        Assert.False(SurveyFeedbackComparisonProjection.IsValid(Policy(SurveyFieldType.Text)));
    }

    [Fact]
    public void EligibleNumericValuesReleaseOnlyMeanAndSampleSize()
    {
        var cell = Project(Policy(SurveyFieldType.Number),
            Sample("2"), Sample("4"), Sample("6"));

        Assert.False(cell.Suppressed);
        Assert.Equal(3, cell.SampleSize);
        Assert.Equal(4m, cell.Mean);
        Assert.Null(cell.Distribution);
    }

    [Fact]
    public void UndersizedIncompatibleOrDuplicateValuesOmitAllStatistics()
    {
        var policy = Policy(SurveyFieldType.Number);
        var first = Sample("2");
        var second = Sample("4");
        AssertSuppressed(Project(policy, first));
        AssertSuppressed(Project(policy, first, second with { CompatibilityRootFieldId = Guid.NewGuid() }));
        AssertSuppressed(Project(policy, first, second with { SubmissionId = first.SubmissionId }));
        AssertSuppressed(Project(policy, first, second with { ValueJson = "\"invalid\"" }));
    }

    [Fact]
    public void ChoiceCellsAndComplementsMustMeetTheMinimum()
    {
        var policy = Policy(SurveyFieldType.SingleChoice);
        var a = Sample("\"a\"", SurveyFieldType.SingleChoice);
        var b = Sample("\"b\"", SurveyFieldType.SingleChoice);
        AssertSuppressed(Project(policy, a, a with { SubmissionId = Guid.NewGuid() }, b));
        AssertSuppressed(Project(policy, a, a with { SubmissionId = Guid.NewGuid() }));
        var released = Project(policy, a, a with { SubmissionId = Guid.NewGuid() },
            b, b with { SubmissionId = Guid.NewGuid() });

        Assert.False(released.Suppressed);
        Assert.Equal(4, released.SampleSize);
        Assert.Equal([new FeedbackOptionCount("a", 2), new FeedbackOptionCount("b", 2)],
            released.Distribution);
        Assert.Null(released.Mean);
    }

    [Fact]
    public void SmallMultiChoicePatternsAreSuppressedEvenWithSafeMarginals()
    {
        var policy = Policy(SurveyFieldType.MultipleChoice);
        AssertSuppressed(Project(policy, Sample("[\"a\",\"b\"]", SurveyFieldType.MultipleChoice),
            Sample("[\"a\"]", SurveyFieldType.MultipleChoice), Sample("[\"b\"]", SurveyFieldType.MultipleChoice),
            Sample("[]", SurveyFieldType.MultipleChoice)));

        var released = Project(policy, Sample("[\"a\"]", SurveyFieldType.MultipleChoice),
            Sample("[\"a\"]", SurveyFieldType.MultipleChoice),
            Sample("[\"b\",\"c\"]", SurveyFieldType.MultipleChoice),
            Sample("[\"c\",\"b\"]", SurveyFieldType.MultipleChoice));
        Assert.False(released.Suppressed);
        Assert.Equal([new FeedbackOptionCount("a", 2), new FeedbackOptionCount("b", 2),
            new FeedbackOptionCount("c", 2)], released.Distribution);
    }

    private static ApprovedFeedbackComparisonPolicy Policy(SurveyFieldType type) =>
        new(TenantId, EventId, "approved-fixed-cohorts", 1, 2,
            [new ApprovedFeedbackComparisonCohort("Cohort", [LeftPresentationId, RightPresentationId])],
            [new ApprovedFeedbackComparisonField("score", RootFieldId, type)]);

    private static FeedbackComparisonSample Sample(string value, SurveyFieldType type = SurveyFieldType.Number) =>
        new(Guid.NewGuid(), "score", RootFieldId, type, value);

    private static FeedbackComparisonCell Project(ApprovedFeedbackComparisonPolicy policy,
        params FeedbackComparisonSample[] values) =>
        Assert.Single(SurveyFeedbackComparisonProjection.Project(policy, Assert.Single(policy.Cohorts), values).Cells);

    private static void AssertSuppressed(FeedbackComparisonCell cell)
    {
        Assert.True(cell.Suppressed);
        Assert.Equal("{\"fieldKey\":\"score\",\"suppressed\":true}", JsonSerializer.Serialize(cell, WebJson));
    }
}
