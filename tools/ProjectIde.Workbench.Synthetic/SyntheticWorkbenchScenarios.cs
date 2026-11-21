using System.ComponentModel;
using System.Runtime.ExceptionServices;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

public sealed partial class SyntheticWorkbenchViewModel
{
    private sealed record ScenarioScheduleRead(ProjectScenarioPlan Scenario, ProjectSimulationRun Baseline,
        ProjectSimulationRun Hypothesis, ProjectScenarioElapsedScheduleImpact Impact);

    private long _scenarioPreviewGeneration;
    private long _scenarioScheduleGeneration;
    private ProjectScenarioPlan? _activeScenario;
    private CompilerResult? _scenarioBaselineResult;
    private ProjectTestRun? _scenarioBaselineTests;
    private ProjectSimulationRun? _scenarioBaselineSimulation;
    private ScenarioScheduleRead? _scenarioScheduleRead;
    public ProjectScenarioPreview? ScenarioPreview { get; private set; }
    public ProjectScenarioDraftPreview? ScenarioDraftPreview { get; private set; }
    public ProjectScenarioPlan? ActiveScenario => State == "Ready" && _activeScenario is { } plan && _compilation is { } compilation &&
        plan.Input.Data.Equals(compilation.Input.Data) && plan.Input.Source.Data.Equals(Source?.Data) ? plan : null;
    public CompilerResult? ScenarioBaselineResult => ActiveScenario is null ? null : _scenarioBaselineResult;
    public ProjectTestRun? ScenarioBaselineTests => ActiveScenario is null ? null : _scenarioBaselineTests;
    public ProjectSimulationRun? ScenarioBaselineSimulation => ActiveScenario is null ? null : _scenarioBaselineSimulation;
    public ProjectScenarioElapsedScheduleImpact? ScenarioScheduleImpact => _scenarioScheduleRead is { } read &&
        ReferenceEquals(ActiveScenario, read.Scenario) && ReferenceEquals(ScenarioBaselineSimulation, read.Baseline) &&
        ReferenceEquals(SimulationRun, read.Hypothesis) ? read.Impact : null;

