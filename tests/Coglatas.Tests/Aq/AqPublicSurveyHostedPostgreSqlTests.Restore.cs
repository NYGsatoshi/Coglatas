using System.Text.Json;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.SurveyFeedback;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Coglatas.Infrastructure.Persistence.SurveyFeedback;
using Coglatas.Tests.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AdmissionHost = Coglatas.Tests.PostgreSql.ConversationAdmissionHostedHttpTests.AdmissionHost;

namespace Coglatas.Tests.Aq;

public sealed partial class AqPublicSurveyHostedPostgreSqlTests
{
    private const string FixtureHideReason = "Synthetic recovery fixture hide";
    private const string FixtureRevokeReason = "Synthetic recovery fixture revoke";

    [PostgreSqlFact]
    public Task RestoredFixtureRequiresReconciliationBeforePreservingHideAndRevocation()
    {
        var server = PostgreSqlTestEnvironment.RequireConnectionString();
        return PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(server, async original =>
        {
            RestoreScope scope;
            await using (var host = await AdmissionHost.CreateAsync(original, configurationOverrides:
                             new Dictionary<string, string?> { ["AQ:EnablePublicSurveys"] = "true" }))
            {
                var (project, activity) = await AqPostgreSqlScenario.SeedEventAsync(original, host);
                var survey = await SeedAsync(original, host, project, activity, presenterFeedbackFixture: true);
                using var guest = AqPostgreSqlScenario.CreateGuestClient(host);
                using var accepted = await SubmitAsync(guest, survey.TargetCode, "restore-before-decisions", Payload(survey));
                await ReadMinimalReceiptAsync(accepted);
                using var acceptedThenVoided = await SubmitAsync(guest, survey.TargetCode, "restore-before-void", Payload(survey));
                await ReadMinimalReceiptAsync(acceptedThenVoided);
                scope = await SeedRestoreScopeAsync(original, host, project, activity.Id, survey);
                await using var services = host.Services.CreateAsyncScope();
                services.ServiceProvider.GetRequiredService<CurrentTenantService>().SetTenant(host.Tenant.Id, host.Tenant.Slug);
                var authorization = services.ServiceProvider.GetRequiredService<SurveyFeedbackAuthorization>();
                var access = await authorization.ReadAccessAsync(new FeedbackActor(scope.TenantId, scope.RecipientId),
                    scope.PresentationId, CancellationToken.None);
                Assert.True(access is { CanRead: true, Granted: true, Presenter: false, Reviewer: false, Operator: false });
            }

            await AssertRestoreProjectionAsync(original, scope, restricted: false);
            // The source is quiesced before PostgreSQL makes the snapshot copy.
            await PostgreSqlMigrationTestDatabase.WithTemporarySnapshotCloneAsync(server, original, async snapshot =>
            {
                var receipt = await MakeFixtureDecisionsAsync(original, scope);
                await AssertRestoreProjectionAsync(original, scope, restricted: true);

                await PostgreSqlMigrationTestDatabase.WithTemporarySnapshotCloneAsync(server, snapshot, async restored =>
                {
                    // No application host is reopened over the stale copy.
                    await AssertRestoreStateAsync(restored, scope, restricted: false);
                    Assert.False(await TryReconcileFixtureAsync(restored, scope, null));
                    Assert.False(await TryReconcileFixtureAsync(restored, scope, receipt with
                    {
                        Scope = scope with { PresentationId = Guid.NewGuid() }
                    }));
                    Assert.False(await TryReconcileFixtureAsync(restored, scope, receipt with
                    {
                        AcceptedSubmissionIds = [.. receipt.AcceptedSubmissionIds, Guid.NewGuid()]
                    }));
                    await AssertRestoreStateAsync(restored, scope, restricted: false);

                    Assert.True(await TryReconcileFixtureAsync(restored, scope, receipt));
                    Assert.True(await TryReconcileFixtureAsync(restored, scope, receipt));
                    await AssertRestoreProjectionAsync(restored, scope, restricted: true);
                    await AssertRestoreStateAsync(restored, scope, restricted: true);
                });
            });
        });
    }

