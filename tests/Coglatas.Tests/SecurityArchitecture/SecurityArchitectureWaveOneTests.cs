using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.SecurityArchitecture;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed class SecurityArchitectureWaveOneTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-16T00:00:00Z", CultureInfo.InvariantCulture);
    private static readonly string Sha = new('a', 40);
    private static readonly string Digest = new('b', 64);

    private static FlowContract Contract() => new("SEC-ARCH-SYNTHETIC-WAVE1", ["SPEC-AUTH-SYNTHETIC-001"],
        "synthetic-owner", ContractType.Service, "synthetic-caller", "synthetic-service", "synthetic-http",
        "synthetic-principal", "read", "synthetic-tenant", "synthetic-resource", "synthetic-boundary",
        AccessDecision.Deny, "synthetic-deny", [EvidenceClass.Runtime], ActivationState.Active,
        ["SYNTHETIC-STRIDE-001"], Service: new("synthetic-audience", ["synthetic.read"],
            "synthetic-service", true, "synthetic-edge"));

    [Theory]
    [InlineData("principal", "PRINCIPAL_CHANGED")]
    [InlineData("requirement", "REQUIREMENT_REFERENCES_CHANGED")]
    [InlineData("evidence", "EVIDENCE_REQUIREMENTS_CHANGED")]
    [InlineData("activation", "ACTIVATION_CHANGED")]
    [InlineData("policy", "POLICY_CHANGED")]
    [InlineData("scope-addition", "SCOPE_EXPANDED")]
    [InlineData("scope-removal", "SCOPE_NARROWED")]
    [InlineData("resource", "SCOPE_CHANGED")]
    [InlineData("approval", "APPROVAL_CHANGED")]
    [InlineData("exception", "EXCEPTION_CHANGED")]
    public void InventoryChangesExposeSpecificOwnerReviewDiagnostics(string mutation, string expectedRule)
    {
        var contract = Contract();
        if (mutation == "scope-removal")
            contract = contract with { Service = contract.Service! with { RequiredScopes = ["synthetic.read", "synthetic.write"] } };
        var approval = new OwnerApproval("synthetic-owner", "synthetic-approval-reference", Digest, Now.AddDays(-1));
        var changed = mutation switch
        {
            "principal" => contract with { Principal = "synthetic-other-principal" },
            "requirement" => contract with { SpecIds = [.. contract.SpecIds, "SPEC-AUTH-SYNTHETIC-002"] },
            "evidence" => contract with { RequiredEvidence = [EvidenceClass.Runtime, EvidenceClass.Static] },
            "activation" => contract with { ActivationState = ActivationState.Retired },
            "policy" => contract with { ExpectedDecision = AccessDecision.Allow },
            "scope-addition" => contract with { Service = contract.Service! with { RequiredScopes = ["synthetic.read", "synthetic.write"] } },
            "scope-removal" => contract with { Service = contract.Service! with { RequiredScopes = ["synthetic.read"] } },
            "resource" => contract with { ResourceScope = "synthetic-other-resource" },
            "approval" => contract with { ActivationState = ActivationState.Conditional,
                ActivationApproval = new("synthetic-inactive-product", approval) },
            "exception" => contract with { Exception = new("synthetic-exception", contract.ContractId,
                contract.ResourceScope, "synthetic-temporary-exception", Now.AddHours(-1), Now.AddDays(1), approval) },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        var result = InventoryDiff.Compare(new(1, [contract]), new(1, [changed]), Now);
        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.RuleId == expectedRule);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.RuleId == "CONTRACT_CHANGED");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.RuleId == "POLICY_CHANGED");
        Assert.Equal(JsonSerializer.Serialize(result, ContractJson.Options),
            JsonSerializer.Serialize(InventoryDiff.Compare(new(1, [contract]), new(1, [changed]), Now), ContractJson.Options));
    }

    [Fact]
    public async Task FabricatedRuntimePassRemainsStructurallyValidAndSecurityUnverified()
    {
        var contract = Contract();
        var evidence = new EvidenceRecord(contract.ContractId, contract.SpecIds, "Synthetic.Fabricated.Verifier", "1.0",
            Sha, Digest, contract.ExpectedPolicy, contract.ExpectedPolicy, EvidenceClass.Runtime, EvidenceOutcome.Pass,
            "synthetic-identity", Now.AddMinutes(-1), "SYNTHETIC-FINDING", null, null, null,
            "synthetic-fabricated-execution", Digest, true, 1, 1);
        await WithFiles(async (first, second) =>
        {
            await File.WriteAllTextAsync(first, JsonSerializer.Serialize(new ContractDocument(1, [contract]), ContractJson.Options));
            await File.WriteAllTextAsync(second, JsonSerializer.Serialize(new EvidenceDocument(1, [evidence]), ContractJson.Options));
            var contractBytes = await File.ReadAllBytesAsync(first);
            var evidenceBytes = await File.ReadAllBytesAsync(second);
            await using var output = new StringWriter(CultureInfo.InvariantCulture);
            Assert.Equal(0, await SecurityArchitectureCli.RunAsync(["evidence-check", first, second, Sha, Digest, Now.ToString("O")], output));
            using var result = JsonDocument.Parse(output.ToString());
            Assert.True(result.RootElement.GetProperty("valid").GetBoolean());
            Assert.Equal("STRUCTURALLY_VALID", result.RootElement.GetProperty("status").GetString());
            foreach (var field in new[] { "approvalStatus", "executionAttestationStatus", "runtimeSecurityStatus" })
                Assert.Equal("UNVERIFIED", result.RootElement.GetProperty(field).GetString());
            Assert.Equal(contractBytes, await File.ReadAllBytesAsync(first));
            Assert.Equal(evidenceBytes, await File.ReadAllBytesAsync(second));
        });
    }

    [Theory]
    [InlineData("2026-10-16")]
    [InlineData("2026-10-16T00:00:00")]
    [InlineData("2026-10-16T09:00:00+09:00")]
    [InlineData("2026-10-16T00:00:00-00:00")]
    [InlineData("not-a-time")]
    public async Task CliRejectsImplicitOrNonUtcValidationTime(string time)
    {
        await WithFiles(async (first, _) =>
        {
            await File.WriteAllTextAsync(first, JsonSerializer.Serialize(new ContractDocument(1, [Contract()]), ContractJson.Options));
            await using var output = new StringWriter(CultureInfo.InvariantCulture);
            Assert.Equal(1, await SecurityArchitectureCli.RunAsync(["validate", first, time], output));
            Assert.Contains("INPUT_ERROR", output.ToString());
            Assert.DoesNotContain(first, output.ToString());
        });
    }

    [Theory]
    [InlineData("registry-validate")]
    [InlineData("inventory-diff")]
    public async Task InvalidRepositoryRootsProduceSanitizedCliDiagnostics(string command)
    {
        await WithFiles(async (registryPath, contractsPath) =>
        {
            const string statement = "Synthetic private requirement must not appear in diagnostics.";
            var registry = new SpecRegistryDocument(1, 1, Sha, [new(Contract().SpecIds[0],
                [new(1, SpecStatus.Active, new(Sha, "synthetic/authority.md", null, SpecDigest.Text(statement)),
                    statement, SpecDigest.Text(statement), "synthetic-owner", SpecSeverity.Advisory,
                    [SpecVerificationClass.ContractTest], "Synthetic fixture only.", [], "synthetic-review")])]);
            await File.WriteAllTextAsync(registryPath, JsonSerializer.Serialize(registry, ContractJson.Options));
            await File.WriteAllTextAsync(contractsPath, JsonSerializer.Serialize(new ContractDocument(1, [Contract()]), ContractJson.Options));
            var missingRoot = Path.Combine(Path.GetDirectoryName(registryPath)!, "synthetic-missing-private-spec-root");
            Assert.False(Directory.Exists(missingRoot));
            string[] arguments = command == "registry-validate"
                ? [command, registryPath, missingRoot, Sha]
                : [command, contractsPath, contractsPath, Now.ToString("O"), registryPath, registryPath, missingRoot, Sha];
            await using var output = new StringWriter(CultureInfo.InvariantCulture);
            Assert.Equal(1, await SecurityArchitectureCli.RunAsync(arguments, output));
            Assert.Contains("INPUT_ERROR", output.ToString());
            Assert.DoesNotContain(missingRoot, output.ToString());
            Assert.DoesNotContain(registryPath, output.ToString());
            Assert.DoesNotContain(statement, output.ToString());
        });
    }

    [Theory]
    [InlineData("approval", "2026-10-16T00:00:00")]
    [InlineData("approval", "2026-10-16T09:00:00+09:00")]
    [InlineData("execution", "2026-10-16T00:00:00")]
    [InlineData("execution", "2026-10-16T09:00:00+09:00")]
    [InlineData("optional-expiry", "2026-10-16T00:00:00")]
    [InlineData("optional-expiry", "2026-10-16T09:00:00+09:00")]
    [InlineData("exception", "2026-10-16T00:00:00")]
    [InlineData("exception", "2026-10-16T09:00:00+09:00")]
    public void ContractJsonRejectsHostDependentAndNonUtcTimestamps(string shape, string timestamp)
    {
        var document = TimestampDocument(shape);
        var json = JsonNode.Parse(JsonSerializer.Serialize(document, document.GetType(), ContractJson.Options))!;
        var property = shape switch
        {
            "approval" => "approvedAtUtc",
            "execution" => "executedAtUtc",
            "optional-expiry" => "exceptionExpiresAtUtc",
            _ => "startsAtUtc"
        };
        json[property] = timestamp;
        Assert.Throws<JsonException>(() => ReadTimestampDocument(shape, json.ToJsonString()));
    }

    [Fact]
    public void ExplicitUtcJsonPreservesDefaultTimestampSerializationAndNullableValues()
    {
        var originalWriter = new JsonSerializerOptions(ContractJson.Options);
        for (var index = originalWriter.Converters.Count - 1; index >= 0; index--)
            if (originalWriter.Converters[index].CanConvert(typeof(DateTimeOffset)))
                originalWriter.Converters.RemoveAt(index);
        foreach (var shape in new[] { "approval", "execution", "optional-expiry", "exception" })
        {
            var document = TimestampDocument(shape);
            var json = JsonSerializer.Serialize(document, document.GetType(), ContractJson.Options);
            Assert.Equal(JsonSerializer.Serialize(document, document.GetType(), originalWriter), json);
            Assert.Equal(json, JsonSerializer.Serialize(ReadTimestampDocument(shape, json), document.GetType(), ContractJson.Options));
            var utcZulu = json.Replace("+00:00\"", "Z\"");
            Assert.Equal(json, JsonSerializer.Serialize(ReadTimestampDocument(shape, utcZulu), document.GetType(), ContractJson.Options));
        }
        var nullable = (EvidenceRecord)TimestampDocument("optional-expiry") with { ExceptionExpiresAtUtc = null };
        var nullableJson = JsonSerializer.Serialize(nullable, ContractJson.Options);
        Assert.Null(ContractJson.Read<EvidenceRecord>(nullableJson).ExceptionExpiresAtUtc);
        Assert.Equal(JsonSerializer.Serialize(nullable, originalWriter), nullableJson);
    }

    private static object TimestampDocument(string shape)
    {
        var approval = new OwnerApproval("synthetic-owner", "synthetic-review", Digest, Now);
        if (shape == "approval") return approval;
        if (shape == "exception") return new ContractException("synthetic-exception", Contract().ContractId,
            Contract().ResourceScope, "synthetic-reason", Now, Now.AddDays(1), approval);
        var contract = Contract();
        return new EvidenceRecord(contract.ContractId, contract.SpecIds, "Synthetic.Verifier", "1.0", Sha, Digest,
            contract.ExpectedPolicy, contract.ExpectedPolicy, EvidenceClass.Runtime, EvidenceOutcome.Pass,
            "synthetic-identity", Now, "SYNTHETIC-FINDING", null, null,
            shape == "optional-expiry" ? Now : null, "synthetic-execution", Digest, true, 1, 1);
    }

    private static object ReadTimestampDocument(string shape, string json) => shape switch
    {
        "approval" => ContractJson.Read<OwnerApproval>(json),
        "exception" => ContractJson.Read<ContractException>(json),
        _ => ContractJson.Read<EvidenceRecord>(json)
    };

    [Theory]
    [InlineData("contract-id")]
    [InlineData("spec-id")]
    [InlineData("service-audience")]
    public void StableIdentityAndTypedAudienceCannotHideUnboundedSuffixes(string mutation)
    {
        var contract = Contract();
        contract = mutation switch
        {
            "contract-id" => contract with { ContractId = contract.ContractId + "\n" },
            "spec-id" => contract with { SpecIds = [contract.SpecIds[0] + "\n"] },
            _ => contract with { Service = contract.Service! with { Audience = "*" } }
        };
        Assert.False(ContractValidator.Validate(new(1, [contract]), Now).Valid);
    }

    [Fact]
    public void ActiveContractCannotHideMalformedOptionalApprovalReference()
    {
        var contract = Contract() with { ActivationApproval = new("synthetic-reason",
            new("synthetic-owner", "", Digest, Now.AddDays(-1))) };
        var result = ContractValidator.Validate(new(1, [contract]), Now);
        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.RuleId == "OWNER_APPROVAL");
    }

    [Fact]
    public void OptionalMitigationReferencesPreserveLegacySerialization()
    {
        var contract = Contract();
        var legacyJson = JsonSerializer.Serialize(contract, ContractJson.Options);
        Assert.DoesNotContain("mitigationReferences", legacyJson);
        var legacyDigest = SpecDigest.Document(contract);
        var restored = ContractJson.Read<FlowContract>(legacyJson);
        Assert.Null(restored.MitigationReferences);
        Assert.Equal(legacyDigest, SpecDigest.Document(restored));
        Assert.Equal(legacyJson, JsonSerializer.Serialize(restored with { MitigationReferences = null }, ContractJson.Options));
        var explicitReference = contract with { MitigationReferences = ["SYNTHETIC-MITIGATION-001"] };
        Assert.True(ContractValidator.Validate(new(1, [explicitReference]), Now).Valid);
        Assert.NotEqual(legacyDigest, SpecDigest.Document(explicitReference));
        Assert.Contains("SYNTHETIC-MITIGATION-001", JsonSerializer.Serialize(explicitReference, ContractJson.Options));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("blank")]
    [InlineData("duplicate")]
    [InlineData("wildcard")]
    [InlineData("untrimmed")]
    [InlineData("oversized")]
    public void InvalidMitigationReferencesCannotValidate(string mutation)
    {
        string[] references = mutation switch
        {
            "empty" => [],
            "blank" => [""],
            "duplicate" => ["SYNTHETIC-MITIGATION-001", "SYNTHETIC-MITIGATION-001"],
            "wildcard" => ["SYNTHETIC-MITIGATION-*"],
            "untrimmed" => [" SYNTHETIC-MITIGATION-001 "],
            "oversized" => [new string('M', 513)],
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        var result = ContractValidator.Validate(new(1, [Contract() with { MitigationReferences = references }]), Now);
        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.RuleId == "MITIGATION_REFERENCES");
    }

    [Fact]
    public void MitigationReferenceChangesRequireExplicitInventoryReview()
    {
        var contract = Contract() with { MitigationReferences = ["SYNTHETIC-MITIGATION-001"] };
        var changed = contract with { MitigationReferences = ["SYNTHETIC-MITIGATION-002"] };
        var result = InventoryDiff.Compare(new(1, [contract]), new(1, [changed]), Now);
        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.RuleId == "MITIGATION_REFERENCES_CHANGED");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.RuleId == "CONTRACT_CHANGED");
    }

    [Fact]
    public void NonFlagsEnumCannotAcceptCompositeNamesAsAnotherDefinedValue()
    {
        var json = JsonSerializer.Serialize(new ContractDocument(1, [Contract()]), ContractJson.Options)
            .Replace("\"type\": \"Service\"", "\"type\": \"Rls, SignalR\"");
        Assert.Throws<JsonException>(() => ContractJson.Read<ContractDocument>(json));
    }

    [Theory]
    [InlineData("MSG", true)]
    [InlineData("SEARCH", true)]
    [InlineData("UNKNOWN", false)]
    public async Task RegisteredFamiliesHaveBoundedSyntheticContractAndRegistrySupport(string family, bool valid)
    {
        var identity = $"SPEC-{family}-SYNTHETIC-001";
        var contract = Contract() with { SpecIds = [identity] };
        Assert.Equal(valid, ContractValidator.Validate(new(1, [contract]), Now).Valid);
        const string statement = "Synthetic foreign resource access must be rejected.";
        var registry = new SpecRegistryDocument(1, 1, Sha, [new(identity,
            [new(1, SpecStatus.Active, new(Sha, "synthetic/authority.md", null, SpecDigest.Text(statement)),
                statement, SpecDigest.Text(statement), "synthetic-owner", SpecSeverity.Advisory,
                [SpecVerificationClass.ContractTest], "Synthetic fixture; no product assurance.", [], "synthetic-review")])]);
        var result = await SpecRegistryValidator.ValidateAsync(registry, Sha,
            _ => Task.FromResult<byte[]?>(System.Text.Encoding.UTF8.GetBytes(statement)));
        Assert.Equal(valid, result.Valid);
        Assert.False(result.NormativeReady);
        Assert.Equal("UNVERIFIED", result.ApprovalStatus);
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("statement-digest")]
    [InlineData("source-digest")]
    [InlineData("spec-id")]
    public async Task RegistryIdentityAndDigestPatternsRejectTrailingNewlines(string mutation)
    {
        const string statement = "Synthetic immutable requirement.";
        var source = new SpecSource(Sha, "synthetic/authority.md", null, SpecDigest.Text(statement));
        if (mutation == "source-digest") source = source with { Digest = source.Digest + "\n" };
        var requirement = new SpecRequirement("SPEC-AUTH-SYNTHETIC-001" + (mutation == "spec-id" ? "\n" : ""),
            [new(1, SpecStatus.Active, source, statement,
                SpecDigest.Text(statement) + (mutation == "statement-digest" ? "\n" : ""), "synthetic-owner",
                SpecSeverity.Advisory, [SpecVerificationClass.ContractTest], "Synthetic fixture only.", [], "synthetic-review")]);
        var registry = new SpecRegistryDocument(1, 1, Sha + (mutation == "revision" ? "\n" : ""), [requirement]);
        var result = await SpecRegistryValidator.ValidateAsync(registry, Sha,
            _ => Task.FromResult<byte[]?>(System.Text.Encoding.UTF8.GetBytes(statement)));
        Assert.False(result.Valid);
    }

    [Theory]
    [InlineData("unchanged")]
    [InlineData("append")]
    [InlineData("rewrite")]
    [InlineData("unknown-contract-spec")]
    [InlineData("invalid-baseline")]
    public async Task HistoryAwareInventoryRetainsBaselineAndRequiresReviewForAppend(string mutation)
    {
        const string statement = "Synthetic retained requirement.";
        var version = new SpecRequirementVersion(1, SpecStatus.Active,
            new(Sha, "synthetic/authority.md", null, SpecDigest.Text(statement)), statement, SpecDigest.Text(statement),
            "synthetic-owner", SpecSeverity.Advisory, [SpecVerificationClass.ContractTest],
            "Synthetic fixture only.", [], "synthetic-review");
        var requirement = new SpecRequirement(Contract().SpecIds[0], [version]);
        var baseline = new SpecRegistryDocument(1, 1, Sha, [requirement]);
        var candidate = mutation is "unchanged" or "unknown-contract-spec" or "invalid-baseline" ? baseline : baseline with
        {
            RegistryVersion = 2, Requirements = [requirement with
            {
                Versions = [mutation == "rewrite" ? version with { Owner = "synthetic-replacement-owner" } : version,
                    version with { Version = 2, KnownLimitation = "Updated synthetic limitation.", ChangeReference = "synthetic-second-review" }]
            }]
        };
        if (mutation == "invalid-baseline")
            baseline = baseline with { Requirements = [requirement with { Versions = [] }] };
        var contract = mutation == "unknown-contract-spec"
            ? Contract() with { SpecIds = ["SPEC-AUTH-SYNTHETIC-UNKNOWN"] } : Contract();
        var baselineDigest = SpecDigest.Document(baseline);
        var result = await InventoryDiff.CompareWithHistoryAsync(new(1, [contract]), new(1, [contract]), Now,
            baseline, candidate, Sha, _ => Task.FromResult<byte[]?>(System.Text.Encoding.UTF8.GetBytes(statement)));
        Assert.Equal(mutation == "unchanged", result.Valid);
        if (mutation == "append")
        {
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.RuleId == "HISTORY_CHANGED");
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.RuleId == "REGISTRY_CHANGED");
        }
        if (mutation == "rewrite")
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.RuleId == "SPEC_HISTORY_REWRITTEN");
        if (mutation == "invalid-baseline")
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.RuleId == "SPEC_BASELINE_HISTORY");
        Assert.Equal(baselineDigest, SpecDigest.Document(baseline));
    }

    [Theory]
    [InlineData("candidate")]
    [InlineData("environment")]
    [InlineData("execution-digest")]
    public void EvidenceDigestAndCandidateMustMatchTheEntireValue(string mutation)
    {
        var contract = Contract();
        var evidence = new EvidenceRecord(contract.ContractId, contract.SpecIds, "Synthetic.Verifier", "1.0",
            Sha, Digest, contract.ExpectedPolicy, contract.ExpectedPolicy, EvidenceClass.Runtime, EvidenceOutcome.Pass,
            "synthetic-identity", Now.AddMinutes(-1), "SYNTHETIC-FINDING", null, null, null,
            "synthetic-execution", mutation == "execution-digest" ? Digest + "\n" : Digest, true, 1, 1);
        Assert.False(EvidenceValidator.Check(new(1, [contract]), new(1, [evidence]),
            mutation == "candidate" ? Sha + "\n" : Sha,
            mutation == "environment" ? Digest + "\n" : Digest, Now).Valid);
    }

    [Theory]
    [InlineData("scope-replacement")]
    [InlineData("scope-shrinkage")]
    [InlineData("contract-digest")]
    public void PinnedEvidenceCannotSurviveChangedContractScope(string mutation)
    {
        var contract = Contract() with { Service = Contract().Service! with { RequiredScopes = ["synthetic.read", "synthetic.write"] } };
        var evidence = new EvidenceRecord(contract.ContractId, contract.SpecIds, "Synthetic.Verifier", "1.0",
            Sha, Digest, contract.ExpectedPolicy, contract.ExpectedPolicy, EvidenceClass.Runtime, EvidenceOutcome.Pass,
            "synthetic-identity", Now.AddMinutes(-1), "SYNTHETIC-FINDING", null, null, null,
            "synthetic-execution", Digest, true, 1, 1, SpecDigest.Document(contract));
        Assert.True(EvidenceValidator.Check(new(1, [contract]), new(1, [evidence]), Sha, Digest, Now).Valid);
        if (mutation == "scope-replacement") contract = contract with { ResourceScope = "synthetic-other-resource" };
        if (mutation == "scope-shrinkage") contract = contract with { Service = contract.Service! with { RequiredScopes = ["synthetic.read"] } };
        if (mutation == "contract-digest") evidence = evidence with { ContractDigest = new string('c', 64) };
        var result = EvidenceValidator.Check(new(1, [contract]), new(1, [evidence]), Sha, Digest, Now);
        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.RuleId == "EVIDENCE_CONTRACT_DIGEST");
    }

    private static async Task WithFiles(Func<string, string, Task> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "coglatas-wave1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { await action(Path.Combine(directory, "contracts.json"), Path.Combine(directory, "evidence.json")); }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
