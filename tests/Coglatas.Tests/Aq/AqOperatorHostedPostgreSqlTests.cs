using System.Net;
using System.Text;
using System.Text.Json;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Tests.PostgreSql;
using Coglatas.Web.Configuration;
using Microsoft.EntityFrameworkCore;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;

namespace Coglatas.Tests.Aq;

[Trait("Feature", "AQ")]
[Trait("Category", "PostgreSQLIntegration")]
public sealed class AqOperatorHostedPostgreSqlTests
{
    [PostgreSqlFact]
    public Task OperatorBuildsSurveyAndRotatesTheSameTargetThroughHostedRoutes() =>
        AqPostgreSqlScenario.WithHostedEventAsync(async (database, host, project, activity) =>
        {
            const string core = "/api/v1/core";
            var eventRoute = $"{core}/events/{activity.Id:D}";
            await host.LoginAsync();
            using (var denied = await host.Client.GetAsync(eventRoute + "/presentations"))
                Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

            await GrantOperatorAsync(database, host, project);
            var settings = await WriteAsync(host.Client, HttpMethod.Patch, eventRoute + "/feedback-settings", new
            {
                expectedVersionNo = 1L,
                startAt = activity.StartsAt,
                endAt = activity.EndsAt,
                timeZone = "UTC",
                nameMode = "anonymous"
            });
            Assert.Equal(2, settings.GetProperty("versionNo").GetInt64());

            var primary = await CreatePresentationAsync(host.Client, eventRoute, activity, "合成発表");
            var alternate = await CreatePresentationAsync(host.Client, eventRoute, activity, "Synthetic alternate");
            var presentationId = primary.GetProperty("presentation").GetProperty("id").GetGuid();
            var alternateId = alternate.GetProperty("presentation").GetProperty("id").GetGuid();
            var firstPage = await GetAsync(host.Client, eventRoute + "/presentations?pageSize=1");
            var cursor = firstPage.GetProperty("nextCursor").GetString();
            Assert.False(string.IsNullOrWhiteSpace(cursor));
            var secondPage = await GetAsync(host.Client, eventRoute + "/presentations?pageSize=1&cursor=" + Uri.EscapeDataString(cursor));
            var ids = new[] { firstPage, secondPage }.Select(page =>
                Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("presentation").GetProperty("id").GetGuid()).ToArray();
            Assert.Equal(new[] { presentationId, alternateId }.Order(), ids.Order());

            var assignmentsRoute = $"{core}/presentations/{presentationId:D}/participants";
            var assignmentRequest = new
            {
                expectedVersionNo = primary.GetProperty("versionNo").GetInt64(),
                assignments = new[] { new { role = "presenter", personRef = host.Actor.Id } },
                pendingPresenters = new[] { new { pendingRosterRef = "synthetic-pending" } }
            };
            var assignment = await WriteAsync(host.Client, HttpMethod.Put, assignmentsRoute, assignmentRequest);
            Assert.Equal(host.Actor.Id, Assert.Single(assignment.GetProperty("assignments").EnumerateArray()).GetProperty("personRef").GetGuid());
            Assert.Equal("synthetic-pending", Assert.Single(assignment.GetProperty("pendingPresenters").EnumerateArray())
                .GetProperty("pendingRosterRef").GetString());
            var assignmentRead = await GetAsync(host.Client, assignmentsRoute);
            Assert.Equal(assignment.GetProperty("versionNo").GetInt64(), assignmentRead.GetProperty("versionNo").GetInt64());
            using (var stale = await SendAsync(host.Client, HttpMethod.Put, assignmentsRoute, assignmentRequest))
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

            var form = await WriteAsync(host.Client, HttpMethod.Post, core + "/forms", new
            {
                owner = new { scopeType = "project", scopeId = project.Id },
                purpose = "survey",
                title = "Synthetic AQ form"
            }, HttpStatusCode.Created);
            var formId = form.GetProperty("id").GetGuid();
            var common = await WriteAsync(host.Client, HttpMethod.Post, $"{core}/forms/{formId:D}/versions", new
            {
                expectedVersionNo = form.GetProperty("versionNo").GetInt64(),
                action = "publish",
                fields = new[] { new { fieldKey = "rating", label = "Rating", type = "number", required = true } }
            }, HttpStatusCode.Created);

            var campaign = await WriteAsync(host.Client, HttpMethod.Post, eventRoute + "/survey-campaigns", new
            {
                formDefinitionId = formId,
                policyRef = activity.Id
            }, HttpStatusCode.Created);
            var campaignId = campaign.GetProperty("id").GetGuid();
            var campaignRoute = $"{core}/survey-campaigns/{campaignId:D}";
            await WriteAsync(host.Client, HttpMethod.Patch, campaignRoute, new
            {
                expectedVersionNo = campaign.GetProperty("versionNo").GetInt64(),
                enabled = true,
                published = true
            });
            var target = await WriteAsync(host.Client, HttpMethod.Post, campaignRoute + "/targets", new
            {
                presentationId,
                commonVersionId = common.GetProperty("id").GetGuid(),
                settings = new { enabled = true, resultsStopped = false }
            }, HttpStatusCode.Created);
            var targetId = target.GetProperty("id").GetGuid();
            var code = target.GetProperty("surveyEntryRef").GetString();

            var configuredProgram = await WriteAsync(host.Client, HttpMethod.Patch, eventRoute + "/feedback-settings", new
            {
                expectedVersionNo = settings.GetProperty("versionNo").GetInt64(),
                programPublished = true,
                publicFields = new[] { "title" },
                programTargets = new[] { new { presentationId, targetId } }
            });
            var program = await WriteAsync(host.Client, HttpMethod.Post, eventRoute + "/program-code/rotate", new
            {
                expectedVersionNo = configuredProgram.GetProperty("versionNo").GetInt64()
            });
            var publicProgramPath = "/api/v1/public/events/" + program.GetProperty("entryRef").GetString() + "/presentations";
            using var guest = AqPostgreSqlScenario.CreateGuestClient(host);
            var listing = await GetAsync(guest, publicProgramPath);
            Assert.Equal(code, Assert.Single(listing.GetProperty("presentations").EnumerateArray()).GetProperty("surveyEntryRef").GetString());

            using (var png = await host.Client.GetAsync(campaignRoute + $"/qr-export?format=png&targetId={targetId:D}"))
            {
                Assert.Equal(HttpStatusCode.OK, png.StatusCode);
                Assert.Equal("image/png", png.Content.Headers.ContentType?.MediaType);
                byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
                Assert.Equal(signature, (await png.Content.ReadAsByteArrayAsync()).Take(8));
            }
            using (var svg = await host.Client.GetAsync(campaignRoute + $"/qr-export?format=svg&targetId={targetId:D}"))
            {
                Assert.Equal(HttpStatusCode.OK, svg.StatusCode);
                Assert.Equal("image/svg+xml", svg.Content.Headers.ContentType?.MediaType);
                Assert.Contains("<svg", await svg.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
            using (var pdf = await host.Client.GetAsync(campaignRoute + "/qr-export?format=pdf"))
            {
                Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
                Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
                Assert.StartsWith("%PDF-1.4", Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync()), StringComparison.Ordinal);
            }

            var previousForm = await GetAsync(guest, "/api/v1/public/surveys/" + code);
            var targetRoute = $"{core}/survey-targets/{targetId:D}";
            var rotation = await WriteAsync(host.Client, HttpMethod.Post, targetRoute + "/public-code/rotate", new
            {
                expectedVersionNo = target.GetProperty("versionNo").GetInt64()
            });
            var newCode = rotation.GetProperty("entryRef").GetString();
            Assert.NotEqual(code, newCode);
            using (var revoked = await guest.GetAsync("/api/v1/public/surveys/" + code))
                Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
            var currentForm = await GetAsync(guest, "/api/v1/public/surveys/" + newCode);
            Assert.Equal(previousForm.GetProperty("versionRef").GetString(), currentForm.GetProperty("versionRef").GetString());
            Assert.Equal(previousForm.GetProperty("localMarkerKey").GetString(), currentForm.GetProperty("localMarkerKey").GetString());
            var rotatedProgram = await GetAsync(guest, publicProgramPath);
            Assert.Equal(newCode, Assert.Single(rotatedProgram.GetProperty("presentations").EnumerateArray())
                .GetProperty("surveyEntryRef").GetString());
            await WriteAsync(host.Client, HttpMethod.Post, targetRoute + "/public-code/revoke", new
            {
                expectedVersionNo = rotation.GetProperty("versionNo").GetInt64()
            });
            using (var revoked = await guest.GetAsync("/api/v1/public/surveys/" + newCode))
                Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);

            await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            Assert.Equal(2, await db.Set<SurveyPublicCode>().CountAsync(x => x.SurveyTargetId == targetId && x.RevokedAtUtc.HasValue));
            Assert.Equal(2, await db.Set<PresentationParticipant>().CountAsync(x => x.PresentationId == presentationId));
        });

