namespace Coglatas.Domain.ProjectIde;

/// <summary>In-memory claim over existing contracts. This is neither a wire envelope nor a materialization receipt.</summary>
public sealed record RequirementEvidenceClaim
{
    public ProjectLocation Requirement { get; }
    public PlanEntityReference Target { get; }
    public CompilerResult Result { get; }

    public RequirementEvidenceClaim(ProjectLocation requirement, PlanEntityReference target, CompilerResult result)
    {
        ArgumentNullException.ThrowIfNull(requirement); ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(result);
        Requirement = requirement; Target = target; Result = result;
    }
}

/// <summary>Consistency of supplied claims only. Even a consistent claim cannot discharge a Requirement.</summary>
public sealed class RequirementEvidenceAssessment
{
    public CompilerInputManifest Input { get; }
    public ProjectLocation Requirement { get; }
    public IReadOnlyList<RequirementEvidenceClaim> Claims { get; }
    public IReadOnlyList<CompilerBindingFinding> Findings { get; }
    public CompilerRuleOutcome Outcome { get; }
    public CompilerFreshness Freshness { get; }
    public int LogicalOperationsConsumed { get; }

    internal RequirementEvidenceAssessment(CompilerInputManifest input, ProjectLocation requirement,
        IEnumerable<RequirementEvidenceClaim> claims, IEnumerable<CompilerBindingFinding> findings,
        CompilerExecutionState execution, CompilerFreshness freshness, int operations)
    {
        Input = input; Requirement = requirement; Freshness = freshness; LogicalOperationsConsumed = operations;
        Claims = Array.AsReadOnly(claims.OrderBy(item => item.Result.ResultDigest.Value, StringComparer.Ordinal)
            .ThenBy(item => item.Requirement.Data.CanonicalText, StringComparer.Ordinal).ThenBy(item => item.Target.Data.CanonicalText, StringComparer.Ordinal).ToArray());
        Findings = Array.AsReadOnly(findings.OrderBy(item => item.Diagnostic.Code, StringComparer.Ordinal).ToArray());
        Outcome = new(CoreCompilerRules.Requirements, 1, execution, CompilerCoverage.Partial, CompilerVerdict.Inconclusive,
            new(CompilerEvidenceKind.Missing, [], "coglatas.analysis.requirement-evidence-unavailable"));
    }
}

