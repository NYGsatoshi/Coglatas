using System.Net;
using System.Text.Json;
using Coglatas.Application.SurveyFeedback;
using Coglatas.Domain.Entities;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Coglatas.Tests.Aq;

public sealed partial class AqPublicSurveyHostedPostgreSqlTests
{
    [PostgreSqlFact]
    public Task ConcurrentVisibilityChangesCommitOnceAndFailedOutboxLeavesNoPartialWrite() =>
        AqPostgreSqlScenario.WithHostedEventAsync(async (database, host, project, activity) =>
        {
            var survey = await SeedAsync(database, host, project, activity, presenterFeedbackFixture: true);
            await AssignModerationAsync(database, host, project, survey);
            await host.LoginAsync();
            using var guest = AqPostgreSqlScenario.CreateGuestClient(host);
            using (var submitted = await SubmitAsync(guest, survey.TargetCode, "visibility-atomicity", Payload(survey)))
                await ReadMinimalReceiptAsync(submitted);

            Guid answerId;
            using (var initial = await GetDataAsync(host.Client, $"/api/v1/core/presentations/{survey.PresentationId:D}/feedback"))
            {
                var row = Assert.Single(initial.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
                answerId = Assert.Single(row.GetProperty("answers").EnumerateArray()).GetProperty("id").GetGuid();
            }

            var answerRoute = $"/api/v1/core/feedback/answers/{answerId:D}";
            var visibilityRoute = answerRoute + "/visibility";
            using (var report = await host.PostAsync(answerRoute + "/reports", new { reason = "Synthetic visible comment report" }))
                Assert.Equal(HttpStatusCode.Created, report.StatusCode);
            using (var hide = await PatchAsync(host.Client, visibilityRoute, new
                   { expectedVersionNo = 1L, action = "hide", reason = "Synthetic first moderation decision" }))
                Assert.Equal(HttpStatusCode.OK, hide.StatusCode);
            var before = await ReadVisibilityStateAsync(database, survey, answerId);
            Assert.True(before.IsHidden);
            Assert.Equal(2, before.Version);
            Assert.Equal(1, before.Audits);
            Assert.True(before.OutboxEvents > 0);
            Assert.Equal(FeedbackReportStatus.Open, before.ReportStatus);
            Assert.Equal(1, before.ReportVersion);

            // The fixture owns this disposable database. This rejects new AQ outbox rows without changing existing ones.
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(database, """
                ALTER TABLE outbox_events ADD CONSTRAINT test_aq_visibility_outbox_failure
                CHECK ("EventType" <> 'Feedback.PresentationInvalidated.v1') NOT VALID
                """);
            try
            {
                using var rejected = await PatchAsync(host.Client, visibilityRoute, new
                {
                    expectedVersionNo = before.Version, action = "restore", reason = "Synthetic rejected outbox"
                });
                Assert.Equal(HttpStatusCode.InternalServerError, rejected.StatusCode);
                Assert.Equal(before, await ReadVisibilityStateAsync(database, survey, answerId));
            }
            finally
            {
                await PostgreSqlMigrationTestDatabase.ExecuteAsync(database,
                    "ALTER TABLE outbox_events DROP CONSTRAINT test_aq_visibility_outbox_failure");
            }

            await using var blocker = new NpgsqlConnection(database);
            await blocker.OpenAsync();
            await using var transaction = await blocker.BeginTransactionAsync();
            await using var hold = new NpgsqlCommand("""
                SELECT "FormSubmissionValueId" FROM answer_visibility_decisions
                WHERE "FormSubmissionValueId" = @answer FOR UPDATE
                """, blocker, transaction);
            hold.Parameters.AddWithValue("answer", answerId);
            Assert.Equal(answerId, Assert.IsType<Guid>(await hold.ExecuteScalarAsync()));
            var requests = Task.WhenAll(
                PatchAsync(host.Client, visibilityRoute, new
                {
                    expectedVersionNo = before.Version, action = "restore", reason = "Synthetic first concurrent decision"
                }),
                PatchAsync(host.Client, visibilityRoute, new
                {
                    expectedVersionNo = before.Version, action = "restore", reason = "Synthetic second concurrent decision"
                }));
            var observedBoth = await ObservePostgreSqlWaitersAsync(database, blocker.ProcessID, requests);
            await transaction.CommitAsync();
            var outcomes = await requests;
            using (outcomes[0])
            using (outcomes[1])
            {
                Assert.True(observedBoth, "Both independent requests must reach the held PostgreSQL visibility row.");
                var winner = Assert.Single(outcomes, result => result.StatusCode == HttpStatusCode.OK);
                using var visible = JsonDocument.Parse(await winner.Content.ReadAsStringAsync());
                Assert.Equal(before.Version + 1, visible.RootElement.GetProperty("data").GetProperty("versionNo").GetInt64());
                var loser = Assert.Single(outcomes, result => result.StatusCode != HttpStatusCode.OK);
                await AssertErrorAsync(loser, HttpStatusCode.Conflict, "RowVersionConflict");
            }

            Assert.Equal(before with
            {
                IsHidden = false, Version = before.Version + 1, Audits = before.Audits + 1,
                OutboxEvents = before.OutboxEvents + 1
            }, await ReadVisibilityStateAsync(database, survey, answerId));
        });

    private static async Task<VisibilityState> ReadVisibilityStateAsync(string database, SeededSurvey survey, Guid answerId)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var decision = await db.Set<AnswerVisibilityDecision>().AsNoTracking()
            .SingleAsync(row => row.FormSubmissionValueId == answerId);
        var report = await db.Set<FeedbackReportCase>().AsNoTracking()
            .SingleAsync(row => row.FormSubmissionValueId == answerId);
        var audits = await db.AuditLogs.CountAsync(row =>
            row.Action == "Feedback.VisibilityChanged" && row.EntityId == decision.Id);
        var outbox = await db.Set<OutboxEvent>().CountAsync(row =>
            row.EventType == SurveyFeedbackInvalidation.EventName && row.AggregateId == survey.PresentationId);
        return new VisibilityState(decision.IsHidden, decision.VersionNo, report.Status, report.VersionNo, audits, outbox);
    }

    private sealed record VisibilityState(bool IsHidden, long Version, FeedbackReportStatus ReportStatus, long ReportVersion,
        int Audits, int OutboxEvents);
}
