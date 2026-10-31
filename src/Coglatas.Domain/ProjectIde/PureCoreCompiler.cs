namespace Coglatas.Domain.ProjectIde;

public sealed class CoreCompilation
{
    public CompilerInputManifest Input { get; }
    public PartialSemanticModel? Model { get; }
    public CompilerSemanticValidation? Validation { get; }
    public CompilerResult Result { get; }
    internal CoreCompilation(CompilerInputManifest input, PartialSemanticModel? model, CompilerSemanticValidation? validation, CompilerResult result)
    { Input = input; Model = model; Validation = validation; Result = result; }
}

/// <summary>Full-reference pure compilation for the declared supported scope. It starts no run and grants no merge authority.</summary>
public static class PureCoreCompiler
{
    private static readonly HashSet<string> SupportedRules = new(StringComparer.Ordinal)
    { CoreCompilerRules.Schema, CoreCompilerRules.Declarations, CoreCompilerRules.Relations, CoreCompilerRules.Constraints, CoreCompilerRules.Requirements, CoreCompilerRules.Resources };

    public static CoreCompilation Compile(CompilerInputManifest input, CompilerArtifactIdentity expectedCompiler,
        IEnumerable<CompilerMaterializedSnapshot?>? snapshots = null, IEnumerable<UnitDefinition?>? units = null,
        IEnumerable<CalendarSnapshot?>? calendars = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(expectedCompiler);
        if (!input.Compiler.Data.Equals(expectedCompiler.Data)) throw new UnsupportedSourceException("Compiler identity differs from the host's fixed engine binding.");
        foreach (var id in new[] { CoreCompilerRules.Schema, CoreCompilerRules.Declarations, CoreCompilerRules.Relations })
            if (!input.Rules.Any(rule => rule.RuleId == id && rule.Version == 1 && rule.Required))
                throw new UnsupportedSourceException("The required structural rule baseline is unavailable.");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reparsed = ProjectSourceCodec.Decode(input.Source.Data.ToCanonicalBytes());
            if (reparsed.Source is null || !reparsed.Source.Digest.Equals(input.Source.Digest))
                throw new UnsupportedSourceException("Source exceeds the supported canonical AST profile.");
            var model = ProjectSemanticBinder.Bind(input, snapshots, cancellationToken);
            var validation = CompilerSemanticValidation.Validate(model, units, calendars, cancellationToken);
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
            var remaining = input.ExecutionProfile.MaximumOperations - validation.LogicalOperationsConsumed;
            var exhausted = validation.Findings.Any(item => item.Diagnostic.Code.Contains("budget", StringComparison.Ordinal));
            void Finding(CompilerRuleContract rule, string code, RelationFindingState state, ProjectLocation location,
                CompilerEvidenceBasis? basis = null, bool affectsRequiredTruth = true, string discriminator = "")
            {
                var facts = ruleFacts[rule.RuleId];
                if (affectsRequiredTruth) { if (state == RelationFindingState.Invalid) facts.Failed = true; else facts.Incomplete = true; }
                basis ??= state == RelationFindingState.Invalid ? Proof() : new(CompilerEvidenceKind.Missing, [], code);
                var diagnostic = new CompilerDiagnostic(input, rule.RuleId, rule.Version, 3, code, location, basis,
                    affectsRequiredTruth ? CompilerDiagnosticSeverity.Error : CompilerDiagnosticSeverity.Warning, discriminator);
                if (ids.Add(diagnostic.DiagnosticId.Value)) diagnostics.Add(diagnostic);
            }
            CompilerEvidenceBasis Proof(IEnumerable<SourceJson>? premises = null) => new(CompilerEvidenceKind.ModelProof,
                new[] { input.InputDigest, input.Source.Digest }.Concat((premises ?? []).Select(premise => ContentDigest.Compute("coglatas.predicate-premise/1", premise.ToCanonicalBytes()))));
            var schemaRule = input.Rules.Single(rule => rule.RuleId == CoreCompilerRules.Schema);
            var declarationRule = input.Rules.Single(rule => rule.RuleId == CoreCompilerRules.Declarations);
            var capabilities = RelationKindDescriptor.BuiltIns.SelectMany(item => item.RequiredCapabilities).Prepend("coglatas.core.structural").ToHashSet(StringComparer.Ordinal);
            if (SourceFields.Array(input.Settings.Value, "featureSet").EnumerateArray().Any(value => !capabilities.Contains(ValueFields.String(value))) ||
                SourceFields.Array(input.Settings.Value, "targets").EnumerateArray().Any(value => ValueFields.String(value) is not ("coglatas.semantic-model" or "coglatas.project-ir")) ||
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
                    var attached = model.Relations.Select(item => item.Relation).OfType<TypedRelation>().Where(item => item.Kind == RelationKinds.Constrains)
                        .Select(item => item.Source.Reference.EntityId).ToHashSet();
                    foreach (var constraint in declarations.OfType<ConstraintPlanDeclaration>())
                    {
                        var location = ProjectLocation.Create(constraint.DocumentId, input.Source.Context, entityId: constraint.EntityId);
                        if (!attached.Contains(constraint.EntityId))
                        {
                            if (constraint.IsRequired) Finding(rule, "coglatas.analysis.unattached-required-constraint", RelationFindingState.Unknown, location);
                            continue;
                        }
                        if (!SpendPredicate(constraint.Predicate)) { ruleFacts[rule.RuleId].Execution = CompilerExecutionState.ResourceExhausted; Finding(rule, "coglatas.analysis.budget-exhausted", RelationFindingState.Unknown, location); continue; }
                        var evaluation = constraint.Predicate.Evaluate();
                        ruleFacts[rule.RuleId].Premises.AddRange(evaluation.EvaluatedPremises);
                        if (evaluation.Truth == PredicateTruth.False)
                            Finding(rule, "coglatas.analysis.constraint-false", RelationFindingState.Invalid, location, Proof(evaluation.EvaluatedPremises), constraint.IsRequired);
                        else if (evaluation.Truth == PredicateTruth.Unknown)
                            Finding(rule, "coglatas.analysis.constraint-unknown", RelationFindingState.Unknown, location, affectsRequiredTruth: constraint.IsRequired);
                    }
                }
                if (rule.RuleId == CoreCompilerRules.Requirements)
                    foreach (var requirement in declarations.OfType<RequirementPlanDeclaration>())
                    {
                        var location = ProjectLocation.Create(requirement.DocumentId, input.Source.Context, entityId: requirement.EntityId);
                        if (SpendPredicate(requirement.Condition))
                        {
                            var evaluation = requirement.Condition.Evaluate();
                            ruleFacts[rule.RuleId].Premises.AddRange(evaluation.EvaluatedPremises);
                            if (evaluation.Truth == PredicateTruth.False) Finding(rule, "coglatas.analysis.requirement-condition-false", RelationFindingState.Invalid, location, Proof(evaluation.EvaluatedPremises));
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
            var stages = new[]
            {
                new CompilerStageOutcome("coglatas.parse-normalize", 1, CompilerExecutionState.Completed, CompilerCoverage.Complete, input.InputDigest, [input.Source.Digest]),
                new CompilerStageOutcome("coglatas.binding", 1, CompilerExecutionState.Completed, model.BindingComplete ? CompilerCoverage.Complete : CompilerCoverage.Partial,
                    input.InputDigest, [], model.BindingComplete ? null : "coglatas.binding.partial"),
                new CompilerStageOutcome("coglatas.semantic-validation-ir", 1, CompilerExecutionState.Completed,
                    validation.Findings.Any(item => item.State != RelationFindingState.Invalid) ? CompilerCoverage.Partial : CompilerCoverage.Complete,
                    input.InputDigest, outputs, validation.Ir is null ? "coglatas.ir.not-produced" : null),
                new CompilerStageOutcome("coglatas.analysis", 1, exhausted ? CompilerExecutionState.ResourceExhausted : CompilerExecutionState.Completed,
                    complete ? CompilerCoverage.Complete : CompilerCoverage.Partial, input.InputDigest,
                    [ContentDigest.Compute("coglatas.analysis/1", SourceJson.FromObject(outcomes.Select(item => item.Data.Value).ToArray()).ToCanonicalBytes())], complete ? null : "coglatas.analysis.partial")
            };
            var artifacts = validation.Ir is { } produced ? new[]
            {
                new CompilerArtifactReference("coglatas.semantic-model", "coglatas.semantic-model", 1, input.InputDigest, produced.SemanticModelDigest),
                new CompilerArtifactReference("coglatas.ir", "coglatas.project-ir", 1, input.InputDigest, produced.Digest),
                new CompilerArtifactReference("coglatas.source-map", "coglatas.semantic-source-map", 1, input.InputDigest, produced.SourceMapDigest)
            } : [];
            var coverage = complete && stages.All(stage => stage.Coverage == CompilerCoverage.Complete) ? CompilerCoverage.Complete : CompilerCoverage.Partial;
            var result = new CompilerResult(input, exhausted ? CompilerExecutionState.ResourceExhausted : CompilerExecutionState.Completed,
                coverage, outcomes, diagnostics, stages, complete, artifacts);
            cancellationToken.ThrowIfCancellationRequested();
            return new(input, model, validation, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { return new(input, null, null, new(input, CompilerExecutionState.Cancelled, CompilerCoverage.None, [], [], [], false)); }
    }

    private sealed class RuleFacts
    {
        internal bool Failed { get; set; }
        internal bool Incomplete { get; set; }
        internal CompilerExecutionState Execution { get; set; } = CompilerExecutionState.Completed;
        internal List<SourceJson> Premises { get; } = [];
    }
}
