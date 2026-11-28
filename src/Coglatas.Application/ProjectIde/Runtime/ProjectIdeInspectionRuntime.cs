using System.Globalization;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Runtime;

public sealed record ProjectIdeInspectionReference(SecuritySourceIdentitySnapshot Source, ContentDigest SourceDigest,
    Guid RecordingId, ContentDigest RecordingDigest);

public sealed record ProjectIdeModelProfileView(SimulationTraceProfileState State, CompilerExecutionState Execution,
    CompilerCoverage Coverage, long ElapsedVirtualTicks, long BusyVirtualTicks, long UnattributedIdleVirtualTicks,
    string TaskVirtualTicks, int PeakActiveTasks, int OpenTasks)
{
    public string InputKind => "SimulatedModel";
}

public sealed record CompilerOutputCaptureKey(Guid CaptureId, ProjectIdeInspectionReference Recording);

public sealed record CompilerOutputCaptureView(CompilerOutputSnapshotMode Mode, CompilerOutputSnapshotState State,
    CompilerExecutionState Execution, CompilerCoverage Coverage, CompilerVerdict Verdict, bool EvidenceComplete,
    bool SemanticModelWasCaptured, bool IrWasCaptured, bool SourceMapWasCaptured);

public sealed record CompilerOutputCapture(CompilerOutputCaptureKey Key, int MaximumBodyBytes, CompilerOutputCaptureView View);

/// <summary>Returns only the original server-owned recording, after applying its storage access policy.</summary>
public interface IProjectIdeInspectionRecordings
{
    Task<ProjectSimulationRun?> FindSimulationAsync(ProjectIdeViewerContext viewer, ProjectIdeInspectionReference reference,
        CancellationToken cancellationToken);

    Task<LocalCompilerRecordedInvocation?> FindCompilerAsync(ProjectIdeViewerContext viewer, ProjectIdeInspectionReference reference,
        CancellationToken cancellationToken);
}

/// <summary>Resolves current retention and disclosure policy for the requested recording and operation.</summary>
public interface IProjectIdeInspectionPolicy
{
    Task<bool> AllowsAsync(ProjectIdeViewerContext viewer, SecurityOperationRef operation, ProjectIdeInspectionReference reference,
        CancellationToken cancellationToken);
}

public interface ICompilerOutputCaptureStore
{
    Task<CompilerOutputCapture?> FindAsync(CompilerOutputCaptureKey key, CancellationToken cancellationToken);

    /// <summary>Holds the exact live capture stable while checking current disclosure authority.</summary>
    Task<CompilerOutputCapture?> ReadAsync(CompilerOutputCaptureKey key, Func<CancellationToken, Task<bool>> reauthorize,
        CancellationToken cancellationToken);

    /// <summary>Commits an immutable redacted capture or returns its exact replay. Reauthorizes before committing.</summary>
    Task<CompilerOutputCapture?> RetainAsync(CompilerOutputCapture capture, Func<CancellationToken, Task<bool>> reauthorize,
        CancellationToken cancellationToken);

    /// <summary>Deletes the exact capture and prevents idempotent replay from restoring it. Reauthorizes before committing.</summary>
    Task<bool> DeleteAsync(CompilerOutputCapture capture, Func<CancellationToken, Task<bool>> reauthorize,
        CancellationToken cancellationToken);
}

