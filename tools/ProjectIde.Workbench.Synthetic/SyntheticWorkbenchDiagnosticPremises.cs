using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

public sealed partial class SyntheticWorkbenchViewModel
{
    private ProjectDiagnosticPremiseSnapshot? _diagnosticPremises;
    private CoreCompilation? _premiseCompilation;
    private CompilerDiagnostic? _premiseDiagnostic;
    private long _premiseGeneration;
    private bool CurrentPremiseSource => CurrentScopedSources || CurrentRootSource &&
        SelectedDiagnostic?.Location.Context.Equals(_rootSourceAdmission!.Source.Context) == true;
    public ProjectDiagnosticPremiseSnapshot? DiagnosticPremises => CurrentPremiseSource &&
        ReferenceEquals(_compilation, _premiseCompilation) && ReferenceEquals(SelectedDiagnostic, _premiseDiagnostic) ? _diagnosticPremises : null;

    /// <summary>Read retained captured objects only after Source admission; no Compiler or current access grant.</summary>
    public bool ReadSyntheticDiagnosticPremises(ContentDigest expectedResult, DiagnosticPremiseSnapshotMode mode = DiagnosticPremiseSnapshotMode.Manifest,
        int maximumBodyBytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(expectedResult);
        ClearDiagnosticPremises(); var generation = _premiseGeneration; var selection = _selectionGeneration;
        var compilation = _compilation; var diagnostic = SelectedDiagnostic; var request = _desired; var scope = _scopedGeneration; var rootScope = _rootSourceGeneration;
        bool Current() => !_disposed && generation == _premiseGeneration && selection == _selectionGeneration && scope == _scopedGeneration && rootScope == _rootSourceGeneration &&
            CurrentPremiseSource && compilation is not null && diagnostic is not null && ReferenceEquals(compilation, _compilation) &&
            ReferenceEquals(diagnostic, SelectedDiagnostic) && ReferenceEquals(request, _desired);
        Changed();
        if (!Current())
        {
            token.ThrowIfCancellationRequested();
            return false;
        }
        var snapshot = CurrentScopedSources
            ? ProjectDiagnosticPremiseSnapshot.Read(compilation!, diagnostic!, expectedResult, mode, maximumBodyBytes, token)
            : ProjectDiagnosticPremiseSnapshot.ReadRoot(_rootSourceAdmission!, diagnostic!, expectedResult, mode, maximumBodyBytes, token);
        if (!Current()) return false;
        _diagnosticPremises = snapshot;
        _premiseCompilation = compilation;
        _premiseDiagnostic = diagnostic;
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
            if (cancelledAtPublication && generation == _premiseGeneration && ReferenceEquals(_diagnosticPremises, snapshot))
            {
                ClearDiagnosticPremises();
                try { Changed(); }
                catch when (!publicationCompleted)
                {
                    // Preserve the original publication exception after withdrawing the cancelled view.
                }
            }
        }
        if (cancelledAtPublication) token.ThrowIfCancellationRequested();
        return Current() && ReferenceEquals(DiagnosticPremises, snapshot) && snapshot.State is not
            (DiagnosticPremiseSnapshotState.RefusedWrongResult or DiagnosticPremiseSnapshotState.RefusedInputBinding or
             DiagnosticPremiseSnapshotState.RefusedDiagnostic or DiagnosticPremiseSnapshotState.RefusedBodyBinding);
    }
    private void ClearDiagnosticPremises()
    { _premiseGeneration++; _diagnosticPremises = null; _premiseCompilation = null; _premiseDiagnostic = null; }
}
