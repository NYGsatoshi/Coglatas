namespace Coglatas.Domain.ProjectIde;

public sealed class CoreCompilation
{
    public CompilerInputManifest Input { get; }
    public PartialSemanticModel? Model { get; }
    public CompilerSemanticValidation? Validation { get; }
    public CompilerResult Result { get; }
    public CompilerDependencyAnalysis? Dependencies { get; }
    public CompilerScopedPublication? Publication { get; }
    public int LogicalOperationsConsumed { get; }
    public ProjectPredicatePremiseRecord? PredicatePremises { get; private init; }
    internal CoreCompilation(CompilerInputManifest input, PartialSemanticModel? model, CompilerSemanticValidation? validation, CompilerResult result, int logicalOperationsConsumed = 0, CompilerDependencyAnalysis? dependencies = null, CompilerScopedPublication? publication = null)
    { Input = input; Model = model; Validation = validation; Result = result; LogicalOperationsConsumed = logicalOperationsConsumed; Dependencies = dependencies; Publication = publication; }
    internal CoreCompilation WithPredicatePremises(ProjectPredicatePremiseRecord record)
    {
        if (!record.MatchesResult(this)) throw new ArgumentException("Predicate record differs from the original compilation.", nameof(record));
        return new(Input, Model, Validation, Result, LogicalOperationsConsumed, Dependencies, Publication) { PredicatePremises = record };
    }
}

/// <summary>Full-reference pure compilation for the declared supported scope. It starts no run and grants no merge authority.</summary>
public static class PureCoreCompiler
{
    private static readonly HashSet<string> SupportedRules = new(StringComparer.Ordinal)
    { CoreCompilerRules.Schema, CoreCompilerRules.Declarations, CoreCompilerRules.Relations, CoreCompilerRules.Constraints, CoreCompilerRules.Requirements, CoreCompilerRules.Resources };

    public static CoreCompilation Compile(CompilerInputManifest input, CompilerArtifactIdentity expectedCompiler,
        IEnumerable<CompilerMaterializedSnapshot?>? snapshots = null, IEnumerable<UnitDefinition?>? units = null,
        IEnumerable<CalendarSnapshot?>? calendars = null, CancellationToken cancellationToken = default, PlanDeclarationReadCache? declarationCache = null)
    {
        var local = CompileSingle(input, expectedCompiler, snapshots, units, calendars, cancellationToken, declarationCache);
        return CompilerDependencyAnalysis.Attach(local, cancellationToken, declarationCache);
    }

    /// <summary>Explicit pre-run root predicate recording. The existing Compile path and host/cache protocol stay unchanged.</summary>
    public static CoreCompilation CompileWithPredicateCapture(CompilerInputManifest input, CompilerArtifactIdentity expectedCompiler,
        CompilerPredicateCapturePolicy capturePolicy, IEnumerable<CompilerMaterializedSnapshot?>? snapshots = null,
        IEnumerable<UnitDefinition?>? units = null, IEnumerable<CalendarSnapshot?>? calendars = null,
        CancellationToken cancellationToken = default, PlanDeclarationReadCache? declarationCache = null)
    {
        ArgumentNullException.ThrowIfNull(capturePolicy);
        var collector = new CompilerPredicateCaptureCollector(capturePolicy);
        var local = CompileSingle(input, expectedCompiler, snapshots, units, calendars, cancellationToken, declarationCache, predicateCapture: collector);
        var completed = CompilerDependencyAnalysis.Attach(local, cancellationToken, declarationCache);
        var record = collector.Complete(completed, cancellationToken);
        return completed.WithPredicatePremises(record);
    }

