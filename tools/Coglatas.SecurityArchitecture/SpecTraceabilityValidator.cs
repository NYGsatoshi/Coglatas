using System.Text;
using System.Text.RegularExpressions;

namespace Coglatas.SecurityArchitecture;

public static partial class SpecTraceabilityValidator
{
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.-]*$")]
    private static partial Regex VerifierIdPattern();

    public static async Task<SpecValidationResult> ValidateAsync(SpecRegistryDocument registry,
        SpecTraceabilityDocument manifest, ContractDocument contracts, string expectedSpecificationRevision,
        string candidateSha, DateTimeOffset asOfUtc,
        Func<SpecSource, Task<byte[]?>> readSpecification,
        Func<SpecSource, Task<byte[]?>> readImplementation,
        SpecTraceabilityEvidenceDocument? evidence = null, SpecRegistryDocument? baseline = null)
    {
        var registryResult = await SpecRegistryValidator.ValidateAsync(registry, expectedSpecificationRevision, readSpecification, baseline);
        var diagnostics = registryResult.Diagnostics.Concat(ContractValidator.Validate(contracts, asOfUtc).Diagnostics).ToList();
        void Add(string rule, string id, string reason) => diagnostics.Add(new(rule, id, reason));
        if (manifest.SchemaVersion != 1 || manifest.RegistryVersion != registry.RegistryVersion ||
            manifest.RegistryDigest != SpecDigest.Document(registry))
            Add("SPEC_MANIFEST_REGISTRY", "manifest", "Supported manifest must pin the exact registry version and canonical digest.");
        if (!RepositoryArtifactReader.RevisionPattern().IsMatch(candidateSha))
            Add("SPEC_CANDIDATE", "manifest", "An independently supplied exact candidate SHA is required.");
        if (diagnostics.Count != 0) return SpecRegistryValidator.Result(diagnostics);
        var requirements = registry.Requirements.ToDictionary(r => r.SpecId, r => r.Versions[^1], StringComparer.Ordinal);
        var contractById = contracts.Contracts.ToDictionary(c => c.ContractId, StringComparer.Ordinal);
        var seen = new HashSet<(string, string)>();
        var verifierById = new Dictionary<string, SpecVerifier>(StringComparer.Ordinal);
        var expectedLinks = new Dictionary<string, (SpecMapping Mapping, SpecVerifier Verifier)>(StringComparer.Ordinal);
        foreach (var mapping in manifest.Mappings)
        {
            var id = SpecRegistryValidator.IdPattern().IsMatch(mapping.SpecId) ? mapping.SpecId : "invalid";
            if (!seen.Add((id, mapping.ContractId))) Add("SPEC_DUPLICATE_MAPPING", id, "Requirement/contract mapping is duplicated.");
            if (!requirements.TryGetValue(mapping.SpecId, out var requirement))
            { Add("SPEC_UNKNOWN_MAPPING", id, "Mapping references an unregistered requirement identity."); continue; }
            if (requirement.Status == SpecStatus.Retired)
                Add("SPEC_RETIRED_MAPPING", id, "Retired requirements cannot be mapped as current obligations.");
            if (mapping.RequirementVersion != requirement.Version)
                Add("SPEC_STALE_MAPPING", id, "Mapping references a noncurrent requirement version.");
            if (!contractById.TryGetValue(mapping.ContractId, out var contract) ||
                !contract.SpecIds.Contains(id, StringComparer.Ordinal) || contract.ActivationState == ActivationState.Retired)
                Add("SPEC_CONTRACT_MAPPING", id, "Mapping must reference a current contract that names this requirement.");
            else if (mapping.ContractDigest != SpecDigest.Document(contract))
                Add("SPEC_CONTRACT_DIGEST", id, "The mapped contract has changed from its pinned canonical digest.");
            if (mapping.Verifiers.Length == 0)
                Add("SPEC_MISSING_VERIFIER", id, "Every mapped obligation needs explicit verifier or manual-review linkage.");
            var mappingVerifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var verifier in mapping.Verifiers)
            {
                if (!VerifierIdPattern().IsMatch(verifier.VerifierId) || !mappingVerifiers.Add(verifier.VerifierId) ||
                    string.IsNullOrWhiteSpace(verifier.Version) || string.IsNullOrWhiteSpace(verifier.SourceIdentity) ||
                    verifier.EvidenceIds.Length == 0 || verifier.EvidenceIds.Any(e => !VerifierIdPattern().IsMatch(e)) ||
                    verifier.EvidenceIds.Distinct(StringComparer.Ordinal).Count() != verifier.EvidenceIds.Length)
                    Add("SPEC_VERIFIER", id, "Verifier identity, version, source identity and distinct evidence identities are required.");
                if (!Enum.IsDefined(verifier.Class) || !requirement.VerificationClasses.Contains(verifier.Class))
                    Add("SPEC_VERIFIER_CLASS", id, "Verifier class is unsupported or does not match the registered obligation.");
                if (verifier.Source.Revision != candidateSha)
                    Add("SPEC_VERIFIER_CANDIDATE", id, "Verifier source must bind to the independently supplied current candidate.");
                var bytes = await SpecRegistryValidator.ValidateSourceAsync(verifier.Source, id, readImplementation, Add);
                if (bytes is not null && !HasSourceIdentity(Encoding.UTF8.GetString(bytes), verifier.SourceIdentity))
                    Add("SPEC_VERIFIER_IDENTITY", id, "The pinned source no longer declares the exact verifier identity.");
                if (verifierById.TryGetValue(verifier.VerifierId, out var previous) &&
                    SpecDigest.Document(previous with { EvidenceIds = [] }) != SpecDigest.Document(verifier with { EvidenceIds = [] }))
                    Add("SPEC_VERIFIER_COLLISION", id, "A verifier identity has conflicting version/source/class/declaration bindings.");
                else verifierById[verifier.VerifierId] = verifier;
                foreach (var evidenceId in verifier.EvidenceIds)
                {
                    // Evidence identities are globally unique links, never silently shared between obligations.
                    if (!expectedLinks.TryAdd(evidenceId, (mapping, verifier)))
                        Add("SPEC_DUPLICATE_EVIDENCE_ID", id, "An execution-link identity is assigned to multiple mappings.");
                }
            }
        }
        foreach (var requirement in requirements.Where(pair => pair.Value.Status != SpecStatus.Retired))
        {
            var mappings = manifest.Mappings.Where(m => m.SpecId == requirement.Key).ToArray();
            if (mappings.Length == 0) Add("SPEC_MISSING_MAPPING", requirement.Key, "Current requirements must have explicit traceability mappings.");
            var classes = mappings.SelectMany(m => m.Verifiers).Select(v => v.Class).Distinct().ToArray();
            if (requirement.Value.VerificationClasses.Any(c => !classes.Contains(c)))
                Add("SPEC_INCOMPLETE_CLASSES", requirement.Key, "A complementary required verification class has no mapping.");
        }
        foreach (var contract in contracts.Contracts.Where(c => c.ActivationState != ActivationState.Retired))
            foreach (var specId in contract.SpecIds)
                if (!seen.Contains((specId, contract.ContractId)))
                    Add("SPEC_MISSING_CONTRACT_MAPPING", specId, "A current contract's normative reference has no canonical mapping.");
        var executed = ValidateEvidence(evidence, expectedLinks, candidateSha, diagnostics.Count == 0, Add);
        var active = requirements.Where(r => r.Value.Status == SpecStatus.Active).ToArray();
        var coverage = new SpecCoverageSummary(candidateSha, registry.SchemaVersion, registry.RegistryVersion,
            Counts(active.Select(r => r.Key.Split('-')[1])),
            Counts(requirements.Where(r => r.Value.Status != SpecStatus.Retired).Select(r => r.Value.Severity.ToString())),
            Counts(manifest.Mappings.SelectMany(m => m.Verifiers).Select(v => v.Class.ToString())),
            active.Length, requirements.Count(r => r.Value.Status == SpecStatus.Deprecated),
            requirements.Count(r => r.Value.Status == SpecStatus.Retired), manifest.Mappings.Length,
            manifest.Mappings.Sum(m => m.Verifiers.Count(v => v.Class == SpecVerificationClass.Manual)),
            executed, expectedLinks.Count - executed,
            requirements.Count(r => r.Value.KnownLimitation != "None"));
        return SpecRegistryValidator.Result(diagnostics, coverage);
    }

    private static bool HasSourceIdentity(string source, string identity)
    {
        // This is source declaration linkage, not executable discovery or semantic proof.
        // Actual discovery/results must also resolve the full identity in the trusted execution receipt.
        if (identity.StartsWith("marker:", StringComparison.Ordinal))
            return source.Split('\n').Count(line => line.TrimEnd('\r') == "# verifier-id: " + identity[7..]) == 1;
        var segments = identity.Split('.');
        if (segments.Length < 3) return false;
        var method = segments[^1];
        var type = segments[^2];
        var space = string.Join('.', segments[..^2]);
        if (!Regex.IsMatch(method, "^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)) return false;
        return Regex.IsMatch(source, @"^\s*namespace\s+" + Regex.Escape(space) + @"\s*[;{]", RegexOptions.Multiline | RegexOptions.CultureInvariant) &&
            Regex.IsMatch(source, @"^\s*public\s+(?:sealed\s+|static\s+|partial\s+)*class\s+" + Regex.Escape(type) + @"\b", RegexOptions.Multiline | RegexOptions.CultureInvariant) &&
            Regex.Matches(source, @"^\s*public\s+(?:static\s+)?(?:async\s+)?(?:Task(?:<[^>]+>)?|void|ValueTask(?:<[^>]+>)?)\s+" +
            Regex.Escape(method) + @"\s*\(", RegexOptions.Multiline | RegexOptions.CultureInvariant).Count == 1;
    }

    private static int ValidateEvidence(SpecTraceabilityEvidenceDocument? evidence,
        Dictionary<string, (SpecMapping Mapping, SpecVerifier Verifier)> expected, string candidateSha, bool bindingsValid,
        Action<string, string, string> add)
    {
        if (evidence is null) return 0; // Structural mode remains visibly unexecuted, not PASS.
        if (evidence.SchemaVersion != 1)
        {
            add("SPEC_EVIDENCE_SCHEMA", "evidence", "Unsupported execution-link schema.");
            return 0;
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var passing = 0;
        foreach (var record in evidence.Records)
        {
            if (!expected.TryGetValue(record.EvidenceId, out var expectedLink))
            { add("SPEC_UNKNOWN_EVIDENCE", "evidence", "Execution evidence references an unknown mapping."); continue; }
            var (mapping, verifier) = expectedLink;
            var id = mapping.SpecId;
            if (!seen.Add(record.EvidenceId))
            { add("SPEC_DUPLICATE_EXECUTION", id, "Execution evidence identity is duplicated."); continue; }
            var valid = bindingsValid && record.SpecId == mapping.SpecId && record.RequirementVersion == mapping.RequirementVersion &&
                record.ContractId == mapping.ContractId && record.ContractDigest == mapping.ContractDigest &&
                record.VerifierId == verifier.VerifierId && record.VerifierVersion == verifier.Version &&
                record.VerifierSourceDigest == verifier.Source.Digest && record.CandidateSha == candidateSha &&
                record.VerifierEnabled && record.Outcome == EvidenceOutcome.Pass &&
                verifier.Class != SpecVerificationClass.Manual && !string.IsNullOrWhiteSpace(record.ExecutionReference) &&
                ContractValidator.DigestPattern().IsMatch(record.ExecutionDigest);
            if (!valid) add(verifier.Class == SpecVerificationClass.Manual ? "SPEC_MANUAL_NOT_PROVEN" : "SPEC_EXECUTION_BINDING",
                id, "Execution has invalid structural bindings, or is unavailable, nonpassing, disabled, manual, stale or bound to different requirement/contract/verifier/candidate bytes.");
            else passing++;
        }
        foreach (var link in expected.Where(e => !seen.Contains(e.Key) && e.Value.Verifier.Class != SpecVerificationClass.Manual))
            add("SPEC_MISSING_EXECUTION", link.Value.Mapping.SpecId, "A required machine verifier link has no execution evidence.");
        return passing;
    }

    private static SpecCoverageCount[] Counts(IEnumerable<string> values) => values.GroupBy(v => v, StringComparer.Ordinal)
        .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new SpecCoverageCount(g.Key, g.Count())).ToArray();
}
