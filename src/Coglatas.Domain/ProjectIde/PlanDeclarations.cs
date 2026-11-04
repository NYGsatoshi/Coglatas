using System.Collections.ObjectModel;

namespace Coglatas.Domain.ProjectIde;

/// <summary>Read-only typed view of canonical Source. No duplicate writable Task or operational state.</summary>
public abstract class PlanDeclaration
{
    public EntityId EntityId { get; }
    public DocumentId DocumentId { get; }
    public string Kind { get; }
    public string? DisplayName { get; }
    public SourceJson Data { get; }
    public SourceJson Payload { get; }

    private protected PlanDeclaration(SourceJson data, DocumentId documentId)
    {
        Data = data; DocumentId = documentId;
        EntityId = SourceFields.Identity<EntityId>(data.Value, "entityId");
        Kind = SourceFields.Token(data.Value, "kind");
        if (SourceFields.PositiveInteger(data.Value, "schemaVersion") != 1) throw new UnsupportedSourceException("Unsupported declaration version.");
        if (data.Value.TryGetProperty("displayName", out var display)) DisplayName = ValueFields.String(display);
        Payload = SourceJson.FromElement(SourceFields.Object(data.Value, "payload"), data.Limits);
        if (ValueFields.HasUnknown(data.Value, "entityId", "documentId", "kind", "schemaVersion", "displayName", "payload"))
            throw new UnsupportedSourceException("Unknown declaration members are preserved without semantic assumptions.");
    }

    private protected void Fields(params string[] names)
    {
        if (ValueFields.HasUnknown(Payload.Value, names)) throw new UnsupportedSourceException("Unknown declaration payload members.");
    }

    private protected SourceJson Child(string name) => SourceJson.FromElement(SourceFields.Object(Payload.Value, name), Data.Limits);
    private protected IReadOnlyList<EntityId> EntityIds(string name) => DeclarationFields.Identities<EntityId>(Payload, name);
}

public sealed class ProjectPlanDeclaration : PlanDeclaration
{
    public IReadOnlyList<EntityId> Roots { get; }
    public CalendarReference? Calendar { get; }
    internal ProjectPlanDeclaration(SourceJson data, DocumentId document) : base(data, document)
    {
        Fields("roots", "calendar");
        Roots = Payload.Value.TryGetProperty("roots", out _) ? EntityIds("roots") : Array.Empty<EntityId>();
        if (Payload.Value.TryGetProperty("calendar", out _)) Calendar = DeclarationFields.Calendar(Child("calendar"));
    }
}

public sealed class TaskPlanDeclaration : PlanDeclaration
{
    public TypedProjectValue Duration { get; }
    public IReadOnlyList<PortDefinition> Ports { get; }
    internal TaskPlanDeclaration(SourceJson data, DocumentId document) : base(data, document)
    {
        Fields("duration", "ports");
        Duration = DeclarationFields.Value(Child("duration"), "coglatas.elapsed-duration", "coglatas.working-duration");
        if (Duration.State == ValueState.Null || Duration.KnownValue is ElapsedDuration { Ticks: < 0 } or WorkingDuration { Ticks: < 0 })
            throw new FormatException("Task work duration must be nonnegative or explicitly Unknown.");
        var ports = SourceFields.Array(Payload.Value, "ports").EnumerateArray()
            .Select(port => new PortDefinition(SourceJson.FromElement(port, Data.Limits))).ToArray();
        if (ports.Select(port => port.PortId).Distinct().Count() != ports.Length) throw new FormatException("Duplicate PortId.");
        Ports = new ReadOnlyCollection<PortDefinition>(ports);
    }
}

