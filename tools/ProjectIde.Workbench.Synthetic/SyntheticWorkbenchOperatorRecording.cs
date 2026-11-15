using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

public sealed partial class SyntheticWorkbenchViewModel
{
    private PredicateObservationCompilerHostRequest? _desiredOperatorRequest;
    private LocalPredicateObservationCompilerRecordingManifest? _desiredOperatorManifest;
    private PredicateObservationCompilerRecordedInvocation? _acceptedOperatorInvocation;
    private SourceJson? _operatorReceiptMetadata;
    private SourceJson? _operatorRecordMetadata;
    private long _operatorReadGeneration;
    private long _operatorSnapshotGeneration;
    private long _operatorSnapshotSelection;
    private ProjectCompilerPredicateObservationSnapshot? _recordedOperators;
    private CompilerDiagnostic? _operatorDiagnostic;
    private CoreCompilation? _operatorCompilation;
    private bool CurrentOperatorSource => CurrentRootSource && _desiredOperatorRequest is { } request &&
        ReferenceEquals(_desired, request.Core) && _desiredOperatorManifest is { } manifest &&
        _acceptedOperatorInvocation is { } actual && actual.IsFor(request, manifest) &&
        ReferenceEquals(actual.Response.Execution?.Compilation, _compilation);
    public SourceJson? OperatorReceiptMetadata => CurrentOperatorSource ? _operatorReceiptMetadata : null;
    public SourceJson? OperatorRecordMetadata => CurrentOperatorSource ? _operatorRecordMetadata : null;
    public ProjectCompilerPredicateObservationSnapshot? RecordedOperators => CurrentOperatorSource &&
        ReferenceEquals(_operatorCompilation, _compilation) && ReferenceEquals(_operatorDiagnostic, SelectedDiagnostic) &&
        _operatorSnapshotSelection == _selectionGeneration ? _recordedOperators : null;

    /// <summary>Declare the exact actual local run before notifications; content equality cannot select an intended run.</summary>
    public void BeginOperatorSynthetic(LocalPredicateObservationCompilerRecordingManifest manifest, PredicateObservationCompilerHostRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(manifest); ArgumentNullException.ThrowIfNull(request);
        if (!manifest.IsFor(request)) throw new ArgumentException("Manifest differs from the exact declared operator request.");
        BeginSyntheticContext(request.Core, null, null, request, manifest);
    }

    /// <summary>Admit only the actual owned pair for the predeclared intended run; no current runtime authorization is implied.</summary>
    public bool TryAcceptOperator(PredicateObservationCompilerRecordedInvocation actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        if (_disposed || State != "Waiting" || _desiredOperatorRequest is not { } request || _desiredOperatorManifest is not { } manifest ||
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
        ClearResult(); _compilation = compilation; _acceptedOperatorInvocation = actual;
        Result = compilation.Result; Inspection = inspection; State = "Ready"; Changed();
        return !_disposed && lifecycle == _lifecycle && ReferenceEquals(Result, compilation.Result) && ReferenceEquals(_acceptedOperatorInvocation, actual);
    }

    /// <summary>Explicit metadata after actual root Source admission; no body map or authority grant is exposed.</summary>
    public bool ReadOperatorRecordingMetadata()
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ClearOperatorDisplay();
        var generation = _operatorReadGeneration; var root = _rootSourceGeneration;
        var request = _desiredOperatorRequest; var actual = _acceptedOperatorInvocation;
        bool Current() => !_disposed && generation == _operatorReadGeneration && root == _rootSourceGeneration && CurrentOperatorSource &&
            ReferenceEquals(request, _desiredOperatorRequest) && ReferenceEquals(actual, _acceptedOperatorInvocation);
        Changed(); if (!Current()) return false;
        _operatorReceiptMetadata = actual!.Receipt.Data; _operatorRecordMetadata = actual.Receipt.ObservationMetadata; Changed();
        return Current() && ReferenceEquals(OperatorReceiptMetadata, actual.Receipt.Data);
    }

    /// <summary>Explicit original frames for the exact current Source/run/result/diagnostic; no replay or earlier-state reconstruction.</summary>
    public bool ReadRecordedOperators(ContentDigest expectedResult, CompilerPredicateObservationSnapshotMode mode = CompilerPredicateObservationSnapshotMode.Manifest,
        int maximumBodyBytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(expectedResult);
        ClearOperatorSnapshot(); var generation = _operatorSnapshotGeneration; var root = _rootSourceGeneration; var selection = _selectionGeneration;
        var request = _desiredOperatorRequest; var actual = _acceptedOperatorInvocation; var diagnostic = SelectedDiagnostic; var compilation = _compilation;
        bool Current() => !_disposed && generation == _operatorSnapshotGeneration && root == _rootSourceGeneration && selection == _selectionGeneration &&
            CurrentOperatorSource && diagnostic is not null && ReferenceEquals(request, _desiredOperatorRequest) && ReferenceEquals(actual, _acceptedOperatorInvocation) &&
            ReferenceEquals(compilation, _compilation) && ReferenceEquals(diagnostic, SelectedDiagnostic) && compilation!.Result.ResultDigest.Equals(expectedResult);
        Changed(); if (!Current()) return false;
        var execution = actual!.Response.Execution!;
        var snapshot = ProjectCompilerPredicateObservationSnapshot.Read(execution, compilation!, execution.Observation, diagnostic!, mode, maximumBodyBytes, token);
        if (!Current()) return false;
        _recordedOperators = snapshot; _operatorDiagnostic = diagnostic; _operatorCompilation = compilation; _operatorSnapshotSelection = selection; Changed();
        return Current() && ReferenceEquals(RecordedOperators, snapshot) && snapshot.State != CompilerPredicateObservationSnapshotState.RefusedInvocation;
    }

    private void ClearOperatorSnapshot()
    { _operatorSnapshotGeneration++; _recordedOperators = null; _operatorDiagnostic = null; _operatorCompilation = null; }
    private void ClearOperatorDisplay()
    { _operatorReadGeneration++; _operatorReceiptMetadata = null; _operatorRecordMetadata = null; ClearOperatorSnapshot(); }
    private void ClearOperatorContext()
    { _desiredOperatorRequest = null; _desiredOperatorManifest = null; _acceptedOperatorInvocation = null; ClearOperatorDisplay(); }
}