    private static async Task<RestoreScope> SeedRestoreScopeAsync(string database, AdmissionHost host, Project project,
        Guid eventId, SeededSurvey survey)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var submissions = await db.Set<FormSubmission>().Where(row => row.SurveyTargetId == survey.TargetId)
            .OrderBy(row => row.Id).ToListAsync();
        Assert.Equal(2, submissions.Count);
        var submission = submissions[0];
        var toVoid = submissions[1];
        var voidedAnswer = await db.Set<FormSubmissionValue>().SingleAsync(row => row.FormSubmissionId == toVoid.Id);
        var answer = await db.Set<FormSubmissionValue>().SingleAsync(row => row.FormSubmissionId == submission.Id);
        var policy = new FeedbackInvitationPolicy
        {
            TenantId = host.Tenant.Id, EventId = eventId, Enabled = true,
            DecisionWindowSeconds = 3600, MaximumActiveSeconds = 7200
        };
        var now = DateTimeOffset.UtcNow;
        var grant = FeedbackViewerGrant.Issue(survey.PresentationId, host.Actor.Id, host.Recipient.Id, policy,
            activeDurationSeconds: null, now);
        grant.Approve(policy, host.Actor.Id, grant.VersionNo, now);
        grant.Accept(policy, grant.VersionNo, now);
        db.AddRange(policy, grant, new ProjectMember
        {
            TenantId = host.Tenant.Id, ProjectId = project.Id, UserId = host.Recipient.Id, Role = ProjectRole.Viewer,
            JoinedAt = now
        });
        foreach (var capability in new[] { "feedback.invite.create", "feedback.invite.approve" })
        {
            db.Add(new CapabilityGrant
            {
                TenantId = host.Tenant.Id, SubjectUserId = host.Actor.Id, GrantedByUserId = host.Actor.Id,
                ScopeType = CapabilityScopeType.Workspace, ScopeId = project.WorkspaceId,
                CapabilityKey = capability, GrantedAt = now.AddMinutes(-1), ExpiresAt = now.AddHours(1)
            });
        }
        await db.SaveChangesAsync();

