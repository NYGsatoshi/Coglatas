using Coglatas.Application.Forms;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Forms;

public sealed partial class FormServiceTests
{
    [Theory]
    [InlineData(FormQuestionType.ShortText, "  answer  ", null, "answer", null)]
    [InlineData(FormQuestionType.LongText, "  first\nsecond  ", null, "first\nsecond", null)]
    [InlineData(FormQuestionType.Boolean, "TRUE", null, "true", null)]
    [InlineData(FormQuestionType.Boolean, "false", null, "false", null)]
    [InlineData(FormQuestionType.Boolean, null, "true", "true", null)]
    [InlineData(FormQuestionType.Boolean, "true", "false", "false", null)]
    [InlineData(FormQuestionType.Boolean, "true", "{}", "true", null)]
    [InlineData(FormQuestionType.Date, "2026-06-07T09:15:00+09:00", null, "2026-06-07", null)]
    [InlineData(FormQuestionType.Date, null, "\"2026-06-07\"", "2026-06-07", null)]
    [InlineData(FormQuestionType.Number, "1,234.50", null, "1234.50", null)]
    [InlineData(FormQuestionType.Number, null, "-12.5", "-12.5", null)]
    [InlineData(FormQuestionType.SingleChoice, " A ", null, "A", null)]
    [InlineData(FormQuestionType.SingleChoice, null, "\"B\"", "B", null)]
    [InlineData(FormQuestionType.SingleChoice, "A", "\"B\"", "A", null)]
    [InlineData(FormQuestionType.MultipleChoice, null, "[\" A \",\"B\"]", null, "[\"A\",\"B\"]")]
    public async Task TypedAnswersStoreNormalizedValuesAndReturnTheStoredAnswer(
        FormQuestionType type, string? text, string? json, string? expectedText, string? expectedJson)
    {
        var (fixture, form, question, member) = CreateAnswerFixture(type);

        var result = await fixture.Service.SubmitResponseAsync(form.Id,
            new SubmitFormResponseRequest([new SubmitFormAnswerRequest(question.Id, text, json)]));

        Assert.True(result.IsSuccess, result.Error);
        var stored = Assert.Single(fixture.StoredResponses);
        Assert.Same(stored, Assert.Single(form.Responses));
        Assert.Equal(member.Id, stored.RespondentUserId);
        Assert.Equal(form.CreatedAt, stored.SubmittedAt);
        var answer = Assert.Single(stored.Answers);
        Assert.Equal(question.Id, answer.FormQuestionId);
        Assert.Equal(expectedText, answer.AnswerText);
        Assert.Equal(expectedJson, answer.AnswerJson);
        var returned = Assert.Single(result.Value!.Answers);
        Assert.Equal(expectedText, returned.AnswerText);
        Assert.Equal(expectedJson, returned.AnswerJson);
        Assert.Equal(1, fixture.AuditCalls);
        Assert.Equal(1, fixture.SaveCalls);
    }

