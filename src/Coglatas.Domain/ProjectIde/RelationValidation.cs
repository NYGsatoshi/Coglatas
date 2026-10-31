namespace Coglatas.Domain.ProjectIde;

public enum RelationFindingState { Invalid, Unknown, Unsupported }
public sealed record RelationFinding(string Code, RelationFindingState State, ProjectLocation Location);
public sealed record RelationEndpointResolution(RelationId RelationId, bool IsSource, ReferenceResolutionState State);

/// <summary>Host-attested coverage of an exact immutable snapshot; the Core does not authenticate these claims.</summary>
public sealed record RelationScopeSnapshot(ProjectSource Source, bool IsAuthorized, bool DeclarationsComplete, bool RelationsComplete);
public sealed record RelationValidationLimits(int MaximumDeclarations = 10_000, int MaximumRelations = 20_000, int MaximumSnapshots = 32);

public sealed class RelationValidationResult
{
    public IReadOnlyList<RelationFinding> Findings { get; }
    public IReadOnlyList<RelationEndpointResolution> Resolutions { get; }
    public bool HasInvalid => Findings.Any(item => item.State == RelationFindingState.Invalid);
    public bool IsComplete => Findings.Count == 0;
    internal RelationValidationResult(IEnumerable<RelationFinding> findings, IEnumerable<RelationEndpointResolution> resolutions)
    {
        Findings = Array.AsReadOnly(findings.OrderBy(item => item.Location.Data.CanonicalText, StringComparer.Ordinal)
            .ThenBy(item => item.Code, StringComparer.Ordinal).ThenBy(item => item.State).Distinct().ToArray());
        Resolutions = Array.AsReadOnly(resolutions.OrderBy(item => item.RelationId.ToString(), StringComparer.Ordinal)
            .ThenByDescending(item => item.IsSource).ToArray());
    }
}

