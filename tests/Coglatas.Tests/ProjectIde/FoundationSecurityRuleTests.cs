using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Security;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class FoundationSecurityRuleTests
{
    [Fact]
    public async Task KnownIndependentEvidenceVerifiesAllThreeBindingsWithoutMutation()
    {
        var binding = SecurityEvaluationTestData.Binding();
        var source = binding.Request.Source!;
        var policy = binding.Evidence.Policy!;
        var sourceBefore = source.Data.CanonicalText;
        var policyBefore = policy.Content.CanonicalText;
        var results = await Task.WhenAll(Rules().Select(rule => rule.EvaluateAsync(binding, default).AsTask()));
        Assert.All(results, rule => Assert.Equal(SecurityDecisionOutcome.Allow, rule.Outcome));
        Assert.Equal(sourceBefore, source.Data.CanonicalText);
        Assert.Equal(policyBefore, policy.Content.CanonicalText);
        Assert.Same(source, binding.Request.Source);
        Assert.All(source.GetType().GetProperties(), property => Assert.Null(property.GetSetMethod(nonPublic: true)));
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("project")]
    [InlineData("branch")]
    [InlineData("revision")]
    [InlineData("input")]
    [InlineData("subject-tenant")]
    [InlineData("host-source")]
    [InlineData("claimed-source")]
    [InlineData("context-extension")]
    public async Task RevisionContradictionsAreQuarantine(string changed)
    {
        var request = SecurityEvaluationTestData.Request();
        var original = request.Resource.Context.CommittedRevision!;
        var branch = original.Branch;
        var otherBranch = new BranchRef(changed == "tenant" ? TenantId.New() : branch.TenantId,
            changed == "project" ? ProjectId.New() : branch.ProjectId, changed == "branch" ? BranchId.New() : branch.BranchId);
        var context = SourceRevisionContext.Committed(new(otherBranch, changed == "revision" ? RevisionId.New() : original.RevisionId));
        if (changed == "context-extension")
        {
            var data = JsonNode.Parse(context.Data.CanonicalText)!;
            data["unrecognized"] = "preserved-and-bound";
            context = SourceRevisionContext.Parse(SourceJson.Parse(data.ToJsonString()));
        }
        var otherSource = ProjectSource.Create(request.Source!.Context, request.Source.Documents,
            extensions: SourceJson.Parse("{\"changed\":true}"));
        var altered = new SecurityEvaluationRequest(request.EvaluationId,
            changed == "subject-tenant" ? new(TenantId.New(), request.Subject.UserId) : request.Subject,
            request.Operation, new(context, changed == "input" ? otherSource.Digest : request.Resource.InputDigest),
            request.EnforcementMode, changed == "claimed-source" ? otherSource : request.Source, request.Policy, request.Compiler);
        var binding = SecurityEvaluationTestData.Binding(altered, new(
            changed == "host-source" ? otherSource : request.Source, SecurityEvaluationTestData.Policy(), request.Compiler));
        var result = await new RevisionBindingRule().EvaluateAsync(binding, default);
        Assert.Equal(SecurityDecisionOutcome.Quarantine, result.Outcome);
        Assert.Equal(SecurityReasonCode.RevisionBindingMismatch, result.ReasonCode);
    }

    [Theory]
    [InlineData("candidate")]
    [InlineData("proposal")]
    [InlineData("base")]
    [InlineData("head")]
    public async Task CandidateFreshnessIncludesAllCapturedRevisionIdentities(string changed)
    {
        var source = SecurityEvaluationTestData.Source();
        var revision = source.Context.CommittedRevision!;
        var proposal = new ProposalContext(ProposalId.New(), CandidateRevisionId.New(), revision, revision);
        var candidateSource = ProjectSource.Create(SourceRevisionContext.Candidate(proposal), source.Documents);
        var foreign = new ProposalContext(changed == "proposal" ? ProposalId.New() : proposal.ProposalId,
            changed == "candidate" ? CandidateRevisionId.New() : proposal.CandidateRevisionId,
            changed == "base" ? new(revision.Branch, RevisionId.New()) : revision,
            changed == "head" ? new(revision.Branch, RevisionId.New()) : revision);
        var request = new SecurityEvaluationRequest(Guid.NewGuid(), new(source.TenantId, Guid.NewGuid()), new("projectide.analyze"),
            new(candidateSource.Context, candidateSource.Digest), SecurityEnforcementMode.Shadow, candidateSource);
        var rule = new RevisionBindingRule();
        Assert.Equal(SecurityDecisionOutcome.Allow, (await rule.EvaluateAsync(
            SecurityEvaluationTestData.Binding(request, new(candidateSource)), default)).Outcome);
        Assert.Equal(SecurityDecisionOutcome.Quarantine, (await rule.EvaluateAsync(
            SecurityEvaluationTestData.Binding(request, new(ProjectSource.Create(SourceRevisionContext.Candidate(foreign), source.Documents))), default)).Outcome);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingSourceIsUnknownAndDoesNotReplaceIndependentEvidence(bool missingClaim)
    {
        var original = SecurityEvaluationTestData.Request();
        var request = new SecurityEvaluationRequest(original.EvaluationId, original.Subject, original.Operation, original.Resource,
            original.EnforcementMode, missingClaim ? null : original.Source, original.Policy, original.Compiler);
        var result = await new RevisionBindingRule().EvaluateAsync(SecurityEvaluationTestData.Binding(request,
            new(missingClaim ? original.Source : null, SecurityEvaluationTestData.Policy(), original.Compiler)), default);
        Assert.Equal(SecurityDecisionOutcome.Unknown, result.Outcome);
        Assert.Equal(SecurityReasonCode.RevisionEvidenceMissing, result.ReasonCode);
    }

    [Fact]
    public async Task ProvenRevisionContradictionDominatesMissingSourceEvidence()
    {
        var original = SecurityEvaluationTestData.Request();
        var request = new SecurityEvaluationRequest(original.EvaluationId, new(TenantId.New(), original.Subject.UserId),
            original.Operation, original.Resource, original.EnforcementMode);
        Assert.Equal(SecurityDecisionOutcome.Quarantine, (await new RevisionBindingRule().EvaluateAsync(
            SecurityEvaluationTestData.Binding(request, new()), default)).Outcome);
    }

    [Fact]
    public async Task DigestObjectExtensionsAreNotNewDigestIdentity()
    {
        var original = SecurityEvaluationTestData.Request();
        var data = JsonNode.Parse(original.Resource.InputDigest.Data.CanonicalText)!;
        data["unrecognized"] = "metadata";
        var request = new SecurityEvaluationRequest(original.EvaluationId, original.Subject, original.Operation,
            new(original.Resource.Context, ContentDigest.Parse(SourceJson.Parse(data.ToJsonString()))),
            original.EnforcementMode, original.Source, original.Policy, original.Compiler);
        Assert.Equal(SecurityDecisionOutcome.Allow, (await new RevisionBindingRule().EvaluateAsync(
            SecurityEvaluationTestData.Binding(request), default)).Outcome);
    }

    [Theory]
    [InlineData("set")]
    [InlineData("version")]
    [InlineData("schema")]
    [InlineData("content")]
    public async Task KnownPolicyContradictionsAreQuarantineIncludingSameVersionDifferentContent(string changed)
    {
        var expected = SecurityEvaluationTestData.Policy();
        var policy = new SecurityPolicySnapshot(changed == "set" ? "other-set" : expected.Snapshot.PolicySetId,
            changed == "version" ? "other-version" : expected.Snapshot.Version,
            changed == "content" ? SecurityEvaluationTestData.Policy("{\"rules\":{\"foundation\":false}}").Snapshot.ContentDigest : expected.Snapshot.ContentDigest,
            changed == "schema" ? 2 : expected.Snapshot.SchemaVersion);
        var request = WithPolicy(policy);
        var result = await new PolicyBindingRule().EvaluateAsync(SecurityEvaluationTestData.Binding(request), default);
        Assert.Equal(SecurityDecisionOutcome.Quarantine, result.Outcome);
        Assert.Equal(SecurityReasonCode.PolicyBindingMismatch, result.ReasonCode);
    }

    [Theory]
    [InlineData("claim")]
    [InlineData("host")]
    [InlineData("unsupported")]
    public async Task MissingOrUnsupportedPolicyIsUnknownNeverDeny(string missing)
    {
        var policy = SecurityEvaluationTestData.Policy(schema: missing == "unsupported" ? 2 : 1);
        var request = WithPolicy(missing == "claim" ? null : policy.Snapshot);
        var binding = SecurityEvaluationTestData.Binding(request, new(SecurityEvaluationTestData.Source(),
            missing == "host" ? null : policy, SecurityEvaluationTestData.Compiler()));
        var result = await new PolicyBindingRule().EvaluateAsync(binding, default);
        Assert.Equal(SecurityDecisionOutcome.Unknown, result.Outcome);
        Assert.Equal(SecurityReasonCode.PolicyEvidenceMissing, result.ReasonCode);
    }

    [Fact]
    public async Task SelfDeclaredMatchingPolicyDigestCannotSupplyPolicyAuthority()
    {
        var request = WithPolicy(SecurityEvaluationTestData.Policy().Snapshot);
        var evidence = new SecurityEvaluationEvidence(request.Source, compiler: request.Compiler);
        var binding = SecurityEvaluationTestData.Binding(request, evidence);
        var result = await new PolicyBindingRule().EvaluateAsync(binding, default);
        Assert.Equal(SecurityDecisionOutcome.Unknown, result.Outcome);
        Assert.Null(binding.Evidence.Policy);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("build")]
    [InlineData("commit")]
    public async Task CompilerContradictionsAreQuarantine(string changed)
    {
        var expected = new SecurityCompilerProvenance("test-host/1", "deterministic-fixture", new string('a', 40));
        var claimed = new SecurityCompilerProvenance(changed == "version" ? "other-host/1" : expected.Version,
            changed == "build" ? "other-build" : expected.BuildIdentity, changed == "commit" ? new string('b', 40) : expected.GitCommitSha);
        var result = await new CompilerProvenanceRule().EvaluateAsync(SecurityEvaluationTestData.Binding(WithCompiler(claimed),
            new(SecurityEvaluationTestData.Source(), SecurityEvaluationTestData.Policy(), expected)), default);
        Assert.Equal(SecurityDecisionOutcome.Quarantine, result.Outcome);
        Assert.Equal(SecurityReasonCode.CompilerProvenanceMismatch, result.ReasonCode);
    }

    [Theory]
    [InlineData("claim")]
    [InlineData("host")]
    [InlineData("claim-commit")]
    [InlineData("host-commit")]
    public async Task MissingCompilerOrUnverifiedOptionalCommitIsUnknown(string missing)
    {
        var claimed = missing == "claim" ? null : missing == "claim-commit" ?
            new SecurityCompilerProvenance("test-host/1", "deterministic-fixture", new string('a', 40)) : SecurityEvaluationTestData.Compiler();
        var expected = missing == "host" ? null : missing == "host-commit" ?
            new SecurityCompilerProvenance("test-host/1", "deterministic-fixture", new string('a', 40)) : SecurityEvaluationTestData.Compiler();
        var result = await new CompilerProvenanceRule().EvaluateAsync(SecurityEvaluationTestData.Binding(WithCompiler(claimed),
            new(SecurityEvaluationTestData.Source(), SecurityEvaluationTestData.Policy(), expected)), default);
        Assert.Equal(SecurityDecisionOutcome.Unknown, result.Outcome);
        Assert.Equal(SecurityReasonCode.CompilerEvidenceMissing, result.ReasonCode);
    }

    [Fact]
    public async Task KnownCompilerVersionAndBuildNeedNoFabricatedOptionalCommit()
    {
        var result = await new CompilerProvenanceRule().EvaluateAsync(SecurityEvaluationTestData.Binding(), default);
        Assert.Equal(SecurityDecisionOutcome.Allow, result.Outcome);
        var compiler = SecurityEvaluationTestData.Compiler();
        Assert.Null(compiler.GitCommitSha);
    }

    [Fact]
    public async Task EveryFoundationRuleHonorsCancellationBeforeInspectingEvidence()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        foreach (var rule in Rules())
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await rule.EvaluateAsync(SecurityEvaluationTestData.Binding(), cancellation.Token));
    }

    private static ISecurityRuleEvaluator[] Rules() => [new RevisionBindingRule(), new PolicyBindingRule(), new CompilerProvenanceRule()];

    private static SecurityEvaluationRequest WithPolicy(SecurityPolicySnapshot? policy)
    {
        var original = SecurityEvaluationTestData.Request();
        return new(original.EvaluationId, original.Subject, original.Operation, original.Resource, original.EnforcementMode,
            original.Source, policy, original.Compiler);
    }

    private static SecurityEvaluationRequest WithCompiler(SecurityCompilerProvenance? compiler)
    {
        var original = SecurityEvaluationTestData.Request();
        return new(original.EvaluationId, original.Subject, original.Operation, original.Resource, original.EnforcementMode,
            original.Source, original.Policy, compiler);
    }
}
