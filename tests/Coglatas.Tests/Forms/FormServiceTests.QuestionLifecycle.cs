using System.Text.Json;
using Coglatas.Application.Forms;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Forms;

public sealed partial class FormServiceTests
{
    [Theory]
    [InlineData("blank-text")]
    [InlineData("invalid-type")]
    [InlineData("missing-options")]
    [InlineData("empty-options")]
    [InlineData("blank-option")]
    [InlineData("trimmed-duplicate")]
    [InlineData("multiple-duplicate")]
    [InlineData("text-options")]
    public async Task InvalidQuestionCreatesAndUpdatesPreserveTheOriginalQuestion(string scenario)
    {
        var (fixture, form, _) = CreateQuestionManagerFixture();
        var question = fixture.AddQuestion(form, FormQuestionType.SingleChoice, true, ["A", "B"]);
        var before = QuestionState(question);
        var request = InvalidQuestionRequest(scenario);
        var expectedError = scenario switch
        {
            "blank-text" => "Question text is required.",
            "invalid-type" => "Question type is invalid.",
            "trimmed-duplicate" or "multiple-duplicate" => "Choice question options must be unique.",
            "text-options" => "Options are only allowed for choice questions.",
            _ => "Choice questions require at least one non-empty option."
        };

        var created = await fixture.Service.AddQuestionAsync(form.Id, request);
        // An omitted update keeps the existing choices; explicitly empty options exercise its rejection.
        var updated = await fixture.Service.UpdateQuestionAsync(form.Id, question.Id,
            new UpdateFormQuestionRequest(request.QuestionText, request.QuestionType,
                IsRequired: false, SortOrder: 99, Options: request.Options ?? []));

        Assert.False(created.IsSuccess);
        Assert.Equal(expectedError, created.Error);
        Assert.False(updated.IsSuccess);
        Assert.Equal(expectedError, updated.Error);
        Assert.Same(question, Assert.Single(form.Questions));
        Assert.Equal(before, QuestionState(question));
        Assert.Empty(form.Responses);
        Assert.Empty(fixture.StoredResponses);
        Assert.Equal(0, fixture.AuditCalls);
        Assert.Equal(0, fixture.SaveCalls);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    public async Task MissingFormOrUnauthenticatedQuestionMutationHasNoEffects(int action, bool missingForm)
    {
        var (fixture, form, _) = CreateQuestionManagerFixture();
        var question = fixture.AddQuestion(form, FormQuestionType.ShortText);
        var before = QuestionState(question);
        var formId = missingForm ? Guid.NewGuid() : form.Id;
        if (!missingForm)
            fixture.Current.UserIdValue = null;

        var outcome = action switch
        {
            0 => QuestionMutationOutcome(await fixture.Service.AddQuestionAsync(formId,
                new CreateFormQuestionRequest("New question", FormQuestionType.Boolean))),
            1 => QuestionMutationOutcome(await fixture.Service.UpdateQuestionAsync(formId, question.Id,
                new UpdateFormQuestionRequest(QuestionText: "Changed question"))),
            _ => QuestionMutationOutcome(await fixture.Service.DeleteQuestionAsync(formId, question.Id))
        };

        Assert.False(outcome.Success);
        Assert.Equal(missingForm ? "Form not found." : "You are not allowed to update questions for this form.", outcome.Error);
        Assert.Same(question, Assert.Single(form.Questions));
        Assert.Equal(before, QuestionState(question));
        Assert.Empty(form.Responses);
        Assert.Empty(fixture.StoredResponses);
        Assert.Equal(0, fixture.AuditCalls);
        Assert.Equal(0, fixture.SaveCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuestionFromAnotherFormCannotBeChangedOrDeleted(bool delete)
    {
        var (fixture, form, _) = CreateQuestionManagerFixture();
        var selected = fixture.AddQuestion(form, FormQuestionType.Boolean);
        var otherForm = fixture.AddForm(FormStatus.Draft);
        var foreign = fixture.AddQuestion(otherForm, FormQuestionType.SingleChoice, true, ["private-option"]);
        var selectedBefore = QuestionState(selected);
        var foreignBefore = QuestionState(foreign);

        var outcome = delete
            ? QuestionMutationOutcome(await fixture.Service.DeleteQuestionAsync(form.Id, foreign.Id))
            : QuestionMutationOutcome(await fixture.Service.UpdateQuestionAsync(form.Id, foreign.Id,
                new UpdateFormQuestionRequest(QuestionText: "Changed question")));

        Assert.False(outcome.Success);
        Assert.Equal("Question not found.", outcome.Error);
        Assert.Same(selected, Assert.Single(form.Questions));
        Assert.Same(foreign, Assert.Single(otherForm.Questions));
        Assert.Equal(selectedBefore, QuestionState(selected));
        Assert.Equal(foreignBefore, QuestionState(foreign));
        Assert.Equal(0, fixture.AuditCalls);
        Assert.Equal(0, fixture.SaveCalls);
    }

    [Fact]
    public async Task AnsweredQuestionRejectsTypeChangeAndDeletionWithoutLosingItsAnswer()
    {
        var (fixture, form, manager) = CreateQuestionManagerFixture();
        var question = fixture.AddQuestion(form, FormQuestionType.ShortText, true);
        fixture.AddResponse(form, manager.Id, [(question, "Retained answer", null)]);
        var response = Assert.Single(fixture.StoredResponses);
        var answer = Assert.Single(response.Answers);
        var before = QuestionState(question);

        var updated = await fixture.Service.UpdateQuestionAsync(form.Id, question.Id,
            new UpdateFormQuestionRequest("Changed", FormQuestionType.Boolean, false, 99));
        var deleted = await fixture.Service.DeleteQuestionAsync(form.Id, question.Id);

        Assert.False(updated.IsSuccess);
        Assert.Equal("Question type cannot be changed after responses exist.", updated.Error);
        Assert.False(deleted.IsSuccess);
        Assert.Equal("Questions cannot be deleted after responses exist.", deleted.Error);
        Assert.Same(question, Assert.Single(form.Questions));
        Assert.Equal(before, QuestionState(question));
        Assert.Same(response, Assert.Single(form.Responses));
        Assert.Same(response, Assert.Single(fixture.StoredResponses));
        Assert.Same(answer, Assert.Single(response.Answers));
        Assert.Equal(question.Id, answer.FormQuestionId);
        Assert.Equal(response.Id, answer.FormResponseId);
        Assert.Equal("Retained answer", answer.AnswerText);
        Assert.Null(answer.AnswerJson);
        Assert.Equal(manager.Id, response.RespondentUserId);
        Assert.Equal(0, fixture.AuditCalls);
        Assert.Equal(0, fixture.SaveCalls);
    }

    [Fact]
    public async Task QuestionLifecycleNormalizesChoicesRetainsOmittedOptionsAndClearsThemForText()
    {
        var (fixture, form, _) = CreateQuestionManagerFixture();
        var created = await fixture.Service.AddQuestionAsync(form.Id,
            new CreateFormQuestionRequest("  Choose  ", FormQuestionType.SingleChoice, true, -4, [" B ", "A"]));
        Assert.True(created.IsSuccess, created.Error);
        var question = Assert.Single(form.Questions);
        Assert.NotEqual(Guid.Empty, question.Id);
        Assert.Equal(form.Id, question.FormId);
        AssertQuestionProjection(created.Value!, question, "Choose", FormQuestionType.SingleChoice, true, -4, ["B", "A"]);
        Assert.Equal(1, fixture.AuditCalls);
        Assert.Equal(1, fixture.SaveCalls);

        var updated = await fixture.Service.UpdateQuestionAsync(form.Id, question.Id,
            new UpdateFormQuestionRequest(" Changed ", FormQuestionType.MultipleChoice, false, 99, [" C ", "D"]));
        Assert.True(updated.IsSuccess, updated.Error);
        AssertQuestionProjection(updated.Value!, question, "Changed", FormQuestionType.MultipleChoice, false, 99, ["C", "D"]);

        var retained = await fixture.Service.UpdateQuestionAsync(form.Id, question.Id,
            new UpdateFormQuestionRequest(QuestionText: " Retained "));
        Assert.True(retained.IsSuccess, retained.Error);
        AssertQuestionProjection(retained.Value!, question, "Retained", FormQuestionType.MultipleChoice, false, 99, ["C", "D"]);

        var text = await fixture.Service.UpdateQuestionAsync(form.Id, question.Id,
            new UpdateFormQuestionRequest(QuestionType: FormQuestionType.LongText));
        Assert.True(text.IsSuccess, text.Error);
        AssertQuestionProjection(text.Value!, question, "Retained", FormQuestionType.LongText, false, 99, null);
        Assert.Null(question.OptionsJson);
        var deleted = await fixture.Service.DeleteQuestionAsync(form.Id, question.Id);
        Assert.True(deleted.IsSuccess, deleted.Error);
        Assert.Empty(form.Questions);
        Assert.Empty(fixture.StoredResponses);
        Assert.Equal(5, fixture.AuditCalls);
        Assert.Equal(5, fixture.SaveCalls);
    }

    [Fact]
    public async Task RepositoryQuestionFallbackStillUpdatesAndDeletesTheSelectedQuestion()
    {
        var (fixture, form, _) = CreateQuestionManagerFixture();
        var question = fixture.AddQuestion(form, FormQuestionType.Boolean, true);
        form.Questions.Remove(question);

        var updated = await fixture.Service.UpdateQuestionAsync(form.Id, question.Id,
            new UpdateFormQuestionRequest("  Repository question  ", SortOrder: 7));
        Assert.True(updated.IsSuccess, updated.Error);
        AssertQuestionProjection(updated.Value!, question, "Repository question", FormQuestionType.Boolean, true, 7, null);
        var deleted = await fixture.Service.DeleteQuestionAsync(form.Id, question.Id);
        Assert.True(deleted.IsSuccess, deleted.Error);
        var repeated = await fixture.Service.DeleteQuestionAsync(form.Id, question.Id);
        Assert.False(repeated.IsSuccess);
        Assert.Equal("Question not found.", repeated.Error);
        Assert.Empty(form.Questions);
        Assert.Equal(2, fixture.AuditCalls);
        Assert.Equal(2, fixture.SaveCalls);
    }

    private static (FormFixture Fixture, InternalForm Form, User Manager) CreateQuestionManagerFixture()
    {
        var fixture = FormFixture.Create();
        var manager = fixture.AddUser();
        fixture.AddWorkspaceMember(manager.Id, WorkspaceRole.Admin);
        fixture.Current.UserIdValue = manager.Id;
        return (fixture, fixture.AddForm(FormStatus.Draft), manager);
    }

    private static CreateFormQuestionRequest InvalidQuestionRequest(string scenario) => scenario switch
    {
        "blank-text" => new("  ", FormQuestionType.ShortText),
        "invalid-type" => new("Question", (FormQuestionType)(-1)),
        "missing-options" => new("Question", FormQuestionType.SingleChoice),
        "empty-options" => new("Question", FormQuestionType.MultipleChoice, Options: []),
        "blank-option" => new("Question", FormQuestionType.SingleChoice, Options: ["A", " "]),
        "trimmed-duplicate" => new("Question", FormQuestionType.SingleChoice, Options: [" A ", "A"]),
        "multiple-duplicate" => new("Question", FormQuestionType.MultipleChoice, Options: ["B", " B "]),
        "text-options" => new("Question", FormQuestionType.ShortText, Options: ["A"]),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario))
    };

    private static string QuestionState(FormQuestion question) => JsonSerializer.Serialize(new
    {
        question.Id, question.TenantId, question.FormId, question.QuestionText, question.QuestionType,
        question.IsRequired, question.SortOrder, question.OptionsJson, question.CreatedAt, question.UpdatedAt
    });

    private static (bool Success, string? Error) QuestionMutationOutcome(Coglatas.Application.Common.Result result) =>
        (result.IsSuccess, result.Error);

    private static (bool Success, string? Error) QuestionMutationOutcome<T>(Coglatas.Application.Common.Result<T> result) =>
        (result.IsSuccess, result.Error);

    private static void AssertQuestionProjection(FormQuestionResponse actual, FormQuestion stored,
        string text, FormQuestionType type, bool required, int order, IReadOnlyList<string>? options)
    {
        Assert.Equal(stored.Id, actual.Id);
        Assert.Equal(text, actual.QuestionText);
        Assert.Equal(type, actual.QuestionType);
        Assert.Equal(required, actual.IsRequired);
        Assert.Equal(order, actual.SortOrder);
        Assert.Equal(options, actual.Options);
        Assert.Equal(text, stored.QuestionText);
        Assert.Equal(type, stored.QuestionType);
        Assert.Equal(required, stored.IsRequired);
        Assert.Equal(order, stored.SortOrder);
        Assert.Equal(options is null ? null : JsonSerializer.Serialize(options), stored.OptionsJson);
    }
}
