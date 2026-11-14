namespace Coglatas.Domain.ProjectIde;

/// <summary>Immutable definition, never run state or execution authority. This initial engine lowers elapsed Task DAGs.</summary>
public sealed class CompilerExecutionPlan
{
    public const string Format = "coglatas.execution-plan-definition/1";
    public const string Target = "coglatas.execution-plan-definition";
    private static readonly SourceJson EngineContract = SourceJson.FromObject(new {
        format = "coglatas.plan-lowering-engine/1", kind = "coglatas.elapsed-finish-to-start", version = 1,
        duration = "coglatas.known-nonnegative-elapsed-ticks", lag = "coglatas.known-nonnegative-elapsed-ticks",
        ports = "coglatas.fixed-input-defaults", order = "coglatas.context-then-entity-ordinal", execution = "coglatas.definition-only",
        relations = new[] { RelationKinds.DependsOn, RelationKinds.Constrains, RelationKinds.RelatedTo }
    });
    public static CompilerArtifactIdentity Engine { get; } = new(SourceJson.FromObject(new { artifactId = "coglatas.elapsed-finish-to-start-plan-lowerer", version = "1",
        digest = ContentDigest.Compute("coglatas.plan-lowering-engine/1", EngineContract.ToCanonicalBytes()).Data.Value }));
    public CompilerInputManifest Input { get; }
    public CompilerScopedIr Scope { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }
    public int LogicalOperationsConsumed { get; }
    internal static bool Requested(CompilerInputManifest input) => input.Settings.Value.GetProperty("targets").EnumerateArray().Any(v => v.GetString() == Target);
    private sealed record TaskRow(string Key, SourceRevisionContext Context, TaskPlanDeclaration Declaration, IrNodeId Node, ProjectLocation Location);
    private sealed record EdgeRow(TypedRelation Relation, TaskRow Dependent, TaskRow Prerequisite, DependencyRelationPayload Payload);
    private CompilerExecutionPlan(CompilerScopedIr scope, TaskRow[] tasks, EdgeRow[] edges, TaskRow[] evaluationOrder, int initial, ref int remaining, CancellationToken token)
    {
        Input = scope.Input; Scope = scope;
        Data = SourceJson.FromObject(new {
            format = Format, compilerInputId = Input.InputId.ToString(), inputDigest = Input.InputDigest.Data.Value,
            sourceDigest = Input.Source.Digest.Data.Value, closureDigest = scope.Digest.Data.Value, engine = Engine.Data.Value,
            regions = scope.Regions.Select(r => new { context = r.Binding.Source.Context.Data.Value, sourceDigest = r.Binding.Source.Digest.Data.Value }).ToArray(),
            tasks = tasks.Select(t => new { context = t.Context.Data.Value, irNodeId = t.Node.ToString(), sourceEntityId = t.Declaration.EntityId.ToString(),
                location = t.Location.Data.Value, duration = t.Declaration.Duration.Data.Value, ports = t.Declaration.Ports.Select(p => p.Data.Value).ToArray() }).ToArray(),
            dependencies = edges.Select(e => new { owner = e.Relation.Location.Context.Data.Value, relationId = e.Relation.RelationId.ToString(),
                location = e.Relation.Location.Data.Value, mode = e.Payload.Mode, lag = e.Payload.Lag.Data.Value,
                dependent = new { context = e.Dependent.Context.Data.Value, irNodeId = e.Dependent.Node.ToString() },
                prerequisite = new { context = e.Prerequisite.Context.Data.Value, irNodeId = e.Prerequisite.Node.ToString() } }).ToArray(),
            definitionOrder = evaluationOrder.Select(t => new { context = t.Context.Data.Value, irNodeId = t.Node.ToString() }).ToArray(),
            execution = "coglatas.not-instantiated"
        }, Input.Source.Data.Limits);
        if (!ProjectSemanticBinder.WithinBudget(Data.Value, ref remaining, token)) throw new FormatException("Plan definition exhausted the root logical budget.");
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
        token.ThrowIfCancellationRequested(); LogicalOperationsConsumed = initial - Math.Max(0, remaining);
    }
    internal static (CompilerExecutionPlan? Plan, string? Reason) Prepare(CompilerScopedIr scope, ref int remaining, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var initial = remaining; var input = scope.Input;
        if (!Requested(input)) return (null, null);
        if (input.PlanLowerer is null || !input.PlanLowerer.Data.Equals(Engine.Data)) return (null, "coglatas.plan.engine-unsupported");
        foreach (var region in scope.Regions) {
            var actual = new Dictionary<string, CompilerRuleOutcome>(StringComparer.Ordinal);
            foreach (var rule in region.Rules) { token.ThrowIfCancellationRequested(); Charge(ref remaining); actual.Add(rule.RuleId, rule); }
            foreach (var rule in input.Rules.Where(r => r.Required)) {
                token.ThrowIfCancellationRequested(); Charge(ref remaining);
                if (!actual.TryGetValue(rule.RuleId, out var outcome) || outcome.Version != rule.Version || outcome.Execution != CompilerExecutionState.Completed ||
                    outcome.Coverage != CompilerCoverage.Complete || outcome.Verdict != CompilerVerdict.Pass || outcome.Evidence.Kind == CompilerEvidenceKind.Missing)
                    return (null, "coglatas.plan.required-validation-incomplete");
            }
        }
        var tasks = new SortedDictionary<string, TaskRow>(StringComparer.Ordinal); var edges = new SortedDictionary<string, EdgeRow>(StringComparer.Ordinal);
        foreach (var region in scope.Regions) foreach (var row in region.Binding.Declarations) {
            token.ThrowIfCancellationRequested(); Charge(ref remaining);
            if (row.Declaration is ConstraintPlanDeclaration { IsRequired: true } && !Required(CoreCompilerRules.Constraints)) return (null, "coglatas.plan.constraint-rule-required");
            if (row.Declaration is ProjectPlanDeclaration or ProjectTestPlanDeclaration or ConstraintPlanDeclaration) continue;
            if (row.Declaration is not TaskPlanDeclaration task) return (null, "coglatas.plan.declaration-unsupported");
            if (task.Duration.State != ValueState.Known || task.Duration.KnownValue is not ElapsedDuration { Ticks: >= 0 })
                return (null, "coglatas.plan.duration-unsupported");
            foreach (var port in task.Ports) {
                token.ThrowIfCancellationRequested(); Charge(ref remaining);
                if (port.Direction != PortDirection.Input || port.DefaultValue is null || !Ready(port.DefaultValue, ref remaining, token))
                    return (null, "coglatas.plan.input-unsupported");
            }
            var context = region.Binding.Source.Context; var node = new IrNodeId(context.Branch.TenantId, context.Branch.ProjectId, task.EntityId);
            var location = scope.ResolveNode(context, node) ?? throw new FormatException("Plan Task lacks its exact validated IR origin.");
            tasks.Add(NodeKey(context, task.EntityId), new(NodeKey(context, task.EntityId), context, task, node, location));
        }
        foreach (var region in scope.Regions) foreach (var row in region.Binding.Relations) {
            token.ThrowIfCancellationRequested(); Charge(ref remaining); var relation = row.Relation!;
            if (relation.Kind is RelationKinds.Constrains or RelationKinds.RelatedTo) continue;
            if (relation.Kind != RelationKinds.DependsOn || relation.Payload is not DependencyRelationPayload payload || payload.Mode != "coglatas.finish-to-start" ||
                payload.Lag.State != ValueState.Known || payload.Lag.KnownValue is not ElapsedDuration { Ticks: >= 0 } ||
                relation.Source.PortId is not null || relation.Target.PortId is not null) return (null, "coglatas.plan.relation-unsupported");
            if (!tasks.TryGetValue(NodeKey(relation.Source.Reference.Context, relation.Source.Reference.EntityId), out var dependent) ||
                !tasks.TryGetValue(NodeKey(relation.Target.Reference.Context, relation.Target.Reference.EntityId), out var prerequisite))
                return (null, "coglatas.plan.endpoint-unsupported");
            var key = region.Binding.Source.Context.Data.CanonicalText + "|" + relation.RelationId;
            edges.Add(key, new(relation, dependent, prerequisite, payload));
        }
        // This is a stable definition evaluation order, not a linear runtime schedule or call stack.
        var indegree = tasks.Keys.ToDictionary(k => k, _ => 0, StringComparer.Ordinal);
        var dependents = tasks.Keys.ToDictionary(k => k, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var edge in edges.Values) { token.ThrowIfCancellationRequested(); Charge(ref remaining); indegree[edge.Dependent.Key]++; dependents[edge.Prerequisite.Key].Add(edge.Dependent.Key); }
        var ready = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var key in tasks.Keys) { token.ThrowIfCancellationRequested(); Charge(ref remaining); if (indegree[key] == 0) ready.Add(key); }
        var order = new List<TaskRow>();
        while (ready.Count != 0) {
            token.ThrowIfCancellationRequested(); Charge(ref remaining); var key = ready.Min!; ready.Remove(key); order.Add(tasks[key]);
            foreach (var dependent in dependents[key]) { token.ThrowIfCancellationRequested(); Charge(ref remaining); if (--indegree[dependent] == 0) ready.Add(dependent); }
        }
        if (order.Count != tasks.Count) return (null, "coglatas.plan.dependency-cycle");
        return (new(scope, tasks.Values.ToArray(), edges.Values.ToArray(), order.ToArray(), initial, ref remaining, token), null);
        bool Required(string id) => input.Rules.Any(r => r.RuleId == id && r.Version == 1 && r.Required);
    }
    private static bool Ready(TypedProjectValue value, ref int remaining, CancellationToken token)
    {
        var pending = new Stack<TypedProjectValue>(); pending.Push(value);
        while (pending.TryPop(out var next)) {
            token.ThrowIfCancellationRequested(); Charge(ref remaining);
            if (next.State == ValueState.Unknown) return false;
            if (next.KnownValue is ProjectCollectionValue collection) {
                foreach (var item in collection.Items) pending.Push(item);
                foreach (var entry in collection.Entries) { pending.Push(entry.Key); pending.Push(entry.Value); }
            } else if (next.KnownValue is ProjectStructureValue structure) {
                foreach (var field in structure.Fields.Values) pending.Push(field);
            } else if (next.KnownValue is OptionalProjectValue { Present: true, Value: { } item }) pending.Push(item);
        }
        return true;
    }
    private static string NodeKey(SourceRevisionContext context, EntityId entity) => context.Data.CanonicalText + "|" + entity;
    private static void Charge(ref int remaining) { if (--remaining < 0) throw new FormatException("Plan definition exhausted the root logical budget."); }
}