public static class RequirementEvidenceValidator
{
    /// <summary>No ambient IO, current authority lookup, receipt fabrication or eligibility policy.</summary>
    public static RequirementEvidenceAssessment Assess(CompilerInputManifest input, ProjectLocation requirement,
        IEnumerable<RequirementEvidenceClaim?>? claims = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(requirement);
        if (!input.Rules.Any(rule => rule.RuleId == CoreCompilerRules.Requirements && rule.Version == 1 && rule.Required))
            throw new ArgumentException("Evidence assessment requires the locked Requirement rule baseline.", nameof(input));
        var source = input.Source;
        var location = ProjectLocation.Create(source.Documents[0].DocumentId, source.Context);
        var findings = new Dictionary<string, CompilerBindingFinding>(StringComparer.Ordinal);
        var observed = new List<RequirementEvidenceClaim>();
        var remaining = input.ExecutionProfile.MaximumOperations;
        var execution = CompilerExecutionState.Completed;
        var freshness = CompilerFreshness.Undetermined;
        void Add(string code, RelationFindingState state)
        {
            var basis = state == RelationFindingState.Invalid ? new CompilerEvidenceBasis(CompilerEvidenceKind.ModelProof, [input.InputDigest, source.Digest])
                : new CompilerEvidenceBasis(CompilerEvidenceKind.Missing, [], code);
            findings.TryAdd(code, new(state, new(input, CoreCompilerRules.Requirements, 1, 3, code, location, basis)));
        }
        bool Spend(SourceJson data)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ProjectSemanticBinder.WithinBudget(data.Value, ref remaining)) return true;
            execution = CompilerExecutionState.ResourceExhausted;
            Add("coglatas.evidence.budget-exhausted", RelationFindingState.Unknown); return false;
        }
        RequirementEvidenceAssessment Finish()
        {
            Add("coglatas.analysis.requirement-evidence-unavailable", RelationFindingState.Unknown);
            return new(input, location, observed, findings.Values, execution, freshness,
                input.ExecutionProfile.MaximumOperations - Math.Max(0, remaining));
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Spend(source.Data)) return Finish();
            if (source.Documents.Count + source.Declarations.Value.GetArrayLength() > input.ExecutionProfile.MaximumDeclarations)
            { execution = CompilerExecutionState.ResourceExhausted; Add("coglatas.evidence.budget-exhausted", RelationFindingState.Unknown); return Finish(); }
            if (!requirement.Context.Equals(source.Context) || !source.Manifest.DocumentIds.Contains(requirement.DocumentId) ||
                requirement.EntityId is null || requirement.RelationId is not null || requirement.PropertyPath.Length != 0 || requirement.TextSpan is not null)
            { Add("coglatas.evidence.requirement-identity-mismatch", RelationFindingState.Invalid); return Finish(); }
            location = requirement;
            var matches = PlanDeclarationReader.Read(source).Where(item => item.DocumentId == requirement.DocumentId &&
                SourceFields.Identity<EntityId>(item.Data.Value, "entityId") == requirement.EntityId).ToArray();
            if (matches.Length != 1 || matches[0].Declaration is not RequirementPlanDeclaration declared)
            {
                Add(matches.Length == 1 && matches[0].Status == ValueDecodeStatus.Unsupported ? "coglatas.evidence.requirement-unsupported" :
                    "coglatas.evidence.requirement-unavailable", matches.Length == 1 && matches[0].Status == ValueDecodeStatus.Unsupported ? RelationFindingState.Unsupported : RelationFindingState.Invalid);
                return Finish();
            }
            var identities = new HashSet<CompilerResultId>();
            var stale = false;
            var examined = 0;
            foreach (var claim in claims ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (--remaining < 0 || ++examined > 1024)
                { execution = CompilerExecutionState.ResourceExhausted; Add("coglatas.evidence.budget-exhausted", RelationFindingState.Unknown); return Finish(); }
                if (claim is null) { Add("coglatas.evidence.claim-invalid", RelationFindingState.Invalid); continue; }
                observed.Add(claim);
            }
            foreach (var claim in observed.OrderBy(item => item.Result.ResultDigest.Value, StringComparer.Ordinal)
                .ThenBy(item => item.Requirement.Data.CanonicalText, StringComparer.Ordinal).ThenBy(item => item.Target.Data.CanonicalText, StringComparer.Ordinal))
            {
                if (!Spend(claim.Result.Data)) break;
                if (!identities.Add(claim.Result.ResultId)) Add("coglatas.evidence.duplicate", RelationFindingState.Invalid);
                if (!claim.Requirement.Equals(requirement)) Add("coglatas.evidence.requirement-identity-mismatch", RelationFindingState.Invalid);
                if (!claim.Target.Data.Equals(declared.Target.Data)) Add("coglatas.evidence.target-mismatch", RelationFindingState.Invalid);
                if (!claim.Result.Input.InputDigest.Equals(input.InputDigest) || !claim.Result.Input.Data.Equals(input.Data))
                { stale = true; Add("coglatas.evidence.input-mismatch", RelationFindingState.Invalid); }
                var outcome = claim.Result.RuleOutcomes.SingleOrDefault(item => item.RuleId == CoreCompilerRules.Requirements && item.Version == 1);
                if (outcome is null || outcome.Evidence.Kind == CompilerEvidenceKind.Missing)
                    Add("coglatas.evidence.basis-unavailable", RelationFindingState.Unknown);
                else
                {
                    if (CompilerContractTokens.Evidence(outcome.Evidence.Kind) != declared.EvidenceKind)
                        Add("coglatas.evidence.kind-mismatch", RelationFindingState.Invalid);
                    if (outcome.Evidence.Kind is CompilerEvidenceKind.Heuristic or CompilerEvidenceKind.HumanApproval)
                        Add("coglatas.evidence.technical-proof-unavailable", RelationFindingState.Unsupported);
                    if (!outcome.Evidence.Premises.Contains(input.InputDigest) || !outcome.Evidence.Premises.Contains(source.Digest))
                        Add("coglatas.evidence.premise-mismatch", RelationFindingState.Invalid);
                }
                if (claim.Result.Execution != CompilerExecutionState.Completed || outcome?.Execution != CompilerExecutionState.Completed)
                    Add("coglatas.evidence.execution-incomplete", RelationFindingState.Unknown);
                if (claim.Result.Coverage != CompilerCoverage.Complete || outcome?.Coverage != CompilerCoverage.Complete || !claim.Result.EvidenceComplete)
                    Add("coglatas.evidence.coverage-incomplete", RelationFindingState.Unknown);
                if (outcome?.Verdict != CompilerVerdict.Pass || claim.Result.Verdict != CompilerVerdict.Pass)
                    Add("coglatas.evidence.claim-not-passed", RelationFindingState.Unknown);
            }
            cancellationToken.ThrowIfCancellationRequested();
            freshness = stale ? CompilerFreshness.Stale : observed.Count == 0 ? CompilerFreshness.Undetermined : CompilerFreshness.Current;
            if (identities.Count > 1) Add("coglatas.evidence.ambiguous", RelationFindingState.Invalid);
            if (observed.Count == 0) Add("coglatas.evidence.missing", RelationFindingState.Unknown);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { execution = CompilerExecutionState.Cancelled; Add("coglatas.evidence.cancelled", RelationFindingState.Unknown); }
        return Finish();
    }
}
