using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Evaluations;

public enum SecurityTerminalizationResult { Terminalized, AlreadyTerminal, Unavailable }

/// <summary>Current authorization is required for every operation. IDs and digests never grant access.</summary>
public interface ISecurityEvaluationStore
{
    Task<bool> CreatePendingAsync(SecurityBinding binding, CancellationToken cancellationToken = default);
    Task<SecurityTerminalizationResult> TerminalizeAsync(SecurityBinding binding, SecurityDecision decision,
        CancellationToken cancellationToken = default);
    Task<SecurityEvaluationRecord?> FindAsync(Guid projectId, Guid evaluationId, CancellationToken cancellationToken = default);
}

public sealed record SecurityEvaluationRecord(
    Guid EvaluationId, Guid TenantId, Guid ProjectId, string BindingDigest, int SchemaVersion,
    SecurityEvaluationIdentitySnapshot Identity, SecurityEnforcementMode EnforcementMode,
    SecurityEvaluationStatus Status, SecurityDecisionOutcome? Outcome, SecurityReasonCode ReasonCode,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? TerminalAtUtc, IReadOnlyList<SecurityRuleResult> Rules);
