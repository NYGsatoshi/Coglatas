using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

public sealed partial class SyntheticWorkbenchViewModel
{
    private RegionalPredicateObservationCompilerHostRequest? _desiredRegionalOperatorRequest;
    private LocalRegionalPredicateObservationCompilerRecordingManifest? _desiredRegionalOperatorManifest;
    private RegionalPredicateObservationCompilerRecordedInvocation? _acceptedRegionalOperatorInvocation;
    private SourceJson? _regionalOperatorReceiptMetadata;
    private SourceJson? _regionalOperatorRecordMetadata;
    private CompilerDependencyRegion? _regionalOperatorRegion;
    private CompilerDiagnostic? _regionalOperatorDiagnostic;
    private long _regionalOperatorSelection;
    private long _regionalOperatorReadGeneration;
    private long _regionalOperatorSnapshotGeneration;
    private ProjectCompilerRegionalPredicateObservationSnapshot? _recordedRegionalOperators;
    private RegionalPredicateObservationCompilerRecordedInvocation? _regionalOperatorSnapshotInvocation;
    private CompilerDependencyRegion? _regionalOperatorSnapshotRegion;
    private CompilerDiagnostic? _regionalOperatorSnapshotDiagnostic;

    private bool CurrentRegionalOperatorInvocation => !_disposed && State == "Ready" &&
        _desiredRegionalOperatorRequest is { } request && ReferenceEquals(_desired, request.Core) &&
        _desiredRegionalOperatorManifest is { } manifest && _acceptedRegionalOperatorInvocation is { } actual &&
        actual.IsFor(request, manifest) && ReferenceEquals(actual.Response.Execution?.Compilation, _compilation);
    private bool CurrentRegionalOperatorSource => CurrentRegionalOperatorInvocation && (CurrentRootSource || CurrentScopedSources);
    private bool RegionalOperatorSourceAdmitted(CompilerDependencyRegion region) => CurrentRegionalOperatorInvocation &&
        (region.Binding.IsRoot ? CurrentRootSource || CurrentScopedSources : CurrentScopedSources);
    private bool CurrentRegionalOperatorSelection => _regionalOperatorSelection == _selectionGeneration &&
        _regionalOperatorRegion is { } region && _regionalOperatorDiagnostic is { } diagnostic && RegionalOperatorSourceAdmitted(region) &&
        _compilation?.Dependencies?.Regions.Any(r => ReferenceEquals(r, region)) == true &&
        region.Diagnostics.Any(d => ReferenceEquals(d, diagnostic));

    public SourceJson? RegionalOperatorReceiptMetadata => CurrentRegionalOperatorSource ? _regionalOperatorReceiptMetadata : null;
    public SourceJson? RegionalOperatorRecordMetadata => CurrentRegionalOperatorSource ? _regionalOperatorRecordMetadata : null;
    public CompilerDependencyRegion? SelectedRegionalOperatorRegion => CurrentRegionalOperatorSelection ? _regionalOperatorRegion : null;
    public CompilerDiagnostic? SelectedRegionalOperatorDiagnostic => CurrentRegionalOperatorSelection ? _regionalOperatorDiagnostic : null;
    public ProjectCompilerRegionalPredicateObservationSnapshot? RecordedRegionalOperators => CurrentRegionalOperatorSelection &&
        ReferenceEquals(_regionalOperatorSnapshotInvocation, _acceptedRegionalOperatorInvocation) &&
        ReferenceEquals(_regionalOperatorSnapshotRegion, _regionalOperatorRegion) &&
        ReferenceEquals(_regionalOperatorSnapshotDiagnostic, _regionalOperatorDiagnostic) ? _recordedRegionalOperators : null;

    /// <summary>Declare a separate actual regional run before notifications. This is local intent, not current access.</summary>
    public void BeginRegionalOperatorSynthetic(LocalRegionalPredicateObservationCompilerRecordingManifest manifest,
        RegionalPredicateObservationCompilerHostRequest request, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(manifest); ArgumentNullException.ThrowIfNull(request);
        if (!manifest.IsFor(request)) throw new ArgumentException("Manifest differs from the exact regional operator request.");
        BeginSyntheticContext(request.Core, null, null, cancellationToken: token,
            regionalOperatorRequest: request, regionalOperatorManifest: manifest);
    }

