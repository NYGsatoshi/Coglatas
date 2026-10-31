namespace Coglatas.Domain.ProjectIde;

/// <summary>Scoped policy request metadata only. It never edits a finding, proof, rule result or platform invariant.</summary>
public sealed class CompilerSuppressionRequest
{
    public Guid RequestId { get; }
    public string RuleId { get; }
    public int RuleVersion { get; }
    public ProjectLocation Target { get; }
    public string Reason { get; }
    public string PolicyVersion { get; }
    public ContentDigest PolicyDigest { get; }
    public ProjectInstant? ExpiresAt { get; }
    public SourceJson Data { get; }
    public CompilerSuppressionRequest(SourceJson data, SourceRevisionContext expectedContext)
    {
        ArgumentNullException.ThrowIfNull(data); ArgumentNullException.ThrowIfNull(expectedContext);
        RelationFields.Require(data, "requestId", "ruleId", "ruleVersion", "target", "reason", "policyVersion", "policyDigest", "expiresAt");
        RequestId = ProjectIdentity.Parse(SourceFields.String(data.Value, "requestId"));
        if (RequestId.ToString("D") != SourceFields.String(data.Value, "requestId")) throw new FormatException("Noncanonical suppression request ID.");
        RuleId = SourceFields.Token(data.Value, "ruleId"); RuleVersion = SourceFields.PositiveInteger(data.Value, "ruleVersion");
        Target = ProjectLocation.Parse(RelationFields.Child(data, "target"));
        if (!Target.Context.Equals(expectedContext)) throw new FormatException("Suppression request targets a different revision/scope.");
        Reason = SourceFields.String(data.Value, "reason");
        if (string.IsNullOrWhiteSpace(Reason) || Reason.Length > 1024) throw new FormatException("A bounded explicit suppression reason is required.");
        PolicyVersion = RelationFields.Version(data, "policyVersion"); PolicyDigest = RelationFields.Digest(data, "policyDigest");
        var expires = data.Value.GetProperty("expiresAt");
        if (expires.ValueKind != System.Text.Json.JsonValueKind.Null) ExpiresAt = ProjectInstant.Parse(ValueFields.String(expires));
        Data = data;
    }
}
