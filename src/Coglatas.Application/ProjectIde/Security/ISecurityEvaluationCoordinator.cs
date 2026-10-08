using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Security;

/// <summary>
/// Analysis-only evaluation of one frozen request and independently supplied host evidence.
/// Neither this result nor its binding grants authorization or changes operational state.
/// </summary>
public interface ISecurityEvaluationCoordinator
{
    ValueTask<SecurityDecision> EvaluateAsync(SecurityBinding binding, CancellationToken cancellationToken = default);
}

/// <summary>
/// A bounded, cooperative evaluator. Implementations must not perform I/O, mutate Source,
/// write state, promote/Merge, or substitute request claims for host authority.
/// </summary>
public interface ISecurityRuleEvaluator
{
    string RuleId { get; }
    ValueTask<SecurityRuleResult> EvaluateAsync(SecurityBinding binding, CancellationToken cancellationToken);
}
