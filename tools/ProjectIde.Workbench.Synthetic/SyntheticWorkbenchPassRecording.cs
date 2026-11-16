using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

public sealed partial class SyntheticWorkbenchViewModel
{
    private PassObservationCompilerHostRequest? _desiredPassRequest;
    private LocalPassObservationCompilerRecordingManifest? _desiredPassManifest;
    private PassObservationCompilerRecordedInvocation? _acceptedPassInvocation;
    private SourceJson? _passReceiptMetadata;
    private SourceJson? _passRecordMetadata;
    private long _passReadGeneration;
    private long _passSnapshotGeneration;
    private ProjectCompilerPassObservationSnapshot? _recordedPasses;
    private CoreCompilation? _passCompilation;
    private bool CurrentPassSource => CurrentRootSource && _desiredPassRequest is { } request &&
        ReferenceEquals(_desired, request.Core) && _desiredPassManifest is { } manifest &&
        _acceptedPassInvocation is { } actual && actual.IsFor(request, manifest) &&
        ReferenceEquals(actual.Response.Execution?.Compilation, _compilation);
    public SourceJson? PassReceiptMetadata => CurrentPassSource ? _passReceiptMetadata : null;
    public SourceJson? PassRecordMetadata => CurrentPassSource ? _passRecordMetadata : null;
    public ProjectCompilerPassObservationSnapshot? RecordedPasses => CurrentPassSource &&
        ReferenceEquals(_passCompilation, _compilation) ? _recordedPasses : null;

    /// <summary>Declare the exact actual local run before notifications; content equality cannot select an intended run.</summary>
    public void BeginPassSynthetic(LocalPassObservationCompilerRecordingManifest manifest, PassObservationCompilerHostRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(manifest); ArgumentNullException.ThrowIfNull(request);
        if (!manifest.IsFor(request)) throw new ArgumentException("Manifest differs from the exact declared pass request.");
        BeginSyntheticContext(request.Core, null, null, passRequest: request, passManifest: manifest);
    }

    /// <summary>Admit only the actual owned pair for the predeclared intended run; no current runtime authorization is implied.</summary>
    public bool TryAcceptPass(PassObservationCompilerRecordedInvocation actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        if (_disposed || State != "Waiting" || _desiredPassRequest is not { } request || _desiredPassManifest is not { } manifest ||
            !actual.IsFor(request, manifest)) return false;
        var response = actual.Response;
        if (response.Status != CompilerHostStatus.Completed)
        { ClearResult(); State = response.Status.ToString(); Changed(); return false; }
        if (response.Execution is not { } execution || !execution.Observation.IsOwnedBy(execution) ||
            !ReferenceEquals(execution.Observation.Policy, request.ObservationPolicy) ||
            !execution.Compilation.Input.Data.Equals(execution.Compilation.Result.Input.Data) ||
            !execution.Compilation.Input.Source.Data.Equals(Source?.Data)) return false;
        var compilation = execution.Compilation;
        var inspection = compilation.Model is { } model ? new CompilerSemanticInspection(model) : null;
        var lifecycle = _lifecycle;
        ClearResult(); _compilation = compilation; _acceptedPassInvocation = actual;
        Result = compilation.Result; Inspection = inspection; State = "Ready"; Changed();
        return !_disposed && lifecycle == _lifecycle && ReferenceEquals(Result, compilation.Result) && ReferenceEquals(_acceptedPassInvocation, actual);
    }

    /// <summary>Explicit metadata after actual root Source admission; no body map or authority grant is exposed.</summary>
    public bool ReadPassRecordingMetadata()
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ClearPassDisplay();
        var generation = _passReadGeneration; var root = _rootSourceGeneration;
        var request = _desiredPassRequest; var actual = _acceptedPassInvocation;
        bool Current() => !_disposed && generation == _passReadGeneration && root == _rootSourceGeneration && CurrentPassSource &&
            ReferenceEquals(request, _desiredPassRequest) && ReferenceEquals(actual, _acceptedPassInvocation);
        Changed(); if (!Current()) return false;
        _passReceiptMetadata = actual!.Receipt.Data; _passRecordMetadata = actual.Receipt.ObservationMetadata; Changed();
        return Current() && ReferenceEquals(PassReceiptMetadata, actual.Receipt.Data);
    }

    /// <summary>Explicit original initial-root pass frames for the current Source/run/result; no diagnostic selection or replay.</summary>
    public bool ReadRecordedPasses(ContentDigest expectedResult, CompilerPassObservationSnapshotMode mode = CompilerPassObservationSnapshotMode.Manifest,
        int maximumBodyBytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(expectedResult);
        ClearPassSnapshot(); var generation = _passSnapshotGeneration; var root = _rootSourceGeneration;
        var request = _desiredPassRequest; var actual = _acceptedPassInvocation; var compilation = _compilation;
        bool Current() => !_disposed && generation == _passSnapshotGeneration && root == _rootSourceGeneration &&
            CurrentPassSource && ReferenceEquals(request, _desiredPassRequest) && ReferenceEquals(actual, _acceptedPassInvocation) &&
            ReferenceEquals(compilation, _compilation) && compilation!.Result.ResultDigest.Equals(expectedResult);
        Changed(); if (!Current()) return false;
        var execution = actual!.Response.Execution!;
        var snapshot = ProjectCompilerPassObservationSnapshot.Read(execution, compilation!, execution.Observation, compilation!.Input.InputDigest, expectedResult, mode, maximumBodyBytes, token);
        if (!Current()) return false;
        _recordedPasses = snapshot; _passCompilation = compilation; Changed();
        return Current() && ReferenceEquals(RecordedPasses, snapshot) && snapshot.State is not (CompilerPassObservationSnapshotState.RefusedInvocation or
            CompilerPassObservationSnapshotState.RefusedInputBinding or CompilerPassObservationSnapshotState.RefusedWrongResult);
    }

    private void ClearPassSnapshot()
    { _passSnapshotGeneration++; _recordedPasses = null; _passCompilation = null; ClearPassBoundarySnapshot(); }
    private void ClearPassDisplay()
    { _passReadGeneration++; _passReceiptMetadata = null; _passRecordMetadata = null; ClearPassSnapshot(); }
    private void ClearPassContext()
    { _desiredPassRequest = null; _desiredPassManifest = null; _acceptedPassInvocation = null; ClearPassDisplay(); }
}
