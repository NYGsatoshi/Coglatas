using System.Text.Json;
using System.Text.Json.Serialization;

namespace Coglatas.SecurityArchitecture;

public enum ContractType { Api, Rls, SignalR, Kafka, Service }
public enum ActivationState { Active, Conditional, Retired }
public enum EvidenceClass { Static, Configuration, Runtime, Manual }
public enum AccessDecision { Allow, Deny }
public enum EvidenceOutcome
{
    [JsonStringEnumMemberName("PASS")] Pass,
    [JsonStringEnumMemberName("FAIL")] Fail,
    [JsonStringEnumMemberName("UNVERIFIED")] Unverified,
    [JsonStringEnumMemberName("NOT_APPLICABLE")] NotApplicable,
    [JsonStringEnumMemberName("ERROR")] Error
}

public sealed record ContractDocument(int SchemaVersion, FlowContract[] Contracts);
public sealed record OwnerApproval(string Owner, string Reference, string ReviewedDigest, DateTimeOffset ApprovedAtUtc);
public sealed record ContractException(
    string Id, string ContractId, string Scope, string Reason,
    DateTimeOffset StartsAtUtc, DateTimeOffset ExpiresAtUtc, OwnerApproval Approval);
public sealed record ActivationApproval(string Reason, OwnerApproval Approval);
public sealed record FlowContract(
    string ContractId, string[] SpecIds, string Owner, ContractType Type,
    string Source, string Destination, string Protocol, string Principal,
    string Operation, string TenantScope, string ResourceScope, string TrustBoundary,
    AccessDecision ExpectedDecision, string ExpectedPolicy, EvidenceClass[] RequiredEvidence,
    ActivationState ActivationState, string[] ThreatReferences,
    ActivationApproval? ActivationApproval = null, ContractException? Exception = null,
    ApiContract? Api = null, RlsContract? Rls = null, SignalRContract? SignalR = null,
    KafkaContract? Kafka = null, ServiceContract? Service = null);
public sealed record ApiContract(string Route, string Method, bool AllowAnonymous, string Policy);
public sealed record RlsContract(
    string Schema, string Table, string Policy, string Role, string Operation,
    string Context, string Using, string WithCheck, bool Enabled, bool Forced);
public sealed record SignalRContract(string Hub, string Method, string Event, string Scope);
public sealed record KafkaContract(
    string ResourceType, string Resource, string Pattern, string Host,
    string Operation, bool Superuser, bool AllowEveryoneIfNoAcl);
public sealed record ServiceContract(
    string Audience, string[] RequiredScopes, string Destination, bool RequireTls, string NetworkRule);

public sealed record EvidenceDocument(int SchemaVersion, EvidenceRecord[] Records);
public sealed record EvidenceRecord(
    string ContractId, string[] SpecIds, string VerifierId, string VerifierVersion,
    string CandidateSha, string EnvironmentFingerprint, string ExpectedPolicy,
    string ObservedPolicy, EvidenceClass EvidenceClass, EvidenceOutcome Outcome,
    string TestIdentityCategory, DateTimeOffset ExecutedAtUtc, string SanitizedFindingId,
    string? MissingReason, string? ExceptionId, DateTimeOffset? ExceptionExpiresAtUtc,
    string ExecutionReference, string ExecutionDigest, bool VerifierEnabled,
    int PositiveControlCount, int NegativeControlCount);

public sealed record Diagnostic(string RuleId, string Subject, string Reason);
public sealed record ValidationResult(bool Valid, Diagnostic[] Diagnostics);

public static class ContractJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            RespectRequiredConstructorParameters = true,
            RespectNullableAnnotations = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }

    public static T Read<T>(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        RejectDuplicates(parsed.RootElement);
        return JsonSerializer.Deserialize<T>(json, Options)
            ?? throw new JsonException("Document is null.");
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new JsonException("Duplicate JSON property.");
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Null) throw new JsonException("Null array element.");
                RejectDuplicates(item);
            }
    }
}
