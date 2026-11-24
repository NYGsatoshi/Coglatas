using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coglatas.Domain.Common;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Tests.PostgreSql;

namespace Coglatas.Tests.SecurityArchitecture;

internal static partial class SecurityArchitectureRlsRowFixtures
{
    private static readonly HashSet<Type> SurveySourceTypes =
    [
        typeof(EventFeedbackSettings),
        typeof(AnswerVisibilityDecision),
        typeof(FeedbackInvitationPolicy),
        typeof(FeedbackReadState),
        typeof(FeedbackReportCase),
        typeof(FeedbackViewerGrant),
        typeof(FormDefinition),
        typeof(FormFieldDefinition),
        typeof(FormSubmission),
        typeof(FormSubmissionValue),
        typeof(FormVersion),
        typeof(Presentation),
        typeof(PresentationFeedbackSettings),
        typeof(PresentationParticipant),
        typeof(PublishedSurveySchema),
        typeof(PublishedSurveySchemaField),
        typeof(SharedTeacherFeedback),
        typeof(SubmissionIdempotency),
        typeof(SurveyCampaign),
        typeof(SurveyFeedbackExportGrant),
        typeof(SurveyFeedbackExportRequest),
        typeof(SurveyProgramEntry),
        typeof(SurveyPublicCode),
        typeof(SurveyRespondentIdentity),
        typeof(SurveyTarget),
        typeof(TargetQuestionSetVersion),
        typeof(TeacherPrivateNote)
    ];

    private sealed record SurveySourceRows(
        IReadOnlyList<AuditableEntity> Rows, SurveyTarget PublishedTarget, User FeedbackRecipient);

    private static SurveySourceRows CreateSurveySourceRows(Dictionary<string, object> rows, Guid tenant)
    {
        var project = (Project)rows["projects"];
        var user = (User)rows["users"];
        var activityEvent = (ActivityEvent)rows["activity_events"];
        var startsAt = new DateTimeOffset(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);
        var endsAt = startsAt.AddHours(2);
        activityEvent.ProjectId = project.Id;
        activityEvent.StartsAt = startsAt;
        activityEvent.EndsAt = endsAt;

        var definition = FormDefinition.Create(tenant, user.Id, null, null, project.Id, FormPurpose.Survey, "Synthetic survey");
        var presentation = Presentation.Create(activityEvent, "Synthetic presentation", null, startsAt, endsAt);
        var eventSettings = EventFeedbackSettings.Create(activityEvent, startsAt, endsAt, "UTC");
        eventSettings.SetParticipationPolicy(eventSettings.VersionNo, SurveyNameMode.OptionalName, SurveyAccountBinding.Off);
        eventSettings.SetProgram(eventSettings.VersionNo, published: true, SurveyProgramFields.Title, presenterNamesApproved: false);
        var campaign = SurveyCampaign.Create(activityEvent, project, definition);
        campaign.SetPublication(campaign.VersionNo, enabled: true, published: true);
        var target = SurveyTarget.Create(campaign, presentation, "synthetic-" + tenant.ToString("N"));
        target.Configure(target.VersionNo, eventSettings, startsAt, endsAt, enabled: true, resultsStopped: false);

        var field = new SurveyField("feedback", "Synthetic feedback", SurveyFieldType.Text, true, []);
        var version = FormVersion.Publish(definition, 1, [field], startsAt);
        var targetVersion = TargetQuestionSetVersion.Publish(target, 1, [], startsAt);
        var schema = PublishedSurveySchema.Compose(target, version, targetVersion, "synthetic-v1", startsAt);

        // The target must exist before its immutable publication. Build the submitted graph
        // against the published state, but insert the draft row first and publish it in SQL.
        var publishedTarget = SurveyTarget.Create(campaign, presentation, target.LocalMarkerKey);
        publishedTarget.Id = target.Id;
        publishedTarget.Configure(publishedTarget.VersionNo, eventSettings, startsAt, endsAt, enabled: true, resultsStopped: false);
        publishedTarget.Publish(publishedTarget.VersionNo, schema);

        var submission = FormSubmission.SubmitSurvey(definition, publishedTarget, schema,
            new Dictionary<string, JsonElement> { [field.FieldKey] = JsonSerializer.SerializeToElement("Synthetic survey answer") },
            startsAt.AddMinutes(1));
        var identity = SurveyRespondentIdentity.Create(submission, publishedTarget, eventSettings, "Synthetic contributor",
            accountOptIn: false, eligibleAccountUserId: null, submission.SubmittedAtUtc)
            ?? throw new InvalidOperationException("The named survey fixture requires an identity record.");
        var receipt = SubmissionIdempotency.Record(submission, SurveyDigest(tenant, "key"), SurveyDigest(tenant, "intent"),
            JsonSerializer.Serialize(new { submissionId = submission.Id }));
        var recipientEmail = $"synthetic-feedback-{tenant:N}@example.invalid";
        var recipient = new User
        {
            DisplayName = "Synthetic feedback recipient",
            Email = recipientEmail,
            NormalizedEmail = recipientEmail.ToUpperInvariant(),
            PasswordHash = user.PasswordHash,
            CreatedAt = startsAt
        };
        AuditableEntity[] entities =
        [
            eventSettings, definition, version, version.Fields.Single(), submission, submission.Values.Single(),
            presentation, PresentationFeedbackSettings.Create(presentation),
            PresentationParticipant.PendingPresenter(presentation, "synthetic-roster"),
            schema, schema.Fields.Single(), receipt, campaign, SurveyProgramEntry.Select(presentation, publishedTarget),
            SurveyPublicCode.ForTarget(publishedTarget, SurveyDigest(tenant, "code"), protectedReprintValue: null),
            identity, target, targetVersion,
            .. CreateFeedbackSourceRows(tenant, activityEvent, presentation, submission, user, recipient)
        ];
        foreach (var entity in entities)
            entity.CreatedAt = startsAt;
        publishedTarget.CreatedAt = target.CreatedAt;
        return new SurveySourceRows(entities, publishedTarget, recipient);
    }

