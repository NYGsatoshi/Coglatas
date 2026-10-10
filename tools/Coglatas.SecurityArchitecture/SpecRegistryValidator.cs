using System.Text;
using System.Text.RegularExpressions;

namespace Coglatas.SecurityArchitecture;

public static partial class SpecRegistryValidator
{
    [GeneratedRegex("^SPEC-(ARCH|AUTH|RT|STATE|API|UI)-[A-Z0-9]+(?:-[A-Z0-9]+)*$")]
    internal static partial Regex IdPattern();
    [GeneratedRegex("^ARCH-[A-Z0-9]+(?:-[A-Z0-9]+)*$")]
    private static partial Regex ArchitectureIdPattern();
    [GeneratedRegex("^[a-z][a-z0-9-]*$")]
    private static partial Regex AnchorPattern();

    public static async Task<SpecValidationResult> ValidateAsync(SpecRegistryDocument registry,
        string expectedSpecificationRevision, Func<SpecSource, Task<byte[]?>> readSource,
        SpecRegistryDocument? baseline = null)
    {
        var diagnostics = new List<Diagnostic>();
        void Add(string rule, string subject, string reason) => diagnostics.Add(new(rule, subject, reason));
        if (registry.SchemaVersion != 1 || registry.RegistryVersion < 1)
            Add("SPEC_SCHEMA", "registry", "Supported schema and positive registry version are required.");
        if (!RepositoryArtifactReader.RevisionPattern().IsMatch(expectedSpecificationRevision) ||
            registry.SpecificationRevision != expectedSpecificationRevision)
            Add("SPEC_SOURCE_REVISION", "registry", "Registry source revision differs from independently supplied specification revision.");
        if (registry.Requirements.Length == 0)
            Add("SPEC_REGISTRY_EMPTY", "registry", "An empty registry cannot establish requirement coverage.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var uniqueSources = new HashSet<(string, string, string?)>();
        foreach (var requirement in registry.Requirements)
        {
            var id = IdPattern().IsMatch(requirement.SpecId) ? requirement.SpecId : "invalid";
            if (id == "invalid") Add("SPEC_ID", id, "Invalid or unknown SPEC identity family.");
            if (!ids.Add(id)) Add("SPEC_DUPLICATE_ID", id, "Each stable identity must have exactly one history.");
            if (requirement.Versions.Length == 0)
            { Add("SPEC_HISTORY", id, "A stable identity needs allocation and version history."); continue; }
            for (var i = 0; i < requirement.Versions.Length; i++)
            {
                var version = requirement.Versions[i];
                if (version.Version != i + 1)
                    Add("SPEC_VERSION", id, "Requirement versions must be contiguous, ordered and start at one.");
                if (!Enum.IsDefined(version.Status) || !Enum.IsDefined(version.Severity))
                    Add("SPEC_ENUM", id, "Unknown status or severity.");
                if (string.IsNullOrWhiteSpace(version.NormativeStatement) || string.IsNullOrWhiteSpace(version.Owner) ||
                    string.IsNullOrWhiteSpace(version.KnownLimitation) || string.IsNullOrWhiteSpace(version.ChangeReference))
                    Add("SPEC_FIELD", id, "Statement, owner, explicit limitation and change review reference are required.");
                if (!ContractValidator.DigestPattern().IsMatch(version.StatementDigest) ||
                    SpecDigest.Text(version.NormativeStatement) != version.StatementDigest)
                    Add("SPEC_STATEMENT_DIGEST", id, "The exact UTF-8 normative statement digest does not match.");
                if (version.VerificationClasses.Length == 0 || version.VerificationClasses.Any(c => !Enum.IsDefined(c)) ||
                    version.VerificationClasses.Distinct().Count() != version.VerificationClasses.Length)
                    Add("SPEC_VERIFICATION_CLASS", id, "Closed, nonempty and unique verification classes are required.");
                if (version.Severity == SpecSeverity.Manual &&
                    !version.VerificationClasses.SequenceEqual([SpecVerificationClass.Manual]))
                    Add("SPEC_MANUAL_CLASS", id, "Manual severity must remain exclusively manual.");
                if (version.ArchitectureRuleIds.Any(a => !ArchitectureIdPattern().IsMatch(a)) ||
                    version.ArchitectureRuleIds.Distinct(StringComparer.Ordinal).Count() != version.ArchitectureRuleIds.Length)
                    Add("SPEC_ARCH_ID", id, "Architecture rule identities must be valid and unique.");
                if (version.Status == SpecStatus.Retired && string.IsNullOrWhiteSpace(version.RetirementReason))
                    Add("SPEC_RETIREMENT", id, "Retirement must retain an explicit reason and review reference.");
                if (version.Status != SpecStatus.Retired && (version.RetirementReason is not null || version.SuccessorSpecIds is not null))
                    Add("SPEC_RETIREMENT", id, "Retirement metadata is unavailable on active or deprecated versions.");
                if (version.SuccessorSpecIds is { } successors && (successors.Distinct(StringComparer.Ordinal).Count() != successors.Length ||
                    successors.Any(s => !IdPattern().IsMatch(s) || s == id)))
                    Add("SPEC_SUCCESSOR", id, "Successors must be valid, distinct identities other than the retired identity.");
                if (i > 0)
                {
                    var previous = requirement.Versions[i - 1];
                    if (previous.Status == SpecStatus.Retired || previous.Status == SpecStatus.Deprecated && version.Status == SpecStatus.Active)
                        Add("SPEC_ID_REUSE", id, "Retired identities cannot be reused and deprecated identities cannot be silently reactivated.");
                    if (previous.StatementDigest != version.StatementDigest)
                        Add("SPEC_SEMANTIC_REPLACEMENT", id, "Changing an identity's normative statement requires a new identity and reviewed successor relationship.");
                }
                var sourceBytes = await ValidateSourceAsync(version.Source, id, readSource, Add);
                if (sourceBytes is not null && !Encoding.UTF8.GetString(sourceBytes).Contains(version.NormativeStatement, StringComparison.Ordinal))
                    Add("SPEC_STATEMENT_SOURCE", id, "The registered statement is not an exact excerpt of the pinned normative source.");
            }
            var latest = requirement.Versions[^1];
            if (latest.Status != SpecStatus.Retired && latest.Source.Revision != expectedSpecificationRevision)
                Add("SPEC_CURRENT_SOURCE", id, "Current obligations must resolve against the independently supplied canonical source revision.");
            if (latest.Status != SpecStatus.Retired &&
                !uniqueSources.Add((latest.Source.Path, latest.StatementDigest, latest.Source.Anchor)))
                Add("SPEC_DUPLICATE_REQUIREMENT", id, "An active normative source/statement already has another identity.");
        }
        foreach (var requirement in registry.Requirements)
            foreach (var successor in requirement.Versions.LastOrDefault()?.SuccessorSpecIds ?? [])
                if (!ids.Contains(successor)) Add("SPEC_SUCCESSOR", requirement.SpecId, "A successor identity is absent from the retained registry.");
        if (baseline is not null)
        {
            // Historical sources keep their own revision; structural validation does not authenticate the baseline's authority.
            var baselineResult = await ValidateAsync(baseline, baseline.SpecificationRevision, readSource);
            diagnostics.AddRange(baselineResult.Diagnostics.Select(diagnostic => diagnostic with
            { RuleId = "SPEC_BASELINE_" + diagnostic.RuleId[5..] }));
            if (baselineResult.Valid) ValidateTransition(baseline, registry, Add);
        }
        return Result(diagnostics);
    }

    private static void ValidateTransition(SpecRegistryDocument baseline, SpecRegistryDocument candidate,
        Action<string, string, string> add)
    {
        if (candidate.RegistryVersion < baseline.RegistryVersion)
            add("SPEC_REGISTRY_ROLLBACK", "registry", "Registry versions cannot decrease.");
        var changed = SpecDigest.Document(baseline.Requirements) != SpecDigest.Document(candidate.Requirements);
        if (changed && candidate.RegistryVersion <= baseline.RegistryVersion)
            add("SPEC_REGISTRY_VERSION", "registry", "An allocation or lifecycle change must increment the registry version.");
        foreach (var old in baseline.Requirements)
        {
            var current = candidate.Requirements.FirstOrDefault(r => r.SpecId == old.SpecId);
            if (current is null)
            { add("SPEC_HISTORY_REMOVED", old.SpecId, "Previously allocated identities, including retired identities, cannot be removed."); continue; }
            if (current.Versions.Length < old.Versions.Length ||
                !current.Versions.Take(old.Versions.Length).Select(SpecDigest.Document)
                    .SequenceEqual(old.Versions.Select(SpecDigest.Document), StringComparer.Ordinal))
                add("SPEC_HISTORY_REWRITTEN", old.SpecId, "Previously recorded versions must remain byte-equivalent in canonical serialization.");
        }
    }

    internal static async Task<byte[]?> ValidateSourceAsync(SpecSource source, string id,
        Func<SpecSource, Task<byte[]?>> readSource, Action<string, string, string> add)
    {
        if (!RepositoryArtifactReader.RevisionPattern().IsMatch(source.Revision) || !RepositoryArtifactReader.IsSafePath(source.Path) ||
            !ContractValidator.DigestPattern().IsMatch(source.Digest) ||
            source.Anchor is not null && !AnchorPattern().IsMatch(source.Anchor))
        { add("SPEC_SOURCE", id, "Source needs an immutable revision, safe repository-relative path, SHA256 digest and valid optional anchor."); return null; }
        var bytes = await readSource(source);
        if (bytes is null) { add("SPEC_SOURCE_MISSING", id, "The referenced source is missing at its exact revision."); return null; }
        if (SpecDigest.Bytes(bytes) != source.Digest)
            add("SPEC_SOURCE_DIGEST", id, "The referenced source bytes differ from the pinned digest.");
        if (source.Anchor is { } anchor)
        {
            // Explicit anchors avoid renderer-specific heading slugs and fragile line numbers.
            var text = Encoding.UTF8.GetString(bytes);
            var expressions = new[] { "<a id=\"" + anchor + "\"></a>", "<span id=\"" + anchor + "\"></span>" };
            var count = expressions.Sum(expression => text.Split(expression).Length - 1);
            if (count != 1) add("SPEC_SOURCE_ANCHOR", id, "The exact explicit source anchor must exist exactly once.");
        }
        return bytes;
    }

    internal static SpecValidationResult Result(IEnumerable<Diagnostic> diagnostics, SpecCoverageSummary? coverage = null)
    {
        var result = ContractValidator.Result(diagnostics);
        // Offline metadata never authenticates an owner review or an execution artifact.
        return new(result.Valid, false, "UNVERIFIED", "UNVERIFIED", result.Diagnostics, coverage);
    }
}