/// <summary>Bounded structural validation only. No Source mutation, live lookup, evidence proof or executable plan.</summary>
public static class RelationGraphValidator
{
    public static RelationValidationResult Validate(RelationScopeSnapshot input, IEnumerable<RelationScopeSnapshot>? externalSnapshots = null,
        IEnumerable<UnitDefinition>? units = null, RelationValidationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(input.Source);
        limits ??= new();
        if (limits.MaximumDeclarations is < 1 or > 100_000 || limits.MaximumRelations is < 1 or > 100_000 || limits.MaximumSnapshots is < 1 or > 128)
            throw new ArgumentOutOfRangeException(nameof(limits));
        var source = input.Source;
        var root = ProjectLocation.Create(source.Documents[0].DocumentId, source.Context);
        var findings = new List<RelationFinding>();
        var resolutions = new List<RelationEndpointResolution>();
        void RootFinding(string code, RelationFindingState state) => findings.Add(new(code, state, root));
        var snapshots = (externalSnapshots ?? []).Take(limits.MaximumSnapshots + 1).Prepend(input).ToArray();
        if (snapshots.Length > limits.MaximumSnapshots + 1 || snapshots.Any(item => item is null || item.Source is null) ||
            snapshots.Sum(item => (long)item.Source.Declarations.Value.GetArrayLength() + item.Source.Documents.Count) > limits.MaximumDeclarations ||
            snapshots.Sum(item => (long)item.Source.Relations.Value.GetArrayLength()) > limits.MaximumRelations)
        {
            RootFinding("coglatas.relation.budget-exhausted", RelationFindingState.Unknown);
            return new(findings, resolutions);
        }
        if (!input.IsAuthorized)
        {
            RootFinding("coglatas.relation.scope-unavailable", RelationFindingState.Unknown);
            return new(findings, resolutions);
        }
        if (!input.DeclarationsComplete || !input.RelationsComplete)
            RootFinding("coglatas.relation.incomplete-coverage", RelationFindingState.Unknown);
        var unitList = (units ?? []).Take(limits.MaximumDeclarations + 1).ToArray();
        if (unitList.Length > limits.MaximumDeclarations || unitList.Any(item => item is null))
        {
            RootFinding("coglatas.relation.unit-budget", RelationFindingState.Unknown);
            return new(findings, resolutions);
        }
        var declarations = snapshots.Where(item => item.IsAuthorized).Distinct().ToDictionary(item => item,
            item => PlanDeclarationReader.Read(item.Source).ToDictionary(result => EntityIdOf(result.Data)));
        var relations = TypedRelationReader.Read(source);
        var bound = new List<(TypedRelation Relation, PlanDeclaration Source, PlanDeclaration Target)>();
        foreach (var decoded in relations)
        {
            var id = SourceFields.Identity<RelationId>(decoded.Data.Value, "relationId");
            var document = SourceFields.Identity<DocumentId>(decoded.Data.Value, "documentId");
            var location = ProjectLocation.Create(document, source.Context, relationId: id);
            if (decoded.Relation is not { } relation)
            {
                findings.Add(new(decoded.Status == ValueDecodeStatus.Invalid ? "coglatas.relation.invalid-shape" : "coglatas.relation.unsupported-descriptor",
                    decoded.Status == ValueDecodeStatus.Invalid ? RelationFindingState.Invalid : RelationFindingState.Unsupported, location));
                continue;
            }
            var from = Resolve(relation.Source, true); var to = Resolve(relation.Target, false);
            if (from is null || to is null) continue;
            if (!CompatibleEndpoint(relation.Source, from, relation.Descriptor.SourceRole, relation.Descriptor.SourceKinds) ||
                !CompatibleEndpoint(relation.Target, to, relation.Descriptor.TargetRole, relation.Descriptor.TargetKinds))
            {
                Add("coglatas.relation.endpoint-type", RelationFindingState.Invalid); continue;
            }
            bound.Add((relation, from, to));
            switch (relation.Payload)
            {
                case DependencyRelationPayload dependency:
                    if (relation.Source.IdentityKey == relation.Target.IdentityKey) Add("coglatas.relation.self-dependency", RelationFindingState.Invalid);
                    if (dependency.Mode != "coglatas.finish-to-start") Add("coglatas.relation.mode-not-lowered", RelationFindingState.Unsupported);
                    if (dependency.Lag.State == ValueState.Unknown) Add("coglatas.relation.unknown-lag", RelationFindingState.Unknown);
                    if (dependency.Lag.KnownValue is WorkingDuration working &&
                        (from is not TaskPlanDeclaration { Duration.KnownValue: WorkingDuration taskWork } || !working.Calendar.Data.Equals(taskWork.Calendar.Data)))
                        Add("coglatas.relation.calendar-context", RelationFindingState.Unsupported);
                    break;
                case RequirementRelationPayload requirement when to is RequirementPlanDeclaration target:
                    var version = relation.Target.Reference.Context.CommittedRevision?.RevisionId.ToString();
                    if (version is null) Add("coglatas.relation.requirement-version-contract", RelationFindingState.Unsupported);
                    else if (requirement.RequirementVersion != version || !requirement.RequirementDigest.Equals(ContentDigest.Compute("coglatas.requirement/1", target.Data.ToCanonicalBytes())))
                        Add("coglatas.relation.requirement-version", RelationFindingState.Invalid);
                    if (!requirement.Condition.Data.Equals(target.Condition.Data) || requirement.EvidenceKind != target.EvidenceKind)
                        Add("coglatas.relation.requirement-context", RelationFindingState.Invalid);
                    if (!target.Target.Context.Equals(relation.Target.Reference.Context)) Add("coglatas.relation.requirement-target-context", RelationFindingState.Invalid);
                    // Evidence eligibility/freshness/runs are owned by later Compiler/Tests contracts.
                    Add("coglatas.relation.evidence-not-evaluated", RelationFindingState.Unknown);
                    break;
                case ResourceRelationPayload resource when to is ResourceBindingDeclaration binding:
                    var expected = relation.Kind == RelationKinds.Consumes ? ResourceBehavior.Consumable : ResourceBehavior.Reusable;
                    if (binding.Behavior != expected) Add("coglatas.relation.resource-kind", RelationFindingState.Invalid);
                    var matches = unitList.Where(unit => unit.UnitId == resource.Amount.Type.UnitId).ToArray();
                    if (matches.Length == 0) Add("coglatas.relation.unit-unavailable", RelationFindingState.Unknown);
                    else if (matches.Length != 1) Add("coglatas.relation.unit-ambiguous", RelationFindingState.Invalid);
                    else if (!matches[0].Dimension.Equals(resource.Amount.Type.Dimension)) Add("coglatas.relation.unit-dimension", RelationFindingState.Invalid);
                    if (resource.Amount.State == ValueState.Unknown || resource.At?.State == ValueState.Unknown ||
                        resource.Interval is { } interval && (interval.Start.State != ValueState.Known || interval.End.State != ValueState.Known))
                        Add("coglatas.relation.resource-input-unknown", RelationFindingState.Unknown);
                    // A binding pins the Resource snapshot but does not materialize capacity/stock units or authorization.
                    Add("coglatas.relation.resource-snapshot-not-evaluated", RelationFindingState.Unknown);
                    break;
                case WorkflowRelationPayload workflow when from is WorkflowPlanDeclaration definition:
                    if (!definition.States.Contains(workflow.State) || workflow.Transition is { } transition &&
                        !definition.Transitions.Any(item => item.TransitionId == transition && item.From == workflow.State))
                        Add("coglatas.relation.workflow-state", RelationFindingState.Invalid);
                    Add("coglatas.relation.workflow-engine-not-evaluated", RelationFindingState.Unsupported);
                    break;
                case ConstraintRelationPayload constraint when from is ConstraintPlanDeclaration definition:
                    if (!constraint.PredicateReference.Context.Equals(relation.Source.Reference.Context) ||
                        constraint.PredicateReference.EntityId != definition.EntityId ||
                        constraint.Version != relation.Source.Reference.Context.CommittedRevision?.RevisionId.ToString() ||
                        !constraint.PredicateDigest.Equals(ContentDigest.Compute("coglatas.predicate/1", definition.Predicate.Data.ToCanonicalBytes())))
                        Add("coglatas.relation.predicate-reference", RelationFindingState.Invalid);
                    Add("coglatas.relation.constraint-not-evaluated", RelationFindingState.Unknown);
                    break;
            }
            if (relation.Kind == RelationKinds.FlowsTo && from is TaskPlanDeclaration outputOwner && to is TaskPlanDeclaration inputOwner)
            {
                var output = outputOwner.Ports.Single(item => item.PortId == relation.Source.PortId);
                var inputPort = inputOwner.Ports.Single(item => item.PortId == relation.Target.PortId);
                if (output.Direction != PortDirection.Output || inputPort.Direction != PortDirection.Input) Add("coglatas.relation.port-direction", RelationFindingState.Invalid);
                if (!CompatiblePortType(output.Type, inputPort.Type)) Add("coglatas.relation.port-type", RelationFindingState.Invalid);
            }

            void Add(string code, RelationFindingState state) => findings.Add(new(code, state, relation.Location));
            PlanDeclaration? Resolve(RelationEndpoint endpoint, bool isSource)
            {
                ReferenceResolutionState state;
                PlanDeclaration? declaration = null;
                var reference = endpoint.Reference;
                if (reference.TenantId != source.TenantId) state = ReferenceResolutionState.OutOfScope;
                else
                {
                    var exact = snapshots.Where(item => item.Source.Context.Equals(reference.Context)).ToArray();
                    if (exact.Length == 0)
                        state = reference.ProjectId == source.ProjectId ? ReferenceResolutionState.OutOfScope : ReferenceResolutionState.UnknownDueToCoverage;
                    else if (exact.Any(item => !item.IsAuthorized)) state = ReferenceResolutionState.OutOfScope;
                    else if (exact.Length > 1) state = ReferenceResolutionState.Ambiguous;
                    else if (endpoint.Role == RelationEndpointRole.Relation) state = ReferenceResolutionState.Unsupported;
                    else if (!declarations[exact[0]].TryGetValue(reference.EntityId, out var result))
                        state = exact[0].DeclarationsComplete ? ReferenceResolutionState.MissingWithinCompleteScope : ReferenceResolutionState.UnknownDueToCoverage;
                    else if (result.Status != ValueDecodeStatus.Decoded)
                        state = ReferenceResolutionState.Unsupported;
                    else if (endpoint.PortId is { } port && (result.Declaration is not TaskPlanDeclaration task || !task.Ports.Any(item => item.PortId == port)))
                        state = ReferenceResolutionState.MissingWithinCompleteScope;
                    else { state = ReferenceResolutionState.Resolved; declaration = result.Declaration; }
                }
                resolutions.Add(new(relation.RelationId, isSource, state));
                if (state != ReferenceResolutionState.Resolved)
                    Add("coglatas.relation.reference-" + ResolutionToken(state), state switch
                    {
                        ReferenceResolutionState.UnknownDueToCoverage => RelationFindingState.Unknown,
                        ReferenceResolutionState.Unsupported => RelationFindingState.Unsupported, _ => RelationFindingState.Invalid
                    });
                return declaration;
            }
        }
        CheckCardinality();
        CheckCycles(RelationKinds.DependsOn);
        CheckCycles(RelationKinds.FlowsTo);
        return new(findings, resolutions);

        void CheckCardinality()
        {
            var connectionCounts = new Dictionary<(EntityId Entity, PortId Port), int>();
            foreach (var edge in bound.Where(item => item.Relation.Kind == RelationKinds.FlowsTo))
            {
                foreach (var endpoint in new[] { edge.Relation.Source, edge.Relation.Target })
                    if (endpoint.Reference.Context.Equals(source.Context) && endpoint.PortId is { } port)
                    {
                        var key = (endpoint.Reference.EntityId, port);
                        connectionCounts[key] = connectionCounts.GetValueOrDefault(key) + 1;
                    }
            }
            var keys = new Dictionary<string, TypedRelation>(StringComparer.Ordinal);
            foreach (var entry in bound)
            {
                var relation = entry.Relation;
                var key = SourceJson.FromObject(new { kind = relation.Kind, source = relation.Source.IdentityKey, target = relation.Target.IdentityKey,
                    payload = relation.Data.Value.GetProperty("payload") }).CanonicalText;
                if (!keys.TryAdd(key, relation)) findings.Add(new("coglatas.relation.duplicate", RelationFindingState.Invalid, relation.Location));
            }
            foreach (var group in bound.Where(item => item.Relation.Kind == RelationKinds.Governs).GroupBy(item => item.Relation.Target.IdentityKey))
                if (group.Count() > 1)
                    foreach (var entry in group) findings.Add(new("coglatas.relation.workflow-cardinality", RelationFindingState.Invalid, entry.Relation.Location));
            foreach (var item in declarations[input].Values)
            {
                if (item.Declaration is not TaskPlanDeclaration task) continue;
                foreach (var port in task.Ports)
                {
                    var count = connectionCounts.GetValueOrDefault((task.EntityId, port.PortId));
                    var location = ProjectLocation.Create(task.DocumentId, source.Context, "/payload/ports/" + port.PortId, entityId: task.EntityId);
                    if (port.MaximumConnections is { } maximum && count > maximum)
                        findings.Add(new("coglatas.relation.port-cardinality", RelationFindingState.Invalid, location));
                    if (count < port.MinimumConnections && port.DefaultValue is null)
                        findings.Add(new("coglatas.relation.required-port", input.DeclarationsComplete && input.RelationsComplete &&
                            relations.All(edge => edge.Status == ValueDecodeStatus.Decoded) && resolutions.All(item => item.State == ReferenceResolutionState.Resolved)
                            ? RelationFindingState.Invalid : RelationFindingState.Unknown, location));
                }
            }
        }

        void CheckCycles(string kind)
        {
            // Iterative strongly connected components, O(V+E); downstream tails are not cycle members.
            var edges = bound.Where(item => item.Relation.Kind == kind).Select(item => item.Relation).ToArray();
            string Vertex(RelationEndpoint endpoint) => SourceJson.FromObject(new { context = endpoint.Reference.Context.Data.Value, entityId = endpoint.Reference.EntityId.ToString() }).CanonicalText;
            var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var reverse = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var edge in edges)
            {
                var from = Vertex(edge.Source); var to = Vertex(edge.Target);
                if (!adjacency.ContainsKey(from)) adjacency[from] = [];
                if (!adjacency.ContainsKey(to)) adjacency[to] = [];
                if (!reverse.ContainsKey(from)) reverse[from] = [];
                if (!reverse.ContainsKey(to)) reverse[to] = [];
                adjacency[from].Add(to); reverse[to].Add(from);
            }
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var finish = new List<string>();
            foreach (var start in adjacency.Keys)
            {
                if (!visited.Add(start)) continue;
                var stack = new Stack<(string Vertex, int Next)>(); stack.Push((start, 0));
                while (stack.TryPop(out var frame))
                {
                    if (frame.Next == adjacency[frame.Vertex].Count) { finish.Add(frame.Vertex); continue; }
                    stack.Push((frame.Vertex, frame.Next + 1));
                    var next = adjacency[frame.Vertex][frame.Next];
                    if (visited.Add(next)) stack.Push((next, 0));
                }
            }
            var components = new Dictionary<string, int>(StringComparer.Ordinal);
            var sizes = new List<int>();
            for (var i = finish.Count - 1; i >= 0; i--)
            {
                var start = finish[i]; if (components.ContainsKey(start)) continue;
                var component = sizes.Count; var size = 0;
                var stack = new Stack<string>(); stack.Push(start); components[start] = component;
                while (stack.TryPop(out var vertex))
                {
                    size++;
                    foreach (var next in reverse[vertex])
                        if (components.TryAdd(next, component)) stack.Push(next);
                }
                sizes.Add(size);
            }
            foreach (var edge in edges.Where(edge => components[Vertex(edge.Source)] == components[Vertex(edge.Target)] &&
                         (sizes[components[Vertex(edge.Source)]] > 1 || Vertex(edge.Source) == Vertex(edge.Target))))
                findings.Add(new(kind == RelationKinds.DependsOn ? "coglatas.relation.dependency-cycle" : "coglatas.relation.dataflow-cycle", RelationFindingState.Invalid, edge.Location));
        }
    }

    private static EntityId EntityIdOf(SourceJson data) => SourceFields.Identity<EntityId>(data.Value, "entityId");
    private static bool CompatiblePortType(TypeReference output, TypeReference input)
    {
        if (output.Nullable && !input.Nullable || output.AcceptsUnknown && !input.AcceptsUnknown) return false;
        SourceJson Shape(TypeReference type) => SourceJson.FromObject(type.Data.Value.EnumerateObject()
            .Where(item => item.Name is not ("nullable" or "acceptsUnknown")).ToDictionary(item => item.Name, item => item.Value));
        return Shape(output).Equals(Shape(input));
    }
    private static bool CompatibleEndpoint(RelationEndpoint endpoint, PlanDeclaration declaration, RelationEndpointRole role, IReadOnlyList<string> kinds) =>
        endpoint.Role == role && endpoint.ExpectedKind == declaration.Kind && kinds.Contains(declaration.Kind, StringComparer.Ordinal);
    private static string ResolutionToken(ReferenceResolutionState state) => state switch
    {
        ReferenceResolutionState.MissingWithinCompleteScope => "missing", ReferenceResolutionState.UnknownDueToCoverage => "coverage-unknown",
        ReferenceResolutionState.Ambiguous => "ambiguous", ReferenceResolutionState.Unsupported => "unsupported", ReferenceResolutionState.OutOfScope => "out-of-scope",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };
}
