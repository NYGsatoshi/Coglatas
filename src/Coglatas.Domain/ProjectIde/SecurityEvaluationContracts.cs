using System.Collections.ObjectModel;

namespace Coglatas.Domain.ProjectIde;

/// <summary>Rule decisions, independent of execution and enforcement.</summary>
public enum SecurityDecisionOutcome
{
    Allow,
    Deny,
    /// <summary>Evaluation completed, but required decision evidence is missing or unsupported.</summary>
    Unknown,
    /// <summary>An integrity or binding anomaly; not a generic risk classification.</summary>
    Quarantine
}

/// <summary>Execution state. Failure, cancellation and timeout are not decision outcomes.</summary>
public enum SecurityEvaluationStatus
{
    NotExecuted,
    Pending,
    Completed,
    Failed,
    Cancelled,
    TimedOut
}

public enum SecurityEnforcementMode
{
    Disabled,
    Shadow,
    /// <summary>Future compatibility only. Pre-Avalonia runtime activation is prohibited.</summary>
    Enforce
}

/// <summary>Finite safe codes. No exception text, input, credentials or unrestricted evidence.</summary>
public enum SecurityReasonCode
{
    NotExecuted,
    BindingsVerified,
    MissingEvidence,
    BindingMismatch,
    PolicyViolation,
    EvaluationFailed,
    EvaluationCancelled,
    EvaluationTimedOut,
    RuleExecutionFailed,
    RuleNotExecuted,
    RevisionBindingVerified,
    RevisionEvidenceMissing,
    RevisionBindingMismatch,
    PolicyBindingVerified,
    PolicyEvidenceMissing,
    PolicyBindingMismatch,
    CompilerProvenanceVerified,
    CompilerEvidenceMissing,
    CompilerProvenanceMismatch
}

/// <summary>Identity only. Neither a subject reference nor a digest grants authorization.</summary>
public sealed record SecuritySubjectRef
{
    public TenantId TenantId { get; }
    public Guid UserId { get; }

    public SecuritySubjectRef(TenantId tenantId, Guid userId)
    {
        _ = tenantId.ToString();
        if (userId == Guid.Empty) throw new ArgumentException("A subject requires a user identity.", nameof(userId));
        TenantId = tenantId;
        UserId = userId;
    }
}

public sealed record SecurityOperationRef
{
    public string OperationId { get; }

    public SecurityOperationRef(string operationId) =>
        OperationId = SecurityContractValue.Identifier(operationId, nameof(operationId));
}

/// <summary>Declared immutable Source/Revision identity. Its correctness is evaluated separately.</summary>
public sealed record SecurityResourceRef
{
    public SourceRevisionContext Context { get; }
    public ContentDigest InputDigest { get; }

    public SecurityResourceRef(SourceRevisionContext context, ContentDigest inputDigest)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(inputDigest);
        Context = context;
        InputDigest = inputDigest;
    }
}

/// <summary>Snapshot identity only. Self-reported metadata does not establish policy authenticity.</summary>
public sealed record SecurityPolicySnapshot
{
    public string PolicySetId { get; }
    public string Version { get; }
    public ContentDigest ContentDigest { get; }

    public SecurityPolicySnapshot(string policySetId, string version, ContentDigest contentDigest)
    {
        ArgumentNullException.ThrowIfNull(contentDigest);
        PolicySetId = SecurityContractValue.Identifier(policySetId, nameof(policySetId));
        Version = SecurityContractValue.Identifier(version, nameof(version));
        ContentDigest = contentDigest;
    }
}

/// <summary>Known compiler identity. Absent or incomplete identity is represented by no provenance.</summary>
public sealed record SecurityCompilerProvenance
{
    public string Version { get; }
    public string BuildIdentity { get; }

    public SecurityCompilerProvenance(string version, string buildIdentity)
    {
        Version = SecurityContractValue.Identifier(version, nameof(version));
        BuildIdentity = SecurityContractValue.Identifier(buildIdentity, nameof(buildIdentity), maximumLength: 256);
    }
}

