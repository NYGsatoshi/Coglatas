using System.Text;

namespace Coglatas.Domain.ProjectIde;

public sealed record ProjectScenarioAppliedValue(ProjectLocation Target, ProjectScenarioOverrideKind Kind, PortId? PortId,
    SourceJson Assumption, SourceJson Value, IReadOnlyList<SourceJson> OriginEdits)
{
    public ContentDigest AssumptionDigest => ContentDigest.Compute("coglatas.local-scenario-assumption/1", Assumption.ToCanonicalBytes());
    public ContentDigest ValueDigest => ContentDigest.Compute("coglatas.local-scenario-value/1", Value.ToCanonicalBytes());
}
public sealed record ProjectScenarioAppliedValuesPreview(ProjectScenarioState State, ProjectScenarioAppliedValues? Projection);

/// <summary>Exact Core-applied values, separate from declared assumptions and execution verdicts. No producer attestation or authority.</summary>
public sealed class ProjectScenarioAppliedValues
{
    public const int MaximumOriginEdits = 4096;
    public const string Format = "coglatas.local-scenario-applied-values/1";
    public CompilerInputManifest Input { get; }
    public IReadOnlyList<ProjectScenarioAppliedValue> Values { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }
    private ProjectScenarioAppliedValues(ProjectScenarioPlan scenario, CompilerInputManifest input, IEnumerable<ProjectScenarioAppliedValue> values)
    {
        Input = input; Values = new ImmutableListView<ProjectScenarioAppliedValue>(values);
        Data = SourceJson.FromObject(new { format = Format, overlayDigest = scenario.OverlayDigest.Data.Value,
            baseInputDigest = scenario.Basis.InputDigest.Data.Value, scenarioInputDigest = scenario.Input.InputDigest.Data.Value,
            afterInputDigest = input.InputDigest.Data.Value, afterSourceDigest = input.Source.Digest.Data.Value,
            afterContext = input.Source.Context.Data.Value,
            values = Values.Select(item => new { target = item.Target.Data.Value, kind = item.Kind.ToString(), portId = item.PortId?.ToString(),
                originEdits = item.OriginEdits.Select(edit => edit.Value).ToArray(),
                assumption = new { text = item.Assumption.CanonicalText, digest = item.AssumptionDigest.Data.Value }, value = new { text = item.Value.CanonicalText, digest = item.ValueDigest.Data.Value } }).ToArray()
        }, input.Source.Data.Limits);
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }

    public static ProjectScenarioAppliedValuesPreview Read(ProjectScenarioPlan scenario, CompilerInputManifest exactScenario,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario); ArgumentNullException.ThrowIfNull(exactScenario);
        return Read(scenario, scenario.Input, exactScenario, cancellationToken);
    }
    public static ProjectScenarioAppliedValuesPreview Read(ProjectScenarioDraftPlan draft, CompilerInputManifest exactDraft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft); ArgumentNullException.ThrowIfNull(exactDraft);
        return Read(draft.Scenario, draft.After, exactDraft, cancellationToken);
    }
    private static ProjectScenarioAppliedValuesPreview Read(ProjectScenarioPlan scenario, CompilerInputManifest expected,
        CompilerInputManifest supplied, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!supplied.Data.Equals(expected.Data) || !supplied.Source.Data.Equals(expected.Source.Data))
            return new(ProjectScenarioState.StaleInput, null);
        try
        {
            var declarations = PlanDeclarationReader.Read(expected.Source);
            var values = new List<ProjectScenarioAppliedValue>(); long bytes = 0;
            foreach (var change in scenario.Changes)
            {
                cancellationToken.ThrowIfCancellationRequested(); var item = change.Override;
                var matches = declarations.Where(row => row.Declaration?.EntityId == item.Target.EntityId && row.DocumentId == item.Target.DocumentId).Take(2).ToArray();
                if (matches.Length != 1) return new(ProjectScenarioState.Unavailable, null);
                var declaration = matches[0].Declaration; TypedProjectValue? value;
                if (item.Kind == ProjectScenarioOverrideKind.TaskDuration && declaration is TaskPlanDeclaration duration && item.PortId is null)
                    value = duration.Duration;
                else if (item.Kind == ProjectScenarioOverrideKind.MilestoneTarget && declaration is MilestonePlanDeclaration milestone && item.PortId is null)
                    value = milestone.Target;
                else if (item.Kind == ProjectScenarioOverrideKind.TaskInputDefault && declaration is TaskPlanDeclaration task && item.PortId is { } portId)
                    value = task.Ports.SingleOrDefault(port => port.PortId == portId && port.Direction == PortDirection.Input)?.DefaultValue;
                else return new(ProjectScenarioState.Unsupported, null);
                if (value is null) return new(ProjectScenarioState.Unavailable, null);
                var target = ProjectLocation.Create(item.Target.DocumentId, expected.Source.Context, entityId: item.Target.EntityId);
                bytes += Encoding.UTF8.GetByteCount(value.Data.CanonicalText) + (long)Encoding.UTF8.GetByteCount(target.Data.CanonicalText);
                if (values.Count == ProjectScenarios.MaximumOverrides || bytes > ProjectScenarios.MaximumOverrideBytes)
                    return new(ProjectScenarioState.ResourceLimit, null);
                var edits = new List<SourceJson>();
                var transformed = DraftSourceTransition.RebaseValueOrigins(item.Value, scenario.Basis.Source.Context, expected.Source.Context, cancellationToken, edits);
                if (!transformed.Equals(value.Data)) return new(ProjectScenarioState.Conflict, null);
                values.Add(new(target, item.Kind, item.PortId, item.Data, value.Data, new ImmutableListView<SourceJson>(edits)));
            }
            var projection = new ProjectScenarioAppliedValues(scenario, expected, values);
            cancellationToken.ThrowIfCancellationRequested(); return new(ProjectScenarioState.Ready, projection);
        }
        catch (DraftSourceLimitException) { return new(ProjectScenarioState.ResourceLimit, null); }
        catch (UnsupportedSourceException) { return new(ProjectScenarioState.Unsupported, null); }
        catch (FormatException) { return new(ProjectScenarioState.ResourceLimit, null); }
    }
}