public sealed class MilestonePlanDeclaration : PlanDeclaration
{
    public TypedProjectValue Target { get; }
    internal MilestonePlanDeclaration(SourceJson data, DocumentId document) : base(data, document)
    {
        if (Payload.Value.TryGetProperty("duration", out _)) throw new FormatException("Milestone is a zero-work target.");
        Fields("target");
        Target = DeclarationFields.Value(Child("target"), "coglatas.local-date", "coglatas.instant");
        if (Target.State == ValueState.Null) throw new FormatException("Milestone target cannot be Null.");
        // Membership is represented only by Slice 3 contains relations, never a duplicate Task list.
    }
}

public sealed class RequirementPlanDeclaration : PlanDeclaration
{
    public PlanEntityReference Target { get; }
    public PurePredicate Condition { get; }
    public string EvidenceKind { get; }
    internal RequirementPlanDeclaration(SourceJson data, DocumentId document) : base(data, document)
    {
        Fields("target", "condition", "evidenceKind");
        Target = new(Child("target"));
        Condition = PurePredicate.Parse(Child("condition"));
        EvidenceKind = SourceFields.Token(Payload.Value, "evidenceKind");
        if (EvidenceKind is not ("coglatas.model-proof" or "coglatas.observation" or "coglatas.human-approval" or "coglatas.heuristic"))
            throw new UnsupportedSourceException("Unknown required evidence kind.");
        // A parsed condition or link never establishes eligible evidence or Requirement satisfaction.
    }
}

public enum ResourceBehavior { Reusable, Consumable }

/// <summary>Project binding to a Tenant-owned snapshot; shared capacity/stock is not duplicated in Source.</summary>
public sealed class ResourceBindingDeclaration : PlanDeclaration
{
    public TenantId TenantId { get; }
    public ResourceId ResourceId { get; }
    public string SnapshotVersion { get; }
    public ContentDigest SnapshotDigest { get; }
    public string ResourceKind { get; }
    public ResourceBehavior Behavior { get; }
    public CalendarReference? Calendar { get; }
    internal ResourceBindingDeclaration(SourceJson data, DocumentId document) : base(data, document)
    {
        Fields("tenantId", "resourceId", "snapshotVersion", "snapshotDigest", "resourceKind", "calendar");
        TenantId = SourceFields.Identity<TenantId>(Payload.Value, "tenantId");
        ResourceId = SourceFields.Identity<ResourceId>(Payload.Value, "resourceId");
        SnapshotVersion = TimeValueFields.Nonempty(Payload, "snapshotVersion");
        SnapshotDigest = ContentDigest.Parse(Child("snapshotDigest"));
        if (ValueFields.HasUnknown(SnapshotDigest.Data.Value, "algorithm", "value")) throw new UnsupportedSourceException("Unknown resource digest contract.");
        ResourceKind = SourceFields.Token(Payload.Value, "resourceKind");
        Behavior = ResourceKind switch
        {
            "coglatas.human" or "coglatas.equipment" or "coglatas.facility" => ResourceBehavior.Reusable,
            "coglatas.material" => ResourceBehavior.Consumable,
            _ => throw new UnsupportedSourceException("Unknown resource behavior; no reusable fallback.")
        };
        if (Payload.Value.TryGetProperty("calendar", out _)) Calendar = DeclarationFields.Calendar(Child("calendar"));
    }
}

public sealed class ConstraintPlanDeclaration : PlanDeclaration
{
    public PurePredicate Predicate { get; }
    public bool IsRequired { get; }
    internal ConstraintPlanDeclaration(SourceJson data, DocumentId document) : base(data, document)
    {
        Fields("predicate", "classification");
        Predicate = PurePredicate.Parse(Child("predicate"));
        IsRequired = SourceFields.Token(Payload.Value, "classification") switch
        {
            "coglatas.required" => true, "coglatas.advisory" => false,
            _ => throw new UnsupportedSourceException("Unknown constraint classification.")
        };
        // Attached targets belong only to typed constrains relations in Slice 3.
    }
}

