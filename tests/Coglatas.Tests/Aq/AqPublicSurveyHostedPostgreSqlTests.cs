using System.Globalization;
using System.Net;
using System.Text.Json;
using Coglatas.Application.Forms.PublicSurveys;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Tests.PostgreSql;
using Coglatas.Web.Configuration;
using Coglatas.Web.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;

namespace Coglatas.Tests.Aq;

[Trait("Feature", "AQ")]
[Trait("Category", "PostgreSQLIntegration")]
public sealed partial class AqPublicSurveyHostedPostgreSqlTests
{
    private const string SurveyRoute = "/api/v1/public/surveys/";
    private const string FieldKey = "comment";
    private const string AnswerText = "Synthetic audience answer";

    [PostgreSqlFact]
    public Task GuestProgramAndQrEntryResolveTheSameCompositionAndNoProtectedFields() =>
        AqPostgreSqlScenario.WithHostedEventAsync(async (database, host, project, activity) =>
        {
            var survey = await SeedAsync(database, host, project, activity);
            using var guest = AqPostgreSqlScenario.CreateGuestClient(host);
            using var program = await GetDataAsync(guest, $"/api/v1/public/events/{survey.ProgramCode}/presentations");
            var entry = Assert.Single(program.RootElement.GetProperty("data").GetProperty("presentations").EnumerateArray());
            Assert.Equal(survey.TargetCode, entry.GetProperty("surveyEntryRef").GetString());
            Assert.Equal("Synthetic presentation", entry.GetProperty("title").GetString());
            Assert.DoesNotContain(entry.EnumerateObject(), field => field.Name is "id" or "presentationId" or "resultCount");

            using var selectedForm = await GetDataAsync(guest, SurveyRoute + entry.GetProperty("surveyEntryRef").GetString());
            using var directForm = await GetDataAsync(guest, SurveyRoute + survey.TargetCode);
            var form = selectedForm.RootElement.GetProperty("data");
            var direct = directForm.RootElement.GetProperty("data");
            Assert.Equal(survey.VersionRef, direct.GetProperty("versionRef").GetString());
            Assert.Equal(survey.Digest, direct.GetProperty("schemaDigest").GetString());
            Assert.Equal(direct.GetProperty("versionRef").GetString(), form.GetProperty("versionRef").GetString());
            Assert.Equal(direct.GetProperty("schemaDigest").GetString(), form.GetProperty("schemaDigest").GetString());
            Assert.Equal("anonymous", form.GetProperty("nameMode").GetString());
            Assert.False(form.GetProperty("accountOptInAvailable").GetBoolean());
            Assert.Equal("open", form.GetProperty("receptionState").GetString());
            Assert.Equal(FieldKey, Assert.Single(form.GetProperty("fields").EnumerateArray()).GetProperty("fieldKey").GetString());
            Assert.DoesNotContain(form.EnumerateObject(), field => field.Name is "tenantId" or "targetId" or "answers" or "resultCount");
        });