    [Theory]
    [InlineData(FormQuestionType.Boolean, "yes", null)]
    [InlineData(FormQuestionType.Boolean, null, "1")]
    [InlineData(FormQuestionType.Boolean, null, "{broken")]
    [InlineData(FormQuestionType.Date, "not-a-date", null)]
    [InlineData(FormQuestionType.Date, null, "23")]
    [InlineData(FormQuestionType.Number, "NaN", null)]
    [InlineData(FormQuestionType.Number, null, "\"12\"")]
    [InlineData(FormQuestionType.Number, null, "1e10")]
    [InlineData(FormQuestionType.SingleChoice, "a", null)]
    [InlineData(FormQuestionType.SingleChoice, null, "{}")]
    [InlineData(FormQuestionType.MultipleChoice, null, "[\"A\",2]")]
    [InlineData(FormQuestionType.MultipleChoice, null, "[\"A\",\" \" ]")]
    [InlineData(FormQuestionType.MultipleChoice, null, "[\"C\"]")]
    [InlineData(FormQuestionType.MultipleChoice, null, "[]")]
    [InlineData(FormQuestionType.MultipleChoice, "A", null)]
    [InlineData(FormQuestionType.ShortText, null, "\"answer\"")]
    public async Task InvalidTypedAnswersRejectBeforeResponseAuditOrSave(
        FormQuestionType type, string? text, string? json)
    {
        var (fixture, form, question, _) = CreateAnswerFixture(type);

        var result = await fixture.Service.SubmitResponseAsync(form.Id,
            new SubmitFormResponseRequest([new SubmitFormAnswerRequest(question.Id, text, json)]));

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Empty(fixture.StoredResponses);
        Assert.Empty(form.Responses);
        Assert.Equal(0, fixture.AuditCalls);
        Assert.Equal(0, fixture.SaveCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownOrDuplicateQuestionRejectsTheWholeResponse(bool duplicate)
    {
        var (fixture, form, question, _) = CreateAnswerFixture(FormQuestionType.ShortText);
        var invalidId = duplicate ? question.Id : Guid.NewGuid();

        var result = await fixture.Service.SubmitResponseAsync(form.Id, new SubmitFormResponseRequest([
            new SubmitFormAnswerRequest(question.Id, "valid"),
            new SubmitFormAnswerRequest(invalidId, "invalid")
        ]));

        Assert.False(result.IsSuccess);
        Assert.Equal(duplicate ? "A question has duplicate answers." : "An answer references an unknown question.", result.Error);
        Assert.Empty(fixture.StoredResponses);
        Assert.Empty(form.Responses);
        Assert.Equal(0, fixture.AuditCalls);
        Assert.Equal(0, fixture.SaveCalls);
    }

    [Theory]
    [InlineData(FormQuestionType.ShortText, " ", null)]
    [InlineData(FormQuestionType.Boolean, null, null)]
    [InlineData(FormQuestionType.MultipleChoice, null, "[]")]
    public async Task OptionalEmptyAnswersDoNotCreateEmptyAnswerRows(FormQuestionType type, string? text, string? json)
    {
        var (fixture, form, question, _) = CreateAnswerFixture(type, required: false);

        var result = await fixture.Service.SubmitResponseAsync(form.Id,
            new SubmitFormResponseRequest([new SubmitFormAnswerRequest(question.Id, text, json)]));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(Assert.Single(fixture.StoredResponses).Answers);
        Assert.Empty(result.Value!.Answers);
        Assert.Equal(1, fixture.AuditCalls);
        Assert.Equal(1, fixture.SaveCalls);
    }

    [Theory]
    [InlineData(-1, 0, true)]
    [InlineData(0, 1, true)]
    [InlineData(1, 2, false)]
    [InlineData(-2, -1, false)]
    public async Task ResponseWindowIncludesBothExactBoundaries(int opensOffset, int closesOffset, bool accepted)
    {
        var (fixture, form, question, _) = CreateAnswerFixture(FormQuestionType.ShortText);
        form.OpensAt = form.CreatedAt.AddTicks(opensOffset);
        form.ClosesAt = form.CreatedAt.AddTicks(closesOffset);

        var result = await fixture.Service.SubmitResponseAsync(form.Id,
            new SubmitFormResponseRequest([new SubmitFormAnswerRequest(question.Id, "answer")]));

        Assert.Equal(accepted, result.IsSuccess);
        Assert.Equal(accepted ? 1 : 0, fixture.StoredResponses.Count);
        Assert.Equal(accepted ? 1 : 0, fixture.AuditCalls);
        Assert.Equal(accepted ? 1 : 0, fixture.SaveCalls);
        Assert.Equal(accepted ? null : "This form is not accepting responses.", result.Error);
    }

    [Fact]
    public async Task AnonymousSubmissionMasksTheDtoButRetainsIdentityForDuplicateAdmission()
    {
        var (fixture, form, question, member) = CreateAnswerFixture(FormQuestionType.ShortText);
        form.IsAnonymous = true;
        var request = new SubmitFormResponseRequest([new SubmitFormAnswerRequest(question.Id, "answer")]);

        var first = await fixture.Service.SubmitResponseAsync(form.Id, request);
        var duplicate = await fixture.Service.SubmitResponseAsync(form.Id, request);

        Assert.True(first.IsSuccess, first.Error);
        Assert.Null(first.Value!.RespondentUserId);
        Assert.Null(first.Value.RespondentDisplayName);
        Assert.Null(first.Value.RespondentEmail);
        Assert.Equal(member.Id, Assert.Single(fixture.StoredResponses).RespondentUserId);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal("You have already submitted a response for this form.", duplicate.Error);
        Assert.Single(form.Responses);
        Assert.Single(Assert.Single(fixture.StoredResponses).Answers);
        Assert.Equal(1, fixture.AuditCalls);
        Assert.Equal(1, fixture.SaveCalls);
    }

    private static (FormFixture Fixture, InternalForm Form, FormQuestion Question, User Member) CreateAnswerFixture(
        FormQuestionType type, bool required = true)
    {
        var fixture = FormFixture.Create();
        var member = fixture.AddUser();
        fixture.AddWorkspaceMember(member.Id, WorkspaceRole.Member);
        fixture.Current.UserIdValue = member.Id;
        var form = fixture.AddForm(FormStatus.Open);
        var options = type is FormQuestionType.SingleChoice or FormQuestionType.MultipleChoice
            ? new[] { "A", "B" }
            : null;
        var question = fixture.AddQuestion(form, type, required, options);
        return (fixture, form, question, member);
    }
}
