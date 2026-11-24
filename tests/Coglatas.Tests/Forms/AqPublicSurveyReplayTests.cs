using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Forms.PublicSurveys;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Tests.Forms;

[Trait("Feature", "AQ")]
public sealed class AqPublicSurveyReplayTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReorderedFieldsOptionsAndEquivalentDecimalsRecoverTheOriginalReceipt()
    {
        var (service, store) = Create();
        var first = await SubmitAsync(service, store, "retry-key-1", "request-original",
            """[{"fieldKey":"number","value":1.0},{"fieldKey":"choices","value":["B","A"]},{"fieldKey":"text","value":" original "} ]""");
        var retry = await SubmitAsync(service, store, "retry-key-1", "request-retry",
            """[{"fieldKey":"text","value":" original "},{"fieldKey":"choices","value":["A","B"]},{"fieldKey":"number","value":1e0} ]""");
        var changed = await SubmitAsync(service, store, "retry-key-1", "request-changed",
            """[{"fieldKey":"number","value":1},{"fieldKey":"choices","value":["A","B"]},{"fieldKey":"text","value":"original"} ]""");

        Assert.Equal("request-original", first.Receipt?.RequestId);
        Assert.Equal("request-original", retry.Receipt?.RequestId);
        Assert.Equal("IdempotencyConflict", changed.ErrorCode);
        Assert.Equal(1, store.Commits);
    }

    [Fact]
    public async Task DuplicateFieldAndUnknownOptionFailBeforeCreatingAReceipt()
    {
        var (service, store) = Create();
        var duplicate = await SubmitAsync(service, store, "retry-key-2", "request-duplicate",
            """[{"fieldKey":"number","value":1},{"fieldKey":"number","value":2}]""");
        var unknown = await SubmitAsync(service, store, "retry-key-2", "request-unknown",
            """[{"fieldKey":"choices","value":["X"]}]""");

        Assert.Equal("ValidationFailed", duplicate.ErrorCode);
        Assert.Equal("ValidationFailed", unknown.ErrorCode);
        Assert.Equal(0, store.Commits);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    public async Task NullAnswerListAndEntriesDoNotCreateAReceipt(string answers)
    {
        var (service, store) = Create();

        var result = await SubmitAsync(service, store, "retry-key-3", "request-null-answers", answers);

        Assert.Equal("ValidationFailed", result.ErrorCode);
        Assert.Equal(0, store.Commits);
    }

    private static Task<AqPublicCommandResult> SubmitAsync(
        AqPublicSurveyService service, ReceiptStore store, string key, string requestId, string answers)
    {
        var parsed = JsonSerializer.Deserialize<AqPublicAnswerInput[]>(answers, JsonSerializerOptions.Web);
        return service.SubmitAsync("public-code", key,
            new AqPublicSubmissionInput(store.Survey.Schema.VersionRef, store.Survey.Schema.SchemaDigest, parsed!, null, false),
            requestId, CancellationToken.None);
    }

    private static (AqPublicSurveyService Service, ReceiptStore Store) Create()
    {
        var project = new Project { TenantId = Guid.NewGuid(), WorkspaceId = Guid.NewGuid() };
        var activity = new ActivityEvent { TenantId = project.TenantId, ProjectId = project.Id };
        var settings = EventFeedbackSettings.Create(activity, Start, Start.AddHours(1), "UTC");
        var presentation = Presentation.Create(activity, "Synthetic presentation", null, Start, Start.AddHours(1));
        var definition = FormDefinition.Create(project.TenantId, Guid.NewGuid(), null, null, project.Id, FormPurpose.Survey, "Survey");
        var campaign = SurveyCampaign.Create(activity, project, definition);
        campaign.SetPublication(campaign.VersionNo, true, true);
        var target = SurveyTarget.Create(campaign, presentation, "synthetic-marker");
        target.Configure(target.VersionNo, settings, null, null, true, false);
        var common = FormVersion.Publish(definition, 1,
            [
                new SurveyField("number", "Number", SurveyFieldType.Number, false, []),
                new SurveyField("choices", "Choices", SurveyFieldType.MultipleChoice, false, [new("A", "A"), new("B", "B")]),
                new SurveyField("text", "Text", SurveyFieldType.Text, false, [])
            ], Start);
        var extra = TargetQuestionSetVersion.Publish(target, 1, [], Start);
        var schema = PublishedSurveySchema.Compose(target, common, extra, "version-original", Start);
        target.Publish(target.VersionNo, schema);
        var store = new ReceiptStore(new AqPublicSurveyTarget(target, schema, settings, campaign, definition, common.Fields, false));
        return (new AqPublicSurveyService(store, new Guest(), new FixedClock()), store);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Start.AddMinutes(10);
    }

    private sealed class Guest : ICurrentUser
    {
        public Guid? UserId => null;
        public Guid? SessionId => null;
        public string? Email => null;
        public SystemRole? SystemRole => null;
        public bool IsAuthenticated => false;
    }

    private sealed class ReceiptStore(AqPublicSurveyTarget survey) : IAqPublicSurveyStore
    {
        private SubmissionIdempotency? _receipt;

        public AqPublicSurveyTarget Survey => survey;
        public int Commits { get; private set; }

        public Task<AqPublicProgram?> ReadProgramAsync(string codeDigest, string? cursor, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult<AqPublicProgram?>(null);

        public Task<AqPublicSurveyTarget?> ReadTargetAsync(string codeDigest, AqPublicSession? session, CancellationToken cancellationToken) =>
            Task.FromResult<AqPublicSurveyTarget?>(survey);

        public Task<AqPublicCommandResult> SubmitAsync(
            string codeDigest, string keyDigest, AqPublicSession? session,
            Func<AqPublicSurveyTarget, SubmissionIdempotency?, AqPublicWriteDecision> decide,
            CancellationToken cancellationToken)
        {
            var decision = decide(survey, _receipt);
            if (decision.Plan is { } plan)
            {
                var submission = FormSubmission.SubmitSurvey(survey.Definition, survey.Target, survey.Schema, plan.Answers, plan.SubmittedAt);
                _receipt = SubmissionIdempotency.Record(submission, keyDigest, plan.IntentDigest,
                    JsonSerializer.Serialize(new AqPublicReceipt(plan.RequestId)));
                Commits++;
            }
            return Task.FromResult(decision.Result);
        }
    }
}
