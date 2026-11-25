using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Runtime;

public static class ProjectScenarioOperations
{
    public static SecurityOperationRef Save { get; } = new("coglatas.projectide.scenario.save");
    public static SecurityOperationRef Read { get; } = new("coglatas.projectide.scenario.read");
    public static SecurityOperationRef ReadAssumptions { get; } = new("coglatas.projectide.scenario.read-assumptions");
    public static SecurityOperationRef CompareRuns { get; } = new("coglatas.projectide.scenario.compare-runs");
}

public sealed record ProjectScenarioSource(SecuritySourceIdentitySnapshot Identity, string Digest)
{
    public static ProjectScenarioSource Capture(CompilerInputManifest input) =>
        new(SecuritySourceIdentitySnapshot.Capture(input.Source.Context, input.InputDigest), input.Source.Digest.Value);
}

/// <summary>Durable identity independent of the access to its private assumption values.</summary>
public sealed record SavedProjectScenario(
    ScenarioId Id, ScenarioRevisionId Revision, ProjectScenarioSource Basis,
    ProjectScenarioSource Input, DateTimeOffset SavedAtUtc);

public sealed record ProjectScenarioSummary(ScenarioId Id, ScenarioRevisionId Revision, DateTimeOffset SavedAtUtc);

public sealed record SavedScenarioComparison(
    bool SourceChanged, SimulationRunComparisonState RunState,
    bool? SameRunLimits, long? ObservedElapsedDelta, string ComparisonDigest);

public interface IProjectScenarioStore
{
    bool CanStartSave();
    Task StageAsync(SavedProjectScenario scenario, string canonicalOverlay, CancellationToken cancellationToken);
    Task<SavedProjectScenario?> FindAsync(Guid tenantId, Guid projectId, ScenarioId scenarioId, CancellationToken cancellationToken);
    Task<string?> FindOverlayAsync(Guid tenantId, Guid projectId, ScenarioId scenarioId, ScenarioRevisionId revision,
        CancellationToken cancellationToken);
}

