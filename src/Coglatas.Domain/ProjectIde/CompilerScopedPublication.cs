using System.Text.Json;

namespace Coglatas.Domain.ProjectIde;

/// <summary>Complete structural artifacts over the exact declared scope. Rule/plan/current authority are separate.</summary>
public sealed class CompilerScopedPublication
{
    public const string ModelFormat = "coglatas.semantic-model/2";
    public const string IrFormat = "coglatas.project-ir/2";
    public const string MapFormat = "coglatas.semantic-source-map/2";
    public CompilerInputManifest Input => Scope.Input;
    public CompilerScopedIr Scope { get; }
    public SourceJson SemanticModel { get; }
    public ContentDigest SemanticModelDigest { get; }
    public SourceJson Ir { get; }
    public ContentDigest IrDigest { get; }
    public SourceJson SourceMap { get; }
    public ContentDigest SourceMapDigest { get; }
    public IReadOnlyList<CompilerArtifactReference> Artifacts { get; }
    public int LogicalOperationsConsumed { get; }
    internal static bool Requested(CompilerInputManifest input) =>
        input.Settings.Value.GetProperty("semantics").GetProperty("artifactId").GetString() == "coglatas.semantic-model" &&
        input.Settings.Value.GetProperty("semantics").GetProperty("version").GetString() == "2" &&
        input.Settings.Value.GetProperty("ir").GetProperty("artifactId").GetString() == "coglatas.project-ir" &&
        input.Settings.Value.GetProperty("ir").GetProperty("version").GetString() == "2";

    private CompilerScopedPublication(CompilerScopedIr scope, ref int remaining, CancellationToken token)
    {
        Scope = scope; var initial = remaining; token.ThrowIfCancellationRequested();
        SemanticModel = SourceJson.FromObject(new
        {
            format = ModelFormat, compilerInputId = Input.InputId.ToString(), inputDigest = Input.InputDigest.Data.Value,
            sourceDigest = Input.Source.Digest.Data.Value, closureDigest = scope.Digest.Data.Value,
            regions = scope.Regions.Select(region => new
            {
                context = region.Binding.Source.Context.Data.Value, sourceDigest = region.Binding.Source.Digest.Data.Value,
                semanticModelDigest = region.Validation!.Ir!.SemanticModelDigest.Data.Value, semanticModel = region.Validation.Ir.SemanticModel.Value
            }).ToArray()
        }, Input.Source.Data.Limits);
        if (!ProjectSemanticBinder.WithinBudget(SemanticModel.Value, ref remaining, token)) throw new FormatException("Scoped model publication exhausted the root budget.");
        SemanticModelDigest = ContentDigest.Compute(ModelFormat, SemanticModel.ToCanonicalBytes());
        SourceMap = SourceJson.FromObject(new
        {
            format = MapFormat, compilerInputId = Input.InputId.ToString(), inputDigest = Input.InputDigest.Data.Value,
            sourceDigest = Input.Source.Digest.Data.Value, closureDigest = scope.Digest.Data.Value, semanticModelDigest = SemanticModelDigest.Data.Value,
            regions = scope.SourceMap.Value.GetProperty("regions")
        }, Input.Source.Data.Limits);
        if (!ProjectSemanticBinder.WithinBudget(SourceMap.Value, ref remaining, token)) throw new FormatException("Scoped map publication exhausted the root budget.");
        SourceMapDigest = ContentDigest.Compute(MapFormat, SourceMap.ToCanonicalBytes());
        Ir = SourceJson.FromObject(new
        {
            format = IrFormat, compilerInputId = Input.InputId.ToString(), inputDigest = Input.InputDigest.Data.Value,
            sourceDigest = Input.Source.Digest.Data.Value, closureDigest = scope.Digest.Data.Value,
            semanticModelDigest = SemanticModelDigest.Data.Value, sourceMapDigest = SourceMapDigest.Data.Value,
            regions = scope.Data.Value.GetProperty("regions"),
            executionPlan = new { kind = "coglatas.not-produced", reason = "coglatas.analysis-and-plan-lowering-required" }
        }, Input.Source.Data.Limits);
        if (!ProjectSemanticBinder.WithinBudget(Ir.Value, ref remaining, token)) throw new FormatException("Scoped IR publication exhausted the root budget.");
        IrDigest = ContentDigest.Compute(IrFormat, Ir.ToCanonicalBytes());
        Artifacts = new ImmutableListView<CompilerArtifactReference>(new[]
        {
            new CompilerArtifactReference("coglatas.semantic-model", "coglatas.semantic-model", 2, Input.InputDigest, SemanticModelDigest),
            new CompilerArtifactReference("coglatas.ir", "coglatas.project-ir", 2, Input.InputDigest, IrDigest),
            new CompilerArtifactReference("coglatas.source-map", "coglatas.semantic-source-map", 2, Input.InputDigest, SourceMapDigest)
        });
        token.ThrowIfCancellationRequested(); LogicalOperationsConsumed = initial - Math.Max(0, remaining);
    }
    internal static CompilerScopedPublication Build(CompilerScopedIr scope, ref int remaining, CancellationToken token)
    {
        if (!Requested(scope.Input)) throw new ArgumentException("Scoped publication requires explicit model/IR target version two.");
        return new(scope, ref remaining, token);
    }
    /// <summary>Checks the exact actual published artifact set and producing stage; not producer trust or current authority.</summary>
    public bool IsDeclaredBy(CompilerResult result) => result.Input.Data.Equals(Input.Data) && result.Artifacts.Count == Artifacts.Count &&
        Artifacts.All(expected => result.Artifacts.Any(actual => actual.Data.Equals(expected.Data))) &&
        result.Stages.Count(stage => stage.StageId == "coglatas.scoped-publication") == 1 &&
        result.Stages.Any(stage => stage.StageId == "coglatas.scoped-publication" && stage.Version == 2 && stage.Execution == CompilerExecutionState.Completed &&
            stage.Coverage == CompilerCoverage.Complete && stage.InputDigest.Equals(Input.InputDigest) &&
            stage.Data.Value.GetProperty("outputs").GetArrayLength() == Artifacts.Count &&
            Artifacts.All(artifact => stage.Data.Value.GetProperty("outputs").EnumerateArray().Any(output => SourceJson.FromElement(output).Equals(artifact.Digest.Data))));
}