    /// <summary>Predeclare actual initial-root operator observation; existing Compile and host protocols stay unchanged.</summary>
    public static RecordedPredicateCompilation CompileWithPredicateObservation(CompilerInputManifest input, CompilerArtifactIdentity expectedCompiler,
        CompilerPredicateObservationPolicy observationPolicy, IEnumerable<CompilerMaterializedSnapshot?>? snapshots = null,
        IEnumerable<UnitDefinition?>? units = null, IEnumerable<CalendarSnapshot?>? calendars = null,
        CancellationToken cancellationToken = default, PlanDeclarationReadCache? declarationCache = null)
    {
        ArgumentNullException.ThrowIfNull(observationPolicy);
        var collector = new CompilerPredicateObservationCollector(observationPolicy);
        var local = CompileSingle(input, expectedCompiler, snapshots, units, calendars, cancellationToken, declarationCache, predicateObservation: collector);
        var completed = CompilerDependencyAnalysis.Attach(local, cancellationToken, declarationCache);
        return collector.Complete(completed, cancellationToken);
    }

    /// <summary>Predeclare actual initial-root pass boundaries and existing original JSON; no FullDump or later state reconstruction.</summary>
    public static RecordedPassCompilation CompileWithPassObservation(CompilerInputManifest input, CompilerArtifactIdentity expectedCompiler,
        CompilerPassObservationPolicy policy, IEnumerable<CompilerMaterializedSnapshot?>? snapshots = null, IEnumerable<UnitDefinition?>? units = null,
        IEnumerable<CalendarSnapshot?>? calendars = null, CancellationToken cancellationToken = default, PlanDeclarationReadCache? declarationCache = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var collector = new CompilerPassObservationCollector(policy);
        var local = CompileSingle(input, expectedCompiler, snapshots, units, calendars, cancellationToken, declarationCache, passObservation: collector);
        var completed = CompilerDependencyAnalysis.Attach(local, cancellationToken, declarationCache);
        return collector.Complete(completed);
    }

    /// <summary>Explicit actual regional observation; existing root-only observation and host contracts remain unchanged.</summary>
    public static RecordedRegionalPredicateCompilation CompileWithRegionalPredicateObservation(CompilerInputManifest input,
        CompilerArtifactIdentity expectedCompiler, CompilerRegionalPredicateObservationPolicy policy,
        IEnumerable<CompilerMaterializedSnapshot?>? snapshots = null, IEnumerable<UnitDefinition?>? units = null,
        IEnumerable<CalendarSnapshot?>? calendars = null, CancellationToken cancellationToken = default,
        PlanDeclarationReadCache? declarationCache = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var collector = new CompilerRegionalPredicateObservationCollector(policy);
        var local = CompileSingle(input, expectedCompiler, snapshots, units, calendars, cancellationToken, declarationCache);
        var completed = CompilerDependencyAnalysis.Attach(local, cancellationToken, declarationCache, collector);
        return collector.Complete(completed, cancellationToken);
    }

    internal static CoreCompilation CompileRegion(PartialSemanticModel model, CompilerSemanticValidation validation,
        IReadOnlySet<(SourceRevisionContext Context, EntityId Entity)> attachments, int remainingOperations, CancellationToken cancellationToken,
        CompilerPredicateObservationCollector? predicateObservation = null) =>
        CompileSingle(model.Input, model.Input.Compiler, null, null, null, cancellationToken, null, model, remainingOperations, validation, attachments, predicateObservation: predicateObservation);

