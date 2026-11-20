using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

public sealed partial class SyntheticWorkbenchViewModel
{
    private PredicateCompilerHostRequest? _desiredPredicateRequest;
    private LocalPredicateCompilerRecordingManifest? _desiredPredicateManifest;
    private PredicateCompilerRecordedInvocation? _acceptedPredicateInvocation;
    private SourceJson? _predicateReceiptMetadata;
    private SourceJson? _predicateRecordMetadata;
    private long _predicateReadGeneration;
    private ProjectRecordedPredicatePremiseSnapshot? _recordedPredicatePremises;
    private CompilerDiagnostic? _recordedPredicateDiagnostic;
    private CoreCompilation? _recordedPredicateCompilation;
    private long _recordedPredicateGeneration;
    private bool CurrentPredicateSource => CurrentRootSource && _desiredPredicateRequest is { } request && ReferenceEquals(_desired, request.Core) &&
        _acceptedPredicateInvocation is { } invocation && ReferenceEquals(invocation.Receipt.Manifest, _desiredPredicateManifest) &&
        invocation.Response.IsFor(request) && invocation.Receipt.IsFor(invocation.Response) && ReferenceEquals(invocation.Response.Compilation, _compilation);
    public SourceJson? PredicateReceiptMetadata => CurrentPredicateSource ? _predicateReceiptMetadata : null;
    public SourceJson? PredicateRecordMetadata => CurrentPredicateSource ? _predicateRecordMetadata : null;
    public ProjectRecordedPredicatePremiseSnapshot? RecordedPredicatePremises => CurrentPredicateSource &&
        ReferenceEquals(_recordedPredicateCompilation, _compilation) && ReferenceEquals(_recordedPredicateDiagnostic, SelectedDiagnostic) ? _recordedPredicatePremises : null;

    /// <summary>Declare this exact intended local run before execution; hashes alone do not select a run.</summary>
    public void BeginPredicateSynthetic(LocalPredicateCompilerRecordingManifest manifest, PredicateCompilerHostRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(manifest); ArgumentNullException.ThrowIfNull(request);
        if (manifest.ClientGeneration != request.ClientGeneration || !manifest.ExecutionInputDigest.Equals(request.ExecutionInputDigest) ||
            !manifest.BaseExecutionInputDigest.Equals(request.Core.ExecutionInputDigest) || !manifest.PolicyDigest.Equals(request.PolicyDigest) ||
            !manifest.MaterializationsDigest.Equals(request.Core.MaterializationDigest)) throw new ArgumentException("Manifest differs from declared predicate request.");
        BeginSyntheticContext(request.Core, request, manifest);
    }

    /// <summary>Accept only the actual opaque invocation for the preselected manifest and request.</summary>
    public bool TryAcceptPredicate(PredicateCompilerRecordedInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (_disposed || State != "Waiting" || _desiredPredicateRequest is not { } request || _desired is null ||
            !ReferenceEquals(invocation.Receipt.Manifest, _desiredPredicateManifest) || !invocation.Response.IsFor(request) ||
            !invocation.Receipt.IsFor(invocation.Response)) return false;
        var response = invocation.Response;
        if (response.Status != CompilerHostStatus.Completed)
        { ClearResult(); State = response.Status.ToString(); Changed(); return false; }
        if (response.Compilation is not { PredicatePremises: { } record } compilation ||
            !record.IsOwnedBy(compilation) || !ReferenceEquals(record.Policy, request.CapturePolicy) ||
            !compilation.Input.Data.Equals(compilation.Result.Input.Data) || !compilation.Input.Source.Data.Equals(Source?.Data)) return false;
        if (!TryPrepareInspection(compilation.Model, out var inspection)) return false; var lifecycle = _lifecycle;
        ClearResult(); _compilation = compilation; _acceptedPredicateInvocation = invocation;
        Result = compilation.Result; Inspection = inspection; State = "Ready"; Changed();
        return !_disposed && lifecycle == _lifecycle && ReferenceEquals(Result, compilation.Result) && ReferenceEquals(_acceptedPredicateInvocation, invocation);
    }

