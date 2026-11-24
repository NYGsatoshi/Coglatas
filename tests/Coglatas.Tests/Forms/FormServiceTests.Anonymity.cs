using Coglatas.Application.Forms;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Forms;

public sealed partial class FormServiceTests
{
    [Fact]
    public async Task AnonymousFormWithResponsesCannotRelaxAnonymity()
    {
        var fixture = FormFixture.Create();
        var manager = fixture.AddUser();
        fixture.AddWorkspaceMember(manager.Id, WorkspaceRole.Owner);
        fixture.Current.UserIdValue = manager.Id;
        var form = fixture.AddForm(FormStatus.Open, isAnonymous: true);
        var question = fixture.AddQuestion(form, FormQuestionType.ShortText);

        var submitted = await fixture.Service.SubmitResponseAsync(
            form.Id, new SubmitFormResponseRequest([new SubmitFormAnswerRequest(question.Id, AnswerText: "Synthetic answer")]));
        Assert.True(submitted.IsSuccess);
        Assert.Null(submitted.Value!.RespondentUserId);
        Assert.True(Assert.Single(fixture.StoredResponses).MaskRespondent);
        var saveCalls = fixture.SaveCalls;
        var auditCalls = fixture.AuditCalls;

        var changed = await fixture.Service.UpdateAsync(form.Id, new UpdateFormRequest(IsAnonymous: false));

        Assert.False(changed.IsSuccess);
        Assert.True(form.IsAnonymous);
        Assert.Equal(saveCalls, fixture.SaveCalls);
        Assert.Equal(auditCalls, fixture.AuditCalls);
    }

    [Fact]
    public async Task HistoricallyMaskedResponseKeepsOwnershipAndHidesIdentityOnIdentifiedForm()
    {
        var fixture = FormFixture.Create();
        var manager = fixture.AddUser();
        fixture.AddWorkspaceMember(manager.Id, WorkspaceRole.Owner);
        fixture.Current.UserIdValue = manager.Id;
        var form = fixture.AddForm(FormStatus.Open);
        var question = fixture.AddQuestion(form, FormQuestionType.ShortText);
        fixture.AddResponse(form, manager.Id, [(question, "Historical synthetic answer", null)]);
        var stored = Assert.Single(fixture.StoredResponses);
        stored.MaskRespondent = true;

        var listed = await fixture.Service.ListResponsesAsync(form.Id);
        var ownResponse = await fixture.Service.GetMyResponseAsync(form.Id);
        var duplicate = await fixture.Service.SubmitResponseAsync(
            form.Id, new SubmitFormResponseRequest([new SubmitFormAnswerRequest(question.Id, AnswerText: "Duplicate")]));

        Assert.True(listed.IsSuccess);
        Assert.True(ownResponse.IsSuccess);
        var projected = Assert.Single(listed.Value!.Items);
        Assert.Equal(stored.Id, projected.Id);
        Assert.Equal(stored.Id, ownResponse.Value!.Id);
        Assert.Equal(manager.Id, stored.RespondentUserId);
        Assert.Null(projected.RespondentUserId);
        Assert.Null(projected.RespondentDisplayName);
        Assert.Null(projected.RespondentEmail);
        Assert.Null(ownResponse.Value.RespondentUserId);
        Assert.False(duplicate.IsSuccess);
    }
}