/// <summary>Development Application boundary for redacted inspection of admitted recordings.</summary>
public sealed class ProjectIdeInspectionRuntime(ProjectIdeOperationAccess access, IProjectIdeInspectionPolicy policy,
    IProjectIdeInspectionRecordings recordings, ICompilerRecordingStore evidence, ICompilerOutputCaptureStore captures)
{
    public static SecurityOperationRef ProfileQuery { get; } = new("coglatas.projectide.model-profile.query");
    public static SecurityOperationRef ProfileExport { get; } = new("coglatas.projectide.model-profile.export");
    public static SecurityOperationRef OutputCapture { get; } = new("coglatas.projectide.compiler-output.capture");
    public static SecurityOperationRef OutputQuery { get; } = new("coglatas.projectide.compiler-output.query");
    public static SecurityOperationRef OutputExport { get; } = new("coglatas.projectide.compiler-output.export");
    public static SecurityOperationRef OutputDelete { get; } = new("coglatas.projectide.compiler-output.delete");

    public Task<ProjectIdeModelProfileView?> QueryProfileAsync(ProjectIdeInspectionReference reference, int maximumEvents,
        CancellationToken cancellationToken = default) => ReadProfileAsync(reference, maximumEvents, ProfileQuery, cancellationToken);

    public Task<ProjectIdeModelProfileView?> ExportProfileAsync(ProjectIdeInspectionReference reference, int maximumEvents,
        CancellationToken cancellationToken = default) => ReadProfileAsync(reference, maximumEvents, ProfileExport, cancellationToken);

    public Task<CompilerOutputCaptureView?> QueryOutputAsync(CompilerOutputCaptureKey key, CancellationToken cancellationToken = default) =>
        ReadOutputAsync(key, OutputQuery, cancellationToken);

    public Task<CompilerOutputCaptureView?> ExportOutputAsync(CompilerOutputCaptureKey key, CancellationToken cancellationToken = default) =>
        ReadOutputAsync(key, OutputExport, cancellationToken);

    public async Task<CompilerOutputCaptureView?> CaptureOutputAsync(CompilerOutputCaptureKey key, int maximumBodyBytes,
        CancellationToken cancellationToken = default)
    {
        if (key.CaptureId == Guid.Empty || maximumBodyBytes is < 1 or > ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes ||
            await AdmitAsync(key.Recording, OutputCapture, cancellationToken) is not { } viewer)
            return null;

        var reference = key.Recording;
        var recorded = await evidence.FindAsync(reference.Source, reference.SourceDigest.Value, reference.RecordingId, cancellationToken);
        if (recorded is not { Result: not null, CaptureDigest: not null } || recorded.Source != reference.Source ||
            recorded.SourceDigest != reference.SourceDigest.Value || recorded.RunId != reference.RecordingId ||
            recorded.ReceiptDigest != reference.RecordingDigest.Value ||
            !await IsAllowedAsync(viewer, OutputCapture, reference, cancellationToken))
            return null;

        var invocation = await recordings.FindCompilerAsync(viewer, reference, cancellationToken);
        if (invocation is null || CompilerRecordingEvidence.FromInvocation(invocation) != recorded ||
            invocation.Response.Execution is not { } execution ||
            execution.Materializations.Value.GetProperty("snapshots").GetArrayLength() != 0 ||
            invocation.Receipt.Snapshot is not { } snapshot || snapshot.RetainedBodyBytes > maximumBodyBytes)
            return null;

        var view = new CompilerOutputCaptureView(snapshot.Mode, snapshot.State, recorded.Result.Execution,
            recorded.Result.Coverage, recorded.Result.Verdict, recorded.Result.EvidenceComplete,
            snapshot.Bodies.ContainsKey("coglatas.semantic-model"), snapshot.Bodies.ContainsKey("coglatas.ir"),
            snapshot.Bodies.ContainsKey("coglatas.source-map"));
        var capture = new CompilerOutputCapture(key, maximumBodyBytes, view);
        Task<bool> Allowed(CancellationToken token) => IsAllowedAsync(viewer, OutputCapture, reference, token);
        if (!await Allowed(cancellationToken))
            return null;

        var stored = await captures.RetainAsync(capture, Allowed, cancellationToken);
        return stored is not null && stored == capture && await Allowed(cancellationToken) ? stored.View : null;
    }

    public async Task<bool> DeleteOutputAsync(CompilerOutputCaptureKey key, CancellationToken cancellationToken = default)
    {
        if (key.CaptureId == Guid.Empty || await AdmitAsync(key.Recording, OutputDelete, cancellationToken) is not { } viewer)
            return false;

        var capture = await captures.FindAsync(key, cancellationToken);
        Task<bool> Allowed(CancellationToken token) => IsAllowedAsync(viewer, OutputDelete, key.Recording, token);
        return capture is not null && capture.Key == key && await Allowed(cancellationToken) && await captures.DeleteAsync(capture, Allowed, cancellationToken) &&
            await Allowed(cancellationToken);
    }

    private async Task<ProjectIdeModelProfileView?> ReadProfileAsync(ProjectIdeInspectionReference reference, int maximumEvents,
        SecurityOperationRef operation, CancellationToken cancellationToken)
    {
        if (maximumEvents is < 1 or > ProjectSimulationTraceProfile.MaximumRecordedEvents ||
            await AdmitAsync(reference, operation, cancellationToken) is not { } viewer)
            return null;

        var run = await recordings.FindSimulationAsync(viewer, reference, cancellationToken);
        if (run is null || run.Manifest.RunId != reference.RecordingId || !run.Digest.Equals(reference.RecordingDigest))
            return null;

        var plan = run.Manifest.Plan;
        var source = plan.Input.Source;
        if (!source.Digest.Equals(reference.SourceDigest) || SecuritySourceIdentitySnapshot.Capture(source.Context, plan.Input.InputDigest) != reference.Source ||
            plan.Scope.Regions.Any(region => !source.Context.Equals(region.Binding.Source.Context) || !source.Digest.Equals(region.Binding.Source.Digest)))
            return null;

        var profile = ProjectSimulationTraceProfile.Read(run, reference.RecordingDigest, maximumEvents, cancellationToken);
        if (profile.State is SimulationTraceProfileState.Refused || !await IsAllowedAsync(viewer, operation, reference, cancellationToken))
            return null;

        return new(profile.State, run.Execution, run.Coverage, profile.RecordedElapsedTicks!.Value, profile.RecordedBusyTicks!.Value,
            profile.UnattributedIdleTicks!.Value, profile.RecordedTaskTicks!.Value.ToString(CultureInfo.InvariantCulture),
            profile.PeakActiveTasks!.Value, profile.OpenTasks!.Value);
    }

    private async Task<CompilerOutputCaptureView?> ReadOutputAsync(CompilerOutputCaptureKey key, SecurityOperationRef operation,
        CancellationToken cancellationToken)
    {
        if (key.CaptureId == Guid.Empty || await AdmitAsync(key.Recording, operation, cancellationToken) is not { } viewer)
            return null;

        if (!await IsAllowedAsync(viewer, operation, key.Recording, cancellationToken))
            return null;

        Task<bool> Allowed(CancellationToken token) => IsAllowedAsync(viewer, operation, key.Recording, token);
        var capture = await captures.ReadAsync(key, Allowed, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return capture?.Key == key && access.CaptureViewer() == viewer ? capture.View : null;
    }

    private async Task<ProjectIdeViewerContext?> AdmitAsync(ProjectIdeInspectionReference reference, SecurityOperationRef operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return reference.RecordingId != Guid.Empty && access.CaptureViewer() is { } viewer &&
            await IsAllowedAsync(viewer, operation, reference, cancellationToken) ? viewer : null;
    }

    private async Task<bool> IsAllowedAsync(ProjectIdeViewerContext viewer, SecurityOperationRef operation,
        ProjectIdeInspectionReference reference, CancellationToken cancellationToken) =>
        await access.IsAllowedAsync(viewer, operation, reference.Source, reference.SourceDigest.Value, cancellationToken) &&
        await policy.AllowsAsync(viewer, operation, reference, cancellationToken) &&
        await access.IsAllowedAsync(viewer, operation, reference.Source, reference.SourceDigest.Value, cancellationToken);
}
