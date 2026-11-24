using System.Net;
using System.Text.Json;
using Coglatas.Application.Forms.PublicSurveys;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Tests.PostgreSql;
using Coglatas.Web.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;

namespace Coglatas.Tests.Aq;

[Trait("Feature", "AQ")]
[Trait("Category", "PostgreSQLIntegration")]
public sealed class AqVersionCompatibilityHostedPostgreSqlTests
{
    private const string Core = "/api/v1/core";

    [PostgreSqlFact]
    public Task AuthoringACompatibleCorrectionKeepsHistoricalLabelsAndPoolsOnlyApprovedLineage() =>
        AqPostgreSqlScenario.WithHostedEventAsync(async (database, host, project, activity) =>
        {
            var survey = await SeedAsync(database, host, project, activity);
            var publicRoute = "/api/v1/public/surveys/" + survey.EntryCode;
            var commonRoute = $"{Core}/forms/{survey.Definition.Id:D}/versions";
            var targetRoute = $"{Core}/survey-targets/{survey.Target.Id:D}";
            var feedbackRoute = $"{Core}/presentations/{survey.Presentation.Id:D}/feedback";
            using var guest = AqPostgreSqlScenario.CreateGuestClient(host);
            await host.LoginAsync();

            var initialForm = await GetDataAsync(guest, publicRoute);
            Assert.Equal("Raiting", Assert.Single(initialForm.GetProperty("fields").EnumerateArray()).GetProperty("label").GetString());
            var first = await SubmitAsync(guest, publicRoute, initialForm, "correction-intent-one", 2, HttpStatusCode.Created);

            using (var denied = await SendAsync(host.Client, HttpMethod.Post, commonRoute, Correction(survey.Definition.VersionNo)))
                Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            await GrantAuthoringAsync(database, host, project);
            using (var unreviewed = await SendAsync(host.Client, HttpMethod.Post, commonRoute, new
            {
                expectedVersionNo = survey.Definition.VersionNo,
                action = "publish",
                fields = new[] { new { fieldKey = "rating", label = "Rating", type = "number", required = true } }
            }))
                Assert.Equal(HttpStatusCode.Conflict, unreviewed.StatusCode);
            using (var incompatible = await SendAsync(host.Client, HttpMethod.Post, commonRoute,
                       Correction(survey.Definition.VersionNo, new { maximum = 10 })))
                Assert.Equal(HttpStatusCode.Conflict, incompatible.StatusCode);

            using var published = await SendAsync(host.Client, HttpMethod.Post, commonRoute, Correction(survey.Definition.VersionNo));
            var publication = (await ReadAsync(published, HttpStatusCode.Created)).GetProperty("data");
            var newCommonId = publication.GetProperty("id").GetGuid();
            var priorVersion = await GetDataAsync(host.Client, $"{commonRoute}/{survey.Common.Id:D}");
            Assert.Equal("Raiting", Assert.Single(priorVersion.GetProperty("fields").EnumerateArray()).GetProperty("label").GetString());
            var nextVersion = await GetDataAsync(host.Client, $"{commonRoute}/{newCommonId:D}");
            Assert.Equal("Correct a typo", nextVersion.GetProperty("changeReason").GetString());
            Assert.Equal("rating", Assert.Single(nextVersion.GetProperty("compatibility").EnumerateArray())
                .GetProperty("previousFieldKey").GetString());

            using var selected = await SendAsync(host.Client, HttpMethod.Patch, targetRoute,
                new { expectedVersionNo = survey.Target.VersionNo, commonVersionId = newCommonId });
            var selectedTarget = (await ReadAsync(selected, HttpStatusCode.OK)).GetProperty("data");
            Assert.Equal(newCommonId, selectedTarget.GetProperty("commonVersionId").GetGuid());
            Assert.Equal(survey.Target.VersionNo + 1, selectedTarget.GetProperty("versionNo").GetInt64());

            var currentForm = await GetDataAsync(guest, publicRoute);
            Assert.Equal("Rating", Assert.Single(currentForm.GetProperty("fields").EnumerateArray()).GetProperty("label").GetString());
            Assert.NotEqual(initialForm.GetProperty("schemaDigest").GetString(), currentForm.GetProperty("schemaDigest").GetString());
            Assert.Equal(initialForm.GetProperty("localMarkerKey").GetString(), currentForm.GetProperty("localMarkerKey").GetString());
            var replay = await SubmitAsync(guest, publicRoute, initialForm, "correction-intent-one", 2, HttpStatusCode.Created);
            Assert.Equal(first.GetProperty("requestId").GetString(), replay.GetProperty("requestId").GetString());
            await SubmitAsync(guest, publicRoute, initialForm, "correction-stale-key", 5, HttpStatusCode.Conflict);
            await SubmitAsync(guest, publicRoute, currentForm, "correction-intent-two", 4, HttpStatusCode.Created);

            var feedback = await GetDataAsync(host.Client, feedbackRoute);
            var answers = feedback.GetProperty("items").EnumerateArray()
                .Select(row => Assert.Single(row.GetProperty("answers").EnumerateArray()))
                .ToDictionary(answer => answer.GetProperty("value").GetInt32(), answer => answer.GetProperty("label").GetString());
            Assert.Equal("Raiting", answers[2]);
            Assert.Equal("Rating", answers[4]);
            var summary = await GetDataAsync(host.Client, feedbackRoute + "-summary");
            var rating = Assert.Single(summary.GetProperty("questions").EnumerateArray());
            Assert.Equal(2, rating.GetProperty("denominator").GetInt64());
            Assert.Equal(3m, rating.GetProperty("mean").GetDecimal());

            await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            var priorField = await db.Set<FormFieldDefinition>().SingleAsync(x => x.FormVersionId == survey.Common.Id);
            var nextField = await db.Set<FormFieldDefinition>().SingleAsync(x => x.FormVersionId == newCommonId);
            Assert.Equal(priorField.Id, nextField.CompatiblePreviousFieldId);
            Assert.Equal(priorField.Id, nextField.CompatibilityRootFieldId);
            Assert.Equal(2, await db.Set<PublishedSurveySchema>().CountAsync(x => x.SurveyTargetId == survey.Target.Id));
            Assert.Equal(2, await db.Set<FormSubmission>().CountAsync(x => x.SurveyTargetId == survey.Target.Id));
        });

