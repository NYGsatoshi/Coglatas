using System.Globalization;
using System.Net;
using System.Text.Json;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Tests.PostgreSql;
using Coglatas.Web.Configuration;
using Microsoft.EntityFrameworkCore;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;

namespace Coglatas.Tests.Aq;

public sealed partial class AqPublicSurveyHostedPostgreSqlTests
{
    [PostgreSqlFact]
    public Task PublicSurveyAcceptsSharedPeerWithinQuotaAndRejectsBeforeWritingPastIt() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await using var host = await AdmissionHost.CreateAsync(database,
                    configurationOverrides: new Dictionary<string, string?>
                    {
                        ["AQ:EnablePublicSurveys"] = "true",
                        ["Security:EnableRateLimiting"] = "true"
                    },
                    configureRateLimiting: options =>
                        HttpSecurityPolicy.ConfigureRateLimiting(options, aqReadPermitLimit: 3, aqSubmitPermitLimit: 2));
                var (project, activity) = await AqPostgreSqlScenario.SeedEventAsync(database, host);
                var survey = await SeedAsync(database, host, project, activity);
                using var firstGuest = AqPostgreSqlScenario.CreateGuestClient(host);
                using var secondGuest = AqPostgreSqlScenario.CreateGuestClient(host);
                using var thirdGuest = AqPostgreSqlScenario.CreateGuestClient(host);
                var formRoute = SurveyRoute + survey.TargetCode;

                using (var firstForm = await GetDataAsync(firstGuest, formRoute))
                using (var secondForm = await GetDataAsync(secondGuest, formRoute))
                    Assert.Equal(
                        firstForm.RootElement.GetProperty("data").GetProperty("versionRef").GetString(),
                        secondForm.RootElement.GetProperty("data").GetProperty("versionRef").GetString());

                var accepted = Payload(survey);
                using (var first = await SubmitAsync(firstGuest, survey.TargetCode, "synthetic-shared-first", accepted))
                    await ReadMinimalReceiptAsync(first);
                using (var second = await SubmitAsync(secondGuest, survey.TargetCode, "synthetic-shared-second", accepted))
                    await ReadMinimalReceiptAsync(second);

                const string rejectedKey = "synthetic-shared-rejected";
                const string rejectedAnswer = "Synthetic rejected survey answer";
                using (var rejected = await SubmitAsync(thirdGuest, survey.TargetCode, rejectedKey, Payload(survey, rejectedAnswer)))
                    await AssertRateLimitResponseAsync(rejected, survey.TargetCode, rejectedKey, rejectedAnswer);

                // A submit rejection consumes no read permit. Each new cookie jar
                // still shares the anonymous read budget with the same loopback peer.
                using (var form = await thirdGuest.GetAsync(formRoute))
                    Assert.Equal(HttpStatusCode.OK, form.StatusCode);
                using (var rejected = await thirdGuest.GetAsync(formRoute))
                    await AssertRateLimitResponseAsync(rejected, survey.TargetCode);

                await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
                var submissions = await db.Set<FormSubmission>().AsNoTracking()
                    .Where(row => row.SurveyTargetId == survey.TargetId).ToListAsync();
                Assert.Equal(2, submissions.Count);
                Assert.All(submissions, row => Assert.Equal(FormSubmissionStatus.Submitted, row.Status));
                var ids = submissions.Select(row => row.Id).ToArray();
                var values = await db.Set<FormSubmissionValue>().AsNoTracking()
                    .Where(row => ids.Contains(row.FormSubmissionId)).ToListAsync();
                Assert.Equal(2, values.Count);
                Assert.All(values, value =>
                {
                    using var answer = JsonDocument.Parse(value.ValueJson);
                    Assert.Equal(AnswerText, answer.RootElement.GetString());
                });
                Assert.Equal(2, await db.Set<SubmissionIdempotency>()
                    .CountAsync(row => row.SurveyTargetId == survey.TargetId));
                Assert.Equal(2, await db.AuditLogs.CountAsync(log =>
                    log.TenantId == host.Tenant.Id && log.Action == "AqPublicSurveySubmitted"));
            });

    private static async Task AssertRateLimitResponseAsync(HttpResponseMessage response, params string[] privateValues)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Retry-After", out var retryAfter));
        Assert.True(int.TryParse(Assert.Single(retryAfter), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds));
        Assert.InRange(seconds, 1, 60);
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var envelope = json.RootElement;
        Assert.Equal(["error", "requestId", "status", "traceId"],
            envelope.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(429, envelope.GetProperty("status").GetInt32());
        Assert.Equal("RateLimited", envelope.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(envelope.GetProperty("error").GetProperty("details").EnumerateArray());
        Assert.All(privateValues, value => Assert.DoesNotContain(value, body, StringComparison.Ordinal));
        Assert.DoesNotContain("127.0.0.1", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ip:", body, StringComparison.OrdinalIgnoreCase);
    }
}
