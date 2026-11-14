namespace Coglatas.Domain.ProjectIde;

public enum CompilerDependencyAnalysisState { Evaluated, IncompleteBinding, ResourceLimit }

/// <summary>Actual per-region validation and rule execution, certified by the whole original input.
/// The internal binding-only manifest is not the dependency's original compiler input.</summary>
public sealed class CompilerDependencyRegion
{
    public CompilerBindingRegion Binding { get; }
    public CompilerSemanticValidation? Validation => Compilation.Validation;
    public IReadOnlyList<CompilerDiagnostic> Diagnostics => Compilation.Result.Diagnostics;
    public IReadOnlyList<CompilerRuleOutcome> Rules => Compilation.Result.RuleOutcomes;
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }
    internal CoreCompilation Compilation { get; }
    internal CompilerDependencyRegion(CompilerBindingRegion binding, CoreCompilation compilation)
    {
        Binding = binding; Compilation = compilation;
        Data = SourceJson.FromObject(new
        {
            format = "coglatas.compiler-dependency-region/1", rootInputDigest = binding.RootInput.InputDigest.Data.Value,
            bindingRegionDigest = binding.Digest.Data.Value, context = binding.Source.Context.Data.Value,
            sourceDigest = binding.Source.Digest.Data.Value, execution = CompilerContractTokens.Execution(compilation.Result.Execution),
            diagnostics = Diagnostics.Select(item => item.Data.Value).ToArray(), rules = Rules.Select(item => item.Data.Value).ToArray(),
            ir = Validation?.Ir?.Data.Value, sourceMap = Validation?.Ir?.SourceMap.Value,
            logicalOperationsConsumed = compilation.LogicalOperationsConsumed - binding.Model.LogicalOperationsConsumed
        }, binding.RootInput.Source.Data.Limits);
        Digest = ContentDigest.Compute("coglatas.compiler-dependency-region/1", Data.ToCanonicalBytes());
    }
}

/// <summary>Compact retained evidence for an evaluated failed rule, independent of receipt publication.</summary>
public sealed class CompilerDependencyFailure
{
    public SourceRevisionContext Context { get; }
    public string RuleId { get; }
    public int RuleVersion { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }
    internal CompilerDependencyFailure(CompilerBindingRegion binding, CoreCompilation compilation, CompilerRuleOutcome outcome)
    {
        Context = binding.Source.Context; RuleId = outcome.RuleId; RuleVersion = outcome.Version;
        Data = SourceJson.FromObject(new { rootInputDigest = binding.RootInput.InputDigest.Data.Value,
            context = Context.Data.Value, bindingDigest = binding.Digest.Data.Value, ruleId = RuleId, ruleVersion = RuleVersion,
            outcomeDigest = ContentDigest.Compute("coglatas.rule-outcome/1", outcome.Data.ToCanonicalBytes()).Data.Value,
            resultDigest = compilation.Result.ResultDigest.Data.Value });
        Digest = ContentDigest.Compute("coglatas.compiler-dependency-failure/1", Data.ToCanonicalBytes());
    }
}

/// <summary>Input-bound regional analysis, not a global cross-region graph proof or an executable plan.
/// Normal Compiler publication retains its global lowering guard.</summary>
public sealed class CompilerDependencyAnalysis
{
    public const string Format = "coglatas.compiler-dependency-analysis/1";
    public CompilerInputManifest Input { get; }
    public CompilerBindingClosure Binding { get; }
    public CompilerDependencyAnalysisState State { get; }
    public IReadOnlyList<CompilerDependencyRegion> Regions { get; }
    public IReadOnlyList<CompilerDependencyFailure> KnownFailures { get; }
    public ContentDigest KnownFailureDigest { get; }
    public int LogicalOperationsConsumed { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }
    private CompilerDependencyAnalysis(CompilerBindingClosure binding, CompilerDependencyAnalysisState state,
        IEnumerable<CompilerDependencyRegion> regions, int consumed, IEnumerable<CompilerDependencyFailure> knownFailures)
    {
        Input = binding.Input; Binding = binding; State = state; LogicalOperationsConsumed = consumed;
        Regions = new ImmutableListView<CompilerDependencyRegion>(regions.OrderBy(item => item.Binding.Source.Context.Data.CanonicalText, StringComparer.Ordinal));
        KnownFailures = new ImmutableListView<CompilerDependencyFailure>(knownFailures.OrderBy(item => item.RuleId, StringComparer.Ordinal));
        KnownFailureDigest = ContentDigest.Compute("coglatas.compiler-dependency-failures/1",
            SourceJson.FromObject(KnownFailures.Select(item => item.Data.Value).ToArray()).ToCanonicalBytes());
        Data = SourceJson.FromObject(new
        {
            format = Format, rootInputDigest = Input.InputDigest.Data.Value, bindingClosureDigest = binding.Digest.Data.Value,
            state = state.ToString(), logicalOperationsConsumed = consumed, knownFailureDigest = KnownFailureDigest.Data.Value,
            regions = Regions.Select(item => new { digest = item.Digest.Data.Value, region = item.Data.Value }).ToArray(),
            globalGraphProof = "coglatas.not-produced"
        }, Input.Source.Data.Limits);
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }

