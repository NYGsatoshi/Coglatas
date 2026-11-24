using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Aq;

public sealed partial class AqPublicSurveyHostedPostgreSqlTests
{
    [PostgreSqlFact]
    public Task EventReceptionAndResultStopsAreIndependentAcrossCampaignsAndExports() =>
        AqPostgreSqlScenario.WithHostedEventAsync(async (database, host, project, activity) =>
        {
            var first = await SeedAsync(database, host, project, activity, presenterFeedbackFixture: true);
            var second = await SeedAsync(database, host, project, activity, presenterFeedbackFixture: true,
                existingSettingsId: first.SettingsId);
            var surveys = new[] { first, second };
            await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            foreach (var key in new[] { "survey.manage", "feedback.export.aggregate" })
            {
                db.Add(new CapabilityGrant
                {
                    TenantId = project.TenantId, SubjectUserId = host.Actor.Id, GrantedByUserId = host.Actor.Id,
                    ScopeType = CapabilityScopeType.Workspace, ScopeId = project.WorkspaceId,
                    CapabilityKey = key, GrantedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
                });
            }
            await db.SaveChangesAsync();

            var settingsRoute = $"/api/v1/core/events/{activity.Id:D}/feedback-settings";
            await host.LoginAsync();
            using var guest = AqPostgreSqlScenario.CreateGuestClient(host);
            var exportRoutes = new List<string>();
            foreach (var survey in surveys)
            {
                using var answer = await SubmitAsync(guest, survey.TargetCode, "before-stop", Payload(survey, score: 4));
                await ReadMinimalReceiptAsync(answer);
                using var export = new HttpRequestMessage(HttpMethod.Post,
                    $"/api/v1/core/presentations/{survey.PresentationId:D}/feedback-export")
                {
                    Content = JsonContent.Create(new { mode = "aggregate", columns = new[] { "responseCount" } })
                };
                export.Headers.Add("Idempotency-Key", $"before-stop-{survey.TargetId:N}");
                using var queued = await SendAsync(host.Client, export, includeCsrf: true);
                Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
                using var queueBody = JsonDocument.Parse(await queued.Content.ReadAsStringAsync());
                var requestId = queueBody.RootElement.GetProperty("data").GetProperty("exportRequestId").GetGuid();
                exportRoutes.Add($"/api/v1/core/presentation-feedback-exports/{requestId:D}");
            }

            using var settings = await GetDataAsync(host.Client, settingsRoute);
            var version = settings.RootElement.GetProperty("data").GetProperty("versionNo").GetInt64();
            using (var stopped = await PatchAsync(host.Client, settingsRoute, new { expectedVersionNo = version, receptionStopped = true }))
                Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);
            foreach (var survey in surveys)
            {
                using var blocked = await SubmitAsync(guest, survey.TargetCode, "after-reception-stop", Payload(survey));
                await AssertErrorAsync(blocked, HttpStatusCode.Conflict, "InvalidStateTransition");
                using var summary = await GetDataAsync(host.Client,
                    $"/api/v1/core/presentations/{survey.PresentationId:D}/feedback-summary");
                Assert.Equal(1, summary.RootElement.GetProperty("data").GetProperty("responses").GetInt64());
            }

            using (var stopped = await PatchAsync(host.Client, settingsRoute,
                       new { expectedVersionNo = version + 1, resultsStopped = true }))
                Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);
            foreach (var survey in surveys)
            {
                using var denied = await host.Client.GetAsync($"/api/v1/core/presentations/{survey.PresentationId:D}/feedback-summary");
                await AssertErrorAsync(denied, HttpStatusCode.NotFound, "NotFound");
                Assert.Equal(1, await db.Set<FormSubmission>().CountAsync(row => row.SurveyTargetId == survey.TargetId));
            }
            foreach (var route in exportRoutes)
            {
                using var denied = await host.Client.GetAsync(route);
                await AssertErrorAsync(denied, HttpStatusCode.NotFound, "NotFound");
            }
        });
}
