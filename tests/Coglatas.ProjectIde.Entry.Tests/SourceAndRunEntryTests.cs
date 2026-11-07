using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using Coglatas.SecurityArchitecture;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde.Entry;

public sealed class SourceAndRunEntryTests
{
    private const string Candidate = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string TestSource = "namespace Synthetic.Tests;\npublic sealed class EntryTests\n{\n    public void RejectForeignInput() { Assert.False(false); }\n}\n";
    private static readonly byte[] Bytes = Encoding.UTF8.GetBytes(TestSource);
    private static readonly string Blob = Convert.ToHexStringLower(SHA1.HashData(
        Encoding.UTF8.GetBytes("blob " + Bytes.Length + "\0").Concat(Bytes).ToArray()));
    private static SpecVerifier Verifier() => new("Synthetic.Entry", "1", SpecVerificationClass.ContractTest,
        new(Candidate, "synthetic/EntryTests.cs", null, SpecDigest.Bytes(Bytes)), "Synthetic.Tests.EntryTests.RejectForeignInput", ["Synthetic.Execution"]);
    private static SpecRequirement Requirement() => new("SPEC-AUTH-SYNTHETIC-001", [new(1, SpecStatus.Active,
        new(Candidate, "synthetic/requirement.md", null, new string('b', 64)), "Synthetic requirement.", new string('c', 64),
        "synthetic-owner", SpecSeverity.Advisory, [SpecVerificationClass.ContractTest], "Synthetic only.", [], "synthetic-reference")]);
    private static SpecMapping Mapping(SpecVerifier verifier) => new("SPEC-AUTH-SYNTHETIC-001", 1, "SEC-ARCH-SYNTHETIC",
        new string('d', 64), [verifier]);
    private static SourceAssertionReference Assertion() => new(Array.IndexOf(Bytes, (byte)'A'), "Assert.False(false);".Length, SpecDigest.Text("Assert.False(false);"));

    [Fact]
    public void Source_declaration_and_reviewed_assertion_references_remain_nonexecution()
    {
        var verifier = Verifier(); var requirement = Requirement(); var mapping = Mapping(verifier);
        var declaration = SourceEntryValidator.Check(mapping, requirement, verifier, Candidate, Blob, Bytes);
        Assert.Equal(SourceEntryState.DeclarationOnly, declaration.State);
        var located = SourceEntryValidator.Check(mapping, requirement, verifier, Candidate, Blob, Bytes, Assertion());
        Assert.Equal(SourceEntryState.AssertionReferenceBound, located.State);
        foreach (var result in new[] { declaration, located })
        {
            Assert.Equal("NOT_EXECUTED", result.ExecutionStatus); Assert.Equal("UNVERIFIED", result.SemanticAssertionStatus);
            Assert.Equal("UNVERIFIED", result.ApprovalStatus);
        }
    }

    [Theory]
    [InlineData("revision", SourceEntryState.Stale)] [InlineData("blob", SourceEntryState.Stale)]
    [InlineData("digest", SourceEntryState.Stale)] [InlineData("requirement-version", SourceEntryState.Stale)]
    [InlineData("requirement-id", SourceEntryState.Invalid)] [InlineData("missing", SourceEntryState.Missing)]
    [InlineData("method", SourceEntryState.Missing)] [InlineData("skipped", SourceEntryState.Skipped)]
    [InlineData("assertion-digest", SourceEntryState.Stale)] [InlineData("assertion-range", SourceEntryState.Invalid)]
    [InlineData("path", SourceEntryState.Invalid)] [InlineData("candidate-newline", SourceEntryState.Invalid)]
    public void Source_entry_rejects_false_green_substitutions(string fault, SourceEntryState expected)
    {
        var verifier = Verifier(); var mapping = Mapping(verifier); var requirement = Requirement();
        var blob = Blob; var candidate = Candidate; var assertion = Assertion(); byte[]? bytes = Bytes;
        if (fault == "revision") verifier = verifier with { Source = verifier.Source with { Revision = new string('f', 40) } };
        if (fault == "digest") verifier = verifier with { Source = verifier.Source with { Digest = new string('f', 64) } };
        if (fault == "path") verifier = verifier with { Source = verifier.Source with { Path = "../escape.cs" } };
        if (fault == "method") verifier = verifier with { SourceIdentity = "Synthetic.Tests.EntryTests.Invented" };
        mapping = mapping with { Verifiers = [verifier] };
        if (fault == "blob") blob = new string('f', 40);
        if (fault == "requirement-version") mapping = mapping with { RequirementVersion = 2 };
        if (fault == "requirement-id") mapping = mapping with { SpecId = "SPEC-AUTH-SYNTHETIC-002" };
        if (fault == "missing") bytes = null;
        if (fault == "assertion-digest") assertion = assertion with { Digest = new string('f', 64) };
        if (fault == "assertion-range") assertion = assertion with { ByteOffset = int.MaxValue, ByteLength = int.MaxValue };
        if (fault == "candidate-newline") candidate += "\n";
        var result = SourceEntryValidator.Check(mapping, requirement, verifier, candidate, blob, bytes, assertion, fault == "skipped");
        Assert.Equal(expected, result.State); Assert.Equal("NOT_EXECUTED", result.ExecutionStatus);
    }

    [Theory]
    [InlineData(null, CompilerFreshness.Undetermined, "coglatas.evidence.run-unavailable")]
    [InlineData("other-run", CompilerFreshness.Stale, "coglatas.evidence.run-mismatch")]
    [InlineData("desired-run", CompilerFreshness.Current, null)]
    public void Requirement_result_run_and_source_are_independent_bindings(string? run, CompilerFreshness freshness, string? diagnostic)
    {
        var source = Source(); var settings = Settings();
        settings["rules"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { ruleId = CoreCompilerRules.Requirements, version = 1, required = true, configuration = new { } }));
        var target = new { tenantId = source.TenantId.ToString(), projectId = source.ProjectId.ToString(), entityId = Id(6), context = source.Context.Data.Value };
        var declarations = SourceJson.Parse(JsonSerializer.Serialize(new[] { new
        {
            entityId = Id(8), documentId = Id(4), kind = "coglatas.requirement", schemaVersion = 1,
            payload = new { target, condition = new { kind = "coglatas.literal", value = new
                { type = new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = true }, state = "coglatas.known", value = true } }, evidenceKind = "coglatas.model-proof" }
        } }));
        source = ProjectSource.Create(source.Context, source.Documents, declarations);
        var input = Input(settings, source); var location = ProjectLocation.Create(source.Documents[0].DocumentId, source.Context, entityId: EntityId.Parse(Id(8)));
        var reference = new PlanEntityReference(SourceJson.Parse(JsonSerializer.Serialize(target)));
        var claim = new RequirementEvidenceClaim(location, reference, Pass(input), run);
        var result = RequirementEvidenceValidator.Assess(input, location, [claim], expectedRunId: "desired-run");
        Assert.Equal(freshness, result.Freshness); Assert.NotEqual(CompilerVerdict.Pass, result.Outcome.Verdict);
        if (diagnostic is not null) Assert.Contains(result.Findings, finding => finding.Diagnostic.Code == diagnostic);
        Assert.Equal(CompilerEvidenceKind.Missing, result.Outcome.Evidence.Kind);
    }
}
