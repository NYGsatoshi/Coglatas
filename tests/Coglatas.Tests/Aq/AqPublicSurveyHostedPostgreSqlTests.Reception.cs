using System.Net;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;

namespace Coglatas.Tests.Aq;

public sealed partial class AqPublicSurveyHostedPostgreSqlTests
{
    [PostgreSqlFact]
    public Task HostedReceptionEnforcesExactOpeningClosingAndReceiptExpiry() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                var opening = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()).AddMinutes(5);
                var closing = opening.AddMinutes(5);
                var clock = new ReceptionClock { UtcNow = opening.AddTicks(-1) };
                await using var host = await AdmissionHost.CreateAsync(database, configurationOverrides:
                    new Dictionary<string, string?> { ["AQ:EnablePublicSurveys"] = "true" }, clock: clock);
                var (project, activity) = await AqPostgreSqlScenario.SeedEventAsync(database, host);
                var survey = await SeedAsync(database, host, project, activity);
                await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
                var target = await db.Set<SurveyTarget>().SingleAsync(row => row.Id == survey.TargetId);
                var settings = await db.Set<EventFeedbackSettings>().SingleAsync(row => row.Id == survey.SettingsId);
                target.Configure(target.VersionNo, settings, opening, closing, true, false);
                await db.SaveChangesAsync();

                using var guest = AqPostgreSqlScenario.CreateGuestClient(host);
                const string firstKey = "synthetic-exact-open";
                var body = Payload(survey);
                using (var early = await SubmitAsync(guest, survey.TargetCode, firstKey, body))
                    await AssertErrorAsync(early, HttpStatusCode.Conflict, "InvalidStateTransition");
                clock.UtcNow = opening;
                using var first = await SubmitAsync(guest, survey.TargetCode, firstKey, body);
                var firstReceipt = await ReadMinimalReceiptAsync(first);

                clock.UtcNow = closing.AddTicks(-1);
                using (var last = await SubmitAsync(guest, survey.TargetCode, "synthetic-before-close", body))
                    await ReadMinimalReceiptAsync(last);
                clock.UtcNow = closing;
                using (var closed = await SubmitAsync(guest, survey.TargetCode, "synthetic-at-close", body))
                    await AssertErrorAsync(closed, HttpStatusCode.Conflict, "InvalidStateTransition");
                using (var replay = await SubmitAsync(guest, survey.TargetCode, firstKey, body))
                    Assert.Equal(firstReceipt, await ReadMinimalReceiptAsync(replay));

                clock.UtcNow = opening.AddHours(24);
                using (var expired = await SubmitAsync(guest, survey.TargetCode, firstKey, body))
                    await AssertErrorAsync(expired, HttpStatusCode.Conflict, "InvalidStateTransition");
                Assert.Equal(2, await db.Set<FormSubmission>().CountAsync(row => row.SurveyTargetId == survey.TargetId));
                Assert.Equal(2, await db.Set<SubmissionIdempotency>().CountAsync(row => row.SurveyTargetId == survey.TargetId));
            });

    private sealed class ReceptionClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }
}
