using System.Text.RegularExpressions;

namespace Coglatas.SecurityArchitecture;

public static partial class ContractValidator
{
    [GeneratedRegex("^SEC-ARCH-[A-Z0-9]+(?:-[A-Z0-9]+)*$")]
    private static partial Regex ContractIdPattern();
    [GeneratedRegex("^SPEC-(ARCH|AUTH|RT|STATE|API|UI)-[A-Z0-9]+(?:-[A-Z0-9]+)*$")]
    private static partial Regex SpecIdPattern();
    [GeneratedRegex("^[a-f0-9]{64}$")]
    internal static partial Regex DigestPattern();

    public static ValidationResult Validate(ContractDocument document, DateTimeOffset asOfUtc)
    {
        var diagnostics = new List<Diagnostic>();
        void Add(string rule, string subject, string reason) => diagnostics.Add(new(rule, subject, reason));
        if (document.SchemaVersion != 1) Add("SCHEMA_VERSION", "document", "Unsupported schema version.");
        if (document.Contracts.Length == 0)
            Add("CONTRACTS_REQUIRED", "document", "At least one contract is required.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contract in document.Contracts)
        {
            var id = contract.ContractId is { } suppliedId && ContractIdPattern().IsMatch(suppliedId)
                ? suppliedId : "invalid";
            if (id == "invalid") Add("CONTRACT_ID", "contract", "Invalid stable contract ID.");
            if (!ids.Add(id)) Add("DUPLICATE_ID", id, "Duplicate contract ID.");
            if (contract.SpecIds.Length == 0 ||
                contract.SpecIds.Any(s => !SpecIdPattern().IsMatch(s)) ||
                contract.SpecIds.Distinct(StringComparer.Ordinal).Count() != contract.SpecIds.Length)
                Add("SPEC_IDS", id, "Valid unique normative reference IDs are required; registration is a separate prerequisite.");
            if (!Enum.IsDefined(contract.Type) || !Enum.IsDefined(contract.ActivationState) ||
                !Enum.IsDefined(contract.ExpectedDecision))
                Add("ENUM_VALUE", id, "Unknown contract type, state or decision.");
            string[] fields = [contract.Owner, contract.Source, contract.Destination, contract.Protocol,
                contract.Principal, contract.Operation, contract.TenantScope, contract.ResourceScope,
                contract.TrustBoundary, contract.ExpectedPolicy];
            if (fields.Any(string.IsNullOrWhiteSpace)) Add("FIELD_REQUIRED", id, "Required flow field missing.");
            if (fields.Any(f => f.Contains('*') || f.Length > 512))
                Add("UNBOUNDED_SCOPE", id, "Wildcards or unbounded values are forbidden.");
            if (contract.RequiredEvidence.Length == 0 ||
                contract.RequiredEvidence.Any(e => !Enum.IsDefined(e)) ||
                contract.RequiredEvidence.Distinct().Count() != contract.RequiredEvidence.Length)
                Add("EVIDENCE_CLASS", id, "Valid unique required evidence classes are required.");
            if (contract.ThreatReferences.Length == 0 ||
                contract.ThreatReferences.Any(string.IsNullOrWhiteSpace))
                Add("THREAT_REFERENCE", id, "A threat reference is required; approval is separate.");
            if (contract.ActivationState == ActivationState.Conditional)
            {
                if (contract.ActivationApproval is null || string.IsNullOrWhiteSpace(contract.ActivationApproval.Reason))
                    Add("ACTIVATION_APPROVAL", id, "Conditional applicability needs owner approval.");
                else ValidateApproval(contract.ActivationApproval.Approval, id, asOfUtc, Add);
            }
            if (contract.Exception is { } exception)
            {
                if (string.IsNullOrWhiteSpace(exception.Id) || exception.ContractId != id ||
                    exception.Scope != contract.ResourceScope || string.IsNullOrWhiteSpace(exception.Reason) ||
                    exception.Scope.Contains('*'))
                    Add("EXCEPTION_SCOPE", id, "Exception must name this exact contract and scope with a reason.");
                if (exception.StartsAtUtc > asOfUtc || exception.ExpiresAtUtc <= asOfUtc ||
                    exception.ExpiresAtUtc <= exception.StartsAtUtc ||
                    exception.ExpiresAtUtc - exception.StartsAtUtc > TimeSpan.FromDays(30))
                    Add("EXCEPTION_EXPIRY", id, "Exception must be current and last at most 30 days.");
                ValidateApproval(exception.Approval, id, asOfUtc, Add);
                if (exception.Approval is { } approval && approval.ApprovedAtUtc > exception.StartsAtUtc)
                    Add("EXCEPTION_APPROVAL_TIME", id, "Exception cannot start before approval.");
            }
            ValidateTypedPolicy(contract, Add);
        }
        return Result(diagnostics);
    }

    private static void ValidateTypedPolicy(FlowContract c, Action<string, string, string> add)
    {
        var id = c.ContractId is { } value && ContractIdPattern().IsMatch(value) ? value : "invalid";
        if (new object?[] { c.Api, c.Rls, c.SignalR, c.Kafka, c.Service }.Count(p => p is not null) != 1)
            add("TYPED_POLICY", id, "Exactly one matching typed policy is required.");
        bool invalid = c.Type switch
        {
            ContractType.Api => c.Api is not { } a || !Required(a.Route, a.Method, a.Policy) ||
                !new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" }.Contains(a.Method),
            ContractType.Rls => c.Rls is not { } r ||
                !Required(r.Schema, r.Table, r.Policy, r.Role, r.Operation, r.Context) ||
                !new[] { "SELECT", "INSERT", "UPDATE", "DELETE" }.Contains(r.Operation) ||
                !r.Enabled || !r.Forced ||
                (r.Operation is "SELECT" or "UPDATE" or "DELETE" && !Required(r.Using)) ||
                (r.Operation is "INSERT" or "UPDATE" && !Required(r.WithCheck)),
            ContractType.SignalR => c.SignalR is not { } s || !Required(s.Hub, s.Method, s.Event, s.Scope),
            ContractType.Kafka => c.Kafka is not { } k || !Required(k.ResourceType, k.Resource, k.Pattern, k.Host, k.Operation) ||
                k.Superuser || k.AllowEveryoneIfNoAcl || k.Pattern != "LITERAL" ||
                !new[] { "READ", "WRITE", "DESCRIBE", "CREATE", "DELETE", "ALTER", "DESCRIBE_CONFIGS", "ALTER_CONFIGS" }.Contains(k.Operation) ||
                !new[] { "TOPIC", "GROUP", "CLUSTER" }.Contains(k.ResourceType),
            ContractType.Service => c.Service is not { } s ||
                !Required(s.Audience, s.Destination, s.NetworkRule) ||
                s.RequiredScopes.Length == 0 ||
                !Required(s.RequiredScopes) || !s.RequireTls,
            _ => true
        };
        if (invalid) add("TYPED_POLICY", id, "Typed policy is missing, unsafe or inconsistent.");
        var policyFields = c.Type switch
        {
            ContractType.Api when c.Api is { } a => new[] { a.Route, a.Method, a.Policy },
            ContractType.Rls when c.Rls is { } r => [r.Schema, r.Table, r.Policy, r.Role, r.Context, r.Using, r.WithCheck],
            ContractType.SignalR when c.SignalR is { } s => [s.Hub, s.Method, s.Event, s.Scope],
            ContractType.Kafka when c.Kafka is { } k => [k.Resource, k.Host, k.Operation],
            ContractType.Service when c.Service is { } s => [s.Destination, s.NetworkRule, .. s.RequiredScopes],
            _ => Array.Empty<string>()
        };
        if (policyFields.Any(s => s.Contains('*') || s.Length > 2048))
            add("UNBOUNDED_POLICY", id, "Unbounded typed policy is forbidden.");
    }

    private static bool Required(params string?[] fields) => fields.All(f => !string.IsNullOrWhiteSpace(f));

    private static void ValidateApproval(OwnerApproval? approval, string id, DateTimeOffset asOfUtc,
        Action<string, string, string> add)
    {
        if (approval is null || !Required(approval.Owner, approval.Reference, approval.ReviewedDigest) ||
            !DigestPattern().IsMatch(approval.ReviewedDigest) || approval.ApprovedAtUtc > asOfUtc ||
            approval.ApprovedAtUtc == default)
            add("OWNER_APPROVAL", id, "Explicit owner/reference/exact reviewed digest and valid time required.");
        // This is structural validation. The trusted caller must authenticate the approval receipt.
    }

    internal static ValidationResult Result(IEnumerable<Diagnostic> diagnostics)
    {
        var ordered = diagnostics.Distinct().OrderBy(d => d.Subject, StringComparer.Ordinal)
            .ThenBy(d => d.RuleId, StringComparer.Ordinal).ThenBy(d => d.Reason, StringComparer.Ordinal).ToArray();
        return new(ordered.Length == 0, ordered);
    }
}
