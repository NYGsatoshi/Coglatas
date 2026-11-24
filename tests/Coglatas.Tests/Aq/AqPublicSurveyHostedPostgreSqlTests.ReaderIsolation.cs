using System.Net;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;

namespace Coglatas.Tests.Aq;

public sealed partial class AqPublicSurveyHostedPostgreSqlTests
{
    [PostgreSqlFact]
    public Task AssignedAdviserKeepsPrivateNotesSeparateFromFeedbackSharedWithPresenter() =>
        AqPostgreSqlScenario.WithHostedEventAsync(async (database, host, project, activity) =>
        {
            var survey = await SeedAsync(database, host, project, activity, presenterFeedbackFixture: true);
            var reviewer = await SeedFeedbackReaderAsync(database, host, project, survey.PresentationId,
                "feedback.note.private", "feedback.feedback.share");
            var unassigned = await SeedFeedbackReaderAsync(database, host, project, null,
                "feedback.note.private", "feedback.feedback.share");
            using var reviewerClient = await LoginInvitationActorAsync(host, reviewer);
            using var unassignedClient = await LoginInvitationActorAsync(host, unassigned);
            await host.LoginAsync();

            var presentation = $"/api/v1/core/presentations/{survey.PresentationId:D}";
            var privateNotes = presentation + "/private-notes";
            var sharedFeedback = presentation + "/shared-feedback";
            const string privateText = "Synthetic adviser-only note";
            const string sharedText = "Synthetic feedback for the presenter";

            using (var created = await AdmissionHost.PostAsync(reviewerClient, privateNotes, new { body = privateText }))
            {
                var note = await ReadInvitationDataAsync(created, HttpStatusCode.Created);
                Assert.Equal(privateText, note.GetProperty("body").GetString());
                Assert.True(note.GetProperty("privateNoteId").TryGetGuid(out _));
            }
            using (var created = await AdmissionHost.PostAsync(reviewerClient, sharedFeedback, new { body = sharedText }))
            {
                var feedback = await ReadInvitationDataAsync(created, HttpStatusCode.Created);
                Assert.Equal(sharedText, feedback.GetProperty("body").GetString());
                Assert.True(feedback.GetProperty("sharedFeedbackId").TryGetGuid(out _));
            }

            using (var reviewerNotes = await GetDataAsync(reviewerClient, privateNotes))
            {
                var note = Assert.Single(reviewerNotes.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
                Assert.Equal(privateText, note.GetProperty("body").GetString());
                Assert.False(note.TryGetProperty("sharedFeedbackId", out _));
            }
            foreach (var client in new[] { reviewerClient, host.Client })
            {
                using var shared = await GetDataAsync(client, sharedFeedback);
                var feedback = Assert.Single(shared.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
                Assert.Equal(sharedText, feedback.GetProperty("body").GetString());
                Assert.False(feedback.TryGetProperty("privateNoteId", out _));
            }

            await AssertInvitationDeniedAsync(host.Client, privateNotes);
            await AssertInvitationDeniedAsync(unassignedClient, privateNotes);
            await AssertInvitationDeniedAsync(unassignedClient, sharedFeedback);
            using (var denied = await AdmissionHost.PostAsync(host.Client, sharedFeedback, new { body = sharedText }))
                await AssertErrorAsync(denied, HttpStatusCode.NotFound, "NotFound");
            using (var denied = await AdmissionHost.PostAsync(unassignedClient, privateNotes, new { body = privateText }))
                await AssertErrorAsync(denied, HttpStatusCode.NotFound, "NotFound");
        });

    [PostgreSqlFact]
    public Task AssignedReadersHaveIndependentReadStateWhileAnOpenReportRemains() =>
        AqPostgreSqlScenario.WithHostedEventAsync(async (database, host, project, activity) =>
        {
            var survey = await SeedAsync(database, host, project, activity, presenterFeedbackFixture: true);
            var first = await SeedFeedbackReaderAsync(database, host, project, survey.PresentationId,
                "feedback.report", "feedback.moderate");
            var second = await SeedFeedbackReaderAsync(database, host, project, survey.PresentationId,
                "feedback.moderate");
            using var firstReader = await LoginInvitationActorAsync(host, first);
            using var secondReader = await LoginInvitationActorAsync(host, second);
            using var guest = AqPostgreSqlScenario.CreateGuestClient(host);
            using (var submitted = await SubmitAsync(guest, survey.TargetCode, "independent-readers", Payload(survey)))
                await ReadMinimalReceiptAsync(submitted);

            var inbox = $"/api/v1/core/feedback/inbox?presentationId={survey.PresentationId:D}";
            using var initial = await GetDataAsync(firstReader, inbox);
            var initialRow = Assert.Single(initial.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
            var submission = initialRow.GetProperty("submission");
            var submissionId = submission.GetProperty("id").GetGuid();
            var answer = Assert.Single(submission.GetProperty("answers").EnumerateArray());
            var answerId = answer.GetProperty("id").GetGuid();
            var observedRevision = initialRow.GetProperty("readerState").GetProperty("observedRevision").GetInt64();
            using (var reported = await AdmissionHost.PostAsync(firstReader,
                       $"/api/v1/core/feedback/answers/{answerId:D}/reports", new { reason = "Synthetic open report" }))
                Assert.Equal(HttpStatusCode.Created, reported.StatusCode);

            await AssertInboxReadStateAsync(firstReader, inbox, submissionId, isRead: false);
            await AssertInboxReadStateAsync(secondReader, inbox, submissionId, isRead: false);

            var readState = $"/api/v1/core/feedback/submissions/{submissionId:D}/read-state";
            using (var read = await PatchAsync(firstReader, readState, new { observedRevision, isRead = true }))
                Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            await AssertInboxReadStateAsync(firstReader, inbox, submissionId, isRead: true);
            await AssertInboxReadStateAsync(secondReader, inbox, submissionId, isRead: false);

            using (var read = await PatchAsync(secondReader, readState, new { observedRevision, isRead = true }))
                Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            using (var unread = await PatchAsync(firstReader, readState, new { observedRevision, isRead = false }))
                Assert.Equal(HttpStatusCode.OK, unread.StatusCode);
            await AssertInboxReadStateAsync(firstReader, inbox, submissionId, isRead: false);
            await AssertInboxReadStateAsync(secondReader, inbox, submissionId, isRead: true);
        });

    private static async Task AssertInboxReadStateAsync(HttpClient client, string inbox, Guid submissionId, bool isRead)
    {
        using var response = await GetDataAsync(client, inbox);
        var data = response.RootElement.GetProperty("data");
        var item = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal(submissionId, item.GetProperty("submission").GetProperty("id").GetGuid());
        Assert.Equal(isRead, item.GetProperty("readerState").GetProperty("isRead").GetBoolean());
        Assert.Equal(isRead ? 0L : 1L, data.GetProperty("unreadCount").GetInt64());
        Assert.Equal(1L, data.GetProperty("unresolvedReportCount").GetInt64());
    }

    private static async Task<User> SeedFeedbackReaderAsync(
        string database, AdmissionHost host, Project project, Guid? presentationId, params string[] capabilities)
    {
        await using var services = host.Services.CreateAsyncScope();
        var hasher = services.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var email = $"synthetic-feedback-reader-{Guid.NewGuid():N}@example.test";
        var reader = new User
        {
            DisplayName = "Synthetic feedback reader",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Status = UserStatus.Active,
            PasswordHash = hasher.HashPassword(AdmissionHost.Password)
        };
        var membership = new WorkspaceMember
        {
            TenantId = project.TenantId, WorkspaceId = project.WorkspaceId, UserId = reader.Id,
            Role = WorkspaceRole.Adviser, Status = MembershipStatus.Active
        };

        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var now = DateTimeOffset.UtcNow;
        db.AddRange(reader, membership,
            new TenantUser
            {
                TenantId = project.TenantId, UserId = reader.Id, Role = TenantUserRole.Member,
                Status = TenantUserStatus.Active
            },
            new ProjectMember
            {
                TenantId = project.TenantId, ProjectId = project.Id, UserId = reader.Id,
                Role = ProjectRole.Viewer, JoinedAt = now
            });
        if (presentationId is { } assignedId)
        {
            var presentation = await db.Set<Presentation>().SingleAsync(row => row.Id == assignedId);
            db.Add(PresentationParticipant.Assign(presentation, project, membership, PresentationParticipantRole.Reviewer));
        }
        foreach (var capability in capabilities)
        {
            db.Add(new CapabilityGrant
            {
                TenantId = project.TenantId, SubjectUserId = reader.Id, GrantedByUserId = host.Actor.Id,
                ScopeType = CapabilityScopeType.Workspace, ScopeId = project.WorkspaceId, CapabilityKey = capability,
                GrantedAt = now.AddMinutes(-1), ExpiresAt = now.AddHours(1)
            });
        }
        await db.SaveChangesAsync();
        return reader;
    }
}
