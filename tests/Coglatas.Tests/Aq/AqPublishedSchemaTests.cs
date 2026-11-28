using System.Text.Json;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Aq;

[Trait("Feature", "AQ")]
public sealed class AqPublishedSchemaTests
{
    private static readonly DateTimeOffset PublishedAt = new(2026, 11, 24, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PublishingCopiesQuestionAndOptionIdentities()
    {
        var options = new List<SurveyOption> { new("green", "Green"), new("blue", "Blue") };
        var fields = new List<SurveyField>
        {
            new("color", "Preferred colour", SurveyFieldType.SingleChoice, true, options)
        };

        var definition = NewDefinition();
        var first = FormVersion.Publish(definition, 1, fields, PublishedAt);
        var firstSnapshot = first.SnapshotJson;
        options[0] = new SurveyOption("green", "Light green");
        fields[0] = fields[0] with { Label = "Preferred color" };
        var second = FormVersion.Publish(definition, 2, fields, PublishedAt.AddMinutes(1));

        Assert.Equal("Preferred colour", Assert.Single(first.Fields).Label);
        Assert.Equal("Preferred color", Assert.Single(second.Fields).Label);
        Assert.Equal(first.Fields[0].FieldKey, second.Fields[0].FieldKey);
        Assert.Equal(firstSnapshot, first.SnapshotJson);
        Assert.NotEqual(first.SnapshotJson, second.SnapshotJson);
        var firstColor = Assert.Single(PublishedSurveySchema.ReadFields(firstSnapshot, "[]"));
        Assert.Equal(new SurveyOption("green", "Green"), firstColor.Options[0]);
        Assert.Equal(new SurveyOption("blue", "Blue"), firstColor.Options[1]);
    }

    [Fact]
    public void PublishedSnapshotRejectsRepeatedQuestionAndOptionKeys()
    {
        var form = NewDefinition();
        var text = new SurveyField("same", "Comment", SurveyFieldType.Text, false, []);
        Assert.Throws<ArgumentException>(() =>
            FormVersion.Publish(form, 1, [text, text with { Label = "Duplicate question" }], PublishedAt));

        var choice = new SurveyField("choice", "Choice", SurveyFieldType.SingleChoice, false,
            [new("same", "Option A"), new("same", "Option B")]);
        Assert.Throws<ArgumentException>(() =>
            FormVersion.Publish(form, 1, [choice], PublishedAt));
    }

    [Fact]
    public void CompositionDigestChangesWhenTheVersionOrderOrContentChanges()
    {
        const string common = """[{"fieldKey":"common","label":"Common"}]""";
        const string additional = """[{"fieldKey":"extra","label":"Extra"}]""";
        var original = PublishedSurveySchema.ComputeDigest(common, additional);

        Assert.Matches("^[0-9a-f]{64}$", original);
        Assert.Equal(original, PublishedSurveySchema.ComputeDigest(common, additional));
        Assert.NotEqual(original, PublishedSurveySchema.ComputeDigest(additional, common));
        Assert.NotEqual(original, PublishedSurveySchema.ComputeDigest(common, "[]"));
        Assert.NotEqual(original, PublishedSurveySchema.ComputeDigest(common.Replace("Common", "Revised", StringComparison.Ordinal), additional));
    }

    [Fact]
    public void CompositionPinsCommonFieldsBeforeTargetFieldsAndSupportsEmptyAdditionalVersion()
    {
        var (_, definition, target) = NewPublicationScope();
        var comment = new SurveyField("comment", "Comment", SurveyFieldType.Text, false, []);
        var extra = new SurveyField("helpful", "Helpful", SurveyFieldType.Boolean, true, []);
        var common = FormVersion.Publish(definition, 1, [comment], PublishedAt);
        var empty = TargetQuestionSetVersion.Publish(target, 1, [], PublishedAt);
        var additional = TargetQuestionSetVersion.Publish(target, 2, [extra], PublishedAt);

        var noAdditions = PublishedSurveySchema.Compose(target, common, empty, "composition-1", PublishedAt);
        var composition = PublishedSurveySchema.Compose(target, common, additional, "composition-2", PublishedAt);

        Assert.Equal(common.Fields[0].Id, Assert.Single(noAdditions.Fields).FormFieldDefinitionId);
        Assert.Equal([common.Fields[0].Id, additional.Fields[0].Id],
            composition.Fields.Select(field => field.FormFieldDefinitionId));
        Assert.Equal([0, 1], composition.Fields.Select(field => field.SortOrder));
        Assert.Equal(common.Id, composition.FormVersionId);
        Assert.Equal(additional.Id, composition.TargetQuestionSetVersionId);
        Assert.NotEqual(noAdditions.SchemaDigest, composition.SchemaDigest);

        var duplicate = TargetQuestionSetVersion.Publish(target, 3, [comment], PublishedAt);
        Assert.Throws<ArgumentException>(() =>
            PublishedSurveySchema.Compose(target, common, duplicate, "composition-3", PublishedAt));
    }

    [Theory]
    [InlineData("3", true)]
    [InlineData("3.5", false)]
    [InlineData("6", false)]
    [InlineData("\"3\"", false)]
    public void ScaleUsesItsPublishedBoundsAndStep(string json, bool expected)
    {
        var question = new SurveyField("score", "Score", SurveyFieldType.Scale, true, [],
            new SurveyFieldConstraints(Minimum: 1, Maximum: 5, Step: 1));
        using var answer = JsonDocument.Parse(json);
        Assert.Equal(expected, question.Accepts(answer.RootElement));
    }

    [Theory]
    [InlineData("""["green","blue"]""", true)]
    [InlineData("""["green","green"]""", false)]
    [InlineData("""["Green","blue"]""", false)]
    [InlineData("""["Green option"]""", false)]
    [InlineData("[]", false)]
    public void MultipleChoiceUsesDistinctStableKeys(string json, bool expected)
    {
        var question = new SurveyField("colors", "Colors", SurveyFieldType.MultipleChoice, true,
            [new("green", "Green option"), new("blue", "Blue option")]);
        using var answer = JsonDocument.Parse(json);
        Assert.Equal(expected, question.Accepts(answer.RootElement));
    }

    [Fact]
    public void SurveySubmissionPinsValuesAndTheOnlyFeedbackTransitionIsTerminalVoid()
    {
        var (activity, definition, target) = NewPublicationScope();
        var settings = EventFeedbackSettings.Create(activity, PublishedAt, PublishedAt.AddHours(1), "UTC");
        var (common, schema, submission) = SubmitPublishedSurvey(definition, target, settings);

        Assert.Equal(target.Id, submission.SurveyTargetId);
        Assert.Equal(schema.Id, submission.PublishedSurveySchemaId);
        Assert.Equal(common.Id, submission.FormVersionId);
        Assert.Null(submission.RespondentUserId);
        var value = Assert.Single(submission.Values);
        Assert.Equal(common.Fields[0].Id, value.FormFieldDefinitionId);
        Assert.Equal(schema.Id, value.PublishedSurveySchemaId);
        Assert.Equal("\"Synthetic answer\"", value.ValueJson);
        Assert.Throws<InvalidOperationException>(() => submission.Void(submission.VersionNo + 1));
        Assert.Equal(FormSubmissionStatus.Submitted, submission.Status);

        var priorVersion = submission.VersionNo;
        submission.Void(priorVersion);
        Assert.Equal(FormSubmissionStatus.Voided, submission.Status);
        Assert.Equal(priorVersion + 1, submission.VersionNo);
        Assert.Throws<InvalidOperationException>(() => submission.Void(submission.VersionNo));
        Assert.Same(value, Assert.Single(submission.Values));
    }

    [Fact]
    public void GuestIdentityAndOptionalAccountLinkFollowIndependentPolicy()
    {
        var (activity, definition, target) = NewPublicationScope();
        var settings = EventFeedbackSettings.Create(activity, PublishedAt, PublishedAt.AddHours(1), "UTC");
        var (_, _, submission) = SubmitPublishedSurvey(definition, target, settings);
        var signedInUser = Guid.NewGuid();

        Assert.Null(SurveyRespondentIdentity.Create(submission, target, settings, null, false, signedInUser, PublishedAt));
        Assert.Throws<ArgumentException>(() =>
            SurveyRespondentIdentity.Create(submission, target, settings, null, true, signedInUser, PublishedAt));

        settings.SetParticipationPolicy(settings.VersionNo, SurveyNameMode.OptionalName, SurveyAccountBinding.ExplicitOptIn);
        Assert.Null(SurveyRespondentIdentity.Create(submission, target, settings, null, false, signedInUser, PublishedAt));

        var named = SurveyRespondentIdentity.Create(submission, target, settings, "Synthetic guest", false, signedInUser, PublishedAt);
        Assert.NotNull(named);
        Assert.Equal("Synthetic guest", named.EnteredName);
        Assert.Null(named.AccountUserId);
        Assert.Null(named.AccountConsentAtUtc);

        var linked = SurveyRespondentIdentity.Create(submission, target, settings, null, true, signedInUser, PublishedAt);
        Assert.NotNull(linked);
        Assert.Equal(submission.Id, linked.FormSubmissionId);
        Assert.Equal(signedInUser, linked.AccountUserId);
        Assert.Equal(PublishedAt, linked.AccountConsentAtUtc);
        Assert.Null(linked.EnteredName);
        Assert.Null(submission.RespondentUserId);
    }

    private static (FormVersion Common, PublishedSurveySchema Schema, FormSubmission Submission) SubmitPublishedSurvey(
        FormDefinition definition, SurveyTarget target, EventFeedbackSettings settings)
    {
        var question = new SurveyField("comment", "Comment", SurveyFieldType.Text, true, []);
        var common = FormVersion.Publish(definition, 1, [question], PublishedAt);
        var empty = TargetQuestionSetVersion.Publish(target, 1, [], PublishedAt);
        var schema = PublishedSurveySchema.Compose(target, common, empty, "composition", PublishedAt);
        target.Configure(target.VersionNo, settings, null, null, true, false);
        target.Publish(target.VersionNo, schema);

        using var answer = JsonDocument.Parse("\"Synthetic answer\"");
        var submission = FormSubmission.SubmitSurvey(definition, target, schema,
            new Dictionary<string, JsonElement>(StringComparer.Ordinal) { [question.FieldKey] = answer.RootElement },
            PublishedAt);
        return (common, schema, submission);
    }

    private static FormDefinition NewDefinition() =>
        FormDefinition.Create(Guid.NewGuid(), Guid.NewGuid(), null, null, Guid.NewGuid(),
            FormPurpose.Survey, "Synthetic composition");

    private static (ActivityEvent Activity, FormDefinition Definition, SurveyTarget Target) NewPublicationScope()
    {
        var project = new Project { TenantId = Guid.NewGuid(), WorkspaceId = Guid.NewGuid() };
        var definition = FormDefinition.Create(project.TenantId, Guid.NewGuid(), null, null, project.Id,
            FormPurpose.Survey, "Synthetic composition");
        var activity = new ActivityEvent { TenantId = project.TenantId, ProjectId = project.Id };
        var presentation = Presentation.Create(activity, "Synthetic talk", null, PublishedAt, PublishedAt.AddHours(1));
        return (activity, definition, SurveyTarget.Create(SurveyCampaign.Create(activity, project, definition), presentation, "marker"));
    }
}
