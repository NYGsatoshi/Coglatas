using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Coglatas.SecurityArchitecture;

public enum SpecStatus { Active, Deprecated, Retired }
public enum SpecSeverity { Blocking, Advisory, Manual }
public enum SpecVerificationClass
{
    [JsonStringEnumMemberName("archunit")] ArchUnit,
    [JsonStringEnumMemberName("roslyn")] Roslyn,
    [JsonStringEnumMemberName("static-custom")] StaticCustom,
    [JsonStringEnumMemberName("unit-test")] UnitTest,
    [JsonStringEnumMemberName("integration-test")] IntegrationTest,
    [JsonStringEnumMemberName("e2e-test")] E2eTest,
    [JsonStringEnumMemberName("contract-test")] ContractTest,
    [JsonStringEnumMemberName("generated-evidence")] GeneratedEvidence,
    [JsonStringEnumMemberName("manual")] Manual
}

// Store real statements and source references only in the private specification repository.
public sealed record SpecRegistryDocument(int SchemaVersion, int RegistryVersion,
    string SpecificationRevision, SpecRequirement[] Requirements);
public sealed record SpecRequirement(string SpecId, SpecRequirementVersion[] Versions);
public sealed record SpecRequirementVersion(int Version, SpecStatus Status, SpecSource Source,
    string NormativeStatement, string StatementDigest, string Owner, SpecSeverity Severity,
    SpecVerificationClass[] VerificationClasses, string KnownLimitation,
    string[] ArchitectureRuleIds, string ChangeReference, string? RetirementReason = null,
    string[]? SuccessorSpecIds = null);
public sealed record SpecSource(string Revision, string Path, string? Anchor, string Digest);
public sealed record SpecTraceabilityDocument(int SchemaVersion, int RegistryVersion,
    string RegistryDigest, SpecMapping[] Mappings);
public sealed record SpecMapping(string SpecId, int RequirementVersion,
    string ContractId, string ContractDigest, SpecVerifier[] Verifiers);
public sealed record SpecVerifier(string VerifierId, string Version, SpecVerificationClass Class,
    SpecSource Source, string SourceIdentity, string[] EvidenceIds);
public sealed record SpecTraceabilityEvidenceDocument(int SchemaVersion, SpecExecutionLink[] Records);
public sealed record SpecExecutionLink(string EvidenceId, string SpecId, int RequirementVersion,
    string ContractId, string VerifierId, string VerifierVersion, string VerifierSourceDigest,
    string ContractDigest, string CandidateSha, EvidenceOutcome Outcome, bool VerifierEnabled,
    string ExecutionReference, string ExecutionDigest);
public sealed record SpecCoverageCount(string Category, int Count);
public sealed record SpecCoverageSummary(string CandidateSha, int SchemaVersion, int RegistryVersion,
    SpecCoverageCount[] ActiveFamilies, SpecCoverageCount[] Severities,
    SpecCoverageCount[] VerificationClasses, int ActiveRequirements, int DeprecatedRequirements,
    int RetiredRequirements, int Mappings, int ManualMappings, int ExecutedPassingLinks,
    int UnresolvedLinks, int RequirementsWithKnownLimitations);
public sealed record SpecValidationResult(bool Valid, bool NormativeReady,
    string ApprovalStatus, string ExecutionAttestationStatus,
    Diagnostic[] Diagnostics, SpecCoverageSummary? Coverage = null);

public static class SpecDigest
{
    public static string Bytes(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static string Text(string value) => Bytes(Encoding.UTF8.GetBytes(value));
    public static string Document<T>(T value) => Bytes(JsonSerializer.SerializeToUtf8Bytes(value, ContractJson.Options));
}
