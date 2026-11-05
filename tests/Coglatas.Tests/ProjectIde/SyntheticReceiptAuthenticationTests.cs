using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.ReceiptVerification;
using Coglatas.Domain.ProjectIde;
using Coglatas.Domain.ProjectIde.VersionedArtifacts;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticReceiptAuthenticationTests
{
    private sealed class Registry(Func<string, SyntheticProducerRegistration?> find) : ISyntheticProducerRegistry
    {
        public SyntheticProducerRegistration? FindCurrent(string producerIdentity) => find(producerIdentity);
    }

    private sealed class Fixture : IDisposable
    {
        internal RSA Key { get; } = RSA.Create(2048);
        internal PrototypeArtifact Receipt { get; }
        internal SyntheticRunExpectation Expected { get; } = new("run-1", "synthetic-workflow", "synthetic-runner", new string('a', 40));
        internal SyntheticProducerRegistration Registration { get; }
        internal Fixture()
        {
            var input = CompilerContractFixtures.Input();
            var source = ArtifactSourcePin.FromSource(input.Source);
            var manifest = PrototypeArtifact.Create(PrototypeArtifactRegistry.DependencyManifest, source, Json(new { entries = Array.Empty<object>() }));
            var association = ArtifactAssembly.Associate(input, manifest);
            var actual = PrototypeArtifact.Create(PrototypeArtifactRegistry.MaterializationSet, source,
                Json(new { dependencyManifest = manifest.Identity.Data.Value, enumeration = "completed", entries = Array.Empty<object>() }));
            Receipt = PrototypeArtifact.Create(PrototypeArtifactRegistry.MaterializationReceipt, source, Json(new
            {
                compilerInput = association.Identity.Data.Value, actualSet = actual.Identity.Data.Value,
                producerClaim = new { kind = "trusted-ci-candidate", identity = "synthetic-ci" },
                captureContext = new { runId = Expected.RunId, capturedAtUtc = "2026-11-06T00:00:00.0000000Z" },
                execution = "completed", coverage = "complete", provenance = new[] { "synthetic" }
            }));
            Registration = new("synthetic-ci", Expected.WorkflowIdentity, Expected.EnvironmentIdentity, "registry-v1", false, Key.ExportSubjectPublicKeyInfo());
        }
        internal JsonObject Statement() => new()
        {
            ["_type"] = SyntheticReceiptAuthenticator.StatementType,
            ["subject"] = JsonSerializer.SerializeToNode(new[] { new { name = "receipt", digest = new { sha256 = Receipt.Identity.FullDigest } } }),
            ["predicateType"] = SyntheticReceiptAuthenticator.PredicateType,
            ["predicate"] = JsonSerializer.SerializeToNode(new
            {
                receiptIdentity = Receipt.Identity.Data.Value, producerIdentity = "synthetic-ci",
                workflowIdentity = Expected.WorkflowIdentity, runId = Expected.RunId, sourceRevision = Expected.SourceRevision,
                buildInput = Receipt.Data.Value.GetProperty("payload").GetProperty("compilerInput"), environmentIdentity = Expected.EnvironmentIdentity
            })
        };
        internal JsonObject Envelope(JsonObject? statement = null, RSA? key = null)
        {
            var bytes = Encoding.UTF8.GetBytes((statement ?? Statement()).ToJsonString());
            var signature = (key ?? Key).SignData(SyntheticReceiptAuthenticator.PreAuthenticationEncoding(SyntheticReceiptAuthenticator.PayloadType, bytes),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            return new()
            {
                ["payloadType"] = SyntheticReceiptAuthenticator.PayloadType, ["payload"] = Convert.ToBase64String(bytes),
                ["signatures"] = new JsonArray(new JsonObject { ["keyid"] = "untrusted-hint", ["sig"] = Convert.ToBase64String(signature) })
            };
        }
        internal SyntheticProofResult Verify(JsonObject? envelope = null, ISyntheticProducerRegistry? registry = null,
            SyntheticRunExpectation? expected = null, PrototypeArtifact? receipt = null) =>
            new SyntheticReceiptAuthenticator(registry ?? new Registry(_ => Registration)).Verify(
                Encoding.UTF8.GetBytes((envelope ?? Envelope()).ToJsonString()), receipt ?? Receipt, expected ?? Expected);
        public void Dispose() => Key.Dispose();
    }

    private static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));

    [Fact]
    public void Valid_protected_signature_is_diagnostic_only_and_never_formal_evidence()
    {
        using var fixture = new Fixture(); var result = fixture.Verify();
        Assert.Equal(SyntheticProofStatus.DiagnosticAuthenticated, result.Status);
        Assert.False(result.FormalEvidenceEligible);
    }

    [Fact]
    public void PAE_counts_UTF8_bytes_and_matches_the_DSSE_protocol_framing()
    {
        var body = Encoding.UTF8.GetBytes("{\"x\":\"é😀\"}");
        Assert.Equal(Encoding.UTF8.GetBytes("DSSEv1 28 application/vnd.in-toto+json 14 {\"x\":\"é😀\"}"),
            SyntheticReceiptAuthenticator.PreAuthenticationEncoding(SyntheticReceiptAuthenticator.PayloadType, body));
        Assert.Throws<ArgumentException>(() => SyntheticReceiptAuthenticator.PreAuthenticationEncoding("application/json", body));
        Assert.Throws<ArgumentException>(() => SyntheticReceiptAuthenticator.PreAuthenticationEncoding(SyntheticReceiptAuthenticator.PayloadType, []));
    }

    [Fact]
    public void Unknown_or_fabricated_producer_cannot_be_authenticated_by_a_matching_hash()
    {
        using var fixture = new Fixture();
        Assert.Equal(SyntheticProofStatus.Untrusted, fixture.Verify(registry: new Registry(_ => null)).Status);
        var wrong = new SyntheticProducerRegistration("another-producer", fixture.Expected.WorkflowIdentity,
            fixture.Expected.EnvironmentIdentity, "v1", false, fixture.Key.ExportSubjectPublicKeyInfo());
        Assert.Equal(SyntheticProofStatus.Untrusted, fixture.Verify(registry: new Registry(_ => wrong)).Status);
    }

    [Fact]
    public void Wrong_trust_root_and_fabricated_signature_are_rejected_even_with_the_same_key_hint()
    {
        using var fixture = new Fixture(); using var other = RSA.Create(2048);
        Assert.Equal(SyntheticProofStatus.Invalid, fixture.Verify(fixture.Envelope(key: other)).Status);
        var wrongRoot = new SyntheticProducerRegistration("synthetic-ci", fixture.Expected.WorkflowIdentity,
            fixture.Expected.EnvironmentIdentity, "v1", false, other.ExportSubjectPublicKeyInfo());
        Assert.Equal(SyntheticProofStatus.Invalid, fixture.Verify(registry: new Registry(_ => wrongRoot)).Status);
    }

    [Fact]
    public void Unauthenticated_key_hint_cannot_replace_or_select_Producer_authority()
    {
        using var fixture = new Fixture(); var envelope = fixture.Envelope();
        envelope["signatures"]![0]!["keyid"] = "fabricated-trusted-root";
        Assert.Equal(SyntheticProofStatus.DiagnosticAuthenticated, fixture.Verify(envelope).Status);
        Assert.Equal(SyntheticProofStatus.Untrusted, fixture.Verify(envelope, new Registry(_ => null)).Status);
    }

    [Fact]
    public void Signature_authenticates_original_bytes_without_canonicalizing_tampering_away()
    {
        using var fixture = new Fixture(); var envelope = fixture.Envelope();
        var original = Convert.FromBase64String(envelope["payload"]!.GetValue<string>());
        envelope["payload"] = Convert.ToBase64String([.. original, (byte)'\n']);
        Assert.Equal(SyntheticProofStatus.Invalid, fixture.Verify(envelope).Status);
    }

    [Theory]
    [InlineData("runId", "wrong-run")]
    [InlineData("workflowIdentity", "wrong-workflow")]
    [InlineData("environmentIdentity", "wrong-environment")]
    [InlineData("sourceRevision", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    public void Signed_wrong_run_or_environment_is_stale(string field, string value)
    {
        using var fixture = new Fixture(); var statement = fixture.Statement(); statement["predicate"]![field] = value;
        Assert.Equal(SyntheticProofStatus.Stale, fixture.Verify(fixture.Envelope(statement)).Status);
    }

    [Theory]
    [InlineData("receiptIdentity")]
    [InlineData("buildInput")]
    public void Valid_signature_cannot_substitute_exact_typed_artifact_pins(string field)
    {
        using var fixture = new Fixture(); var statement = fixture.Statement();
        statement["predicate"]![field]!["digest"] = new string('b', 64);
        Assert.Equal(SyntheticProofStatus.Invalid, fixture.Verify(fixture.Envelope(statement)).Status);
    }

    [Fact]
    public void Subject_digest_and_full_typed_identity_must_both_match()
    {
        using var fixture = new Fixture(); var statement = fixture.Statement();
        statement["subject"]![0]!["digest"]!["sha256"] = new string('b', 64);
        Assert.Equal(SyntheticProofStatus.Invalid, fixture.Verify(fixture.Envelope(statement)).Status);
        statement = fixture.Statement(); statement["predicate"]!["receiptIdentity"]!["schemaVersion"] = 2;
        Assert.Equal(SyntheticProofStatus.Invalid, fixture.Verify(fixture.Envelope(statement)).Status);
    }

    [Fact]
    public void Producer_claim_and_protected_statement_cannot_disagree()
    {
        using var fixture = new Fixture(); var statement = fixture.Statement(); statement["predicate"]!["producerIdentity"] = "invented";
        Assert.Equal(SyntheticProofStatus.Invalid, fixture.Verify(fixture.Envelope(statement)).Status);
    }

    [Fact]
    public void Revocation_is_checked_for_each_verification_and_after_signature_verification()
    {
        using var fixture = new Fixture();
        var revoked = new SyntheticProducerRegistration("synthetic-ci", fixture.Expected.WorkflowIdentity,
            fixture.Expected.EnvironmentIdentity, "v2", true, fixture.Key.ExportSubjectPublicKeyInfo());
        Assert.Equal(SyntheticProofStatus.Revoked, fixture.Verify(registry: new Registry(_ => revoked)).Status);
        var reads = 0;
        Assert.Equal(SyntheticProofStatus.Revoked, fixture.Verify(registry: new Registry(_ => ++reads == 1 ? fixture.Registration : revoked)).Status);
        Assert.Equal(2, reads);
        Assert.Equal(SyntheticProofStatus.DiagnosticAuthenticated, fixture.Verify().Status);
    }

    [Fact]
    public void Registry_revision_key_or_scope_drift_during_verification_is_not_admitted()
    {
        using var fixture = new Fixture();
        var changed = new SyntheticProducerRegistration("synthetic-ci", fixture.Expected.WorkflowIdentity,
            fixture.Expected.EnvironmentIdentity, "v2", false, fixture.Key.ExportSubjectPublicKeyInfo());
        var reads = 0;
        Assert.Equal(SyntheticProofStatus.Stale, fixture.Verify(registry: new Registry(_ => ++reads == 1 ? fixture.Registration : changed)).Status);
        var wrongEnvironment = new SyntheticProducerRegistration("synthetic-ci", fixture.Expected.WorkflowIdentity,
            "other-runner", "v1", false, fixture.Key.ExportSubjectPublicKeyInfo());
        Assert.Equal(SyntheticProofStatus.Untrusted, fixture.Verify(registry: new Registry(_ => wrongEnvironment)).Status);
    }

    [Fact]
    public void Redacted_or_changed_receipt_cannot_inherit_the_original_signature()
    {
        using var fixture = new Fixture(); var root = JsonNode.Parse(fixture.Receipt.Data.CanonicalText)!;
        root["payload"]!["provenance"] = new JsonArray("redacted");
        var projection = PrototypeArtifact.Parse(SourceJson.Parse(root.ToJsonString()));
        Assert.NotEqual(fixture.Receipt.Identity, projection.Identity);
        Assert.Equal(SyntheticProofStatus.Invalid, fixture.Verify(receipt: projection).Status);
    }

    [Theory]
    [InlineData("predicateType")]
    [InlineData("_type")]
    public void Unsupported_statement_contracts_cannot_be_reinterpreted(string field)
    {
        using var fixture = new Fixture(); var statement = fixture.Statement(); statement[field] = "urn:unsupported";
        Assert.Equal(SyntheticProofStatus.Unsupported, fixture.Verify(fixture.Envelope(statement)).Status);
    }

    [Fact]
    public void Unknown_critical_members_or_signature_grammars_are_unsupported()
    {
        using var fixture = new Fixture(); var statement = fixture.Statement(); statement["predicate"]!["futureTrust"] = true;
        Assert.Equal(SyntheticProofStatus.Unsupported, fixture.Verify(fixture.Envelope(statement)).Status);
        var envelope = fixture.Envelope(); envelope["signatures"] = new JsonArray();
        Assert.Equal(SyntheticProofStatus.Unsupported, fixture.Verify(envelope).Status);
        envelope = fixture.Envelope(); envelope["trusted"] = true;
        Assert.Equal(SyntheticProofStatus.Unsupported, fixture.Verify(envelope).Status);
    }

    [Fact]
    public void Duplicate_JSON_members_and_noncanonical_base64_are_invalid()
    {
        using var fixture = new Fixture(); var envelope = fixture.Envelope();
        envelope["payload"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"x\":1,\"x\":2}"));
        Assert.Equal(SyntheticProofStatus.Invalid, fixture.Verify(envelope).Status);
        envelope = fixture.Envelope(); envelope["payload"] = envelope["payload"]!.GetValue<string>() + "\n";
        Assert.Equal(SyntheticProofStatus.Invalid, fixture.Verify(envelope).Status);
    }

    [Fact]
    public void Missing_oversized_and_invalid_UTF8_proofs_do_not_authenticate()
    {
        using var fixture = new Fixture(); var verifier = new SyntheticReceiptAuthenticator(new Registry(_ => fixture.Registration));
        Assert.Equal(SyntheticProofStatus.Invalid, verifier.Verify([], fixture.Receipt, fixture.Expected).Status);
        Assert.Equal(SyntheticProofStatus.Invalid, verifier.Verify(new byte[262145], fixture.Receipt, fixture.Expected).Status);
        Assert.Equal(SyntheticProofStatus.Invalid, verifier.Verify([0xff], fixture.Receipt, fixture.Expected).Status);
    }

    [Fact]
    public void Weak_or_trailing_public_key_bytes_are_rejected_and_registration_defensively_copies_key_material()
    {
        using var fixture = new Fixture(); using var weak = RSA.Create(1024);
        Assert.Throws<ArgumentException>(() => new SyntheticProducerRegistration("synthetic-ci", "workflow", "runner", "v1", false, weak.ExportSubjectPublicKeyInfo()));
        Assert.Throws<ArgumentException>(() => new SyntheticProducerRegistration("synthetic-ci", "workflow", "runner", "v1", false, [.. fixture.Key.ExportSubjectPublicKeyInfo(), 0]));
        var key = fixture.Key.ExportSubjectPublicKeyInfo();
        var registration = new SyntheticProducerRegistration("synthetic-ci", fixture.Expected.WorkflowIdentity, fixture.Expected.EnvironmentIdentity, "v1", false, key);
        Array.Clear(key);
        Assert.Equal(SyntheticProofStatus.DiagnosticAuthenticated, fixture.Verify(registry: new Registry(_ => registration)).Status);
    }
}
