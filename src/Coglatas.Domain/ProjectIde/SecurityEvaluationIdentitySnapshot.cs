namespace Coglatas.Domain.ProjectIde;

/// <summary>Allowlisted durable identities only; no Source, policy contents or context extensions.</summary>
public sealed record SecurityEvaluationIdentitySnapshot(
    Guid SubjectUserId,
    string OperationId,
    SecuritySourceIdentitySnapshot Resource,
    SecuritySourceIdentitySnapshot? ClaimedSource,
    SecuritySourceIdentitySnapshot? ExpectedSource,
    SecurityPolicyIdentitySnapshot? ClaimedPolicy,
    SecurityPolicyIdentitySnapshot? ExpectedPolicy,
    SecurityCompilerProvenance? ClaimedCompiler,
    SecurityCompilerProvenance? ExpectedCompiler)
{
    public static SecurityEvaluationIdentitySnapshot Capture(SecurityBinding binding) => new(
        binding.Request.Subject.UserId,
        binding.Request.Operation.OperationId,
        SecuritySourceIdentitySnapshot.Capture(binding.Request.Resource.Context, binding.Request.Resource.InputDigest),
        CaptureSource(binding.Request.Source), CaptureSource(binding.Evidence.Source),
        CapturePolicy(binding.Request.Policy), CapturePolicy(binding.Evidence.Policy?.Snapshot),
        binding.Request.Compiler, binding.Evidence.Compiler);

    private static SecuritySourceIdentitySnapshot? CaptureSource(ProjectSource? source) =>
        source is null ? null : SecuritySourceIdentitySnapshot.Capture(source.Context, source.Digest);

    private static SecurityPolicyIdentitySnapshot? CapturePolicy(SecurityPolicySnapshot? policy) =>
        policy is null ? null : new(policy.PolicySetId, policy.Version, policy.ContentDigest.Value, policy.SchemaVersion);
}

public sealed record SecurityPolicyIdentitySnapshot(string PolicySetId, string Version, string ContentDigest, int SchemaVersion);

/// <summary>The complete canonical context is represented by its digest, including unknown extensions.</summary>
public sealed record SecuritySourceIdentitySnapshot(
    string ContextKind, string BaseContextKind, Guid TenantId, Guid ProjectId, Guid BranchId,
    Guid? RevisionId, Guid? ProposalId, Guid? CandidateRevisionId,
    Guid? BaseBranchId, Guid? BaseRevisionId, Guid? CapturedHeadBranchId, Guid? CapturedHeadRevisionId,
    Guid? ScenarioId, Guid? ScenarioRevisionId, string? OverlayDigest,
    string ContextDigest, string InputDigest)
{
    private const string ContextDigestDomain = "coglatas.security-context/1";

    public static SecuritySourceIdentitySnapshot Capture(SourceRevisionContext context, ContentDigest inputDigest)
    {
        var basis = context.Scenario?.BaseContext ?? context;
        var proposal = basis.Proposal;
        return new(context.Kind, basis.Kind, context.Branch.TenantId.Value, context.Branch.ProjectId.Value,
            context.Branch.BranchId.Value, basis.CommittedRevision?.RevisionId.Value,
            proposal?.ProposalId.Value, proposal?.CandidateRevisionId.Value,
            proposal?.BaseRevision.Branch.BranchId.Value, proposal?.BaseRevision.RevisionId.Value,
            proposal?.CapturedTargetHead.Branch.BranchId.Value, proposal?.CapturedTargetHead.RevisionId.Value,
            context.Scenario?.ScenarioId.Value, context.Scenario?.ScenarioRevisionId.Value,
            context.Scenario?.OverlayDigest.Value,
            ContentDigest.Compute(ContextDigestDomain, context.Data.ToCanonicalBytes()).Value, inputDigest.Value);
    }
}
