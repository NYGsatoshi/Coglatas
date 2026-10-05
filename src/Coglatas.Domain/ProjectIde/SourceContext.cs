using System.Text.Json;

namespace Coglatas.Domain.ProjectIde;

public sealed record BranchRef
{
    public TenantId TenantId { get; }
    public ProjectId ProjectId { get; }
    public BranchId BranchId { get; }
    public BranchRef(TenantId tenantId, ProjectId projectId, BranchId branchId)
    {
        _ = tenantId.ToString(); _ = projectId.ToString(); _ = branchId.ToString();
        TenantId = tenantId; ProjectId = projectId; BranchId = branchId;
    }
}

public sealed record RevisionRef
{
    public BranchRef Branch { get; }
    public RevisionId RevisionId { get; }
    public RevisionRef(BranchRef branch, RevisionId revisionId)
    {
        ArgumentNullException.ThrowIfNull(branch);
        _ = revisionId.ToString();
        Branch = branch; RevisionId = revisionId;
    }
}

public sealed record ProposalContext
{
    public ProposalId ProposalId { get; }
    public CandidateRevisionId CandidateRevisionId { get; }
    public RevisionRef BaseRevision { get; }
    public RevisionRef CapturedTargetHead { get; }
    public BranchRef TargetBranch => CapturedTargetHead.Branch;

    public ProposalContext(ProposalId proposalId, CandidateRevisionId candidateRevisionId,
        RevisionRef baseRevision, RevisionRef capturedTargetHead)
    {
        ArgumentNullException.ThrowIfNull(baseRevision);
        ArgumentNullException.ThrowIfNull(capturedTargetHead);
        _ = proposalId.ToString(); _ = candidateRevisionId.ToString();
        if (baseRevision.Branch.TenantId != capturedTargetHead.Branch.TenantId ||
            baseRevision.Branch.ProjectId != capturedTargetHead.Branch.ProjectId)
            throw new ArgumentException("Proposal base and target must belong to the same Tenant/Project.");
        ProposalId = proposalId; CandidateRevisionId = candidateRevisionId;
        BaseRevision = baseRevision; CapturedTargetHead = capturedTargetHead;
    }
}

public sealed record ScenarioRef
{
    public ScenarioId ScenarioId { get; }
    public ScenarioRevisionId ScenarioRevisionId { get; }
    public SourceRevisionContext BaseContext { get; }
    public ContentDigest OverlayDigest { get; }
    public ScenarioRef(ScenarioId scenarioId, ScenarioRevisionId scenarioRevisionId,
        SourceRevisionContext baseContext, ContentDigest overlayDigest)
    {
        ArgumentNullException.ThrowIfNull(baseContext);
        ArgumentNullException.ThrowIfNull(overlayDigest);
        if (baseContext.Kind == "coglatas.scenario") throw new ArgumentException("A Scenario base must be committed or candidate.");
        _ = scenarioId.ToString(); _ = scenarioRevisionId.ToString();
        ScenarioId = scenarioId; ScenarioRevisionId = scenarioRevisionId;
        BaseContext = baseContext; OverlayDigest = overlayDigest;
    }
}

/// <summary>Frozen context only. It grants no access, review or commit authority.</summary>
public sealed class SourceRevisionContext : IEquatable<SourceRevisionContext>
{
    public string Kind { get; }
    public BranchRef Branch { get; }
    public RevisionRef? CommittedRevision { get; }
    public ProposalContext? Proposal { get; }
    public ScenarioRef? Scenario { get; }
    public SourceJson Data { get; }

    private SourceRevisionContext(string kind, BranchRef branch, RevisionRef? revision,
        ProposalContext? proposal, ScenarioRef? scenario, SourceJson data)
    {
        Kind = kind; Branch = branch; CommittedRevision = revision; Proposal = proposal; Scenario = scenario; Data = data;
    }

    public static SourceRevisionContext Committed(RevisionRef revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        return Parse(SourceJson.FromObject(new { kind = "coglatas.committed", branch = BranchData(revision.Branch), revisionId = revision.RevisionId.ToString() }));
    }

    public static SourceRevisionContext Candidate(ProposalContext proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        return Parse(SourceJson.FromObject(new
        {
            kind = "coglatas.candidate", branch = BranchData(proposal.TargetBranch),
            proposalId = proposal.ProposalId.ToString(), candidateRevisionId = proposal.CandidateRevisionId.ToString(),
            baseRevision = Committed(proposal.BaseRevision).Data.Value,
            capturedTargetHead = Committed(proposal.CapturedTargetHead).Data.Value
        }));
    }

    public static SourceRevisionContext Hypothetical(ScenarioRef scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        return Parse(SourceJson.FromObject(new
        {
            kind = "coglatas.scenario", branch = BranchData(scenario.BaseContext.Branch),
            scenarioId = scenario.ScenarioId.ToString(), scenarioRevisionId = scenario.ScenarioRevisionId.ToString(),
            baseContext = scenario.BaseContext.Data.Value, overlayDigest = scenario.OverlayDigest.Data.Value
        }));
    }