        return new RestoreScope(host.Tenant.Id, host.Actor.Id, host.Recipient.Id, survey.PresentationId,
            survey.TargetId, submission.Id, answer.Id, toVoid.Id, voidedAnswer.Id, toVoid.VersionNo, grant.Id, grant.VersionNo);
    }

    private static async Task<RestoreReceipt> MakeFixtureDecisionsAsync(string database, RestoreScope scope)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var grant = await db.Set<FeedbackViewerGrant>().SingleAsync(row => row.Id == scope.GrantId);
        var decision = new AnswerVisibilityDecision
        {
            TenantId = scope.TenantId, FormSubmissionValueId = scope.AnswerId
        };
        var now = DateTimeOffset.UtcNow;
        decision.Decide(true, scope.ActorId, FixtureHideReason, expectedVersionNo: 1);
        grant.Terminate(false, FixtureRevokeReason, scope.OriginalGrantVersion, now);
        var submission = await db.Set<FormSubmission>().SingleAsync(row => row.Id == scope.VoidedSubmissionId);
        submission.Void(scope.OriginalVoidVersion);
        db.Add(decision);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return new RestoreReceipt(scope, decision.Id, decision.VersionNo, grant.VersionNo, submission.VersionNo, now,
            await RestorableSubmissionIdsAsync(db, scope));
    }

    private static async Task<bool> TryReconcileFixtureAsync(string database, RestoreScope scope,
        RestoreReceipt? receipt)
    {
        if (receipt is null || receipt.Scope != scope)
        {
            return false;
        }

        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var accepted = await RestorableSubmissionIdsAsync(db, scope);
        if (!receipt.AcceptedSubmissionIds.SequenceEqual(accepted))
        {
            return false;
        }

        var grant = await db.Set<FeedbackViewerGrant>().SingleOrDefaultAsync(row => row.Id == scope.GrantId &&
            row.TenantId == scope.TenantId && row.PresentationId == scope.PresentationId &&
            row.InviterUserId == scope.ActorId && row.RecipientUserId == scope.RecipientId);
        var containsAnswer = await db.Set<FormSubmissionValue>().AnyAsync(row => row.Id == scope.AnswerId &&
            row.TenantId == scope.TenantId && row.FormSubmissionId == scope.SubmissionId);
        var submission = await db.Set<FormSubmission>().SingleOrDefaultAsync(row => row.Id == scope.VoidedSubmissionId &&
            row.TenantId == scope.TenantId && row.SurveyTargetId == scope.TargetId);
        if (grant is null || !containsAnswer || submission is null)
        {
            return false;
        }

        var decision = await db.Set<AnswerVisibilityDecision>().SingleOrDefaultAsync(row =>
            row.TenantId == scope.TenantId && row.FormSubmissionValueId == scope.AnswerId);
        if (decision is { IsHidden: true } && decision.Id == receipt.VisibilityDecisionId &&
            decision.VersionNo == receipt.VisibilityVersion && grant.Status == FeedbackViewerGrantStatus.Revoked &&
            grant.VersionNo == receipt.GrantVersion && submission.Status == FormSubmissionStatus.Voided &&
            submission.VersionNo == receipt.VoidVersion)
        {
            return true;
        }
        if (decision is not null || grant.VersionNo != scope.OriginalGrantVersion ||
            !grant.IsActive(receipt.DecisionsAtUtc) || receipt.VisibilityVersion != 2 ||
            receipt.GrantVersion != scope.OriginalGrantVersion + 1 || submission.Status != FormSubmissionStatus.Submitted ||
            submission.VersionNo != scope.OriginalVoidVersion || receipt.VoidVersion != scope.OriginalVoidVersion + 1)
        {
            return false;
        }

        var hide = new AnswerVisibilityDecision
        {
            Id = receipt.VisibilityDecisionId, TenantId = scope.TenantId, FormSubmissionValueId = scope.AnswerId
        };
        hide.Decide(true, scope.ActorId, FixtureHideReason, expectedVersionNo: 1);
        grant.Terminate(false, FixtureRevokeReason, scope.OriginalGrantVersion, receipt.DecisionsAtUtc);
        submission.Void(scope.OriginalVoidVersion);
        db.Add(hide);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }

    private static Task<Guid[]> RestorableSubmissionIdsAsync(AppDbContext db, RestoreScope scope) =>
        db.Set<FormSubmission>().AsNoTracking()
            .Where(row => row.TenantId == scope.TenantId && row.SurveyTargetId == scope.TargetId)
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync();

    private static async Task AssertRestoreStateAsync(string database, RestoreScope scope, bool restricted)
    {
        await using var db = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        Assert.Equal(new[] { scope.SubmissionId, scope.VoidedSubmissionId }, await RestorableSubmissionIdsAsync(db, scope));
        var saved = await db.Set<FormSubmission>().AsNoTracking().SingleAsync(row => row.Id == scope.SubmissionId);
        Assert.Equal(FormSubmissionStatus.Submitted, saved.Status);
        var value = await db.Set<FormSubmissionValue>().AsNoTracking().SingleAsync(row => row.Id == scope.AnswerId);
        Assert.Equal(AnswerText, JsonSerializer.Deserialize<string>(value.ValueJson));
        var retired = await db.Set<FormSubmission>().AsNoTracking().SingleAsync(row => row.Id == scope.VoidedSubmissionId);
        Assert.Equal(restricted ? FormSubmissionStatus.Voided : FormSubmissionStatus.Submitted, retired.Status);
        Assert.Equal(scope.OriginalVoidVersion + (restricted ? 1 : 0), retired.VersionNo);
        var retiredValue = await db.Set<FormSubmissionValue>().AsNoTracking().SingleAsync(row => row.Id == scope.VoidedAnswerId);
        Assert.Equal(scope.VoidedSubmissionId, retiredValue.FormSubmissionId);
        Assert.Equal(AnswerText, JsonSerializer.Deserialize<string>(retiredValue.ValueJson));
        var grant = await db.Set<FeedbackViewerGrant>().AsNoTracking().SingleAsync(row => row.Id == scope.GrantId);
        Assert.Equal(restricted ? FeedbackViewerGrantStatus.Revoked : FeedbackViewerGrantStatus.Active, grant.Status);
        Assert.Equal(scope.OriginalGrantVersion + (restricted ? 1 : 0), grant.VersionNo);
        var decisions = await db.Set<AnswerVisibilityDecision>().AsNoTracking()
            .Where(row => row.FormSubmissionValueId == scope.AnswerId).ToListAsync();
        if (restricted)
        {
            var decision = Assert.Single(decisions);
            Assert.True(decision.IsHidden);
            Assert.Equal(2, decision.VersionNo);
        }
        else
        {
            Assert.Empty(decisions);
        }
    }

    private static async Task AssertRestoreProjectionAsync(string database, RestoreScope scope, bool restricted)
    {
        var tenant = new CurrentTenantService();
        tenant.SetTenant(scope.TenantId, "synthetic-restore-fixture");
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).Options, tenant);
        var repository = new EfSurveyFeedbackRepository(db);
        var eligibleIssuers = await repository.GetActiveGrantIssuersAsync(scope.TenantId, scope.PresentationId,
            scope.RecipientId, DateTimeOffset.UtcNow, CancellationToken.None);
        var rows = await repository.ReadAsync(scope.TenantId, scope.PresentationId, null, "visible", null, 5, CancellationToken.None);
        var row = Assert.Single(rows, row => row.Id == scope.SubmissionId);
        Assert.Equal(restricted ? 1 : 2, rows.Count);
        if (restricted)
        {
            Assert.DoesNotContain(rows, item => item.Id == scope.VoidedSubmissionId);
        }
        else
        {
            Assert.Contains(rows, item => item.Id == scope.VoidedSubmissionId);
        }
        if (restricted)
        {
            Assert.Empty(eligibleIssuers);
            Assert.Empty(row.Answers);
        }
        else
        {
            Assert.Equal(scope.ActorId, Assert.Single(eligibleIssuers));
            Assert.Equal(scope.AnswerId, Assert.Single(row.Answers).Id);
        }
    }

    private sealed record RestoreScope(Guid TenantId, Guid ActorId, Guid RecipientId, Guid PresentationId, Guid TargetId,
        Guid SubmissionId, Guid AnswerId, Guid VoidedSubmissionId, Guid VoidedAnswerId, long OriginalVoidVersion,
        Guid GrantId, long OriginalGrantVersion);
    private sealed record RestoreReceipt(RestoreScope Scope, Guid VisibilityDecisionId, long VisibilityVersion,
        long GrantVersion, long VoidVersion, DateTimeOffset DecisionsAtUtc, Guid[] AcceptedSubmissionIds);
}