public sealed class WorkflowTransitionDefinition
{
    public WorkflowTransitionId TransitionId { get; }
    public WorkflowStateId From { get; }
    public WorkflowStateId To { get; }
    public PurePredicate Guard { get; }
    public SourceJson Data { get; }
    internal WorkflowTransitionDefinition(SourceJson data)
    {
        Data = data;
        TransitionId = SourceFields.Identity<WorkflowTransitionId>(data.Value, "transitionId");
        From = SourceFields.Identity<WorkflowStateId>(data.Value, "from");
        To = SourceFields.Identity<WorkflowStateId>(data.Value, "to");
        Guard = PurePredicate.Parse(SourceJson.FromElement(SourceFields.Object(data.Value, "guard"), data.Limits));
        if (ValueFields.HasUnknown(data.Value, "transitionId", "from", "to", "guard"))
            throw new UnsupportedSourceException("Unsupported workflow action/transition contract.");
    }
}

public sealed class WorkflowPlanDeclaration : PlanDeclaration
{
    public IReadOnlyList<WorkflowStateId> States { get; }
    public IReadOnlyList<WorkflowTransitionDefinition> Transitions { get; }
    public WorkflowStateId InitialState { get; }
    public int MaximumTransitions { get; }
    internal WorkflowPlanDeclaration(SourceJson data, DocumentId document) : base(data, document)
    {
        Fields("states", "transitions", "initialState", "maximumTransitions");
        States = DeclarationFields.Identities<WorkflowStateId>(Payload, "states");
        InitialState = SourceFields.Identity<WorkflowStateId>(Payload.Value, "initialState");
        MaximumTransitions = SourceFields.PositiveInteger(Payload.Value, "maximumTransitions");
        if (!States.Contains(InitialState)) throw new FormatException("Workflow initial state is undeclared.");
        var transitions = SourceFields.Array(Payload.Value, "transitions").EnumerateArray()
            .Select(item => new WorkflowTransitionDefinition(SourceJson.FromElement(item, Data.Limits))).ToArray();
        if (transitions.Select(item => item.TransitionId).Distinct().Count() != transitions.Length ||
            transitions.Any(item => !States.Contains(item.From) || !States.Contains(item.To)))
            throw new FormatException("Workflow transition identity or endpoint is invalid.");
        Transitions = new ReadOnlyCollection<WorkflowTransitionDefinition>(transitions);
        // Loops are syntactically bounded here; this is not an executing Workflow engine.
    }
}

internal static class DeclarationFields
{
    internal static IReadOnlyList<T> Identities<T>(SourceJson data, string name) where T : struct, IProjectIdentity<T>
    {
        var values = SourceFields.Array(data.Value, name).EnumerateArray().Select(value =>
        {
            var text = ValueFields.String(value);
            var id = T.Parse(text);
            if (id.ToString() != text) throw new FormatException("Declaration identity must be canonical.");
            return id;
        }).ToArray();
        if (values.Distinct().Count() != values.Length) throw new FormatException("Duplicate declaration identity.");
        return new ReadOnlyCollection<T>(values);
    }

    internal static TypedProjectValue Value(SourceJson data, params string[] kinds)
    {
        var result = TypedProjectValueCodec.Decode(data);
        if (result.Status == ValueDecodeStatus.Invalid) throw new FormatException("Invalid typed declaration value.");
        if (result.Status == ValueDecodeStatus.Unsupported) throw new UnsupportedSourceException("Unsupported typed declaration value.");
        if (!kinds.Contains(result.Value!.Type.Kind, StringComparer.Ordinal)) throw new FormatException("Declaration value type mismatch.");
        return result.Value;
    }

    internal static CalendarReference Calendar(SourceJson data)
    {
        var calendar = new CalendarReference(data);
        if (ValueFields.HasUnknown(data.Value, "calendarId", "version", "timeZoneId", "rulesDigest") ||
            ValueFields.HasUnknown(calendar.RulesDigest.Data.Value, "algorithm", "value"))
            throw new UnsupportedSourceException("Unsupported calendar contract.");
        return calendar;
    }
}
