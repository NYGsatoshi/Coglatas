using System.Net;
using Coglatas.Application.Events;
using Coglatas.Application.Forms;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;

namespace Coglatas.Tests.Aq;

[Trait("Feature", "AQ")]
[Trait("Category", "PostgreSQLIntegration")]
public sealed class AqMigrationCompatibilityPostgreSqlTests
{
    private const string Predecessor = "20261101062657_StabilizeLogicalRecoveryConstraintExpressions";
    private const string HistoricalChoice = "Removed synthetic option";
    private const string CurrentChoice = "Current synthetic option";

    [PostgreSqlFact]
    public Task ExistingOrdinaryFormResponseRemainsOwnedMaskedAndReadableAfterUpgrade() =>
        PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await PostgreSqlMigrationTestDatabase.MigrateAsync(database, Predecessor);
                await using var host = await AdmissionHost.CreateAsync(database);
                var fixture = await SeedPredecessorAsync(database, host);

                await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
                await AssertCutoverReconciliationAsync(database);
                await using (var verification = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    var applied = (await verification.Database.GetAppliedMigrationsAsync()).ToArray();
                    Assert.Contains(Predecessor, applied);
                    Assert.NotEqual(Predecessor, applied[^1]);
                    Assert.Empty(await verification.Database.GetPendingMigrationsAsync());
                    var form = await verification.Set<FormDefinition>().AsNoTracking().SingleAsync(row => row.Id == fixture.FormId);
                    Assert.Equal(FormPurpose.Ordinary, form.Purpose);
                    var response = await verification.Set<FormSubmission>().AsNoTracking().SingleAsync(row => row.Id == fixture.ResponseId);
                    Assert.Equal(fixture.FormId, response.FormDefinitionId);
                    Assert.Equal(host.Actor.Id, response.RespondentUserId);
                    Assert.Null(response.SurveyTargetId);
                    Assert.Null(response.PublishedSurveySchemaId);
                    var observed = await verification.Set<FormVersion>().AsNoTracking()
                        .Where(row => row.Id == response.FormVersionId)
                        .Select(row => EF.Property<bool>(row, "IsLegacyObservedSnapshot")).SingleAsync();
                    Assert.True(observed);
                    var value = await verification.Set<FormSubmissionValue>().AsNoTracking()
                        .Where(row => row.Id == fixture.AnswerId)
                        .Select(row => new
                        {
                            row.FormSubmissionId,
                            QuestionId = EF.Property<Guid?>(row, "LegacyQuestionId"),
                            HistoricalText = EF.Property<string?>(row, "LegacyAnswerText"),
                            Uninterpreted = EF.Property<bool>(row, "HasUninterpretedLegacyValue")
                        }).SingleAsync();
                    Assert.Equal(fixture.ResponseId, value.FormSubmissionId);
                    Assert.Equal(fixture.QuestionId, value.QuestionId);
                    Assert.Equal(HistoricalChoice, value.HistoricalText);
                    Assert.True(value.Uninterpreted);
                    Assert.False(await verification.Set<SurveyRespondentIdentity>().AnyAsync(identity => identity.FormSubmissionId == fixture.ResponseId));
                    Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database,
                        """SELECT "MaskRespondent" FROM form_responses WHERE "Id" = @id""", ("id", fixture.ResponseId)));
                }

                await host.LoginAsync();
                var formRoute = $"/api/forms/{fixture.FormId:D}";
                var saved = await host.Client.GetFromJsonAsync<FormResponseDetailResponse>(formRoute + "/responses/me");
                Assert.NotNull(saved);
                Assert.Equal(fixture.ResponseId, saved.Id);
                Assert.Equal(fixture.FormId, saved.FormId);
                Assert.Null(saved.RespondentUserId);
                Assert.Null(saved.RespondentEmail);
                Assert.Null(saved.RespondentDisplayName);
                var answer = Assert.Single(saved.Answers);
                Assert.Equal(fixture.QuestionId, answer.FormQuestionId);
                Assert.Equal(HistoricalChoice, answer.AnswerText);

                var summary = await host.Client.GetFromJsonAsync<FormSummaryResponse>(formRoute + "/summary");
                Assert.NotNull(summary);
                Assert.Equal(1, summary.TotalResponses);
                var choiceCounts = Assert.Single(summary.QuestionSummaries).Counts;
                Assert.NotNull(choiceCounts);
                Assert.Equal(1, choiceCounts[HistoricalChoice]);
                Assert.Equal(0, choiceCounts[CurrentChoice]);

                var unanswered = await host.Client.GetFromJsonAsync<UnansweredUserResponse[]>(formRoute + "/unanswered-users");
                Assert.NotNull(unanswered);
                Assert.DoesNotContain(unanswered, item => item.UserId == host.Actor.Id);
                Assert.Contains(unanswered, item => item.UserId == host.Recipient.Id);

                using var duplicate = await host.PostAsync(formRoute + "/responses",
                    new SubmitFormResponseRequest([new SubmitFormAnswerRequest(fixture.QuestionId, CurrentChoice)]));
                Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
                Assert.Contains("already submitted", await duplicate.Content.ReadAsStringAsync(), StringComparison.Ordinal);
                var summaryAfter = await host.Client.GetFromJsonAsync<FormSummaryResponse>(formRoute + "/summary");
                Assert.Equal(1, summaryAfter?.TotalResponses);

                var emptyFormRoute = $"/api/forms/{fixture.EmptyFormId:D}";
                using var accepted = await host.PostAsync(emptyFormRoute + "/responses",
                    new SubmitFormResponseRequest([new SubmitFormAnswerRequest(fixture.EmptyFormQuestionId, "Synthetic new answer")]));
                Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
                var newResponse = await accepted.Content.ReadFromJsonAsync<FormResponseDetailResponse>();
                Assert.NotNull(newResponse);
                Assert.Null(newResponse.RespondentUserId);
                await using (var verification = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    var stored = await verification.Set<FormSubmission>().AsNoTracking()
                        .SingleAsync(row => row.FormDefinitionId == fixture.EmptyFormId);
                    Assert.Equal(newResponse.Id, stored.Id);
                    Assert.Equal(host.Actor.Id, stored.RespondentUserId);
                    Assert.Null(stored.SurveyTargetId);
                    Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database,
                        """SELECT "MaskRespondent" FROM form_responses WHERE "Id" = @id""", ("id", stored.Id)));
                    var sourceCopies = await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database,
                        """SELECT count(*) FROM pre_aq_form_responses WHERE "Id" = @id""", ("id", stored.Id));
                    Assert.Equal(0, sourceCopies);
                    var legacyValues = await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database,
                        """SELECT count(*) FROM pre_aq_form_answers WHERE "FormResponseId" = @id""", ("id", stored.Id));
                    Assert.Equal(0, legacyValues);
                    Assert.Equal(1, await verification.Set<FormSubmissionValue>().CountAsync(row => row.FormSubmissionId == stored.Id));
                }

                var eventRoute = $"/api/events/{fixture.ActivityId:D}";
                var activity = await host.Client.GetFromJsonAsync<EventDetailResponse>(eventRoute);
                Assert.NotNull(activity);
                Assert.Equal(AttendanceStatus.Attending, activity.CurrentUserAttendance?.Status);
                using var updated = await host.PostAsync(eventRoute + "/attendance/me",
                    new UpdateMyAttendanceRequest(AttendanceStatus.Maybe, "Synthetic updated attendance"));
                Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
                var attendance = await host.Client.GetFromJsonAsync<AttendanceResponse[]>(eventRoute + "/attendance");
                var current = Assert.Single(Assert.IsType<AttendanceResponse[]>(attendance), row => row.UserId == host.Actor.Id);
                Assert.Equal(AttendanceStatus.Maybe, current.Status);
                Assert.Equal("Synthetic updated attendance", current.Comment);
                await using var attendanceDb = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
                Assert.Equal(AttendanceStatus.Maybe, await attendanceDb.EventAttendances
                    .Where(row => row.EventId == fixture.ActivityId && row.UserId == host.Actor.Id)
                    .Select(row => row.Status).SingleAsync());
            });

    private static async Task AssertCutoverReconciliationAsync(string database)
    {
        var responseDifference = await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, """
            WITH source AS (
                SELECT "Id", "TenantId", "FormId", "RespondentUserId" FROM pre_aq_form_responses
            ), target AS (
                SELECT "Id", "TenantId", "FormDefinitionId", "RespondentUserId" FROM form_submissions
            ), differences AS (
                (SELECT * FROM source EXCEPT ALL SELECT * FROM target)
                UNION ALL
                (SELECT * FROM target EXCEPT ALL SELECT * FROM source)
            )
            SELECT count(*) FROM differences
            """);
        Assert.Equal(0, responseDifference);

        var answerDifference = await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, """
            WITH source AS (
                SELECT "Id", "TenantId", "FormResponseId", "FormQuestionId" FROM pre_aq_form_answers
            ), target AS (
                SELECT "Id", "TenantId", "FormSubmissionId", "LegacyQuestionId" FROM form_submission_values
            ), differences AS (
                (SELECT * FROM source EXCEPT ALL SELECT * FROM target)
                UNION ALL
                (SELECT * FROM target EXCEPT ALL SELECT * FROM source)
            )
            SELECT count(*) FROM differences
            """);
        Assert.Equal(0, answerDifference);

        var orphanedAnswers = await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, """
            SELECT count(*) FROM form_submission_values AS value
            LEFT JOIN form_submissions AS submission
                ON submission."Id" = value."FormSubmissionId" AND submission."TenantId" = value."TenantId"
            WHERE submission."Id" IS NULL
            """);
        Assert.Equal(0, orphanedAnswers);

        var staleWrite = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
                """UPDATE pre_aq_form_responses SET "UpdatedAt" = "UpdatedAt" """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, staleWrite.SqlState);
    }

    private static async Task<PredecessorFixture> SeedPredecessorAsync(
        string database, AdmissionHost host)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var now = DateTimeOffset.UtcNow;
        var form = new InternalForm
        {
            TenantId = host.Tenant.Id,
            WorkspaceId = host.Workspace.Id,
            CreatedByUserId = host.Actor.Id,
            Title = "Existing synthetic ordinary form",
            // The cutover can observe today's flag but cannot reconstruct the historical flag.
            IsAnonymous = false,
            FormType = FormType.Survey,
            Status = FormStatus.Open,
            OpensAt = now.AddDays(-1),
            ClosesAt = now.AddDays(1)
        };
        var question = new FormQuestion
        {
            TenantId = form.TenantId,
            FormId = form.Id,
            QuestionText = "Question label observed at cutover",
            QuestionType = FormQuestionType.SingleChoice,
            OptionsJson = """["Current synthetic option"]"""
        };
        var saved = new FormResponse
        {
            TenantId = form.TenantId,
            FormId = form.Id,
            RespondentUserId = host.Actor.Id,
            SubmittedAt = now
        };
        var answer = new FormAnswer
        {
            TenantId = form.TenantId,
            FormResponseId = saved.Id,
            FormQuestionId = question.Id,
            AnswerText = HistoricalChoice
        };
        var activity = new ActivityEvent
        {
            TenantId = form.TenantId,
            WorkspaceId = host.Workspace.Id,
            CreatedByUserId = host.Actor.Id,
            Title = "Existing synthetic ordinary event",
            StartsAt = now.AddHours(1),
            EndsAt = now.AddHours(2),
            Status = EventStatus.Published
        };
        var empty = new InternalForm
        {
            TenantId = form.TenantId,
            WorkspaceId = form.WorkspaceId,
            CreatedByUserId = host.Actor.Id,
            Title = "Synthetic form with no previous responses",
            IsAnonymous = true,
            FormType = FormType.Other,
            Status = FormStatus.Open
        };
        var emptyQuestion = new FormQuestion
        {
            TenantId = form.TenantId,
            FormId = empty.Id,
            QuestionText = "Synthetic question",
            QuestionType = FormQuestionType.ShortText
        };
        db.AddRange(form, question, activity, empty, emptyQuestion);
        db.EventAttendances.Add(new EventAttendance
        {
            TenantId = form.TenantId,
            EventId = activity.Id,
            UserId = host.Actor.Id,
            Status = AttendanceStatus.Attending,
            RespondedAt = now
        });
        await db.SaveChangesAsync();
        // Persist the predecessor's physical shape. Current EF also maps privacy columns added during cutover.
        await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
            INSERT INTO form_responses ("Id", "TenantId", "FormId", "RespondentUserId", "SubmittedAt", "CreatedAt")
            VALUES (@response_id, @tenant_id, @form_id, @respondent_id, @submitted_at, @submitted_at);
            INSERT INTO form_answers ("Id", "TenantId", "FormResponseId", "FormQuestionId", "AnswerText", "CreatedAt")
            VALUES (@answer_id, @tenant_id, @response_id, @question_id, @choice, @submitted_at);
            """, ("response_id", saved.Id), ("tenant_id", form.TenantId), ("form_id", form.Id),
            ("respondent_id", host.Actor.Id), ("submitted_at", now), ("answer_id", answer.Id),
            ("question_id", question.Id), ("choice", HistoricalChoice));
        return new PredecessorFixture(form.Id, question.Id, saved.Id, answer.Id, activity.Id, empty.Id, emptyQuestion.Id);
    }

    private sealed record PredecessorFixture(Guid FormId, Guid QuestionId, Guid ResponseId, Guid AnswerId, Guid ActivityId,
        Guid EmptyFormId, Guid EmptyFormQuestionId);
}
