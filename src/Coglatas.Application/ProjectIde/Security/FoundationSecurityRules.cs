using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Security;

public sealed class RevisionBindingRule : ISecurityRuleEvaluator
{
    public string RuleId => "SEC-FND-REVISION-BINDING";

    public ValueTask<SecurityRuleResult> EvaluateAsync(SecurityBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        cancellationToken.ThrowIfCancellationRequested();
        var request = binding.Request;
        var resource = request.Resource;
        var sources = new[] { request.Source, binding.Evidence.Source };
        var mismatch = request.Subject.TenantId != resource.Context.Branch.TenantId || sources.Any(source =>
            source is not null && (!source.Context.Equals(resource.Context) || source.Digest.Value != resource.InputDigest.Value));
        var outcome = mismatch ? SecurityDecisionOutcome.Quarantine :
            sources.Any(source => source is null) ? SecurityDecisionOutcome.Unknown : SecurityDecisionOutcome.Allow;
        return ValueTask.FromResult(new SecurityRuleResult(RuleId, SecurityEvaluationStatus.Completed, outcome, outcome switch
        {
            SecurityDecisionOutcome.Quarantine => SecurityReasonCode.RevisionBindingMismatch,
            SecurityDecisionOutcome.Unknown => SecurityReasonCode.RevisionEvidenceMissing,
            _ => SecurityReasonCode.RevisionBindingVerified
        }));
    }
}

public sealed class PolicyBindingRule : ISecurityRuleEvaluator
{
    public string RuleId => "SEC-FND-POLICY-BINDING";

    public ValueTask<SecurityRuleResult> EvaluateAsync(SecurityBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        cancellationToken.ThrowIfCancellationRequested();
        var claimed = binding.Request.Policy;
        // This snapshot derives from actual canonical host content, never the request's declared hash.
        var expected = binding.Evidence.Policy?.Snapshot;
        var outcome = claimed is null || expected is null ? SecurityDecisionOutcome.Unknown :
            claimed.PolicySetId != expected.PolicySetId || claimed.Version != expected.Version ||
            claimed.SchemaVersion != expected.SchemaVersion || claimed.ContentDigest.Value != expected.ContentDigest.Value
                ? SecurityDecisionOutcome.Quarantine :
            expected.SchemaVersion != 1 ? SecurityDecisionOutcome.Unknown : SecurityDecisionOutcome.Allow;
        return ValueTask.FromResult(new SecurityRuleResult(RuleId, SecurityEvaluationStatus.Completed, outcome, outcome switch
        {
            SecurityDecisionOutcome.Quarantine => SecurityReasonCode.PolicyBindingMismatch,
            SecurityDecisionOutcome.Unknown => SecurityReasonCode.PolicyEvidenceMissing,
            _ => SecurityReasonCode.PolicyBindingVerified
        }));
    }
}

public sealed class CompilerProvenanceRule : ISecurityRuleEvaluator
{
    public string RuleId => "SEC-FND-COMPILER-PROVENANCE";

    public ValueTask<SecurityRuleResult> EvaluateAsync(SecurityBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        cancellationToken.ThrowIfCancellationRequested();
        var claimed = binding.Request.Compiler;
        var expected = binding.Evidence.Compiler;
        var outcome = claimed is null || expected is null ? SecurityDecisionOutcome.Unknown :
            claimed.Version != expected.Version || claimed.BuildIdentity != expected.BuildIdentity ||
            claimed.GitCommitSha is not null && expected.GitCommitSha is not null && claimed.GitCommitSha != expected.GitCommitSha
                ? SecurityDecisionOutcome.Quarantine :
            (claimed.GitCommitSha is null) != (expected.GitCommitSha is null)
                ? SecurityDecisionOutcome.Unknown : SecurityDecisionOutcome.Allow;
        return ValueTask.FromResult(new SecurityRuleResult(RuleId, SecurityEvaluationStatus.Completed, outcome, outcome switch
        {
            SecurityDecisionOutcome.Quarantine => SecurityReasonCode.CompilerProvenanceMismatch,
            SecurityDecisionOutcome.Unknown => SecurityReasonCode.CompilerEvidenceMissing,
            _ => SecurityReasonCode.CompilerProvenanceVerified
        }));
    }
}
