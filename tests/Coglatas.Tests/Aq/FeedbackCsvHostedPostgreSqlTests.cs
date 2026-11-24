using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coglatas.Application.SurveyFeedback.Csv;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Tests.PostgreSql;
using Coglatas.Web.Configuration;
using Coglatas.Web.Controllers;
using Microsoft.EntityFrameworkCore;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;

namespace Coglatas.Tests.Aq;

[Trait("Feature", "AQ")]
[Trait("Category", "PostgreSQLIntegration")]
public sealed class FeedbackCsvHostedPostgreSqlTests
{
    [PostgreSqlFact]
    public Task CsvRequestReplayAndScopedGrantDeliverOnlyCurrentVisibleFeedback() =>
        AqPostgreSqlScenario.WithHostedEventAsync(async (database, host, project, activity) =>
        {
            var (presentationId, answerId) = await SeedAsync(database, host, project, activity);
            var requestRoute = $"/api/v1/core/presentations/{presentationId:D}/feedback-export";
            await host.LoginAsync();

            var requestedFields = new FeedbackCsvRequest("detail", ["questionLabel", "answerValue"]);
            using var first = await PostAsync(host.Client, requestRoute, "native-csv-request", requestedFields);
            using var replay = await PostAsync(host.Client, requestRoute, "native-csv-request", requestedFields);
            Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
            Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
            Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
            using var request = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
            var queued = request.RootElement.GetProperty("data");
            Assert.Equal("queued", queued.GetProperty("state").GetString());
            var requestId = queued.GetProperty("exportRequestId").GetGuid();
            var statusRoute = $"/api/v1/core/presentation-feedback-exports/{requestId:D}";

            using var ready = await host.Client.GetAsync(statusRoute);
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            using var status = JsonDocument.Parse(await ready.Content.ReadAsStringAsync());
            Assert.Equal("ready", status.RootElement.GetProperty("data").GetProperty("state").GetString());
            Assert.True(ready.Headers.CacheControl!.NoStore);

            using var grantResponse = await PostAsync(host.Client, statusRoute + "/download-grants", "native-csv-grant");
            Assert.Equal(HttpStatusCode.Created, grantResponse.StatusCode);
            using var grant = JsonDocument.Parse(await grantResponse.Content.ReadAsStringAsync());
            var grantId = grant.RootElement.GetProperty("data").GetProperty("grantId").GetGuid();
            var delivery = statusRoute + $"/download-grants/{grantId:D}/content";

            using var csv = await host.Client.GetAsync(delivery);
            Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
            Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
            Assert.True(csv.Headers.CacheControl!.NoStore);
            Assert.Contains("\"Synthetic question\",\"'=fictional input\"", await csv.Content.ReadAsStringAsync());

            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                var export = await db.Set<SurveyFeedbackExportRequest>().SingleAsync();
                Assert.Equal(requestId, export.Id);
                Assert.Equal(host.Actor.Id, export.RequesterId);
                var issued = await db.Set<SurveyFeedbackExportGrant>().SingleAsync();
                Assert.Equal(grantId, issued.Id);
                Assert.Equal(host.Actor.Id, issued.RecipientUserId);

                var hidden = new AnswerVisibilityDecision { TenantId = host.Tenant.Id, FormSubmissionValueId = answerId };
                hidden.Decide(true, host.Actor.Id, "Synthetic hide after package grant", hidden.VersionNo);
                db.Add(hidden);
                await db.SaveChangesAsync();
            }

            using var stale = await host.Client.GetAsync(delivery);
            Assert.Equal(HttpStatusCode.Gone, stale.StatusCode);
            var error = await stale.Content.ReadAsStringAsync();
            Assert.Contains("ExportExpired", error);
            Assert.DoesNotContain("fictional input", error);
        });

    private static async Task<(Guid PresentationId, Guid AnswerId)> SeedAsync(
        string database, AdmissionHost host, Project project, ActivityEvent activity)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var settings = EventFeedbackSettings.Create(activity, activity.StartsAt, activity.EndsAt, "UTC");
        var definition = FormDefinition.Create(host.Tenant.Id, host.Actor.Id, null, null, project.Id, FormPurpose.Survey,
            "Synthetic CSV feedback");
        var presentation = Presentation.Create(activity, "Synthetic export", null, activity.StartsAt, activity.EndsAt);
        var campaign = SurveyCampaign.Create(activity, project, definition);
        campaign.SetPublication(campaign.VersionNo, true, true);
        var target = SurveyTarget.Create(campaign, presentation, Guid.NewGuid().ToString("N"));
        target.Configure(target.VersionNo, settings, null, null, true, false);
        var publishedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var common = FormVersion.Publish(definition, 1,
            [new SurveyField("comment", "Synthetic question", SurveyFieldType.Text, true, [])], publishedAt);
        var extra = TargetQuestionSetVersion.Publish(target, 1, [], publishedAt);
        var schema = PublishedSurveySchema.Compose(target, common, extra, "synthetic-export-version", publishedAt);
        var member = await db.WorkspaceMembers.SingleAsync(row =>
            row.TenantId == host.Tenant.Id && row.WorkspaceId == project.WorkspaceId && row.UserId == host.Actor.Id);
        var reviewer = PresentationParticipant.Assign(presentation, project, member, PresentationParticipantRole.Reviewer);
        db.AddRange(settings, definition, presentation, campaign, target, common, extra, reviewer,
            new CapabilityGrant
            {
                TenantId = host.Tenant.Id,
                SubjectUserId = host.Actor.Id,
                GrantedByUserId = host.Actor.Id,
                ScopeType = CapabilityScopeType.Workspace,
                ScopeId = project.WorkspaceId,
                CapabilityKey = "feedback.export.detail",
                GrantedAt = publishedAt,
                ExpiresAt = publishedAt.AddHours(1)
            });
        await db.SaveChangesAsync();

        db.Add(schema);
        await db.SaveChangesAsync();
        target.Publish(target.VersionNo, schema);
        var answers = new Dictionary<string, JsonElement>
        {
            ["comment"] = JsonSerializer.SerializeToElement("=fictional input")
        };
        var submission = FormSubmission.SubmitSurvey(definition, target, schema, answers, DateTimeOffset.UtcNow);
        db.Add(submission);
        await db.SaveChangesAsync();
        return (presentation.Id, Assert.Single(submission.Values).Id);
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string route, string key, FeedbackCsvRequest? body = null)
    {
        using var bootstrap = await client.GetAsync("/api/security/csrf-token");
        bootstrap.EnsureSuccessStatusCode();
        var token = await bootstrap.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        Assert.NotNull(token);

        using var request = new HttpRequestMessage(HttpMethod.Post, route);
        request.Headers.Add(SecurityOptions.CsrfHeaderName, token.Token);
        request.Headers.Add("Idempotency-Key", key);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        return await client.SendAsync(request);
    }
}
