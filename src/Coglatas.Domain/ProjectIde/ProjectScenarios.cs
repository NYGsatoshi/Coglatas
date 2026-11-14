using System.Text;
using System.Text.Json.Nodes;

namespace Coglatas.Domain.ProjectIde;

public enum ProjectScenarioState { Ready, StaleInput, OutOfScope, Unavailable, Unsupported, InvalidOverride, InvalidContext, ContextConflict, Conflict, ResourceLimit, NoChange }
public enum ProjectScenarioOverrideKind { TaskDuration, TaskInputDefault, MilestoneTarget }

/// <summary>One explicit hypothetical value in an admitted Source, never a permission or operational override.</summary>
public sealed record ProjectScenarioOverride(ProjectLocation? Target, ProjectScenarioOverrideKind Kind, SourceJson? Value, PortId? PortId = null)
{
    internal ProjectLocation RequiredTarget => Target ?? throw new InvalidOperationException("Scenario override target was not validated.");
    internal SourceJson RequiredValue => Value ?? throw new InvalidOperationException("Scenario override value was not validated.");
    internal SourceJson Data => SourceJson.FromObject(new { target = RequiredTarget.Data.Value, kind = Kind.ToString(), portId = PortId?.ToString(), value = RequiredValue.Value });
}
public sealed record ProjectScenarioChange(ProjectScenarioOverride Override, SourceJson? Before);
public sealed record ProjectScenarioPreview(ProjectScenarioState State, ProjectScenarioPlan? Plan);
public sealed record ProjectScenarioDraftPreview(ProjectScenarioState State, ProjectScenarioDraftPlan? Plan);

/// <summary>Immutable local hypothesis bound to its original base. Preparation is not Compile, Test or Simulation PASS.</summary>
public sealed class ProjectScenarioPlan
{
    public CompilerInputManifest Basis { get; }
    public CompilerInputManifest Input { get; }
    public SourceJson Overlay { get; }
    public ContentDigest OverlayDigest { get; }
    public IReadOnlyList<ProjectScenarioChange> Changes { get; }
    internal ProjectScenarioPlan(CompilerInputManifest basis, CompilerInputManifest input, SourceJson overlay, ContentDigest digest,
        IEnumerable<ProjectScenarioChange> changes)
    { Basis = basis; Input = input; Overlay = overlay; OverlayDigest = digest; Changes = new ImmutableListView<ProjectScenarioChange>(changes); }
    public CompilerInputManifest? Materialize(CompilerInputManifest exactBase, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exactBase); cancellationToken.ThrowIfCancellationRequested();
        return exactBase.Data.Equals(Basis.Data) && exactBase.Source.Data.Equals(Basis.Source.Data) ? Input : null;
    }
}

public sealed class ProjectScenarioDraftPlan
{
    public ProjectScenarioPlan Scenario { get; }
    public CompilerInputManifest After { get; }
    public SourceJson Data { get; }
    internal ProjectScenarioDraftPlan(ProjectScenarioPlan scenario, CompilerInputManifest after)
    {
        Scenario = scenario; After = after;
        Data = SourceJson.FromObject(new { format = "coglatas.local-scenario-draft/1", baseInputDigest = scenario.Basis.InputDigest.Data.Value,
            scenarioInputDigest = scenario.Input.InputDigest.Data.Value, afterInputDigest = after.InputDigest.Data.Value,
            scenarioContext = scenario.Input.Source.Context.Data.Value, afterContext = after.Source.Context.Data.Value });
    }
    public CompilerInputManifest? ApplyToDraft(CompilerInputManifest currentBase, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentBase); cancellationToken.ThrowIfCancellationRequested();
        return Scenario.Materialize(currentBase, cancellationToken) is null ? null : After;
    }
}

/// <summary>Local typed hypotheses over one frozen committed/candidate Source. No registry, save, live mutation or authority.</summary>
public static class ProjectScenarios
{
    public const int MaximumOverrides = 128;
    public const int MaximumOverrideBytes = 262_144;
    private static readonly string[] PinFields = ["authorizedExternalSnapshots", "resourceSnapshots", "allocationSnapshots", "observations", "evidence"];