    internal static CoreCompilation Attach(CoreCompilation local, CancellationToken cancellationToken, PlanDeclarationReadCache? cache)
    {
        var model = local.Model; var validation = local.Validation; var input = local.Input;
        if (model is null || validation is null || !validation.SuppliesRead ||
            local.Result.Execution != CompilerExecutionState.Completed || !model.BindingComplete ||
            !model.References.Any(reference => reference.SymbolKey is { } key &&
                model.Symbols.Any(symbol => symbol.Key == key && !symbol.Context.Equals(input.Source.Context))))
            return local;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var closure = CompilerBindingClosure.BuildWithinBudget(model,
                Math.Max(0, input.ExecutionProfile.MaximumOperations - local.LogicalOperationsConsumed), cancellationToken, cache);
            var remaining = input.ExecutionProfile.MaximumOperations - closure.LogicalOperationsConsumed;
            var regions = new List<CompilerDependencyRegion>();
            var state = closure.State == CompilerBindingClosureState.Complete ? CompilerDependencyAnalysisState.Evaluated
                : closure.State == CompilerBindingClosureState.ResourceLimit ? CompilerDependencyAnalysisState.ResourceLimit
                : CompilerDependencyAnalysisState.IncompleteBinding;
            var prepared = new List<(CompilerBindingRegion Binding, CompilerSemanticValidation Validation)>();
            var evaluatedRules = new List<CompilerRuleOutcome>();
            var knownFailures = new Dictionary<string, CompilerDependencyFailure>(StringComparer.Ordinal);
            var publicationLimited = false;
            if (state == CompilerDependencyAnalysisState.Evaluated)
            {
                // Validate all exact regional edge owners before interpreting any cross-region attachment.
                foreach (var region in closure.Regions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (remaining <= 0) { state = CompilerDependencyAnalysisState.ResourceLimit; break; }
                    var checkedRegion = CompilerSemanticValidation.ValidateRegion(region.Model, validation.ReadUnits, validation.ReadCalendars, cancellationToken, remaining);
                    remaining -= checkedRegion.LogicalOperationsConsumed - region.Model.LogicalOperationsConsumed;
                    prepared.Add((region, checkedRegion));
                    if (checkedRegion.Findings.Any(item => item.Diagnostic.Code.Contains("budget", StringComparison.Ordinal)))
                    { state = CompilerDependencyAnalysisState.ResourceLimit; break; }
                }
                var attachments = prepared.Where(item => item.Validation.Ir is not null)
                    .SelectMany(item => item.Binding.Model.Relations.Select(row => row.Relation).OfType<TypedRelation>())
                    .Where(relation => relation.Kind == RelationKinds.Constrains)
                    .Select(relation => (relation.Source.Reference.Context, relation.Source.Reference.EntityId)).ToHashSet();
                foreach (var (region, checkedRegion) in prepared)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (remaining <= 0) { state = CompilerDependencyAnalysisState.ResourceLimit; break; }
                    var result = PureCoreCompiler.CompileRegion(region.Model, checkedRegion, attachments, remaining, cancellationToken);
                    if (result.Result.Execution == CompilerExecutionState.Cancelled) cancellationToken.ThrowIfCancellationRequested();
                    remaining -= result.LogicalOperationsConsumed - checkedRegion.LogicalOperationsConsumed;
                    evaluatedRules.AddRange(result.Result.RuleOutcomes);
                    foreach (var failed in result.Result.RuleOutcomes.Where(item => item.Verdict == CompilerVerdict.Fail))
                        knownFailures.TryAdd(failed.RuleId, new(region, result, failed));
                    if (!publicationLimited)
                        try { regions.Add(new(region, result)); }
                        catch (FormatException)
                        {
                            // Receipt size limits do not erase already evaluated truth or prevent
                            // later bounded rule execution from retaining another known failure.
                            state = CompilerDependencyAnalysisState.ResourceLimit; regions.Clear(); publicationLimited = true;
                        }
                    if (result.Result.Execution == CompilerExecutionState.ResourceExhausted)
                    { state = CompilerDependencyAnalysisState.ResourceLimit; break; }
                }
            }
            CompilerDependencyAnalysis analysis;
            try { analysis = new(closure, state, regions, input.ExecutionProfile.MaximumOperations - Math.Max(0, remaining), knownFailures.Values); }
            catch (FormatException)
            {
                // Discard an oversized publication; its evaluated prefix never becomes a complete receipt.
                state = CompilerDependencyAnalysisState.ResourceLimit;
                analysis = new(closure, state, [], input.ExecutionProfile.MaximumOperations - Math.Max(0, remaining), knownFailures.Values);
            }
            var root = ProjectLocation.Create(input.Source.Documents[0].DocumentId, input.Source.Context);
            var diagnostics = local.Result.Diagnostics.ToList();
            var bindingFindings = closure.Regions.SelectMany(region => region.Findings.Select(finding => (Region: region, Finding: finding))).ToArray();
            foreach (var (region, finding) in bindingFindings)
            {
                var diagnostic = finding.Diagnostic;
                if (region.IsRoot) diagnostics.Add(diagnostic);
                else diagnostics.Add(new(input, diagnostic.RuleId, diagnostic.RuleVersion, 3, "coglatas.analysis.dependency-finding", root,
                    finding.State == RelationFindingState.Invalid
                        ? new(CompilerEvidenceKind.ModelProof, [input.InputDigest, region.Digest, closure.Digest])
                        : new(CompilerEvidenceKind.Missing, [], "coglatas.analysis.dependency-basis-incomplete"),
                    diagnostic.Severity, region.Digest.Value + ":" + diagnostic.DiagnosticId));
            }
            foreach (var region in analysis.Regions)
                foreach (var diagnostic in region.Diagnostics)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (region.Binding.IsRoot) { diagnostics.Add(diagnostic); continue; }
                    // Keep the foreign primary location in the regional receipt. This is an explicit
                    // root summary, never a relabelled foreign diagnostic or a navigation authority.
                    var evidence = diagnostic.Evidence.Kind == CompilerEvidenceKind.ModelProof
                        ? new CompilerEvidenceBasis(CompilerEvidenceKind.ModelProof, [input.InputDigest, region.Binding.Digest, region.Digest])
                        : new CompilerEvidenceBasis(CompilerEvidenceKind.Missing, [], "coglatas.analysis.dependency-basis-incomplete");
                    diagnostics.Add(new(input, diagnostic.RuleId, diagnostic.RuleVersion, 3, "coglatas.analysis.dependency-finding", root,
                        evidence, diagnostic.Severity, region.Digest.Value + ":" + diagnostic.DiagnosticId));
                }
            foreach (var failure in analysis.KnownFailures.Where(failure => !analysis.Regions.Any(region =>
                region.Binding.Source.Context.Equals(failure.Context) && region.Rules.Any(rule => rule.RuleId == failure.RuleId && rule.Verdict == CompilerVerdict.Fail))))
                diagnostics.Add(new(input, failure.RuleId, failure.RuleVersion, 3, "coglatas.analysis.dependency-known-failure", root,
                    new(CompilerEvidenceKind.ModelProof, [input.InputDigest, failure.Digest, analysis.KnownFailureDigest]), discriminator: failure.Digest.Value));
            var incomplete = state != CompilerDependencyAnalysisState.Evaluated;
            if (incomplete)
                diagnostics.Add(new(input, CoreCompilerRules.Relations, 1, 3,
                    state == CompilerDependencyAnalysisState.ResourceLimit ? "coglatas.analysis.dependency-budget-exhausted" : "coglatas.analysis.dependency-binding-incomplete",
                    root, new(CompilerEvidenceKind.Missing, [], "coglatas.analysis.dependency-incomplete")));
            var rules = local.Result.RuleOutcomes.Select(rule =>
            {
                var evaluated = evaluatedRules.Where(item => item.RuleId == rule.RuleId).Prepend(rule).ToArray();
                var failed = evaluated.Any(item => item.Verdict == CompilerVerdict.Fail) ||
                    bindingFindings.Any(item => item.Finding.Diagnostic.RuleId == rule.RuleId && item.Finding.State == RelationFindingState.Invalid);
                var partial = incomplete || evaluated.Any(item => item.Coverage != CompilerCoverage.Complete);
                var exhausted = state == CompilerDependencyAnalysisState.ResourceLimit || evaluated.Any(item => item.Execution == CompilerExecutionState.ResourceExhausted);
                var execution = exhausted ? CompilerExecutionState.ResourceExhausted : rule.Execution;
                var verdict = failed ? CompilerVerdict.Fail : execution == CompilerExecutionState.NotRun ? CompilerVerdict.NotRun
                    : partial ? CompilerVerdict.Inconclusive : rule.Verdict;
                // A non-run rule cannot acquire a verdict merely because another region failed.
                if (execution == CompilerExecutionState.NotRun && failed) execution = CompilerExecutionState.Completed;
                return new CompilerRuleOutcome(rule.RuleId, rule.Version, execution, partial ? CompilerCoverage.Partial : rule.Coverage, verdict,
                    partial ? new(CompilerEvidenceKind.Missing, [], "coglatas.analysis.dependency-incomplete")
                        : new(CompilerEvidenceKind.ModelProof, [input.InputDigest, analysis.Digest]));
            }).ToArray();
            var stage = new CompilerStageOutcome("coglatas.dependency-analysis", 1,
                state == CompilerDependencyAnalysisState.ResourceLimit ? CompilerExecutionState.ResourceExhausted : CompilerExecutionState.Completed,
                incomplete ? CompilerCoverage.Partial : CompilerCoverage.Complete, input.InputDigest, [analysis.Digest],
                incomplete ? "coglatas.analysis.dependency-incomplete" : null);
            CompilerResult combined;
            try
            {
                combined = new(input, state == CompilerDependencyAnalysisState.ResourceLimit ? CompilerExecutionState.ResourceExhausted : local.Result.Execution,
                    CompilerCoverage.Partial, rules, diagnostics.DistinctBy(item => item.DiagnosticId.Value), local.Result.Stages.Append(stage), false, local.Result.Artifacts);
            }
            catch (FormatException)
            {
                var limitedRules = rules.Select(rule => new CompilerRuleOutcome(rule.RuleId, rule.Version, CompilerExecutionState.ResourceExhausted,
                    CompilerCoverage.Partial, rule.Verdict == CompilerVerdict.Fail ? CompilerVerdict.Fail : CompilerVerdict.Inconclusive,
                    new(CompilerEvidenceKind.Missing, [], "coglatas.analysis.publication-budget")));
                combined = new(input, CompilerExecutionState.ResourceExhausted, CompilerCoverage.Partial, limitedRules,
                    [new CompilerDiagnostic(input, CoreCompilerRules.Schema, 1, 3, "coglatas.analysis.publication-budget", root,
                        new(CompilerEvidenceKind.Missing, [], "coglatas.analysis.publication-budget"))],
                    [new CompilerStageOutcome("coglatas.publication", 1, CompilerExecutionState.ResourceExhausted, CompilerCoverage.Partial,
                        input.InputDigest, [], "coglatas.analysis.publication-budget")], false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new(input, model, validation, combined, analysis.LogicalOperationsConsumed, analysis);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { return new(input, null, null, new(input, CompilerExecutionState.Cancelled, CompilerCoverage.None, [], [], [], false)); }
    }
}
