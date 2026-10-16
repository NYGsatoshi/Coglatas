using System.Text;
using Coglatas.SecurityArchitecture;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed class SecurityArchitectureSpecEvidenceAccountingTests
{
    private static readonly string Candidate = new('a', 40);
    private static readonly string SpecificationRevision = new('c', 40);
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-10T00:00:00Z");
    private const string Identity = "SPEC-AUTH-SYNTHETIC-ACCOUNTING-001";
    private const string Statement = "Synthetic foreign-scope access must be rejected.";
    private const string Declaration = """
        using Xunit;
        namespace Synthetic.Tests;
        public sealed class ScopeTests
        {
            private static bool CanRead(string callerScope, string resourceScope) => callerScope == resourceScope;
            [Fact]
            public void RejectForeignCaller()
            {
                Assert.False(CanRead("foreign", "owner"));
            }
        }
        """;

    private static SpecRegistryDocument Registry() => new(1, 1, SpecificationRevision,
        [new(Identity, [new(1, SpecStatus.Active,
            new(SpecificationRevision, "synthetic/authority.md", null, SpecDigest.Text(Statement)),
            Statement, SpecDigest.Text(Statement), "synthetic-owner", SpecSeverity.Advisory,
            [SpecVerificationClass.ContractTest], "Pure synthetic metadata; no product or attestation credit.",
            [], "synthetic-review-pending")])]);

    private static FlowContract Contract() => new("SEC-ARCH-SYNTHETIC-ACCOUNTING", [Identity], "synthetic-owner",
        ContractType.Api, "synthetic-caller", "synthetic-service", "synthetic-http", "synthetic-principal",
        "read", "synthetic-tenant", "synthetic-resource", "synthetic-boundary", AccessDecision.Deny,
        "synthetic-deny", [EvidenceClass.Runtime], ActivationState.Active, ["SYNTHETIC-STRIDE-001"],
        Api: new("/synthetic/resource", "GET", false, "synthetic-policy"));

    private static SpecTraceabilityDocument Manifest(SpecRegistryDocument registry) => new(1, 1,
        SpecDigest.Document(registry), [new(Identity, 1, Contract().ContractId, SpecDigest.Document(Contract()),
            [new("Synthetic.Accounting.Verifier", "1.0", SpecVerificationClass.ContractTest,
                new(Candidate, "synthetic/ScopeTests.cs", null, SpecDigest.Text(Declaration)),
                "Synthetic.Tests.ScopeTests.RejectForeignCaller", ["Synthetic.Accounting.Execution"])])]);

    private static SpecExecutionLink Record(SpecTraceabilityDocument manifest)
    {
        var mapping = manifest.Mappings[0];
        var verifier = mapping.Verifiers[0];
        return new(verifier.EvidenceIds[0], mapping.SpecId, mapping.RequirementVersion, mapping.ContractId,
            verifier.VerifierId, verifier.Version, verifier.Source.Digest, mapping.ContractDigest, Candidate,
            EvidenceOutcome.Pass, true, "synthetic-execution", new('b', 64));
    }

    private static Task<SpecValidationResult> Check(SpecRegistryDocument registry, SpecTraceabilityDocument manifest,
        int evidenceSchema = 1, string? source = Declaration) => SpecTraceabilityValidator.ValidateAsync(registry,
        manifest, new(1, [Contract()]), SpecificationRevision, Candidate, Now,
        _ => Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes(Statement)),
        _ => Task.FromResult(source is null ? null : Encoding.UTF8.GetBytes(source)),
        new(evidenceSchema, [Record(manifest)]));

    [Fact]
    public async Task StructurallyBoundSyntheticLinkHasExplicitMetadataOnlyPassingCount()
    {
        var registry = Registry();
        var result = await Check(registry, Manifest(registry));
        Assert.True(result.Valid);
        Assert.Equal(1, result.Coverage!.ExecutedPassingLinks);
        Assert.Equal(0, result.Coverage.UnresolvedLinks);
        Assert.False(result.NormativeReady);
        Assert.Equal("UNVERIFIED", result.ApprovalStatus);
        Assert.Equal("UNVERIFIED", result.ExecutionAttestationStatus);
    }

    [Fact]
    public async Task SevenSyntheticExecutionLinksCannotApproveFortyOneMappings()
    {
        var template = Registry();
        var requirements = Enumerable.Range(1, 41).Select(index => new SpecRequirement(
            $"SPEC-AUTH-SYNTHETIC-ACCOUNTING-{index:D3}",
            [template.Requirements[0].Versions[0] with
            { Source = template.Requirements[0].Versions[0].Source with { Path = $"synthetic/authority-{index:D3}.md" } }])).ToArray();
        var registry = template with { Requirements = requirements };
        var contracts = requirements.Select((requirement, index) => Contract() with
        { ContractId = $"SEC-ARCH-SYNTHETIC-ACCOUNTING-{index + 1:D3}", SpecIds = [requirement.SpecId] }).ToArray();
        var mapping = Manifest(template).Mappings[0];
        var mappings = contracts.Select((contract, index) => mapping with
        {
            SpecId = contract.SpecIds[0], ContractId = contract.ContractId, ContractDigest = SpecDigest.Document(contract),
            Verifiers = [mapping.Verifiers[0] with { EvidenceIds = [$"Synthetic.Accounting.Execution.{index + 1:D3}"] }]
        }).ToArray();
        var manifest = new SpecTraceabilityDocument(1, 1, SpecDigest.Document(registry), mappings);
        var links = mappings.Take(7).Select(item => Record(manifest with { Mappings = [item] })).ToArray();
        var result = await SpecTraceabilityValidator.ValidateAsync(registry, manifest, new(1, contracts),
            SpecificationRevision, Candidate, Now,
            _ => Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes(Statement)),
            _ => Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes(Declaration)), new(1, links));
        Assert.False(result.Valid);
        Assert.Equal(41, result.Coverage!.Mappings);
        Assert.Equal(7, result.Coverage.ExecutedPassingLinks);
        Assert.Equal(34, result.Coverage.UnresolvedLinks);
        Assert.Equal(34, result.Diagnostics.Count(diagnostic => diagnostic.RuleId == "SPEC_MISSING_EXECUTION"));
        Assert.False(result.NormativeReady);
        Assert.Equal("UNVERIFIED", result.ApprovalStatus);
        Assert.Equal("UNVERIFIED", result.ExecutionAttestationStatus);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-1)]
    public async Task UnsupportedEvidenceSchemaCannotRetainPassingLinkCredit(int schema)
    {
        var registry = Registry();
        var manifest = Manifest(registry);
        Assert.True((await Check(registry, manifest)).Valid);
        var result = await Check(registry, manifest, schema);
        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.RuleId == "SPEC_EVIDENCE_SCHEMA");
        Assert.Equal(0, result.Coverage!.ExecutedPassingLinks);
        Assert.Equal(1, result.Coverage.UnresolvedLinks);
        Assert.False(result.NormativeReady);
    }

    [Theory]
    [InlineData("assertion-weakened", "SPEC_SOURCE_DIGEST")]
    [InlineData("renamed-test", "SPEC_VERIFIER_IDENTITY")]
    [InlineData("deleted-test", "SPEC_VERIFIER_IDENTITY")]
    [InlineData("missing-source", "SPEC_SOURCE_MISSING")]
    [InlineData("stale-requirement", "SPEC_STALE_MAPPING")]
    [InlineData("changed-contract", "SPEC_CONTRACT_DIGEST")]
    [InlineData("wrong-source-candidate", "SPEC_VERIFIER_CANDIDATE")]
    [InlineData("duplicate-mapping", "SPEC_DUPLICATE_MAPPING")]
    public async Task InvalidStructuralBindingsCannotKeepSelfReportedPassingLinkCredit(string mutation, string rule)
    {
        var registry = Registry();
        var manifest = Manifest(registry);
        var positive = await Check(registry, manifest);
        Assert.True(positive.Valid);
        Assert.Equal(1, positive.Coverage!.ExecutedPassingLinks);
        var mapping = manifest.Mappings[0];
        var verifier = mapping.Verifiers[0];
        var source = mutation switch
        {
            "assertion-weakened" => Declaration.Replace("Assert.False(CanRead(\"foreign\", \"owner\"));", "Assert.True(true);"),
            "renamed-test" => Declaration.Replace("RejectForeignCaller", "NoLongerRejectForeignCaller"),
            "deleted-test" => "namespace Synthetic.Tests;\npublic sealed class ScopeTests { }\n",
            "missing-source" => null,
            _ => Declaration
        };
        // Rebinding a renamed/deleted declaration's digest cannot restore its old method identity.
        if (mutation is "renamed-test" or "deleted-test")
            verifier = verifier with { Source = verifier.Source with { Digest = SpecDigest.Text(source!) } };
        if (mutation == "wrong-source-candidate")
            verifier = verifier with { Source = verifier.Source with { Revision = new('d', 40) } };
        mapping = mapping with { Verifiers = [verifier] };
        if (mutation == "stale-requirement") mapping = mapping with { RequirementVersion = 2 };
        if (mutation == "changed-contract") mapping = mapping with { ContractDigest = new('e', 64) };
        manifest = manifest with { Mappings = mutation == "duplicate-mapping" ? [mapping, mapping] : [mapping] };
        var result = await Check(registry, manifest, source: source);
        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.RuleId == rule);
        Assert.Equal(0, result.Coverage!.ExecutedPassingLinks);
        Assert.Equal(1, result.Coverage.UnresolvedLinks);
        Assert.False(result.NormativeReady);
        Assert.Equal("UNVERIFIED", result.ExecutionAttestationStatus);
    }
}
