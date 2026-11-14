using System.Text;
using System.Text.Json.Nodes;

namespace Coglatas.Domain.ProjectIde;

public enum CompilerBindingClosureState { Complete, Incomplete, StaleInput, ResourceLimit }

/// <summary>A derived binding region belongs to the complete root input; it is not an independently reusable compiler input.</summary>
public sealed class CompilerBindingRegion
{
    public CompilerInputManifest RootInput { get; }
    public ProjectSource Source => Model.Input.Source;
    public bool IsRoot { get; }
    public bool BindingComplete => Model.BindingComplete;
    public IReadOnlyList<DeclarationDecodeResult> Declarations => Model.Declarations;
    public IReadOnlyList<RelationDecodeResult> Relations => Model.Relations;
    public IReadOnlyList<CompilerBoundReference> References => Model.References;
    public IReadOnlyList<CompilerBindingFinding> Findings => Model.Findings;
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }
    internal PartialSemanticModel Model { get; }
    internal CompilerBindingRegion(CompilerInputManifest root, PartialSemanticModel model, CompilerMaterializedSnapshot? snapshot)
    {
        RootInput = root; Model = model; IsRoot = snapshot is null;
        Data = SourceJson.FromObject(new { format = "coglatas.semantic-binding-region/1", rootInputDigest = root.InputDigest.Data.Value,
            context = Source.Context.Data.Value, sourceDigest = Source.Digest.Data.Value, isRoot = IsRoot,
            snapshot = snapshot is null ? null : new { snapshotId = snapshot.SnapshotId.ToString(), version = snapshot.Version },
            bindingComplete = BindingComplete,
            declarations = Declarations.Select(item => new { status = item.Status.ToString(), digest = ContentDigest.Compute("coglatas.binding-declaration/1", item.Data.ToCanonicalBytes()).Data.Value }).ToArray(),
            relations = Relations.Select(item => new { status = item.Status.ToString(), digest = ContentDigest.Compute("coglatas.binding-relation/1", item.Data.ToCanonicalBytes()).Data.Value }).ToArray(),
            references = References.Select(item => new { origin = item.Origin.Data.Value, state = item.State.ToString(), symbolKey = item.SymbolKey }).ToArray(),
            findings = Findings.Select(item => new { state = item.State.ToString(), diagnostic = item.Diagnostic.Data.Value }).ToArray()
        }, root.Source.Data.Limits);
        Digest = ContentDigest.Compute("coglatas.semantic-binding-region/1", Data.ToCanonicalBytes());
    }
}

/// <summary>Reachable binding closure over already admitted fixed inputs. Complete binding does not prove semantic validation, IR or execution PASS.</summary>
public sealed class CompilerBindingClosure
{
    public const string Format = "coglatas.semantic-binding-closure/1";
    public CompilerInputManifest Input { get; }
    public CompilerBindingClosureState State { get; }
    public IReadOnlyList<CompilerBindingRegion> Regions { get; }
    public bool IncludesDeclaredSnapshots { get; }
    public int LogicalOperationsConsumed { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }
    private CompilerBindingClosure(CompilerInputManifest input, CompilerBindingClosureState state, IEnumerable<CompilerBindingRegion> regions, int consumed, bool includesDeclaredSnapshots)
    {
        Input = input; State = state; LogicalOperationsConsumed = consumed; IncludesDeclaredSnapshots = includesDeclaredSnapshots;
        Regions = new ImmutableListView<CompilerBindingRegion>(regions.OrderBy(item => item.Source.Context.Data.CanonicalText, StringComparer.Ordinal));
        Data = SourceJson.FromObject(new { format = Format, compilerInputId = input.InputId.ToString(), inputDigest = input.InputDigest.Data.Value,
            context = input.Source.Context.Data.Value, state = state.ToString(), logicalOperationsConsumed = consumed,
            regions = Regions.Select(item => new { digest = item.Digest.Data.Value, region = item.Data.Value }).ToArray()
        }, input.Source.Data.Limits);
        if (IncludesDeclaredSnapshots)
        {
            var data = JsonNode.Parse(Data.CanonicalText)!.AsObject(); data["scope"] = "coglatas.declared-snapshots";
            Data = SourceJson.FromObject(data, input.Source.Data.Limits);
        }
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }
    public static CompilerBindingClosure Build(PartialSemanticModel root, ContentDigest expectedInput, CancellationToken cancellationToken = default,
        PlanDeclarationReadCache? declarationCache = null)
        => BuildCore(root, expectedInput, cancellationToken, declarationCache, null, false);

    internal static CompilerBindingClosure BuildWithinBudget(PartialSemanticModel root, int remainingOperations, CancellationToken cancellationToken, PlanDeclarationReadCache? declarationCache)
        => BuildCore(root, root.Input.InputDigest, cancellationToken, declarationCache, remainingOperations, true);