    private static object Correction(long expectedVersionNo, object? constraints = null) => new
    {
        expectedVersionNo,
        action = "publish",
        fields = new[] { new { fieldKey = "rating", label = "Rating", type = "number", required = true, constraints } },
        changeReason = "Correct a typo",
        compatibility = new[] { new { previousFieldKey = "rating", fieldKey = "rating" } }
    };

    private static async Task SeedAssignmentsAsync(AdmissionHost host, Project project, Presentation presentation,
        Coglatas.Infrastructure.Persistence.AppDbContext db)
    {
        var member = await db.WorkspaceMembers.SingleAsync(x => x.TenantId == project.TenantId &&
            x.WorkspaceId == project.WorkspaceId && x.UserId == host.Actor.Id);
        db.Add(PresentationParticipant.Assign(presentation, project, member, PresentationParticipantRole.Presenter));
    }

    private static async Task<(FormDefinition Definition, FormVersion Common, Presentation Presentation, SurveyTarget Target, string EntryCode)>
        SeedAsync(string database, AdmissionHost host, Project project, ActivityEvent activity)
    {
        var settings = EventFeedbackSettings.Create(activity, activity.StartsAt, activity.EndsAt, "UTC");
        var definition = FormDefinition.Create(project.TenantId, host.Actor.Id, null, null, project.Id,
            FormPurpose.Survey, "Synthetic versioned survey");
        var now = DateTimeOffset.UtcNow.AddMinutes(-5);
        var common = FormVersion.Publish(definition, 1,
            [new SurveyField("rating", "Raiting", SurveyFieldType.Number, true, [])], now);
        definition.AdvancePublicationVersion(definition.VersionNo);
        var presentation = Presentation.Create(activity, "Synthetic presentation", null, activity.StartsAt, activity.EndsAt);
        var campaign = SurveyCampaign.Create(activity, project, definition);
        campaign.SetPublication(campaign.VersionNo, enabled: true, published: true);
        var target = SurveyTarget.Create(campaign, presentation, Guid.NewGuid().ToString("N"));
        target.Configure(target.VersionNo, settings, null, null, enabled: true, resultsStopped: false);
        var extra = TargetQuestionSetVersion.Publish(target, 1, [], now);
        var original = PublishedSurveySchema.Compose(target, common, extra, "synthetic-correction-version", now);
        var code = Guid.NewGuid().ToString("N");
        var codec = host.Services.GetRequiredService<IAqPublicProjectionCodec>();
        var entry = SurveyPublicCode.ForTarget(target, AqPublicSurveyService.Hash(code), codec.ProtectEntry(code));

        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        db.AddRange(settings, definition, common, presentation, campaign, target, extra);
        await SeedAssignmentsAsync(host, project, presentation, db);
        await db.SaveChangesAsync();
        db.Add(original);
        await db.SaveChangesAsync();
        target.Publish(target.VersionNo, original);
        db.Add(entry);
        await db.SaveChangesAsync();
        return (definition, common, presentation, target, code);
    }

    private static async Task GrantAuthoringAsync(string database, AdmissionHost host, Project project)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        foreach (var capability in new[] { "survey.manage", "survey.questions.edit" })
        {
            db.Add(new CapabilityGrant
            {
                TenantId = project.TenantId,
                SubjectUserId = host.Actor.Id,
                GrantedByUserId = host.Actor.Id,
                CapabilityKey = capability,
                ScopeType = CapabilityScopeType.Workspace,
                ScopeId = project.WorkspaceId,
                GrantedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            });
        }
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> SubmitAsync(HttpClient client, string route, JsonElement form,
        string key, int answer, HttpStatusCode expected)
    {
        using var response = await SendAsync(client, HttpMethod.Post, route + "/submissions", new
        {
            versionRef = form.GetProperty("versionRef").GetString(),
            schemaDigest = form.GetProperty("schemaDigest").GetString(),
            answers = new[] { new { fieldKey = "rating", value = answer } }
        }, key);
        return await ReadAsync(response, expected);
    }

    private static async Task<JsonElement> GetDataAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        return (await ReadAsync(response, HttpStatusCode.OK)).GetProperty("data");
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string route,
        object body, string? idempotencyKey = null)
    {
        using var csrf = await client.GetAsync("/api/security/csrf-token");
        var csrfPayload = await ReadAsync(csrf, HttpStatusCode.OK);
        using var request = new HttpRequestMessage(method, route);
        request.Content = JsonContent.Create(body);
        request.Headers.Add(SecurityOptions.CsrfHeaderName, csrfPayload.GetProperty("token").GetString());
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {(int)expected}. Observed {(int)response.StatusCode}: {content}");
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }
}