    public static ProjectScenarioPreview Prepare(PartialSemanticModel basis, ContentDigest expectedInput, ScenarioId scenarioId,
        ScenarioRevisionId revision, IEnumerable<ProjectScenarioOverride?> overrides, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(basis); ArgumentNullException.ThrowIfNull(expectedInput); ArgumentNullException.ThrowIfNull(overrides);
        cancellationToken.ThrowIfCancellationRequested();
        ProjectScenarioPreview Refuse(ProjectScenarioState state) => new(state, null);
        var input = basis.Input;
        if (!input.InputDigest.Equals(expectedInput)) return Refuse(ProjectScenarioState.StaleInput);
        if (input.Source.Context.Scenario is not null) return Refuse(ProjectScenarioState.InvalidContext);
        if (basis.Declarations.Count != input.Source.Declarations.Value.GetArrayLength() + input.Source.Documents.Count ||
            basis.Relations.Count != input.Source.Relations.Value.GetArrayLength()) return Refuse(ProjectScenarioState.ResourceLimit);
        _ = scenarioId.ToString(); _ = revision.ToString();
        if (Reserved(basis, scenarioId, revision, cancellationToken)) return Refuse(ProjectScenarioState.ContextConflict);
        var requests = new List<ProjectScenarioOverride>(); var overrideBytes = 0L;
        foreach (var item in overrides)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item is null || item.Target is null || item.Value is null) return Refuse(ProjectScenarioState.InvalidOverride);
            overrideBytes += Encoding.UTF8.GetByteCount(item.Value.CanonicalText) + (long)Encoding.UTF8.GetByteCount(item.Target.Data.CanonicalText);
            if (requests.Count == MaximumOverrides || overrideBytes > MaximumOverrideBytes) return Refuse(ProjectScenarioState.ResourceLimit);
            requests.Add(item);
        }
        if (requests.Count == 0) return Refuse(ProjectScenarioState.NoChange);
        requests.Sort((a, b) => StringComparer.Ordinal.Compare(Key(a), Key(b)));
        if (requests.Select(Key).Distinct(StringComparer.Ordinal).Count() != requests.Count) return Refuse(ProjectScenarioState.Conflict);
        var changed = new Dictionary<EntityId, JsonObject>(); var declarations = new Dictionary<EntityId, PlanDeclaration>();
        var changes = new List<ProjectScenarioChange>(); var anyChange = false;
        foreach (var item in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = Read(basis, item, out var declaration, out var before);
            if (state != ProjectScenarioState.Ready) return Refuse(state);
            var decoded = TypedProjectValueCodec.Decode(item.RequiredValue);
            if (decoded.Status == ValueDecodeStatus.Unsupported) return Refuse(ProjectScenarioState.Unsupported);
            if (decoded.Value is not { } value) return Refuse(ProjectScenarioState.InvalidOverride);
            var type = item.Kind switch
            {
                ProjectScenarioOverrideKind.TaskDuration => ((TaskPlanDeclaration)declaration!).Duration.Type,
                ProjectScenarioOverrideKind.TaskInputDefault => ((TaskPlanDeclaration)declaration!).Ports.Single(port => port.PortId == item.PortId).Type,
                ProjectScenarioOverrideKind.MilestoneTarget => ((MilestonePlanDeclaration)declaration!).Target.Type,
                _ => throw new InvalidOperationException()
            };
            if (!type.Data.Equals(value.Type.Data)) return Refuse(ProjectScenarioState.InvalidOverride);
            // A supplied reference cannot reserve the very Scenario version being created.
            foreach (var child in CompilerValueReferenceTraversal.Values(value, ""))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (child.Value.KnownValue is TypedProjectReference reference &&
                    (SameVersion(reference.Context, scenarioId, revision) || SameVersion(reference.Origin.Context, scenarioId, revision)))
                    return Refuse(ProjectScenarioState.ContextConflict);
            }
            var id = declaration.EntityId;
            if (!changed.TryGetValue(id, out var node)) changed.Add(id, node = JsonNode.Parse(declaration.Data.CanonicalText)!.AsObject());
            if (item.Kind == ProjectScenarioOverrideKind.TaskInputDefault)
                node["payload"]!["ports"]!.AsArray().Single(port => port!["portId"]!.GetValue<string>() == item.PortId!.Value.ToString())!["defaultValue"] = JsonNode.Parse(item.RequiredValue.CanonicalText);
            else node["payload"]![item.Kind == ProjectScenarioOverrideKind.TaskDuration ? "duration" : "target"] = JsonNode.Parse(item.RequiredValue.CanonicalText);
            changes.Add(new(item, before)); anyChange |= !item.RequiredValue.Equals(before);
        }
        if (!anyChange) return Refuse(ProjectScenarioState.NoChange);
        try
        {
            foreach (var pair in changed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var document = basis.Declarations.Single(item => item.Declaration?.EntityId == pair.Key).DocumentId;
                var decoded = PlanDeclarationReader.Read(SourceJson.FromObject(pair.Value, input.Source.Data.Limits), document);
                if (decoded.Status == ValueDecodeStatus.Unsupported) return Refuse(ProjectScenarioState.Unsupported);
                if (decoded.Declaration is null) return Refuse(ProjectScenarioState.InvalidOverride);
                declarations.Add(pair.Key, decoded.Declaration);
            }
            var overlay = SourceJson.FromObject(new { format = "coglatas.local-scenario-overlay/1", baseInputDigest = input.InputDigest.Data.Value,
                baseSourceDigest = input.Source.Digest.Data.Value, baseContext = input.Source.Context.Data.Value,
                overrides = requests.Select(item => item.Data.Value).ToArray() }, input.Source.Data.Limits);
            var digest = ContentDigest.Compute("coglatas.local-scenario-overlay/1", overlay.ToCanonicalBytes());
            var context = SourceRevisionContext.Hypothetical(new(scenarioId, revision, input.Source.Context, digest));
            var source = DraftSourceTransition.Recontextualize(basis, context, null, null, cancellationToken, replacements: declarations);
            var settings = JsonNode.Parse(input.Settings.CanonicalText)!.AsObject();
            settings["scenario"] = JsonSerializerNode(new { baseContext = input.Source.Context.Data.Value, overlayDigest = digest.Data.Value,
                assumptions = requests.Select(item => item.Data.Value).ToArray() });
            var result = new CompilerInputManifest(source, SourceJson.FromObject(settings, input.Settings.Limits));
            cancellationToken.ThrowIfCancellationRequested();
            return new(ProjectScenarioState.Ready, new(input, result, overlay, digest, changes));
        }
        catch (UnsupportedSourceException) { return Refuse(ProjectScenarioState.Unsupported); }
        catch (DraftSourceLimitException) { return Refuse(ProjectScenarioState.ResourceLimit); }
        catch (FormatException) { return Refuse(ProjectScenarioState.ResourceLimit); }
    }

    public static ProjectScenarioPreview Revise(ProjectScenarioPlan previous, PartialSemanticModel basis, ContentDigest expectedInput,
        ScenarioRevisionId revision, IEnumerable<ProjectScenarioOverride> overrides, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(previous); ArgumentNullException.ThrowIfNull(basis); cancellationToken.ThrowIfCancellationRequested();
        if (previous.Materialize(basis.Input, cancellationToken) is null) return new(ProjectScenarioState.StaleInput, null);
        var identity = previous.Input.Source.Context.Scenario!;
        return revision == identity.ScenarioRevisionId ? new(ProjectScenarioState.ContextConflict, null) :
            Prepare(basis, expectedInput, identity.ScenarioId, revision, overrides, cancellationToken);
    }

    public static ProjectScenarioPreview Rebase(ProjectScenarioPlan previous, PartialSemanticModel newBasis, ContentDigest expectedInput,
        ScenarioRevisionId revision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(previous); ArgumentNullException.ThrowIfNull(newBasis); ArgumentNullException.ThrowIfNull(expectedInput);
        cancellationToken.ThrowIfCancellationRequested();
        if (!newBasis.Input.InputDigest.Equals(expectedInput)) return new(ProjectScenarioState.StaleInput, null);
        if (newBasis.Input.Source.Context.Branch != previous.Basis.Source.Context.Branch) return new(ProjectScenarioState.OutOfScope, null);
        if (newBasis.Input.Source.Context.Equals(previous.Basis.Source.Context) && !newBasis.Input.Source.Data.Equals(previous.Basis.Source.Data))
            return new(ProjectScenarioState.Conflict, null);
        var identity = previous.Input.Source.Context.Scenario!;
        if (revision == identity.ScenarioRevisionId) return new(ProjectScenarioState.ContextConflict, null);
        var overrides = new List<ProjectScenarioOverride>();
        foreach (var change in previous.Changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var old = change.Override;
            var target = ProjectLocation.Create(old.RequiredTarget.DocumentId, newBasis.Input.Source.Context, entityId: old.RequiredTarget.EntityId);
            try
            {
                var before = change.Before is null ? null : DraftSourceTransition.RebaseValueOrigins(change.Before,
                    previous.Basis.Source.Context, newBasis.Input.Source.Context, cancellationToken);
                var value = DraftSourceTransition.RebaseValueOrigins(old.RequiredValue, previous.Basis.Source.Context, newBasis.Input.Source.Context, cancellationToken);
                var item = old with { Target = target, Value = value };
                if (Read(newBasis, item, out _, out var current) != ProjectScenarioState.Ready || !Equals(current, before))
                    return new(ProjectScenarioState.Conflict, null);
                overrides.Add(item);
            }
            catch (UnsupportedSourceException) { return new(ProjectScenarioState.Unsupported, null); }
            catch (DraftSourceLimitException) { return new(ProjectScenarioState.ResourceLimit, null); }
        }
        return Prepare(newBasis, expectedInput, identity.ScenarioId, revision, overrides, cancellationToken);
    }

    public static ProjectScenarioDraftPreview ToDraft(ProjectScenarioPlan scenario, PartialSemanticModel currentBase,
        ContentDigest expectedInput, ProposalContext proposal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario); ArgumentNullException.ThrowIfNull(currentBase); ArgumentNullException.ThrowIfNull(expectedInput);
        ArgumentNullException.ThrowIfNull(proposal); cancellationToken.ThrowIfCancellationRequested();
        ProjectScenarioDraftPreview Refuse(ProjectScenarioState state) => new(state, null);
        if (!currentBase.Input.InputDigest.Equals(expectedInput) || scenario.Materialize(currentBase.Input, cancellationToken) is null)
            return Refuse(ProjectScenarioState.StaleInput);
        var context = currentBase.Input.Source.Context;
        if (context.CommittedRevision is { } committed)
        { if (proposal.BaseRevision != committed) return Refuse(ProjectScenarioState.InvalidContext); }
        else if (context.Proposal is not { } current || proposal.ProposalId != current.ProposalId || proposal.BaseRevision != current.BaseRevision ||
            proposal.CapturedTargetHead != current.CapturedTargetHead || proposal.CandidateRevisionId == current.CandidateRevisionId)
            return Refuse(ProjectScenarioState.InvalidContext);
        var next = SourceRevisionContext.Candidate(proposal);
        try
        {
            if (ProjectDraftRename.ContextAlreadyKnown(currentBase, next, cancellationToken)) return Refuse(ProjectScenarioState.ContextConflict);
            var model = ProjectSemanticBinder.Bind(scenario.Input, cancellationToken: cancellationToken);
            if (model.Declarations.Count != scenario.Input.Source.Declarations.Value.GetArrayLength() + scenario.Input.Source.Documents.Count ||
                model.Relations.Count != scenario.Input.Source.Relations.Value.GetArrayLength()) return Refuse(ProjectScenarioState.ResourceLimit);
            // Includes references introduced by the hypothetical values, not only the original base.
            if (ProjectDraftRename.ContextAlreadyKnown(model, next, cancellationToken)) return Refuse(ProjectScenarioState.ContextConflict);
            var source = DraftSourceTransition.Recontextualize(model, next, null, null, cancellationToken);
            var after = new CompilerInputManifest(source, scenario.Basis.Settings);
            cancellationToken.ThrowIfCancellationRequested();
            return new(ProjectScenarioState.Ready, new(scenario, after));
        }
        catch (UnsupportedSourceException) { return Refuse(ProjectScenarioState.Unsupported); }
        catch (DraftSourceLimitException) { return Refuse(ProjectScenarioState.ResourceLimit); }
        catch (FormatException) { return Refuse(ProjectScenarioState.ResourceLimit); }
    }

    private static JsonNode JsonSerializerNode(object value) => JsonNode.Parse(SourceJson.FromObject(value).CanonicalText)!;
    private static string Key(ProjectScenarioOverride value) => value.RequiredTarget.DocumentId + "/" + value.RequiredTarget.EntityId + "/" + value.Kind + "/" + value.PortId;
    private static ProjectScenarioState Read(PartialSemanticModel basis, ProjectScenarioOverride item, out PlanDeclaration? declaration, out SourceJson? before)
    {
        declaration = null; before = null; var target = item.RequiredTarget;
        if (!target.Context.Equals(basis.Input.Source.Context)) return ProjectScenarioState.OutOfScope;
        if (target.EntityId is null || target.RelationId is not null || target.PropertyPath.Length != 0 || target.TextSpan is not null)
            return ProjectScenarioState.InvalidOverride;
        var matches = basis.Declarations.Where(value => value.Declaration?.EntityId == target.EntityId).Take(2).ToArray();
        if (matches.Length != 1 || matches[0].DocumentId != target.DocumentId) return ProjectScenarioState.Unavailable;
        declaration = matches[0].Declaration;
        if (item.Kind == ProjectScenarioOverrideKind.TaskDuration && declaration is TaskPlanDeclaration duration && item.PortId is null)
            before = duration.Duration.Data;
        else if (item.Kind == ProjectScenarioOverrideKind.TaskInputDefault && declaration is TaskPlanDeclaration task && item.PortId is { } portId)
        {
            var port = task.Ports.SingleOrDefault(value => value.PortId == portId);
            if (port is null || port.Direction != PortDirection.Input) return ProjectScenarioState.Unavailable;
            before = port.DefaultValue?.Data;
        }
        else if (item.Kind == ProjectScenarioOverrideKind.MilestoneTarget && declaration is MilestonePlanDeclaration milestone && item.PortId is null)
            before = milestone.Target.Data;
        else return ProjectScenarioState.Unsupported;
        return ProjectScenarioState.Ready;
    }
    private static bool SameVersion(SourceRevisionContext context, ScenarioId id, ScenarioRevisionId revision) =>
        context.Scenario is { } scenario && scenario.ScenarioId == id && scenario.ScenarioRevisionId == revision;
    private static bool Reserved(PartialSemanticModel model, ScenarioId id, ScenarioRevisionId revision, CancellationToken token)
    {
        bool Match(SourceRevisionContext context) => SameVersion(context, id, revision);
        foreach (var field in PinFields)
            foreach (var pin in model.Input.Settings.Value.GetProperty(field).EnumerateArray())
            { token.ThrowIfCancellationRequested(); if (Match(SourceRevisionContext.Parse(SourceJson.FromElement(pin.GetProperty("context"))))) return true; }
        foreach (var snapshot in model.BoundSnapshots)
        { token.ThrowIfCancellationRequested(); if (Match(snapshot.Source.Context)) return true; }
        foreach (var reference in model.References)
        { token.ThrowIfCancellationRequested(); if (Match(reference.Origin.Context)) return true; }
        foreach (var item in model.Declarations)
        {
            token.ThrowIfCancellationRequested();
            if (item.Declaration is RequirementPlanDeclaration requirement && Match(requirement.Target.Context)) return true;
            if (item.Declaration is ProjectTestPlanDeclaration test && test.Assertions.Any(assertion => Match(assertion.Target.Context))) return true;
            if (item.Declaration is not TaskPlanDeclaration task) continue;
            foreach (var port in task.Ports)
                if (port.DefaultValue is { } value)
                    foreach (var child in CompilerValueReferenceTraversal.Values(value, ""))
                    { token.ThrowIfCancellationRequested(); if (child.Value.KnownValue is TypedProjectReference target && (Match(target.Context) || Match(target.Origin.Context))) return true; }
        }
        foreach (var item in model.Relations)
        {
            token.ThrowIfCancellationRequested();
            if (item.Relation is { } relation && (Match(relation.Source.Reference.Context) || Match(relation.Target.Reference.Context) ||
                relation.Payload is ConstraintRelationPayload constraint && Match(constraint.PredicateReference.Context))) return true;
        }
        return false;
    }
}
