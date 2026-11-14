using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

public sealed partial class SyntheticWorkbenchViewModel
{
    private long _testGeneration;
    public ProjectTestRun? TestRun { get; private set; }
    public ProjectTestAssertionResult? SelectedTestAssertion { get; private set; }

    /// <summary>Run only the fixed offline fixture host; request pins never define the supported evaluator.</summary>
    public bool RunSyntheticTests(CancellationToken cancellationToken = default, ProjectTestLimits? limits = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var generation = ++_testGeneration; var lifecycle = _lifecycle;
        TestRun = null; ClearSelection(); Changed();
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

    private void ClearTests() { _testGeneration++; TestRun = null; SelectedTestAssertion = null; }
}
