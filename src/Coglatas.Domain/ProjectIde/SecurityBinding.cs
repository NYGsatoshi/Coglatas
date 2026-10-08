namespace Coglatas.Domain.ProjectIde;

/// <summary>
/// Transient canonical policy contents, supplied independently by the host.
/// Construction and hash equality do not authenticate policy ownership or grant access.
/// Durable adapters must not store Content.
/// </summary>
public sealed class SecurityPolicyEvidence
{
    private const string DigestDomain = "coglatas.security-policy/1";
    public SourceJson Content { get; }
    public SecurityPolicySnapshot Snapshot { get; }

    public SecurityPolicyEvidence(string policySetId, string version, SourceJson content, int schemaVersion = 1)
    {
        ArgumentNullException.ThrowIfNull(content);
        Content = content;
        Snapshot = new(policySetId, version, ContentDigest.Compute(DigestDomain, content.ToCanonicalBytes()), schemaVersion);
    }
}

/// <summary>
/// Frozen host evidence, distinct from request claims. The caller must load it from
/// the applicable host authority; values cannot establish their own trust.
/// Missing Source, policy or compiler remains absent, with no fabricated default.
/// </summary>
public sealed class SecurityEvaluationEvidence(ProjectSource? source = null, SecurityPolicyEvidence? policy = null,
    SecurityCompilerProvenance? compiler = null)
{
    public ProjectSource? Source { get; } = source;
    public SecurityPolicyEvidence? Policy { get; } = policy;
    public SecurityCompilerProvenance? Compiler { get; } = compiler;
}

/// <summary>
/// One canonical integrity binding of claims and independently supplied host evidence.
/// Data is transient: it retains canonical context extensions, not a storage/log DTO.
/// Digest equality never grants authorization or proves the evidence's authenticity.
/// </summary>
public sealed class SecurityBinding
{
    public const string SchemaId = "coglatas.security-binding";
    public const int SchemaVersion = 1;
    public const string DigestDomain = "coglatas.security-binding/1";
    public SecurityEvaluationRequest Request { get; }
    public SecurityEvaluationEvidence Evidence { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }

    private SecurityBinding(SecurityEvaluationRequest request, SecurityEvaluationEvidence evidence, SourceJson data)
    {
        Request = request;
        Evidence = evidence;
        Data = data;
        Digest = ContentDigest.Compute(DigestDomain, data.ToCanonicalBytes());
    }

    public static SecurityBinding Create(SecurityEvaluationRequest request, SecurityEvaluationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(evidence);
        // Evaluation identity, timestamps, database identity and enforcement mode
        // are execution metadata, not semantic binding input.
        var data = SourceJson.FromObject(new
        {
            schemaId = SchemaId,
            schemaVersion = SchemaVersion,
            domainTag = DigestDomain,
            canonicalEncoding = "utf-8",
            subject = new { tenantId = request.Subject.TenantId.ToString(), userId = request.Subject.UserId.ToString("D") },
            operationId = request.Operation.OperationId,
            claimed = new
            {
                resource = SourceValue(request.Resource.Context, request.Resource.InputDigest),
                source = SourceValue(request.Source),
                policy = PolicyValue(request.Policy),
                compiler = CompilerValue(request.Compiler)
            },
            expected = new
            {
                source = SourceValue(evidence.Source),
                policy = PolicyValue(evidence.Policy?.Snapshot),
                compiler = CompilerValue(evidence.Compiler)
            }
        });
        return new(request, evidence, data);
    }

    private static object? SourceValue(ProjectSource? source) => source is null ? null : SourceValue(source.Context, source.Digest);

    private static object SourceValue(SourceRevisionContext context, ContentDigest digest) => new
    {
        context = context.Data.Value,
        inputDigest = DigestValue(digest)
    };

    private static object? PolicyValue(SecurityPolicySnapshot? policy) => policy is null ? null : new
    {
        policySetId = policy.PolicySetId,
        version = policy.Version,
        schemaVersion = policy.SchemaVersion,
        contentDigest = DigestValue(policy.ContentDigest)
    };

    private static object? CompilerValue(SecurityCompilerProvenance? compiler) => compiler is null ? null : new
    {
        version = compiler.Version,
        buildIdentity = compiler.BuildIdentity,
        gitCommitSha = compiler.GitCommitSha
    };

    private static object DigestValue(ContentDigest digest) => new { algorithm = ContentDigest.Algorithm, value = digest.Value };
}
