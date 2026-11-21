using System.Text.Json;

namespace Coglatas.Domain.ProjectIde;

public enum PortDirection { Input, Output }

/// <summary>Immutable declaration, not a bound flow edge. Binding/cardinality across edges belongs to Slice 3.</summary>
public sealed class PortDefinition
{
    public PortId PortId { get; }
    public PortDirection Direction { get; }
    public TypeReference Type { get; }
    public int MinimumConnections { get; }
    public int? MaximumConnections { get; }
    public TypedProjectValue? DefaultValue { get; }
    public SourceJson Data { get; }

    public PortDefinition(SourceJson data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
        PortId = SourceFields.Identity<PortId>(data.Value, "portId");
        Direction = SourceFields.Token(data.Value, "direction") switch
        {
            "coglatas.input" => PortDirection.Input, "coglatas.output" => PortDirection.Output,
            _ => throw new UnsupportedSourceException("Unsupported port direction.")
        };
        Type = new(SourceJson.FromElement(SourceFields.Object(data.Value, "type"), data.Limits));
        if (!Type.IsSupported) throw new UnsupportedSourceException("Unsupported port type.");
        MinimumConnections = ValueFields.Integer(data.Value, "minimumConnections");
        var maximum = ValueFields.Required(data.Value, "maximumConnections");
        MaximumConnections = maximum.ValueKind == JsonValueKind.Null ? null : ValueFields.Integer(data.Value, "maximumConnections");
        if (MinimumConnections < 0 || (MaximumConnections is { } max && max < MinimumConnections))
            throw new FormatException("Invalid port cardinality.");
        if (Direction == PortDirection.Input && MaximumConnections is not 1)
            throw new UnsupportedSourceException("Multiple input requires a declared collection/merge contract.");
        if (data.Value.TryGetProperty("defaultValue", out var defaultValue))
        {
            if (Direction != PortDirection.Input) throw new FormatException("Only input ports may declare defaults.");
            var result = TypedProjectValueCodec.Decode(SourceJson.FromElement(defaultValue, data.Limits));
            if (result.Status == ValueDecodeStatus.Invalid) throw new FormatException("Invalid port default.");
            if (result.Status != ValueDecodeStatus.Decoded) throw new UnsupportedSourceException("Unsupported port default.");
            if (!Type.Data.Equals(result.Value!.Type.Data)) throw new FormatException("Port default type mismatch.");
            DefaultValue = result.Value;
        }
        if (ValueFields.HasUnknown(data.Value, "portId", "direction", "type", "minimumConnections", "maximumConnections", "defaultValue"))
            throw new UnsupportedSourceException("Unknown port fields require a versioned contract.");
    }
}

/// <summary>A pinned target reference. Parsing a reference does not resolve or authorize it.</summary>
public sealed class PlanEntityReference
{
    public TenantId TenantId { get; }
    public ProjectId ProjectId { get; }
    public EntityId EntityId { get; }
    public SourceRevisionContext Context { get; }
    public SourceJson Data { get; }

    public PlanEntityReference(SourceJson data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
        TenantId = SourceFields.Identity<TenantId>(data.Value, "tenantId");
        ProjectId = SourceFields.Identity<ProjectId>(data.Value, "projectId");
        EntityId = SourceFields.Identity<EntityId>(data.Value, "entityId");
        Context = SourceRevisionContext.Parse(SourceJson.FromElement(SourceFields.Object(data.Value, "context"), data.Limits));
        if (Context.Branch.TenantId != TenantId || Context.Branch.ProjectId != ProjectId)
            throw new FormatException("Plan reference context scope mismatch.");
        if (ValueFields.HasUnknown(data.Value, "tenantId", "projectId", "entityId", "context"))
            throw new UnsupportedSourceException("Unknown reference contract.");
        RequireKnownContext(Context);
    }

    internal static void RequireKnownContext(SourceRevisionContext context)
    {
        var data = context.Data.Value;
        if (ValueFields.HasUnknown(data.GetProperty("branch"), "tenantId", "projectId", "branchId"))
            throw new UnsupportedSourceException("Unknown reference Branch contract.");
        var unknown = context.Kind switch
        {
            "coglatas.committed" => ValueFields.HasUnknown(data, "kind", "branch", "revisionId"),
            "coglatas.candidate" => ValueFields.HasUnknown(data, "kind", "branch", "proposalId", "candidateRevisionId", "baseRevision", "capturedTargetHead"),
            "coglatas.scenario" => ValueFields.HasUnknown(data, "kind", "branch", "scenarioId", "scenarioRevisionId", "baseContext", "overlayDigest"),
            _ => true
        };
        if (unknown) throw new UnsupportedSourceException("Unknown reference context fields.");
        if (context.Proposal is not null)
        {
            RequireKnownContext(SourceRevisionContext.Parse(SourceJson.FromElement(data.GetProperty("baseRevision"), context.Data.Limits)));
            RequireKnownContext(SourceRevisionContext.Parse(SourceJson.FromElement(data.GetProperty("capturedTargetHead"), context.Data.Limits)));
        }
        if (context.Scenario is not null)
        {
            RequireKnownContext(context.Scenario.BaseContext);
            if (ValueFields.HasUnknown(data.GetProperty("overlayDigest"), "algorithm", "value"))
                throw new UnsupportedSourceException("Unknown Scenario digest contract.");
        }
    }
}
