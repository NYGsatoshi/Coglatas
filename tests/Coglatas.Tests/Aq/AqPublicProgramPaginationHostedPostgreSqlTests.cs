using System.Net;
using System.Text.Json;
using Coglatas.Application.Forms.PublicSurveys;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Tests.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;

namespace Coglatas.Tests.Aq;

[Trait("Feature", "AQ")]
[Trait("Category", "PostgreSQLIntegration")]
public sealed class AqPublicProgramPaginationHostedPostgreSqlTests
{
    [PostgreSqlFact]
    public Task PublishedProgramPaginatesPresentationsWithTiedStartTimes() =>
        AqPostgreSqlScenario.WithHostedEventAsync(async (database, host, project, activity) =>
        {
            var (programCode, targetCodes) = await SeedProgramAsync(database, host, project, activity);
            using var guest = AqPostgreSqlScenario.CreateGuestClient(host);
            var route = $"/api/v1/public/events/{programCode}/presentations?pageSize=1";

            var first = await ReadPageAsync(guest, route);
            var cursor = first.GetProperty("nextCursor").GetString();
            Assert.False(string.IsNullOrWhiteSpace(cursor));
            var second = await ReadPageAsync(guest, route + "&cursor=" + Uri.EscapeDataString(cursor));
            Assert.False(second.TryGetProperty("nextCursor", out _));

            var listedCodes = new[] { first, second }.Select(page =>
            {
                var item = Assert.Single(page.GetProperty("presentations").EnumerateArray());
                Assert.False(item.TryGetProperty("venue", out _));
                Assert.False(item.TryGetProperty("presentationId", out _));
                return item.GetProperty("surveyEntryRef").GetString();
            });
            Assert.Equal(targetCodes.Order(), listedCodes.Order());

            using var invalid = await guest.GetAsync(route + "&cursor=invalid-protected-cursor");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        });

    private static async Task<(string Code, string[] TargetCodes)> SeedProgramAsync(
        string database, AdmissionHost host, Project project, ActivityEvent activity)
    {
        var settings = EventFeedbackSettings.Create(activity, activity.StartsAt, activity.EndsAt, "UTC");
        settings.SetProgram(settings.VersionNo, true, SurveyProgramFields.Title, presenterNamesApproved: false);
        var form = FormDefinition.Create(project.TenantId, host.Actor.Id, null, null, project.Id,
            FormPurpose.Survey, "Synthetic audience survey");
        var now = DateTimeOffset.UtcNow.AddMinutes(-5);
        var common = FormVersion.Publish(form, 1, [new SurveyField("feedback", "Feedback", SurveyFieldType.Text, true, [])], now);
        var campaign = SurveyCampaign.Create(activity, project, form);
        campaign.SetPublication(campaign.VersionNo, enabled: true, published: true);
        var programCode = Guid.NewGuid().ToString("N");
        var program = SurveyPublicCode.ForProgram(settings, AqPublicSurveyService.Hash(programCode), null);
        var codec = host.Services.GetRequiredService<IAqPublicProjectionCodec>();

        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        db.AddRange(settings, form, common, campaign, program);
        var targets = new List<(Presentation Presentation, SurveyTarget Target, PublishedSurveySchema Schema, string Code)>();
        foreach (var title in new[] { "Synthetic first", "Synthetic second" })
        {
            var presentation = Presentation.Create(activity, title, "Unselected venue", activity.StartsAt, activity.EndsAt);
            var target = SurveyTarget.Create(campaign, presentation, Guid.NewGuid().ToString("N"));
            target.Configure(target.VersionNo, settings, null, null, enabled: true, resultsStopped: false);
            var extra = TargetQuestionSetVersion.Publish(target, 1, [], now);
            var schema = PublishedSurveySchema.Compose(target, common, extra, Guid.NewGuid().ToString("N"), now);
            db.AddRange(presentation, target, extra);
            targets.Add((presentation, target, schema, Guid.NewGuid().ToString("N")));
        }
        await db.SaveChangesAsync();
        db.AddRange(targets.Select(item => item.Schema));
        await db.SaveChangesAsync();

        foreach (var (presentation, target, schema, code) in targets)
        {
            target.Publish(target.VersionNo, schema);
            db.Add(SurveyProgramEntry.Select(presentation, target));
            db.Add(SurveyPublicCode.ForTarget(target, AqPublicSurveyService.Hash(code), codec.ProtectEntry(code)));
        }
        await db.SaveChangesAsync();
        return (programCode, targets.Select(item => item.Code).ToArray());
    }

    private static async Task<JsonElement> ReadPageAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected the published program. Observed {(int)response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("data").Clone();
    }
}