    private static IReadOnlyList<AuditableEntity> CreateFeedbackSourceRows(Guid tenant, ActivityEvent activityEvent,
        Presentation presentation, FormSubmission submission, User author, User recipient)
    {
        var answer = submission.Values.Single();
        var now = submission.SubmittedAtUtc;
        var policy = new FeedbackInvitationPolicy
        {
            TenantId = tenant,
            EventId = activityEvent.Id,
            Enabled = true,
            DecisionWindowSeconds = 60,
            MaximumActiveSeconds = 3600
        };
        var invitation = FeedbackViewerGrant.Issue(presentation.Id, author.Id, recipient.Id, policy, 600, now);
        var visibility = new AnswerVisibilityDecision { TenantId = tenant, FormSubmissionValueId = answer.Id };
        visibility.Decide(hide: true, author.Id, "Synthetic visibility decision", visibility.VersionNo);
        var export = SurveyFeedbackExportRequest.Prepare(Guid.NewGuid(), tenant, presentation.Id, author.Id, "aggregate",
            "[\"responseCount\"]", SurveyDigest(tenant, "export"), "synthetic-export", now.AddMinutes(5));

        return
        [
            visibility, policy, invitation, export,
            SurveyFeedbackExportGrant.Issue(Guid.NewGuid(), export, "synthetic-grant", now.AddMinutes(1)),
            new FeedbackReadState
            {
                TenantId = tenant, UserId = author.Id, FormSubmissionId = submission.Id,
                ObservedRevision = 1, IsRead = true
            },
            new FeedbackReportCase
            {
                TenantId = tenant, PresentationId = presentation.Id, FormSubmissionValueId = answer.Id,
                ReporterUserId = author.Id, Reason = "Synthetic moderation report"
            },
            new TeacherPrivateNote
            {
                TenantId = tenant, PresentationId = presentation.Id, AuthorUserId = author.Id,
                Text = "Synthetic private note"
            },
            new SharedTeacherFeedback
            {
                TenantId = tenant, PresentationId = presentation.Id, AuthorUserId = author.Id,
                Text = "Synthetic shared feedback"
            }
        ];
    }

    private static Task PublishSurveyTargetAsync(string database, SurveySourceRows survey) =>
        PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
            UPDATE survey_targets SET "PublishedSchemaId"=@schema, "VersionNo"=@version
            WHERE "Id"=@target AND "TenantId"=@tenant
            """, ("schema", survey.PublishedTarget.PublishedSchemaId), ("version", survey.PublishedTarget.VersionNo),
            ("target", survey.PublishedTarget.Id), ("tenant", survey.PublishedTarget.TenantId));

    private static string SurveyDigest(Guid tenant, string purpose) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"synthetic-rls:{tenant:N}:{purpose}")));
}
