using System.Text.Json.Nodes;
using System.Text.Json;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

/// <summary>Offline original-byte transport. It grants no producer trust or current foreign-read authority.</summary>
public static class SyntheticScopedSourceProjection
{
    public const string Format = "coglatas.local-scoped-source-projection/1";
    public const string FormatTwo = "coglatas.local-scoped-source-projection/2";
    public static SourceJson? Export(CoreCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        if (compilation.Dependencies is not { State: CompilerDependencyAnalysisState.Evaluated, ScopedIr: { } scoped } analysis ||
            compilation.Result.Execution != CompilerExecutionState.Completed) return null;
        object Frame(SourceJson data, ContentDigest digest) => new { text = data.CanonicalText, digest = digest.Data.Value };
        var result = SourceJson.Parse(JsonSerializer.Serialize(new
        {
            format = Format, inputText = compilation.Input.Data.CanonicalText, result = Frame(compilation.Result.Data, compilation.Result.ResultDigest),
            analysis = Frame(analysis.Data, analysis.Digest), binding = Frame(analysis.Binding.Data, analysis.Binding.Digest),
            graph = Frame(analysis.Graph!.Data, analysis.Graph.Digest), ir = Frame(scoped.Data, scoped.Digest),
            sourceMap = Frame(scoped.SourceMap, scoped.SourceMapDigest),
            regions = scoped.Regions.Select(region => new
            {
                context = region.Binding.Source.Context.Data.Value,
                ir = Frame(region.Validation!.Ir!.Data, region.Validation.Ir.Digest),
                sourceMap = Frame(region.Validation.Ir.SourceMap, region.Validation.Ir.SourceMapDigest)
            }).ToArray()
        }, new JsonSerializerOptions { MaxDepth = 256 }));
        if (compilation.Publication is { } publication)
        {
            if (!publication.IsDeclaredBy(compilation.Result)) throw new FormatException("Published artifacts differ from the actual result.");
            var node = JsonNode.Parse(result.CanonicalText)!.AsObject(); node["format"] = FormatTwo;
            node["publication"] = JsonSerializer.SerializeToNode(new
            {
                semanticModel = Frame(publication.SemanticModel, publication.SemanticModelDigest),
                ir = Frame(publication.Ir, publication.IrDigest), sourceMap = Frame(publication.SourceMap, publication.SourceMapDigest)
            }, new JsonSerializerOptions { MaxDepth = 256 });
            if (publication.ExecutionPlan is { } plan) node["publication"]!["plan"] = JsonSerializer.SerializeToNode(Frame(plan.Data, plan.Digest));
            for (var index = 0; index < scoped.Regions.Count; index++)
            {
                var ir = scoped.Regions[index].Validation!.Ir!;
                node["regions"]![index]!["semanticModel"] = JsonSerializer.SerializeToNode(Frame(ir.SemanticModel, ir.SemanticModelDigest));
            }
            result = SourceJson.Parse(node.ToJsonString(new JsonSerializerOptions { MaxDepth = 256 }));
        }
        if (result.ToCanonicalBytes().Length > 4_194_304) throw new FormatException("Synthetic scoped projection exceeds its byte bound.");
        return result;
    }
    public static CompilerScopedIr Read(CoreCompilation compilation, SourceJson data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var actual = Export(compilation);
        if (actual is null || !actual.Equals(data)) throw new FormatException("Scoped projection differs from the accepted actual Core compilation.");
        return compilation.Dependencies!.ScopedIr!;
    }
}