/// <summary>Development use cases over trusted, host-owned immutable inputs and original runs.</summary>
public sealed class ProjectScenarioRuntime(ProjectIdeOperationAccess access, IProjectScenarioStore store,
    ICreateIdempotencyCoordinator idempotency, IAuditLogger audit, IClock clock)
{
    private const string OverlayFormat = "coglatas.local-scenario-overlay/1";

    public async Task<ProjectScenarioSummary?> SaveAsync(PartialSemanticModel basis, ContentDigest expectedInput,
        IEnumerable<ProjectScenarioOverride?> overrides, string requestIdentity, CancellationToken cancellationToken)
    {
        if (requestIdentity is not { Length: >= 8 and <= 128 } || string.IsNullOrWhiteSpace(requestIdentity) ||
            !requestIdentity.All(character => character is >= ' ' and <= '~'))
            return null;

        var viewer = access.CaptureViewer();
        var baseSource = ProjectScenarioSource.Capture(basis.Input);
        if (viewer is null || !await AllowedAsync(viewer, ProjectScenarioOperations.Save, baseSource, cancellationToken))
            return null;

        var id = ScenarioId.New();
        var revision = ScenarioRevisionId.New();
        var plan = ProjectScenarios.Prepare(basis, expectedInput, id, revision, overrides, cancellationToken).Plan;
        if (plan is null)
            return null;

        var scenario = new SavedProjectScenario(id, revision, baseSource, ProjectScenarioSource.Capture(plan.Input), clock.UtcNow);
        if (!store.CanStartSave())
            return null;

        var claim = new CreateIdempotencyContext(viewer.TenantId, viewer.UserId, ProjectScenarioOperations.Save.OperationId,
            requestIdentity, plan.OverlayDigest.Value, "ProjectIdeScenario", id.Value);
        try
        {
            var result = await idempotency.ExecuteAsync(claim,
                async token =>
                {
                    if (!await AllowedAsync(viewer, ProjectScenarioOperations.Save, scenario.Input, token))
                        throw new ScenarioAccessChangedException();

                    await store.StageAsync(scenario, plan.Overlay.CanonicalText, token);
                    await audit.LogAsync(new(viewer.UserId, "Security.ProjectIde.ScenarioSaved", "ProjectIdeScenario", id.Value,
                        ProjectId: baseSource.Identity.ProjectId, TenantId: viewer.TenantId), token);
                    if (!await AllowedAsync(viewer, ProjectScenarioOperations.Save, baseSource, token) ||
                        !await AllowedAsync(viewer, ProjectScenarioOperations.Save, scenario.Input, token))
                        throw new ScenarioAccessChangedException();

                    return scenario;
                },
                async (existingId, token) =>
                {
                    var existing = await FindAsync(viewer, baseSource, new(existingId), ProjectScenarioOperations.Save, token);
                    return existing?.Input.Identity.OverlayDigest == plan.OverlayDigest.Value ? existing : null;
                }, cancellationToken);
            return result.Value is { } saved &&
                await AllowedAsync(viewer, ProjectScenarioOperations.Save, baseSource, cancellationToken) &&
                await AllowedAsync(viewer, ProjectScenarioOperations.Save, saved.Input, cancellationToken)
                    ? Summary(saved) : null;
        }
        catch (ScenarioAccessChangedException)
        {
            return null;
        }
    }

    public async Task<ProjectScenarioSummary?> FindAsync(CompilerInputManifest exactBasis, ScenarioId scenarioId,
        CancellationToken cancellationToken)
    {
        var viewer = access.CaptureViewer();
        if (viewer is null)
            return null;

        var scenario = await FindAsync(viewer, ProjectScenarioSource.Capture(exactBasis), scenarioId,
            ProjectScenarioOperations.Read, cancellationToken);
        return scenario is null ? null : Summary(scenario);
    }

    public async Task<SourceJson?> ReadAssumptionsAsync(CompilerInputManifest exactBasis, ScenarioId scenarioId,
        CancellationToken cancellationToken)
    {
        var viewer = access.CaptureViewer();
        if (viewer is null)
            return null;

        var baseSource = ProjectScenarioSource.Capture(exactBasis);
        var scenario = await FindAsync(viewer, baseSource, scenarioId, ProjectScenarioOperations.ReadAssumptions, cancellationToken);
        return scenario is null ? null : await ReadOverlayAsync(viewer, scenario, cancellationToken);
    }

    public async Task<CompilerInputManifest?> MaterializeAsync(PartialSemanticModel exactBasis, ScenarioId scenarioId,
        CancellationToken cancellationToken)
    {
        var viewer = access.CaptureViewer();
        if (viewer is null)
            return null;

        var baseSource = ProjectScenarioSource.Capture(exactBasis.Input);
        var scenario = await FindAsync(viewer, baseSource, scenarioId, ProjectScenarioOperations.ReadAssumptions, cancellationToken);
        if (scenario is null || await ReadOverlayAsync(viewer, scenario, cancellationToken) is not { } overlay)
            return null;

        try
        {
            var restored = ProjectScenarios.Prepare(exactBasis, exactBasis.Input.InputDigest, scenario.Id,
                scenario.Revision, DecodeOverrides(overlay), cancellationToken).Plan;
            if (restored is null || !restored.Overlay.Equals(overlay) || ProjectScenarioSource.Capture(restored.Input) != scenario.Input)
                return null;

            return await AllowedAsync(viewer, ProjectScenarioOperations.ReadAssumptions, baseSource, cancellationToken) &&
                await AllowedAsync(viewer, ProjectScenarioOperations.ReadAssumptions, scenario.Input, cancellationToken)
                    ? restored.Input : null;
        }
        catch (Exception error) when (error is FormatException or JsonException or UnsupportedSourceException)
        {
            return null;
        }
    }

    private async Task<SourceJson?> ReadOverlayAsync(ProjectIdeViewerContext viewer, SavedProjectScenario scenario,
        CancellationToken cancellationToken)
    {
        var stored = await store.FindOverlayAsync(viewer.TenantId, scenario.Basis.Identity.ProjectId, scenario.Id,
            scenario.Revision, cancellationToken);
        if (stored is null)
            return null;

        try
        {
            var overlay = SourceJson.Parse(stored);
            if (overlay.CanonicalText != stored ||
                ContentDigest.Compute(OverlayFormat, overlay.ToCanonicalBytes()).Value != scenario.Input.Identity.OverlayDigest)
                return null;

            return await AllowedAsync(viewer, ProjectScenarioOperations.ReadAssumptions, scenario.Basis, cancellationToken) &&
                await AllowedAsync(viewer, ProjectScenarioOperations.ReadAssumptions, scenario.Input, cancellationToken)
                    ? overlay : null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<SavedScenarioComparison?> CompareAsync(ScenarioId scenarioId, ProjectSimulationRun baseline,
        ContentDigest expectedBaseline, ProjectSimulationRun hypothesis, ContentDigest expectedHypothesis,
        CancellationToken cancellationToken)
    {
        var viewer = access.CaptureViewer();
        if (viewer is null)
            return null;

        var baseSource = ProjectScenarioSource.Capture(baseline.Manifest.Plan.Input);
        var scenario = await FindAsync(viewer, baseSource, scenarioId, ProjectScenarioOperations.CompareRuns, cancellationToken);
        if (scenario is null || scenario.Input != ProjectScenarioSource.Capture(hypothesis.Manifest.Plan.Input) ||
            !await CanReadContributionsAsync(viewer, baseline, hypothesis, cancellationToken))
            return null;

        var comparison = ProjectSimulationRunComparison.Compare(baseline, expectedBaseline, hypothesis, expectedHypothesis, cancellationToken);
        if (comparison.State == SimulationRunComparisonState.Refused ||
            !await CanReadContributionsAsync(viewer, baseline, hypothesis, cancellationToken) ||
            !await AllowedAsync(viewer, ProjectScenarioOperations.CompareRuns, baseSource, cancellationToken) ||
            !await AllowedAsync(viewer, ProjectScenarioOperations.CompareRuns, scenario.Input, cancellationToken))
            return null;

        return new(baseSource.Digest != scenario.Input.Digest, comparison.State,
            comparison.SameLimits, comparison.ObservedElapsedDelta, comparison.Digest.Value);
    }

    private async Task<SavedProjectScenario?> FindAsync(ProjectIdeViewerContext viewer, ProjectScenarioSource exactBasis,
        ScenarioId scenarioId, SecurityOperationRef operation, CancellationToken cancellationToken)
    {
        if (scenarioId.Value == Guid.Empty || !await AllowedAsync(viewer, operation, exactBasis, cancellationToken))
            return null;

        var scenario = await store.FindAsync(viewer.TenantId, exactBasis.Identity.ProjectId, scenarioId, cancellationToken);
        if (scenario is null || scenario.Basis != exactBasis || scenario.Id != scenarioId ||
            scenario.Input.Identity.ScenarioId != scenario.Id.Value || scenario.Input.Identity.ScenarioRevisionId != scenario.Revision.Value ||
            scenario.Input.Identity.TenantId != exactBasis.Identity.TenantId ||
            scenario.Input.Identity.ProjectId != exactBasis.Identity.ProjectId ||
            scenario.Input.Identity.BranchId != exactBasis.Identity.BranchId ||
            scenario.Input.Identity.OverlayDigest is null)
            return null;

        return await AllowedAsync(viewer, operation, scenario.Input, cancellationToken) &&
            await AllowedAsync(viewer, operation, exactBasis, cancellationToken) ? scenario : null;
    }

    private async Task<bool> CanReadContributionsAsync(ProjectIdeViewerContext viewer, ProjectSimulationRun baseline,
        ProjectSimulationRun hypothesis, CancellationToken cancellationToken)
    {
        foreach (var plan in new[] { baseline.Manifest.Plan, hypothesis.Manifest.Plan })
        {
            if (plan.Scope.Regions.Count > ProjectSimulationRunComparison.MaximumCapturedInputsPerRun + 1)
                return false;

            foreach (var item in plan.Scope.Regions)
            {
                var source = item.Binding.Source;
                if (source.Context.Equals(plan.Input.Source.Context) && source.Digest.Equals(plan.Input.Source.Digest))
                    continue;

                var identity = SecuritySourceIdentitySnapshot.Capture(source.Context, source.Digest);
                if (!await access.IsAllowedAsync(viewer, ProjectScenarioOperations.CompareRuns, identity, source.Digest.Value, cancellationToken))
                    return false;
            }
        }

        return true;
    }

    private Task<bool> AllowedAsync(ProjectIdeViewerContext viewer, SecurityOperationRef operation,
        ProjectScenarioSource source, CancellationToken cancellationToken) =>
        access.IsAllowedAsync(viewer, operation, source.Identity, source.Digest, cancellationToken);

    private static ProjectScenarioSummary Summary(SavedProjectScenario scenario) =>
        new(scenario.Id, scenario.Revision, scenario.SavedAtUtc);

    private static IEnumerable<ProjectScenarioOverride> DecodeOverrides(SourceJson overlay)
    {
        foreach (var item in overlay.Value.GetProperty("overrides").EnumerateArray())
        {
            var kindText = item.GetProperty("kind").GetString();
            if (!Enum.TryParse<ProjectScenarioOverrideKind>(kindText, out var kind) || kind.ToString() != kindText)
                throw new FormatException("Invalid persisted Scenario override kind.");

            var target = ProjectLocation.Parse(SourceJson.Parse(item.GetProperty("target").GetRawText()));
            var value = SourceJson.Parse(item.GetProperty("value").GetRawText());
            var port = item.GetProperty("portId").GetString();
            yield return new(target, kind, value, port is null ? null : PortId.Parse(port));
        }
    }

    private sealed class ScenarioAccessChangedException : Exception;
}
