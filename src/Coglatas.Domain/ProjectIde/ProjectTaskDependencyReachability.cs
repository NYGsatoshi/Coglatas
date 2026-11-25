namespace Coglatas.Domain.ProjectIde;

public enum TaskDependencyReachabilityState { Complete, DepthLimit, OperationLimit, StaleInput, Unavailable }

/// <summary>A reachable Task and one shortest path in prerequisite-to-dependent order.</summary>
public sealed class TaskDependencyReachabilityRow
{
    public ProjectLocation Task { get; }
    public int Depth => Relations.Count;
    public IReadOnlyList<ProjectLocation> Relations { get; }

    internal TaskDependencyReachabilityRow(ProjectLocation task, IEnumerable<ProjectLocation> relations)
    {
        Task = task;
        Relations = new ImmutableListView<ProjectLocation>(relations);
    }
}

/// <summary>Structural reachability within an evaluated, fixed compiler graph.</summary>
public sealed class ProjectTaskDependencyReachability
{
    public const int MaximumDepth = 32;
    public TaskDependencyReachabilityState State { get; }
    public ContentDigest? InputDigest { get; }
    public ContentDigest? GraphDigest { get; }
    public ProjectLocation? Anchor { get; }
    public IReadOnlyList<TaskDependencyReachabilityRow> Tasks { get; }
    public int LogicalOperationsConsumed { get; }

    private readonly record struct TaskKey(SourceRevisionContext Context, EntityId EntityId);
    private sealed record Edge(TaskKey Dependent, ProjectLocation Relation);

    private ProjectTaskDependencyReachability(TaskDependencyReachabilityState state, ContentDigest? inputDigest,
        ContentDigest? graphDigest, ProjectLocation? anchor, IEnumerable<TaskDependencyReachabilityRow> tasks, int consumed)
    {
        State = state;
        InputDigest = inputDigest;
        GraphDigest = graphDigest;
        Anchor = anchor;
        Tasks = new ImmutableListView<TaskDependencyReachabilityRow>(tasks);
        LogicalOperationsConsumed = consumed;
    }

    public static ProjectTaskDependencyReachability Read(CoreCompilation compilation, ContentDigest expectedInput,
        ProjectLocation changedTask, int maximumDepth = MaximumDepth, int? maximumOperations = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(expectedInput);
        ArgumentNullException.ThrowIfNull(changedTask);
        cancellationToken.ThrowIfCancellationRequested();
        var availableOperations = compilation.Input.ExecutionProfile.MaximumOperations;
        var maximum = maximumOperations ?? availableOperations;
        if (maximum < 1 || maximum > availableOperations) throw new ArgumentOutOfRangeException(nameof(maximumOperations));
        if (maximumDepth is < 0 or > MaximumDepth) throw new ArgumentOutOfRangeException(nameof(maximumDepth));

        if (!compilation.Input.InputDigest.Equals(expectedInput))
            return Refused(TaskDependencyReachabilityState.StaleInput);
        if (compilation.Result.Execution != CompilerExecutionState.Completed ||
            compilation.Dependencies?.Graph is not { StructuralValidated: true } graph ||
            changedTask.EntityId is not { } entity)
            return Refused(TaskDependencyReachabilityState.Unavailable);

        var remaining = maximum;
        var tasks = new Dictionary<TaskKey, ProjectLocation>();
        var affected = new List<TaskDependencyReachabilityRow>();
        ProjectLocation? anchor = null;

        bool Charge(int operations = 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operations > remaining) return false;
            remaining -= operations;
            return true;
        }
        ProjectTaskDependencyReachability Finish(TaskDependencyReachabilityState state)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(state, graph.Binding.Input.InputDigest, graph.Digest, anchor, affected, maximum - remaining);
        }

        foreach (var region in graph.Binding.Regions)
        {
            if (!Charge()) return Finish(TaskDependencyReachabilityState.OperationLimit);
            foreach (var row in region.Declarations)
            {
                if (!Charge()) return Finish(TaskDependencyReachabilityState.OperationLimit);
                if (row.Declaration is not TaskPlanDeclaration task) continue;
                var key = new TaskKey(region.Source.Context, task.EntityId);
                var location = ProjectLocation.Create(task.DocumentId, key.Context, entityId: key.EntityId);
                if (!tasks.TryAdd(key, location)) return Refused(TaskDependencyReachabilityState.Unavailable);
            }
        }

        var root = new TaskKey(changedTask.Context, entity);
        if (!tasks.TryGetValue(root, out var found) || !found.Equals(changedTask))
            return Refused(TaskDependencyReachabilityState.Unavailable);
        anchor = found;

        var dependents = new Dictionary<TaskKey, SortedDictionary<string, Edge>>();
        foreach (var region in graph.Binding.Regions)
        {
            foreach (var row in region.Relations)
            {
                if (!Charge()) return Finish(TaskDependencyReachabilityState.OperationLimit);
                if (row.Relation is not { Kind: RelationKinds.DependsOn } relation) continue;
                var prerequisite = new TaskKey(relation.Target.Reference.Context, relation.Target.Reference.EntityId);
                var dependent = new TaskKey(relation.Source.Reference.Context, relation.Source.Reference.EntityId);
                if (!tasks.ContainsKey(prerequisite) || !tasks.ContainsKey(dependent))
                    return Refused(TaskDependencyReachabilityState.Unavailable);
                if (!dependents.TryGetValue(prerequisite, out var edges))
                    dependents.Add(prerequisite, edges = new(StringComparer.Ordinal));
                edges.Add(relation.Location.Data.CanonicalText, new(dependent, relation.Location));
            }
        }

        var visited = new HashSet<TaskKey> { root };
        var queue = new Queue<(TaskKey Task, IReadOnlyList<ProjectLocation> Path)>();
        queue.Enqueue((root, []));
        var depthLimited = false;
        while (queue.TryDequeue(out var current))
        {
            if (!Charge()) return Finish(TaskDependencyReachabilityState.OperationLimit);
            if (!dependents.TryGetValue(current.Task, out var edges)) continue;
            foreach (var edge in edges.Values)
            {
                if (!Charge()) return Finish(TaskDependencyReachabilityState.OperationLimit);
                if (visited.Contains(edge.Dependent)) continue;
                if (current.Path.Count == maximumDepth)
                {
                    depthLimited = true;
                    continue;
                }
                if (!Charge(current.Path.Count + 1)) return Finish(TaskDependencyReachabilityState.OperationLimit);
                visited.Add(edge.Dependent);
                var row = new TaskDependencyReachabilityRow(tasks[edge.Dependent], current.Path.Append(edge.Relation));
                affected.Add(row);
                queue.Enqueue((edge.Dependent, row.Relations));
            }
        }
        return Finish(depthLimited ? TaskDependencyReachabilityState.DepthLimit : TaskDependencyReachabilityState.Complete);
    }

    private static ProjectTaskDependencyReachability Refused(TaskDependencyReachabilityState state) =>
        new(state, null, null, null, [], 0);
}
