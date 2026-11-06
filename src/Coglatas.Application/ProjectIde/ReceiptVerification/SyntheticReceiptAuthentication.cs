using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coglatas.Domain.ProjectIde;
using Coglatas.Domain.ProjectIde.VersionedArtifacts;

namespace Coglatas.Application.ProjectIde.ReceiptVerification;

public enum SyntheticProofStatus { DiagnosticAuthenticated, Untrusted, Invalid, Unsupported, Revoked, Stale }

/// <summary>Diagnostic authentication only. No instance of this result grants formal evidence eligibility.</summary>
public sealed class SyntheticProofResult
{
    public SyntheticProofStatus Status { get; }
    public bool FormalEvidenceEligible => false;
    internal SyntheticProofResult(SyntheticProofStatus status) => Status = status;
}

/// <summary>A synthetic public key registration, not a production trust root or approved Producer allowlist.</summary>
public sealed class SyntheticProducerRegistration
{
    private readonly byte[] _publicKey;
    public string ProducerIdentity { get; }
    public string WorkflowIdentity { get; }
    public string EnvironmentIdentity { get; }
    public string Revision { get; }
    public bool Revoked { get; }

    public SyntheticProducerRegistration(string producerIdentity, string workflowIdentity, string environmentIdentity,
        string revision, bool revoked, ReadOnlySpan<byte> publicKey)
    {
        foreach (var value in new[] { producerIdentity, workflowIdentity, environmentIdentity, revision })
            if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.Any(char.IsControl))
                throw new ArgumentException("Invalid synthetic registration identity.");
        if (publicKey.Length is < 1 or > 1024) throw new ArgumentException("Invalid bounded public key.");
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(publicKey, out var read);
        if (read != publicKey.Length || rsa.KeySize is < 2048 or > 4096)
            throw new ArgumentException("Synthetic proof requires a complete RSA 2048..4096 public key.");
        _publicKey = publicKey.ToArray();
        ProducerIdentity = producerIdentity; WorkflowIdentity = workflowIdentity; EnvironmentIdentity = environmentIdentity;
        Revision = revision; Revoked = revoked;
    }

    internal byte[] PublicKey() => (byte[])_publicKey.Clone();
    internal bool SameRegistration(SyntheticProducerRegistration other) =>
        ProducerIdentity == other.ProducerIdentity && WorkflowIdentity == other.WorkflowIdentity &&
        EnvironmentIdentity == other.EnvironmentIdentity && Revision == other.Revision &&
        Revoked == other.Revoked && _publicKey.AsSpan().SequenceEqual(other._publicKey);
}

/// <summary>Called for every verification and again before admitting its result; never a cached receipt claim.</summary>
public interface ISyntheticProducerRegistry
{
    SyntheticProducerRegistration? FindCurrent(string producerIdentity);
}

public sealed record SyntheticRunExpectation
{
    public string RunId { get; }
    public string WorkflowIdentity { get; }
    public string EnvironmentIdentity { get; }
    public string SourceRevision { get; }

    public SyntheticRunExpectation(string runId, string workflowIdentity, string environmentIdentity, string sourceRevision)
    {
        foreach (var value in new[] { runId, workflowIdentity, environmentIdentity })
            if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.Any(char.IsControl))
                throw new ArgumentException("Invalid expected run context.");
        if (sourceRevision.Length != 40 || sourceRevision.Any(c => c is not (>= 'a' and <= 'f') and not (>= '0' and <= '9')))
            throw new ArgumentException("Expected an exact source-code revision.");
        RunId = runId; WorkflowIdentity = workflowIdentity; EnvironmentIdentity = environmentIdentity; SourceRevision = sourceRevision;
    }
}

/// <summary>DSSE/in-toto proof experiment with explicit RSA-PSS keys. It has no DI, network, signing or production activation path.</summary>
public sealed class SyntheticReceiptAuthenticator(ISyntheticProducerRegistry producers)
{
    public const string PayloadType = "application/vnd.in-toto+json";
    public const string StatementType = "https://in-toto.io/Statement/v1";
    public const string PredicateType = "urn:coglatas:prototype:receipt-provenance:v1";
    private const int MaximumProofBytes = 262_144;

    /// <summary>DSSE v1.0.2 PAE uses UTF-8 byte lengths, without canonicalizing the signed payload.</summary>
    public static byte[] PreAuthenticationEncoding(string payloadType, ReadOnlySpan<byte> payload)
    {
        if (payloadType != PayloadType || payload.Length is < 1 or > MaximumProofBytes)
            throw new ArgumentException("Unsupported or oversized synthetic DSSE payload.");
        var typeBytes = Encoding.UTF8.GetBytes(payloadType);
        var prefix = Encoding.ASCII.GetBytes("DSSEv1 " + typeBytes.Length.ToString(CultureInfo.InvariantCulture) + " ");
        var bodyPrefix = Encoding.ASCII.GetBytes(" " + payload.Length.ToString(CultureInfo.InvariantCulture) + " ");
        var framed = new byte[prefix.Length + typeBytes.Length + bodyPrefix.Length + payload.Length];
        var offset = 0;
        foreach (var part in new[] { prefix, typeBytes, bodyPrefix })
        {
            part.CopyTo(framed, offset); offset += part.Length;
        }
        payload.CopyTo(framed.AsSpan(offset));
        return framed;
    }

