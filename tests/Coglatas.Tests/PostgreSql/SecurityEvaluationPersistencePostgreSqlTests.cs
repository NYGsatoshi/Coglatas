using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Common.Tenancy;
using Coglatas.Application.Groups;
using Coglatas.Application.ProjectIde.Evaluations;
using Coglatas.Application.ProjectIde.Security;
using Coglatas.Application.Projects;
using Coglatas.Application.Tenancy;
using Coglatas.Application.Workspaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Domain.ProjectIde;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Coglatas.Tests.PostgreSql;

[Trait("Category", "PostgreSQLIntegration")]
public sealed class SecurityEvaluationPersistencePostgreSqlTests
{
    private const string PreviousMigration = "20260924133000_AddCoglatasUiCanonicalChangeJournal";

    [PostgreSqlFact]
    public Task AuthorizedReadModelUsesRealStoreAndNeverReturnsPrivateEvidence() => WithFixtureAsync(async fixture =>
    {
        var binding = fixture.Binding();
        Assert.True(await fixture.Store.CreatePendingAsync(binding));
        Assert.Equal(SecurityTerminalizationResult.Terminalized, await fixture.Store.TerminalizeAsync(binding, Decision()));
        var provider = new CurrentContextProvider(binding);
        var diagnostics = new SecurityEvaluationDiagnostics();
        var reader = new SecurityEvaluationReader(fixture.Store, provider, diagnostics);
        var read = Assert.IsType<SecurityEvaluationReadModel>(await reader.FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId));
        Assert.Equal(SecurityEvaluationFreshness.Current, read.Freshness);
        Assert.Equal(SecurityEvaluationIdentitySnapshot.Capture(binding), read.Identity);
        Assert.Equal(binding.Request.EvaluationId, read.EvaluationId);
        Assert.Equal(SecurityDecisionOutcome.Allow, read.Outcome);
        Assert.Equal(["fixture.a", "fixture.z"], read.Rules.Select(rule => rule.RuleId));
        Assert.False(read.IsAuthoritative);
        Assert.DoesNotContain("canary-private", System.Text.Json.JsonSerializer.Serialize(read));
        Assert.DoesNotContain("canary-private", System.Text.Json.JsonSerializer.Serialize(diagnostics.Snapshot()));
        Assert.DoesNotContain(fixture.Context.ChangeTracker.Entries(), entry =>
            entry.Entity is SecurityEvaluationRun or SecurityEvaluationRuleRecord);
    });

    [PostgreSqlFact]
    public Task AuthorizedReadRejectsKnownIdsAcrossScopesBeforeResolvingCurrentContext() => WithFixtureAsync(async fixture =>
    {
        var binding = fixture.Binding();
        Assert.True(await fixture.Store.CreatePendingAsync(binding));
        var provider = new CurrentContextProvider(binding);
        var reader = new SecurityEvaluationReader(fixture.Store, provider, new());
        Assert.Null(await reader.FindAsync(fixture.Seed.OtherProjectId, binding.Request.EvaluationId));
        fixture.Tenant.SetTenant(fixture.Seed.ForeignTenantId, "foreign-security");
        Assert.Null(await reader.FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId));
        Assert.Null(await reader.FindAsync(fixture.Seed.ForeignProjectId, binding.Request.EvaluationId));
        fixture.Tenant.SetTenant(fixture.Seed.TenantId, "security");
        fixture.Actor.Set(fixture.Seed.ReaderId);
        Assert.Null(await reader.FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId));
        fixture.Actor.Set(null);
        Assert.Null(await reader.FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId));
        Assert.Equal(0, provider.Calls);
    });

    [PostgreSqlFact]
    public Task ReadRechecksRevocationCommittedDuringAwaitedHostResolution() => WithFixtureAsync(async fixture =>
    {
        var binding = fixture.Binding();
        Assert.True(await fixture.Store.CreatePendingAsync(binding));
        var provider = new CurrentContextProvider(binding, async () =>
            await PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database,
                "UPDATE workspace_members SET \"Status\" = 'Suspended' WHERE \"WorkspaceId\" = @workspace AND \"UserId\" = @user",
                ("workspace", fixture.Seed.WorkspaceId), ("user", fixture.Seed.UserId)));
        var reader = new SecurityEvaluationReader(fixture.Store, provider, new());
        Assert.Null(await reader.FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId));
        Assert.Equal(1, provider.Calls);
        Assert.Null(await reader.FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId));
        Assert.Equal(1, provider.Calls);
    });

    [PostgreSqlFact]
    public Task CurrentnessDoesNotRequireTheHistoricalEvaluationActorToBeTheAuthorizedViewer() => WithFixtureAsync(async fixture =>
    {
        var binding = fixture.Binding();
        Assert.True(await fixture.Store.CreatePendingAsync(binding));
        await using (var context = PostgreSqlMigrationTestDatabase.CreatePlatformContext(fixture.Database))
        {
            context.WorkspaceMembers.Add(new WorkspaceMember { TenantId = fixture.Seed.TenantId, WorkspaceId = fixture.Seed.WorkspaceId,
                UserId = fixture.Seed.ReaderId, Status = MembershipStatus.Active, Role = WorkspaceRole.Member });
            context.ProjectMembers.Add(new ProjectMember { TenantId = fixture.Seed.TenantId, ProjectId = fixture.Seed.ProjectId,
                UserId = fixture.Seed.ReaderId, Role = ProjectRole.Viewer });
            await context.SaveChangesAsync();
        }
        fixture.Actor.Set(fixture.Seed.ReaderId);
        var request = binding.Request;
        var current = SecurityBinding.Create(new(Guid.NewGuid(), new(request.Subject.TenantId, fixture.Seed.ReaderId),
            request.Operation, request.Resource, request.EnforcementMode, request.Source, request.Policy, request.Compiler), binding.Evidence);
        Assert.NotEqual(binding.Digest, current.Digest);
        var read = Assert.IsType<SecurityEvaluationReadModel>(await new SecurityEvaluationReader(fixture.Store,
            new CurrentContextProvider(current), new()).FindAsync(fixture.Seed.ProjectId, request.EvaluationId));
        Assert.Equal(SecurityEvaluationFreshness.Current, read.Freshness);
        Assert.Equal(fixture.Seed.UserId, read.Identity.SubjectUserId);
        Assert.False(read.IsAuthoritative);
        Assert.Equal(SecurityEvaluationStatus.Pending, read.Status);
        Assert.Null(read.Outcome);
    });

    [PostgreSqlFact]
    public Task RealHistoricalReadDistinguishesForeignBindingAndMissingHostEvidence() => WithFixtureAsync(async fixture =>
    {
        var binding = fixture.Binding();
        Assert.True(await fixture.Store.CreatePendingAsync(binding));
        var diagnostics = new SecurityEvaluationDiagnostics();
        var stale = Assert.IsType<SecurityEvaluationReadModel>(await new SecurityEvaluationReader(fixture.Store,
            new CurrentContextProvider(fixture.Binding()), diagnostics).FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId));
        Assert.Equal(SecurityEvaluationFreshness.Stale, stale.Freshness);
        Assert.Equal(1, diagnostics.Snapshot().BindingMismatch);
        var unverified = Assert.IsType<SecurityEvaluationReadModel>(await new SecurityEvaluationReader(fixture.Store,
            new CurrentContextProvider(null), diagnostics).FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId));
        Assert.Equal(SecurityEvaluationFreshness.Unverified, unverified.Freshness);
        Assert.Equal(1, diagnostics.Snapshot().BindingMismatch);
    });

    [PostgreSqlFact]
    public Task ShadowSeamAwaitsRealCoordinatorAndDurableStoreWithoutSourceMutation() => WithFixtureAsync(async fixture =>
    {
        var binding = fixture.Binding();
        var sourceBefore = binding.Request.Source!.Data.ToCanonicalBytes();
        var coordinator = new SecurityEvaluationCoordinator(
            [new RevisionBindingRule(), new PolicyBindingRule(), new CompilerProvenanceRule()]);
        var result = await new RevisionSecurityGate(coordinator, fixture.Store, SecurityEnforcementMode.Shadow)
            .AnalyzeAsync(binding.Request, binding.Evidence);
        Assert.Equal(SecurityRecordingStatus.Recorded, result.RecordingStatus);
        Assert.Equal(SecurityDecisionOutcome.Allow, result.Summary.Outcome);
        Assert.False(result.Summary.IsAuthoritative);
        Assert.Equal(sourceBefore, binding.Request.Source.Data.ToCanonicalBytes());
        Assert.True(result.Matches(binding));
        var saved = Assert.IsType<SecurityEvaluationRecord>(
            await fixture.Store.FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId));
        Assert.Equal(result.Summary.Status, saved.Status);
        Assert.Equal(result.Summary.Outcome, saved.Outcome);
        Assert.Equal(result.Summary.ReasonCode, saved.ReasonCode);
        Assert.Equal(3, saved.Rules.Count);
        Assert.DoesNotContain("canary-private-source", await fixture.StoredIdentityAsync(binding));
    });

    [PostgreSqlFact]
    public async Task MigrationUpgradesExistingSchemaAndCanRollbackOnlySecurityRecords()
    {
        await PostgreSqlMigrationTestDatabase.WithTemporaryDatabaseAsync(
            PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
            {
                await PostgreSqlMigrationTestDatabase.MigrateAsync(database, PreviousMigration);
                Assert.Equal(73L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database,
                    "SELECT count(*) FROM \"__EFMigrationsHistory\""));
                var seed = await SeedAsync(database);
                await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
                await using (var fixture = new Fixture(database, seed))
                {
                    var binding = fixture.Binding();
                    Assert.True(await fixture.Store.CreatePendingAsync(binding));
                    Assert.Equal(SecurityTerminalizationResult.Terminalized,
                        await fixture.Store.TerminalizeAsync(binding, Decision()));
                }
                await PostgreSqlMigrationTestDatabase.MigrateAsync(database, PreviousMigration);
                Assert.Equal(3L, await PostgreSqlMigrationTestDatabase.ScalarAsync<long>(database, "SELECT count(*) FROM projects"));
                Assert.True(await PostgreSqlMigrationTestDatabase.ScalarAsync<bool>(database,
                    "SELECT to_regclass('security_evaluation_runs') IS NULL"));
                await PostgreSqlMigrationTestDatabase.MigrateAsync(database);
                await using var current = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
                Assert.Empty(await current.Database.GetPendingMigrationsAsync());
                Assert.Equal(74, (await current.Database.GetAppliedMigrationsAsync()).Count());
                Assert.Empty(await current.SecurityEvaluationRuns.AsNoTracking().ToArrayAsync());
            });
    }

    [PostgreSqlFact]
    public Task SafeBindingRoundTripsAndRuleOrderIsOrdinal() => WithFixtureAsync(async fixture =>
    {
        var binding = fixture.Binding();
        Assert.True(await fixture.Store.CreatePendingAsync(binding));
        var pending = Assert.IsType<SecurityEvaluationRecord>(await fixture.Store.FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId));
        Assert.Equal(binding.Request.EvaluationId, pending.EvaluationId);
        Assert.Equal(fixture.Seed.TenantId, pending.TenantId);
        Assert.Equal(fixture.Seed.ProjectId, pending.ProjectId);
        Assert.Equal(SecurityEvaluationStatus.Pending, pending.Status);
        Assert.Null(pending.Outcome);
        Assert.Null(pending.TerminalAtUtc);
        Assert.Empty(pending.Rules);
        Assert.Equal(SecurityEvaluationIdentitySnapshot.Capture(binding), pending.Identity);
        Assert.Equal(binding.Digest.Value, pending.BindingDigest);
        Assert.Equal(1, pending.SchemaVersion);
        Assert.Equal(SecurityEnforcementMode.Shadow, pending.EnforcementMode);
        var policy = Assert.IsType<SecurityPolicyIdentitySnapshot>(pending.Identity.ExpectedPolicy);
        Assert.Equal("fixture-policy", policy.PolicySetId);
        Assert.Equal("v1", policy.Version);
        Assert.Equal(binding.Evidence.Policy!.Snapshot.ContentDigest.Value, policy.ContentDigest);
        Assert.Equal(1, policy.SchemaVersion);
        Assert.Equal(SecurityTerminalizationResult.Terminalized, await fixture.Store.TerminalizeAsync(binding, Decision()));
        var terminal = Assert.IsType<SecurityEvaluationRecord>(await fixture.Store.FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId));
        Assert.Equal(pending.Identity, terminal.Identity);
        Assert.Equal(pending.BindingDigest, terminal.BindingDigest);
        Assert.Equal(pending.CreatedAtUtc, terminal.CreatedAtUtc);
        Assert.Equal(TimeSpan.Zero, terminal.CreatedAtUtc.Offset);
        Assert.True(terminal.TerminalAtUtc >= terminal.CreatedAtUtc);
        Assert.Equal(SecurityEvaluationStatus.Completed, terminal.Status);
        Assert.Equal(SecurityDecisionOutcome.Allow, terminal.Outcome);
        Assert.Equal(SecurityReasonCode.BindingsVerified, terminal.ReasonCode);
        Assert.Equal(["fixture.a", "fixture.z"], terminal.Rules.Select(rule => rule.RuleId));
        Assert.All(terminal.Rules, rule => Assert.Equal(SecurityDecisionOutcome.Allow, rule.Outcome));
        Assert.DoesNotContain(fixture.Context.ChangeTracker.Entries(), entry =>
            entry.Entity is SecurityEvaluationRun or SecurityEvaluationRuleRecord);
    });

    [PostgreSqlFact]
    public Task CandidateScenarioAndUnknownContextExtensionsRetainExactIdentityWithoutContents() => WithFixtureAsync(async fixture =>
    {
        var branch = new BranchRef(new(fixture.Seed.TenantId), new(fixture.Seed.ProjectId), BranchId.New());
        var ancestor = new BranchRef(branch.TenantId, branch.ProjectId, BranchId.New());
        var proposal = new ProposalContext(ProposalId.New(), CandidateRevisionId.New(),
            new(ancestor, RevisionId.New()), new(branch, RevisionId.New()));
        var candidate = SourceRevisionContext.Candidate(proposal);
        var scenario = SourceRevisionContext.Hypothetical(new(ScenarioId.New(), ScenarioRevisionId.New(), candidate,
            ContentDigest.Compute("fixture.overlay/1", "overlay"u8)));
        foreach (var context in new[] { candidate, scenario })
        {
            var extended = SourceRevisionContext.Parse(SourceJson.Parse(context.Data.CanonicalText.TrimEnd('}') +
                ",\"privateExtension\":\"canary-context-private\"}"));
            var binding = fixture.Binding(extended);
            Assert.True(await fixture.Store.CreatePendingAsync(binding));
            var read = Assert.IsType<SecurityEvaluationRecord>(await fixture.Store.FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId));
            Assert.Equal(SecurityEvaluationIdentitySnapshot.Capture(binding), read.Identity);
            Assert.Equal(proposal.CandidateRevisionId.Value, read.Identity.Resource.CandidateRevisionId);
            Assert.Equal(proposal.ProposalId.Value, read.Identity.Resource.ProposalId);
            Assert.Equal(candidate.Kind, read.Identity.Resource.BaseContextKind);
            Assert.Equal(ancestor.BranchId.Value, read.Identity.Resource.BaseBranchId);
            Assert.Equal(proposal.BaseRevision.RevisionId.Value, read.Identity.Resource.BaseRevisionId);
            Assert.Equal(branch.BranchId.Value, read.Identity.Resource.CapturedHeadBranchId);
            Assert.Equal(proposal.CapturedTargetHead.RevisionId.Value, read.Identity.Resource.CapturedHeadRevisionId);
            Assert.Equal(context.Kind, read.Identity.Resource.ContextKind);
            Assert.Equal(context.Scenario?.ScenarioId.Value, read.Identity.Resource.ScenarioId);
            Assert.Equal(context.Scenario?.ScenarioRevisionId.Value, read.Identity.Resource.ScenarioRevisionId);
            Assert.Equal(context.Scenario?.OverlayDigest.Value, read.Identity.Resource.OverlayDigest);
            Assert.NotEqual(SecuritySourceIdentitySnapshot.Capture(context, binding.Request.Resource.InputDigest).ContextDigest,
                read.Identity.Resource.ContextDigest);
            Assert.DoesNotContain("canary-context-private", await fixture.StoredIdentityAsync(binding));
        }
    });

    [PostgreSqlFact]
    public Task ReEvaluationAppendsWithTheSameBindingAndCannotReplaceTerminalResult() => WithFixtureAsync(async fixture =>
    {
        var binding = fixture.Binding();
        Assert.True(await fixture.Store.CreatePendingAsync(binding));
        Assert.Equal(SecurityTerminalizationResult.Terminalized, await fixture.Store.TerminalizeAsync(binding, Decision()));
        Assert.Equal(SecurityTerminalizationResult.AlreadyTerminal,
            await fixture.Store.TerminalizeAsync(binding, Decision(SecurityDecisionOutcome.Deny)));
        var request = binding.Request;
        var repeated = SecurityBinding.Create(new(Guid.NewGuid(), request.Subject, request.Operation, request.Resource,
            request.EnforcementMode, request.Source, request.Policy, request.Compiler), binding.Evidence);
        Assert.Equal(binding.Digest.Value, repeated.Digest.Value);
        Assert.True(await fixture.Store.CreatePendingAsync(repeated));
        Assert.Equal(SecurityTerminalizationResult.Terminalized, await fixture.Store.TerminalizeAsync(repeated, Decision(SecurityDecisionOutcome.Deny)));
        Assert.Equal(2, await fixture.Context.SecurityEvaluationRuns.AsNoTracking().CountAsync());
        Assert.Equal(SecurityDecisionOutcome.Allow, (await fixture.Store.FindAsync(fixture.Seed.ProjectId, request.EvaluationId))!.Outcome);
        Assert.Equal(SecurityDecisionOutcome.Deny, (await fixture.Store.FindAsync(fixture.Seed.ProjectId, repeated.Request.EvaluationId))!.Outcome);
    });

    [PostgreSqlFact]
    public Task ConcurrentTerminalizationHasExactlyOneCommittedWinner() => WithFixtureAsync(async fixture =>
    {
        var binding = fixture.Binding();
        Assert.True(await fixture.Store.CreatePendingAsync(binding));
        await using var otherContext = fixture.NewContext();
        var otherStore = fixture.CreateStore(otherContext);
        var results = await Task.WhenAll(fixture.Store.TerminalizeAsync(binding, Decision()),
            otherStore.TerminalizeAsync(binding, Decision(SecurityDecisionOutcome.Deny)));
        Assert.Single(results, result => result == SecurityTerminalizationResult.Terminalized);
        Assert.Single(results, result => result == SecurityTerminalizationResult.AlreadyTerminal);
        var read = Assert.IsType<SecurityEvaluationRecord>(await fixture.Store.FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId));
        Assert.Equal(2, read.Rules.Count);
        Assert.All(read.Rules, rule => Assert.Equal(read.Outcome, rule.Outcome));
    });

    [PostgreSqlFact]
    public Task KnownIdsAndMatchingDigestsCannotCrossTenantOrProjectScope() => WithFixtureAsync(async fixture =>
    {
        var binding = fixture.Binding();
        Assert.True(await fixture.Store.CreatePendingAsync(binding));
        Assert.Null(await fixture.Store.FindAsync(fixture.Seed.OtherProjectId, binding.Request.EvaluationId));
        fixture.Tenant.SetTenant(fixture.Seed.ForeignTenantId, "foreign-security");
        Assert.Null(await fixture.Store.FindAsync(fixture.Seed.ForeignProjectId, binding.Request.EvaluationId));
        Assert.Null(await fixture.Store.FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId));
        Assert.False(await fixture.Store.CreatePendingAsync(binding));
        Assert.Equal(SecurityTerminalizationResult.Unavailable, await fixture.Store.TerminalizeAsync(binding, Decision()));
        fixture.Tenant.SetTenant(fixture.Seed.TenantId, "security");
        fixture.Actor.Set(fixture.Seed.ReaderId);
        Assert.Null(await fixture.Store.FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId));
        Assert.False(await fixture.Store.CreatePendingAsync(binding));
        fixture.Actor.Set(null);
        Assert.Null(await fixture.Store.FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId));
        fixture.Actor.Set(fixture.Seed.UserId);
        fixture.Tenant.SetPlatformScope();
        Assert.Null(await fixture.Store.FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId));
    });

    [PostgreSqlFact]
    public Task RevokedWorkspaceMembershipDeniesHistoricalReadInAnAlreadyUsedScope() => WithFixtureAsync(async fixture =>
    {
        var binding = fixture.Binding();
        Assert.True(await fixture.Store.CreatePendingAsync(binding));
        Assert.NotNull(await fixture.Store.FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId));
        await PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database,
            "UPDATE workspace_members SET \"Status\" = 'Suspended' WHERE \"WorkspaceId\" = @workspace AND \"UserId\" = @user",
            ("workspace", fixture.Seed.WorkspaceId), ("user", fixture.Seed.UserId));
        Assert.Null(await fixture.Store.FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId));
        Assert.Equal(SecurityTerminalizationResult.Unavailable, await fixture.Store.TerminalizeAsync(binding, Decision()));
    });

    [PostgreSqlFact]
    public Task RevokedContributionCannotTerminalizeAnAlreadyUsedScope() => WithFixtureAsync(async fixture =>
    {
        var binding = fixture.Binding();
        Assert.True(await fixture.Store.CreatePendingAsync(binding));
        await PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database,
            "UPDATE project_members SET \"Role\" = 'Viewer' WHERE \"ProjectId\" = @project AND \"UserId\" = @user",
            ("project", fixture.Seed.ProjectId), ("user", fixture.Seed.UserId));
        Assert.Equal(SecurityTerminalizationResult.Unavailable, await fixture.Store.TerminalizeAsync(binding, Decision()));
        await using var currentContext = fixture.NewContext();
        Assert.Equal(SecurityEvaluationStatus.Pending,
            (await fixture.CreateStore(currentContext).FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId))!.Status);
    });

    [PostgreSqlFact]
    public Task BindingAndRuleHistoryAreImmutableEvenThroughDirectSql() => WithFixtureAsync(async fixture =>
    {
        var binding = fixture.Binding();
        Assert.True(await fixture.Store.CreatePendingAsync(binding));
        foreach (var assignment in new[]
                 { "\"BindingDigest\" = repeat('a',64)", "\"InputDigest\" = repeat('b',64)",
                     "\"IdentityJson\" = '{}'::jsonb", "\"BranchId\" = gen_random_uuid()", "\"EnforcementMode\" = 'Disabled'" })
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database,
                "UPDATE security_evaluation_runs SET \"Status\"='Failed', \"ReasonCode\"='EvaluationFailed', \"TerminalAtUtc\"=now(), " +
                assignment + " WHERE \"Id\"=@id", ("id", binding.Request.EvaluationId)));
            Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
        }
        Assert.Equal(SecurityTerminalizationResult.Terminalized, await fixture.Store.TerminalizeAsync(binding, Decision()));
        foreach (var sql in new[]
                 { "UPDATE security_evaluation_runs SET \"Outcome\"='Deny', \"ReasonCode\"='PolicyViolation' WHERE \"Id\"=@id",
                     "UPDATE security_evaluation_rule_results SET \"Outcome\"='Deny', \"ReasonCode\"='PolicyViolation' WHERE \"EvaluationId\"=@id",
                     "DELETE FROM security_evaluation_rule_results WHERE \"EvaluationId\"=@id",
                     "INSERT INTO security_evaluation_rule_results (\"EvaluationId\",\"TenantId\",\"ProjectId\",\"Sequence\",\"RuleId\",\"Status\",\"Outcome\",\"ReasonCode\",\"SchemaVersion\") SELECT \"EvaluationId\",\"TenantId\",\"ProjectId\",2,'fixture.late',\"Status\",\"Outcome\",\"ReasonCode\",\"SchemaVersion\" FROM security_evaluation_rule_results WHERE \"EvaluationId\"=@id LIMIT 1" })
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database, sql,
                ("id", binding.Request.EvaluationId)));
            Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
        }
        Assert.Equal(SecurityDecisionOutcome.Allow, (await fixture.Store.FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId))!.Outcome);
    });

    [PostgreSqlFact]
    public Task FailedTerminalTransactionRollsBackRulesAndNeverClaimsDurability() => WithFixtureAsync(async fixture =>
    {
        var binding = fixture.Binding();
        Assert.True(await fixture.Store.CreatePendingAsync(binding));
        await PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database, """
            CREATE FUNCTION security_test_fail_terminal() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Synthetic terminal persistence failure'; END; $$;
            CREATE TRIGGER security_test_fail_terminal_trigger BEFORE UPDATE ON security_evaluation_runs
                FOR EACH ROW EXECUTE FUNCTION security_test_fail_terminal();
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Store.TerminalizeAsync(binding, Decision()));
        var read = Assert.IsType<SecurityEvaluationRecord>(await fixture.Store.FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId));
        Assert.Equal(SecurityEvaluationStatus.Pending, read.Status);
        Assert.Empty(read.Rules);
        Assert.Equal(0, await fixture.Context.SecurityEvaluationRuleResults.AsNoTracking().CountAsync());
        Assert.False(fixture.Context.ChangeTracker.HasChanges());
    });

    [PostgreSqlFact]
    public Task ShadowSeamReportsTerminalWriteFailureAndRetainsOnlyPending() => WithFixtureAsync(async fixture =>
    {
        await PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database, """
            CREATE FUNCTION security_test_fail_shadow_terminal() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Synthetic terminal persistence failure'; END; $$;
            CREATE TRIGGER security_test_fail_shadow_terminal_trigger BEFORE UPDATE ON security_evaluation_runs
                FOR EACH ROW EXECUTE FUNCTION security_test_fail_shadow_terminal();
            """);
        var binding = fixture.Binding();
        var coordinator = new SecurityEvaluationCoordinator(
            [new RevisionBindingRule(), new PolicyBindingRule(), new CompilerProvenanceRule()]);
        var result = await new RevisionSecurityGate(coordinator, fixture.Store, SecurityEnforcementMode.Shadow)
            .AnalyzeAsync(binding.Request, binding.Evidence);
        Assert.Equal(SecurityDecisionOutcome.Allow, result.Summary.Outcome);
        Assert.Equal(SecurityRecordingStatus.Failed, result.RecordingStatus);
        Assert.False(result.Summary.IsAuthoritative);
        var saved = Assert.IsType<SecurityEvaluationRecord>(
            await fixture.Store.FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId));
        Assert.Equal(SecurityEvaluationStatus.Pending, saved.Status);
        Assert.Null(saved.Outcome);
        Assert.Empty(saved.Rules);
        Assert.False(fixture.Context.ChangeTracker.HasChanges());
    });

    [PostgreSqlFact]
    public Task UnrelatedPendingChangesAndExternalTransactionsAreNotFlushedOrAdopted() => WithFixtureAsync(async fixture =>
    {
        var project = await fixture.Context.Projects.SingleAsync(project => project.Id == fixture.Seed.ProjectId);
        var original = project.Name;
        project.Name = "Unrelated pending Source change";
        var binding = fixture.Binding();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.CreatePendingAsync(binding));
        Assert.Equal("Unrelated pending Source change", project.Name);
        Assert.Equal(original, await PostgreSqlMigrationTestDatabase.ScalarAsync<string>(fixture.Database,
            "SELECT \"Name\" FROM projects WHERE \"Id\"=@id", ("id", project.Id)));
        fixture.Context.Entry(project).State = EntityState.Detached;
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.CreatePendingAsync(binding));
        Assert.Equal(transaction, fixture.Context.Database.CurrentTransaction);
    });

    [PostgreSqlFact]
    public Task SourcePolicyAndExceptionCanariesAreAbsentFromDurableMetadata() => WithFixtureAsync(async fixture =>
    {
        var binding = fixture.Binding();
        Assert.True(await fixture.Store.CreatePendingAsync(binding));
        var failed = await new SecurityEvaluationCoordinator([new ThrowingRule()]).EvaluateAsync(binding);
        Assert.Equal(SecurityTerminalizationResult.Terminalized, await fixture.Store.TerminalizeAsync(binding, failed));
        var stored = await fixture.StoredIdentityAsync(binding);
        Assert.DoesNotContain("canary-private-source", stored);
        Assert.DoesNotContain("canary-private-policy", stored);
        Assert.DoesNotContain("canary-private-exception", stored);
        Assert.DoesNotContain("PasswordHash", stored);
        Assert.DoesNotContain("payload", stored, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(binding.Request.Source!.Digest.Value, stored);
        Assert.Contains(binding.Evidence.Policy!.Snapshot.ContentDigest.Value, stored);
        var read = Assert.IsType<SecurityEvaluationRecord>(await fixture.Store.FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId));
        Assert.Equal(SecurityEvaluationStatus.Failed, read.Status);
        Assert.Null(read.Outcome);
        Assert.Equal(SecurityReasonCode.EvaluationFailed, read.ReasonCode);
        var rule = Assert.Single(read.Rules);
        Assert.Equal(SecurityReasonCode.RuleExecutionFailed, rule.ReasonCode);
        Assert.DoesNotContain("canary-private-exception", System.Text.Json.JsonSerializer.Serialize(read));
    });

    [PostgreSqlFact]
    public Task FutureRetentionCanDeleteParentAndCascadeWithoutAnOrdinaryDeletionApi() => WithFixtureAsync(async fixture =>
    {
        var binding = fixture.Binding();
        Assert.True(await fixture.Store.CreatePendingAsync(binding));
        await fixture.Store.TerminalizeAsync(binding, Decision());
        await PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database,
            "DELETE FROM security_evaluation_runs WHERE \"Id\"=@id", ("id", binding.Request.EvaluationId));
        Assert.Null(await fixture.Store.FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId));
        Assert.Equal(0, await fixture.Context.SecurityEvaluationRuleResults.AsNoTracking().CountAsync());
    });

    [PostgreSqlFact]
    public Task ClosedMetadataSchemaRejectsUnrestrictedEvidenceFields() => WithFixtureAsync(async fixture =>
    {
        var binding = fixture.Binding();
        Assert.True(await fixture.Store.CreatePendingAsync(binding));
        var failure = await Assert.ThrowsAsync<PostgresException>(() => PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database, """
            INSERT INTO security_evaluation_runs
                ("Id","TenantId","ProjectId","ContextKind","BranchId","RevisionId","CandidateRevisionId",
                 "InputDigest","BindingDigest","SchemaVersion","IdentityJson","EnforcementMode","Status","ReasonCode","CreatedAtUtc")
            SELECT gen_random_uuid(),"TenantId","ProjectId","ContextKind","BranchId","RevisionId","CandidateRevisionId",
                "InputDigest","BindingDigest","SchemaVersion","IdentityJson" || '{"RawSource":"canary-forbidden-evidence"}'::jsonb,
                "EnforcementMode","Status","ReasonCode","CreatedAtUtc"
            FROM security_evaluation_runs WHERE "Id"=@id
            """, ("id", binding.Request.EvaluationId)));
        Assert.Equal(PostgresErrorCodes.RaiseException, failure.SqlState);
        Assert.Equal(1, await fixture.Context.SecurityEvaluationRuns.AsNoTracking().CountAsync());
    });

    [PostgreSqlFact]
    public Task FailedPendingInsertDoesNotLeaveAClaimedDurableEvaluation() => WithFixtureAsync(async fixture =>
    {
        await PostgreSqlMigrationTestDatabase.ExecuteAsync(fixture.Database, """
            CREATE FUNCTION security_test_fail_insert() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Synthetic Pending persistence failure'; END; $$;
            CREATE TRIGGER security_test_fail_insert_trigger BEFORE INSERT ON security_evaluation_runs
                FOR EACH ROW EXECUTE FUNCTION security_test_fail_insert();
            """);
        var binding = fixture.Binding();
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Store.CreatePendingAsync(binding));
        Assert.Null(await fixture.Store.FindAsync(fixture.Seed.ProjectId, binding.Request.EvaluationId));
        Assert.False(fixture.Context.ChangeTracker.HasChanges());
    });

    [PostgreSqlFact]
    public Task CompositeProjectTenantForeignKeyRejectsMisScopedDirectWrites() => WithFixtureAsync(async fixture =>
    {
        var context = SourceRevisionContext.Committed(new(new(new(fixture.Seed.TenantId), new(fixture.Seed.ForeignProjectId), BranchId.New()), RevisionId.New()));
        var binding = fixture.Binding(context);
        Assert.False(await fixture.Store.CreatePendingAsync(binding));
        await using var platform = PostgreSqlMigrationTestDatabase.CreatePlatformContext(fixture.Database);
        platform.SecurityEvaluationRuns.Add(SecurityEvaluationRun.CreatePending(binding, DateTimeOffset.UtcNow));
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => platform.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
    });

    [PostgreSqlFact]
    public Task MismatchedBindingCannotTerminalizeAndEnforceCannotCreateARecord() => WithFixtureAsync(async fixture =>
    {
        var binding = fixture.Binding();
        Assert.True(await fixture.Store.CreatePendingAsync(binding));
        var request = binding.Request;
        var changed = SecurityBinding.Create(new(request.EvaluationId, request.Subject, request.Operation, request.Resource,
            request.EnforcementMode, request.Source, request.Policy, new("fixture/2", "different-build")), binding.Evidence);
        Assert.Equal(SecurityTerminalizationResult.Unavailable, await fixture.Store.TerminalizeAsync(changed, Decision()));
        var enforce = SecurityBinding.Create(new(Guid.NewGuid(), request.Subject, request.Operation, request.Resource,
            SecurityEnforcementMode.Enforce, request.Source, request.Policy, request.Compiler), binding.Evidence);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.CreatePendingAsync(enforce));
        Assert.Equal(SecurityEvaluationStatus.Pending, (await fixture.Store.FindAsync(fixture.Seed.ProjectId, request.EvaluationId))!.Status);
    });

    private static SecurityDecision Decision(SecurityDecisionOutcome outcome = SecurityDecisionOutcome.Allow)
    {
        var reason = outcome switch
        {
            SecurityDecisionOutcome.Allow => SecurityReasonCode.BindingsVerified,
            SecurityDecisionOutcome.Deny => SecurityReasonCode.PolicyViolation,
            SecurityDecisionOutcome.Unknown => SecurityReasonCode.MissingEvidence,
            _ => SecurityReasonCode.BindingMismatch
        };
        return new(SecurityEvaluationStatus.Completed, outcome, reason,
            [new("fixture.z", SecurityEvaluationStatus.Completed, outcome, reason), new("fixture.a", SecurityEvaluationStatus.Completed, outcome, reason)]);
    }

    private static Task WithFixtureAsync(Func<Fixture, Task> scenario) => PostgreSqlMigrationTestDatabase.WithMigratedTemporaryDatabaseAsync(
        PostgreSqlTestEnvironment.RequireConnectionString(), async database =>
        {
            var seed = await SeedAsync(database);
            await using var fixture = new Fixture(database, seed);
            await scenario(fixture);
        });

    private static async Task<Seed> SeedAsync(string database)
    {
        await using var context = PostgreSqlMigrationTestDatabase.CreatePlatformContext(database);
        var tenant = new Tenant { Name = "Security", Slug = "security" };
        var foreign = new Tenant { Name = "Foreign Security", Slug = "foreign-security" };
        var user = new User { Email = "owner@example.test", NormalizedEmail = "OWNER@EXAMPLE.TEST", DisplayName = "Owner", PasswordHash = "test-hash" };
        var reader = new User { Email = "reader@example.test", NormalizedEmail = "READER@EXAMPLE.TEST", DisplayName = "Reader", PasswordHash = "test-hash" };
        context.Tenants.AddRange(tenant, foreign);
        context.Users.AddRange(user, reader);
        await context.SaveChangesAsync();
        var workspace = new Workspace { TenantId = tenant.Id, Name = "Security", Slug = "security", CreatedByUserId = user.Id };
        var foreignWorkspace = new Workspace { TenantId = foreign.Id, Name = "Foreign", Slug = "foreign", CreatedByUserId = user.Id };
        context.Workspaces.AddRange(workspace, foreignWorkspace);
        foreach (var scope in new[] { tenant.Id, foreign.Id })
            context.TenantUsers.Add(new TenantUser { TenantId = scope, UserId = user.Id, Status = TenantUserStatus.Active });
        context.TenantUsers.Add(new TenantUser { TenantId = tenant.Id, UserId = reader.Id, Status = TenantUserStatus.Active });
        foreach (var item in new[] { workspace, foreignWorkspace })
            context.WorkspaceMembers.Add(new WorkspaceMember { TenantId = item.TenantId, WorkspaceId = item.Id, UserId = user.Id,
                Status = MembershipStatus.Active, Role = WorkspaceRole.Member });
        var project = Project(workspace, user, "first");
        var other = Project(workspace, user, "second");
        var foreignProject = Project(foreignWorkspace, user, "foreign");
        context.Projects.AddRange(project, other, foreignProject);
        foreach (var item in new[] { project, other, foreignProject })
            context.ProjectMembers.Add(new ProjectMember { TenantId = item.TenantId, ProjectId = item.Id, UserId = user.Id, Role = ProjectRole.Owner });
        await context.SaveChangesAsync();
        return new(tenant.Id, foreign.Id, project.Id, other.Id, foreignProject.Id, workspace.Id, user.Id, reader.Id);
    }

    private static Project Project(Workspace workspace, User user, string name) => new()
    {
        TenantId = workspace.TenantId, WorkspaceId = workspace.Id, Name = name, Slug = name,
        OwnerUserId = user.Id, CreatedByUserId = user.Id, Status = ProjectStatus.Active, Visibility = ProjectVisibility.MembersOnly,
        ActivationState = ProjectActivationState.Activated, ActivatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1), ActivationVersion = 1
    };

    private sealed record Seed(Guid TenantId, Guid ForeignTenantId, Guid ProjectId, Guid OtherProjectId,
        Guid ForeignProjectId, Guid WorkspaceId, Guid UserId, Guid ReaderId);

    private sealed class Fixture : IAsyncDisposable
    {
        public string Database { get; }
        public Seed Seed { get; }
        public CurrentTenantService Tenant { get; } = new();
        public Actor Actor { get; }
        public AppDbContext Context { get; }
        public ISecurityEvaluationStore Store { get; }

        public Fixture(string database, Seed seed)
        {
            Database = database; Seed = seed; Actor = new(seed.UserId);
            Tenant.SetTenant(seed.TenantId, "security");
            Context = NewContext(); Store = CreateStore(Context);
        }

        public AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Database).Options, Tenant);

        public ISecurityEvaluationStore CreateStore(AppDbContext context)
        {
            var workspaces = new WorkspaceRepository(context);
            var groups = new GroupRepository(context);
            var workspaceAuthorization = new WorkspaceAuthorizationService(new UserRepository(context), workspaces,
                new TenantAuthorizationService(new TenantRepository(context)));
            var projectAuthorization = new ProjectAuthorizationService(new ProjectRepository(context), workspaceAuthorization,
                new GroupAuthorizationService(groups, workspaces, workspaceAuthorization), groups);
            return new SecurityEvaluationStore(context, Tenant, Actor, projectAuthorization, new Clock());
        }

        public SecurityBinding Binding(SourceRevisionContext? context = null)
        {
            context ??= SourceRevisionContext.Committed(new(new(new(Seed.TenantId), new(Seed.ProjectId), BranchId.New()), RevisionId.New()));
            var document = SourceDocument.Create(DocumentId.New(), "project", SourceJson.Parse("""
                {"entityId":"00000000-0000-4000-8000-000000000006","kind":"coglatas.project","schemaVersion":1,"payload":{"private":"canary-private-source"}}
                """));
            var source = ProjectSource.Create(context, [document]);
            var policy = new SecurityPolicyEvidence("fixture-policy", "v1", SourceJson.Parse("""{"private":"canary-private-policy"}"""));
            var compiler = new SecurityCompilerProvenance("fixture/1", "disposable-test-host");
            return SecurityBinding.Create(new(Guid.NewGuid(), new(new(Seed.TenantId), Seed.UserId), new("projectide.analyze"),
                new(context, source.Digest), SecurityEnforcementMode.Shadow, source, policy.Snapshot, compiler), new(source, policy, compiler));
        }

        public Task<string> StoredIdentityAsync(SecurityBinding binding) => PostgreSqlMigrationTestDatabase.ScalarAsync<string>(Database,
            "SELECT \"IdentityJson\"::text FROM security_evaluation_runs WHERE \"Id\"=@id", ("id", binding.Request.EvaluationId));
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class Actor(Guid userId) : ICurrentUser
    {
        public Guid? UserId { get; private set; } = userId;
        public Guid? SessionId => null;
        public string? Email => null;
        public SystemRole? SystemRole => Domain.Enums.SystemRole.User;
        public bool IsAuthenticated => UserId.HasValue;
        public void Set(Guid? value) => UserId = value;
    }

    private sealed class ThrowingRule : ISecurityRuleEvaluator
    {
        public string RuleId => "fixture.failure";
        public ValueTask<SecurityRuleResult> EvaluateAsync(SecurityBinding binding, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("canary-private-exception");
    }

    private sealed class CurrentContextProvider(SecurityBinding? current, Func<Task>? beforeReturn = null) : ISecurityCurrentContextProvider
    {
        public int Calls { get; private set; }
        public async ValueTask<SecurityBinding?> ResolveAsync(Guid projectId, SecuritySourceIdentitySnapshot historicalResource,
            CancellationToken cancellationToken)
        {
            Calls++;
            if (beforeReturn is not null)
                await beforeReturn();
            return current;
        }
    }

    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
}
