using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Evaluations;

public enum SecurityEvaluationFreshness { Unverified, Current, Stale }

/// <summary>Safe historical metadata. Currentness never grants access or Merge authority.</summary>
public sealed record SecurityEvaluationReadModel(
    Guid EvaluationId, Guid TenantId, Guid ProjectId, string BindingDigest, int SchemaVersion,
    SecurityEvaluationIdentitySnapshot Identity, SecurityEnforcementMode EnforcementMode,
    SecurityEvaluationStatus Status, SecurityDecisionOutcome? Outcome, SecurityReasonCode ReasonCode,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? TerminalAtUtc,
    SecurityEvaluationFreshness Freshness, IReadOnlyList<SecurityRuleResult> Rules)
{
    public bool IsAuthoritative => false;
}

public interface ISecurityEvaluationReader
{
    Task<SecurityEvaluationReadModel?> FindAsync(Guid projectId, Guid evaluationId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Future #905 host seam: resolve the actual current Source/policy/compiler independently.
/// Never echo historical claims or accept caller-supplied digests as current evidence.
/// </summary>
public interface ISecurityCurrentContextProvider
{
    ValueTask<SecurityBinding?> ResolveAsync(Guid projectId, SecuritySourceIdentitySnapshot historicalResource,
        CancellationToken cancellationToken);
}

internal sealed class UnavailableSecurityCurrentContextProvider : ISecurityCurrentContextProvider
{
    public ValueTask<SecurityBinding?> ResolveAsync(Guid projectId, SecuritySourceIdentitySnapshot historicalResource,
        CancellationToken cancellationToken) => ValueTask.FromResult<SecurityBinding?>(null);
}

public sealed class SecurityEvaluationReader(ISecurityEvaluationStore store,
    ISecurityCurrentContextProvider currentContext, SecurityEvaluationDiagnostics diagnostics) : ISecurityEvaluationReader
{
    public async Task<SecurityEvaluationReadModel?> FindAsync(Guid projectId, Guid evaluationId,
        CancellationToken cancellationToken = default)
    {
        // The store enforces current authentication and Tenant/Project authorization before disclosure.
        var record = await store.FindAsync(projectId, evaluationId, cancellationToken);
        if (record is null)
            return null;
        var current = await currentContext.ResolveAsync(projectId, record.Identity.Resource, cancellationToken);
        // Host resolution can await I/O. Recheck permission and retention before returning anything.
        record = await store.FindAsync(projectId, evaluationId, cancellationToken);
        if (record is null)
            return null;
        var freshness = GetFreshness(record, current);
        if (freshness == SecurityEvaluationFreshness.Stale)
            diagnostics.RecordBindingMismatch();
        return new(record.EvaluationId, record.TenantId, record.ProjectId, record.BindingDigest, record.SchemaVersion,
            record.Identity, record.EnforcementMode, record.Status, record.Outcome, record.ReasonCode,
            record.CreatedAtUtc, record.TerminalAtUtc, freshness, Array.AsReadOnly(record.Rules.ToArray()));
    }

    private static SecurityEvaluationFreshness GetFreshness(SecurityEvaluationRecord record, SecurityBinding? current)
    {
        if (current is null)
            return SecurityEvaluationFreshness.Unverified;
        var expected = SecurityEvaluationIdentitySnapshot.Capture(current);
        var saved = record.Identity;
        // An authorized historical viewer need not be the original evaluating subject.
        if (record.TenantId != current.Request.Subject.TenantId.Value ||
            record.TenantId != expected.Resource.TenantId || record.ProjectId != expected.Resource.ProjectId ||
            saved.OperationId != expected.OperationId || saved.Resource != expected.Resource ||
            Different(saved.ClaimedSource, expected.ClaimedSource) || Different(saved.ExpectedSource, expected.ExpectedSource) ||
            Different(saved.ClaimedPolicy, expected.ClaimedPolicy) || Different(saved.ExpectedPolicy, expected.ExpectedPolicy) ||
            Different(saved.ClaimedCompiler, expected.ClaimedCompiler) || Different(saved.ExpectedCompiler, expected.ExpectedCompiler))
            return SecurityEvaluationFreshness.Stale;
        // Missing evidence is uncertainty; matching nulls cannot certify currentness.
        if (saved.ClaimedSource is null || saved.ExpectedSource is null ||
            saved.ClaimedPolicy is null || saved.ExpectedPolicy is null ||
            saved.ClaimedCompiler is null || saved.ExpectedCompiler is null ||
            expected.ClaimedSource is null || expected.ExpectedSource is null ||
            expected.ClaimedPolicy is null || expected.ExpectedPolicy is null ||
            expected.ClaimedCompiler is null || expected.ExpectedCompiler is null)
            return SecurityEvaluationFreshness.Unverified;
        return expected.Resource == expected.ClaimedSource && expected.Resource == expected.ExpectedSource &&
               expected.ClaimedPolicy == expected.ExpectedPolicy && expected.ClaimedCompiler == expected.ExpectedCompiler
            ? SecurityEvaluationFreshness.Current : SecurityEvaluationFreshness.Stale;
    }

    private static bool Different<T>(T? left, T? right) where T : class =>
        left is not null && right is not null && !EqualityComparer<T>.Default.Equals(left, right);
}
