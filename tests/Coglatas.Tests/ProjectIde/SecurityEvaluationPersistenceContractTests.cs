using System.Text.Json;
using Coglatas.Domain.Common;
using Coglatas.Domain.Entities;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class SecurityEvaluationPersistenceContractTests
{
    [Theory]
    [InlineData(SecurityEvaluationStatus.Completed, SecurityReasonCode.BindingsVerified)]
    [InlineData(SecurityEvaluationStatus.Failed, SecurityReasonCode.EvaluationFailed)]
    [InlineData(SecurityEvaluationStatus.Cancelled, SecurityReasonCode.EvaluationCancelled)]
    [InlineData(SecurityEvaluationStatus.TimedOut, SecurityReasonCode.EvaluationTimedOut)]
    [InlineData(SecurityEvaluationStatus.NotExecuted, SecurityReasonCode.NotExecuted)]
    public void TerminalExecutionStatesPreserveBindingAndNeverFabricateFailureOutcomes(SecurityEvaluationStatus status, SecurityReasonCode reason)
    {
        var binding = Binding();
        var created = new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
        var run = SecurityEvaluationRun.CreatePending(binding, created);
        var identity = run.IdentityJson;
        SecurityDecisionOutcome? outcome = status == SecurityEvaluationStatus.Completed ? SecurityDecisionOutcome.Allow : null;
        SecurityRuleResult[] rules = outcome.HasValue ? [new("fixture.rule", status, outcome, reason)] : [];
        var decision = new SecurityDecision(status, outcome, reason, rules);
        run.Terminalize(decision, created.AddSeconds(1));
        Assert.Equal(status, run.Status);
        Assert.Equal(outcome, run.Outcome);
        Assert.Equal(reason, run.ReasonCode);
        Assert.Equal(binding.Digest.Value, run.BindingDigest);
        Assert.Equal(identity, run.IdentityJson);
        Assert.Equal(created, run.CreatedAtUtc);
        Assert.Equal(created.AddSeconds(1), run.TerminalAtUtc);
        Assert.Throws<InvalidOperationException>(() => run.Terminalize(decision, created.AddSeconds(2)));
    }

    [Fact]
    public void ContradictoryAggregateAndEarlierTimeCannotPartiallyTerminalize()
    {
        var run = SecurityEvaluationRun.CreatePending(Binding(), DateTimeOffset.UtcNow);
        var contradictory = new SecurityDecision(SecurityEvaluationStatus.Completed, SecurityDecisionOutcome.Allow,
            SecurityReasonCode.BindingsVerified,
            [new("fixture.denied", SecurityEvaluationStatus.Completed, SecurityDecisionOutcome.Deny, SecurityReasonCode.PolicyViolation)]);
        Assert.Throws<ArgumentException>(() => run.Terminalize(contradictory, run.CreatedAtUtc.AddSeconds(1)));
        Assert.Throws<ArgumentException>(() => run.Terminalize(
            new(SecurityEvaluationStatus.NotExecuted, null, SecurityReasonCode.NotExecuted, []), run.CreatedAtUtc.AddSeconds(-1)));
        Assert.Equal(SecurityEvaluationStatus.Pending, run.Status);
        Assert.Null(run.Outcome);
        Assert.Null(run.TerminalAtUtc);
    }

    [Fact]
    public void DurableIdentityContainsOnlySafeMetadataAndPreservesAbsentEvidence()
    {
        var binding = Binding();
        var run = SecurityEvaluationRun.CreatePending(binding, DateTimeOffset.UtcNow);
        var identity = JsonSerializer.Deserialize<SecurityEvaluationIdentitySnapshot>(run.IdentityJson)!;
        Assert.Equal(SecurityEvaluationIdentitySnapshot.Capture(binding), identity);
        Assert.Null(identity.ClaimedSource);
        Assert.Null(identity.ExpectedSource);
        Assert.Null(identity.ClaimedPolicy);
        Assert.Null(identity.ExpectedPolicy);
        Assert.Null(identity.ClaimedCompiler);
        Assert.Null(identity.ExpectedCompiler);
        Assert.Equal(binding.Request.Subject.UserId, identity.SubjectUserId);
        Assert.Equal(binding.Request.Operation.OperationId, identity.OperationId);
        Assert.Equal(binding.Request.Resource.Context.Branch.BranchId.Value, run.BranchId);
        Assert.Equal(binding.Request.Resource.Context.CommittedRevision!.RevisionId.Value, run.RevisionId);
        Assert.Null(run.CandidateRevisionId);
        Assert.Equal(binding.Request.Resource.InputDigest.Value, run.InputDigest);
        Assert.Equal(SecurityBinding.SchemaVersion, run.SchemaVersion);
        Assert.Equal(binding.Request.Resource.Context.Kind, run.ContextKind);
        Assert.Equal(run.TenantId, ((ITenantEntity)run).TenantId);
        Assert.Throws<InvalidOperationException>(() => ((ITenantEntity)run).TenantId = Guid.NewGuid());
    }

    [Fact]
    public void PendingRulesAndInvalidSequenceCannotBecomeDurableResults()
    {
        var run = SecurityEvaluationRun.CreatePending(Binding(), DateTimeOffset.UtcNow);
        var rule = new SecurityRuleResult("fixture.rule", SecurityEvaluationStatus.NotExecuted, null, SecurityReasonCode.RuleNotExecuted);
        Assert.Throws<ArgumentOutOfRangeException>(() => SecurityEvaluationRuleRecord.Create(run, -1, rule));
        Assert.Throws<ArgumentException>(() => SecurityEvaluationRuleRecord.Create(run, 0,
            new("fixture.pending", SecurityEvaluationStatus.Pending, null, SecurityReasonCode.NotExecuted)));
        var row = SecurityEvaluationRuleRecord.Create(run, 0, rule);
        Assert.Equal(run.Id, row.EvaluationId);
        Assert.Equal(run.ProjectId, row.ProjectId);
        Assert.Equal(run.TenantId, row.TenantId);
        Assert.Equal(row.TenantId, ((ITenantEntity)row).TenantId);
        Assert.Equal(0, row.Sequence);
        Assert.Equal(rule.RuleId, row.RuleId);
        Assert.Equal(rule.Status, row.Status);
        Assert.Null(row.Outcome);
        Assert.Equal(rule.ReasonCode, row.ReasonCode);
        Assert.Equal(1, row.SchemaVersion);
        Assert.Throws<InvalidOperationException>(() => ((ITenantEntity)row).TenantId = Guid.NewGuid());
        run.Terminalize(new(SecurityEvaluationStatus.NotExecuted, null, SecurityReasonCode.NotExecuted, []), run.CreatedAtUtc);
        Assert.Throws<ArgumentException>(() => SecurityEvaluationRuleRecord.Create(run, 1, rule));
    }

    [Fact]
    public void EnforceCannotCreateEvenAnInMemoryDurableRun()
    {
        var binding = Binding(SecurityEnforcementMode.Enforce);
        Assert.Throws<InvalidOperationException>(() => SecurityEvaluationRun.CreatePending(binding, DateTimeOffset.UtcNow));
    }

    private static SecurityBinding Binding(SecurityEnforcementMode mode = SecurityEnforcementMode.Shadow)
    {
        var tenant = TenantId.New();
        var context = SourceRevisionContext.Committed(new(new(tenant, ProjectId.New(), BranchId.New()), RevisionId.New()));
        return SecurityBinding.Create(new(Guid.NewGuid(), new(tenant, Guid.NewGuid()), new("projectide.analyze"),
            new(context, ContentDigest.Compute("fixture.input/1", "input"u8)), mode), new());
    }
}