    /// <summary>Read original metadata after explicit Source admission; no body map or runtime permission is exposed.</summary>
    public bool ReadPredicateRecordingMetadata()
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ClearPredicateDisplay(); var generation = _predicateReadGeneration;
        var root = _rootSourceGeneration; var request = _desiredPredicateRequest; var invocation = _acceptedPredicateInvocation;
        bool Current() => !_disposed && generation == _predicateReadGeneration && root == _rootSourceGeneration && CurrentPredicateSource &&
            ReferenceEquals(request, _desiredPredicateRequest) && ReferenceEquals(invocation, _acceptedPredicateInvocation);
        Changed(); if (!Current()) return false;
        _predicateReceiptMetadata = invocation!.Receipt.Data; _predicateRecordMetadata = invocation.Receipt.PredicateMetadata; Changed();
        return Current() && ReferenceEquals(PredicateReceiptMetadata, invocation.Receipt.Data);
    }

    /// <summary>Explicit selected-diagnostic original-array read; no execution or earlier-state reconstruction.</summary>
    public bool ReadRecordedPredicatePremises(ContentDigest expectedResult, DiagnosticPremiseSnapshotMode mode = DiagnosticPremiseSnapshotMode.Manifest,
        int maximumBodyBytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(expectedResult);
        ClearRecordedPredicatePremises(); var generation = _recordedPredicateGeneration; var root = _rootSourceGeneration; var selection = _selectionGeneration;
        var request = _desiredPredicateRequest; var invocation = _acceptedPredicateInvocation; var diagnostic = SelectedDiagnostic; var compilation = _compilation;
        bool Current() => !_disposed && generation == _recordedPredicateGeneration && root == _rootSourceGeneration && selection == _selectionGeneration &&
            CurrentPredicateSource && diagnostic is not null && ReferenceEquals(request, _desiredPredicateRequest) && ReferenceEquals(invocation, _acceptedPredicateInvocation) &&
            ReferenceEquals(compilation, _compilation) && ReferenceEquals(diagnostic, SelectedDiagnostic);
        Changed();
        if (!Current())
        {
            token.ThrowIfCancellationRequested();
            return false;
        }
        var snapshot = ProjectRecordedPredicatePremiseSnapshot.Read(_rootSourceAdmission!, compilation!.PredicatePremises!, diagnostic!, expectedResult, mode, maximumBodyBytes, token);
        if (!Current()) return false;
        _recordedPredicatePremises = snapshot;
        _recordedPredicateDiagnostic = diagnostic;
        _recordedPredicateCompilation = compilation;
        var publicationCompleted = false;
        bool cancelledAtPublication;
        try
        {
            Changed();
            publicationCompleted = true;
        }
        finally
        {
            cancelledAtPublication = token.IsCancellationRequested;
            if (cancelledAtPublication && generation == _recordedPredicateGeneration && ReferenceEquals(_recordedPredicatePremises, snapshot))
            {
                ClearRecordedPredicatePremises();
                try { Changed(); }
                catch when (!publicationCompleted)
                {
                    // Preserve the original publication exception after withdrawing the cancelled view.
                }
            }
        }
        if (cancelledAtPublication) token.ThrowIfCancellationRequested();
        return Current() && ReferenceEquals(RecordedPredicatePremises, snapshot) && snapshot.State is not
            (RecordedPredicateSnapshotState.RefusedWrongResult or RecordedPredicateSnapshotState.RefusedRecord or RecordedPredicateSnapshotState.RefusedDiagnostic or RecordedPredicateSnapshotState.RefusedBodyBinding);
    }

    private void ClearRecordedPredicatePremises()
    { _recordedPredicateGeneration++; _recordedPredicatePremises = null; _recordedPredicateDiagnostic = null; _recordedPredicateCompilation = null; }
    private void ClearPredicateDisplay()
    { _predicateReadGeneration++; _predicateReceiptMetadata = null; _predicateRecordMetadata = null; ClearRecordedPredicatePremises(); }
    private void ClearPredicateContext()
    { _desiredPredicateRequest = null; _desiredPredicateManifest = null; _acceptedPredicateInvocation = null; ClearPredicateDisplay(); }
}
