using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Security;

public sealed class SecurityEvaluationCoordinator : ISecurityEvaluationCoordinator
{
    private readonly RegisteredRule[] _rules;
    private readonly TimeSpan _timeout;

    public SecurityEvaluationCoordinator(IEnumerable<ISecurityRuleEvaluator> evaluators)
        : this(evaluators, TimeSpan.FromSeconds(5)) { }

    public SecurityEvaluationCoordinator(IEnumerable<ISecurityRuleEvaluator> evaluators, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(evaluators);
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        _timeout = timeout;
        _rules = evaluators.Select(evaluator =>
        {
            ArgumentNullException.ThrowIfNull(evaluator);
            // Validate and capture the identity once, even for a mutable custom registration.
            var ruleId = new SecurityRuleResult(evaluator.RuleId, SecurityEvaluationStatus.NotExecuted,
                null, SecurityReasonCode.RuleNotExecuted).RuleId;
            return new RegisteredRule(ruleId, evaluator);
        }).OrderBy(rule => rule.RuleId, StringComparer.Ordinal).ToArray();
        if (_rules.Select(rule => rule.RuleId).Distinct(StringComparer.Ordinal).Count() != _rules.Length)
            throw new ArgumentException("Security rule identities must be unique.", nameof(evaluators));
    }

    public async ValueTask<SecurityDecision> EvaluateAsync(SecurityBinding binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        SecurityEnforcementBoundary.ValidateRuntimeMode(binding.Request.EnforcementMode);
        var results = _rules.Select(rule => new SecurityRuleResult(rule.RuleId,
            SecurityEvaluationStatus.NotExecuted, null, SecurityReasonCode.RuleNotExecuted)).ToArray();
        if (binding.Request.EnforcementMode == SecurityEnforcementMode.Disabled || results.Length == 0)
            return Terminal(SecurityEvaluationStatus.NotExecuted, results);
        if (cancellationToken.IsCancellationRequested)
            return Terminal(SecurityEvaluationStatus.Cancelled, results);

        using var timeout = new CancellationTokenSource(_timeout);
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        for (var index = 0; index < _rules.Length; index++)
        {
            var interruption = Interrupted(cancellationToken, timeout.Token);
            if (interruption is not null)
                return Terminal(interruption.Value, results);
            SecurityRuleResult result;
            try
            {
                // Await cooperative completion. No detached work can continue after this returns.
                result = await _rules[index].Evaluator.EvaluateAsync(binding, execution.Token);
                interruption = Interrupted(cancellationToken, timeout.Token);
                if (interruption is not null)
                {
                    results[index] = InterruptedRule(_rules[index].RuleId, interruption.Value);
                    return Terminal(interruption.Value, results);
                }
                if (result is null || result.RuleId != _rules[index].RuleId || result.Status == SecurityEvaluationStatus.Pending)
                    throw new InvalidOperationException("A rule returned an invalid terminal identity or execution state.");
            }
            catch (Exception)
            {
                // Exception messages and arbitrary evidence never become logs or persisted reasons.
                var status = Interrupted(cancellationToken, timeout.Token) ?? SecurityEvaluationStatus.Failed;
                results[index] = InterruptedRule(_rules[index].RuleId, status);
                return Terminal(status, results);
            }
            results[index] = result;
            if (result.Status != SecurityEvaluationStatus.Completed)
                return Terminal(result.Status, results);
        }

        var outcome = results.Select(result => result.Outcome!.Value).OrderByDescending(Severity).First();
        var reason = outcome switch
        {
            SecurityDecisionOutcome.Quarantine => SecurityReasonCode.BindingMismatch,
            SecurityDecisionOutcome.Deny => SecurityReasonCode.PolicyViolation,
            SecurityDecisionOutcome.Unknown => SecurityReasonCode.MissingEvidence,
            _ => SecurityReasonCode.BindingsVerified
        };
        return new(SecurityEvaluationStatus.Completed, outcome, reason, results);
    }

    private static int Severity(SecurityDecisionOutcome outcome) => outcome switch
    {
        SecurityDecisionOutcome.Quarantine => 3,
        SecurityDecisionOutcome.Deny => 2,
        SecurityDecisionOutcome.Unknown => 1,
        _ => 0
    };

    private static SecurityEvaluationStatus? Interrupted(CancellationToken caller, CancellationToken timeout) =>
        caller.IsCancellationRequested ? SecurityEvaluationStatus.Cancelled :
        timeout.IsCancellationRequested ? SecurityEvaluationStatus.TimedOut : null;

    private static SecurityRuleResult InterruptedRule(string ruleId, SecurityEvaluationStatus status) =>
        new(ruleId, status, null, status switch
        {
            SecurityEvaluationStatus.Cancelled => SecurityReasonCode.EvaluationCancelled,
            SecurityEvaluationStatus.TimedOut => SecurityReasonCode.EvaluationTimedOut,
            _ => SecurityReasonCode.RuleExecutionFailed
        });

    private static SecurityDecision Terminal(SecurityEvaluationStatus status, SecurityRuleResult[] results) =>
        new(status, null, status switch
        {
            SecurityEvaluationStatus.Cancelled => SecurityReasonCode.EvaluationCancelled,
            SecurityEvaluationStatus.TimedOut => SecurityReasonCode.EvaluationTimedOut,
            SecurityEvaluationStatus.Failed => SecurityReasonCode.EvaluationFailed,
            _ => SecurityReasonCode.NotExecuted
        }, results);

    private sealed record RegisteredRule(string RuleId, ISecurityRuleEvaluator Evaluator);
}
