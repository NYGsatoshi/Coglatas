namespace Coglatas.Domain.ProjectIde;

public enum RelationEffect { Association, Scheduling, Membership, RequirementClaim, VerificationRoute, Consumption, Allocation, Workflow, Constraint, Dataflow }
public enum RelationCyclePolicy { Allowed, Acyclic, BoundedWorkflow }
public enum RelationEndpointRole { Entity, Port, ResourceBinding, Relation }
public enum ReferenceResolutionState { Resolved, MissingWithinCompleteScope, UnknownDueToCoverage, Ambiguous, Unsupported, OutOfScope }

/// <summary>Versioned built-in vocabulary. Display labels never identify a descriptor.</summary>
public static class RelationKinds
{
    public const string DependsOn = "coglatas.depends-on";
    public const string Satisfies = "coglatas.satisfies";
    public const string Verifies = "coglatas.verifies";
    public const string Consumes = "coglatas.consumes";
    public const string Allocates = "coglatas.allocates";
    public const string Contains = "coglatas.contains";
    public const string Governs = "coglatas.governs";
    public const string Constrains = "coglatas.constrains";
    public const string RelatedTo = "coglatas.related-to";
    public const string FlowsTo = "coglatas.flows-to";
}

/// <summary>Immutable interpretation contract, never an independently writable relation store.</summary>
public sealed class RelationKindDescriptor
{
    public string DescriptorId { get; }
    public int Version => 1;
    public RelationEndpointRole SourceRole { get; }
    public RelationEndpointRole TargetRole { get; }
    public IReadOnlyList<string> SourceKinds { get; }
    public IReadOnlyList<string> TargetKinds { get; }
    public IReadOnlyList<string> AllowedScopes { get; } = Array.AsReadOnly(new[] { "coglatas.same-project", "coglatas.authorized-pinned-project" });
    public int SourceEndpointCount => 1;
    public int TargetEndpointCount => 1;
    public int MinimumOutgoing { get; }
    public int? MaximumIncoming { get; }
    public bool IsSymmetric => false;
    public RelationEffect Effect { get; }
    public RelationCyclePolicy CyclePolicy { get; }
    public IReadOnlyList<string> RequiredCapabilities { get; }
    public IReadOnlyList<string> Invariants { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }

    private RelationKindDescriptor(string id, string[] sources, string[] targets, RelationEffect effect,
        RelationCyclePolicy cycles = RelationCyclePolicy.Allowed, RelationEndpointRole sourceRole = RelationEndpointRole.Entity,
        RelationEndpointRole targetRole = RelationEndpointRole.Entity, int minimumOutgoing = 0, int? maximumIncoming = null)
    {
        DescriptorId = id; SourceRole = sourceRole; TargetRole = targetRole;
        SourceKinds = Array.AsReadOnly(sources); TargetKinds = Array.AsReadOnly(targets);
        Effect = effect; CyclePolicy = cycles; MinimumOutgoing = minimumOutgoing; MaximumIncoming = maximumIncoming;
        RequiredCapabilities = Array.AsReadOnly(new[] { id + "/1" });
        Invariants = Array.AsReadOnly(new[] { "coglatas.typed-endpoints", "coglatas.exact-context", "coglatas.no-equivalent-duplicates" });
        Data = SourceJson.FromObject(new
        {
            descriptorId = id, version = Version, sourceRole = RoleToken(sourceRole), targetRole = RoleToken(targetRole),
            sourceKinds = sources, targetKinds = targets, allowedScopes = AllowedScopes, sourceEndpointCount = 1, targetEndpointCount = 1,
            minimumOutgoing, maximumIncoming, symmetric = IsSymmetric, effect = id, cyclePolicy = CycleToken(cycles),
            requiredCapabilities = RequiredCapabilities, invariants = Invariants
        });
        Digest = ContentDigest.Compute("coglatas.relation-descriptor/1", Data.ToCanonicalBytes());
    }

    private static string RoleToken(RelationEndpointRole role) => role switch
    {
        RelationEndpointRole.Entity => "coglatas.entity", RelationEndpointRole.Port => "coglatas.port",
        RelationEndpointRole.ResourceBinding => "coglatas.resource-binding", RelationEndpointRole.Relation => "coglatas.relation",
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };
    private static string CycleToken(RelationCyclePolicy policy) => policy switch
    {
        RelationCyclePolicy.Allowed => "coglatas.cycles-allowed", RelationCyclePolicy.Acyclic => "coglatas.acyclic",
        RelationCyclePolicy.BoundedWorkflow => "coglatas.bounded-workflow", _ => throw new ArgumentOutOfRangeException(nameof(policy))
    };

    private static readonly string[] Entities = ["coglatas.project", "coglatas.task", "coglatas.milestone", "coglatas.requirement", "coglatas.resource-binding", "coglatas.constraint", "coglatas.workflow"];
    public static IReadOnlyList<RelationKindDescriptor> BuiltIns { get; } = Array.AsReadOnly(new[]
    {
        new RelationKindDescriptor(RelationKinds.DependsOn, ["coglatas.task"], ["coglatas.task"], RelationEffect.Scheduling, RelationCyclePolicy.Acyclic),
        new RelationKindDescriptor(RelationKinds.Satisfies, ["coglatas.task"], ["coglatas.requirement"], RelationEffect.RequirementClaim),
        new RelationKindDescriptor(RelationKinds.Verifies, ["coglatas.task"], ["coglatas.requirement"], RelationEffect.VerificationRoute),
        new RelationKindDescriptor(RelationKinds.Consumes, ["coglatas.task"], ["coglatas.resource-binding"], RelationEffect.Consumption, targetRole: RelationEndpointRole.ResourceBinding),
        new RelationKindDescriptor(RelationKinds.Allocates, ["coglatas.task"], ["coglatas.resource-binding"], RelationEffect.Allocation, targetRole: RelationEndpointRole.ResourceBinding),
        new RelationKindDescriptor(RelationKinds.Contains, ["coglatas.milestone"], ["coglatas.task"], RelationEffect.Membership),
        new RelationKindDescriptor(RelationKinds.Governs, ["coglatas.workflow"], ["coglatas.task"], RelationEffect.Workflow, RelationCyclePolicy.BoundedWorkflow, maximumIncoming: 1),
        new RelationKindDescriptor(RelationKinds.Constrains, ["coglatas.constraint"], ["coglatas.project", "coglatas.task", "coglatas.milestone", "coglatas.relation", "coglatas.resource-binding", "coglatas.workflow"], RelationEffect.Constraint, minimumOutgoing: 1),
        new RelationKindDescriptor(RelationKinds.RelatedTo, Entities, Entities, RelationEffect.Association),
        new RelationKindDescriptor(RelationKinds.FlowsTo, ["coglatas.task"], ["coglatas.task"], RelationEffect.Dataflow, RelationCyclePolicy.Acyclic, RelationEndpointRole.Port, RelationEndpointRole.Port)
    });

    public static RelationKindDescriptor? Find(string descriptorId, int version) =>
        version == 1 ? BuiltIns.SingleOrDefault(item => item.DescriptorId == descriptorId) : null;
}
