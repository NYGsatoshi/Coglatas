using System.Text.Json;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

/// <summary>Offline original Core index/query projection. It is not a remote reader, authorization or Registry contract.</summary>
public static class SyntheticSemanticIndexProjection
{
    public const string Format = "coglatas.local-semantic-index-projection/1";
    public const int MaximumProjectionBytes = 4_194_304;
    /// <summary>Absent when the prepared projection exceeds its local export bound; original Compiler facts remain available.</summary>
    public static SourceJson? Export(CoreCompilation compilation, int maximumProjectionBytes = MaximumProjectionBytes)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        if (maximumProjectionBytes is < 1 or > MaximumProjectionBytes) throw new ArgumentOutOfRangeException(nameof(maximumProjectionBytes));
        if (compilation.Model is not { } model) return null;
        var input = compilation.Input;
        var binding = compilation.Dependencies?.Binding ?? CompilerBindingClosure.Build(model, input.InputDigest);
        var index = CompilerSemanticIndex.Build(binding, input.InputDigest);
        var references = new Dictionary<CompilerBoundReference, int>(ReferenceEqualityComparer.Instance);
        for (var ordinal = 0; ordinal < index.References.Count; ordinal++) references.Add(index.References[ordinal], ordinal);
        var definitions = new List<object>(); var completions = new List<object>(); var searches = new List<object>();
        if (index.State == CompilerSemanticIndexState.Complete && index.IndexCoverageComplete)
        {
            for (var ordinal = 0; ordinal < index.References.Count; ordinal++)
            {
                var definition = index.FindDefinition(index.References[ordinal].Origin, input.InputDigest);
                definitions.Add(new { referenceIndex = ordinal, state = definition.State.ToString(), targetKey = definition.Target?.Symbol.Key,
                    resolution = definition.Resolution?.ToString() });
            }
            foreach (var symbol in index.Symbols)
            {
                var pages = new List<object>(); CompilerIndexCursor? cursor = null;
                do
                {
                    var page = index.FindReferences(symbol.Symbol.Location, input.InputDigest, cursor: cursor);
                    if (page.State != CompilerIndexQueryState.Ready || pages.Count > index.References.Count)
                        throw new InvalidOperationException("Full-index reference continuation is not bounded to its original input.");
                    pages.Add(new { state = page.State.ToString(), indexCoverageComplete = page.IndexCoverageComplete, scanComplete = page.ScanComplete,
                        scannedReferences = page.ScannedReferences, referenceIndices = page.References.Select(item => references[item]).ToArray() });
                    cursor = page.NextCursor;
                } while (cursor is not null);
                searches.Add(new { targetKey = symbol.Symbol.Key, pages });
                if (symbol.Symbol.PortId is null) continue;
                pages = []; cursor = null;
                do
                {
                    var page = index.CompleteEntityReference(symbol.Symbol.Location, input.InputDigest, CompilerSemanticIndex.MaximumPageSize, cursor);
                    if (pages.Count > index.Symbols.Count) throw new InvalidOperationException("Full-index completion continuation is not bounded.");
                    pages.Add(new { state = page.State.ToString(), expectedKind = page.ExpectedKind, indexCoverageComplete = page.IndexCoverageComplete,
                        scanComplete = page.ScanComplete, scannedSymbols = page.ScannedSymbols, symbolKeys = page.Candidates.Select(item => item.Symbol.Key).ToArray() });
                    cursor = page.NextCursor;
                } while (cursor is not null);
                completions.Add(new { originKey = symbol.Symbol.Key, pages });
            }
        }
        var json = JsonSerializer.Serialize(new { format = Format, resultDigest = compilation.Result.ResultDigest.Data.Value,
            index = new { text = index.Data.CanonicalText, digest = index.Digest.Data.Value }, definitions, completions, referenceSearches = searches }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        try { return SourceJson.Parse(json, new SourceCodecLimits(MaximumBytes: maximumProjectionBytes)); }
        catch (FormatException) { return null; }
    }
}
