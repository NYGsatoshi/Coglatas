using System.Text.Json;

namespace Coglatas.Domain.ProjectIde;

/// <summary>A typed, exactly scoped reference. Parsing is neither resolution nor authorization.</summary>
public sealed class RelationEndpoint
{
    public RelationEndpointRole Role { get; }
    public string ExpectedKind { get; }
    public PlanEntityReference Reference { get; }
    public PortId? PortId { get; }
    public RelationId? RelationId { get; }
    public SourceJson Data { get; }
    public string IdentityKey { get; }

    internal RelationEndpoint(SourceJson data)
    {
        Data = data;
        Role = SourceFields.Token(data.Value, "kind") switch
        {
            "coglatas.entity" => RelationEndpointRole.Entity, "coglatas.port" => RelationEndpointRole.Port,
            "coglatas.resource-binding" => RelationEndpointRole.ResourceBinding, "coglatas.relation" => RelationEndpointRole.Relation,
            _ => throw new UnsupportedSourceException("Unsupported endpoint role.")
        };
        ExpectedKind = SourceFields.Token(data.Value, "expectedKind");
        Reference = new(RelationFields.Child(data, "reference"));
        if (Role == RelationEndpointRole.Port) PortId = SourceFields.Identity<PortId>(data.Value, "portId");
        if (Role == RelationEndpointRole.Relation) RelationId = SourceFields.Identity<RelationId>(data.Value, "relationId");
        RelationFields.Require(data, Role switch
        {
            RelationEndpointRole.Port => ["kind", "expectedKind", "reference", "portId"],
            RelationEndpointRole.Relation => ["kind", "expectedKind", "reference", "relationId"],
            _ => ["kind", "expectedKind", "reference"]
        });
        IdentityKey = SourceJson.FromObject(new { reference = Reference.Data.Value, portId = PortId?.ToString(), relationId = RelationId?.ToString() }).CanonicalText;
    }
}

public abstract record RelationPayload;
public sealed record DependencyRelationPayload(string Mode, TypedProjectValue Lag) : RelationPayload;
public sealed record RequirementRelationPayload(string RequirementVersion, ContentDigest RequirementDigest, PurePredicate Condition, string EvidenceKind) : RelationPayload;
public sealed record ResourceRelationPayload(TypedProjectValue Amount, TypedProjectValue? At, ProjectTimeInterval? Interval, string? Release) : RelationPayload;
public sealed record WorkflowRelationPayload(WorkflowStateId State, WorkflowTransitionId? Transition, PurePredicate Guard) : RelationPayload;
public sealed record ConstraintRelationPayload(PlanEntityReference PredicateReference, string Version, ContentDigest PredicateDigest) : RelationPayload;
public sealed record EmptyRelationPayload : RelationPayload;

/// <summary>Read-only projection over an entry of ProjectSource.Relations; Data always retains the original entry.</summary>
public sealed class TypedRelation
{
    public RelationId RelationId { get; }
    public DocumentId DocumentId { get; }
    public string Kind { get; }
    public int SchemaVersion { get; }
    public string DescriptorId { get; }
    public int DescriptorVersion { get; }
    public RelationEndpoint Source { get; }
    public RelationEndpoint Target { get; }
    public RelationKindDescriptor Descriptor { get; }
    public RelationPayload Payload { get; }
    public SourceJson Data { get; }
    public ProjectLocation Location { get; }

    internal TypedRelation(SourceJson data, SourceRevisionContext context)
    {
        Data = data;
        RelationId = SourceFields.Identity<RelationId>(data.Value, "relationId");
        DocumentId = SourceFields.Identity<DocumentId>(data.Value, "documentId");
        Kind = SourceFields.Token(data.Value, "kind");
        SchemaVersion = SourceFields.PositiveInteger(data.Value, "schemaVersion");
        if (SchemaVersion != 1 || RelationKindDescriptor.Find(Kind, 1) is null)
            throw new UnsupportedSourceException("Unsupported relation schema or kind.");
        var descriptor = RelationFields.Child(data, "descriptor");
        DescriptorId = SourceFields.Token(descriptor.Value, "descriptorId");
        DescriptorVersion = SourceFields.PositiveInteger(descriptor.Value, "version");
        if (DescriptorId != Kind) throw new FormatException("Relation kind and descriptor identity disagree.");
        Descriptor = RelationKindDescriptor.Find(DescriptorId, DescriptorVersion) ?? throw new UnsupportedSourceException("Unsupported relation descriptor.");
        RelationFields.Require(descriptor, "descriptorId", "version");
        RelationFields.Require(data, "relationId", "documentId", "kind", "schemaVersion", "descriptor", "source", "target", "payload");
        Source = new(RelationFields.Child(data, "source")); Target = new(RelationFields.Child(data, "target"));
        Location = ProjectLocation.Create(DocumentId, context, relationId: RelationId);
        Payload = ReadPayload(RelationFields.Child(data, "payload"));
    }