    private static CoreCompilation CompileSingle(CompilerInputManifest input, CompilerArtifactIdentity expectedCompiler,
        IEnumerable<CompilerMaterializedSnapshot?>? snapshots, IEnumerable<UnitDefinition?>? units, IEnumerable<CalendarSnapshot?>? calendars,
        CancellationToken cancellationToken, PlanDeclarationReadCache? declarationCache, PartialSemanticModel? boundRegion = null, int? operationBudget = null, CompilerSemanticValidation? preparedValidation = null,
        IReadOnlySet<(SourceRevisionContext Context, EntityId Entity)>? constraintAttachments = null, CompilerPredicateCaptureCollector? predicateCapture = null, CompilerPredicateObservationCollector? predicateObservation = null, CompilerPassObservationCollector? passObservation = null)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(expectedCompiler);
        if (!input.Compiler.Data.Equals(expectedCompiler.Data)) throw new UnsupportedSourceException("Compiler identity differs from the host's fixed engine binding.");
        foreach (var id in new[] { CoreCompilerRules.Schema, CoreCompilerRules.Declarations, CoreCompilerRules.Relations })
            if (!input.Rules.Any(rule => rule.RuleId == id && rule.Version == 1 && rule.Required))
                throw new UnsupportedSourceException("The required structural rule baseline is unavailable.");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            passObservation?.Enter("coglatas.parse-normalize", "Only the actual Source bytes passed to the decoder are referenced.", new CompilerPassObservationFrame("source", ProjectSource.Format, input.Source.Data));
            var reparsed = ProjectSourceCodec.Decode(input.Source.Data.ToCanonicalBytes());
            if (reparsed.Source is null || !reparsed.Source.Digest.Equals(input.Source.Digest))
                throw new UnsupportedSourceException("Source exceeds the supported canonical AST profile.");
            passObservation?.Returned("coglatas.parse-normalize", "The actual decoded Source returned.", new CompilerPassObservationFrame("decoded-source", ProjectSource.Format, reparsed.Source.Data));
            passObservation?.Enter("coglatas.binding", "External materializations are not pre-enumerated by capture.", new CompilerPassObservationFrame("input", CompilerInputManifest.Format, input.Data));
            var model = boundRegion ?? ProjectSemanticBinder.Bind(input, snapshots, cancellationToken, declarationCache);
            passObservation?.Returned("coglatas.binding", "The original binding model has no published canonical body at this boundary.");
            passObservation?.Enter("coglatas.semantic-validation-ir", "Actual binding model and lazy unit/calendar supplies are not serialized here.");
            var initialRemaining = operationBudget ?? input.ExecutionProfile.MaximumOperations - model.LogicalOperationsConsumed;
            var validation = preparedValidation ?? (boundRegion is null ? CompilerSemanticValidation.Validate(model, units, calendars, cancellationToken)
                : CompilerSemanticValidation.ValidateRegion(model, units, calendars, cancellationToken, initialRemaining));
            passObservation?.Returned("coglatas.semantic-validation-ir", validation.Ir is null ? "No original IR was produced." : "Only existing original canonical IR outputs are referenced.",
                validation.Ir is { } observedIr ? [new CompilerPassObservationFrame("semantic-model", "coglatas.semantic-model/1", observedIr.SemanticModel), new CompilerPassObservationFrame("ir", "coglatas.project-ir/1", observedIr.Data), new CompilerPassObservationFrame("source-map", "coglatas.semantic-source-map/1", observedIr.SourceMap)] : []);
            passObservation?.Enter("coglatas.analysis", "Actual binding/validation objects supply this root analysis; no canonical input projection is invented.");
            var root = ProjectLocation.Create(input.Source.Documents[0].DocumentId, input.Source.Context);
            var diagnostics = validation.Findings.Select(item => item.Diagnostic).ToList();
            var ids = diagnostics.Select(item => item.DiagnosticId.Value).ToHashSet();
            var ruleFacts = input.Rules.ToDictionary(rule => rule.RuleId, _ => new RuleFacts(), StringComparer.Ordinal);
            foreach (var finding in validation.Findings)
            {
                var facts = ruleFacts[finding.Diagnostic.RuleId];
                if (finding.State == RelationFindingState.Invalid) facts.Failed = true; else facts.Incomplete = true;
                if (finding.Diagnostic.Code.Contains("budget", StringComparison.Ordinal)) facts.Execution = CompilerExecutionState.ResourceExhausted;
            }
            var remaining = initialRemaining - (preparedValidation is null ? validation.LogicalOperationsConsumed - model.LogicalOperationsConsumed : 0);
            var alreadyConsumedSemantics = preparedValidation is null ? 0 : validation.LogicalOperationsConsumed - model.LogicalOperationsConsumed;
            var bindingExhausted = model.Findings.Any(item => item.Diagnostic.Code.Contains("budget", StringComparison.Ordinal));
            var semanticExhausted = validation.Findings.Any(item => item.Diagnostic.StageOrdinal == 2 && item.Diagnostic.Code.Contains("budget", StringComparison.Ordinal));
            var exhausted = validation.Findings.Any(item => item.Diagnostic.Code.Contains("budget", StringComparison.Ordinal));
            void Finding(CompilerRuleContract rule, string code, RelationFindingState state, ProjectLocation location,
                CompilerEvidenceBasis? basis = null, bool affectsRequiredTruth = true, string discriminator = "", PredicateEvaluation? observedEvaluation = null)
            {
                var facts = ruleFacts[rule.RuleId];
                if (affectsRequiredTruth) { if (state == RelationFindingState.Invalid) facts.Failed = true; else facts.Incomplete = true; }
                basis ??= state == RelationFindingState.Invalid ? Proof() : new(CompilerEvidenceKind.Missing, [], code);
                var diagnostic = new CompilerDiagnostic(input, rule.RuleId, rule.Version, 3, code, location, basis,
                    affectsRequiredTruth ? CompilerDiagnosticSeverity.Error : CompilerDiagnosticSeverity.Warning, discriminator);
                if (ids.Add(diagnostic.DiagnosticId.Value)) diagnostics.Add(diagnostic);
                if (observedEvaluation is not null) predicateObservation?.Link(observedEvaluation, diagnostics.Single(item => item.DiagnosticId == diagnostic.DiagnosticId));
            }
            CompilerEvidenceBasis Proof(IEnumerable<SourceJson>? premises = null)
            {
                var actual = (premises ?? []).Distinct().OrderBy(item => item.CanonicalText, StringComparer.Ordinal).Select(item => item.Value).ToArray();
                if (actual.Length == 0) return new(CompilerEvidenceKind.ModelProof, [input.InputDigest, input.Source.Digest]);
                var body = SourceJson.FromObject(actual); var bytes = body.ToCanonicalBytes();
                var digest = ContentDigest.Compute(ProjectPredicatePremiseRecord.BodyDomain, bytes);
                predicateCapture?.Observe(digest, body, bytes.Length);
                return new(CompilerEvidenceKind.ModelProof, [input.InputDigest, input.Source.Digest, digest]);
            }
            var schemaRule = input.Rules.Single(rule => rule.RuleId == CoreCompilerRules.Schema);
            if (CompilerExecutionPlan.Requested(input) && !CompilerScopedPublication.Requested(input))
                Finding(schemaRule, "coglatas.plan.requires-version-two", RelationFindingState.Unsupported, root);
            var declarationRule = input.Rules.Single(rule => rule.RuleId == CoreCompilerRules.Declarations);
            var capabilities = RelationKindDescriptor.BuiltIns.SelectMany(item => item.RequiredCapabilities).Prepend("coglatas.core.structural").ToHashSet(StringComparer.Ordinal);
            if (SourceFields.Array(input.Settings.Value, "featureSet").EnumerateArray().Any(value => !capabilities.Contains(ValueFields.String(value))) ||
                SourceFields.Array(input.Settings.Value, "targets").EnumerateArray().Any(value => ValueFields.String(value) is not ("coglatas.semantic-model" or "coglatas.project-ir" or CompilerExecutionPlan.Target)) ||
                SourceFields.Array(input.Settings.Value, "analysisScope").EnumerateArray().Any(value => ValueFields.String(value) != "coglatas.source-structure"))
                Finding(schemaRule, "coglatas.analysis.unsupported-target", RelationFindingState.Unsupported, root);
            var declarations = model.Declarations.Select(item => item.Declaration).OfType<PlanDeclaration>().ToArray();
            RequireFamily(CoreCompilerRules.Constraints, declarations.OfType<ConstraintPlanDeclaration>().Any(item => item.IsRequired));
            RequireFamily(CoreCompilerRules.Requirements, declarations.OfType<RequirementPlanDeclaration>().Any());
            RequireFamily(CoreCompilerRules.Resources, declarations.OfType<ResourceBindingDeclaration>().Any());
            void RequireFamily(string id, bool applies)
            {
                if (applies && !input.Rules.Any(rule => rule.RuleId == id && rule.Version == 1 && rule.Required))
                    Finding(declarationRule, "coglatas.analysis.required-family-missing", RelationFindingState.Unsupported, root, discriminator: id);
            }
            foreach (var rule in input.Rules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (--remaining < 0)
                { exhausted = true; ruleFacts[rule.RuleId].Execution = CompilerExecutionState.ResourceExhausted; Finding(rule, "coglatas.analysis.budget-exhausted", RelationFindingState.Unknown, root); continue; }
                if (!SupportedRules.Contains(rule.RuleId) || rule.Version != 1 || rule.Configuration.Value.EnumerateObject().Any())
                { ruleFacts[rule.RuleId].Execution = CompilerExecutionState.NotRun; Finding(rule, "coglatas.analysis.unsupported-rule", RelationFindingState.Unsupported, root); continue; }
                if (rule.RuleId is CoreCompilerRules.Schema or CoreCompilerRules.Declarations or CoreCompilerRules.Relations) continue;
                if (!validation.StructuralClosureValidated)
                { Finding(rule, "coglatas.analysis.semantic-closure-unavailable", RelationFindingState.Unknown, root); continue; }
                if (rule.RuleId == CoreCompilerRules.Constraints)
                {
                    var attached = model.Relations.Select(item => item.Relation).OfType<TypedRelation>().Where(item => item.Kind == RelationKinds.Constrains && item.Source.Reference.Context.Equals(input.Source.Context))
                        .Select(item => item.Source.Reference.EntityId).ToHashSet();
                    foreach (var constraint in declarations.OfType<ConstraintPlanDeclaration>())
                    {
                        var location = ProjectLocation.Create(constraint.DocumentId, input.Source.Context, entityId: constraint.EntityId);
                        if (!(constraintAttachments?.Contains((input.Source.Context, constraint.EntityId)) ?? attached.Contains(constraint.EntityId)))
                        {
                            if (constraint.IsRequired) Finding(rule, "coglatas.analysis.unattached-required-constraint", RelationFindingState.Unknown, location);
                            continue;
                        }
                        if (!SpendPredicate(constraint.Predicate)) { ruleFacts[rule.RuleId].Execution = CompilerExecutionState.ResourceExhausted; Finding(rule, "coglatas.analysis.budget-exhausted", RelationFindingState.Unknown, location); continue; }
                        var evaluation = predicateObservation is null ? constraint.Predicate.Evaluate() : predicateObservation.Evaluate(constraint.Predicate, rule, location, cancellationToken);
                        ruleFacts[rule.RuleId].Premises.AddRange(evaluation.EvaluatedPremises);
                        if (evaluation.Truth == PredicateTruth.False)
                            Finding(rule, "coglatas.analysis.constraint-false", RelationFindingState.Invalid, location, Proof(evaluation.EvaluatedPremises), constraint.IsRequired, observedEvaluation: evaluation);
                        else if (evaluation.Truth == PredicateTruth.Unknown)
                            Finding(rule, "coglatas.analysis.constraint-unknown", RelationFindingState.Unknown, location, affectsRequiredTruth: constraint.IsRequired, observedEvaluation: evaluation);
                    }
                }
                if (rule.RuleId == CoreCompilerRules.Requirements)
                    foreach (var requirement in declarations.OfType<RequirementPlanDeclaration>())
                    {
                        var location = ProjectLocation.Create(requirement.DocumentId, input.Source.Context, entityId: requirement.EntityId);
                        if (SpendPredicate(requirement.Condition))
                        {
                            var evaluation = predicateObservation is null ? requirement.Condition.Evaluate() : predicateObservation.Evaluate(requirement.Condition, rule, location, cancellationToken);
                            ruleFacts[rule.RuleId].Premises.AddRange(evaluation.EvaluatedPremises);
                            if (evaluation.Truth == PredicateTruth.False) Finding(rule, "coglatas.analysis.requirement-condition-false", RelationFindingState.Invalid, location, Proof(evaluation.EvaluatedPremises), observedEvaluation: evaluation);
                        }
                        else { ruleFacts[rule.RuleId].Execution = CompilerExecutionState.ResourceExhausted; Finding(rule, "coglatas.analysis.budget-exhausted", RelationFindingState.Unknown, location); }
                        Finding(rule, "coglatas.analysis.requirement-evidence-unavailable", RelationFindingState.Unknown, location);
                    }
                if (rule.RuleId == CoreCompilerRules.Resources)
                    foreach (var resource in declarations.OfType<ResourceBindingDeclaration>())
                        Finding(rule, "coglatas.analysis.resource-snapshot-unavailable", RelationFindingState.Unknown,
                            ProjectLocation.Create(resource.DocumentId, input.Source.Context, entityId: resource.EntityId));
            }
            bool SpendPredicate(PurePredicate predicate)
            {
                remaining -= predicate.Data.CanonicalText.Length;
                if (remaining >= 0) return true;
                exhausted = true; return false;
            }
            var outcomes = input.Rules.Select(rule =>
            {
                var facts = ruleFacts[rule.RuleId];
                return new CompilerRuleOutcome(rule.RuleId, rule.Version, facts.Execution,
                    facts.Execution == CompilerExecutionState.NotRun ? CompilerCoverage.None : facts.Incomplete ? CompilerCoverage.Partial : CompilerCoverage.Complete,
                    facts.Execution == CompilerExecutionState.NotRun ? CompilerVerdict.NotRun : facts.Failed ? CompilerVerdict.Fail : facts.Incomplete ? CompilerVerdict.Inconclusive : CompilerVerdict.Pass,
                    facts.Incomplete ? new(CompilerEvidenceKind.Missing, [], "coglatas.analysis.incomplete-basis") : Proof(facts.Premises));
            }).ToArray();
            var complete = input.SourceCoverageComplete && outcomes.All(item => item.Coverage == CompilerCoverage.Complete) && !exhausted;
            var outputs = validation.Ir is { } ir ? new[] { ir.SemanticModelDigest, ir.Digest, ir.SourceMapDigest } : [];
            var analysisData = SourceJson.FromObject(outcomes.Select(item => item.Data.Value).ToArray());
            passObservation?.Returned("coglatas.analysis", "Original root rule outcomes only; no regional observation is inferred.", new CompilerPassObservationFrame("outcomes", "coglatas.analysis/1", analysisData));
            var stages = new[]
            {
                new CompilerStageOutcome("coglatas.parse-normalize", 1, CompilerExecutionState.Completed, CompilerCoverage.Complete, input.InputDigest, [input.Source.Digest]),
                new CompilerStageOutcome("coglatas.binding", 1, bindingExhausted ? CompilerExecutionState.ResourceExhausted : CompilerExecutionState.Completed, model.BindingComplete ? CompilerCoverage.Complete : CompilerCoverage.Partial,
                    input.InputDigest, [], model.BindingComplete ? null : "coglatas.binding.partial"),
                new CompilerStageOutcome("coglatas.semantic-validation-ir", 1, semanticExhausted ? CompilerExecutionState.ResourceExhausted : CompilerExecutionState.Completed,
                    validation.Findings.Any(item => item.State != RelationFindingState.Invalid) ? CompilerCoverage.Partial : CompilerCoverage.Complete,
                    input.InputDigest, outputs, validation.Ir is null ? "coglatas.ir.not-produced" : null),
                new CompilerStageOutcome("coglatas.analysis", 1, exhausted ? CompilerExecutionState.ResourceExhausted : CompilerExecutionState.Completed,
                    complete ? CompilerCoverage.Complete : CompilerCoverage.Partial, input.InputDigest,
                    [ContentDigest.Compute("coglatas.analysis/1", analysisData.ToCanonicalBytes())], complete ? null : "coglatas.analysis.partial")
            };
            var artifacts = boundRegion is null && validation.Ir is { } produced ? new[]
            {
                new CompilerArtifactReference("coglatas.semantic-model", "coglatas.semantic-model", 1, input.InputDigest, produced.SemanticModelDigest),
                new CompilerArtifactReference("coglatas.ir", "coglatas.project-ir", 1, input.InputDigest, produced.Digest),
                new CompilerArtifactReference("coglatas.source-map", "coglatas.semantic-source-map", 1, input.InputDigest, produced.SourceMapDigest)
            } : [];
            var coverage = complete && stages.All(stage => stage.Coverage == CompilerCoverage.Complete) ? CompilerCoverage.Complete : CompilerCoverage.Partial;
            passObservation?.Enter("coglatas.publication", "The original result constructor inputs have no canonical pass-input body here.");
            CompilerResult result;
            try
            {
                result = new(input, exhausted ? CompilerExecutionState.ResourceExhausted : CompilerExecutionState.Completed,
                    coverage, outcomes, diagnostics, stages, complete, artifacts);
            }
            catch (FormatException)
            {
                var limited = outcomes.Select(outcome => new CompilerRuleOutcome(outcome.RuleId, outcome.Version, CompilerExecutionState.ResourceExhausted,
                    CompilerCoverage.Partial, outcome.Verdict == CompilerVerdict.Fail ? CompilerVerdict.Fail : CompilerVerdict.Inconclusive,
                    new(CompilerEvidenceKind.Missing, [], "coglatas.analysis.publication-budget"))).ToArray();
                var diagnostic = new CompilerDiagnostic(input, schemaRule.RuleId, schemaRule.Version, 3, "coglatas.analysis.publication-budget", root,
                    new(CompilerEvidenceKind.Missing, [], "coglatas.analysis.publication-budget"));
                result = new(input, CompilerExecutionState.ResourceExhausted, CompilerCoverage.Partial, limited, [diagnostic],
                    [new CompilerStageOutcome("coglatas.publication", 1, CompilerExecutionState.ResourceExhausted, CompilerCoverage.Partial, input.InputDigest, [], "coglatas.analysis.publication-budget")], false);
                passObservation?.Returned("coglatas.publication", "Original publication overflowed; only its actual fallback result body returned.", new CompilerPassObservationFrame("root-result", "coglatas.compiler-result/1", result.Data));
                return new(input, model, null, result, model.LogicalOperationsConsumed + alreadyConsumedSemantics + initialRemaining - Math.Max(0, remaining));
            }
            cancellationToken.ThrowIfCancellationRequested();
            passObservation?.Returned("coglatas.publication", "Original root result before dependency attachment.", new CompilerPassObservationFrame("root-result", "coglatas.compiler-result/1", result.Data));
            return new(input, model, validation, result, model.LogicalOperationsConsumed + alreadyConsumedSemantics + initialRemaining - Math.Max(0, remaining));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { passObservation?.Cancelled(); return new(input, null, null, new(input, CompilerExecutionState.Cancelled, CompilerCoverage.None, [], [], [], false)); }
    }

    private sealed class RuleFacts
    {
        internal bool Failed { get; set; }
        internal bool Incomplete { get; set; }
        internal CompilerExecutionState Execution { get; set; } = CompilerExecutionState.Completed;
        internal List<SourceJson> Premises { get; } = [];
    }
}
