using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class SecurityBindingTests
{
    private static readonly Guid UserId = Guid.Parse("00000000-0000-4000-8000-000000000009");

    [Fact]
    public void CanonicalBindingMatchesIndependentGoldenBytesAndDigest()
    {
        var source = FixtureSource();
        var policy = Policy();
        var compiler = Compiler();
        var request = Request(source, policy.Snapshot, compiler);
        var evidence = new SecurityEvaluationEvidence(source, policy, compiler);
        var binding = SecurityBinding.Create(request, evidence);

        Assert.Equal(Fixture("security-binding-v1.canonical.json"), binding.Data.ToCanonicalBytes());
        Assert.Equal(Encoding.UTF8.GetString(Fixture("security-binding-v1.sha256")).Trim(), binding.Digest.Value);
        Assert.Same(request, binding.Request);
        Assert.Same(evidence, binding.Evidence);
        Assert.Equal("coglatas.security-binding", SecurityBinding.SchemaId);
        Assert.Equal(1, SecurityBinding.SchemaVersion);
        Assert.Equal("utf-8", binding.Data.Value.GetProperty("canonicalEncoding").GetString());
        Assert.Equal("sha-256", binding.Digest.Data.Value.GetProperty("algorithm").GetString());
        Assert.Equal("a79895fb82a26a9789f5211177d387c63ed1c06e186147ca8202072734b8bee3", policy.Snapshot.ContentDigest.Value);
        Assert.Equal("{\"rules\":{\"a\":true,\"b\":false}}", policy.Content.CanonicalText);
        Assert.Equal(1, policy.Snapshot.SchemaVersion);
        Assert.Null(compiler.GitCommitSha);
    }

    [Fact]
    public void PropertyDictionaryAndNumericOrderingDoNotChangeBinding()
    {
        var context = FixtureSource().Context;
        var first = WithContext(context, extensions: SourceJson.Parse("{\"b\":2.0,\"a\":1e0}"));
        var second = WithContext(context, extensions: SourceJson.Parse(JsonSerializer.Serialize(
            new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 })));
        var firstPolicy = Policy("{\"rules\":{\"b\":false,\"a\":true}}");
        var secondPolicy = Policy();

        Assert.Equal(first.Digest.Value, second.Digest.Value);
        Assert.Equal(firstPolicy.Snapshot.ContentDigest.Value, secondPolicy.Snapshot.ContentDigest.Value);
        Assert.Equal(Bind(first, firstPolicy).Data.CanonicalText, Bind(second, secondPolicy).Data.CanonicalText);
        Assert.Equal(Bind(first, firstPolicy).Digest.Value, Bind(second, secondPolicy).Digest.Value);
        Assert.Equal(Bind(FixtureSource()).Digest.Value,
            Bind(FixtureSource("source-v1.canonical.json")).Digest.Value);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("project")]
    [InlineData("branch")]
    [InlineData("revision")]
    public void EveryCommittedScopeIdentityParticipates(string changedField)
    {
        var original = FixtureSource().Context.CommittedRevision!;
        var branch = original.Branch;
        var changedBranch = new BranchRef(
            changedField == "tenant" ? TenantId.New() : branch.TenantId,
            changedField == "project" ? ProjectId.New() : branch.ProjectId,
            changedField == "branch" ? BranchId.New() : branch.BranchId);
        var changed = SourceRevisionContext.Committed(new(changedBranch,
            changedField == "revision" ? RevisionId.New() : original.RevisionId));

        Assert.NotEqual(Bind(WithContext(SourceRevisionContext.Committed(original))).Digest.Value,
            Bind(WithContext(changed)).Digest.Value);
    }

    [Theory]
    [InlineData("proposal")]
    [InlineData("candidate")]
    [InlineData("base-revision")]
    [InlineData("head-revision")]
    [InlineData("base-branch")]
    [InlineData("target-branch")]
    public void CandidateBindingIncludesProposalBaseAndCapturedHead(string changedField)
    {
        var revision = FixtureSource().Context.CommittedRevision!;
        var proposalId = ProposalId.New();
        var candidateId = CandidateRevisionId.New();
        var original = new ProposalContext(proposalId, candidateId, revision, revision);
        var otherBranch = new BranchRef(revision.Branch.TenantId, revision.Branch.ProjectId, BranchId.New());
        var changed = new ProposalContext(
            changedField == "proposal" ? ProposalId.New() : proposalId,
            changedField == "candidate" ? CandidateRevisionId.New() : candidateId,
            new(changedField == "base-branch" ? otherBranch : revision.Branch,
                changedField == "base-revision" ? RevisionId.New() : revision.RevisionId),
            new(changedField == "target-branch" ? otherBranch : revision.Branch,
                changedField == "head-revision" ? RevisionId.New() : revision.RevisionId));

        Assert.NotEqual(Bind(WithContext(SourceRevisionContext.Candidate(original))).Digest.Value,
            Bind(WithContext(SourceRevisionContext.Candidate(changed))).Digest.Value);
    }

    [Theory]
    [InlineData("resource-context")]
    [InlineData("resource-digest")]
    [InlineData("supplied-source")]
    [InlineData("host-source")]
    [InlineData("claimed-policy")]
    [InlineData("host-policy")]
    [InlineData("claimed-compiler")]
    [InlineData("host-compiler")]
    [InlineData("subject-tenant")]
    [InlineData("subject-user")]
    [InlineData("operation")]
    public void ContradictoryClaimsCannotAliasTheQualifiedInput(string changedField)
    {
        var source = FixtureSource();
        var otherSource = WithContext(source.Context);
        var policy = Policy();
        var otherPolicy = Policy("{\"rules\":{\"a\":false,\"b\":false}}");
        var compiler = Compiler();
        var otherCompiler = new SecurityCompilerProvenance("test-host/1", "fixture-build-2");
        var request = Request(source, policy.Snapshot, compiler);
        var evidence = new SecurityEvaluationEvidence(source, policy, compiler);
        var otherContext = SourceRevisionContext.Committed(new(source.Context.Branch, RevisionId.New()));
        var changedRequest = changedField switch
        {
            "resource-context" => Copy(request, resource: new(otherContext, source.Digest)),
            "resource-digest" => Copy(request, resource: new(source.Context, otherSource.Digest)),
            "supplied-source" => Copy(request, source: otherSource),
            "claimed-policy" => Copy(request, policy: otherPolicy.Snapshot),
            "claimed-compiler" => Copy(request, compiler: otherCompiler),
            "subject-tenant" => Copy(request, subject: new(TenantId.New(), UserId)),
            "subject-user" => Copy(request, subject: new(source.TenantId, Guid.NewGuid())),
            "operation" => Copy(request, operation: new("projectide.other-operation")),
            _ => request
        };
        var changedEvidence = changedField switch
        {
            "host-source" => new SecurityEvaluationEvidence(otherSource, policy, compiler),
            "host-policy" => new SecurityEvaluationEvidence(source, otherPolicy, compiler),
            "host-compiler" => new SecurityEvaluationEvidence(source, policy, otherCompiler),
            _ => evidence
        };

        Assert.NotEqual(SecurityBinding.Create(request, evidence).Digest.Value,
            SecurityBinding.Create(changedRequest, changedEvidence).Digest.Value);
    }

    [Theory]
    [InlineData("content")]
    [InlineData("set")]
    [InlineData("version")]
    [InlineData("schema")]
    public void PolicyIdentityAndActualContentAreIndependentlyBound(string changedField)
    {
        var source = FixtureSource();
        var original = Policy();
        var changed = new SecurityPolicyEvidence(
            changedField == "set" ? "foundation-other" : "foundation-test",
            changedField == "version" ? "2" : "1",
            SourceJson.Parse(changedField == "content" ? "{\"rules\":{\"a\":false,\"b\":false}}" : original.Content.CanonicalText),
            changedField == "schema" ? 2 : 1);

        Assert.NotEqual(Bind(source, original).Digest.Value, Bind(source, changed).Digest.Value);
        if (changedField == "content") Assert.Equal(original.Snapshot.Version, changed.Snapshot.Version);
        else Assert.Equal(original.Snapshot.ContentDigest.Value, changed.Snapshot.ContentDigest.Value);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("build")]
    [InlineData("commit")]
    public void CompilerIdentityChangesTheBinding(string changedField)
    {
        var source = FixtureSource();
        var changed = new SecurityCompilerProvenance(changedField == "version" ? "test-host/2" : "test-host/1",
            changedField == "build" ? "fixture-build-2" : "fixture-build-1",
            changedField == "commit" ? new string('a', 40) : null);

        Assert.NotEqual(Bind(source).Digest.Value, Bind(source, compiler: changed).Digest.Value);
        Assert.Equal(changedField == "commit" ? new string('a', 40) : null, changed.GitCommitSha);
    }

    [Theory]
    [InlineData("futureSource")]
    [InlineData("context")]
    public void PreservedUnknownSourceAndContextMembersParticipate(string changedBoundary)
    {
        var original = FixtureSource();
        var changed = JsonNode.Parse(original.Data.CanonicalText)!;
        changed[changedBoundary]!["additionalBindingValue"] = "future-value";
        var decoded = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(changed.ToJsonString())).RequireSource();

        Assert.Equal("future-value", decoded.Data.Value.GetProperty(changedBoundary).GetProperty("additionalBindingValue").GetString());
        Assert.NotEqual(original.Digest.Value, decoded.Digest.Value);
        Assert.NotEqual(Bind(original).Digest.Value, Bind(decoded).Digest.Value);
    }

    [Fact]
    public void MissingCompilerOrHostEvidenceIsExplicitAndNeverFabricated()
    {
        var source = FixtureSource();
        var request = Request(source, null, null);
        var binding = SecurityBinding.Create(request, new());

        Assert.Null(binding.Request.Compiler);
        Assert.Null(binding.Evidence.Source);
        Assert.Null(binding.Evidence.Policy);
        Assert.Null(binding.Evidence.Compiler);
        Assert.Equal(JsonValueKind.Null, binding.Data.Value.GetProperty("expected").GetProperty("compiler").ValueKind);
        Assert.Equal(JsonValueKind.Null, binding.Data.Value.GetProperty("expected").GetProperty("policy").ValueKind);
        Assert.NotEqual(binding.Digest.Value, Bind(source).Digest.Value);
        Assert.Throws<ArgumentNullException>(() => SecurityBinding.Create(null!, new()));
        Assert.Throws<ArgumentNullException>(() => SecurityBinding.Create(request, null!));
    }

    [Fact]
    public void EvaluationIdentityAndModeRemainOutsideSemanticBinding()
    {
        var source = FixtureSource();
        var policy = Policy();
        var compiler = Compiler();
        var request = Request(source, policy.Snapshot, compiler);
        var evidence = new SecurityEvaluationEvidence(source, policy, compiler);
        var original = SecurityBinding.Create(request, evidence);
        var other = SecurityBinding.Create(Copy(request, evaluationId: Guid.NewGuid(), mode: SecurityEnforcementMode.Disabled), evidence);

        Assert.NotEqual(original.Request.EvaluationId, other.Request.EvaluationId);
        Assert.NotEqual(original.Request.EnforcementMode, other.Request.EnforcementMode);
        Assert.Equal(original.Data.CanonicalText, other.Data.CanonicalText);
        Assert.Equal(original.Digest.Value, other.Digest.Value);
        foreach (var field in new[] { "evaluationId", "createdUtc", "databaseRowId", "enforcementMode" })
            Assert.False(original.Data.Value.TryGetProperty(field, out _));
    }

    [Fact]
    public void SchemaAndDomainTagsPreventAccidentalCrossContractEquivalence()
    {
        var binding = Bind(FixtureSource());
        Assert.NotEqual(binding.Digest.Value, ContentDigest.Compute(SourceDocument.DigestDomain, binding.Data.ToCanonicalBytes()).Value);
        var changed = JsonNode.Parse(binding.Data.CanonicalText)!;
        changed["schemaVersion"] = 2;
        Assert.NotEqual(binding.Digest.Value, ContentDigest.Compute(SecurityBinding.DigestDomain,
            SourceJson.Parse(changed.ToJsonString()).ToCanonicalBytes()).Value);
        Assert.Equal(SecurityBinding.DigestDomain, binding.Data.Value.GetProperty("domainTag").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PolicySchemaVersionMustBePositive(int version) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecurityPolicySnapshot("test", "1", FixtureSource().Digest, version));

    [Theory]
    [InlineData("")]
    [InlineData("main")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void AProvidedCompilerCommitMustBeExactCanonicalIdentity(string commit) =>
        Assert.Throws<ArgumentException>(() => new SecurityCompilerProvenance("test-host/1", "fixture-build-1", commit));

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "ProjectIde", "Fixtures", name));
    private static ProjectSource FixtureSource(string name = "source-v1.json") => ProjectSourceCodec.Decode(Fixture(name)).RequireSource();
    private static ProjectSource WithContext(SourceRevisionContext context, SourceJson? extensions = null) =>
        ProjectSource.Create(context, [FixtureSource().Documents[0]], extensions: extensions);
    private static SecurityPolicyEvidence Policy(string content = "{\"rules\":{\"a\":true,\"b\":false}}") =>
        new("foundation-test", "1", SourceJson.Parse(content));
    private static SecurityCompilerProvenance Compiler() => new("test-host/1", "fixture-build-1");
    private static SecurityBinding Bind(ProjectSource source, SecurityPolicyEvidence? policy = null, SecurityCompilerProvenance? compiler = null)
    {
        policy ??= Policy();
        compiler ??= Compiler();
        return SecurityBinding.Create(Request(source, policy.Snapshot, compiler), new(source, policy, compiler));
    }
    private static SecurityEvaluationRequest Request(ProjectSource source, SecurityPolicySnapshot? policy, SecurityCompilerProvenance? compiler) =>
        new(Guid.NewGuid(), new(source.TenantId, UserId), new("projectide.analyze"), new(source.Context, source.Digest),
            SecurityEnforcementMode.Shadow, source, policy, compiler);
    private static SecurityEvaluationRequest Copy(SecurityEvaluationRequest original, SecuritySubjectRef? subject = null,
        SecurityOperationRef? operation = null, SecurityResourceRef? resource = null, ProjectSource? source = null,
        SecurityPolicySnapshot? policy = null, SecurityCompilerProvenance? compiler = null, Guid? evaluationId = null,
        SecurityEnforcementMode? mode = null) => new(evaluationId ?? original.EvaluationId, subject ?? original.Subject,
        operation ?? original.Operation, resource ?? original.Resource, mode ?? original.EnforcementMode,
        source ?? original.Source, policy ?? original.Policy, compiler ?? original.Compiler);
}
