using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

public sealed partial class SyntheticWorkbenchViewModel
{
    private long _testGeneration;
    private long _testDiscoveryGeneration;
    public ProjectTestRun? TestRun { get; private set; }
    public ProjectTestAssertionResult? SelectedTestAssertion { get; private set; }

    public ProjectTestDiscovery? TestDiscovery { get; private set; }
    public ProjectDiscoveredTest? SelectedDiscoveredTest { get; private set; }

    /// <summary>Read exact local Test definitions without evaluating assertions or replacing a completed run.</summary>
    public ProjectTestDiscovery? DiscoverSyntheticTests(CancellationToken cancellationToken = default, ProjectTestLimits? limits = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); cancellationToken.ThrowIfCancellationRequested();
        ClearTestDiscovery();
        var generation = _testDiscoveryGeneration; var lifecycle = _lifecycle;
        Changed();
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed || generation != _testDiscoveryGeneration || lifecycle != _lifecycle ||
            _desired is null || State != "Ready" || _compilation?.Model is not { } model ||
            !model.Input.InputDigest.Equals(_desired.Input.ExpectedInputDigest) || !model.Input.Source.Data.Equals(Source?.Data)) return null;
        var compilation = _compilation;
        var inventory = ProjectTestPlan.Compile(model, SyntheticProjectTestConfiguration.Evaluator, limits, cancellationToken)
            .Discover(_desired.Input.ExpectedInputDigest, cancellationToken);
        if (_disposed || generation != _testDiscoveryGeneration || lifecycle != _lifecycle || !ReferenceEquals(compilation, _compilation)) return null;
        cancellationToken.ThrowIfCancellationRequested();
        TestDiscovery = inventory;
        bool cancelled; Exception? publicationFailure = null; Exception? withdrawalFailure = null;
        try { Changed(); }
        catch (Exception error) { publicationFailure = error; }
        finally
        {
            cancelled = cancellationToken.IsCancellationRequested;
            if (cancelled && generation == _testDiscoveryGeneration && ReferenceEquals(TestDiscovery, inventory))
            {
                ClearTestDiscovery();
                try { Changed(); } catch (Exception error) { withdrawalFailure = error; }
            }
        }
        if (publicationFailure is not null)
        {
            if (withdrawalFailure is not null) RetainDiscoveryWithdrawalFailure(publicationFailure, withdrawalFailure);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(publicationFailure).Throw();
        }
        if (cancelled) throw new OperationCanceledException("Test discovery was cancelled.", withdrawalFailure, cancellationToken);
        return !_disposed && generation == _testDiscoveryGeneration && lifecycle == _lifecycle &&
            ReferenceEquals(compilation, _compilation) && ReferenceEquals(TestDiscovery, inventory) ? inventory : null;
    }

    private static void RetainDiscoveryWithdrawalFailure(Exception publication, Exception withdrawal)
    {
        try { publication.Data["TestDiscoveryWithdrawalFailure"] = withdrawal; }
        catch (Exception)
        {
            // Diagnostic attachment is best effort; the original publication failure remains authoritative.
        }
    }

    public bool SelectDiscoveredTest(ProjectDiscoveredTest test)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(test);
        if (State != "Ready" || TestDiscovery is not { State: ProjectTestDiscoveryState.Ready } inventory || _desired is null ||
            !inventory.InputDigest!.Equals(_desired.Input.ExpectedInputDigest) || _compilation?.Model is not { } model ||
            !model.Input.Source.Data.Equals(Source?.Data) || !inventory.Tests.Any(item => ReferenceEquals(item, test)) || Resolve(test.Location) != "ready") return false;
        var generation = ClearSelection();
        NavigationState = "ready"; SelectedDiscoveredTest = test;
        Changed(); return !_disposed && generation == _selectionGeneration && ReferenceEquals(SelectedDiscoveredTest, test);
    }

    private void ClearTestDiscovery()
    {
        _testDiscoveryGeneration++; TestDiscovery = null;
        if (SelectedDiscoveredTest is null) return;
        SelectedDiscoveredTest = null; SourceTarget = null; NavigationState = ""; _selectionGeneration++;
    }

    /// <summary>Run only the fixed offline fixture host; request pins never define the supported evaluator.</summary>
    public bool RunSyntheticTests(CancellationToken cancellationToken = default, ProjectTestLimits? limits = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var generation = ++_testGeneration; var lifecycle = _lifecycle;
        TestRun = null; ClearTestDiscovery(); ClearSelection(); Changed();
        if (_disposed || generation != _testGeneration || lifecycle != _lifecycle ||
            _desired is null || State != "Ready" || _compilation?.Model is not { } model ||
            !model.Input.InputDigest.Equals(_desired.Input.ExpectedInputDigest) || !model.Input.Source.Data.Equals(Source?.Data)) return false;
        var compilation = _compilation;
        var run = ProjectTestPlan.Compile(model, SyntheticProjectTestConfiguration.Evaluator, limits).Execute(_desired.Input.ExpectedInputDigest, cancellationToken);
        if (_disposed || generation != _testGeneration || lifecycle != _lifecycle || !ReferenceEquals(compilation, _compilation)) return false;
        TestRun = run; Changed();
        return !_disposed && generation == _testGeneration && lifecycle == _lifecycle && ReferenceEquals(TestRun, run);
    }

    public bool SelectTestAssertion(ProjectTestAssertionResult assertion)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(assertion);
        var generation = ClearSelection();
        if (State == "Ready" && TestRun is { } run && _desired is not null &&
            run.Input.InputDigest.Equals(_desired.Input.ExpectedInputDigest) && run.Input.Source.Data.Equals(Source?.Data) &&
            run.Tests.SelectMany(test => test.Assertions).Any(item => ReferenceEquals(item, assertion)))
        {
            NavigationState = Resolve(assertion.Location);
            if (NavigationState == "ready") SelectedTestAssertion = assertion;
        }
        else NavigationState = "unavailable";
        Changed(); return !_disposed && generation == _selectionGeneration && ReferenceEquals(SelectedTestAssertion, assertion);
    }

    private void ClearTests() { _testGeneration++; TestRun = null; SelectedTestAssertion = null; ClearTestDiscovery(); }
}