    private static CompilerBindingClosure BuildCore(PartialSemanticModel root, ContentDigest expectedInput, CancellationToken cancellationToken,
        PlanDeclarationReadCache? declarationCache, int? remainingOperations, bool includeDeclaredSnapshots)
    {
        ArgumentNullException.ThrowIfNull(root); ArgumentNullException.ThrowIfNull(expectedInput); cancellationToken.ThrowIfCancellationRequested();
        var input = root.Input; var remaining = remainingOperations ?? input.ExecutionProfile.MaximumOperations - root.LogicalOperationsConsumed;
        var regions = new List<CompilerBindingRegion>();
        CompilerBindingClosure Finish(CompilerBindingClosureState state)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = new CompilerBindingClosure(input, state, regions, input.ExecutionProfile.MaximumOperations - Math.Max(0, remaining), includeDeclaredSnapshots);
            cancellationToken.ThrowIfCancellationRequested(); return value;
        }
        if (!input.InputDigest.Equals(expectedInput)) return Finish(CompilerBindingClosureState.StaleInput);
        try
        {
            regions.Add(new(input, root, null));
            if (!root.BindingComplete) return Finish(root.Findings.Any(item => item.Diagnostic.Code.Contains("budget", StringComparison.Ordinal))
                ? CompilerBindingClosureState.ResourceLimit : CompilerBindingClosureState.Incomplete);
            var registry = root.MaterializedSnapshots.GroupBy(item => item.Snapshot.Source.Context).ToDictionary(group => group.Key, group => group.ToArray());
            var visited = new HashSet<SourceRevisionContext> { input.Source.Context };
            var pending = new SortedDictionary<string, SourceRevisionContext>(StringComparer.Ordinal);
            if (includeDeclaredSnapshots)
                foreach (var pin in input.Settings.Value.GetProperty("authorizedExternalSnapshots").EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!ProjectSemanticBinder.WithinBudget(pin, ref remaining, cancellationToken)) return Finish(CompilerBindingClosureState.ResourceLimit);
                    var context = SourceRevisionContext.Parse(SourceJson.FromElement(pin.GetProperty("context")));
                    if (!visited.Contains(context)) pending.TryAdd(context.Data.CanonicalText, context);
                }
            bool Discover(PartialSemanticModel model)
            {
                foreach (var reference in model.References)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (reference.State != ReferenceResolutionState.Resolved || reference.SymbolKey is null) return false;
                    remaining -= Encoding.UTF8.GetByteCount(reference.SymbolKey) + 1;
                    if (remaining < 0) return false;
                    var key = SourceJson.Parse(reference.SymbolKey);
                    var context = SourceRevisionContext.Parse(SourceJson.FromElement(key.Value.GetProperty("context")));
                    if (!visited.Contains(context)) pending.TryAdd(context.Data.CanonicalText, context);
                }
                return true;
            }
            if (!Discover(root)) return Finish(remaining < 0 ? CompilerBindingClosureState.ResourceLimit : CompilerBindingClosureState.Incomplete);
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (--remaining < 1) return Finish(CompilerBindingClosureState.ResourceLimit);
                var next = pending.First(); pending.Remove(next.Key); var context = next.Value;
                if (!visited.Add(context)) continue;
                if (visited.Count > 33 || !registry.TryGetValue(context, out var candidates))
                    return Finish(visited.Count > 33 ? CompilerBindingClosureState.ResourceLimit : CompilerBindingClosureState.Incomplete);
                var snapshot = candidates.OrderBy(item => item.SnapshotId.ToString(), StringComparer.Ordinal).First();
                if (candidates.Any(item => item.Version != snapshot.Version || !item.Snapshot.Source.Data.Equals(snapshot.Snapshot.Source.Data) ||
                    item.Snapshot.DeclarationsComplete != snapshot.Snapshot.DeclarationsComplete || item.Snapshot.RelationsComplete != snapshot.Snapshot.RelationsComplete))
                    return Finish(CompilerBindingClosureState.Incomplete);
                var settings = JsonNode.Parse(input.Settings.CanonicalText)!.AsObject();
                var pins = settings["authorizedExternalSnapshots"]!.AsArray();
                foreach (var pin in pins.Where(pin => SourceRevisionContext.Parse(SourceJson.Parse(pin!["context"]!.ToJsonString())).Equals(context)).ToArray()) pins.Remove(pin);
                // This region remains bound to root.InputDigest; no fictitious root snapshot identity/version is minted.
                var scenario = snapshot.Snapshot.Source.Context.Scenario;
                // Only the already-applied Source and exact context are bound here. Dependency overlay intent is not replayed or reconstructed.
                settings["scenario"] = scenario is null ? new JsonObject { ["kind"] = "coglatas.none" } :
                    JsonNode.Parse(SourceJson.FromObject(new { baseContext = scenario.BaseContext.Data.Value, overlayDigest = scenario.OverlayDigest.Data.Value,
                        assumptions = Array.Empty<object>() }).CanonicalText);
                settings["sourceCoverage"] = snapshot.Snapshot.DeclarationsComplete && snapshot.Snapshot.RelationsComplete ? "coglatas.complete" : "coglatas.partial";
                settings["executionProfile"]!["maximumOperations"] = remaining;
                var regionInput = new CompilerInputManifest(snapshot.Snapshot.Source, SourceJson.FromObject(settings, input.Settings.Limits));
                var model = ProjectSemanticBinder.BindRegion(regionInput,
                    root.MaterializedSnapshots.Where(item => !item.Snapshot.Source.Context.Equals(context)), input, cancellationToken, declarationCache);
                remaining -= model.LogicalOperationsConsumed;
                regions.Add(new(input, model, snapshot));
                if (!model.BindingComplete) return Finish(remaining <= 0 || model.Findings.Any(item => item.Diagnostic.Code.Contains("budget", StringComparison.Ordinal))
                    ? CompilerBindingClosureState.ResourceLimit : CompilerBindingClosureState.Incomplete);
                if (!Discover(model)) return Finish(remaining < 0 ? CompilerBindingClosureState.ResourceLimit : CompilerBindingClosureState.Incomplete);
            }
            cancellationToken.ThrowIfCancellationRequested(); return Finish(CompilerBindingClosureState.Complete);
        }
        catch (FormatException)
        {
            // A bounded publication cannot turn a truncated prefix into a complete closure.
            regions.Clear(); return Finish(CompilerBindingClosureState.ResourceLimit);
        }
    }
}