    [PostgreSqlFact]
    public Task GuestPostReplaysOneReceiptAcrossCloseAndRotationAndKeepsOneDurableIntent() =>
        AqPostgreSqlScenario.WithHostedEventAsync(async (database, host, project, activity) =>
        {
            var survey = await SeedAsync(database, host, project, activity);
            using var firstGuest = AqPostgreSqlScenario.CreateGuestClient(host);
            using var secondGuest = AqPostgreSqlScenario.CreateGuestClient(host);
            var payload = Payload(survey);
            using (var forbidden = await SubmitAsync(firstGuest, survey.TargetCode, "no-token", payload, includeCsrf: false))
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

            var key = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            var (outcomes, sawConcurrentRequests) = await SubmitAcrossDatabaseBarrierAsync(
                database, survey, firstGuest, secondGuest, key, payload);
            string receiptId;
            using (outcomes[0])
            using (outcomes[1])
            {
                Assert.True(sawConcurrentRequests, "PostgreSQL did not observe both requests at the held target row.");
                var receipts = new List<string>();
                foreach (var outcome in outcomes)
                {
                    if (outcome.StatusCode == HttpStatusCode.Created)
                        receipts.Add(await ReadMinimalReceiptAsync(outcome));
                    else
                        await AssertErrorAsync(outcome, HttpStatusCode.Conflict, "ConcurrentModification");
                }

                Assert.NotEmpty(receipts);
                receiptId = receipts[0];
                Assert.All(receipts, receipt => Assert.Equal(receiptId, receipt));
            }

            using (var different = await SubmitAsync(firstGuest, survey.TargetCode, key, Payload(survey, value: "Different synthetic answer")))
                await AssertErrorAsync(different, HttpStatusCode.Conflict, "IdempotencyConflict");

            var replacementCode = await StopAndRotateAsync(database, host, survey);
            using (var replacementForm = await GetDataAsync(firstGuest, SurveyRoute + replacementCode))
            {
                var current = replacementForm.RootElement.GetProperty("data");
                Assert.NotEqual(survey.VersionRef, current.GetProperty("versionRef").GetString());
                Assert.NotEqual(survey.Digest, current.GetProperty("schemaDigest").GetString());
                Assert.Equal("closed", current.GetProperty("receptionState").GetString());
            }
            using (var revoked = await SubmitAsync(firstGuest, survey.TargetCode, key, payload))
                await AssertErrorAsync(revoked, HttpStatusCode.NotFound, "NotFound");
            using (var replay = await SubmitAsync(firstGuest, replacementCode, key, payload))
                Assert.Equal(receiptId, await ReadMinimalReceiptAsync(replay));
            using (var closed = await SubmitAsync(firstGuest, replacementCode, "new-closed-intent", payload))
                await AssertErrorAsync(closed, HttpStatusCode.Conflict, "InvalidStateTransition");

            await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            var saved = await db.Set<FormSubmission>().AsNoTracking().SingleAsync(row => row.SurveyTargetId == survey.TargetId);
            Assert.Null(saved.RespondentUserId);
            var value = await db.Set<FormSubmissionValue>().AsNoTracking().SingleAsync(row => row.FormSubmissionId == saved.Id);
            using (var answer = JsonDocument.Parse(value.ValueJson))
                Assert.Equal(AnswerText, answer.RootElement.GetString());
            Assert.Equal(survey.SchemaId, saved.PublishedSurveySchemaId);
            Assert.Equal(survey.SchemaId, value.PublishedSurveySchemaId);
            Assert.False(await db.Set<SurveyRespondentIdentity>().AnyAsync(row => row.FormSubmissionId == saved.Id));
            var record = await db.Set<SubmissionIdempotency>().AsNoTracking()
                .SingleAsync(row => row.SurveyTargetId == survey.TargetId);
            Assert.Equal(saved.Id, record.FormSubmissionId);
            Assert.Equal(saved.SubmittedAtUtc.AddHours(24), record.ExpiresAtUtc);
            Assert.Equal(1, await db.AuditLogs.CountAsync(row => row.EntityId == saved.Id && row.Action == "AqPublicSurveySubmitted"));
        });

    [PostgreSqlFact]
    public Task HeldSubmissionIntentReturnsConflictWithoutWritingAndRetryReplaysOneReceipt() =>
        AqPostgreSqlScenario.WithHostedEventAsync(async (database, host, project, activity) =>
        {
            var survey = await SeedAsync(database, host, project, activity);
            using var guest = AqPostgreSqlScenario.CreateGuestClient(host);
            var key = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            var payload = Payload(survey);
            // Coordinate the real PostgreSQL lock independently of request
            // scheduling. The original two-request race test remains unchanged.
            var digest = AqPublicSurveyService.Hash(key);
            var hash = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"aq-public:{survey.TargetId:D}:{digest}"));
            var lockKey = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(hash);
            await using (var blocker = new NpgsqlConnection(database))
            {
                await blocker.OpenAsync();
                await using var transaction = await blocker.BeginTransactionAsync();
                await using var hold = new NpgsqlCommand(
                    "SELECT pg_try_advisory_xact_lock(@key)", blocker, transaction);
                hold.Parameters.AddWithValue("key", lockKey);
                Assert.True(Assert.IsType<bool>(await hold.ExecuteScalarAsync()));

                using (var conflict = await SubmitAsync(guest, survey.TargetCode, key, payload))
                    await AssertErrorAsync(conflict, HttpStatusCode.Conflict, "ConcurrentModification");

                await using var before = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
                Assert.False(await before.Set<FormSubmission>().AnyAsync(row => row.SurveyTargetId == survey.TargetId));
                Assert.False(await before.Set<SubmissionIdempotency>().AnyAsync(row => row.SurveyTargetId == survey.TargetId));
                await transaction.CommitAsync();
            }

