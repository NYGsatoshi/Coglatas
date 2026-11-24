using System.Net;
using System.Net.Http.Json;
using Coglatas.Application.Forms;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
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
                var (formId, questionId, responseId, activityId) = await SeedPredecessorAsync(database, host);

                await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
                await using (var verification = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
                {
                    var applied = (await verification.Database.GetAppliedMigrationsAsync()).ToArray();
                    Assert.Contains(Predecessor, applied);
                    Assert.NotEqual(Predecessor, applied[^1]);
                    Assert.Empty(await verification.Database.GetPendingMigrationsAsync());
                }

                await host.LoginAsync();
                var formRoute = $"/api/forms/{formId:D}";
                var saved = await host.Client.GetFromJsonAsync<FormResponseDetailResponse>(formRoute + "/responses/me");
                Assert.NotNull(saved);
                Assert.Equal(responseId, saved.Id);
                Assert.Equal(formId, saved.FormId);
                Assert.Null(saved.RespondentUserId);
                Assert.Null(saved.RespondentEmail);
                Assert.Null(saved.RespondentDisplayName);
                var answer = Assert.Single(saved.Answers);
                Assert.Equal(questionId, answer.FormQuestionId);
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
                    new SubmitFormResponseRequest([new SubmitFormAnswerRequest(questionId, CurrentChoice)]));
                Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
                Assert.Contains("already submitted", await duplicate.Content.ReadAsStringAsync(), StringComparison.Ordinal);
                var summaryAfter = await host.Client.GetFromJsonAsync<FormSummaryResponse>(formRoute + "/summary");
                Assert.Equal(1, summaryAfter?.TotalResponses);

                using var activity = await host.Client.GetAsync($"/api/events/{activityId:D}");
                Assert.Equal(HttpStatusCode.OK, activity.StatusCode);
            });

    private static async Task<(Guid FormId, Guid QuestionId, Guid ResponseId, Guid ActivityId)> SeedPredecessorAsync(
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
            IsAnonymous = true,
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
        db.AddRange(form, question, saved, answer, activity);
        db.EventAttendances.Add(new EventAttendance
        {
            TenantId = form.TenantId,
            EventId = activity.Id,
            UserId = host.Actor.Id,
            Status = AttendanceStatus.Attending,
            RespondedAt = now
        });
        await db.SaveChangesAsync();
        return (form.Id, question.Id, saved.Id, activity.Id);
    }
}
