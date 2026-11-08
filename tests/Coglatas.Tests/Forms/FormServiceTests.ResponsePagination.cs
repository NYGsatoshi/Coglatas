using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Forms;

public sealed partial class FormServiceTests
{
    [Theory]
    [InlineData(int.MaxValue, 100, false)]
    [InlineData(1_073_741_825, 4, false)]
    [InlineData(42_949_674, 100, false)]
    [InlineData(21_474_838, 100, false)]
    [InlineData(int.MaxValue, 100, true)]
    [InlineData(1_073_741_825, 4, true)]
    [InlineData(42_949_674, 100, true)]
    [InlineData(21_474_838, 100, true)]
    public async Task ResponsePaginationCannotWrapAnOutOfRangePageIntoStoredAnswers(int page, int pageSize, bool anonymous)
    {
        var (fixture, form, _) = CreateResponsePaginationFixture(anonymous);

        var result = await fixture.Service.ListResponsesAsync(form.Id, page, pageSize);

        Assert.True(result.IsSuccess);
        Assert.Equal(page, result.Value!.Page);
        Assert.Equal(pageSize, result.Value.PageSize);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, fixture.AuditCalls);
        Assert.Equal(0, fixture.SaveCalls);
    }

    [Theory]
    [InlineData(1, 2, 1, 2, 0, 2)]
    [InlineData(2, 2, 2, 2, 2, 2)]
    [InlineData(3, 2, 3, 2, 4, 1)]
    [InlineData(4, 2, 4, 2, 5, 0)]
    [InlineData(0, 0, 1, 1, 0, 1)]
    [InlineData(-1, 200, 1, 100, 0, 5)]
    public async Task ResponsePaginationRetainsOrderingClampsAnswersAndIdentityPrivacy(
        int page, int pageSize, int expectedPage, int expectedSize, int start, int count)
    {
        foreach (var anonymous in new[] { false, true })
        {
            var (fixture, form, responses) = CreateResponsePaginationFixture(anonymous);

            var result = await fixture.Service.ListResponsesAsync(form.Id, page, pageSize);

            Assert.True(result.IsSuccess);
            Assert.Equal(expectedPage, result.Value!.Page);
            Assert.Equal(expectedSize, result.Value.PageSize);
            Assert.Equal(5, result.Value.TotalCount);
            var expected = responses.Skip(start).Take(count).ToArray();
            Assert.Equal(expected.Select(response => response.Id), result.Value.Items.Select(item => item.Id));
            for (var index = 0; index < expected.Length; index++)
            {
                var original = expected[index];
                var item = result.Value.Items[index];
                Assert.Equal(form.Id, item.FormId);
                Assert.Equal(original.SubmittedAt, item.SubmittedAt);
                Assert.Equal(anonymous ? null : original.RespondentUserId, item.RespondentUserId);
                Assert.Equal(anonymous ? null : original.RespondentUser!.DisplayName, item.RespondentDisplayName);
                Assert.Equal(anonymous ? null : original.RespondentUser!.Email, item.RespondentEmail);
                Assert.Equal(Assert.Single(original.Answers).AnswerText, Assert.Single(item.Answers).AnswerText);
            }
            Assert.Equal(0, fixture.AuditCalls);
            Assert.Equal(0, fixture.SaveCalls);
        }
    }

    [Theory]
    [InlineData("missing", "Form not found.")]
    [InlineData("anonymous", "You are not allowed to view responses for this form.")]
    [InlineData("member", "You are not allowed to view responses for this form.")]
    [InlineData("foreign", "You are not allowed to view responses for this form.")]
    public async Task ResponsePaginationPreservesAdmissionBeforeLargePageHandling(string denial, string error)
    {
        var (fixture, form, responses) = CreateResponsePaginationFixture(anonymous: false);
        var formId = form.Id;
        if (denial == "missing") formId = Guid.NewGuid();
        else if (denial == "anonymous") fixture.Current.UserIdValue = null;
        else if (denial == "member") fixture.Current.UserIdValue = responses[0].RespondentUserId;
        else { form.WorkspaceId = Guid.NewGuid(); form.Workspace = null; }

        var result = await fixture.Service.ListResponsesAsync(formId, int.MaxValue, 100);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(error, result.Error);
        Assert.Equal(0, fixture.AuditCalls);
        Assert.Equal(0, fixture.SaveCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponsePaginationPreservesAnEmptyAuthorizedList(bool anonymous)
    {
        var (fixture, _, _) = CreateResponsePaginationFixture(anonymous);
        var empty = fixture.AddForm(FormStatus.Open, anonymous);

        var result = await fixture.Service.ListResponsesAsync(empty.Id, int.MaxValue, 100);

        Assert.True(result.IsSuccess);
        Assert.Equal(int.MaxValue, result.Value!.Page);
        Assert.Equal(100, result.Value.PageSize);
        Assert.Equal(0, result.Value.TotalCount);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, fixture.AuditCalls);
        Assert.Equal(0, fixture.SaveCalls);
    }

    private static (FormFixture Fixture, InternalForm Form, FormResponse[] Responses) CreateResponsePaginationFixture(bool anonymous)
    {
        var fixture = FormFixture.Create();
        var manager = fixture.AddUser();
        fixture.AddWorkspaceMember(manager.Id, WorkspaceRole.Admin);
        fixture.Current.UserIdValue = manager.Id;
        var form = fixture.AddForm(FormStatus.Open, anonymous);
        var question = fixture.AddQuestion(form, FormQuestionType.ShortText);
        for (var index = 0; index < 5; index++)
        {
            var respondent = fixture.AddUser();
            fixture.AddWorkspaceMember(respondent.Id, WorkspaceRole.Member);
            fixture.AddResponse(form, respondent.Id, [(question, $"Selected answer {index}", null)]);
            if (index != 1) continue;
            var other = fixture.AddForm(FormStatus.Open);
            var otherQuestion = fixture.AddQuestion(other, FormQuestionType.ShortText);
            fixture.AddResponse(other, respondent.Id, [(otherQuestion, "Other Form answer", null)]);
        }
        return (fixture, form, fixture.StoredResponses.Where(response => response.FormId == form.Id).ToArray());
    }
}
