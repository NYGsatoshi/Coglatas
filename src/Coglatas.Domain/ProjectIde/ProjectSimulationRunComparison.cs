using System.Globalization;
using System.Text.Json;
namespace Coglatas.Domain.ProjectIde;

public enum SimulationRunComparisonState { Complete, Incomplete, Refused }

/// <summary>Local candidate comparison of admitted immutable run recordings. No scheduling, authority or outcome assertion.</summary>
public sealed class ProjectSimulationRunComparison
{
    public const string Format = "coglatas.local-simulation-comparison/1";
    public const int MaximumCapturedInputsPerRun = 256;
    public SimulationRunComparisonState State { get; }
    public string Reason { get; }
    public ProjectSimulationRun? Before { get; }
    public ProjectSimulationRun? After { get; }
    public bool? SameInput { get; }
    public bool? SameLimits { get; }
    public long? ObservedElapsedDelta { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }

    private ProjectSimulationRunComparison(SimulationRunComparisonState state, string reason,
        ProjectSimulationRun? before = null, ProjectSimulationRun? after = null)
    {
        State = state; Reason = reason; Before = before; After = after;
        if (before is not null && after is not null) {
            SameInput = before.Manifest.Plan.Input.InputDigest.Equals(after.Manifest.Plan.Input.InputDigest);
            SameLimits = before.Manifest.Limits == after.Manifest.Limits;
            if (state == SimulationRunComparisonState.Complete) ObservedElapsedDelta = Elapsed(after) - Elapsed(before);
        }
        Data = Json(new { format = Format, state = state.ToString(), reason,
            before = before is null ? null : Summary(before), after = after is null ? null : Summary(after),
            sameInput = SameInput, sameLimits = SameLimits, observedElapsedDelta = ObservedElapsedDelta?.ToString(CultureInfo.InvariantCulture),
            verdict = "NotRun", evidenceKind = "coglatas.simulation", authority = "NotEvaluated" });
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }

    public static ProjectSimulationRunComparison Compare(ProjectSimulationRun before, ContentDigest expectedBefore,
        ProjectSimulationRun after, ContentDigest expectedAfter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(before); ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(expectedBefore); ArgumentNullException.ThrowIfNull(expectedAfter);
        cancellationToken.ThrowIfCancellationRequested();
        if (!before.Digest.Equals(expectedBefore) || !after.Digest.Equals(expectedAfter))
            return new(SimulationRunComparisonState.Refused, "coglatas.comparison.wrong-run");
        var beforeBranch = SourceJson.Parse(before.Manifest.Plan.Input.Source.Context.Data.Value.GetProperty("branch").GetRawText());
        var afterBranch = SourceJson.Parse(after.Manifest.Plan.Input.Source.Context.Data.Value.GetProperty("branch").GetRawText());
        if (!beforeBranch.Equals(afterBranch)) return new(SimulationRunComparisonState.Refused, "coglatas.comparison.different-root-branch");
        if (!before.Manifest.Engine.Data.Equals(ProjectSimulation.Engine.Data) || !after.Manifest.Engine.Data.Equals(ProjectSimulation.Engine.Data))
            return new(SimulationRunComparisonState.Refused, "coglatas.comparison.engine-unsupported");
        if (before.Manifest.RunId == after.Manifest.RunId && !before.Digest.Equals(after.Digest))
            return new(SimulationRunComparisonState.Refused, "coglatas.comparison.run-identity-conflict");
        if (before.Manifest.Plan.Input.Settings.Value.GetProperty("authorizedExternalSnapshots").GetArrayLength() > MaximumCapturedInputsPerRun ||
            after.Manifest.Plan.Input.Settings.Value.GetProperty("authorizedExternalSnapshots").GetArrayLength() > MaximumCapturedInputsPerRun ||
            before.Manifest.Plan.Scope.Regions.Count > MaximumCapturedInputsPerRun + 1 || after.Manifest.Plan.Scope.Regions.Count > MaximumCapturedInputsPerRun + 1)
            return new(SimulationRunComparisonState.Refused, "coglatas.comparison.capture-bound");
        if (!ConsistentCaptures(before.Manifest.Plan, after.Manifest.Plan, cancellationToken))
            return new(SimulationRunComparisonState.Refused, "coglatas.comparison.capture-identity-conflict");
        var complete = before.Execution == CompilerExecutionState.Completed && after.Execution == CompilerExecutionState.Completed &&
            before.Coverage == CompilerCoverage.Complete && after.Coverage == CompilerCoverage.Complete &&
            before.Freshness == CompilerFreshness.Current && after.Freshness == CompilerFreshness.Current;
        cancellationToken.ThrowIfCancellationRequested();
        return new(complete ? SimulationRunComparisonState.Complete : SimulationRunComparisonState.Incomplete,
            complete ? "coglatas.comparison.complete-recordings" : "coglatas.comparison.incomplete-or-stale-recording", before, after);
    }

