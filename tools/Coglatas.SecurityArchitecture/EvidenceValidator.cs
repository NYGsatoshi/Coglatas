using System.Text.RegularExpressions;

namespace Coglatas.SecurityArchitecture;

public static partial class EvidenceValidator
{
    [GeneratedRegex(@"^[a-f0-9]{40}$")]
    private static partial Regex CandidatePattern();

    public static ValidationResult Check(ContractDocument contracts, EvidenceDocument evidence,
        string candidateSha, string environmentFingerprint, DateTimeOffset asOfUtc)
    {
        var diagnostics = ContractValidator.Validate(contracts, asOfUtc).Diagnostics.ToList();
        void Add(string rule, string subject, string reason) => diagnostics.Add(new(rule, subject, reason));
        if (!CandidatePattern().IsMatch(candidateSha) ||
            !ContractValidator.DigestPattern().IsMatch(environmentFingerprint))
            Add("CANDIDATE_CONTEXT", "document", "Exact SHA and environment digest are required.");
        if (evidence.SchemaVersion != 1 || evidence.Records is null)
            Add("EVIDENCE_SCHEMA", "document", "Unsupported or missing evidence document.");
        if (diagnostics.Count != 0) return ContractValidator.Result(diagnostics);
        var known = contracts.Contracts.ToDictionary(c => c.ContractId, StringComparer.Ordinal);
        var seen = new HashSet<(string, EvidenceClass)>();
        foreach (var record in evidence.Records ?? [])
        {
            if (record is null) { Add("EVIDENCE_RECORD", "document", "Null evidence."); continue; }
            if (record.ContractId is null || !known.TryGetValue(record.ContractId, out var contract))
            { Add("UNKNOWN_EVIDENCE", "document", "Unknown contract in evidence."); continue; }
            var id = contract.ContractId;
            if (!seen.Add((id, record.EvidenceClass))) Add("DUPLICATE_EVIDENCE", id, "Duplicate evidence class.");
            if (record.CandidateSha != candidateSha || record.EnvironmentFingerprint != environmentFingerprint)
                Add("EVIDENCE_CONTEXT", id, "Evidence candidate/environment does not match the independently supplied context.");
            if (record.SpecIds is null ||
                !record.SpecIds.Order(StringComparer.Ordinal).SequenceEqual(contract.SpecIds.Order(StringComparer.Ordinal)))
                Add("EVIDENCE_SPEC", id, "Normative reference identities differ.");
            if (record.ExpectedPolicy != contract.ExpectedPolicy || record.ObservedPolicy != contract.ExpectedPolicy)
                Add("EVIDENCE_POLICY", id, "Expected or normalized observed policy differs from the contract.");
            if (string.IsNullOrWhiteSpace(record.VerifierId) || string.IsNullOrWhiteSpace(record.VerifierVersion) ||
                string.IsNullOrWhiteSpace(record.ExecutionReference) ||
                !ContractValidator.DigestPattern().IsMatch(record.ExecutionDigest ?? "") ||
                string.IsNullOrWhiteSpace(record.TestIdentityCategory) ||
                string.IsNullOrWhiteSpace(record.SanitizedFindingId) || !record.VerifierEnabled)
                Add("EXECUTION_PROVENANCE", id, "Enabled verifier and execution/provenance identities required.");
            if (record.ExecutedAtUtc > asOfUtc || record.ExecutedAtUtc == default ||
                asOfUtc - record.ExecutedAtUtc > TimeSpan.FromHours(24))
                Add("STALE_EVIDENCE", id, "Evidence must be executed in the preceding 24 hours.");
            if (!Enum.IsDefined(record.EvidenceClass) || !Enum.IsDefined(record.Outcome) ||
                !contract.RequiredEvidence.Contains(record.EvidenceClass))
                Add("EVIDENCE_CLASS", id, "Unknown or unexpected evidence class/outcome.");
            if (record.EvidenceClass == EvidenceClass.Manual && record.Outcome == EvidenceOutcome.PASS)
                Add("MANUAL_PASS", id, "Manual review is not machine or runtime PASS.");
            if (record.Outcome == EvidenceOutcome.NOT_APPLICABLE)
            {
                if (contract.ActivationState != ActivationState.Conditional || contract.ActivationApproval is null ||
                    record.EvidenceClass == EvidenceClass.Runtime || string.IsNullOrWhiteSpace(record.MissingReason))
                    Add("FALSE_NOT_APPLICABLE", id,
                        "Conditional product applicability cannot exempt isolated runtime verification.");
            }
            else if (record.Outcome != EvidenceOutcome.PASS || !string.IsNullOrEmpty(record.MissingReason))
                Add("EVIDENCE_NOT_PASS", id, "Failed, unverified, missing or erroneous execution is not PASS.");
            if (record.EvidenceClass == EvidenceClass.Runtime &&
                (record.PositiveControlCount < 1 || record.NegativeControlCount < 1))
                Add("RUNTIME_CONTROLS", id, "Runtime evidence needs executed positive and rejection controls.");
            if (record.ExceptionId != contract.Exception?.Id ||
                record.ExceptionExpiresAtUtc != contract.Exception?.ExpiresAtUtc)
                Add("EVIDENCE_EXCEPTION", id, "Evidence exception identity/expiry mismatch.");
        }
        foreach (var contract in contracts.Contracts)
            foreach (var required in contract.RequiredEvidence)
                if (!seen.Contains((contract.ContractId, required)))
                    Add("MISSING_EVIDENCE", contract.ContractId, "A required evidence class has no execution record.");
        return ContractValidator.Result(diagnostics);
    }
}