            string receiptId;
            using (var accepted = await SubmitAsync(guest, survey.TargetCode, key, payload))
                receiptId = await ReadMinimalReceiptAsync(accepted);
            using (var replay = await SubmitAsync(guest, survey.TargetCode, key, payload))
                Assert.Equal(receiptId, await ReadMinimalReceiptAsync(replay));

            await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            var saved = await db.Set<FormSubmission>().AsNoTracking().SingleAsync(row => row.SurveyTargetId == survey.TargetId);
            Assert.Null(saved.RespondentUserId);
            var value = await db.Set<FormSubmissionValue>().AsNoTracking().SingleAsync(row => row.FormSubmissionId == saved.Id);
            using (var answer = JsonDocument.Parse(value.ValueJson))
                Assert.Equal(AnswerText, answer.RootElement.GetString());
            var record = await db.Set<SubmissionIdempotency>().AsNoTracking()
                .SingleAsync(row => row.SurveyTargetId == survey.TargetId);
            Assert.Equal(saved.Id, record.FormSubmissionId);
            Assert.Equal(digest, record.KeyDigest);
            Assert.Equal(saved.SubmittedAtUtc.AddHours(24), record.ExpiresAtUtc);
            Assert.Equal(1, await db.AuditLogs.CountAsync(row => row.EntityId == saved.Id && row.Action == "AqPublicSurveySubmitted"));
        });

    [PostgreSqlFact]
    public Task SignedInAnonymousAndOptionalNameRemainUnlinkedUntilExplicitEligibleConsent() =>
        AqPostgreSqlScenario.WithHostedEventAsync(async (database, host, project, activity) =>
        {
            var survey = await SeedAsync(database, host, project, activity);
            using var guest = AqPostgreSqlScenario.CreateGuestClient(host);
            await host.LoginAsync();

            using (var anonymous = await SubmitAsync(host.Client, survey.TargetCode, "signed-anonymous", Payload(survey)))
                await ReadMinimalReceiptAsync(anonymous);

            await SetNameModeAsync(database, survey, SurveyNameMode.OptionalName);
            using (var named = await SubmitAsync(guest, survey.TargetCode, "optional-guest", Payload(survey, name: "Synthetic guest")))
                await ReadMinimalReceiptAsync(named);
            using (var unlinked = await SubmitAsync(host.Client, survey.TargetCode, "signed-no-opt-in", Payload(survey)))
                await ReadMinimalReceiptAsync(unlinked);
            using (var invalidConsent = await SubmitAsync(guest, survey.TargetCode, "guest-opt-in", Payload(survey, optIn: true)))
                await AssertErrorAsync(invalidConsent, HttpStatusCode.Unauthorized, "AuthenticationRequired");
            using (var linked = await SubmitAsync(host.Client, survey.TargetCode, "signed-opt-in", Payload(survey, optIn: true)))
                await ReadMinimalReceiptAsync(linked);

            await SetNameModeAsync(database, survey, SurveyNameMode.RequiredName);
            using (var omitted = await SubmitAsync(guest, survey.TargetCode, "missing-required-name", Payload(survey)))
                await AssertErrorAsync(omitted, HttpStatusCode.BadRequest, "ValidationFailed");
            using (var named = await SubmitAsync(guest, survey.TargetCode, "required-guest", Payload(survey, name: "Synthetic required guest")))
                await ReadMinimalReceiptAsync(named);

            await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            var submissions = await db.Set<FormSubmission>().AsNoTracking()
                .Where(row => row.SurveyTargetId == survey.TargetId).ToListAsync();
            Assert.Equal(5, submissions.Count);
            Assert.All(submissions, row => Assert.Null(row.RespondentUserId));
            var ids = submissions.Select(row => row.Id).ToArray();
            var identities = await db.Set<SurveyRespondentIdentity>().AsNoTracking()
                .Where(row => ids.Contains(row.FormSubmissionId)).ToListAsync();
            Assert.Equal(3, identities.Count);
            var linkedIdentity = Assert.Single(identities, row => row.AccountUserId is not null);
            Assert.Equal(host.Actor.Id, linkedIdentity.AccountUserId);
            Assert.NotNull(linkedIdentity.AccountConsentAtUtc);
            Assert.Null(linkedIdentity.EnteredName);
            Assert.Equal(["Synthetic guest", "Synthetic required guest"], identities
                .Where(row => row.AccountUserId is null).Select(row => row.EnteredName).Order(StringComparer.Ordinal));
        });

    [PostgreSqlFact]
    public Task PresenterCanReadFirstAnswerAndScopedSummaryUntilResultsStop() =>
        AqPostgreSqlScenario.WithHostedEventAsync(async (database, host, project, activity) =>
        {
            var survey = await SeedAsync(database, host, project, activity, presenterFeedbackFixture: true);
            using var guest = AqPostgreSqlScenario.CreateGuestClient(host);
            await host.LoginAsync();
            var feedbackRoute = $"/api/v1/core/presentations/{survey.PresentationId:D}/feedback";
            using (var unrelated = await host.Client.GetAsync("/api/v1/core/not-an-aq-endpoint"))
                Assert.Equal("strict-origin-when-cross-origin", unrelated.Headers.GetValues("Referrer-Policy").Single());

            using (var first = await SubmitAsync(guest, survey.TargetCode, "aggregate-first", Payload(survey, score: 2)))
                await ReadMinimalReceiptAsync(first);
            using (var publicFeedback = await guest.GetAsync(feedbackRoute))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, publicFeedback.StatusCode);
                Assert.Equal("no-referrer", publicFeedback.Headers.GetValues("Referrer-Policy").Single());
            }

            using (var visible = await GetDataAsync(host.Client, feedbackRoute))
            {
                var row = Assert.Single(visible.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
                Assert.DoesNotContain(row.EnumerateObject(), field => field.Name is "accountUserId" or "respondentUserId" or "respondentName");
                var comment = Assert.Single(row.GetProperty("answers").EnumerateArray(),
                    answer => answer.GetProperty("fieldKey").GetString() == FieldKey);
                Assert.Equal(AnswerText, comment.GetProperty("value").GetString());
            }

            using (var second = await SubmitAsync(guest, survey.TargetCode, "aggregate-second", Payload(survey, score: 4)))
                await ReadMinimalReceiptAsync(second);
            using (var third = await SubmitAsync(guest, survey.TargetCode, "aggregate-third", Payload(survey)))
                await ReadMinimalReceiptAsync(third);

            var summaryRoute = feedbackRoute + "-summary";
            using (var aggregate = await GetDataAsync(host.Client, summaryRoute))
            {
                var summary = aggregate.RootElement.GetProperty("data");
                Assert.Equal(3, summary.GetProperty("responses").GetInt64());
                var score = Assert.Single(summary.GetProperty("questions").EnumerateArray(),
                    question => question.GetProperty("fieldKey").GetString() == "score");
                Assert.Equal(2, score.GetProperty("denominator").GetInt64());
                Assert.Equal(3m, score.GetProperty("mean").GetDecimal());
            }

            await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            var settings = await db.Set<EventFeedbackSettings>().SingleAsync(row => row.Id == survey.SettingsId);
            settings.SetResultsStop(settings.VersionNo, true);
            await db.SaveChangesAsync();
            using (var stoppedDetail = await host.Client.GetAsync(feedbackRoute))
                await AssertErrorAsync(stoppedDetail, HttpStatusCode.NotFound, "NotFound");
            using (var stoppedSummary = await host.Client.GetAsync(summaryRoute))
                await AssertErrorAsync(stoppedSummary, HttpStatusCode.NotFound, "NotFound");

            using var openSurvey = await GetDataAsync(guest, SurveyRoute + survey.TargetCode);
            Assert.Equal("open", openSurvey.RootElement.GetProperty("data").GetProperty("receptionState").GetString());
        });

    [PostgreSqlFact]
    public Task ReportingAndHidingDoNotVoidNumericFeedbackAndStaleVisibilityCannotOverwrite() =>
        AqPostgreSqlScenario.WithHostedEventAsync(async (database, host, project, activity) =>
        {
            var survey = await SeedAsync(database, host, project, activity, presenterFeedbackFixture: true);
            await AssignModerationAsync(database, host, project, survey);
            using var guest = AqPostgreSqlScenario.CreateGuestClient(host);
            await host.LoginAsync();
            using (var first = await SubmitAsync(guest, survey.TargetCode, "moderated-first", Payload(survey, score: 4)))
                await ReadMinimalReceiptAsync(first);

            var feedbackRoute = $"/api/v1/core/presentations/{survey.PresentationId:D}/feedback";
            using var initial = await GetDataAsync(host.Client, feedbackRoute);
            var row = Assert.Single(initial.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
            var comment = Assert.Single(row.GetProperty("answers").EnumerateArray(),
                answer => answer.GetProperty("fieldKey").GetString() == FieldKey);
            Assert.Equal(AnswerText, comment.GetProperty("value").GetString());
            var submissionId = row.GetProperty("id").GetGuid();
            var answerId = comment.GetProperty("id").GetGuid();
            var answerRoute = $"/api/v1/core/feedback/answers/{answerId:D}";

            using var reported = await host.PostAsync(answerRoute + "/reports", new { reason = "Synthetic report" });
            Assert.Equal(HttpStatusCode.Created, reported.StatusCode);
            using var reportBody = JsonDocument.Parse(await reported.Content.ReadAsStringAsync());
            var reportId = reportBody.RootElement.GetProperty("data").GetProperty("reportId").GetGuid();
            using (var stillVisible = await GetDataAsync(host.Client, feedbackRoute))
                Assert.Contains(AnswerText, stillVisible.RootElement.GetRawText(), StringComparison.Ordinal);

            var inboxRoute = $"/api/v1/core/feedback/inbox?presentationId={survey.PresentationId:D}";
            await AssertInboxCountsAsync(host.Client, inboxRoute, unread: 1, unresolved: 1);
            using (var read = await PatchAsync(host.Client, $"/api/v1/core/feedback/submissions/{submissionId:D}/read-state", new
                   { observedRevision = 1L, isRead = true }))
                Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            await AssertInboxCountsAsync(host.Client, inboxRoute, unread: 0, unresolved: 1);

            using (var hide = await PatchAsync(host.Client, answerRoute + "/visibility", new
                   { expectedVersionNo = 1L, action = "hide", reason = "Synthetic hide" }))
                Assert.Equal(HttpStatusCode.OK, hide.StatusCode);
            using (var stale = await PatchAsync(host.Client, answerRoute + "/visibility", new
                   { expectedVersionNo = 1L, action = "restore", reason = "Synthetic stale decision" }))
                await AssertErrorAsync(stale, HttpStatusCode.Conflict, "RowVersionConflict");
            using (var dismiss = await host.PostAsync($"/api/v1/core/feedback/reports/{reportId:D}/resolve", new
                   { expectedVersionNo = 1L, resolution = "dismiss", reason = "Synthetic dismissal" }))
                Assert.Equal(HttpStatusCode.OK, dismiss.StatusCode);
            await AssertInboxCountsAsync(host.Client, inboxRoute, unread: 0, unresolved: 0);

            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                var permission = await db.Set<CapabilityGrant>().SingleAsync(grant =>
                    grant.SubjectUserId == host.Actor.Id && grant.CapabilityKey == "feedback.moderate");
                permission.RevokedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }
            using (var hidden = await GetDataAsync(host.Client, feedbackRoute))
                Assert.DoesNotContain(AnswerText, hidden.RootElement.GetRawText(), StringComparison.Ordinal);

            using (var aggregate = await GetDataAsync(host.Client, feedbackRoute + "-summary"))
            {
                var data = aggregate.RootElement.GetProperty("data");
                Assert.Equal(1, data.GetProperty("responses").GetInt64());
                var score = Assert.Single(data.GetProperty("questions").EnumerateArray(),
                    question => question.GetProperty("fieldKey").GetString() == "score");
                Assert.Equal(1, score.GetProperty("denominator").GetInt64());
                Assert.Equal(4m, score.GetProperty("mean").GetDecimal());
            }

            const string voidReason = "Synthetic void";
            using (var voided = await host.PostAsync($"/api/v1/core/feedback/submissions/{submissionId:D}/void", new
                   { expectedVersionNo = 1L, reason = $" {voidReason} " }))
            {
                Assert.Equal(HttpStatusCode.OK, voided.StatusCode);
                Assert.DoesNotContain(voidReason, await voided.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                var persisted = await db.Set<FormSubmission>().AsNoTracking().SingleAsync(submission => submission.Id == submissionId);
                Assert.Equal(FormSubmissionStatus.Voided, persisted.Status);
                Assert.Equal(2, persisted.VersionNo);
                var audit = await db.AuditLogs.AsNoTracking().SingleAsync(entry =>
                    entry.Action == "Feedback.SubmissionVoided" && entry.EntityId == submissionId);
                Assert.Equal(host.Actor.Id, audit.ActorUserId);
                Assert.Equal(host.Tenant.Id, audit.TenantId);
                using var metadata = JsonDocument.Parse(Assert.IsType<string>(audit.MetadataJson));
                Assert.Equal(voidReason, metadata.RootElement.GetProperty("reason").GetString());
                Assert.Equal("feedback.submission.void", metadata.RootElement.GetProperty("policyBasis").GetString());
            }
            using (var afterVoid = await GetDataAsync(host.Client, feedbackRoute + "-summary"))
                Assert.Equal(0, afterVoid.RootElement.GetProperty("data").GetProperty("responses").GetInt64());
            using (var detail = await GetDataAsync(host.Client, feedbackRoute))
                Assert.Empty(detail.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
        });

    private static async Task<SeededSurvey> SeedAsync(
        string database, AdmissionHost host, Project project, ActivityEvent activity, bool presenterFeedbackFixture = false,
        SurveyField? additionalQuestion = null, Guid? existingSettingsId = null)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var settings = existingSettingsId is { } settingsId
            ? await db.Set<EventFeedbackSettings>().SingleAsync(row => row.Id == settingsId && row.EventId == activity.Id)
            : EventFeedbackSettings.Create(activity, activity.StartsAt, activity.EndsAt, "UTC");
        if (existingSettingsId is null)
        {
            settings.SetProgram(settings.VersionNo, true, SurveyProgramFields.Title | SurveyProgramFields.TimeZone,
                presenterNamesApproved: false);
            db.Add(settings);
        }
        var definition = FormDefinition.Create(activity.TenantId, host.Actor.Id, null, null,
            project.Id, FormPurpose.Survey, "Synthetic audience feedback");
        var presentation = Presentation.Create(activity, "Synthetic presentation", "Unselected room",
            activity.StartsAt, activity.EndsAt);
        var campaign = SurveyCampaign.Create(activity, project, definition);
        campaign.SetPublication(campaign.VersionNo, true, true);
        var target = SurveyTarget.Create(campaign, presentation, Guid.NewGuid().ToString("N"));
        target.Configure(target.VersionNo, settings, null, null, true, false);
        var publishedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var questions = new List<SurveyField> { new(FieldKey, "Comment", SurveyFieldType.Text, true, []) };
        if (presenterFeedbackFixture) questions.Add(new SurveyField("score", "Score", SurveyFieldType.Number, false, []));
        var common = FormVersion.Publish(definition, 1, questions, publishedAt);
        var extra = TargetQuestionSetVersion.Publish(target, 1, additionalQuestion is null ? [] : [additionalQuestion], publishedAt);
        var schema = PublishedSurveySchema.Compose(target, common, extra, Guid.NewGuid().ToString("N"), publishedAt);
        var programCode = Guid.NewGuid().ToString("N");
        var targetCode = Guid.NewGuid().ToString("N");
        var codec = host.Services.GetRequiredService<IAqPublicProjectionCodec>();
        var publicTarget = SurveyPublicCode.ForTarget(target, AqPublicSurveyService.Hash(targetCode), codec.ProtectEntry(targetCode));

        db.AddRange(definition, presentation, campaign, target, common, extra);
        if (presenterFeedbackFixture)
        {
            var membership = await db.WorkspaceMembers.SingleAsync(row =>
                row.TenantId == project.TenantId && row.WorkspaceId == project.WorkspaceId && row.UserId == host.Actor.Id);
            db.Add(PresentationParticipant.Assign(presentation, project, membership, PresentationParticipantRole.Presenter));
        }
        await db.SaveChangesAsync();
        db.Add(schema);
        await db.SaveChangesAsync();
        target.Publish(target.VersionNo, schema);
        if (existingSettingsId is null)
            db.Add(SurveyPublicCode.ForProgram(settings, AqPublicSurveyService.Hash(programCode), null));
        db.AddRange(SurveyProgramEntry.Select(presentation, target), publicTarget);
        await db.SaveChangesAsync();

        return new SeededSurvey(programCode, targetCode, settings.Id, presentation.Id, target.Id, schema.Id,
            schema.VersionRef, schema.SchemaDigest);
    }

    private static async Task<string> StopAndRotateAsync(string database, AdmissionHost host, SeededSurvey survey)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var settings = await db.Set<EventFeedbackSettings>().SingleAsync(row => row.Id == survey.SettingsId);
        settings.SetReception(settings.VersionNo, settings.StartsAtUtc, settings.EndsAtUtc, "UTC", stopped: true);
        var old = await db.Set<SurveyPublicCode>().SingleAsync(row => row.SurveyTargetId == survey.TargetId);
        old.Revoke(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        var replacement = Guid.NewGuid().ToString("N");
        var target = await db.Set<SurveyTarget>().SingleAsync(row => row.Id == survey.TargetId);
        var original = await db.Set<PublishedSurveySchema>().SingleAsync(row => row.Id == survey.SchemaId);
        var common = await db.Set<FormVersion>().Include(row => row.Fields)
            .SingleAsync(row => row.Id == original.FormVersionId);
        var now = DateTimeOffset.UtcNow;
        var extra = TargetQuestionSetVersion.Publish(target, 2,
            [new SurveyField("later-field", "Synthetic later field", SurveyFieldType.Boolean, false, [])], now);
        db.Add(extra);
        await db.SaveChangesAsync();
        var changed = PublishedSurveySchema.Compose(target, common, extra, Guid.NewGuid().ToString("N"), now);
        db.Add(changed);
        await db.SaveChangesAsync();
        target.Publish(target.VersionNo, changed);
        var codec = host.Services.GetRequiredService<IAqPublicProjectionCodec>();
        db.Add(SurveyPublicCode.ForTarget(target, AqPublicSurveyService.Hash(replacement), codec.ProtectEntry(replacement)));
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return replacement;
    }

    private static async Task SetNameModeAsync(string database, SeededSurvey survey, SurveyNameMode mode)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var settings = await db.Set<EventFeedbackSettings>().SingleAsync(row => row.Id == survey.SettingsId);
        settings.SetParticipationPolicy(settings.VersionNo, mode, SurveyAccountBinding.ExplicitOptIn);
        await db.SaveChangesAsync();
    }

    private static async Task AssignModerationAsync(string database, AdmissionHost host, Project project, SeededSurvey survey)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var membership = await db.WorkspaceMembers.SingleAsync(row =>
            row.TenantId == project.TenantId && row.WorkspaceId == project.WorkspaceId && row.UserId == host.Actor.Id);
        var presentation = await db.Set<Presentation>().SingleAsync(row => row.Id == survey.PresentationId);
        db.Add(PresentationParticipant.Assign(presentation, project, membership, PresentationParticipantRole.Reviewer));
        var now = DateTimeOffset.UtcNow;
        foreach (var capability in new[] { "feedback.report", "feedback.moderate", "feedback.submission.void" })
        {
            db.Add(new CapabilityGrant
            {
                TenantId = host.Tenant.Id,
                SubjectUserId = host.Actor.Id,
                GrantedByUserId = host.Actor.Id,
                ScopeType = CapabilityScopeType.Workspace,
                ScopeId = project.WorkspaceId,
                CapabilityKey = capability,
                GrantedAt = now.AddMinutes(-1),
                ExpiresAt = now.AddHours(1)
            });
        }
        await db.SaveChangesAsync();
    }

    private static async Task AssertInboxCountsAsync(HttpClient client, string route, long unread, long unresolved)
    {
        using var response = await GetDataAsync(client, route);
        var data = response.RootElement.GetProperty("data");
        Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal(unread, data.GetProperty("unreadCount").GetInt64());
        Assert.Equal(unresolved, data.GetProperty("unresolvedReportCount").GetInt64());
    }

    private static async Task<(HttpResponseMessage[] Outcomes, bool SawConcurrentRequests)> SubmitAcrossDatabaseBarrierAsync(
        string database, SeededSurvey survey, HttpClient firstGuest, HttpClient secondGuest,
        string key, AqPublicSubmissionInput payload)
    {
        await using var blocker = new NpgsqlConnection(database);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using var hold = new NpgsqlCommand(
            """SELECT "Id" FROM survey_targets WHERE "Id" = @target FOR UPDATE""", blocker, transaction);
        hold.Parameters.AddWithValue("target", survey.TargetId);
        Assert.Equal(survey.TargetId, Assert.IsType<Guid>(await hold.ExecuteScalarAsync()));

        var requests = Task.WhenAll(
            SubmitAsync(firstGuest, survey.TargetCode, key, payload),
            SubmitAsync(secondGuest, survey.TargetCode, key, payload));
        var observed = await ObservePostgreSqlWaitersAsync(database, blocker.ProcessID, requests);
        await transaction.CommitAsync();
        return (await requests, observed);
    }

    private static async Task<bool> ObservePostgreSqlWaitersAsync(string database, int blockerId, Task requests)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (!requests.IsCompleted && TimeProvider.System.GetElapsedTime(started) < TimeSpan.FromSeconds(10))
        {
            var waiting = await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, """
                WITH RECURSIVE blocked(pid) AS (
                    SELECT activity.pid FROM pg_stat_activity AS activity
                    WHERE activity.datname = current_database() AND activity.wait_event_type = 'Lock'
                        AND @blocker = ANY(pg_blocking_pids(activity.pid))
                    UNION
                    SELECT activity.pid FROM pg_stat_activity AS activity
                    JOIN blocked ON blocked.pid = ANY(pg_blocking_pids(activity.pid))
                    WHERE activity.datname = current_database() AND activity.wait_event_type = 'Lock'
                )
                SELECT count(*) FROM blocked
                """, ("blocker", blockerId));
            if (waiting >= 2)
            {
                return true;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
        return false;
    }

    private static AqPublicSubmissionInput Payload(
        SeededSurvey survey, string value = AnswerText, string? name = null, bool optIn = false, int? score = null)
    {
        var answers = new List<AqPublicAnswerInput> { new(FieldKey, JsonSerializer.SerializeToElement(value)) };
        if (score.HasValue) answers.Add(new AqPublicAnswerInput("score", JsonSerializer.SerializeToElement(score.Value)));
        return new AqPublicSubmissionInput(survey.VersionRef, survey.Digest, answers, name, optIn);
    }

    private static async Task<JsonDocument> GetDataAsync(HttpClient client, string route)
    {
        using var result = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        return JsonDocument.Parse(await result.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> SubmitAsync(
        HttpClient client, string code, string key, AqPublicSubmissionInput payload, bool includeCsrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, SurveyRoute + code + "/submissions");
        request.Content = JsonContent.Create(payload);
        request.Headers.Add("Idempotency-Key", key);
        return await SendAsync(client, request, includeCsrf);
    }

    private static async Task<HttpResponseMessage> PatchAsync<T>(HttpClient client, string route, T payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, route);
        request.Content = JsonContent.Create(payload);
        return await SendAsync(client, request, includeCsrf: true);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, bool includeCsrf)
    {
        if (!includeCsrf) return await client.SendAsync(request);

        using var bootstrap = await client.GetAsync("/api/security/csrf-token");
        bootstrap.EnsureSuccessStatusCode();
        var token = await bootstrap.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        Assert.NotNull(token);
        request.Headers.Add(SecurityOptions.CsrfHeaderName, token.Token);
        return await client.SendAsync(request);
    }

    private static async Task<string> ReadMinimalReceiptAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.All(root.EnumerateObject(), property => Assert.Contains(property.Name, new[] { "requestId", "data", "warnings", "meta" }));
        Assert.Equal("accepted", Assert.Single(root.GetProperty("data").EnumerateObject()).Name);
        Assert.True(root.GetProperty("data").GetProperty("accepted").GetBoolean());
        Assert.Empty(root.GetProperty("warnings").EnumerateArray());
        var requestId = root.GetProperty("requestId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(requestId));
        return requestId;
    }

    private static void AssertStatus(HttpResponseMessage response, HttpStatusCode expected)
    {
        var diagnostic = $"Expected HTTP {(int)expected} ({expected}), got {(int)response.StatusCode} ({response.StatusCode}).";
        Assert.True(response.StatusCode == expected, diagnostic);
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        AssertStatus(response, status);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private sealed record SeededSurvey(string ProgramCode, string TargetCode, Guid SettingsId,
        Guid PresentationId, Guid TargetId, Guid SchemaId, string VersionRef, string Digest);
}
