using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

public sealed partial class SyntheticWorkbenchViewModel
{
    private ProjectRootSourceAdmission? _rootSourceAdmission;
    private CompilerHostRequest? _rootSourceRequest;
    private long _rootSourceGeneration;
    public string RootSourceReadState { get; private set; } = "Idle";
    public ProjectSource? RootSource => CurrentRootSource ? _rootSourceAdmission!.Source : null;
    private bool CurrentRootSource => !_disposed && State == "Ready" && _compilation is not null &&
        ReferenceEquals(_desired, _rootSourceRequest) && _rootSourceAdmission?.IsOwnedBy(_compilation) == true;

    /// <summary>Admit the exact retained root Source without implying complete regional IR or runtime authorization.</summary>
    public bool LoadSyntheticRootSource(ContentDigest expectedResult, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(expectedResult);
        ClearScopedSources(); var generation = _rootSourceGeneration; var request = _desired; var compilation = _compilation;
        RootSourceReadState = "Loading"; Changed();
        bool Current() => !_disposed && generation == _rootSourceGeneration && State == "Ready" && compilation is not null && request is not null &&
            ReferenceEquals(compilation, _compilation) && ReferenceEquals(request, _desired) && ReferenceEquals(Result, compilation.Result);
        if (!Current()) return false;
        ProjectRootSourceAdmission? admission;
        try { admission = ProjectRootSourceAdmission.Read(compilation!, Result!, expectedResult, token); }
        catch (OperationCanceledException)
        {
            if (Current()) { ClearRootSource(); Changed(); }
            throw;
        }
        if (!Current()) return false;
        _rootSourceAdmission = admission; _rootSourceRequest = request;
        RootSourceReadState = admission is null ? "Unavailable" : "Ready"; Changed();
        if (token.IsCancellationRequested)
        {
            if (generation == _rootSourceGeneration && ReferenceEquals(_rootSourceAdmission, admission))
            { ClearRootSource(); Changed(); }
            token.ThrowIfCancellationRequested();
        }
        return Current() && CurrentRootSource && ReferenceEquals(_rootSourceAdmission, admission);
    }
    private void ClearRootSource()
    {
        ClearReferenceSearchPage();
        _rootSourceGeneration++; _rootSourceAdmission = null; _rootSourceRequest = null; RootSourceReadState = "Idle";
        ClearDiagnosticPremises(); ClearPredicateDisplay(); ClearOperatorDisplay(); ClearPassDisplay();
    }
}
