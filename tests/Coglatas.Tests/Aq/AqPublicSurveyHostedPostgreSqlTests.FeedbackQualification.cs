using System.Net;
using Coglatas.Domain.Enums;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Aq;

public sealed partial class AqPublicSurveyHostedPostgreSqlTests
{
    [PostgreSqlFact]
    public Task ManagedFeedbackAndNoPolicyComparisonPreserveCurrentPresentationScope() =>
        AqPostgreSqlScenario.WithHostedEventAsync(async (database, host, project, activity) =>
        {
            var first = await SeedAsync(database, host, project, activity, presenterFeedbackFixture: true);
            var second = await SeedAsync(database, host, project, activity, presenterFeedbackFixture: true,
                existingSettingsId: first.SettingsId);
            var reviewer = await SeedFeedbackReaderAsync(database, host, project, first.PresentationId);
            var manager = await SeedFeedbackReaderAsync(database, host, project, null,
                "feedback.read.managed", "feedback.aggregate.cross");
            var unrelated = await SeedFeedbackReaderAsync(database, host, project, null);
            using var reviewerClient = await LoginInvitationActorAsync(host, reviewer);
            using var managerClient = await LoginInvitationActorAsync(host, manager);
            using var unrelatedClient = await LoginInvitationActorAsync(host, unrelated);
            using var guest = AqPostgreSqlScenario.CreateGuestClient(host);

            const string firstComment = "Synthetic first presentation text";
            const string secondComment = "Synthetic second presentation text";
            using (var result = await SubmitAsync(guest, first.TargetCode, "comparison-first", Payload(first, firstComment, score: 2)))
                await ReadMinimalReceiptAsync(result);
            using (var result = await SubmitAsync(guest, second.TargetCode, "comparison-second", Payload(second, secondComment, score: 4)))
                await ReadMinimalReceiptAsync(result);

            var firstPath = $"/api/v1/core/presentations/{first.PresentationId:D}/feedback";
            var secondPath = $"/api/v1/core/presentations/{second.PresentationId:D}/feedback";
            await AssertScopedFeedbackAsync(reviewerClient, firstPath, firstComment, "comment");
            await AssertScopedFeedbackAsync(managerClient, firstPath, firstComment, "comment");
            await AssertScopedFeedbackAsync(managerClient, secondPath, secondComment, "comment");
            using (var response = await reviewerClient.GetAsync(secondPath))
                await AssertErrorAsync(response, HttpStatusCode.NotFound, "NotFound");
            using (var response = await unrelatedClient.GetAsync(firstPath))
                await AssertErrorAsync(response, HttpStatusCode.NotFound, "NotFound");

            var comparisonPath = $"/api/v1/core/events/{activity.Id:D}/feedback-summary?comparisonRef=synthetic-unapproved";
            using (var comparison = await GetDataAsync(managerClient, comparisonPath))
                Assert.Empty(comparison.RootElement.GetProperty("data").EnumerateObject());
            using (var response = await reviewerClient.GetAsync(comparisonPath))
                await AssertErrorAsync(response, HttpStatusCode.NotFound, "NotFound");

            await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            var membership = await db.TenantUsers.SingleAsync(row => row.TenantId == host.Tenant.Id && row.UserId == reviewer.Id);
            membership.Status = TenantUserStatus.Suspended;
            await db.SaveChangesAsync();
            using var inactive = await reviewerClient.GetAsync(firstPath);
            Assert.True(inactive.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
            Assert.DoesNotContain(firstComment, await inactive.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        });

    private static async Task AssertScopedFeedbackAsync(HttpClient client, string path, string comment, string fieldKey)
    {
        using var detail = await GetDataAsync(client, path);
        var item = Assert.Single(detail.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
        var answer = Assert.Single(item.GetProperty("answers").EnumerateArray(), value =>
            value.GetProperty("fieldKey").GetString() == fieldKey);
        Assert.Equal(comment, answer.GetProperty("value").GetString());
        Assert.DoesNotContain(item.EnumerateObject(), property =>
            property.Name is "accountUserId" or "respondentUserId" or "respondentName");
    }
}