    public SyntheticProofResult Verify(ReadOnlySpan<byte> envelopeBytes, PrototypeArtifact receipt, SyntheticRunExpectation expected)
    {
        ArgumentNullException.ThrowIfNull(receipt); ArgumentNullException.ThrowIfNull(expected);
        if (envelopeBytes.Length is < 1 or > MaximumProofBytes) return new(SyntheticProofStatus.Invalid);
        if (!receipt.SemanticsSupported || receipt.Identity.ArtifactType != PrototypeArtifactRegistry.MaterializationReceipt)
            return new(SyntheticProofStatus.Unsupported);
        try
        {
            var envelope = SourceJson.Parse(envelopeBytes).Value;
            RequireObject(envelope, "payloadType", "payload", "signatures");
            if (Text(envelope, "payloadType") != PayloadType) return new(SyntheticProofStatus.Unsupported);
            var payloadBytes = DecodeBase64(Text(envelope, "payload"), MaximumProofBytes);
            var statement = SourceJson.Parse(payloadBytes).Value;
            RequireObject(statement, "_type", "subject", "predicateType", "predicate");
            if (Text(statement, "_type") != StatementType || Text(statement, "predicateType") != PredicateType)
                return new(SyntheticProofStatus.Unsupported);
            var subjects = Array(statement, "subject");
            if (subjects.GetArrayLength() != 1) return new(SyntheticProofStatus.Invalid);
            var subject = subjects[0]; RequireObject(subject, "name", "digest");
            var subjectDigest = subject.GetProperty("digest"); RequireObject(subjectDigest, "sha256");
            if (Text(subject, "name") != "receipt" || Text(subjectDigest, "sha256") != receipt.Identity.FullDigest)
                return new(SyntheticProofStatus.Invalid);
            var predicate = statement.GetProperty("predicate");
            RequireObject(predicate, "receiptIdentity", "producerIdentity", "workflowIdentity", "runId", "sourceRevision", "buildInput", "environmentIdentity");
            if (!Same(predicate.GetProperty("receiptIdentity"), receipt.Identity.Data.Value) ||
                !Same(predicate.GetProperty("buildInput"), receipt.Data.Value.GetProperty("payload").GetProperty("compilerInput")))
                return new(SyntheticProofStatus.Invalid);
            var producer = Text(predicate, "producerIdentity");
            var receiptPayload = receipt.Data.Value.GetProperty("payload");
            if (producer != Text(receiptPayload.GetProperty("producerClaim"), "identity")) return new(SyntheticProofStatus.Invalid);
            if (Text(predicate, "runId") != expected.RunId ||
                Text(receiptPayload.GetProperty("captureContext"), "runId") != expected.RunId ||
                Text(predicate, "workflowIdentity") != expected.WorkflowIdentity ||
                Text(predicate, "environmentIdentity") != expected.EnvironmentIdentity ||
                Text(predicate, "sourceRevision") != expected.SourceRevision)
                return new(SyntheticProofStatus.Stale);
            var registration = producers.FindCurrent(producer);
            if (registration is null || registration.ProducerIdentity != producer) return new(SyntheticProofStatus.Untrusted);
            if (registration.Revoked) return new(SyntheticProofStatus.Revoked);
            if (registration.WorkflowIdentity != expected.WorkflowIdentity || registration.EnvironmentIdentity != expected.EnvironmentIdentity)
                return new(SyntheticProofStatus.Untrusted);
            var signatures = Array(envelope, "signatures");
            if (signatures.GetArrayLength() != 1) return new(SyntheticProofStatus.Unsupported);
            var signature = signatures[0]; RequireObject(signature, "keyid", "sig");
            _ = Text(signature, "keyid"); // Unauthenticated hint only; never selects authority or replaces signature verification.
            var signatureBytes = DecodeBase64(Text(signature, "sig"), 512);
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(registration.PublicKey(), out _);
            if (!rsa.VerifyData(PreAuthenticationEncoding(PayloadType, payloadBytes), signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                return new(SyntheticProofStatus.Invalid);
            var current = producers.FindCurrent(producer);
            if (current is null || current.Revoked) return new(SyntheticProofStatus.Revoked);
            if (!registration.SameRegistration(current)) return new(SyntheticProofStatus.Stale);
            return new(SyntheticProofStatus.DiagnosticAuthenticated);
        }
        catch (UnsupportedSourceException) { return new(SyntheticProofStatus.Unsupported); }
        catch (Exception error) when (error is FormatException or ArgumentException or InvalidOperationException or
            KeyNotFoundException or CryptographicException or JsonException)
        {
            return new(SyntheticProofStatus.Invalid);
        }
    }

    private static byte[] DecodeBase64(string value, int maximumBytes)
    {
        if (value.Length > ((maximumBytes + 2) / 3) * 4) throw new FormatException("Oversized proof field.");
        var bytes = Convert.FromBase64String(value);
        if (bytes.Length > maximumBytes || Convert.ToBase64String(bytes) != value) throw new FormatException("Noncanonical base64 proof field.");
        return bytes;
    }

    private static void RequireObject(JsonElement value, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != fields.Length ||
            fields.Any(field => !value.TryGetProperty(field, out _)))
            throw new UnsupportedSourceException("Unsupported proof member set.");
    }

    private static JsonElement Array(JsonElement value, string field) => value.GetProperty(field).ValueKind == JsonValueKind.Array
        ? value.GetProperty(field) : throw new FormatException("Expected proof array.");
    private static string Text(JsonElement value, string field) => value.GetProperty(field).ValueKind == JsonValueKind.String
        ? value.GetProperty(field).GetString()! : throw new FormatException("Expected proof string.");
    private static bool Same(JsonElement left, JsonElement right) => SourceJson.Parse(left.GetRawText()).Equals(SourceJson.Parse(right.GetRawText()));
}
