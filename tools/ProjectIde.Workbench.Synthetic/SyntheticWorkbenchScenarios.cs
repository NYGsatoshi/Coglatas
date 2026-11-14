using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

public sealed partial class SyntheticWorkbenchViewModel
{
    private long _scenarioPreviewGeneration;
    private ProjectScenarioPlan? _activeScenario;
    private CompilerResult? _scenarioBaselineResult;
    private ProjectTestRun? _scenarioBaselineTests;
    public ProjectScenarioPreview? ScenarioPreview { get; private set; }
    public ProjectScenarioDraftPreview? ScenarioDraftPreview { get; private set; }
    public ProjectScenarioPlan? ActiveScenario => State == "Ready" && _activeScenario is { } plan && _compilation is { } compilation &&
        plan.Input.Data.Equals(compilation.Input.Data) && plan.Input.Source.Data.Equals(Source?.Data) ? plan : null;
    public CompilerResult? ScenarioBaselineResult => ActiveScenario is null ? null : _scenarioBaselineResult;
    public ProjectTestRun? ScenarioBaselineTests => ActiveScenario is null ? null : _scenarioBaselineTests;

    public ProjectScenarioPreview? PreviewScenario(ScenarioId id, ScenarioRevisionId revision, IEnumerable<ProjectScenarioOverride> overrides,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(overrides); cancellationToken.ThrowIfCancellationRequested();
        var lifecycle = _lifecycle; var selection = _selectionGeneration; var generation = ++_scenarioPreviewGeneration;
        ScenarioPreview = null; ScenarioDraftPreview = null; Changed();
        if (_disposed || lifecycle != _lifecycle || selection != _selectionGeneration || generation != _scenarioPreviewGeneration ||
            State != "Ready" || _compilation?.Model is not { } model || _desired is null) return null;
        var preview = ProjectScenarios.Prepare(model, _desired.Input.ExpectedInputDigest, id, revision, overrides, cancellationToken);
        if (_disposed || lifecycle != _lifecycle || selection != _selectionGeneration || generation != _scenarioPreviewGeneration) return null;
        ScenarioPreview = preview; Changed();
        if (!_disposed && lifecycle == _lifecycle && selection == _selectionGeneration && generation == _scenarioPreviewGeneration &&
            ReferenceEquals(ScenarioPreview, preview)) return preview;
        if (ReferenceEquals(ScenarioPreview, preview)) { ClearScenarioPreview(); Changed(); } return null;
    }

    public CompilerHostRequest? LoadScenario(ProjectScenarioPlan plan, long clientGeneration, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(plan); cancellationToken.ThrowIfCancellationRequested();
        if (State != "Ready" || !ReferenceEquals(ScenarioPreview?.Plan, plan) || _desired is null || _compilation is null ||
            clientGeneration <= _lastGeneration || plan.Materialize(_compilation.Input, cancellationToken) is not { } input) return null;
        var result = Result; var tests = TestRun;
        var request = new CompilerHostRequest(new(input), clientGeneration, _desired.Snapshots, _desired.Units, _desired.Calendars);
        BeginSynthetic(request);
        if (_disposed || !ReferenceEquals(_desired, request)) return null;
        _activeScenario = plan; _scenarioBaselineResult = result; _scenarioBaselineTests = tests; Changed();
        return !_disposed && ReferenceEquals(_desired, request) && ReferenceEquals(_activeScenario, plan) ? request : null;
    }

    /// <summary>The caller supplies the currently admitted base; the captured baseline is never substituted for that check.</summary>
    public ProjectScenarioDraftPreview? PreviewScenarioDraft(PartialSemanticModel currentBase, ContentDigest expectedBase, ProposalContext proposal,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(currentBase); cancellationToken.ThrowIfCancellationRequested();
        var lifecycle = _lifecycle; var selection = _selectionGeneration; var generation = ++_scenarioPreviewGeneration;
        ScenarioDraftPreview = null; Changed();
        if (_disposed || lifecycle != _lifecycle || selection != _selectionGeneration || generation != _scenarioPreviewGeneration || ActiveScenario is not { } plan) return null;
        var preview = ProjectScenarios.ToDraft(plan, currentBase, expectedBase, proposal, cancellationToken);
        if (_disposed || lifecycle != _lifecycle || selection != _selectionGeneration || generation != _scenarioPreviewGeneration) return null;
        ScenarioDraftPreview = preview; Changed();
        if (!_disposed && lifecycle == _lifecycle && selection == _selectionGeneration && generation == _scenarioPreviewGeneration &&
            ReferenceEquals(ScenarioDraftPreview, preview)) return preview;
        if (ReferenceEquals(ScenarioDraftPreview, preview)) { ClearScenarioPreview(); Changed(); } return null;
    }

    public CompilerHostRequest? ApplyScenarioDraft(ProjectScenarioDraftPlan plan, CompilerInputManifest currentBase, long clientGeneration,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(plan); cancellationToken.ThrowIfCancellationRequested();
        if (State != "Ready" || !ReferenceEquals(ScenarioDraftPreview?.Plan, plan) || !ReferenceEquals(ActiveScenario, plan.Scenario) ||
            _desired is null || clientGeneration <= _lastGeneration || plan.ApplyToDraft(currentBase, cancellationToken) is not { } after) return null;
        var request = new CompilerHostRequest(new(after), clientGeneration, _desired.Snapshots, _desired.Units, _desired.Calendars);
        BeginSynthetic(request); return !_disposed && ReferenceEquals(_desired, request) ? request : null;
    }
    private void ClearScenarioPreview() { _scenarioPreviewGeneration++; ScenarioPreview = null; ScenarioDraftPreview = null; }
    private void ClearScenarioContext() { ClearScenarioPreview(); _activeScenario = null; _scenarioBaselineResult = null; _scenarioBaselineTests = null; }
}