    private RelationPayload ReadPayload(SourceJson data)
    {
        switch (Kind)
        {
            case RelationKinds.DependsOn:
                RelationFields.Require(data, "mode", "lag");
                var mode = SourceFields.Token(data.Value, "mode");
                if (mode is not ("coglatas.finish-to-start" or "coglatas.start-to-start" or "coglatas.finish-to-finish" or "coglatas.start-to-finish"))
                    throw new UnsupportedSourceException("Unsupported dependency mode.");
                var lag = DeclarationFields.Value(RelationFields.Child(data, "lag"), "coglatas.elapsed-duration", "coglatas.working-duration");
                if (lag.State == ValueState.Null) throw new FormatException("Dependency lag cannot be Null.");
                return new DependencyRelationPayload(mode, lag);
            case RelationKinds.Satisfies:
            case RelationKinds.Verifies:
                RelationFields.Require(data, "requirementVersion", "requirementDigest", "condition", "evidenceKind");
                var evidence = SourceFields.Token(data.Value, "evidenceKind");
                if (evidence is not ("coglatas.model-proof" or "coglatas.observation" or "coglatas.human-approval" or "coglatas.heuristic"))
                    throw new UnsupportedSourceException("Unsupported evidence specification.");
                return new RequirementRelationPayload(RelationFields.Version(data, "requirementVersion"), RelationFields.Digest(data, "requirementDigest"), PurePredicate.Parse(RelationFields.Child(data, "condition")), evidence);
            case RelationKinds.Consumes:
            case RelationKinds.Allocates:
                var amount = DeclarationFields.Value(RelationFields.Child(data, "amount"), "coglatas.quantity");
                if (amount.State == ValueState.Null || amount.KnownValue is Quantity { Amount.Coefficient.Sign: <= 0 })
                    throw new FormatException("Resource demand must be positive or explicitly Unknown.");
                if (Kind == RelationKinds.Consumes)
                {
                    RelationFields.Require(data, "amount", "at");
                    var at = DeclarationFields.Value(RelationFields.Child(data, "at"), "coglatas.instant", "coglatas.local-date");
                    if (at.State == ValueState.Null) throw new FormatException("Consumption time cannot be Null.");
                    return new ResourceRelationPayload(amount, at, null, null);
                }
                RelationFields.Require(data, "amount", "interval", "release");
                var release = SourceFields.Token(data.Value, "release");
                if (release != "coglatas.interval-end") throw new UnsupportedSourceException("Unsupported allocation release policy.");
                return new ResourceRelationPayload(amount, null, new(RelationFields.Child(data, "interval")), release);
            case RelationKinds.Governs:
                RelationFields.Require(data, "stateId", "transitionId", "guard");
                WorkflowTransitionId? transition = data.Value.TryGetProperty("transitionId", out var value) && value.ValueKind != JsonValueKind.Null
                    ? SourceFields.Identity<WorkflowTransitionId>(data.Value, "transitionId") : null;
                return new WorkflowRelationPayload(SourceFields.Identity<WorkflowStateId>(data.Value, "stateId"), transition, PurePredicate.Parse(RelationFields.Child(data, "guard")));
            case RelationKinds.Constrains:
                RelationFields.Require(data, "predicateReference", "version", "predicateDigest");
                return new ConstraintRelationPayload(new(RelationFields.Child(data, "predicateReference")), RelationFields.Version(data, "version"), RelationFields.Digest(data, "predicateDigest"));
            default:
                RelationFields.Require(data);
                return new EmptyRelationPayload();
        }
    }
}

/// <summary>Unsupported/invalid entries retain complete raw endpoints and payload without a usable semantic value.</summary>
public sealed record RelationDecodeResult(ValueDecodeStatus Status, TypedRelation? Relation, SourceJson Data);

public static class TypedRelationReader
{
    public static RelationDecodeResult Read(SourceJson data, SourceRevisionContext context)
    {
        ArgumentNullException.ThrowIfNull(data); ArgumentNullException.ThrowIfNull(context);
        try { return new(ValueDecodeStatus.Decoded, new(data, context), data); }
        catch (UnsupportedSourceException) { return new(ValueDecodeStatus.Unsupported, null, data); }
        catch (Exception error) when (error is FormatException or ArgumentException or OverflowException or InvalidOperationException)
        { return new(ValueDecodeStatus.Invalid, null, data); }
    }

    public static IReadOnlyList<RelationDecodeResult> Read(ProjectSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Array.AsReadOnly(source.Relations.Value.EnumerateArray()
            .Select(item => Read(SourceJson.FromElement(item, source.Data.Limits), source.Context)).ToArray());
    }
}

internal static class RelationFields
{
    internal static SourceJson Child(SourceJson data, string field) => SourceJson.FromElement(SourceFields.Object(data.Value, field), data.Limits);
    internal static void Require(SourceJson data, params string[] fields)
    {
        if (data.Value.ValueKind != JsonValueKind.Object) throw new FormatException("Expected a relation object.");
        if (ValueFields.HasUnknown(data.Value, fields)) throw new UnsupportedSourceException("Unknown relation semantics retained.");
    }
    internal static string Version(SourceJson data, string field)
    {
        var value = SourceFields.String(data.Value, field);
        if (string.IsNullOrWhiteSpace(value)) throw new FormatException("A fixed version is required.");
        return value;
    }
    internal static ContentDigest Digest(SourceJson data, string field)
    {
        var child = Child(data, field); Require(child, "algorithm", "value"); return ContentDigest.Parse(child);
    }
}
