using System.Collections.ObjectModel;

namespace Coglatas.Domain.ProjectIde;

public sealed record CompilerInspectedSymbol(CompilerSymbol Symbol, SourceJson? DeclaredType);

/// <summary>Input-bound local declaration/reference projection for inspection, including partial models. No execution or authority.</summary>
public sealed class CompilerSemanticInspection
{
    public CompilerInputManifest Input { get; }
    public bool BindingComplete { get; }
    public IReadOnlyList<CompilerInspectedSymbol> Symbols { get; }
    public IReadOnlyList<CompilerBoundReference> References { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }

    public CompilerSemanticInspection(PartialSemanticModel model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model); cancellationToken.ThrowIfCancellationRequested();
        Input = model.Input; BindingComplete = model.BindingComplete;
        var symbols = new List<CompilerInspectedSymbol>();
        var declarations = model.Declarations.GroupBy(item => SourceFields.Identity<EntityId>(item.Data.Value, "entityId"))
            .ToDictionary(group => group.Key, group => group.ToArray());
        foreach (var symbol in model.Symbols.Where(item => item.Context.Equals(Input.Source.Context)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            SourceJson? type = null;
            if (symbol.PortId is { } portId && declarations.TryGetValue(symbol.EntityId, out var entries) && entries.Length == 1 &&
                entries[0].Declaration is TaskPlanDeclaration task)
                type = task.Ports.Single(port => port.PortId == portId).Type.Data;
            symbols.Add(new(symbol, type));
        }
        Symbols = new ReadOnlyCollection<CompilerInspectedSymbol>(symbols.OrderBy(item => item.Symbol.Key, StringComparer.Ordinal).ToArray());
        References = Array.AsReadOnly(model.References.Where(item => item.Origin.Context.Equals(Input.Source.Context))
            .OrderBy(item => item.Origin.Data.CanonicalText, StringComparer.Ordinal).ThenBy(item => item.State).ToArray());
        Data = SourceJson.FromObject(new
        {
            format = "coglatas.semantic-inspection/1", compilerInputId = Input.InputId.ToString(), inputDigest = Input.InputDigest.Data.Value,
            sourceDigest = Input.Source.Digest.Data.Value, context = Input.Source.Context.Data.Value, bindingComplete = BindingComplete,
            symbols = Symbols.Select(item => new
            {
                key = item.Symbol.Key, kind = item.Symbol.Kind, status = item.Symbol.Status.ToString(), location = item.Symbol.Location.Data.Value,
                declaredType = item.DeclaredType?.Value
            }).ToArray(),
            references = References.Select(item => new { origin = item.Origin.Data.Value, state = item.State.ToString(), symbolKey = item.SymbolKey }).ToArray()
        });
        cancellationToken.ThrowIfCancellationRequested();
        Digest = ContentDigest.Compute("coglatas.semantic-inspection/1", Data.ToCanonicalBytes());
    }

    public CompilerInspectedSymbol? Inspect(ProjectLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (!location.Context.Equals(Input.Source.Context)) return null;
        var matches = Symbols.Where(item => item.Symbol.Location.Data.Equals(location.Data)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public IReadOnlyList<CompilerBoundReference> FindReferences(ProjectLocation target)
    {
        var symbol = Inspect(target);
        return symbol is null ? Array.Empty<CompilerBoundReference>() : Array.AsReadOnly(References
            .Where(item => item.State == ReferenceResolutionState.Resolved && item.SymbolKey == symbol.Symbol.Key).ToArray());
    }
}
