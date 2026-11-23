using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Coglatas.SecurityArchitecture;

public enum SourceEntryState { AssertionReferenceBound, DeclarationOnly, Missing, Skipped, Stale, Invalid }

/// <summary>A reviewed byte-range reference, not semantic proof that an assertion executes.</summary>
public sealed record SourceAssertionReference(int ByteOffset, int ByteLength, string Digest);

public sealed record SourceEntryAssessment(SourceEntryState State)
{
    public string ExecutionStatus => "NOT_EXECUTED";
    public string SemanticAssertionStatus => "UNVERIFIED";
    public string ApprovalStatus => "UNVERIFIED";
}

/// <summary>
/// Pure optional entry checks over existing mapping identities and independently supplied bytes.
/// No source result is accepted as execution evidence, approval, eligibility or an SEC-ARCH exit.
/// </summary>
public static class SourceEntryValidator
{
    public static SourceEntryAssessment Check(SpecMapping mapping, SpecRequirement requirement,
        SpecVerifier verifier, string candidateSha, string expectedGitBlobSha, byte[]? bytes,
        SourceAssertionReference? assertion = null, bool skipped = false)
    {
        ArgumentNullException.ThrowIfNull(mapping); ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(verifier);
        if (requirement.Versions.Length == 0) return new(SourceEntryState.Invalid);
        var current = requirement.Versions[^1];
        if (!RepositoryArtifactReader.RevisionPattern().IsMatch(candidateSha) ||
            !RepositoryArtifactReader.RevisionPattern().IsMatch(expectedGitBlobSha) ||
            !RepositoryArtifactReader.IsSafePath(verifier.Source.Path) ||
            !ContractValidator.DigestPattern().IsMatch(verifier.Source.Digest) ||
            mapping.SpecId != requirement.SpecId || !mapping.Verifiers.Contains(verifier) ||
            !current.VerificationClasses.Contains(verifier.Class) || current.Status == SpecStatus.Retired)
            return new(SourceEntryState.Invalid);
        if (verifier.Source.Revision != candidateSha || mapping.RequirementVersion != current.Version)
            return new(SourceEntryState.Stale);
        if (bytes is null) return new(SourceEntryState.Missing);
        if (bytes.Length > RepositoryArtifactReader.MaximumBlobBytes) return new(SourceEntryState.Invalid);
        // Freeze the supplied snapshot so blob, digest, declaration and range refer to the same bytes.
        var snapshot = (byte[])bytes.Clone();
        var header = Encoding.ASCII.GetBytes("blob " + snapshot.Length.ToString(CultureInfo.InvariantCulture) + "\0");
        // SHA-1 is used only for the existing Git object identity, never as a security/trust digest.
        var gitBlob = Convert.ToHexStringLower(SHA1.HashData([.. header, .. snapshot]));
        if (gitBlob != expectedGitBlobSha || SpecDigest.Bytes(snapshot) != verifier.Source.Digest)
            return new(SourceEntryState.Stale);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(snapshot); }
        catch (DecoderFallbackException) { return new(SourceEntryState.Invalid); }
        if (!SpecTraceabilityValidator.HasSourceIdentity(text, verifier.SourceIdentity)) return new(SourceEntryState.Missing);
        if (skipped) return new(SourceEntryState.Skipped);
        if (assertion is null) return new(SourceEntryState.DeclarationOnly);
        if (assertion.ByteOffset < 0 || assertion.ByteLength <= 0 || assertion.ByteOffset > snapshot.Length ||
            assertion.ByteLength > snapshot.Length - assertion.ByteOffset ||
            !ContractValidator.DigestPattern().IsMatch(assertion.Digest))
            return new(SourceEntryState.Invalid);
        return new(SpecDigest.Bytes(snapshot.AsSpan(assertion.ByteOffset, assertion.ByteLength)) == assertion.Digest
            ? SourceEntryState.AssertionReferenceBound : SourceEntryState.Stale);
    }
}
