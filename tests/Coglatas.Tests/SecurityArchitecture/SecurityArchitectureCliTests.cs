using System.Text.Json;
using Coglatas.SecurityArchitecture;

namespace Coglatas.Tests.SecurityArchitecture;

public sealed class SecurityArchitectureCliTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-09T10:00:00Z");
    private static readonly string Sha = new('a', 40);
    private static readonly string Digest = new('b', 64);

    internal static FlowContract Contract(ContractType type = ContractType.Api) => new(
        "SEC-ARCH-SYNTHETIC-" + type.ToString().ToUpperInvariant(), ["SPEC-AUTH-SYNTHETIC-001"],
        "synthetic-test-owner", type, "synthetic-caller", "synthetic-callee", "synthetic-protocol",
        "synthetic-alpha", "read", "alpha", "alpha-resource", "synthetic-boundary",
        AccessDecision.Deny, "synthetic-policy-v1", [EvidenceClass.Runtime],
        ActivationState.Active, ["SYNTHETIC-STRIDE-001"],
        Api: type == ContractType.Api ? new("/synthetic/resource", "GET", false, "synthetic-policy") : null,
        Rls: type == ContractType.Rls ? new("synthetic", "records", "tenant", "synthetic_app",
            "SELECT", "transaction-local", "tenant-match", "", true, true) : null,
        SignalR: type == ContractType.SignalR ? new("/synthetic/hub", "Subscribe", "Changed", "alpha-resource") : null,
        Kafka: type == ContractType.Kafka ? new("TOPIC", "synthetic-alpha", "LITERAL", "synthetic-host",
            "READ", false, false) : null,
        Service: type == ContractType.Service ? new("synthetic-service", ["synthetic.read"],
            "synthetic-service", true, "synthetic-exact-edge") : null);

    private static EvidenceRecord Evidence(FlowContract contract) => new(
        contract.ContractId, contract.SpecIds, "SYNTHETIC-VERIFIER", "1.0", Sha, Digest,
        contract.ExpectedPolicy, contract.ExpectedPolicy, EvidenceClass.Runtime, EvidenceOutcome.Pass,
        "synthetic-alpha", Now.AddMinutes(-1), "SYNTHETIC-FINDING", null, null, null,
        "synthetic-execution", Digest, true, 1, 1);

    private static OwnerApproval Approval() => new("synthetic-owner", "synthetic-approval-receipt", Digest, Now.AddDays(-1));

    private static Task<(int Exit, string Output)> RunAsync(string command,
        ContractDocument contracts, object? second = null, string? sha = null)
    {
        return WithFilesAsync(async paths =>
        {
            await File.WriteAllTextAsync(paths[0], JsonSerializer.Serialize(contracts, ContractJson.Options));
            if (second is not null)
                await File.WriteAllTextAsync(paths[1], JsonSerializer.Serialize(second, ContractJson.Options));
            var time = Now.ToString("O");
            var args = command switch
            {
                "validate" => new[] { command, paths[0], time },
                "inventory-diff" => [command, paths[0], paths[1], time],
                _ => [command, paths[0], paths[1], sha ?? Sha, Digest, time]
            };
            using var writer = new StringWriter();
            var exit = await SecurityArchitectureCli.RunAsync(args, writer);
            return (exit, writer.ToString());
        });
    }

    private static async Task<T> WithFilesAsync<T>(Func<string[], Task<T>> scenario)
    {
        var directory = Path.Combine(Path.GetTempPath(), "coglatas-sec-arch-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { return await scenario([Path.Combine(directory, "first.json"), Path.Combine(directory, "second.json")]); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(ContractType.Api)]
    [InlineData(ContractType.Rls)]
    [InlineData(ContractType.SignalR)]
    [InlineData(ContractType.Kafka)]
    [InlineData(ContractType.Service)]
    public async Task AllTypedSyntheticContractsValidateWithoutServices(ContractType type)
    {
        Assert.Equal(0, (await RunAsync("validate", new(1, [Contract(type)]))).Exit);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("wildcard")]
    [InlineData("missing-owner")]
    [InlineData("missing-spec")]
    [InlineData("unknown-type")]
    [InlineData("wrong-policy-type")]
    [InlineData("missing-evidence")]
    [InlineData("missing-threat")]
    [InlineData("unapproved-conditional")]
    [InlineData("expired-exception")]
    [InlineData("long-exception")]
    [InlineData("unapproved-exception")]
    [InlineData("wrong-exception-scope")]
    [InlineData("missing-rls")]
    [InlineData("bypass-kafka")]
    [InlineData("fallback-kafka")]
    [InlineData("wildcard-kafka")]
    [InlineData("missing-tls")]
    public async Task InvalidContractsAreRejectedThroughCli(string mutation)
    {
        if (mutation == "unknown-type")
        {
            await WithFilesAsync(async paths =>
            {
                var json = JsonSerializer.Serialize(new ContractDocument(1, [Contract()]), ContractJson.Options)
                    .Replace("\"type\": \"Api\"", "\"type\": \"UnknownContract\"");
                await File.WriteAllTextAsync(paths[0], json);
                using var writer = new StringWriter();
                Assert.Equal(1, await SecurityArchitectureCli.RunAsync(["validate", paths[0], Now.ToString("O")], writer));
                return true;
            });
            return;
        }
        var c = Contract();
        var exception = new ContractException("synthetic-exception", c.ContractId, c.ResourceScope,
            "synthetic-reason", Now.AddHours(-1), Now.AddDays(1), Approval());
        c = mutation switch
        {
            "wildcard" => c with { ResourceScope = "*" },
            "missing-owner" => c with { Owner = "" },
            "missing-spec" => c with { SpecIds = [] },
            "unknown-type" => c with { Type = (ContractType)999 },
            "wrong-policy-type" => c with { Type = ContractType.Rls },
            "missing-evidence" => c with { RequiredEvidence = [] },
            "missing-threat" => c with { ThreatReferences = [] },
            "unapproved-conditional" => c with { ActivationState = ActivationState.Conditional },
            "expired-exception" => c with { Exception = exception with { ExpiresAtUtc = Now.AddMinutes(-1) } },
            "long-exception" => c with { Exception = exception with { ExpiresAtUtc = Now.AddDays(31) } },
            "unapproved-exception" => c with { Exception = exception with { Approval = Approval() with { Reference = "" } } },
            "wrong-exception-scope" => c with { Exception = exception with { Scope = "beta" } },
            "missing-rls" => Contract(ContractType.Rls) with { Rls = null },
            "bypass-kafka" => Contract(ContractType.Kafka) with { Kafka = Contract(ContractType.Kafka).Kafka! with { Superuser = true } },
            "fallback-kafka" => Contract(ContractType.Kafka) with { Kafka = Contract(ContractType.Kafka).Kafka! with { AllowEveryoneIfNoAcl = true } },
            "wildcard-kafka" => Contract(ContractType.Kafka) with { Kafka = Contract(ContractType.Kafka).Kafka! with { Resource = "*" } },
            "missing-tls" => Contract(ContractType.Service) with { Service = Contract(ContractType.Service).Service! with { RequireTls = false } },
            _ => c
        };
        var document = new ContractDocument(1, mutation == "duplicate" ? [c, c] : [c]);
        var result = await RunAsync("validate", document);
        Assert.Equal(1, result.Exit);
        Assert.False(ContractJson.Read<ValidationResult>(result.Output).Valid);
    }

    [Theory]
    [InlineData("unknown-field")]
    [InlineData("manual-pass-field")]
    [InlineData("duplicate-property")]
    [InlineData("missing-state")]
    [InlineData("unsupported-version")]
    [InlineData("null-owner")]
    [InlineData("null-contract-array")]
    [InlineData("null-contract-element")]
    [InlineData("null-spec-element")]
    public async Task MalformedAndManualPassDocumentsCannotValidate(string mutation)
    {
        await WithFilesAsync(async paths =>
        {
            var json = JsonSerializer.Serialize(new ContractDocument(1, [Contract()]), ContractJson.Options);
            json = mutation switch
            {
                "unknown-field" => json.Replace("\"schemaVersion\": 1", "\"unknown\": true, \"schemaVersion\": 1"),
                "manual-pass-field" => json.Replace("\"schemaVersion\": 1", "\"verificationStatus\": \"PASS\", \"schemaVersion\": 1"),
                "duplicate-property" => json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1"),
                "missing-state" => json.Replace("\"activationState\": \"Active\",", ""),
                "null-owner" => json.Replace("\"owner\": \"synthetic-test-owner\"", "\"owner\": null"),
                "null-contract-array" => "{\"schemaVersion\":1,\"contracts\":null}",
                "null-contract-element" => "{\"schemaVersion\":1,\"contracts\":[null]}",
                "null-spec-element" => json.Replace("\"SPEC-AUTH-SYNTHETIC-001\"", "null"),
                _ => json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2")
            };
            await File.WriteAllTextAsync(paths[0], json);
            using var writer = new StringWriter();
            Assert.Equal(1, await SecurityArchitectureCli.RunAsync(["validate", paths[0], Now.ToString("O")], writer));
            return true;
        });
    }

    [Fact]
    public void OutcomeWireNamesRemainCanonicalUppercase()
    {
        var expected = new[] { "PASS", "FAIL", "UNVERIFIED", "NOT_APPLICABLE", "ERROR" };
        Assert.Equal(expected, Enum.GetValues<EvidenceOutcome>()
            .Select(outcome => JsonSerializer.Serialize(outcome, ContractJson.Options).Trim('"')));
    }

    [Fact]
    public async Task InventoryDiffAcceptsSameScopeAndDeterministicallyRejectsRelaxation()
    {
        var c = Contract();
        var baseline = new ContractDocument(1, [c]);
        Assert.Equal(0, (await RunAsync("inventory-diff", baseline, baseline)).Exit);
        var candidate = new ContractDocument(1, [c with { ExpectedDecision = AccessDecision.Allow }]);
        var first = await RunAsync("inventory-diff", baseline, candidate);
        var second = await RunAsync("inventory-diff", baseline, candidate);
        Assert.Equal(1, first.Exit);
        Assert.Equal(first.Output, second.Output);
        Assert.Contains("POLICY_CHANGED", first.Output);
    }

    [Fact]
    public async Task InventoryDiffRejectsRemovedAndUnclassifiedSurfaces()
    {
        var c = Contract();
        var baseline = new ContractDocument(1, [c, Contract(ContractType.Rls)]);
        var result = await RunAsync("inventory-diff", baseline,
            new ContractDocument(1, [c, Contract(ContractType.Service)]));
        Assert.Equal(1, result.Exit);
        Assert.Contains("SCOPE_REMOVED", result.Output);
        Assert.Contains("UNCLASSIFIED_ADDITION", result.Output);
    }

    [Fact]
    public async Task EvidenceCheckAcceptsExecutedSyntheticControlsOnExactCandidate()
    {
        var c = Contract();
        Assert.Equal(0, (await RunAsync("evidence-check", new(1, [c]), new EvidenceDocument(1, [Evidence(c)]))).Exit);
    }

    [Theory]
    [InlineData("wrong-sha")]
    [InlineData("wrong-environment")]
    [InlineData("stale")]
    [InlineData("future")]
    [InlineData("disabled")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("unverified")]
    [InlineData("error")]
    [InlineData("failed")]
    [InlineData("false-not-applicable")]
    [InlineData("missing-positive")]
    [InlineData("missing-negative")]
    [InlineData("wrong-spec")]
    [InlineData("wrong-policy")]
    [InlineData("wrong-observed-policy")]
    [InlineData("wrong-exception")]
    [InlineData("missing-provenance")]
    public async Task EvidenceMutationsCannotCreateFalseGreen(string mutation)
    {
        var c = Contract();
        var e = Evidence(c);
        e = mutation switch
        {
            "wrong-sha" => e with { CandidateSha = new string('c', 40) },
            "wrong-environment" => e with { EnvironmentFingerprint = new string('c', 64) },
            "stale" => e with { ExecutedAtUtc = Now.AddDays(-2) },
            "future" => e with { ExecutedAtUtc = Now.AddDays(1) },
            "disabled" => e with { VerifierEnabled = false },
            "unverified" => e with { Outcome = EvidenceOutcome.Unverified },
            "error" => e with { Outcome = EvidenceOutcome.Error },
            "failed" => e with { Outcome = EvidenceOutcome.Fail },
            "false-not-applicable" => e with { Outcome = EvidenceOutcome.NotApplicable, MissingReason = "inactive-product" },
            "missing-positive" => e with { PositiveControlCount = 0 },
            "missing-negative" => e with { NegativeControlCount = 0 },
            "wrong-spec" => e with { SpecIds = ["SPEC-AUTH-SYNTHETIC-002"] },
            "wrong-policy" => e with { ExpectedPolicy = "different-policy" },
            "wrong-observed-policy" => e with { ObservedPolicy = "different-policy" },
            "wrong-exception" => e with { ExceptionId = "unapproved" },
            "missing-provenance" => e with { ExecutionDigest = "" },
            _ => e
        };
        EvidenceRecord[] records = mutation == "missing" ? [] : mutation == "duplicate" ? [e, e] : [e];
        Assert.Equal(1, (await RunAsync("evidence-check", new(1, [c]), new EvidenceDocument(1, records))).Exit);
    }

    [Fact]
    public async Task ConditionalProductApplicabilityDoesNotExemptIsolatedRuntime()
    {
        var c = Contract() with { ActivationState = ActivationState.Conditional,
            ActivationApproval = new("inactive-product", Approval()) };
        var e = Evidence(c) with { Outcome = EvidenceOutcome.NotApplicable, MissingReason = "inactive-product" };
        Assert.Equal(1, (await RunAsync("evidence-check", new(1, [c]), new EvidenceDocument(1, [e]))).Exit);
    }

    [Fact]
    public async Task ManualReviewCannotCountAsRuntimePass()
    {
        var c = Contract() with { RequiredEvidence = [EvidenceClass.Manual] };
        var e = Evidence(c) with { EvidenceClass = EvidenceClass.Manual };
        Assert.Equal(1, (await RunAsync("evidence-check", new(1, [c]), new EvidenceDocument(1, [e]))).Exit);
    }

    [Fact]
    public async Task InvalidInputDiagnosticsDoNotEchoSensitiveContent()
    {
        await WithFilesAsync(async paths =>
        {
            await File.WriteAllTextAsync(paths[0], "{\"privateToken\":\"synthetic-do-not-echo\"}");
            using var writer = new StringWriter();
            Assert.Equal(1, await SecurityArchitectureCli.RunAsync(["validate", paths[0], Now.ToString("O")], writer));
            Assert.DoesNotContain("synthetic-do-not-echo", writer.ToString());
            Assert.DoesNotContain(paths[0], writer.ToString());
            return true;
        });
    }
}
