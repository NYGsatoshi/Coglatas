using System.Net;
using System.Text.Json;
using Coglatas.Domain.Entities;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;

namespace Coglatas.Tests.Aq;

public sealed partial class AqPublicSurveyHostedPostgreSqlTests
{
    [PostgreSqlFact]
    public Task PresentationFeedbackAndSubmissionKeysRemainInTheirTenantAndParentScope() =>
        AqPostgreSqlScenario.WithHostedEventAsync(async (database, firstHost, firstProject, firstEvent) =>
        {
            await using var secondHost = await AdmissionHost.CreateAsync(database, configurationOverrides:
                new Dictionary<string, string?> { ["AQ:EnablePublicSurveys"] = "true" });
            var (secondProject, secondEvent) = await AqPostgreSqlScenario.SeedEventAsync(database, secondHost);
            Assert.NotEqual(firstHost.Tenant.Id, secondHost.Tenant.Id);
            var first = await SeedAsync(database, firstHost, firstProject, firstEvent, presenterFeedbackFixture: true);
            var second = await SeedAsync(database, secondHost, secondProject, secondEvent, presenterFeedbackFixture: true);
            await firstHost.LoginAsync();
            await secondHost.LoginAsync();

            using var firstGuest = AqPostgreSqlScenario.CreateGuestClient(firstHost);
            using var secondGuest = AqPostgreSqlScenario.CreateGuestClient(secondHost);
            const string firstAnswer = "Synthetic response in the first tenant";
            const string secondAnswer = "Synthetic response in the second tenant";
            const string sharedKey = "same-idempotency-key-in-disjoint-targets";

            using (var submitted = await SubmitAsync(firstGuest, first.TargetCode, sharedKey, Payload(first, firstAnswer)))
                await ReadMinimalReceiptAsync(submitted);
            using (var submitted = await SubmitAsync(secondGuest, second.TargetCode, sharedKey, Payload(second, secondAnswer)))
                await ReadMinimalReceiptAsync(submitted);
            var firstFeedback = $"/api/v1/core/presentations/{first.PresentationId:D}/feedback";
            var secondFeedback = $"/api/v1/core/presentations/{second.PresentationId:D}/feedback";
            await AssertScopedFeedbackAsync(firstHost.Client, firstFeedback, firstAnswer);
            await AssertScopedFeedbackAsync(secondHost.Client, secondFeedback, secondAnswer);

            foreach (var (host, foreignRoute) in new[] { (firstHost, secondFeedback), (secondHost, firstFeedback) })
            {
                using var summary = await host.Client.GetAsync(foreignRoute + "-summary");
                await AssertErrorAsync(summary, HttpStatusCode.NotFound, "NotFound");
            }

            using (var swappedSchema = await SubmitAsync(firstGuest, first.TargetCode, "foreign-schema", Payload(second)))
                await AssertErrorAsync(swappedSchema, HttpStatusCode.Conflict, "InvalidStateTransition");
            using (var swappedSchema = await SubmitAsync(secondGuest, second.TargetCode, "foreign-schema", Payload(first)))
                await AssertErrorAsync(swappedSchema, HttpStatusCode.Conflict, "InvalidStateTransition");

            await AssertForeignPresentationConstraintAsync(database, first, second);

            await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            var submissions = await db.Set<FormSubmission>().AsNoTracking()
                .Where(row => row.SurveyTargetId == first.TargetId || row.SurveyTargetId == second.TargetId)
                .ToListAsync();
            Assert.Equal(2, submissions.Count);
            foreach (var (host, survey, answer) in new[] { (firstHost, first, firstAnswer), (secondHost, second, secondAnswer) })
            {
                var submission = Assert.Single(submissions, row => row.SurveyTargetId == survey.TargetId);
                Assert.Equal(host.Tenant.Id, submission.TenantId);
                Assert.Equal(survey.SchemaId, submission.PublishedSurveySchemaId);
                var value = await db.Set<FormSubmissionValue>().SingleAsync(row => row.FormSubmissionId == submission.Id);
                Assert.Equal(host.Tenant.Id, value.TenantId);
                Assert.Equal(survey.SchemaId, value.PublishedSurveySchemaId);
                using var json = JsonDocument.Parse(value.ValueJson);
                Assert.Equal(answer, json.RootElement.GetString());
                var receipt = await db.Set<SubmissionIdempotency>().SingleAsync(row => row.SurveyTargetId == survey.TargetId);
                Assert.Equal(submission.Id, receipt.FormSubmissionId);
                Assert.Equal(host.Tenant.Id, receipt.TenantId);
            }
        });

    private static async Task AssertScopedFeedbackAsync(HttpClient client, string route, string answer)
    {
        using var detail = await GetDataAsync(client, route);
        var submission = Assert.Single(detail.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
        var response = Assert.Single(submission.GetProperty("answers").EnumerateArray());
        Assert.Equal(answer, response.GetProperty("value").GetString());
        using var summary = await GetDataAsync(client, route + "-summary");
        Assert.Equal(1, summary.RootElement.GetProperty("data").GetProperty("responses").GetInt64());
    }

    private static async Task AssertForeignPresentationConstraintAsync(string database, SeededSurvey source, SeededSurvey foreign)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        // Keep the source tenant, event, campaign and form fixed. Only the presentation is from another parent scope.
        await using var command = new NpgsqlCommand("""
            INSERT INTO survey_targets
                ("Id", "TenantId", "EventId", "CampaignId", "PresentationId", "FormDefinitionId",
                 "IsEnabled", "ResultsStopped", "LocalMarkerKey", "VersionNo", "CreatedAt")
            SELECT @candidate, "TenantId", "EventId", "CampaignId", @foreign, "FormDefinitionId",
                   "IsEnabled", "ResultsStopped", @marker, "VersionNo", "CreatedAt"
            FROM survey_targets WHERE "Id" = @source
            """, connection, transaction);
        var candidateId = Guid.NewGuid();
        command.Parameters.AddWithValue("candidate", candidateId);
        command.Parameters.AddWithValue("foreign", foreign.PresentationId);
        command.Parameters.AddWithValue("marker", candidateId.ToString("N"));
        command.Parameters.AddWithValue("source", source.TargetId);
        var rejected = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, rejected.SqlState);
        Assert.Equal("FK_survey_targets_presentations_PresentationId_TenantId_EventId", rejected.ConstraintName);
        await transaction.RollbackAsync();
        Assert.Equal(0, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(
            database, """SELECT count(*) FROM survey_targets WHERE "Id" = @candidate""", ("candidate", candidateId)));
    }
}
