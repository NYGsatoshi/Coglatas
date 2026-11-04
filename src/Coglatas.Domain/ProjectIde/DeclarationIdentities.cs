using System.Text.Json.Serialization;

namespace Coglatas.Domain.ProjectIde;

[JsonConverter(typeof(ProjectIdentityJsonConverter<ResourceId>))]
public readonly record struct ResourceId : IProjectIdentity<ResourceId>
{
    public Guid Value { get; }
    public ResourceId(Guid value) => Value = ProjectIdentity.Validate(value);
    public static ResourceId New() => new(Guid.NewGuid());
    public static ResourceId Parse(string text) => new(ProjectIdentity.Parse(text));
    public override string ToString() => ProjectIdentity.Format(Value);
}

[JsonConverter(typeof(ProjectIdentityJsonConverter<PortId>))]
public readonly record struct PortId : IProjectIdentity<PortId>
{
    public Guid Value { get; }
    public PortId(Guid value) => Value = ProjectIdentity.Validate(value);
    public static PortId New() => new(Guid.NewGuid());
    public static PortId Parse(string text) => new(ProjectIdentity.Parse(text));
    public override string ToString() => ProjectIdentity.Format(Value);
}

[JsonConverter(typeof(ProjectIdentityJsonConverter<WorkflowStateId>))]
public readonly record struct WorkflowStateId : IProjectIdentity<WorkflowStateId>
{
    public Guid Value { get; }
    public WorkflowStateId(Guid value) => Value = ProjectIdentity.Validate(value);
    public static WorkflowStateId New() => new(Guid.NewGuid());
    public static WorkflowStateId Parse(string text) => new(ProjectIdentity.Parse(text));
    public override string ToString() => ProjectIdentity.Format(Value);
}

[JsonConverter(typeof(ProjectIdentityJsonConverter<WorkflowTransitionId>))]
public readonly record struct WorkflowTransitionId : IProjectIdentity<WorkflowTransitionId>
{
    public Guid Value { get; }
    public WorkflowTransitionId(Guid value) => Value = ProjectIdentity.Validate(value);
    public static WorkflowTransitionId New() => new(Guid.NewGuid());
    public static WorkflowTransitionId Parse(string text) => new(ProjectIdentity.Parse(text));
    public override string ToString() => ProjectIdentity.Format(Value);
}