    [PostgreSqlFact]
    public Task RosterPreviewDoesNotWriteAndCommitUsesConfirmedSourceVersionAndIdempotency() =>
        AqPostgreSqlScenario.WithHostedEventAsync(async (database, host, project, activity) =>
        {
            const string core = "/api/v1/core";
            var eventRoute = $"{core}/events/{activity.Id:D}";
            await GrantOperatorAsync(database, host, project);
            await host.LoginAsync();
            var created = await CreatePresentationAsync(host.Client, eventRoute, activity, "Synthetic CSV roster target");
            var presentationId = created.GetProperty("presentation").GetProperty("id").GetGuid();
            var participants = $"{core}/presentations/{presentationId:D}/participants";
            var before = await GetAsync(host.Client, participants);
            var originalVersion = before.GetProperty("versionNo").GetInt64();
            Assert.Empty(before.GetProperty("assignments").EnumerateArray());

            var header = "presentationId,role,personRef,pendingRosterRef\n";
            var row = $"{presentationId:D},reviewer,{host.Actor.Id:D},\n";
            var duplicates = await PreviewAsync(host.Client, eventRoute, header + row + row);
            Assert.Equal(JsonValueKind.Null, duplicates.GetProperty("previewRef").ValueKind);
            Assert.Equal("DuplicateAssignment", Assert.Single(duplicates.GetProperty("diagnostics").EnumerateArray())
                .GetProperty("code").GetString());
            var preview = await PreviewAsync(host.Client, eventRoute, header + row);
            Assert.Empty(preview.GetProperty("diagnostics").EnumerateArray());
            var version = Assert.Single(preview.GetProperty("expectedSourceVersions").EnumerateArray());
            Assert.Equal(originalVersion, version.GetProperty("versionNo").GetInt64());
            var untouched = await GetAsync(host.Client, participants);
            Assert.Empty(untouched.GetProperty("assignments").EnumerateArray());
            Assert.Equal(originalVersion, untouched.GetProperty("versionNo").GetInt64());

            var accepted = new
            {
                previewRef = preview.GetProperty("previewRef").GetString(),
                confirmedAssignments = new[] { new { row = 2, assignment = new { role = "reviewer", personRef = host.Actor.Id } } },
                expectedSourceVersions = preview.GetProperty("expectedSourceVersions")
            };
            var commitRoute = eventRoute + "/presentations/import-commit";
            const string key = "synthetic-roster-intent";
            var committed = await WriteAsync(host.Client, HttpMethod.Post, commitRoute, accepted, idempotencyKey: key);
            Assert.Equal(1, committed.GetProperty("presentationsUpdated").GetInt32());
            Assert.Equal(1, committed.GetProperty("assignmentsApplied").GetInt32());
            var replay = await WriteAsync(host.Client, HttpMethod.Post, commitRoute, accepted, idempotencyKey: key);
            Assert.Equal(committed.GetRawText(), replay.GetRawText());
            using (var stale = await SendAsync(host.Client, HttpMethod.Post, commitRoute, accepted, "synthetic-roster-other"))
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

            var final = await GetAsync(host.Client, participants);
            var reviewer = Assert.Single(final.GetProperty("assignments").EnumerateArray());
            Assert.Equal("reviewer", reviewer.GetProperty("role").GetString());
            Assert.Equal(host.Actor.Id, reviewer.GetProperty("personRef").GetGuid());
            Assert.Equal(originalVersion + 1, final.GetProperty("versionNo").GetInt64());
            await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            Assert.Equal(1, await db.Set<PresentationParticipant>().CountAsync(x => x.PresentationId == presentationId));
        });