/// <summary>
/// Frozen evaluation input, not an authorization capability. Source is transient;
/// durable adapters must not store the full Source or unrestricted evidence.
/// Trusted expected policy/compiler evidence must be supplied independently by the host.
/// </summary>
public sealed record SecurityEvaluationRequest
{
    public Guid EvaluationId { get; }
    public SecuritySubjectRef Subject { get; }
    public SecurityOperationRef Operation { get; }
    public SecurityResourceRef Resource { get; }
    public SecurityEnforcementMode EnforcementMode { get; }
    public ProjectSource? Source { get; }
    public SecurityPolicySnapshot? Policy { get; }
    public SecurityCompilerProvenance? Compiler { get; }

    public SecurityEvaluationRequest(Guid evaluationId, SecuritySubjectRef subject,
        SecurityOperationRef operation, SecurityResourceRef resource, SecurityEnforcementMode enforcementMode,
        ProjectSource? source = null, SecurityPolicySnapshot? policy = null, SecurityCompilerProvenance? compiler = null)
    {
        if (evaluationId == Guid.Empty) throw new ArgumentException("An evaluation requires an identity.", nameof(evaluationId));
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(resource);
        if (!Enum.IsDefined(enforcementMode)) throw new ArgumentOutOfRangeException(nameof(enforcementMode));
        EvaluationId = evaluationId;
        Subject = subject;
        Operation = operation;
        Resource = resource;
        EnforcementMode = enforcementMode;
        Source = source;
        Policy = policy;
        Compiler = compiler;
    }
}

public sealed record SecurityRuleResult
{
    public string RuleId { get; }
    public SecurityEvaluationStatus Status { get; }
    public SecurityDecisionOutcome? Outcome { get; }
    public SecurityReasonCode ReasonCode { get; }

    public SecurityRuleResult(string ruleId, SecurityEvaluationStatus status,
        SecurityDecisionOutcome? outcome, SecurityReasonCode reasonCode)
    {
        SecurityContractValue.Execution(status, outcome, reasonCode);
        RuleId = SecurityContractValue.Identifier(ruleId, nameof(ruleId));
        Status = status;
        Outcome = outcome;
        ReasonCode = reasonCode;
    }
}

/// <summary>Execution and decision remain separate. Aggregation is owned by the application coordinator.</summary>
public sealed class SecurityDecision
{
    public SecurityEvaluationStatus Status { get; }
    public SecurityDecisionOutcome? Outcome { get; }
    public SecurityReasonCode ReasonCode { get; }
    public IReadOnlyList<SecurityRuleResult> Rules { get; }

    public SecurityDecision(SecurityEvaluationStatus status, SecurityDecisionOutcome? outcome,
        SecurityReasonCode reasonCode, IEnumerable<SecurityRuleResult> rules)
    {
        SecurityContractValue.Execution(status, outcome, reasonCode);
        ArgumentNullException.ThrowIfNull(rules);
        var frozen = rules.ToArray();
        if (frozen.Any(rule => rule is null)) throw new ArgumentException("Rule results cannot be null.", nameof(rules));
        if (frozen.Select(rule => rule.RuleId).Distinct(StringComparer.Ordinal).Count() != frozen.Length)
            throw new ArgumentException("Rule identities must be unique.", nameof(rules));
        if (status == SecurityEvaluationStatus.Completed &&
            (frozen.Length == 0 || frozen.Any(rule => rule.Status != SecurityEvaluationStatus.Completed)))
            throw new ArgumentException("Completed decisions require executed rule results.", nameof(rules));
        Status = status;
        Outcome = outcome;
        ReasonCode = reasonCode;
        Rules = new ReadOnlyCollection<SecurityRuleResult>(frozen);
    }
}

/// <summary>Non-authoritative analysis metadata. It cannot grant access or change existing Merge decisions.</summary>
public sealed record SecurityAnalysisSummary
{
    public Guid? EvaluationId { get; }
    public SecurityEnforcementMode EnforcementMode { get; }
    public SecurityEvaluationStatus Status { get; }
    public SecurityDecisionOutcome? Outcome { get; }
    public SecurityReasonCode ReasonCode { get; }
    public bool IsAuthoritative => false;