    public bool TryAcceptRegionalOperator(RegionalPredicateObservationCompilerRecordedInvocation actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        if (_disposed || State != "Waiting" || _desiredRegionalOperatorRequest is not { } request ||
            _desiredRegionalOperatorManifest is not { } manifest || !actual.IsFor(request, manifest)) return false;
        var response = actual.Response;
        if (response.Status != CompilerHostStatus.Completed)
        { ClearResult(); State = response.Status.ToString(); Changed(); return false; }
        if (response.Execution is not { } execution || !execution.Observation.IsOwnedBy(execution) ||
            !ReferenceEquals(execution.Observation.Policy, request.ObservationPolicy) ||
            !execution.Compilation.Input.Data.Equals(execution.Compilation.Result.Input.Data) ||
            !execution.Compilation.Input.Source.Data.Equals(Source?.Data)) return false;
        var compilation = execution.Compilation;
        if (!TryPrepareInspection(compilation.Model, out var inspection)) return false;
        var lifecycle = _lifecycle;
        ClearResult(); _compilation = compilation; _acceptedRegionalOperatorInvocation = actual;
        Result = compilation.Result; Inspection = inspection; State = "Ready"; Changed();
        return !_disposed && lifecycle == _lifecycle && ReferenceEquals(Result, compilation.Result) &&
            ReferenceEquals(_acceptedRegionalOperatorInvocation, actual);
    }

    public bool ReadRegionalOperatorRecordingMetadata(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ClearRegionalOperatorDisplay();
        var generation = _regionalOperatorReadGeneration; var root = _rootSourceGeneration; var scope = _scopedGeneration;
        var request = _desiredRegionalOperatorRequest; var actual = _acceptedRegionalOperatorInvocation;
        bool Current() => generation == _regionalOperatorReadGeneration && root == _rootSourceGeneration && scope == _scopedGeneration &&
            CurrentRegionalOperatorSource && ReferenceEquals(request, _desiredRegionalOperatorRequest) && ReferenceEquals(actual, _acceptedRegionalOperatorInvocation);
        Changed(); token.ThrowIfCancellationRequested(); if (!Current()) return false;
        _regionalOperatorReceiptMetadata = actual!.Receipt.Data; _regionalOperatorRecordMetadata = actual.Receipt.ObservationMetadata; Changed();
        if (token.IsCancellationRequested)
        {
            if (generation == _regionalOperatorReadGeneration && ReferenceEquals(_regionalOperatorReceiptMetadata, actual.Receipt.Data))
            { _regionalOperatorReadGeneration++; _regionalOperatorReceiptMetadata = null; _regionalOperatorRecordMetadata = null; Changed(); }
            token.ThrowIfCancellationRequested();
        }
        return Current() && ReferenceEquals(RegionalOperatorReceiptMetadata, actual.Receipt.Data);
    }

    /// <summary>Select the exact actual regional finding after its original Source has been admitted.</summary>
    public bool SelectRegionalOperatorDiagnostic(CompilerDependencyRegion region, CompilerDiagnostic diagnostic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(region); ArgumentNullException.ThrowIfNull(diagnostic);
        var generation = ClearSelection(); var actual = _acceptedRegionalOperatorInvocation;
        bool Owned() => RegionalOperatorSourceAdmitted(region) && ReferenceEquals(actual, _acceptedRegionalOperatorInvocation) &&
            _compilation?.Dependencies?.Regions.Any(r => ReferenceEquals(r, region)) == true &&
            region.Diagnostics.Any(d => ReferenceEquals(d, diagnostic));
        if (!Owned()) { NavigationState = "unavailable"; Changed(); return false; }
        if (region.Binding.IsRoot) NavigationState = Resolve(diagnostic.Location);
        else
        {
            var expectedSelection = _selectionGeneration + 1;
            _ = NavigateScopedSource(diagnostic.Location);
            if (_selectionGeneration != expectedSelection || !Owned()) return false;
            generation = _selectionGeneration;
        }
        _regionalOperatorRegion = region; _regionalOperatorDiagnostic = diagnostic; _regionalOperatorSelection = generation; Changed();
        return generation == _selectionGeneration && CurrentRegionalOperatorSelection && ReferenceEquals(_regionalOperatorRegion, region) &&
            ReferenceEquals(_regionalOperatorDiagnostic, diagnostic);
    }

