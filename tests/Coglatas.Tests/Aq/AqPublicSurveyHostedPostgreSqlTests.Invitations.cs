using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coglatas.Application.Auth;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.SurveyFeedback.Csv;
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
    public Task ManualApprovalAndConsentGrantOnlyScopedFeedbackUntilRevoked() =>
        AqPostgreSqlScenario.WithHostedEventAsync(async (database, host, project, activity) =>
        {
            var survey = await SeedAsync(database, host, project, activity, presenterFeedbackFixture: true);
            var (operatorAccount, otherPresentationId) = await SeedInvitationActorsAsync(database, host, project, activity);

            using var guest = AqPostgreSqlScenario.CreateGuestClient(host);
            using var recipient = await LoginInvitationActorAsync(host, host.Recipient);
            using var approver = await LoginInvitationActorAsync(host, operatorAccount);
            await host.LoginAsync();

            var presentation = $"/api/v1/core/presentations/{survey.PresentationId:D}";
            var recipients = presentation + "/feedback-invitation-recipients";
            var feedback = presentation + "/feedback";
            var eventRoute = $"/api/v1/core/events/{activity.Id:D}";
            var approvalQueue = eventRoute + "/feedback-invitations/approval-queue";
            var eventSettings = eventRoute + "/feedback-settings";
            using (var answer = await SubmitAsync(guest, survey.TargetCode, "viewer-invitation-first-answer", Payload(survey)))
                await ReadMinimalReceiptAsync(answer);
            await AssertInvitationDeniedAsync(host.Client, recipients);
            await AssertInvitationDeniedAsync(recipient, feedback);
            await AssertInvitationDeniedAsync(approver, feedback);
            await AssertInvitationDeniedAsync(host.Client, approvalQueue);

            using var initial = await GetDataAsync(approver, eventSettings);
            using var configured = await PatchAsync(approver, eventSettings, new
            {
                expectedVersionNo = initial.RootElement.GetProperty("data").GetProperty("versionNo").GetInt64(),
                invitationPolicy = new { enabled = true, decisionWindowSeconds = 3600, maximumActiveSeconds = 7200 }
            });
            var policy = (await ReadInvitationDataAsync(configured, HttpStatusCode.OK))
                .GetProperty("settings").GetProperty("invitationPolicy");
            Assert.True(policy.GetProperty("enabled").GetBoolean());
            Assert.Equal("operator", policy.GetProperty("approvalMode").GetString());
            Assert.Equal(3600, policy.GetProperty("decisionWindowSeconds").GetInt32());
            Assert.Equal(7200, policy.GetProperty("maximumActiveSeconds").GetInt32());

            using (var eligible = await GetDataAsync(host.Client, recipients))
            {
                var entries = eligible.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToArray();
                var invited = Assert.Single(entries, entry => entry.GetProperty("recipientRef").GetGuid() == host.Recipient.Id);
                Assert.Equal(host.Recipient.DisplayName, invited.GetProperty("label").GetString());
                Assert.DoesNotContain(entries, entry =>
                {
                    var personId = entry.GetProperty("recipientRef").GetGuid();
                    return personId == host.Actor.Id || personId == host.ForeignRecipient.Id ||
                        personId == host.OtherWorkspaceRecipient.Id;
                });
                Assert.All(entries, entry => Assert.DoesNotContain(entry.EnumerateObject(),
                    property => property.Name is "email" or "tenantId" or "workspaceId"));
            }
            await AssertInvitationDeniedAsync(approver, recipients);

            using var issue = await host.PostAsync(presentation + "/feedback-invitations", new { recipientRef = host.Recipient.Id });
            var pending = await ReadInvitationDataAsync(issue, HttpStatusCode.Created);
            var invitationId = pending.GetProperty("id").GetGuid();
            var decision = $"/api/v1/core/feedback-invitations/{invitationId:D}";
            Assert.Equal("pending", pending.GetProperty("status").GetString());
            Assert.Equal("awaitingOperator", pending.GetProperty("approvalState").GetString());
            Assert.False(pending.GetProperty("recipientAccepted").GetBoolean());
            Assert.False(pending.TryGetProperty("expiresAt", out _));

            using (var received = await GetDataAsync(recipient, "/api/v1/core/me/feedback-invitations?direction=received"))
            {
                var row = Assert.Single(received.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
                Assert.Equal(invitationId, row.GetProperty("id").GetGuid());
                Assert.Equal("pending", row.GetProperty("status").GetString());
            }
            await AssertInvitationDeniedAsync(recipient, feedback);
            await AssertInvitationDeniedAsync(recipient, approvalQueue);
            using (var queue = await GetDataAsync(approver, approvalQueue))
            {
                var row = Assert.Single(queue.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
                Assert.Equal(invitationId, row.GetProperty("invitation").GetProperty("id").GetGuid());
                Assert.Equal(host.Recipient.DisplayName, row.GetProperty("recipientLabel").GetString());
                Assert.DoesNotContain(AnswerText, row.GetRawText(), StringComparison.Ordinal);
            }

            using var approve = await AdmissionHost.PostAsync(approver, decision + "/approve",
                new { expectedVersionNo = pending.GetProperty("versionNo").GetInt64() });
            var approved = await ReadInvitationDataAsync(approve, HttpStatusCode.OK);
            Assert.Equal("pending", approved.GetProperty("status").GetString());
            Assert.Equal("operatorApproved", approved.GetProperty("approvalState").GetString());
            Assert.False(approved.GetProperty("recipientAccepted").GetBoolean());
            await AssertInvitationDeniedAsync(recipient, feedback);
            using (var afterApproval = await GetDataAsync(approver, approvalQueue))
                Assert.Empty(afterApproval.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());

            using var accept = await AdmissionHost.PostAsync(recipient, decision + "/accept",
                new { expectedVersionNo = approved.GetProperty("versionNo").GetInt64(), consent = true });
            var active = await ReadInvitationDataAsync(accept, HttpStatusCode.OK);
            Assert.Equal("active", active.GetProperty("status").GetString());
            Assert.True(active.GetProperty("recipientAccepted").GetBoolean());
            Assert.True(active.GetProperty("expiresAt").TryGetDateTimeOffset(out _));

            Guid answerId;
            using (var granted = await GetDataAsync(recipient, feedback))
            {
                var row = Assert.Single(granted.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
                var visible = Assert.Single(row.GetProperty("answers").EnumerateArray());
                Assert.Equal(FieldKey, visible.GetProperty("fieldKey").GetString());
                Assert.Equal(AnswerText, visible.GetProperty("value").GetString());
                answerId = visible.GetProperty("id").GetGuid();
            }
            using (var aggregate = await GetDataAsync(recipient, feedback + "-summary"))
                Assert.Equal(1, aggregate.RootElement.GetProperty("data").GetProperty("responses").GetInt64());
            await AssertInvitationDeniedAsync(recipient, $"/api/v1/core/presentations/{otherPresentationId:D}/feedback");
            await AssertInvitationDeniedAsync(recipient, presentation + "/private-notes");
            using (var onward = await AdmissionHost.PostAsync(recipient, presentation + "/feedback-invitations",
                       new { recipientRef = operatorAccount.Id }))
                await AssertErrorAsync(onward, HttpStatusCode.NotFound, "NotFound");
            using (var report = await AdmissionHost.PostAsync(recipient, $"/api/v1/core/feedback/answers/{answerId:D}/reports",
                       new { reason = "Synthetic grant-only report attempt" }))
                await AssertErrorAsync(report, HttpStatusCode.NotFound, "NotFound");
            using (var request = new HttpRequestMessage(HttpMethod.Post, presentation + "/feedback-export")
                   { Content = JsonContent.Create(new FeedbackCsvRequest("aggregate", ["questionLabel"])) })
            {
                request.Headers.Add("Idempotency-Key", "viewer-grant-export-denial");
                using var csv = await SendAsync(recipient, request, includeCsrf: true);
                await AssertErrorAsync(csv, HttpStatusCode.Forbidden, "CapabilityDenied");
            }

            using var revoke = await host.PostAsync(decision + "/revoke",
                new { expectedVersionNo = active.GetProperty("versionNo").GetInt64(), reason = "Synthetic withdrawal" });
            var revoked = await ReadInvitationDataAsync(revoke, HttpStatusCode.OK);
            Assert.Equal("revoked", revoked.GetProperty("status").GetString());
            await AssertInvitationDeniedAsync(recipient, feedback);
            await AssertInvitationDeniedAsync(recipient, feedback + "-summary");
            using (var reopen = await AdmissionHost.PostAsync(approver, decision + "/approve",
                       new { expectedVersionNo = revoked.GetProperty("versionNo").GetInt64() }))
                await AssertErrorAsync(reopen, HttpStatusCode.Conflict, "InvalidStateTransition");

            await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
            var grant = await db.Set<FeedbackViewerGrant>().SingleAsync(row => row.Id == invitationId);
            Assert.Equal(FeedbackViewerGrantStatus.Revoked, grant.Status);
            Assert.Equal(host.Actor.Id, grant.InviterUserId);
            Assert.Equal(operatorAccount.Id, grant.ApprovedByUserId);
            Assert.Equal(host.Recipient.Id, grant.RecipientUserId);
            Assert.NotNull(grant.RecipientAcceptedAtUtc);
            Assert.False(await db.Set<PresentationParticipant>().AnyAsync(row => row.UserId == host.Recipient.Id));
        });

    private static async Task<(User Operator, Guid OtherPresentationId)> SeedInvitationActorsAsync(
        string database, AdmissionHost host, Project project, ActivityEvent activity)
    {
        var hasher = host.Services.GetRequiredService<IPasswordHasher>();
        var passwordHash = hasher.HashPassword(AdmissionHost.Password);
        var email = $"synthetic-invitation-operator-{Guid.NewGuid():N}@example.test";
        var operatorAccount = new User
        {
            DisplayName = "Synthetic invitation operator",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Status = UserStatus.Active,
            PasswordHash = passwordHash
        };
        var alternate = Presentation.Create(activity, "Separate synthetic presentation", null,
            activity.StartsAt, activity.EndsAt);
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var now = DateTimeOffset.UtcNow;
        var recipient = await db.Users.SingleAsync(user => user.Id == host.Recipient.Id);
        recipient.PasswordHash = passwordHash;
        db.AddRange(operatorAccount, alternate,
            new TenantUser
            {
                TenantId = host.Tenant.Id, UserId = operatorAccount.Id, Role = TenantUserRole.Member,
                Status = TenantUserStatus.Active
            },
            new WorkspaceMember
            {
                TenantId = host.Tenant.Id, WorkspaceId = project.WorkspaceId, UserId = operatorAccount.Id,
                Role = WorkspaceRole.Member, Status = MembershipStatus.Active
            });
        foreach (var account in new[] { recipient, operatorAccount })
        {
            db.ProjectMembers.Add(new ProjectMember
            {
                TenantId = host.Tenant.Id, ProjectId = project.Id, UserId = account.Id, Role = ProjectRole.Viewer,
                JoinedAt = now
            });
        }
        foreach (var (user, capability) in new[]
                 {
                     (host.Actor.Id, "feedback.invite.create"),
                     (operatorAccount.Id, "survey.manage"),
                     (operatorAccount.Id, "feedback.invite.approve")
                 })
        {
            db.Add(new CapabilityGrant
            {
                TenantId = host.Tenant.Id, SubjectUserId = user, GrantedByUserId = host.Actor.Id,
                ScopeType = CapabilityScopeType.Workspace, ScopeId = project.WorkspaceId,
                CapabilityKey = capability, GrantedAt = now.AddMinutes(-1), ExpiresAt = now.AddHours(1)
            });
        }
        await db.SaveChangesAsync();
        return (operatorAccount, alternate.Id);
    }

    private static async Task<HttpClient> LoginInvitationActorAsync(AdmissionHost host, User account)
    {
        var client = AqPostgreSqlScenario.CreateGuestClient(host);
        client.DefaultRequestHeaders.Add("X-Tenant-Slug", host.Tenant.Slug);
        try
        {
            using var login = await AdmissionHost.PostAsync(client, "/api/auth/login",
                new LoginRequest(account.Email, AdmissionHost.Password));
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task AssertInvitationDeniedAsync(HttpClient client, string route)
    {
        using var denied = await client.GetAsync(route);
        await AssertErrorAsync(denied, HttpStatusCode.NotFound, "NotFound");
    }

    private static async Task<JsonElement> ReadInvitationDataAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("data").Clone();
    }
}