    private static bool ConsistentCaptures(CompilerExecutionPlan before, CompilerExecutionPlan after, CancellationToken token)
    {
        var contexts = new Dictionary<string, string>(StringComparer.Ordinal);
        var versions = new Dictionary<(string Id, string Version), (string Context, string Digest)>();
        bool Retain(string context, string digest) {
            if (contexts.TryGetValue(context, out var previous)) return previous == digest;
            contexts.Add(context, digest); return true;
        }
        foreach (var plan in new[] { before, after }) {
            token.ThrowIfCancellationRequested();
            var input = plan.Input;
            if (!Retain(input.Source.Context.Data.CanonicalText, input.Source.Digest.Data.CanonicalText)) return false;
            foreach (var region in plan.Scope.Regions) {
                token.ThrowIfCancellationRequested();
                if (!Retain(region.Binding.Source.Context.Data.CanonicalText, region.Binding.Source.Digest.Data.CanonicalText)) return false;
            }
            foreach (var pin in input.Settings.Value.GetProperty("authorizedExternalSnapshots").EnumerateArray()) {
                token.ThrowIfCancellationRequested();
                var context = SourceRevisionContext.Parse(SourceJson.Parse(pin.GetProperty("context").GetRawText())).Data.CanonicalText;
                var digest = SourceJson.Parse(pin.GetProperty("digest").GetRawText()).CanonicalText;
                if (!Retain(context, digest)) return false;
                var key = (ExternalSnapshotId.Parse(pin.GetProperty("snapshotId").GetString()!).ToString(), pin.GetProperty("version").GetString()!);
                if (versions.TryGetValue(key, out var previous) && previous != (context, digest)) return false;
                versions[key] = (context, digest);
            }
        }
        return true;
    }
    private static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    private static long Elapsed(ProjectSimulationRun run) => checked(run.VirtualTicks - run.Manifest.StartTicks);
    private static object Summary(ProjectSimulationRun run) => new {
        runId = run.Manifest.RunId.ToString("D"), runDigest = run.Digest.Data.Value, manifestDigest = run.Manifest.Digest.Data.Value,
        inputDigest = run.Manifest.Plan.Input.InputDigest.Data.Value, sourceDigest = run.Manifest.Plan.Input.Source.Digest.Data.Value,
        context = run.Manifest.Plan.Input.Source.Context.Data.Value, execution = run.Execution.ToString(), coverage = run.Coverage.ToString(),
        freshness = run.Freshness.ToString(), reason = run.Reason, startTicks = run.Manifest.StartTicks.ToString(CultureInfo.InvariantCulture),
        virtualTicks = run.VirtualTicks.ToString(CultureInfo.InvariantCulture), observedElapsedTicks = Elapsed(run).ToString(CultureInfo.InvariantCulture),
        startedTasks = run.StartedTasks, completedTasks = run.CompletedTasks, eventCount = run.Events.Count };
}