    public SecurityAnalysisSummary(Guid? evaluationId, SecurityEnforcementMode enforcementMode,
        SecurityEvaluationStatus status, SecurityDecisionOutcome? outcome, SecurityReasonCode reasonCode)
    {
        SecurityContractValue.Execution(status, outcome, reasonCode);
        if (evaluationId == Guid.Empty) throw new ArgumentException("An evaluation identity cannot be empty.", nameof(evaluationId));
        if (!Enum.IsDefined(enforcementMode)) throw new ArgumentOutOfRangeException(nameof(enforcementMode));
        EvaluationId = evaluationId;
        EnforcementMode = enforcementMode;
        Status = status;
        Outcome = outcome;
        ReasonCode = reasonCode;
    }
}

/// <summary>Pre-Avalonia boundary shared by configuration and future execution seams.</summary>
public static class SecurityEnforcementBoundary
{
    public static bool IsRuntimeAllowed(SecurityEnforcementMode mode, bool enforcementAllowed = false) =>
        !enforcementAllowed && mode is SecurityEnforcementMode.Disabled or SecurityEnforcementMode.Shadow;

    public static void ValidateRuntimeMode(SecurityEnforcementMode mode, bool enforcementAllowed = false)
    {
        if (!IsRuntimeAllowed(mode, enforcementAllowed))
            throw new InvalidOperationException("Pre-Avalonia Security evaluation supports only Disabled/Shadow with enforcement approval disabled.");
    }
}

internal static class SecurityContractValue
{
    internal static string Identifier(string value, string parameterName, int maximumLength = 128)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumLength || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.' or ':' or '/' or '+')))
            throw new ArgumentException("A security identifier must be bounded ASCII metadata.", parameterName);
        return value;
    }

    internal static void Execution(SecurityEvaluationStatus status, SecurityDecisionOutcome? outcome, SecurityReasonCode reasonCode)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (!Enum.IsDefined(reasonCode)) throw new ArgumentOutOfRangeException(nameof(reasonCode));
        if (outcome.HasValue && !Enum.IsDefined(outcome.Value)) throw new ArgumentOutOfRangeException(nameof(outcome));
        if ((status == SecurityEvaluationStatus.Completed) != outcome.HasValue)
            throw new ArgumentException("Only completed execution has a decision outcome.", nameof(outcome));
        var validReason = status switch
        {
            SecurityEvaluationStatus.Completed => outcome switch
            {
                SecurityDecisionOutcome.Allow => reasonCode is SecurityReasonCode.BindingsVerified or
                    SecurityReasonCode.RevisionBindingVerified or SecurityReasonCode.PolicyBindingVerified or SecurityReasonCode.CompilerProvenanceVerified,
                SecurityDecisionOutcome.Deny => reasonCode == SecurityReasonCode.PolicyViolation,
                SecurityDecisionOutcome.Unknown => reasonCode is SecurityReasonCode.MissingEvidence or
                    SecurityReasonCode.RevisionEvidenceMissing or SecurityReasonCode.PolicyEvidenceMissing or SecurityReasonCode.CompilerEvidenceMissing,
                SecurityDecisionOutcome.Quarantine => reasonCode is SecurityReasonCode.BindingMismatch or
                    SecurityReasonCode.RevisionBindingMismatch or SecurityReasonCode.PolicyBindingMismatch or SecurityReasonCode.CompilerProvenanceMismatch,
                _ => false
            },
            SecurityEvaluationStatus.Failed => reasonCode is SecurityReasonCode.EvaluationFailed or SecurityReasonCode.RuleExecutionFailed,
            SecurityEvaluationStatus.Cancelled => reasonCode == SecurityReasonCode.EvaluationCancelled,
            SecurityEvaluationStatus.TimedOut => reasonCode == SecurityReasonCode.EvaluationTimedOut,
            SecurityEvaluationStatus.NotExecuted or SecurityEvaluationStatus.Pending =>
                reasonCode is SecurityReasonCode.NotExecuted or SecurityReasonCode.RuleNotExecuted,
            _ => false
        };
        if (!validReason) throw new ArgumentException("The safe reason code must match execution/decision semantics.", nameof(reasonCode));
    }
}
