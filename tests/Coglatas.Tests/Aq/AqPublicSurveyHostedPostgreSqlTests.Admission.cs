using System.Net;
using System.Text.Json;
using Coglatas.Application.Forms.PublicSurveys;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Aq;

public sealed partial class AqPublicSurveyHostedPostgreSqlTests
{
    [PostgreSqlFact]
    public Task IncompatibleTargetSchemaAndValuesDoNotCreatePartialAnswers() =>
        AqPostgreSqlScenario.WithHostedEventAsync(async (database, host, project, activity) =>
        {
            var first = await SeedAsync(database, host, project, activity, additionalQuestion:
                ChoiceField("first-option"));
            var otherActivity = new ActivityEvent
            {
                TenantId = project.TenantId,
                ProjectId = project.Id,
                CreatedByUserId = host.Actor.Id,
                Title = "Another synthetic event",
                StartsAt = activity.StartsAt,
                EndsAt = activity.EndsAt,
                Status = EventStatus.Published
            };
            await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            db.Add(otherActivity);
            await db.SaveChangesAsync();
            var other = await SeedAsync(database, host, project, otherActivity, additionalQuestion:
                ChoiceField("other-option"));
            using var guest = AqPostgreSqlScenario.CreateGuestClient(host);
            using var published = await GetDataAsync(guest, SurveyRoute + first.TargetCode);
            Assert.Equal([FieldKey, "choice"], published.RootElement.GetProperty("data").GetProperty("fields")
                .EnumerateArray().Select(field => field.GetProperty("fieldKey").GetString()));
            Assert.NotEqual(first.Digest, other.Digest);

            using (var wrongSchema = await SubmitAsync(guest, first.TargetCode, "foreign-schema", Payload(other)))
                await AssertErrorAsync(wrongSchema, HttpStatusCode.Conflict, "InvalidStateTransition");

            var basePayload = Payload(first);
            var rejectedValues = new[]
            {
                new AqPublicAnswerInput("choice", JsonSerializer.SerializeToElement("other-option")),
                new AqPublicAnswerInput("unpublished", JsonSerializer.SerializeToElement("unknown field")),
                basePayload.Answers[0]
            };
            foreach (var rejected in rejectedValues)
            {
                var invalid = basePayload with { Answers = [.. basePayload.Answers, rejected] };
                using var response = await SubmitAsync(guest, first.TargetCode, "invalid-value", invalid);
                await AssertErrorAsync(response, HttpStatusCode.BadRequest, "ValidationFailed");
            }

            Assert.False(await db.Set<FormSubmission>().AnyAsync(row =>
                row.SurveyTargetId == first.TargetId || row.SurveyTargetId == other.TargetId));
            Assert.False(await db.Set<SubmissionIdempotency>().AnyAsync(row =>
                row.SurveyTargetId == first.TargetId || row.SurveyTargetId == other.TargetId));

            var valid = basePayload with { Answers =
                [.. basePayload.Answers, new("choice", JsonSerializer.SerializeToElement("first-option"))] };
            using var accepted = await SubmitAsync(guest, first.TargetCode, "invalid-value", valid);
            await ReadMinimalReceiptAsync(accepted);
            Assert.Equal(2, await db.Set<FormSubmissionValue>().CountAsync(row =>
                row.PublishedSurveySchemaId == first.SchemaId));
        });

    [PostgreSqlFact]
    public Task FailedReceiptCommitRollsBackAnswerAuditAndInvalidationBeforeRetry() =>
        AqPostgreSqlScenario.WithHostedEventAsync(async (database, host, project, activity) =>
        {
            var survey = await SeedAsync(database, host, project, activity, presenterFeedbackFixture: true);
            await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            var originalOutboxCount = await db.Set<OutboxEvent>().CountAsync();
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
                CREATE FUNCTION aq_synthetic_receipt_failure() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Synthetic receipt commit failure';
                END;
                $$;
                CREATE CONSTRAINT TRIGGER aq_synthetic_receipt_failure
                    AFTER INSERT ON submission_idempotency DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION aq_synthetic_receipt_failure();
                """);

            using var guest = AqPostgreSqlScenario.CreateGuestClient(host);
            const string key = "synthetic-commit-retry";
            using (var failure = await SubmitAsync(guest, survey.TargetCode, key, Payload(survey)))
                await AssertErrorAsync(failure, HttpStatusCode.InternalServerError, "UnexpectedServerError");

            Assert.False(await db.Set<FormSubmission>().AnyAsync(row => row.SurveyTargetId == survey.TargetId));
            Assert.False(await db.Set<FormSubmissionValue>().AnyAsync(row => row.PublishedSurveySchemaId == survey.SchemaId));
            Assert.False(await db.Set<SubmissionIdempotency>().AnyAsync(row => row.SurveyTargetId == survey.TargetId));
            Assert.False(await db.AuditLogs.AnyAsync(row => row.Action == "AqPublicSurveySubmitted"));
            Assert.Equal(originalOutboxCount, await db.Set<OutboxEvent>().CountAsync());

            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
                "DROP TRIGGER aq_synthetic_receipt_failure ON submission_idempotency");
            using var retry = await SubmitAsync(guest, survey.TargetCode, key, Payload(survey));
            await ReadMinimalReceiptAsync(retry);
            Assert.Equal(1, await db.Set<FormSubmission>().CountAsync(row => row.SurveyTargetId == survey.TargetId));
            Assert.Equal(1, await db.Set<SubmissionIdempotency>().CountAsync(row => row.SurveyTargetId == survey.TargetId));
        });

    private static SurveyField ChoiceField(string optionKey) =>
        new("choice", "Synthetic choice", SurveyFieldType.SingleChoice, false, [new(optionKey, "Synthetic option")]);
}
