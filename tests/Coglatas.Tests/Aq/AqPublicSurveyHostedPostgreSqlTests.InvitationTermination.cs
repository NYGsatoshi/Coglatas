using System.Net;
using System.Text.Json;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;

namespace Coglatas.Tests.Aq;

public sealed partial class AqPublicSurveyHostedPostgreSqlTests
{
    [PostgreSqlFact]
    public Task InvitationDeclineWithdrawalRelinquishmentAndExpiryRespectIndependentAssignments() =>
        PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                var clock = new ReceptionClock { UtcNow = DateTimeOffset.UtcNow };
                await using var host = await AdmissionHost.CreateAsync(database, configurationOverrides:
                    new Dictionary<string, string?> { ["AQ:EnablePublicSurveys"] = "true" }, clock: clock);
                var (project, activity) = await AqPostgreSqlScenario.SeedEventAsync(database, host);
                var survey = await SeedAsync(database, host, project, activity, presenterFeedbackFixture: true);
                var (operatorAccount, _) = await SeedInvitationActorsAsync(database, host, project, activity);
                using var guest = AqPostgreSqlScenario.CreateGuestClient(host);
                using var posted = await SubmitAsync(guest, survey.TargetCode, "invitation-terminal-answer", Payload(survey));
                await ReadMinimalReceiptAsync(posted);
                using var recipient = await LoginInvitationActorAsync(host, host.Recipient);
                using var approver = await LoginInvitationActorAsync(host, operatorAccount);
                await host.LoginAsync();

                var presentation = $"/api/v1/core/presentations/{survey.PresentationId:D}";
                var feedback = presentation + "/feedback";
                var settings = $"/api/v1/core/events/{activity.Id:D}/feedback-settings";
                await ConfigureManualInvitationsAsync(approver, settings);

                var declined = await IssueAsync();
                var denied = await EndAsync(recipient, declined, "decline");
                Assert.Equal("declined", denied.GetProperty("status").GetString());
                await AssertInvitationDeniedAsync(recipient, feedback);

                var withdrawn = await IssueAsync();
                var revoked = await EndAsync(host.Client, withdrawn, "revoke");
                Assert.Equal("revoked", revoked.GetProperty("status").GetString());
                await AssertInvitationDeniedAsync(recipient, feedback);

                var pending = await IssueAsync();
                using var consent = await AdmissionHost.PostAsync(recipient, Route(pending, "accept"),
                    new { expectedVersionNo = VersionOf(pending), consent = true });
                var acceptedFirst = await ReadInvitationDataAsync(consent, HttpStatusCode.OK);
                Assert.Equal("pending", acceptedFirst.GetProperty("status").GetString());
                await AssertInvitationDeniedAsync(recipient, feedback);
                var active = await ApproveAsync(acceptedFirst);
                Assert.Equal("active", active.GetProperty("status").GetString());
                using (var response = await GetDataAsync(recipient, feedback))
                    Assert.NotEmpty(response.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
                var relinquished = await EndAsync(recipient, active, "revoke");
                Assert.Equal("revoked", relinquished.GetProperty("status").GetString());
                await AssertInvitationDeniedAsync(recipient, feedback);

                var expiring = await AcceptAsync(await ApproveAsync(await IssueAsync()));
                Assert.Equal("active", expiring.GetProperty("status").GetString());
                clock.UtcNow = expiring.GetProperty("expiresAt").GetDateTimeOffset();
                await AssertInvitationDeniedAsync(recipient, feedback);

                await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
                var assignment = await db.WorkspaceMembers.SingleAsync(row => row.TenantId == host.Tenant.Id &&
                    row.WorkspaceId == project.WorkspaceId && row.UserId == host.Recipient.Id);
                var entity = await db.Set<Presentation>().SingleAsync(row => row.Id == survey.PresentationId);
                db.Add(PresentationParticipant.Assign(entity, project, assignment, PresentationParticipantRole.Reviewer));
                await db.SaveChangesAsync();

                using (var ownAssignment = await GetDataAsync(recipient, feedback))
                    Assert.NotEmpty(ownAssignment.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
                Assert.Equal(3, await db.Set<FeedbackViewerGrant>().CountAsync(grant =>
                    grant.PresentationId == survey.PresentationId &&
                    (grant.Status == FeedbackViewerGrantStatus.Revoked || grant.Status == FeedbackViewerGrantStatus.Declined)));

                async Task<JsonElement> IssueAsync()
                {
                    using var response = await host.PostAsync(presentation + "/feedback-invitations",
                        new { recipientRef = host.Recipient.Id, requestedActiveDurationSeconds = 60 });
                    return await ReadInvitationDataAsync(response, HttpStatusCode.Created);
                }

                static async Task<JsonElement> EndAsync(HttpClient client, JsonElement invitation, string action)
                {
                    using var response = await AdmissionHost.PostAsync(client, Route(invitation, action),
                        new { expectedVersionNo = VersionOf(invitation), reason = "Synthetic terminal decision" });
                    return await ReadInvitationDataAsync(response, HttpStatusCode.OK);
                }

                async Task<JsonElement> ApproveAsync(JsonElement invitation)
                {
                    using var response = await AdmissionHost.PostAsync(approver, Route(invitation, "approve"),
                        new { expectedVersionNo = VersionOf(invitation) });
                    return await ReadInvitationDataAsync(response, HttpStatusCode.OK);
                }

                async Task<JsonElement> AcceptAsync(JsonElement invitation)
                {
                    using var response = await AdmissionHost.PostAsync(recipient, Route(invitation, "accept"),
                        new { expectedVersionNo = VersionOf(invitation), consent = true });
                    return await ReadInvitationDataAsync(response, HttpStatusCode.OK);
                }
            });

    private static long VersionOf(JsonElement invitation) => invitation.GetProperty("versionNo").GetInt64();

    private static string Route(JsonElement invitation, string operation) =>
        $"/api/v1/core/feedback-invitations/{invitation.GetProperty("id").GetGuid():D}/{operation}";
}