    public bool ReadRecordedRegionalOperators(ContentDigest expectedResult,
        CompilerRegionalPredicateSnapshotMode mode = CompilerRegionalPredicateSnapshotMode.Manifest,
        int maximumBodyBytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(expectedResult);
        ClearRegionalOperatorSnapshot(); var generation = _regionalOperatorSnapshotGeneration;
        var root = _rootSourceGeneration; var scope = _scopedGeneration; var selection = _selectionGeneration;
        var actual = _acceptedRegionalOperatorInvocation; var region = _regionalOperatorRegion; var diagnostic = _regionalOperatorDiagnostic;
        var compilation = _compilation;
        bool Current() => generation == _regionalOperatorSnapshotGeneration && root == _rootSourceGeneration && scope == _scopedGeneration &&
            selection == _selectionGeneration && CurrentRegionalOperatorSelection && ReferenceEquals(actual, _acceptedRegionalOperatorInvocation) &&
            ReferenceEquals(region, _regionalOperatorRegion) && ReferenceEquals(diagnostic, _regionalOperatorDiagnostic) &&
            ReferenceEquals(compilation, _compilation) && compilation!.Result.ResultDigest.Equals(expectedResult);
        Changed(); token.ThrowIfCancellationRequested(); if (!Current()) return false;
        var execution = actual!.Response.Execution!;
        var snapshot = ProjectCompilerRegionalPredicateObservationSnapshot.Read(execution, compilation!, execution.Observation, region!, diagnostic!,
            mode, maximumBodyBytes, token);
        if (!Current()) return false;
        _recordedRegionalOperators = snapshot; _regionalOperatorSnapshotInvocation = actual;
        _regionalOperatorSnapshotRegion = region; _regionalOperatorSnapshotDiagnostic = diagnostic; Changed();
        if (token.IsCancellationRequested)
        {
            if (generation == _regionalOperatorSnapshotGeneration && ReferenceEquals(_recordedRegionalOperators, snapshot))
            { ClearRegionalOperatorSnapshot(); Changed(); }
            token.ThrowIfCancellationRequested();
        }
        return Current() && ReferenceEquals(RecordedRegionalOperators, snapshot) && snapshot.State != CompilerRegionalPredicateSnapshotState.RefusedInvocation;
    }

    public bool NavigateRegionalOperatorDiagnostic()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!CurrentRegionalOperatorSelection) return false;
        var root = _rootSourceGeneration; var scope = _scopedGeneration; var selection = _selectionGeneration;
        var actual = _acceptedRegionalOperatorInvocation; var region = _regionalOperatorRegion!; var diagnostic = _regionalOperatorDiagnostic!;
        bool Current() => root == _rootSourceGeneration && scope == _scopedGeneration && selection == _selectionGeneration &&
            CurrentRegionalOperatorSelection && ReferenceEquals(actual, _acceptedRegionalOperatorInvocation) &&
            ReferenceEquals(region, _regionalOperatorRegion) && ReferenceEquals(diagnostic, _regionalOperatorDiagnostic);
        SourceTarget = null; ClearScopedSelection();
        if (region.Binding.IsRoot)
        {
            NavigationState = Resolve(diagnostic.Location);
            if (NavigationState == "ready") SourceTarget = diagnostic.Location;
        }
        else ResolveScopedSourceTarget(diagnostic.Location);
        Changed();
        return Current() && ReferenceEquals(region.Binding.IsRoot ? SourceTarget : ScopedSourceTarget, diagnostic.Location);
    }
    private void ClearRegionalOperatorSnapshot()
    {
        _regionalOperatorSnapshotGeneration++; _recordedRegionalOperators = null; _regionalOperatorSnapshotInvocation = null;
        _regionalOperatorSnapshotRegion = null; _regionalOperatorSnapshotDiagnostic = null;
    }
    private void ClearRegionalOperatorSelection()
    { _regionalOperatorRegion = null; _regionalOperatorDiagnostic = null; ClearRegionalOperatorSnapshot(); }
    private void ClearRegionalOperatorDisplay()
    { _regionalOperatorReadGeneration++; _regionalOperatorReceiptMetadata = null; _regionalOperatorRecordMetadata = null; ClearRegionalOperatorSnapshot(); }
    private void ClearRegionalOperatorContext()
    {
        _desiredRegionalOperatorRequest = null; _desiredRegionalOperatorManifest = null; _acceptedRegionalOperatorInvocation = null;
        ClearRegionalOperatorDisplay(); ClearRegionalOperatorSelection();
    }
}
