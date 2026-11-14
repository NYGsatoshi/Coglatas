namespace Coglatas.Domain.ProjectIde;

/// <summary>Root-bound development artifact over a completely validated declared graph.
/// Context slots preserve stable node identities across versions. This grants no current read or execution authority.</summary>
public sealed class CompilerScopedIr
{
    public const string Format = "coglatas.project-ir-closure/1";
    public const string SourceMapFormat = "coglatas.semantic-closure-source-map/1";
    public CompilerInputManifest Input { get; }
    public IReadOnlyList<CompilerDependencyRegion> Regions { get; }
    public SourceJson SourceMap { get; }
    public ContentDigest SourceMapDigest { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }
    public int LogicalOperationsConsumed { get; }
    private readonly Dictionary<(SourceRevisionContext Context, Guid Id), ProjectLocation> _nodes = new();
    private readonly Dictionary<(SourceRevisionContext Context, RelationId Id), ProjectLocation> _relations = new();

    private CompilerScopedIr(CompilerBindingClosure binding, CompilerGraphClosure graph,
        IReadOnlyList<CompilerDependencyRegion> regions, ref int remaining, CancellationToken token)
    {
        Input = binding.Input;
        var initial = remaining;
        Regions = new ImmutableListView<CompilerDependencyRegion>(regions.OrderBy(region => region.Binding.Source.Context.Data.CanonicalText, StringComparer.Ordinal));
        var entities = new HashSet<(SourceRevisionContext Context, EntityId Id)>();
        var ports = new HashSet<(SourceRevisionContext Context, EntityId Entity, PortId Port)>();
        foreach (var region in Regions)
        {
            token.ThrowIfCancellationRequested();
            var context = region.Binding.Source.Context;
            foreach (var node in region.Validation!.Ir!.Nodes)
            {
                token.ThrowIfCancellationRequested();
                if (--remaining < 0) throw new FormatException("Scoped IR exhausted the root logical budget.");
                if (!node.Origin.Context.Equals(context) || !_nodes.TryAdd((context, node.NodeId.Value), node.Origin) || !entities.Add((context, node.EntityId)))
                    throw new FormatException("Scoped IR requires unique exact-context origins.");
            }
            foreach (var task in region.Binding.Declarations.Select(row => row.Declaration).OfType<TaskPlanDeclaration>())
                foreach (var port in task.Ports)
                {
                    token.ThrowIfCancellationRequested();
                    if (--remaining < 0) throw new FormatException("Scoped IR exhausted the root logical budget.");
                    ports.Add((context, task.EntityId, port.PortId));
                }
        }
        foreach (var region in Regions)
            foreach (var relation in region.Binding.Relations.Select(row => row.Relation).OfType<TypedRelation>())
            {
                token.ThrowIfCancellationRequested();
                if (--remaining < 0) throw new FormatException("Scoped IR exhausted the root logical budget.");
                foreach (var endpoint in new[] { relation.Source, relation.Target })
                    if (!entities.Contains((endpoint.Reference.Context, endpoint.Reference.EntityId)) ||
                        endpoint.PortId is { } port && !ports.Contains((endpoint.Reference.Context, endpoint.Reference.EntityId, port)))
                        throw new FormatException("Scoped IR endpoints require an exact admitted context/entity/port.");
                if (!_relations.TryAdd((region.Binding.Source.Context, relation.RelationId), relation.Location))
                    throw new FormatException("Scoped IR requires unique owner-qualified relation identities.");
            }
        SourceMap = SourceJson.FromObject(new
        {
            format = SourceMapFormat, compilerInputId = Input.InputId.ToString(), rootInputDigest = Input.InputDigest.Data.Value,
            bindingClosureDigest = binding.Digest.Data.Value, graphDigest = graph.Digest.Data.Value,
            regions = Regions.Select(region => new
            {
                context = region.Binding.Source.Context.Data.Value, sourceDigest = region.Binding.Source.Digest.Data.Value,
                sourceMapDigest = region.Validation!.Ir!.SourceMapDigest.Data.Value, sourceMap = region.Validation.Ir.SourceMap.Value
            }).ToArray()
        }, Input.Source.Data.Limits);
        if (!ProjectSemanticBinder.WithinBudget(SourceMap.Value, ref remaining, token))
            throw new FormatException("Scoped source map exhausted the root logical budget.");
        SourceMapDigest = ContentDigest.Compute(SourceMapFormat, SourceMap.ToCanonicalBytes());
        Data = SourceJson.FromObject(new
        {
            format = Format, compilerInputId = Input.InputId.ToString(), rootInputDigest = Input.InputDigest.Data.Value,
            bindingClosureDigest = binding.Digest.Data.Value, graphDigest = graph.Digest.Data.Value,
            regions = Regions.Select(region => new
            {
                context = region.Binding.Source.Context.Data.Value, sourceDigest = region.Binding.Source.Digest.Data.Value,
                bindingRegionDigest = region.Binding.Digest.Data.Value, irDigest = region.Validation!.Ir!.Digest.Data.Value,
                ir = region.Validation.Ir.Data.Value
            }).ToArray(), sourceMapDigest = SourceMapDigest.Data.Value,
            executionPlan = new { kind = "coglatas.not-produced", reason = "coglatas.analysis-and-plan-lowering-required" }
        }, Input.Source.Data.Limits);
        if (!ProjectSemanticBinder.WithinBudget(Data.Value, ref remaining, token))
            throw new FormatException("Scoped IR exhausted the root logical budget.");
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
        token.ThrowIfCancellationRequested(); LogicalOperationsConsumed = initial - Math.Max(0, remaining);
    }

    /// <summary>Exact captured-source lookup only; the caller must separately admit current read/navigation authority.</summary>
    public ProjectLocation? ResolveNode(SourceRevisionContext context, IrNodeId node) => _nodes.GetValueOrDefault((context, node.Value));
    public ProjectLocation? ResolveRelation(SourceRevisionContext owner, RelationId relation) => _relations.GetValueOrDefault((owner, relation));

    internal static CompilerScopedIr Build(CompilerBindingClosure binding, CompilerGraphClosure graph,
        IReadOnlyList<CompilerDependencyRegion> regions, ref int remaining, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (binding.State != CompilerBindingClosureState.Complete || !binding.IncludesDeclaredSnapshots || !ReferenceEquals(graph.Binding, binding) ||
            !graph.StructuralValidated || regions.Count != binding.Regions.Count ||
            regions.Select(region => region.Binding.Digest).Distinct().Count() != regions.Count ||
            regions.Any(region => !binding.Regions.Contains(region.Binding) || region.Validation?.Ir is null))
            throw new ArgumentException("Scoped IR requires every exact declared region and a validated whole graph.");
        return new(binding, graph, regions, ref remaining, token);
    }
}
