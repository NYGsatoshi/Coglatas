using System.Text;
using System.Text.Json;
using Coglatas.Application.Common;
using Coglatas.Application.Security.Redaction;
using Coglatas.Application.SurveyFeedback.Csv;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Aq;

public sealed class FeedbackCsvFormatterTests
{
    [Fact]
    public void DetailExcludesHiddenAnswersAndProtectsTextAtColumnBoundaries()
    {
        var submission = Submission();
        var visible = new FeedbackCsvAnswer(Guid.NewGuid(), submission.Id, Guid.NewGuid(), "comment",
            "@review", SurveyFieldType.Text, "[]", JsonSerializer.Serialize(" \t=55,\"included\"\nnext"), 1, false);
        var hidden = visible with { Id = Guid.NewGuid(), ValueJson = "\"private comment\"", IsHidden = true };
        var number = visible with
        {
            Id = Guid.NewGuid(),
            FieldKey = "score",
            Label = "Score",
            FieldType = SurveyFieldType.Number,
            ValueJson = "-3"
        };

        var result = Format(new("detail", ["questionKey", "questionLabel", "answerValue"]),
            Snapshot([submission], [visible, hidden, number]));

        Assert.True(result.IsSuccess);
        Assert.Equal(Encoding.UTF8.GetPreamble(), result.Value!.Content[..3]);
        var csv = Encoding.UTF8.GetString(result.Value.Content);
        Assert.Contains("\"comment\",\"'@review\",\"' \t=55,\"\"included\"\"\nnext\"\r\n", csv);
        Assert.Contains("\"score\",\"Score\",\"-3\"\r\n", csv);
        Assert.DoesNotContain("private comment", csv);
    }

    [Fact]
    public void AggregateUsesPinnedLabelsAndOmitsIncompatibleQuestionVersions()
    {
        var first = Submission();
        var second = Submission();
        var options = JsonSerializer.Serialize(new[] { new SurveyOption("a", "+Selected") });
        var choice = new FeedbackCsvAnswer(Guid.NewGuid(), first.Id, Guid.NewGuid(), "stableChoice", "Common",
            SurveyFieldType.SingleChoice, options, "\"a\"", 1, false);
        var incompatible = new FeedbackCsvAnswer(Guid.NewGuid(), first.Id, Guid.NewGuid(), "changedQuestion", "Earlier",
            SurveyFieldType.Text, "[]", "\"first\"", 1, false);
        var alternate = incompatible with { Id = Guid.NewGuid(), SubmissionId = second.Id, FieldId = Guid.NewGuid(), Label = "Later" };

        var result = Format(new("aggregate",
                ["questionKey", "responseCount", "eligibleDenominator", "choiceLabel", "choiceCount"]),
            Snapshot([first, second], [choice, incompatible, alternate]));

        Assert.True(result.IsSuccess);
        var csv = Encoding.UTF8.GetString(result.Value!.Content);
        Assert.Contains("\"stableChoice\",\"2\",\"1\",\"'+Selected\",\"1\"\r\n", csv);
        Assert.DoesNotContain("changedQuestion", csv);
        Assert.DoesNotContain("first", csv);
    }

    [Fact]
    public void PermissionSensitiveColumnsAndTotalBoundsFailClosed()
    {
        var restricted = Format(new("detail", ["respondentName"]), Snapshot([], []));
        Assert.Equal("RestrictedFieldDenied", restricted.ErrorDetail!.Code);

        var submission = Submission();
        var largeAnswer = new FeedbackCsvAnswer(Guid.NewGuid(), submission.Id, Guid.NewGuid(), "text", "Text",
            SurveyFieldType.Text, "[]", JsonSerializer.Serialize(new string('x', 2 * 1024 * 1024)), 1, false);
        var oversized = Format(new("detail", ["answerValue"]), Snapshot([submission], [largeAnswer]));
        Assert.Equal("ExportTooLarge", oversized.ErrorDetail!.Code);
        Assert.Null(oversized.Value);
    }

    [Fact]
    public void SourceFingerprintChangesAcrossStopAndVisibilityEpochsEvenForUnchangedBytes()
    {
        var submission = Submission();
        var target = new FeedbackCsvTarget(Guid.NewGuid(), 1, false);
        var answer = new FeedbackCsvAnswer(Guid.NewGuid(), submission.Id, Guid.NewGuid(), "comment", "Text",
            SurveyFieldType.Text, "[]", "\"visible text\"", 1, false);
        var snapshot = Snapshot([submission], [answer]) with { Targets = [target] };
        var request = new FeedbackCsvRequest("detail", ["questionKey"]);

        var earlier = Format(request, snapshot).Value!;
        var stoppedAndReopened = Format(request,
            snapshot with { EventSettingsVersion = snapshot.EventSettingsVersion + 2 }).Value!;
        var hiddenAndRestored = Format(request,
            snapshot with { Answers = [answer with { VisibilityVersionNo = 3 }] }).Value!;

        Assert.Equal(earlier.Content, stoppedAndReopened.Content);
        Assert.Equal(earlier.Content, hiddenAndRestored.Content);
        Assert.NotEqual(earlier.SourceDigest, stoppedAndReopened.SourceDigest);
        Assert.NotEqual(earlier.SourceDigest, hiddenAndRestored.SourceDigest);
    }

    private static Result<FeedbackCsvFile> Format(FeedbackCsvRequest request, FeedbackCsvSnapshot source) =>
        FeedbackCsvFormatter.Format(request, source, new CanonicalRedactionService(),
            new AuthorizationContext(Guid.NewGuid(), Guid.NewGuid(), "SurveyFeedback", RedactionPurpose.ExportBuild,
                "request-test", RedactionAuthorizationState.Allowed, FieldAccessPolicySnapshot.ThroughConfidential));

    private static FeedbackCsvSubmission Submission() => new(Guid.NewGuid(), DateTimeOffset.UnixEpoch, 1);

    private static FeedbackCsvSnapshot Snapshot(
        IReadOnlyList<FeedbackCsvSubmission> submissions, IReadOnlyList<FeedbackCsvAnswer> answers) =>
        new(1, 1, 1, [], submissions, answers);
}
