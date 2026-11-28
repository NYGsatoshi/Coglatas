using Coglatas.Application.SurveyFeedback;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Aq;

[Trait("Feature", "AQ")]
public sealed class AqVersionCompatibilityTests
{
    private static readonly DateTimeOffset Publication = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly SurveyField Rating = new("rating", "Raiting", SurveyFieldType.Number, true, []);

    [Fact]
    public void ExplicitCorrectionsRetainOneCompatibilityRootAcrossHistoricalVersions()
    {
        var form = CreateSurvey();
        var original = FormVersion.Publish(form, 1, [Rating], Publication);
        var second = FormVersion.PublishCorrection(form, original, 2, [Rating with { Label = "Rating" }],
            Publication.AddMinutes(1), "Correct a typo");
        var third = FormVersion.PublishCorrection(form, second, 3, [Rating with { Label = "Rating score" }],
            Publication.AddMinutes(2), "Clarify the label");
        var oldField = Assert.Single(original.Fields);
        var newField = Assert.Single(second.Fields);
        var latestField = Assert.Single(third.Fields);

        Assert.Equal("Raiting", oldField.Label);
        Assert.Equal("Rating", newField.Label);
        Assert.Null(oldField.CompatibilityRootFieldId);
        Assert.Equal(oldField.Id, newField.CompatiblePreviousFieldId);
        Assert.Equal(newField.Id, latestField.CompatiblePreviousFieldId);
        Assert.Equal(oldField.Id, latestField.CompatibilityRootFieldId);
        Assert.Equal("Clarify the label", third.CompatibilityChangeReason);
        Assert.NotEqual(original.SnapshotJson, second.SnapshotJson);

        var summary = SurveyFeedbackAggregation.Summarize(new FeedbackSummaryInput(
            3, [Value(oldField, "2"), Value(newField, "4"), Value(latestField, "6")]));
        var question = Assert.Single(summary.Questions);
        Assert.Equal(3, question.Denominator);
        Assert.Equal(4m, question.Mean);
    }

    [Fact]
    public void AnUnlinkedOrIncompatibleVersionDoesNotPoolTheSameFieldKey()
    {
        var form = CreateSurvey();
        var original = FormVersion.Publish(form, 1, [Rating], Publication);
        var unlinked = FormVersion.Publish(form, 2, [Rating with { Label = "Rating" }], Publication.AddMinutes(1));
        Assert.Empty(SurveyFeedbackAggregation.Summarize(new FeedbackSummaryInput(
            2, [Value(Assert.Single(original.Fields), "2"), Value(Assert.Single(unlinked.Fields), "4")])).Questions);

        Assert.Throws<ArgumentException>(() => FormVersion.PublishCorrection(form, original, 2,
            [Rating with { Constraints = new SurveyFieldConstraints(Maximum: 10) }], Publication.AddMinutes(1),
            "Changes the answer bounds"));
        Assert.Throws<ArgumentException>(() => FormVersion.PublishCorrection(CreateSurvey(), original, 2,
            [Rating], Publication.AddMinutes(1), "Foreign form scope"));
    }

    private static FormDefinition CreateSurvey() =>
        FormDefinition.Create(Guid.NewGuid(), Guid.NewGuid(), null, null, Guid.NewGuid(), FormPurpose.Survey, "Survey");

    private static FeedbackValueSnapshot Value(FormFieldDefinition field, string json) =>
        new(Guid.NewGuid(), field.FieldKey, field.FieldType.ToString(),
            (field.CompatibilityRootFieldId ?? field.Id).ToString("D"), json, false);
}
