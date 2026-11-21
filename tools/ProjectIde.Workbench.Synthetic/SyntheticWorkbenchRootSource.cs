using System.ComponentModel;
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
        RootSourceReadState = "Loading";
        try { Changed(); }
        catch when (token.IsCancellationRequested && generation == _rootSourceGeneration)
        {
            ClearRootSource();
            _ = NotifySourceWithdrawal();
            throw;
        }
        bool Current() => !_disposed && generation == _rootSourceGeneration && State == "Ready" && compilation is not null && request is not null &&
            ReferenceEquals(compilation, _compilation) && ReferenceEquals(request, _desired) && ReferenceEquals(Result, compilation.Result);
        if (!Current())
        {
            token.ThrowIfCancellationRequested();
            return false;
        }
        ProjectRootSourceAdmission? admission;
        try { admission = ProjectRootSourceAdmission.Read(compilation!, Result!, expectedResult, token); }
        catch (OperationCanceledException cancellation)
        {
            if (Current())
            {
                ClearRootSource();
                if (NotifySourceWithdrawal() is { } notificationFailure)
                    throw new OperationCanceledException(cancellation.Message, notificationFailure, cancellation.CancellationToken);
            }
            throw;
        }
        if (!Current()) return false;
        _rootSourceAdmission = admission; _rootSourceRequest = request;
        RootSourceReadState = admission is null ? "Unavailable" : "Ready";
        Exception? withdrawalFailure = null;
        bool cancelledAtPublication;
        try { Changed(); }
        finally
        {
            cancelledAtPublication = token.IsCancellationRequested;
            if (cancelledAtPublication && generation == _rootSourceGeneration && ReferenceEquals(_rootSourceAdmission, admission))
            {
                ClearRootSource();
                withdrawalFailure = NotifySourceWithdrawal();
            }
        }
        // If publication threw, its original failure is already propagating.
        if (cancelledAtPublication)
            throw new OperationCanceledException("Root Source read was cancelled.", withdrawalFailure, token);
        return Current() && CurrentRootSource && ReferenceEquals(_rootSourceAdmission, admission);
    }

    private Exception? NotifySourceWithdrawal()
    {
        var subscribers = PropertyChanged;
        if (subscribers is null) return null;

        var args = new PropertyChangedEventArgs(null);
        Exception? firstFailure = null;
        foreach (var subscriber in Delegate.EnumerateInvocationList(subscribers))
        {
            try { subscriber(this, args); }
            catch (Exception error) { firstFailure ??= error; }
        }
        return firstFailure;
    }

    private void ClearRootSource()
    {
        ClearReferenceSearchPage();
        _rootSourceGeneration++; _rootSourceAdmission = null; _rootSourceRequest = null; RootSourceReadState = "Idle";
        ClearDiagnosticPremises(); ClearPredicateDisplay(); ClearOperatorDisplay(); ClearPassDisplay(); ClearRegionalOperatorDisplay();
    }
}
