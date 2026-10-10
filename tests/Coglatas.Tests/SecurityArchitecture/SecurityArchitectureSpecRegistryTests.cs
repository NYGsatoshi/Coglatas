using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Coglatas.SecurityArchitecture;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed class SecurityArchitectureSpecRegistryTests
{
    private static readonly string CandidateSha = new('a', 40);
    private static readonly string SpecificationSha = new('c', 40);
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-10T00:00:00Z");
    private const string SpecId = "SPEC-AUTH-SYNTHETIC-001";
    private const string Statement = "Synthetic scope must reject a foreign caller.";
    private const string Specification = "<a id=\"synthetic-authority\"></a>\n" + Statement + "\n";
    private const string VerifierSource = "namespace Synthetic.Tests;\npublic sealed class ScopeTests\n{\n    public void RejectForeignCaller() { }\n}\n";

    private static SpecRegistryDocument Registry(SpecSeverity severity = SpecSeverity.Advisory,
        SpecVerificationClass verifierClass = SpecVerificationClass.ContractTest) => new(1, 1, SpecificationSha,
        [new(SpecId, [new(1, SpecStatus.Active,
            new(SpecificationSha, "synthetic/authority.md", "synthetic-authority", SpecDigest.Text(Specification)),
            Statement, SpecDigest.Text(Statement), "synthetic-owner", severity, [verifierClass],
            "Synthetic fixture only; no product assurance.", [], "synthetic-review-pending")])]);

    private static FlowContract Contract() => new("SEC-ARCH-SYNTHETIC-SCOPE", [SpecId], "synthetic-owner",
        ContractType.Api, "synthetic-caller", "synthetic-service", "synthetic-http", "synthetic-alpha",
        "read", "synthetic-alpha", "synthetic-resource", "synthetic-boundary", AccessDecision.Deny,
        "synthetic-deny-rule", [EvidenceClass.Runtime], ActivationState.Active, ["SYNTHETIC-STRIDE-001"],
        Api: new("/synthetic/resource", "GET", false, "synthetic-policy"));

    private static SpecTraceabilityDocument Manifest(SpecRegistryDocument registry) => new(1, registry.RegistryVersion,
        SpecDigest.Document(registry), [new(SpecId, registry.Requirements[0].Versions[^1].Version,
            Contract().ContractId, SpecDigest.Document(Contract()),
            [new("Synthetic.Scope.Verifier", "1.0", registry.Requirements[0].Versions[^1].VerificationClasses[0],
                new(CandidateSha, "synthetic/ScopeTests.cs", null, SpecDigest.Text(VerifierSource)),
                "Synthetic.Tests.ScopeTests.RejectForeignCaller", ["Synthetic.Scope.Execution"])])]);

    private static SpecExecutionLink Link(SpecTraceabilityDocument manifest)
    {
        var mapping = manifest.Mappings[0];
        var verifier = mapping.Verifiers[0];
        return new(verifier.EvidenceIds[0], mapping.SpecId, mapping.RequirementVersion, mapping.ContractId,
            verifier.VerifierId, verifier.Version, verifier.Source.Digest, mapping.ContractDigest,
            CandidateSha, EvidenceOutcome.Pass, true, "synthetic-execution-reference", new('b', 64));
    }

    private static Task<byte[]?> ReadSpecification(SpecSource source) => Task.FromResult(
        source.Path == "synthetic/authority.md" && source.Revision == SpecificationSha ? Encoding.UTF8.GetBytes(Specification) : null);
    private static Task<byte[]?> ReadImplementation(SpecSource source) => Task.FromResult(
        source.Path == "synthetic/ScopeTests.cs" && source.Revision == CandidateSha ? Encoding.UTF8.GetBytes(VerifierSource) : null);
    private static Task<SpecValidationResult> Validate(SpecRegistryDocument registry, SpecRegistryDocument? baseline = null) =>
        SpecRegistryValidator.ValidateAsync(registry, SpecificationSha, ReadSpecification, baseline);
    private static Task<SpecValidationResult> Trace(SpecRegistryDocument registry, SpecTraceabilityDocument manifest,
        SpecTraceabilityEvidenceDocument? evidence = null, FlowContract? contract = null,
        Func<SpecSource, Task<byte[]?>>? implementation = null, SpecRegistryDocument? baseline = null) =>
        SpecTraceabilityValidator.ValidateAsync(registry, manifest, new(1, [contract ?? Contract()]), SpecificationSha,
            CandidateSha, Now, ReadSpecification, implementation ?? ReadImplementation, evidence, baseline);

    [Fact]
    public async Task VersionedSyntheticRegistryValidatesWithoutGrantingNormativeAuthority()
    {
        var result = await Validate(Registry());
        Assert.True(result.Valid);
        Assert.False(result.NormativeReady);
        Assert.Equal("UNVERIFIED", result.ApprovalStatus);
        Assert.Equal("UNVERIFIED", result.ExecutionAttestationStatus);
        var classes = new (SpecVerificationClass Class, string WireName)[]
        {
            (SpecVerificationClass.ArchUnit, "archunit"), (SpecVerificationClass.Roslyn, "roslyn"),
            (SpecVerificationClass.StaticCustom, "static-custom"), (SpecVerificationClass.UnitTest, "unit-test"),
            (SpecVerificationClass.IntegrationTest, "integration-test"), (SpecVerificationClass.E2ETest, "e2e-test"),
            (SpecVerificationClass.ContractTest, "contract-test"), (SpecVerificationClass.GeneratedEvidence, "generated-evidence"),
            (SpecVerificationClass.Manual, "manual")
        };
        foreach (var (verifierClass, wireName) in classes)
        {
            var serialized = JsonSerializer.Serialize(verifierClass, ContractJson.Options);
            Assert.Equal("\"" + wireName + "\"", serialized);
            Assert.Equal(verifierClass, ContractJson.Read<SpecVerificationClass>(serialized));
            var registered = await Validate(Registry(verifierClass == SpecVerificationClass.Manual
                ? SpecSeverity.Manual : SpecSeverity.Advisory, verifierClass));
            Assert.True(registered.Valid);
            Assert.False(registered.NormativeReady);
        }
    }

    [Fact]
    public async Task SourceDigestAndStatementMustUseTheSameReadSnapshot()
    {
        Assert.True((await Validate(Registry())).Valid);
        var registry = Registry();
        var requirement = registry.Requirements[0];
        var invented = "Synthetic spliced statement absent from the pinned source.";
        registry = registry with { Requirements = [requirement with { Versions = [requirement.Versions[0] with
        { NormativeStatement = invented, StatementDigest = SpecDigest.Text(invented) }] }] };
        var reads = 0;
        var result = await SpecRegistryValidator.ValidateAsync(registry, SpecificationSha, _ => Task.FromResult<byte[]?>(
            Encoding.UTF8.GetBytes(++reads == 1 ? Specification : Specification + invented)));
        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, d => d.RuleId == "SPEC_STATEMENT_SOURCE");
        Assert.Equal(1, reads);
        Assert.False(result.NormativeReady);
    }

    [Fact]
    public async Task VerifierDigestAndDeclarationMustUseTheSameReadSnapshot()
    {
        var registry = Registry();
        var manifest = Manifest(registry);
        Assert.True((await Trace(registry, manifest)).Valid);
        var mapping = manifest.Mappings[0];
        var verifier = mapping.Verifiers[0] with { SourceIdentity = "Synthetic.Tests.ScopeTests.ForgedGreen" };
        manifest = manifest with { Mappings = [mapping with { Verifiers = [verifier] }] };
        var reads = 0;
        var result = await Trace(registry, manifest, implementation: _ => Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes(
            ++reads == 1 ? VerifierSource : VerifierSource.Replace("RejectForeignCaller", "ForgedGreen", StringComparison.Ordinal))));
        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, d => d.RuleId == "SPEC_VERIFIER_IDENTITY");
        Assert.Equal(1, reads);
        Assert.Equal("UNVERIFIED", result.ExecutionAttestationStatus);
    }

    [Fact]
    public async Task OversizedOtherwiseValidCliDocumentCannotBeAcceptedOrEchoed()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sec-arch-json-bound-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "contracts.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new ContractDocument(1, [Contract()]), ContractJson.Options));
            await using var positive = new StringWriter();
            Assert.Equal(0, await SecurityArchitectureCli.RunAsync(["validate", path, Now.ToString("O")], positive));
            await using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None, 8192, useAsync: true))
            {
                var spaces = Enumerable.Repeat((byte)' ', 1024 * 1024).ToArray();
                for (var index = 0; index < 33; index++) await stream.WriteAsync(spaces);
            }
            await using var negative = new StringWriter();
            Assert.Equal(1, await SecurityArchitectureCli.RunAsync(["validate", path, Now.ToString("O")], negative));
            Assert.Contains("INPUT_ERROR", negative.ToString());
            Assert.DoesNotContain(directory, negative.ToString());
            Assert.DoesNotContain("synthetic-owner", negative.ToString());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("removed-retired", "SPEC_HISTORY_REMOVED")]
    [InlineData("rewritten-history", "SPEC_HISTORY_REWRITTEN")]
    [InlineData("rollback", "SPEC_REGISTRY_ROLLBACK")]
    public async Task TraceabilityMustPreserveIndependentlySuppliedAllocationHistory(string mutation, string expectedRule)
    {
        var original = Registry();
        var retired = new SpecRequirement("SPEC-AUTH-SYNTHETIC-002",
            [original.Requirements[0].Versions[0] with { Status = SpecStatus.Retired,
                RetirementReason = "Synthetic retained retirement." }]);
        var baseline = original with { RegistryVersion = 2, Requirements = [original.Requirements[0], retired] };
        Assert.True((await Validate(baseline)).Valid);
        var candidate = baseline with { RegistryVersion = 3 };
        Assert.True((await Trace(candidate, Manifest(candidate), baseline: baseline)).Valid);
        candidate = mutation switch
        {
            "removed-retired" => candidate with { Requirements = [candidate.Requirements[0]] },
            "rewritten-history" => candidate with { Requirements = [candidate.Requirements[0] with
            { Versions = [candidate.Requirements[0].Versions[0] with { Owner = "different-synthetic-owner" }] }, retired] },
            "rollback" => candidate with { RegistryVersion = 1 },
            _ => candidate
        };
        Assert.True((await Trace(candidate, Manifest(candidate))).Valid);
        var result = await Trace(candidate, Manifest(candidate), baseline: baseline);
        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.RuleId == expectedRule);
        Assert.False(result.NormativeReady);
    }

    [Theory]
    [InlineData("ARCH")]
    [InlineData("AUTH")]
    [InlineData("RT")]
    [InlineData("STATE")]
    [InlineData("API")]
    [InlineData("UI")]
    public async Task AllCanonicalFamiliesSupportSyntheticAllocationWithoutCreatingRequirements(string family)
    {
        var registry = Registry();
        registry = registry with { Requirements = [registry.Requirements[0] with { SpecId = "SPEC-" + family + "-SYNTHETIC-001" }] };
        Assert.True((await Validate(registry)).Valid);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("empty")]
    [InlineData("source-revision")]
    [InlineData("duplicate")]
    [InlineData("family")]
    [InlineData("missing-history")]
    [InlineData("version-gap")]
    [InlineData("unknown-status")]
    [InlineData("unknown-severity")]
    [InlineData("missing-statement")]
    [InlineData("missing-owner")]
    [InlineData("missing-limitation")]
    [InlineData("missing-review")]
    [InlineData("wrong-statement-digest")]
    [InlineData("invented-statement")]
    [InlineData("unsupported-class")]
    [InlineData("missing-class")]
    [InlineData("duplicate-class")]
    [InlineData("manual-auto-class")]
    [InlineData("source-missing")]
    [InlineData("source-digest")]
    [InlineData("source-anchor")]
    [InlineData("path-escape")]
    [InlineData("missing-retirement-reason")]
    [InlineData("false-retirement")]
    [InlineData("unknown-successor")]
    [InlineData("wrong-architecture-id")]
    public async Task DeliberateInvalidRegistryFixturesAreRejectedDeterministically(string mutation)
    {
        var registry = Registry();
        var requirement = registry.Requirements[0];
        var version = requirement.Versions[0];
        version = mutation switch
        {
            "version-gap" => version with { Version = 2 },
            "unknown-status" => version with { Status = (SpecStatus)999 },
            "unknown-severity" => version with { Severity = (SpecSeverity)999 },
            "missing-statement" => version with { NormativeStatement = "" },
            "missing-owner" => version with { Owner = "" },
            "missing-limitation" => version with { KnownLimitation = "" },
            "missing-review" => version with { ChangeReference = "" },
            "wrong-statement-digest" => version with { StatementDigest = new('f', 64) },
            "invented-statement" => version with { NormativeStatement = "An invented synthetic requirement.", StatementDigest = SpecDigest.Text("An invented synthetic requirement.") },
            "unsupported-class" => version with { VerificationClasses = [(SpecVerificationClass)999] },
            "missing-class" => version with { VerificationClasses = [] },
            "duplicate-class" => version with { VerificationClasses = [SpecVerificationClass.ContractTest, SpecVerificationClass.ContractTest] },
            "manual-auto-class" => version with { Severity = SpecSeverity.Manual },
            "source-missing" => version with { Source = version.Source with { Path = "synthetic/missing.md" } },
            "source-digest" => version with { Source = version.Source with { Digest = new('f', 64) } },
            "source-anchor" => version with { Source = version.Source with { Anchor = "missing-anchor" } },
            "path-escape" => version with { Source = version.Source with { Path = "../authority.md" } },
            "missing-retirement-reason" => version with { Status = SpecStatus.Retired },
            "false-retirement" => version with { RetirementReason = "synthetic-reason" },
            "unknown-successor" => version with { Status = SpecStatus.Retired, RetirementReason = "synthetic-reason", SuccessorSpecIds = ["SPEC-AUTH-SYNTHETIC-002"] },
            "wrong-architecture-id" => version with { ArchitectureRuleIds = ["incorrect"] },
            _ => version
        };
        requirement = requirement with { Versions = mutation == "missing-history" ? [] : [version] };
        if (mutation == "family") requirement = requirement with { SpecId = "SPEC-UNKNOWN-SYNTHETIC-001" };
        registry = registry with { Requirements = mutation == "duplicate" ? [requirement, requirement] : [requirement] };
        registry = mutation switch
        {
            "schema" => registry with { SchemaVersion = 2 },
            "empty" => registry with { Requirements = [] },
            "source-revision" => registry with { SpecificationRevision = CandidateSha },
            _ => registry
        };
        var first = await Validate(registry);
        var second = await Validate(registry);
        Assert.False(first.Valid);
        Assert.Equal(JsonSerializer.Serialize(first, ContractJson.Options), JsonSerializer.Serialize(second, ContractJson.Options));
    }

    [Fact]
    public async Task RetiredHistoryIsPreservedAndCannotBeReallocated()
    {
        var baseline = Registry();
        var history = baseline.Requirements[0];
        var retired = history.Versions[0] with { Version = 2, Status = SpecStatus.Retired,
            RetirementReason = "Synthetic requirement retired in pending review.", ChangeReference = "synthetic-retirement-review" };
        var candidate = baseline with { RegistryVersion = 2, Requirements = [history with { Versions = [history.Versions[0], retired] }] };
        Assert.True((await Validate(candidate, baseline)).Valid);
        var reused = candidate with { RegistryVersion = 3,
            Requirements = [history with { Versions = [history.Versions[0], retired, history.Versions[0] with { Version = 3 }] }] };
        Assert.Contains((await Validate(reused, candidate)).Diagnostics, d => d.RuleId == "SPEC_ID_REUSE");
        Assert.Contains((await Validate(baseline with { Requirements = [] }, candidate)).Diagnostics, d => d.RuleId == "SPEC_HISTORY_REMOVED");
    }

    [Theory]
    [InlineData("rewrite-history")]
    [InlineData("statement-replacement")]
    [InlineData("version-not-increased")]
    [InlineData("version-rollback")]
    public async Task AllocationHistoryAndVersionGovernanceCannotBeBypassed(string mutation)
    {
        var baseline = Registry();
        var requirement = baseline.Requirements[0];
        var first = requirement.Versions[0];
        var changed = first with { Version = 2, ChangeReference = "synthetic-metadata-review", KnownLimitation = "Updated synthetic limitation." };
        var candidate = baseline with { RegistryVersion = 2, Requirements = [requirement with { Versions = [first, changed] }] };
        candidate = mutation switch
        {
            "rewrite-history" => candidate with { Requirements = [requirement with { Versions = [first with { Owner = "another-owner" }, changed] }] },
            "statement-replacement" => candidate with { Requirements = [requirement with { Versions = [first,
                changed with { NormativeStatement = "Synthetic weaker requirement.", StatementDigest = SpecDigest.Text("Synthetic weaker requirement.") }] }] },
            "version-not-increased" => candidate with { RegistryVersion = 1 },
            _ => candidate with { RegistryVersion = 0 }
        };
        Assert.False((await Validate(candidate, baseline)).Valid);
    }

    [Fact]
    public async Task TraceabilityReportsSeparateStructuralLinksExecutionAndManualAuthority()
    {
        var registry = Registry();
        var manifest = Manifest(registry);
        var structural = await Trace(registry, manifest);
        Assert.True(structural.Valid);
        Assert.Equal(0, structural.Coverage!.ExecutedPassingLinks);
        Assert.Equal(1, structural.Coverage.UnresolvedLinks);
        var executed = await Trace(registry, manifest, new(1, [Link(manifest)]));
        Assert.True(executed.Valid);
        Assert.False(executed.NormativeReady);
        Assert.Equal(1, executed.Coverage!.ExecutedPassingLinks);
        var manualRegistry = Registry(SpecSeverity.Manual, SpecVerificationClass.Manual);
        var manualManifest = Manifest(manualRegistry);
        var manual = await Trace(manualRegistry, manualManifest, new(1, []));
        Assert.True(manual.Valid);
        Assert.Equal(1, manual.Coverage!.ManualMappings);
        Assert.Equal(0, manual.Coverage.ExecutedPassingLinks);
        Assert.False((await Trace(manualRegistry, manualManifest, new(1, [Link(manualManifest)]))).Valid);
    }

    [Fact]
    public async Task SharedVerifierRetainsDistinctPerObligationExecutionLinks()
    {
        var registry = Registry();
        var manifest = Manifest(registry);
        var first = manifest.Mappings[0];
        var secondContract = Contract() with { ContractId = "SEC-ARCH-SYNTHETIC-SECOND-SCOPE" };
        var second = first with { ContractId = secondContract.ContractId, ContractDigest = SpecDigest.Document(secondContract),
            Verifiers = [first.Verifiers[0] with { EvidenceIds = ["Synthetic.SecondScope.Execution"] }] };
        manifest = manifest with { Mappings = [first, second] };
        var secondLink = Link(manifest) with { EvidenceId = second.Verifiers[0].EvidenceIds[0],
            ContractId = second.ContractId, ContractDigest = second.ContractDigest };
        var result = await SpecTraceabilityValidator.ValidateAsync(registry, manifest, new(1, [Contract(), secondContract]),
            SpecificationSha, CandidateSha, Now, ReadSpecification, ReadImplementation, new(1, [Link(manifest), secondLink]));
        Assert.True(result.Valid);
        Assert.False(result.NormativeReady);
        Assert.Equal(2, result.Coverage!.ExecutedPassingLinks);
        var conflicting = second with { Verifiers = [second.Verifiers[0] with { Version = "2.0" }] };
        var rejected = await SpecTraceabilityValidator.ValidateAsync(registry, manifest with { Mappings = [first, conflicting] },
            new(1, [Contract(), secondContract]), SpecificationSha, CandidateSha, Now, ReadSpecification, ReadImplementation);
        Assert.Contains(rejected.Diagnostics, d => d.RuleId == "SPEC_VERIFIER_COLLISION");
    }

    [Theory]
    [InlineData("manifest-schema")]
    [InlineData("registry-version")]
    [InlineData("registry-digest")]
    [InlineData("missing-mapping")]
    [InlineData("duplicate-mapping")]
    [InlineData("unknown-spec")]
    [InlineData("stale-version")]
    [InlineData("unknown-contract")]
    [InlineData("contract-changed")]
    [InlineData("missing-verifier")]
    [InlineData("disabled-class")]
    [InlineData("missing-verifier-source")]
    [InlineData("renamed-verifier")]
    [InlineData("wrong-verifier-namespace")]
    [InlineData("weak-test-bytes")]
    [InlineData("wrong-verifier-candidate")]
    [InlineData("duplicate-evidence")]
    public async Task MissingStaleAndSemanticallyChangedVerifierBindingsFail(string mutation)
    {
        var registry = Registry(SpecSeverity.Blocking);
        var manifest = Manifest(registry);
        var mapping = manifest.Mappings[0];
        var verifier = mapping.Verifiers[0];
        verifier = mutation switch
        {
            "disabled-class" => verifier with { Class = (SpecVerificationClass)999 },
            "missing-verifier-source" => verifier with { Source = verifier.Source with { Path = "synthetic/Missing.cs" } },
            "renamed-verifier" => verifier with { SourceIdentity = "Synthetic.Tests.ScopeTests.DeletedTest" },
            "wrong-verifier-namespace" => verifier with { SourceIdentity = "Incorrect.Tests.ScopeTests.RejectForeignCaller" },
            "wrong-verifier-candidate" => verifier with { Source = verifier.Source with { Revision = SpecificationSha } },
            "duplicate-evidence" => verifier with { EvidenceIds = [verifier.EvidenceIds[0], verifier.EvidenceIds[0]] },
            _ => verifier
        };
        mapping = mapping with { Verifiers = mutation == "missing-verifier" ? [] : [verifier] };
        mapping = mutation switch
        {
            "unknown-spec" => mapping with { SpecId = "SPEC-AUTH-SYNTHETIC-002" },
            "stale-version" => mapping with { RequirementVersion = 2 },
            "unknown-contract" => mapping with { ContractId = "SEC-ARCH-SYNTHETIC-UNKNOWN" },
            "contract-changed" => mapping with { ContractDigest = new('e', 64) },
            _ => mapping
        };
        manifest = manifest with { Mappings = mutation == "missing-mapping" ? [] : mutation == "duplicate-mapping" ? [mapping, mapping] : [mapping] };
        manifest = mutation switch
        {
            "manifest-schema" => manifest with { SchemaVersion = 2 },
            "registry-version" => manifest with { RegistryVersion = 2 },
            "registry-digest" => manifest with { RegistryDigest = new('e', 64) },
            _ => manifest
        };
        Assert.False((await Trace(registry, manifest, implementation: mutation == "weak-test-bytes"
            ? _ => Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes(VerifierSource.Replace("{ }", "{ /* weakened */ }"))) : null)).Valid);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-candidate")]
    [InlineData("wrong-source")]
    [InlineData("wrong-contract")]
    [InlineData("wrong-version")]
    [InlineData("disabled")]
    [InlineData("skipped")]
    [InlineData("false-not-applicable")]
    [InlineData("missing-reference")]
    [InlineData("duplicate")]
    public async Task ExecutionLinksCannotConflateMissingSkippedOrAnotherCandidateWithPass(string mutation)
    {
        var registry = Registry();
        var manifest = Manifest(registry);
        var link = Link(manifest);
        link = mutation switch
        {
            "wrong-candidate" => link with { CandidateSha = SpecificationSha },
            "wrong-source" => link with { VerifierSourceDigest = new('e', 64) },
            "wrong-contract" => link with { ContractDigest = new('e', 64) },
            "wrong-version" => link with { VerifierVersion = "2.0" },
            "disabled" => link with { VerifierEnabled = false },
            "skipped" => link with { Outcome = EvidenceOutcome.Unverified },
            "false-not-applicable" => link with { Outcome = EvidenceOutcome.NotApplicable },
            "missing-reference" => link with { ExecutionReference = "" },
            _ => link
        };
        var evidence = new SpecTraceabilityEvidenceDocument(1, mutation == "missing" ? [] : mutation == "duplicate" ? [link, link] : [link]);
        var result = await Trace(registry, manifest, evidence);
        Assert.False(result.Valid);
        Assert.InRange(result.Coverage!.UnresolvedLinks, 0, 1);
    }

    [Fact]
    public async Task ExactExplicitAnchorMustExistOnlyOnce()
    {
        var duplicate = Encoding.UTF8.GetBytes(Specification + Specification);
        var registry = Registry();
        var requirement = registry.Requirements[0];
        var version = requirement.Versions[0];
        registry = registry with { Requirements = [requirement with { Versions = [version with { Source = version.Source with { Digest = SpecDigest.Bytes(duplicate) } }] }] };
        var result = await SpecRegistryValidator.ValidateAsync(registry, SpecificationSha, _ => Task.FromResult<byte[]?>(duplicate));
        Assert.Contains(result.Diagnostics, d => d.RuleId == "SPEC_SOURCE_ANCHOR");
    }

    [Fact]
    public async Task CliReadsPinnedGitSourceFromCleanSyntheticRepositoryAndRejectsForgedApprovalField()
    {
        var directory = Path.Combine(Path.GetTempPath(), "coglatas-spec-registry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "synthetic"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "synthetic", "authority.md"), Specification, new UTF8Encoding(false));
            await Git(directory, "init", "--quiet");
            await Git(directory, "add", "synthetic/authority.md");
            await Git(directory, "-c", "user.name=Synthetic Owner", "-c", "user.email=synthetic@example.invalid", "commit", "--quiet", "-m", "Synthetic registry fixture");
            var revision = (await Git(directory, "rev-parse", "HEAD")).Trim();
            var registry = Registry();
            var requirement = registry.Requirements[0];
            var version = requirement.Versions[0];
            registry = registry with { SpecificationRevision = revision,
                Requirements = [requirement with { Versions = [version with { Source = version.Source with { Revision = revision } }] }] };
            var path = Path.Combine(directory, "registry.json");
            var json = JsonSerializer.Serialize(registry, ContractJson.Options);
            await File.WriteAllTextAsync(path, json);
            await using var writer = new StringWriter();
            Assert.Equal(0, await SecurityArchitectureCli.RunAsync(["registry-validate", path, directory, revision], writer));
            Assert.False(ContractJson.Read<SpecValidationResult>(writer.ToString()).NormativeReady);

            await File.WriteAllTextAsync(Path.Combine(directory, "synthetic", "ScopeTests.cs"), VerifierSource, new UTF8Encoding(false));
            await Git(directory, "add", "synthetic/ScopeTests.cs");
            await Git(directory, "-c", "user.name=Synthetic Owner", "-c", "user.email=synthetic@example.invalid", "commit", "--quiet", "-m", "Synthetic verifier source");
            var candidate = (await Git(directory, "rev-parse", "HEAD")).Trim();
            var manifest = Manifest(registry);
            var mapping = manifest.Mappings[0];
            var verifier = mapping.Verifiers[0];
            manifest = manifest with { Mappings = [mapping with { Verifiers = [verifier with { Source = verifier.Source with { Revision = candidate } }] }] };
            var manifestPath = Path.Combine(directory, "manifest.json");
            var contractsPath = Path.Combine(directory, "contracts.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, ContractJson.Options));
            await File.WriteAllTextAsync(contractsPath, JsonSerializer.Serialize(new ContractDocument(1, [Contract()]), ContractJson.Options));
            await using var summaryWriter = new StringWriter();
            Assert.Equal(0, await SecurityArchitectureCli.RunAsync(["traceability-check", path, manifestPath, contractsPath,
                directory, directory, revision, candidate, Now.ToString("O")], summaryWriter));
            var report = ContractJson.Read<SpecValidationResult>(summaryWriter.ToString());
            Assert.False(report.NormativeReady);
            var coverage = Assert.IsType<SpecCoverageSummary>(report.Coverage);
            Assert.Equal(candidate, coverage.CandidateSha);
            Assert.Equal(1, coverage.SchemaVersion);
            Assert.Equal(1, coverage.RegistryVersion);
            Assert.Equal("AUTH", Assert.Single(coverage.ActiveFamilies).Category);
            Assert.Equal(1, coverage.ActiveFamilies[0].Count);
            Assert.Equal("Advisory", Assert.Single(coverage.Severities).Category);
            Assert.Equal(1, coverage.Severities[0].Count);
            Assert.Equal("ContractTest", Assert.Single(coverage.VerificationClasses).Category);
            Assert.Equal(1, coverage.VerificationClasses[0].Count);
            Assert.Equal(1, coverage.ActiveRequirements);
            Assert.Equal(0, coverage.DeprecatedRequirements);
            Assert.Equal(0, coverage.RetiredRequirements);
            Assert.Equal(1, coverage.Mappings);
            Assert.Equal(0, coverage.ManualMappings);
            Assert.Equal(0, coverage.ExecutedPassingLinks);
            Assert.Equal(1, coverage.UnresolvedLinks);
            Assert.Equal(1, coverage.RequirementsWithKnownLimitations);
            Assert.DoesNotContain(Statement, summaryWriter.ToString());
            Assert.DoesNotContain(directory, summaryWriter.ToString());
            Assert.DoesNotContain("synthetic/authority.md", summaryWriter.ToString());
            Assert.DoesNotContain("synthetic-review-pending", summaryWriter.ToString());
            var baselinePath = Path.Combine(directory, "baseline.json");
            await File.WriteAllTextAsync(baselinePath, json);
            await using var transitionWriter = new StringWriter();
            Assert.Equal(0, await SecurityArchitectureCli.RunAsync(["traceability-transition-check", path, manifestPath,
                contractsPath, baselinePath, directory, directory, revision, candidate, Now.ToString("O")], transitionWriter));
            Assert.False(ContractJson.Read<SpecValidationResult>(transitionWriter.ToString()).NormativeReady);
            var changedBaseline = registry with { RegistryVersion = 2 };
            await File.WriteAllTextAsync(baselinePath, JsonSerializer.Serialize(changedBaseline, ContractJson.Options));
            await using var rollbackWriter = new StringWriter();
            Assert.Equal(1, await SecurityArchitectureCli.RunAsync(["traceability-transition-check", path, manifestPath,
                contractsPath, baselinePath, directory, directory, revision, candidate, Now.ToString("O")], rollbackWriter));
            Assert.Contains("SPEC_REGISTRY_ROLLBACK", rollbackWriter.ToString());
            Assert.DoesNotContain(Statement, rollbackWriter.ToString());
            await File.WriteAllTextAsync(path, json.Replace("\"schemaVersion\": 1", "\"ownerApproved\": true, \"schemaVersion\": 1"));
            await using var rejected = new StringWriter();
            Assert.Equal(1, await SecurityArchitectureCli.RunAsync(["registry-validate", path, directory, revision], rejected));
            Assert.DoesNotContain(directory, rejected.ToString());
            Assert.DoesNotContain(Statement, rejected.ToString());
        }
        finally
        {
            // Git marks loose objects read-only on Windows; clear only this unique synthetic fixture's files.
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task OversizeImmutableGitBlobIsRejectedWithoutUnboundedSourceAllocation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "coglatas-spec-oversize-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(directory, "oversize.bin"), new byte[RepositoryArtifactReader.MaximumBlobBytes + 1]);
            await Git(directory, "init", "--quiet");
            await Git(directory, "add", "oversize.bin");
            await Git(directory, "-c", "user.name=Synthetic Owner", "-c", "user.email=synthetic@example.invalid", "commit", "--quiet", "-m", "Synthetic oversize fixture");
            var revision = (await Git(directory, "rev-parse", "HEAD")).Trim();
            Assert.Null(await new RepositoryArtifactReader(directory).ReadAsync(new(revision, "oversize.bin", null, new('b', 64))));
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<string> Git(string directory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = directory, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await error;
        Assert.Equal(0, process.ExitCode);
        return await output;
    }
}
