namespace Coglatas.Domain.ProjectIde;

/// <summary>Structural comparison with an existing Source binding; never Resource acquisition or authorization.</summary>
public sealed class ResourceReferenceAssessment
{
    public TypedProjectReference Reference { get; }
    public ResourceBindingDeclaration Binding { get; }
    public IReadOnlyList<CompilerBindingFinding> Findings { get; }
    public CompilerExecutionState Execution { get; }
    public ReferenceResolutionState Resolution => ReferenceResolutionState.Unsupported;
    public CompilerFreshness Freshness => CompilerFreshness.Undetermined;

    internal ResourceReferenceAssessment(TypedProjectReference reference, ResourceBindingDeclaration binding,
        IEnumerable<CompilerBindingFinding> findings, CompilerExecutionState execution)
    {
        Reference = reference; Binding = binding; Execution = execution;
        Findings = Array.AsReadOnly(findings.OrderBy(item => item.Diagnostic.Code, StringComparer.Ordinal).ToArray());
    }
}

public static class ResourceReferenceValidator
{
    public static ResourceReferenceAssessment Assess(CompilerInputManifest input, TypedProjectReference reference,
        ResourceBindingDeclaration binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(reference); ArgumentNullException.ThrowIfNull(binding);
        if (reference.Kind != "coglatas.resource-reference") throw new ArgumentException("A Tenant Resource reference is required.", nameof(reference));
        if (!input.Rules.Any(rule => rule.RuleId == CoreCompilerRules.Resources && rule.Version == 1 && rule.Required))
            throw new ArgumentException("Resource assessment requires the locked Resource rule baseline.", nameof(input));
        var source = input.Source;
        var root = ProjectLocation.Create(source.Documents[0].DocumentId, source.Context);
        var findings = new Dictionary<string, CompilerBindingFinding>(StringComparer.Ordinal);
        var execution = CompilerExecutionState.Completed;
        void Add(string code, RelationFindingState state)
        {
            var basis = state == RelationFindingState.Invalid ? new CompilerEvidenceBasis(CompilerEvidenceKind.ModelProof, [input.InputDigest, source.Digest])
                : new CompilerEvidenceBasis(CompilerEvidenceKind.Missing, [], code);
            findings.TryAdd(code, new(state, new(input, CoreCompilerRules.Resources, 1, 3, code, root, basis)));
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = input.ExecutionProfile.MaximumOperations;
            if (source.Documents.Count + source.Declarations.Value.GetArrayLength() > input.ExecutionProfile.MaximumDeclarations ||
                !ProjectSemanticBinder.WithinBudget(source.Data.Value, ref remaining) ||
                !ProjectSemanticBinder.WithinBudget(reference.Data.Value, ref remaining) ||
                !ProjectSemanticBinder.WithinBudget(binding.Data.Value, ref remaining))
            { execution = CompilerExecutionState.ResourceExhausted; Add("coglatas.resource.budget-exhausted", RelationFindingState.Unknown); }
            else
            {
                var declarations = PlanDeclarationReader.Read(source);
                var declared = declarations.Where(item => item.DocumentId == binding.DocumentId &&
                    item.Declaration is ResourceBindingDeclaration resource && resource.EntityId == binding.EntityId).ToArray();
                if (declared.Length != 1 || !declared[0].Data.Equals(binding.Data))
                    Add("coglatas.resource.binding-mismatch", RelationFindingState.Invalid);
                if (!reference.Origin.Context.Equals(source.Context) || !source.Manifest.DocumentIds.Contains(reference.Origin.DocumentId))
                    Add("coglatas.resource.origin-mismatch", RelationFindingState.Invalid);
                if (reference.Origin.EntityId is { } origin && !declarations.Any(item => item.DocumentId == reference.Origin.DocumentId &&
                    SourceFields.Identity<EntityId>(item.Data.Value, "entityId") == origin))
                    Add("coglatas.resource.origin-mismatch", RelationFindingState.Invalid);
                if (reference.TenantId != source.TenantId || binding.TenantId != source.TenantId || reference.ResourceId != binding.ResourceId)
                    Add("coglatas.resource.identity-mismatch", RelationFindingState.Invalid);
                if (reference.ValidateTargetContext(source.Context) != ReferenceContextStatus.MatchesPinnedContext)
                    Add("coglatas.resource.context-unavailable", RelationFindingState.Unsupported);
                if (reference.ExpectedKind != binding.ResourceKind)
                    Add("coglatas.resource.kind-mismatch", RelationFindingState.Invalid);
                if (reference.ManifestVersion != binding.SnapshotVersion || !reference.ManifestDigest.Equals(binding.SnapshotDigest))
                    Add("coglatas.resource.pin-mismatch", RelationFindingState.Invalid);
                if (!input.SourceCoverageComplete) Add("coglatas.resource.coverage-incomplete", RelationFindingState.Unknown);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { execution = CompilerExecutionState.Cancelled; Add("coglatas.resource.cancelled", RelationFindingState.Unknown); }
        Add("coglatas.analysis.resource-snapshot-unavailable", RelationFindingState.Unknown);
        return new(reference, binding, findings.Values, execution);
    }
}
