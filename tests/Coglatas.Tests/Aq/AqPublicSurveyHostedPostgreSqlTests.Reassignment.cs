using System.Net;
using Coglatas.Application.Forms.SurveyManagement;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Tests.Aq;

public sealed partial class AqPublicSurveyHostedPostgreSqlTests
{
    [PostgreSqlFact]
    public Task VersionedParticipantReplacementRevokesOldReviewerAndRequiresExplicitPresenterAssignment() =>
        AqPostgreSqlScenario.WithHostedEventAsync(async (database, host, project, activity) =>
        {
            var survey = await SeedAsync(database, host, project, activity);
            var firstPresenter = await SeedFeedbackReaderAsync(database, host, project, null);
            var secondPresenter = await SeedFeedbackReaderAsync(database, host, project, null);
            var pendingAccount = await SeedFeedbackReaderAsync(database, host, project, null);
            var oldReviewer = await SeedFeedbackReaderAsync(database, host, project, null);
            var retainedReviewer = await SeedFeedbackReaderAsync(database, host, project, null);
            var newReviewer = await SeedFeedbackReaderAsync(database, host, project, null);

            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                db.Add(new CapabilityGrant
                {
                    TenantId = project.TenantId,
                    SubjectUserId = host.Actor.Id,
                    GrantedByUserId = host.Actor.Id,
                    ScopeType = CapabilityScopeType.Workspace,
                    ScopeId = project.WorkspaceId,
                    CapabilityKey = "presentation.assign",
                    GrantedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
                });
                await db.SaveChangesAsync();
            }

            await host.LoginAsync();
            using var guest = AqPostgreSqlScenario.CreateGuestClient(host);
            using var firstPresenterClient = await LoginInvitationActorAsync(host, firstPresenter);
            using var secondPresenterClient = await LoginInvitationActorAsync(host, secondPresenter);
            using var pendingClient = await LoginInvitationActorAsync(host, pendingAccount);
            using var oldReviewerClient = await LoginInvitationActorAsync(host, oldReviewer);
            using var retainedReviewerClient = await LoginInvitationActorAsync(host, retainedReviewer);
            using var newReviewerClient = await LoginInvitationActorAsync(host, newReviewer);
            var presentation = $"/api/v1/core/presentations/{survey.PresentationId:D}";
            var participants = presentation + "/participants";
            var feedback = presentation + "/feedback";
            using var initial = await GetDataAsync(host.Client, participants);
            var initialVersion = initial.RootElement.GetProperty("data").GetProperty("versionNo").GetInt64();
            const string pendingRef = "synthetic-roster-pending";
            var firstAssignment = new AqParticipantUpdateRequest(initialVersion,
                [new("presenter", firstPresenter.Id), new("presenter", secondPresenter.Id),
                    new("reviewer", oldReviewer.Id), new("reviewer", retainedReviewer.Id)], [new(pendingRef)]);
            using var assigned = await ReplaceParticipantsAsync(host.Client, participants, firstAssignment);
            var assignedData = await ReadInvitationDataAsync(assigned, HttpStatusCode.OK);
            var assignedVersion = assignedData.GetProperty("versionNo").GetInt64();
            Assert.Equal(initialVersion + 1, assignedVersion);
            var pending = Assert.Single(assignedData.GetProperty("pendingPresenters").EnumerateArray());
            var opaque = Assert.Single(pending.EnumerateObject());
            Assert.Equal("pendingRosterRef", opaque.Name);
            Assert.Equal(pendingRef, opaque.Value.GetString());
            await using (var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database))
            {
                var record = await db.Set<PresentationParticipant>().SingleAsync(row =>
                    row.PresentationId == survey.PresentationId && row.PendingRosterRef == pendingRef);
                Assert.Null(record.UserId);
            }

            using (var submitted = await SubmitAsync(guest, survey.TargetCode, "synthetic-participant-replacement", Payload(survey)))
                await ReadMinimalReceiptAsync(submitted);
            await AssertReassignmentFeedbackAsync(firstPresenterClient, feedback);
            await AssertReassignmentFeedbackAsync(secondPresenterClient, feedback);
            await AssertReassignmentFeedbackAsync(oldReviewerClient, feedback);
            await AssertReassignmentFeedbackAsync(retainedReviewerClient, feedback);
            await AssertInvitationDeniedAsync(newReviewerClient, feedback);
            await AssertInvitationDeniedAsync(pendingClient, feedback);

            var replacement = new AqParticipantUpdateRequest(assignedVersion,
                [new("presenter", firstPresenter.Id), new("presenter", secondPresenter.Id), new("presenter", pendingAccount.Id),
                    new("reviewer", newReviewer.Id), new("reviewer", retainedReviewer.Id)], []);
            using var replaced = await ReplaceParticipantsAsync(host.Client, participants, replacement);
            var replacedData = await ReadInvitationDataAsync(replaced, HttpStatusCode.OK);
            Assert.Equal(assignedVersion + 1, replacedData.GetProperty("versionNo").GetInt64());
            Assert.Empty(replacedData.GetProperty("pendingPresenters").EnumerateArray());

            using (var stale = await ReplaceParticipantsAsync(host.Client, participants,
                       firstAssignment with { ExpectedVersionNo = assignedVersion }))
                await AssertErrorAsync(stale, HttpStatusCode.Conflict, "RowVersionConflict");
            await AssertInvitationDeniedAsync(oldReviewerClient, feedback);
            foreach (var authorized in new[] { firstPresenterClient, secondPresenterClient, pendingClient, newReviewerClient, retainedReviewerClient })
                await AssertReassignmentFeedbackAsync(authorized, feedback);

            await using var finalDb = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            var rows = await finalDb.Set<PresentationParticipant>().AsNoTracking()
                .Where(row => row.PresentationId == survey.PresentationId).ToListAsync();
            Assert.All(rows, row => Assert.Null(row.PendingRosterRef));
            Assert.Equal(new[] { firstPresenter.Id, secondPresenter.Id, pendingAccount.Id }.Order(),
                rows.Where(row => row.Role == PresentationParticipantRole.Presenter).Select(row => row.UserId!.Value).Order());
            Assert.Equal(new[] { newReviewer.Id, retainedReviewer.Id }.Order(),
                rows.Where(row => row.Role == PresentationParticipantRole.Reviewer).Select(row => row.UserId!.Value).Order());
            var final = await finalDb.Set<Presentation>().SingleAsync(row => row.Id == survey.PresentationId);
            Assert.Equal(assignedVersion + 1, final.VersionNo);
        });

    private static async Task<HttpResponseMessage> ReplaceParticipantsAsync(
        HttpClient client, string route, AqParticipantUpdateRequest payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, route);
        request.Content = JsonContent.Create(payload);
        return await SendAsync(client, request, includeCsrf: true);
    }

    private static async Task AssertReassignmentFeedbackAsync(HttpClient client, string route)
    {
        using var response = await GetDataAsync(client, route);
        var row = Assert.Single(response.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
        var answer = Assert.Single(row.GetProperty("answers").EnumerateArray());
        Assert.Equal(AnswerText, answer.GetProperty("value").GetString());
    }
}