    public ProjectScenarioPreview? PreviewScenario(ScenarioId id, ScenarioRevisionId revision, IEnumerable<ProjectScenarioOverride> overrides,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(overrides); cancellationToken.ThrowIfCancellationRequested();
        var lifecycle = _lifecycle; var selection = _selectionGeneration; var generation = ++_scenarioPreviewGeneration;
        ScenarioPreview = null; ScenarioDraftPreview = null; Changed();
        if (_disposed || lifecycle != _lifecycle || selection != _selectionGeneration || generation != _scenarioPreviewGeneration ||
            State != "Ready" || _compilation?.Model is not { } model || _desired is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
        var preview = ProjectScenarios.Prepare(model, _desired.Input.ExpectedInputDigest, id, revision, overrides, cancellationToken);
        if (_disposed || lifecycle != _lifecycle || selection != _selectionGeneration || generation != _scenarioPreviewGeneration) return null;
        ScenarioPreview = preview; Changed();
        if (cancellationToken.IsCancellationRequested)
        {
            if (generation == _scenarioPreviewGeneration && ReferenceEquals(ScenarioPreview, preview))
            {
                ClearScenarioPreview(); Changed();
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        if (!_disposed && lifecycle == _lifecycle && selection == _selectionGeneration && generation == _scenarioPreviewGeneration &&
            ReferenceEquals(ScenarioPreview, preview)) return preview;
        if (ReferenceEquals(ScenarioPreview, preview)) { ClearScenarioPreview(); Changed(); } return null;
    }

    public CompilerHostRequest? LoadScenario(ProjectScenarioPlan plan, long clientGeneration, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(plan); cancellationToken.ThrowIfCancellationRequested();
        if (State != "Ready" || !ReferenceEquals(ScenarioPreview?.Plan, plan) || _desired is null || _compilation is null ||
            clientGeneration <= _lastGeneration || plan.Materialize(_compilation.Input, cancellationToken) is not { } input) return null;
        var result = Result; var tests = TestRun; var simulation = SimulationRun;
        var request = new CompilerHostRequest(new(input), clientGeneration, _desired.Snapshots, _desired.Units, _desired.Calendars);
        BeginSyntheticContext(request, null, null, cancellationToken: cancellationToken);
        if (_disposed || !ReferenceEquals(_desired, request)) return null;
        _activeScenario = plan; _scenarioBaselineResult = result; _scenarioBaselineTests = tests;
        _scenarioBaselineSimulation = simulation; Changed();
        return !_disposed && ReferenceEquals(_desired, request) && ReferenceEquals(_activeScenario, plan) ? request : null;
    }

    /// <summary>Read the modeled schedule change for the owned baseline and current Scenario recording.</summary>
    public bool ReadScenarioScheduleImpact(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var scenario = ActiveScenario;
        var baseline = ScenarioBaselineSimulation;
        var hypothesis = SimulationRun;
        ClearScenarioScheduleImpact();
        var generation = _scenarioScheduleGeneration;
        bool Current() => !_disposed && generation == _scenarioScheduleGeneration && ReferenceEquals(ActiveScenario, scenario) &&
            ReferenceEquals(ScenarioBaselineSimulation, baseline) && ReferenceEquals(SimulationRun, hypothesis);

        Changed();
        cancellationToken.ThrowIfCancellationRequested();
        if (scenario is null || baseline is null || hypothesis is null || !Current()) return false;

        var impact = ProjectScenarioElapsedScheduleImpact.Compare(scenario, baseline, baseline.Digest, hypothesis, hypothesis.Digest,
            cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Current()) return false;
        var read = new ScenarioScheduleRead(scenario, baseline, hypothesis, impact);
        _scenarioScheduleRead = read;
        Exception? publicationFailure = null;
        try { Changed(); }
        catch (Exception error) { publicationFailure = error; }

        var cancelled = cancellationToken.IsCancellationRequested;
        Exception? withdrawalFailure = null;
        if (cancelled && generation == _scenarioScheduleGeneration && ReferenceEquals(_scenarioScheduleRead, read))
        {
            ClearScenarioScheduleImpact();
            var subscribers = PropertyChanged;
            if (subscribers is not null)
            {
                var args = new PropertyChangedEventArgs(null);
                foreach (var subscriber in Delegate.EnumerateInvocationList(subscribers))
                {
                    try { subscriber(this, args); }
                    catch (Exception error) { withdrawalFailure ??= error; }
                }
            }
        }
        if (publicationFailure is not null) ExceptionDispatchInfo.Capture(publicationFailure).Throw();
        if (cancelled) throw new OperationCanceledException("Scenario schedule impact was cancelled.", withdrawalFailure, cancellationToken);
        return Current() && ReferenceEquals(_scenarioScheduleRead, read);
    }

    /// <summary>The caller supplies the currently admitted base; the captured baseline is never substituted for that check.</summary>
    public ProjectScenarioDraftPreview? PreviewScenarioDraft(PartialSemanticModel currentBase, ContentDigest expectedBase, ProposalContext proposal,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(currentBase); cancellationToken.ThrowIfCancellationRequested();
        var lifecycle = _lifecycle; var selection = _selectionGeneration; var generation = ++_scenarioPreviewGeneration;
        ScenarioDraftPreview = null; Changed();
        if (_disposed || lifecycle != _lifecycle || selection != _selectionGeneration || generation != _scenarioPreviewGeneration || ActiveScenario is not { } plan)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
        var preview = ProjectScenarios.ToDraft(plan, currentBase, expectedBase, proposal, cancellationToken);
        if (_disposed || lifecycle != _lifecycle || selection != _selectionGeneration || generation != _scenarioPreviewGeneration) return null;
        ScenarioDraftPreview = preview; Changed();
        if (cancellationToken.IsCancellationRequested)
        {
            if (generation == _scenarioPreviewGeneration && ReferenceEquals(ScenarioDraftPreview, preview))
            {
                ClearScenarioPreview(); Changed();
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
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
        BeginSyntheticContext(request, null, null, cancellationToken: cancellationToken); return !_disposed && ReferenceEquals(_desired, request) ? request : null;
    }
    private void ClearScenarioScheduleImpact() { _scenarioScheduleGeneration++; _scenarioScheduleRead = null; }
    private void ClearScenarioPreview() { _scenarioPreviewGeneration++; ScenarioPreview = null; ScenarioDraftPreview = null; }
    private void ClearScenarioContext()
    {
        ClearScenarioPreview(); _activeScenario = null; _scenarioBaselineResult = null; _scenarioBaselineTests = null;
        _scenarioBaselineSimulation = null;
    }
}
