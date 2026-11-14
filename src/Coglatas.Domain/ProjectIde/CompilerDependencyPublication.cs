namespace Coglatas.Domain.ProjectIde;

public sealed partial class CompilerDependencyAnalysis
{
    private static CoreCompilation PublishScope(CoreCompilation local, CompilerDependencyAnalysis analysis,
        CompilerScopedPublication publication, CancellationToken token)
    {
        var input = local.Input; var root = ProjectLocation.Create(input.Source.Documents[0].DocumentId, input.Source.Context);
        var checkedRoot = analysis.Regions.Single(region => region.Binding.IsRoot);
        var diagnostics = new List<CompilerDiagnostic>();
        foreach (var region in analysis.Regions)
            foreach (var diagnostic in region.Diagnostics)
            {
                token.ThrowIfCancellationRequested();
                if (region.Binding.IsRoot) diagnostics.Add(diagnostic);
                else diagnostics.Add(new(input, diagnostic.RuleId, diagnostic.RuleVersion, 3, "coglatas.analysis.dependency-finding", root,
                    diagnostic.Evidence.Kind == CompilerEvidenceKind.ModelProof
                        ? new(CompilerEvidenceKind.ModelProof, [input.InputDigest, region.Binding.Digest, region.Digest])
                        : new(CompilerEvidenceKind.Missing, [], "coglatas.analysis.dependency-basis-incomplete"),
                    diagnostic.Severity, region.Digest.Value + ":" + diagnostic.DiagnosticId));
            }
        var rules = input.Rules.Select(contract =>
        {
            var actual = analysis.Regions.SelectMany(region => region.Rules.Where(rule => rule.RuleId == contract.RuleId && rule.Version == contract.Version)).ToArray();
            var failed = actual.Any(rule => rule.Verdict == CompilerVerdict.Fail) || analysis.KnownFailures.Any(failure => failure.RuleId == contract.RuleId);
            var partial = actual.Length != analysis.Regions.Count || actual.Any(rule => rule.Execution != CompilerExecutionState.Completed || rule.Coverage != CompilerCoverage.Complete);
            var exhausted = actual.Any(rule => rule.Execution == CompilerExecutionState.ResourceExhausted);
            var execution = exhausted ? CompilerExecutionState.ResourceExhausted : failed ? CompilerExecutionState.Completed
                : actual.Any(rule => rule.Execution == CompilerExecutionState.NotRun) ? CompilerExecutionState.NotRun : CompilerExecutionState.Completed;
            var verdict = failed ? CompilerVerdict.Fail : execution == CompilerExecutionState.NotRun ? CompilerVerdict.NotRun
                : partial ? CompilerVerdict.Inconclusive : CompilerVerdict.Pass;
            return new CompilerRuleOutcome(contract.RuleId, contract.Version, execution, partial ? CompilerCoverage.Partial : CompilerCoverage.Complete, verdict,
                partial ? new(CompilerEvidenceKind.Missing, [], "coglatas.analysis.dependency-incomplete")
                    : new(CompilerEvidenceKind.ModelProof, [input.InputDigest, analysis.Digest, publication.SemanticModelDigest]));
        }).ToArray();
        var complete = rules.All(rule => rule.Execution == CompilerExecutionState.Completed && rule.Coverage == CompilerCoverage.Complete);
        var stages = new[]
        {
            new CompilerStageOutcome("coglatas.parse-normalize", 1, CompilerExecutionState.Completed, CompilerCoverage.Complete, input.InputDigest, [input.Source.Digest]),
            new CompilerStageOutcome("coglatas.binding", 1, CompilerExecutionState.Completed, CompilerCoverage.Complete, input.InputDigest, [analysis.Binding.Digest]),
            new CompilerStageOutcome("coglatas.semantic-validation-ir", 2, CompilerExecutionState.Completed, CompilerCoverage.Complete, input.InputDigest,
                [publication.SemanticModelDigest, publication.IrDigest, publication.SourceMapDigest]),
            new CompilerStageOutcome("coglatas.analysis", 1, CompilerExecutionState.Completed, complete ? CompilerCoverage.Complete : CompilerCoverage.Partial,
                input.InputDigest, [analysis.Digest], complete ? null : "coglatas.analysis.partial"),
            new CompilerStageOutcome("coglatas.dependency-analysis", 1, CompilerExecutionState.Completed, CompilerCoverage.Complete, input.InputDigest, [analysis.Digest]),
            new CompilerStageOutcome("coglatas.scoped-publication", 2, CompilerExecutionState.Completed, CompilerCoverage.Complete, input.InputDigest,
                [publication.SemanticModelDigest, publication.IrDigest, publication.SourceMapDigest])
        };
        CompilerResult result;
        try
        {
            result = new(input, CompilerExecutionState.Completed, complete ? CompilerCoverage.Complete : CompilerCoverage.Partial,
                rules, diagnostics.DistinctBy(diagnostic => diagnostic.DiagnosticId.Value), stages, complete, publication.Artifacts);
            _ = SourceJson.FromElement(result.Data.Value, input.Source.Data.Limits);
        }
        catch (FormatException)
        {
            // A bounded protocol failure envelope retains evaluated FAIL, never declares artifacts produced.
            result = new(input, CompilerExecutionState.ResourceExhausted, CompilerCoverage.Partial,
                rules.Select(rule => new CompilerRuleOutcome(rule.RuleId, rule.Version, CompilerExecutionState.ResourceExhausted,
                    CompilerCoverage.Partial, rule.Verdict == CompilerVerdict.Fail ? CompilerVerdict.Fail : CompilerVerdict.Inconclusive,
                    new(CompilerEvidenceKind.Missing, [], "coglatas.analysis.publication-budget"))),
                [new CompilerDiagnostic(input, CoreCompilerRules.Schema, 1, 3, "coglatas.analysis.publication-budget", root,
                    new(CompilerEvidenceKind.Missing, [], "coglatas.analysis.publication-budget"))],
                [new CompilerStageOutcome("coglatas.publication", 1, CompilerExecutionState.ResourceExhausted, CompilerCoverage.Partial, input.InputDigest,
                    [], "coglatas.analysis.publication-budget")], false);
            token.ThrowIfCancellationRequested();
            return new(input, local.Model, local.Validation, result, analysis.LogicalOperationsConsumed, analysis);
        }
        token.ThrowIfCancellationRequested();
        return new(input, local.Model, checkedRoot.Validation!.WithPublication(publication), result, analysis.LogicalOperationsConsumed, analysis, publication);
    }
}