    public static SourceRevisionContext Parse(SourceJson data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var root = data.Value;
        var kind = SourceFields.String(root, "kind");
        var branch = ReadBranch(SourceFields.Object(root, "branch"));
        RevisionRef? revision = null;
        ProposalContext? proposal = null;
        ScenarioRef? scenario = null;
        switch (kind)
        {
            case "coglatas.committed":
                revision = new(branch, SourceFields.Identity<RevisionId>(root, "revisionId"));
                RejectFields(root, "proposalId", "candidateRevisionId", "scenarioId", "scenarioRevisionId", "baseContext", "baseRevision", "capturedTargetHead", "overlayDigest");
                break;
            case "coglatas.candidate":
                var baseContext = Parse(SourceJson.FromElement(SourceFields.Object(root, "baseRevision"), data.Limits));
                var headContext = Parse(SourceJson.FromElement(SourceFields.Object(root, "capturedTargetHead"), data.Limits));
                if (baseContext.CommittedRevision is null || headContext.CommittedRevision is null || headContext.Branch != branch)
                    throw new FormatException("Candidate base/head must be committed and target head must match Branch.");
                proposal = new(SourceFields.Identity<ProposalId>(root, "proposalId"),
                    SourceFields.Identity<CandidateRevisionId>(root, "candidateRevisionId"), baseContext.CommittedRevision, headContext.CommittedRevision);
                RejectFields(root, "revisionId", "scenarioId", "scenarioRevisionId", "baseContext", "overlayDigest");
                break;
            case "coglatas.scenario":
                var scenarioBase = Parse(SourceJson.FromElement(SourceFields.Object(root, "baseContext"), data.Limits));
                if (scenarioBase.Branch != branch) throw new FormatException("Scenario Branch must match its fixed base.");
                scenario = new(SourceFields.Identity<ScenarioId>(root, "scenarioId"),
                    SourceFields.Identity<ScenarioRevisionId>(root, "scenarioRevisionId"), scenarioBase,
                    ContentDigest.Parse(SourceJson.FromElement(SourceFields.Object(root, "overlayDigest"), data.Limits)));
                RejectFields(root, "revisionId", "proposalId", "candidateRevisionId", "baseRevision", "capturedTargetHead");
                break;
            default: throw new UnsupportedSourceException("Unsupported source context kind.");
        }
        return new(kind, branch, revision, proposal, scenario, data);
    }

    private static void RejectFields(JsonElement root, params string[] fields)
    {
        if (fields.Any(field => root.TryGetProperty(field, out _)))
            throw new FormatException("Conflicting committed/candidate/Scenario identity fields.");
    }

    private static object BranchData(BranchRef branch) => new
    {
        tenantId = branch.TenantId.ToString(), projectId = branch.ProjectId.ToString(), branchId = branch.BranchId.ToString()
    };

    private static BranchRef ReadBranch(JsonElement value) => new(
        SourceFields.Identity<TenantId>(value, "tenantId"), SourceFields.Identity<ProjectId>(value, "projectId"),
        SourceFields.Identity<BranchId>(value, "branchId"));

    public bool Equals(SourceRevisionContext? other) => other is not null && Data.CanonicalText == other.Data.CanonicalText;
    public override bool Equals(object? obj) => obj is SourceRevisionContext other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Data.CanonicalText);
}

internal static class SourceFields
{
    internal static JsonElement Required(JsonElement value, string name, JsonValueKind kind)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var result) || result.ValueKind != kind)
            throw new FormatException("Missing or incorrectly typed Source field: " + name);
        return result;
    }
    internal static string String(JsonElement value, string name) => Required(value, name, JsonValueKind.String).GetString()!;
    internal static T Identity<T>(JsonElement value, string name) where T : struct, IProjectIdentity<T>
    {
        var token = String(value, name);
        var id = T.Parse(token);
        if (token != ProjectIdentity.Format(id.Value)) throw new FormatException("Source IDs must use lowercase canonical UUID spelling.");
        return id;
    }
    internal static string Token(JsonElement value, string name)
    {
        var token = String(value, name);
        if (!token.Contains('.') || token.Any(char.IsWhiteSpace)) throw new FormatException("Expected a stable namespaced kind token.");
        return token;
    }
    internal static JsonElement Object(JsonElement value, string name) => Required(value, name, JsonValueKind.Object);
    internal static JsonElement Array(JsonElement value, string name) => Required(value, name, JsonValueKind.Array);
    internal static int PositiveInteger(JsonElement value, string name)
    {
        var number = Required(value, name, JsonValueKind.Number);
        if (!number.TryGetInt32(out var result) || result < 1) throw new FormatException("Expected a positive schema integer: " + name);
        return result;
    }
}
