namespace Coglatas.Domain.ProjectIde;

public sealed record IrNodeId
{
    public Guid Value { get; }
    public ContentDigest DerivationDigest { get; }
    internal IrNodeId(TenantId tenant, ProjectId project, EntityId entity) => (Value, DerivationDigest) = CompilerIdentity.Derive("IRNodeId",
        SourceJson.FromObject(new object[] { new { tenantId = tenant.ToString(), projectId = project.ToString(), entityId = entity.ToString() }, "coglatas.declaration", "coglatas.primary" }));
    public override string ToString() => Value.ToString("D");
}

public sealed class ProjectIrNode
{
    public IrNodeId NodeId { get; }
    public EntityId EntityId { get; }
    public string Kind { get; }
    public ProjectLocation Origin { get; }
    public SourceJson Data { get; }
    internal ProjectIrNode(CompilerInputManifest input, PlanDeclaration declaration)
    {
        EntityId = declaration.EntityId; Kind = declaration.Kind;
        NodeId = new(input.Source.TenantId, input.Source.ProjectId, EntityId);
        Origin = ProjectLocation.Create(declaration.DocumentId, input.Source.Context, entityId: EntityId);
        Data = SourceJson.FromObject(new { irNodeId = NodeId.ToString(), derivationDigest = NodeId.DerivationDigest.Data.Value,
            sourceEntityId = EntityId.ToString(), kind = Kind, payload = declaration.Payload.Value });
    }
}

/// <summary>Regenerable inspection/analysis artifact. This type never contains a runnable plan or writable Source.</summary>
public sealed class DerivedProjectIr
{
    public CompilerInputManifest Input { get; }
    public IReadOnlyList<ProjectIrNode> Nodes { get; }
    public SourceJson Relations { get; }
    public SourceJson SourceMap { get; }
    public ContentDigest SourceMapDigest { get; }
    public ContentDigest SemanticModelDigest { get; }
    public ContentDigest Digest { get; }
    public SourceJson Data { get; }
    internal DerivedProjectIr(PartialSemanticModel model, bool scopedRegion = false)
    {
        Input = model.Input;
        var nodes = model.Declarations.Select(item => new ProjectIrNode(Input, item.Declaration ?? throw new InvalidOperationException("Opaque declaration cannot be lowered.")))
            .OrderBy(item => item.NodeId.ToString(), StringComparer.Ordinal).ToArray();
        if (nodes.Select(item => item.NodeId.Value).Distinct().Count() != nodes.Length)
            throw new InvalidOperationException("IR identity collision; full derivation digests must be investigated.");
        Nodes = new ImmutableListView<ProjectIrNode>(nodes);
        var endpointIndex = model.Symbols.Where(symbol => symbol.PortId is null).GroupBy(symbol => (symbol.Context, symbol.EntityId))
            .ToDictionary(group => group.Key, group => group.Count());
        IrNodeId Resolve(RelationEndpoint endpoint)
        {
            var reference = endpoint.Reference;
            if (!endpointIndex.TryGetValue((reference.Context, reference.EntityId), out var matches) || matches != 1) throw new FormatException("IR endpoint requires one exact context-qualified declaration.");
            return new(reference.Context.Branch.TenantId, reference.Context.Branch.ProjectId, reference.EntityId);
        }
        object Lower(TypedRelation item)
        {
            var sourceId = Resolve(item.Source); var targetId = Resolve(item.Target);
            if (scopedRegion) return new
            {
                relationId = item.RelationId.ToString(), kind = item.Kind, descriptorVersion = item.DescriptorVersion,
                source = new { irNodeId = sourceId.ToString(), context = item.Source.Reference.Context.Data.Value, portId = item.Source.PortId?.ToString() },
                target = new { irNodeId = targetId.ToString(), context = item.Target.Reference.Context.Data.Value, portId = item.Target.PortId?.ToString() },
                payload = item.Data.Value.GetProperty("payload")
            };
            return new
            {
                relationId = item.RelationId.ToString(), kind = item.Kind, descriptorVersion = item.DescriptorVersion,
                sourceNodeId = sourceId.ToString(), sourcePortId = item.Source.PortId?.ToString(),
                targetNodeId = targetId.ToString(), targetPortId = item.Target.PortId?.ToString(),
                payload = item.Data.Value.GetProperty("payload")
            };
        }
        Relations = SourceJson.FromObject(model.Relations.Select(item => item.Relation ?? throw new InvalidOperationException("Opaque relation cannot be lowered."))
            .OrderBy(item => item.RelationId.ToString(), StringComparer.Ordinal).Select(Lower).ToArray());
        var mapFormat = scopedRegion ? "coglatas.semantic-region-source-map/1" : "coglatas.semantic-source-map/1";
        var irFormat = scopedRegion ? "coglatas.project-ir-region/1" : "coglatas.project-ir/1";
        SourceMap = SourceJson.FromObject(new
        {
            format = mapFormat, compilerInputId = Input.InputId.ToString(),
            nodes = nodes.Select(item => new { irNodeId = item.NodeId.ToString(), location = item.Origin.Data.Value }).ToArray(),
            relations = model.Relations.Select(item => item.Relation!).OrderBy(item => item.RelationId.ToString(), StringComparer.Ordinal)
                .Select(item => new { relationId = item.RelationId.ToString(), location = item.Location.Data.Value }).ToArray()
        });
        SourceMapDigest = ContentDigest.Compute(mapFormat, SourceMap.ToCanonicalBytes());
        SemanticModelDigest = ContentDigest.Compute("coglatas.semantic-model/1", SourceJson.FromObject(new
        {
            compilerInputId = Input.InputId.ToString(), declarations = model.Declarations.OrderBy(item => item.Declaration!.EntityId.ToString(), StringComparer.Ordinal).Select(item => item.Data.Value).ToArray(),
            relations = model.Relations.OrderBy(item => item.Relation!.RelationId.ToString(), StringComparer.Ordinal).Select(item => item.Data.Value).ToArray(),
            references = model.References.Select(item => new { origin = item.Origin.Data.Value, state = "coglatas.resolved", symbolKey = item.SymbolKey }).ToArray()
        }).ToCanonicalBytes());
        Data = SourceJson.FromObject(new
        {
            format = irFormat, compilerInputId = Input.InputId.ToString(), inputDigest = Input.InputDigest.Data.Value,
            sourceDigest = Input.Source.Digest.Data.Value, semanticModelDigest = SemanticModelDigest.Data.Value,
            nodes = nodes.Select(item => item.Data.Value).ToArray(), relations = Relations.Value, sourceMapDigest = SourceMapDigest.Data.Value,
            executionPlan = new { kind = "coglatas.not-produced", reason = "coglatas.analysis-and-plan-lowering-required" }
        });
        Digest = ContentDigest.Compute(irFormat, Data.ToCanonicalBytes());
    }
}
