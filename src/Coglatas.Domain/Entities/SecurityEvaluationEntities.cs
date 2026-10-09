using Coglatas.Domain.Common;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Domain.Entities;

/// <summary>A narrow Security record. Re-evaluation creates another identity; retention belongs to #1034.</summary>
public sealed class SecurityEvaluationRun : Entity, ITenantEntity
{
    private SecurityEvaluationRun() { }

    public Guid TenantId { get; private set; }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set { if (value != TenantId) throw new InvalidOperationException("Security evaluation tenant identity is immutable."); }
    }
    public Guid ProjectId { get; private set; }
    public string ContextKind { get; private set; } = string.Empty;
    public Guid BranchId { get; private set; }
    public Guid? RevisionId { get; private set; }
    public Guid? CandidateRevisionId { get; private set; }
    public string InputDigest { get; private set; } = string.Empty;
    public string BindingDigest { get; private set; } = string.Empty;
    public int SchemaVersion { get; private set; }
    public string IdentityJson { get; private set; } = string.Empty;
    public SecurityEnforcementMode EnforcementMode { get; private set; }
    public SecurityEvaluationStatus Status { get; private set; }
    public SecurityDecisionOutcome? Outcome { get; private set; }
    public SecurityReasonCode ReasonCode { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? TerminalAtUtc { get; private set; }

    public static SecurityEvaluationRun CreatePending(SecurityBinding binding, DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(binding);
        SecurityEnforcementBoundary.ValidateRuntimeMode(binding.Request.EnforcementMode);
        var identity = SecurityEvaluationIdentitySnapshot.Capture(binding);
        return new()
        {
            Id = binding.Request.EvaluationId, TenantId = identity.Resource.TenantId,
            ProjectId = identity.Resource.ProjectId, ContextKind = identity.Resource.ContextKind,
            BranchId = identity.Resource.BranchId, RevisionId = identity.Resource.RevisionId,
            CandidateRevisionId = identity.Resource.CandidateRevisionId,
            InputDigest = identity.Resource.InputDigest, BindingDigest = binding.Digest.Value,
            SchemaVersion = SecurityBinding.SchemaVersion,
            IdentityJson = SourceJson.FromObject(identity).CanonicalText,
            EnforcementMode = binding.Request.EnforcementMode,
            Status = SecurityEvaluationStatus.Pending, ReasonCode = SecurityReasonCode.NotExecuted,
            CreatedAtUtc = createdAtUtc.ToUniversalTime()
        };
    }

    public void Terminalize(SecurityDecision decision, DateTimeOffset terminalAtUtc)
    {
        if (Status != SecurityEvaluationStatus.Pending)
            throw new InvalidOperationException("Security evaluation is already terminal.");
        ValidateTerminalDecision(decision);
        if (terminalAtUtc < CreatedAtUtc)
            throw new ArgumentException("Terminal time cannot precede creation.", nameof(terminalAtUtc));
        Status = decision.Status;
        Outcome = decision.Outcome;
        ReasonCode = decision.ReasonCode;
        TerminalAtUtc = terminalAtUtc.ToUniversalTime();
    }

    public static void ValidateTerminalDecision(SecurityDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.Status == SecurityEvaluationStatus.Pending)
            throw new ArgumentException("A pending decision cannot terminalize a run.", nameof(decision));
        if (decision.Status != SecurityEvaluationStatus.Completed) return;
        var outcome = decision.Rules.Any(rule => rule.Outcome == SecurityDecisionOutcome.Quarantine)
            ? SecurityDecisionOutcome.Quarantine
            : decision.Rules.Any(rule => rule.Outcome == SecurityDecisionOutcome.Deny)
                ? SecurityDecisionOutcome.Deny
                : decision.Rules.Any(rule => rule.Outcome == SecurityDecisionOutcome.Unknown)
                    ? SecurityDecisionOutcome.Unknown : SecurityDecisionOutcome.Allow;
        if (decision.Outcome != outcome)
            throw new ArgumentException("Final outcome must agree with all completed rule results.", nameof(decision));
    }
}

public sealed class SecurityEvaluationRuleRecord : ITenantEntity
{
    private SecurityEvaluationRuleRecord() { }
    public Guid EvaluationId { get; private set; }
    public Guid TenantId { get; private set; }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set { if (value != TenantId) throw new InvalidOperationException("Security rule tenant identity is immutable."); }
    }
    public Guid ProjectId { get; private set; }
    public int Sequence { get; private set; }
    public string RuleId { get; private set; } = string.Empty;
    public SecurityEvaluationStatus Status { get; private set; }
    public SecurityDecisionOutcome? Outcome { get; private set; }
    public SecurityReasonCode ReasonCode { get; private set; }
    public int SchemaVersion { get; private set; }

    public static SecurityEvaluationRuleRecord Create(SecurityEvaluationRun run, int sequence, SecurityRuleResult rule)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(rule);
        if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        if (run.Status != SecurityEvaluationStatus.Pending || rule.Status == SecurityEvaluationStatus.Pending)
            throw new ArgumentException("Rule records require a Pending run and a terminal rule execution state.");
        return new()
        {
            EvaluationId = run.Id, TenantId = run.TenantId, ProjectId = run.ProjectId,
            Sequence = sequence, RuleId = rule.RuleId, Status = rule.Status, Outcome = rule.Outcome,
            ReasonCode = rule.ReasonCode, SchemaVersion = SecurityBinding.SchemaVersion
        };
    }
}