    private static async Task<JsonElement> PreviewAsync(HttpClient client, string eventRoute, string csv)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(csv, Encoding.UTF8, "text/csv"), "file", "synthetic-roster.csv");
        using var request = new HttpRequestMessage(HttpMethod.Post, eventRoute + "/presentations/import-preview");
        request.Content = multipart;
        request.Headers.Add(SecurityOptions.CsrfHeaderName, await ReadCsrfAsync(client));
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Roster preview: {(int)response.StatusCode} {body}");
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").Clone();
    }

    private static async Task GrantOperatorAsync(string database, AdmissionHost host, Project project)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        foreach (var capability in new[] { "survey.manage", "survey.questions.edit", "presentation.assign" })
        {
            db.Set<CapabilityGrant>().Add(new CapabilityGrant
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

    private static Task<JsonElement> CreatePresentationAsync(HttpClient client, string eventRoute, ActivityEvent activity, string title) =>
        WriteAsync(client, HttpMethod.Post, eventRoute + "/presentations", new
        {
            schedule = new { title, startAt = activity.StartsAt, endAt = activity.EndsAt }
        }, HttpStatusCode.Created);

    private static async Task<JsonElement> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path}: {(int)response.StatusCode} {body}");
        using var result = JsonDocument.Parse(body);
        return result.RootElement.GetProperty("data").Clone();
    }

    private static async Task<JsonElement> WriteAsync<T>(
        HttpClient client, HttpMethod method, string path, T payload, HttpStatusCode expected = HttpStatusCode.OK,
        string? idempotencyKey = null)
    {
        using var response = await SendAsync(client, method, path, payload, idempotencyKey);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{path}: {(int)response.StatusCode} {body}");
        using var result = JsonDocument.Parse(body);
        return result.RootElement.GetProperty("data").Clone();
    }

    private static async Task<HttpResponseMessage> SendAsync<T>(
        HttpClient client, HttpMethod method, string path, T payload, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Content = JsonContent.Create(payload);
        request.Headers.Add(SecurityOptions.CsrfHeaderName, await ReadCsrfAsync(client));
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    private static async Task<string> ReadCsrfAsync(HttpClient client)
    {
        using var csrf = await client.GetAsync("/api/security/csrf-token");
        csrf.EnsureSuccessStatusCode();
        using var csrfJson = JsonDocument.Parse(await csrf.Content.ReadAsStringAsync());
        return csrfJson.RootElement.GetProperty("token").GetString()!;
    }
}
