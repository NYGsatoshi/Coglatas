using System.Text;
using Coglatas.SecurityArchitecture;

namespace Coglatas.Tests.Tooling;

public sealed class SourceDeclarationMatchingTests
{
    private const string VerifierSource = "namespace Synthetic.Tests;\npublic sealed class ScopeTests\n{\n    public void RejectForeignCaller() { }\n}\n";

    [Theory]
    [InlineData("namespace")]
    [InlineData("class")]
    [InlineData("method")]
    [InlineData("after-method")]
    public async Task UnavailableDeclarationMatchingCannotGrantExecutionCredit(string stage)
    {
        var whitespace = new string('\n', 262144);
        var source = stage switch
        {
            "namespace" => whitespace + "public sealed class ScopeTests { public void RejectForeignCaller() {} }",
            "class" => "namespace Synthetic.Tests;\n" + whitespace + "public void RejectForeignCaller() {}",
            "method" => "namespace Synthetic.Tests;\npublic sealed class ScopeTests\n{\n" + whitespace + "}\n",
            _ => VerifierSource + whitespace
        };

        var result = await TraceDeclaration(source);

        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.RuleId == "SPEC_VERIFIER_IDENTITY");
        Assert.Equal(0, result.Coverage!.ExecutedPassingLinks);
        Assert.Equal(1, result.Coverage.UnresolvedLinks);
        Assert.False(result.NormativeReady);
        Assert.Equal("UNVERIFIED", result.ExecutionAttestationStatus);
    }

    [Theory]
    [InlineData("namespace Synthetic.Tests;\npublic sealed class ScopeTests\n{\n public void RejectForeignCaller() {}\n}\n")]
    [InlineData("namespace Synthetic.Tests\r\n{\r\n public partial static class ScopeTests\r\n {\r\n public static async Task<int> RejectForeignCaller() {}\r\n }\r\n}\r\n")]
    [InlineData(" namespace Synthetic.Tests;\n public static partial class ScopeTests\n {\n public ValueTask RejectForeignCaller() {}\n }\n")]
    [InlineData("namespace Synthetic.Tests;\npublic partial class ScopeTests\n{\npublic static ValueTask<int> RejectForeignCaller() {}\n}\n")]
    public async Task BoundedDeclarationMatchingPreservesExistingSyntaxAndAccounting(string source)
    {
        var result = await TraceDeclaration(source);

        Assert.True(result.Valid);
        Assert.Equal(1, result.Coverage!.ExecutedPassingLinks);
        Assert.Equal(0, result.Coverage.UnresolvedLinks);
        Assert.False(result.NormativeReady);
        Assert.Equal("UNVERIFIED", result.ExecutionAttestationStatus);
    }

    [Fact]
    public async Task BoundedDeclarationMatchingAcceptsLargeOrdinarySourceAndRejectsDuplicateMethods()
    {
        var padding = string.Concat(Enumerable.Repeat("// Synthetic source padding.\n", 50000));
        Assert.True((await TraceDeclaration(padding + VerifierSource)).Valid);
        var duplicate = VerifierSource + "\npublic void RejectForeignCaller() {}\n";
        var result = await TraceDeclaration(duplicate);
        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.RuleId == "SPEC_VERIFIER_IDENTITY");
        Assert.Equal(0, result.Coverage!.ExecutedPassingLinks);
    }

    [Fact]
    public async Task BoundedDeclarationMatchingRetainsLiteralNamesAndExactMarkerCardinality()
    {
        const string literal = "namespace Synthetic.Tests[0];\npublic class ScopeTests+Literal\n{\npublic void RejectForeignCaller() {}\n}\n";
        Assert.True((await TraceDeclaration(literal, "Synthetic.Tests[0].ScopeTests+Literal.RejectForeignCaller")).Valid);
        Assert.False((await TraceDeclaration(VerifierSource, "Synthetic.Tests[0].ScopeTests.RejectForeignCaller")).Valid);
        Assert.False((await TraceDeclaration(VerifierSource, "Synthetic.Tests.ScopeTests.RejectForeignCaller!")).Valid);
        const string marker = "# verifier-id: Synthetic.Marker\r\n";
        Assert.True((await TraceDeclaration(marker, "marker:Synthetic.Marker")).Valid);
        Assert.False((await TraceDeclaration(marker + marker, "marker:Synthetic.Marker")).Valid);
        Assert.False((await TraceDeclaration(" " + marker, "marker:Synthetic.Marker")).Valid);
    }

    private static Task<SpecValidationResult> TraceDeclaration(string source,
        string identity = "Synthetic.Tests.ScopeTests.RejectForeignCaller")
    {
        var candidate = new string('a', 40);
        var specificationRevision = new string('c', 40);
        const string specId = "SPEC-AUTH-SYNTHETIC-DECLARATION-001";
        const string statement = "Synthetic declaration linkage must remain unverified when unavailable.";
        var registry = new SpecRegistryDocument(1, 1, specificationRevision,
            [new(specId, [new(1, SpecStatus.Active,
                new(specificationRevision, "synthetic/authority.md", null, SpecDigest.Text(statement)),
                statement, SpecDigest.Text(statement), "synthetic-owner", SpecSeverity.Advisory,
                [SpecVerificationClass.ContractTest], "Synthetic metadata only; no formal assurance.",
                [], "synthetic-review-pending")])]);
        var contract = new FlowContract("SEC-ARCH-SYNTHETIC-DECLARATION", [specId], "synthetic-owner",
            ContractType.Api, "synthetic-caller", "synthetic-service", "synthetic-http", "synthetic-principal",
            "read", "synthetic-tenant", "synthetic-resource", "synthetic-boundary", AccessDecision.Deny,
            "synthetic-deny", [EvidenceClass.Runtime], ActivationState.Active, ["SYNTHETIC-STRIDE-001"],
            Api: new("/synthetic/resource", "GET", false, "synthetic-policy"));
        var verifier = new SpecVerifier("Synthetic.Declaration.Verifier", "1.0", SpecVerificationClass.ContractTest,
            new(candidate, "synthetic/Declaration.cs", null, SpecDigest.Text(source)), identity,
            ["Synthetic.Declaration.Execution"]);
        var manifest = new SpecTraceabilityDocument(1, 1, SpecDigest.Document(registry),
            [new(specId, 1, contract.ContractId, SpecDigest.Document(contract), [verifier])]);
        var link = new SpecExecutionLink(verifier.EvidenceIds[0], specId, 1, contract.ContractId,
            verifier.VerifierId, verifier.Version, verifier.Source.Digest, SpecDigest.Document(contract),
            candidate, EvidenceOutcome.Pass, true, "synthetic-execution-reference", new('b', 64));
        return SpecTraceabilityValidator.ValidateAsync(registry, manifest, new(1, [contract]), specificationRevision,
            candidate, DateTimeOffset.Parse("2026-10-10T00:00:00Z"),
            _ => Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes(statement)),
            _ => Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes(source)), new(1, [link]));
    }
}
