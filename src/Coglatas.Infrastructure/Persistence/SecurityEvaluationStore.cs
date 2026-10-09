using System.Text.Json;
using System.Text.Json.Serialization;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.ProjectIde.Evaluations;
using Coglatas.Application.Projects;
using Coglatas.Domain.Entities;
using Coglatas.Domain.ProjectIde;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>Owns a narrow transaction; never flushes another use case's pending changes.</summary>
public sealed class SecurityEvaluationStore(AppDbContext context, ICurrentTenant currentTenant,
    ICurrentUser currentUser, IProjectAuthorizationService authorization, IClock clock) : ISecurityEvaluationStore
{
    private static readonly JsonSerializerOptions IdentityJsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<bool> CreatePendingAsync(SecurityBinding binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        SecurityEnforcementBoundary.ValidateRuntimeMode(binding.Request.EnforcementMode);
        var scope = CaptureScope();
        if (scope is null || !MatchesRequest(scope.Value, binding)) return false;
        EnsureIsolatedWrite([]);
        if (!await CanWriteAsync(scope.Value, binding.Request.Resource.Context.Branch.ProjectId.Value, cancellationToken)) return false;
        EnsureIsolatedWrite([]);
        var run = SecurityEvaluationRun.CreatePending(binding, clock.UtcNow);
        object[] owned = [run];
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!ScopeIsCurrent(scope.Value)) return false;
            context.SecurityEvaluationRuns.Add(run);
            await context.SaveChangesAsync(cancellationToken);
            if (!await CanWriteAsync(scope.Value, run.ProjectId, cancellationToken)) return false;
            EnsureIsolatedWrite(owned);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        finally { DetachOwned(owned); }
    }

    public async Task<SecurityTerminalizationResult> TerminalizeAsync(SecurityBinding binding, SecurityDecision decision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        SecurityEnforcementBoundary.ValidateRuntimeMode(binding.Request.EnforcementMode);
        SecurityEvaluationRun.ValidateTerminalDecision(decision);
        var scope = CaptureScope();
        if (scope is null || !MatchesRequest(scope.Value, binding)) return SecurityTerminalizationResult.Unavailable;
        var projectId = binding.Request.Resource.Context.Branch.ProjectId.Value;
        EnsureIsolatedWrite([]);
        if (!await CanWriteAsync(scope.Value, projectId, cancellationToken)) return SecurityTerminalizationResult.Unavailable;
        EnsureIsolatedWrite([]);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        List<object> owned = [];
        try
        {
            var run = await context.SecurityEvaluationRuns.FromSqlInterpolated($"""
                SELECT * FROM security_evaluation_runs
                WHERE "Id" = {binding.Request.EvaluationId} AND "TenantId" = {scope.Value.TenantId} AND "ProjectId" = {projectId}
                FOR UPDATE
                """).SingleOrDefaultAsync(cancellationToken);
            if (run is null) return SecurityTerminalizationResult.Unavailable;
            owned.Add(run);
            if (!ScopeIsCurrent(scope.Value) || run.BindingDigest != binding.Digest.Value ||
                run.EnforcementMode != binding.Request.EnforcementMode) return SecurityTerminalizationResult.Unavailable;
            if (run.Status != SecurityEvaluationStatus.Pending)
                return await CanWriteAsync(scope.Value, projectId, cancellationToken)
                    ? SecurityTerminalizationResult.AlreadyTerminal : SecurityTerminalizationResult.Unavailable;
            var rules = decision.Rules.OrderBy(rule => rule.RuleId, StringComparer.Ordinal)
                .Select((rule, sequence) => SecurityEvaluationRuleRecord.Create(run, sequence, rule)).ToArray();
            owned.AddRange(rules);
            EnsureIsolatedWrite(owned);
            context.SecurityEvaluationRuleResults.AddRange(rules);
            // Child rows must be inserted while the locked parent remains Pending.
            await context.SaveChangesAsync(cancellationToken);
            run.Terminalize(decision, clock.UtcNow);
            await context.SaveChangesAsync(cancellationToken);
            if (!await CanWriteAsync(scope.Value, projectId, cancellationToken)) return SecurityTerminalizationResult.Unavailable;
            EnsureIsolatedWrite(owned);
            await transaction.CommitAsync(cancellationToken);
            return SecurityTerminalizationResult.Terminalized;
        }
        finally { DetachOwned(owned); }
    }

    public async Task<SecurityEvaluationRecord?> FindAsync(Guid projectId, Guid evaluationId,
        CancellationToken cancellationToken = default)
    {
        var scope = CaptureScope();
        if (scope is null || !await CanReadAsync(scope.Value, projectId, cancellationToken)) return null;
        var run = await context.SecurityEvaluationRuns.AsNoTracking()
            .Where(run => run.Id == evaluationId && run.TenantId == scope.Value.TenantId && run.ProjectId == projectId)
            .Select(run => new
            {
                run.Id, run.TenantId, run.ProjectId, run.BindingDigest, run.SchemaVersion, run.IdentityJson,
                run.EnforcementMode, run.Status, run.Outcome, run.ReasonCode, run.CreatedAtUtc, run.TerminalAtUtc
            }).SingleOrDefaultAsync(cancellationToken);
        if (run is null) return null;
        var rules = run.Status == SecurityEvaluationStatus.Pending ? [] : await context.SecurityEvaluationRuleResults.AsNoTracking()
            .Where(rule => rule.EvaluationId == evaluationId && rule.TenantId == scope.Value.TenantId && rule.ProjectId == projectId)
            .OrderBy(rule => rule.Sequence)
            .Select(rule => new SecurityRuleResult(rule.RuleId, rule.Status, rule.Outcome, rule.ReasonCode)).ToArrayAsync(cancellationToken);
        if (!await CanReadAsync(scope.Value, projectId, cancellationToken)) return null;
        var identity = JsonSerializer.Deserialize<SecurityEvaluationIdentitySnapshot>(run.IdentityJson, IdentityJsonOptions)
            ?? throw new InvalidOperationException("Stored Security identity is invalid.");
        if (identity.Resource.TenantId != run.TenantId || identity.Resource.ProjectId != run.ProjectId)
            throw new InvalidOperationException("Stored Security identity does not match its scope.");
        if (run.Status != SecurityEvaluationStatus.Pending)
            SecurityEvaluationRun.ValidateTerminalDecision(new(run.Status, run.Outcome, run.ReasonCode, rules));
        return new(run.Id, run.TenantId, run.ProjectId, run.BindingDigest, run.SchemaVersion, identity,
            run.EnforcementMode, run.Status, run.Outcome, run.ReasonCode, run.CreatedAtUtc, run.TerminalAtUtc,
            Array.AsReadOnly(rules));
    }

    private (Guid TenantId, Guid UserId)? CaptureScope() => currentTenant.IsAvailable && !currentTenant.IsPlatformScope &&
        currentTenant.TenantId != Guid.Empty && currentUser.IsAuthenticated && currentUser.UserId is { } userId && userId != Guid.Empty
            ? (currentTenant.TenantId, userId) : null;

    private bool ScopeIsCurrent((Guid TenantId, Guid UserId) scope) => CaptureScope() == scope;

    private static bool MatchesRequest((Guid TenantId, Guid UserId) scope, SecurityBinding binding) =>
        binding.Request.Subject.TenantId.Value == scope.TenantId && binding.Request.Subject.UserId == scope.UserId &&
        binding.Request.Resource.Context.Branch.TenantId.Value == scope.TenantId;

    private async Task<bool> CanWriteAsync((Guid TenantId, Guid UserId) scope, Guid projectId, CancellationToken token) =>
        ScopeIsCurrent(scope) && await authorization.CanContributeProject(scope.UserId, projectId, token) &&
        await AuthorizationStateIsCurrentAsync(scope, projectId, token) && ScopeIsCurrent(scope) && await context.Projects.AsNoTracking()
            .AnyAsync(project => project.Id == projectId && project.TenantId == scope.TenantId && project.DeletedAt == null, token) &&
        ScopeIsCurrent(scope);

    private async Task<bool> CanReadAsync((Guid TenantId, Guid UserId) scope, Guid projectId, CancellationToken token) =>
        ScopeIsCurrent(scope) && await authorization.CanViewProject(scope.UserId, projectId, token) &&
        await AuthorizationStateIsCurrentAsync(scope, projectId, token) && ScopeIsCurrent(scope) &&
        await context.Projects.AsNoTracking()
            .AnyAsync(project => project.Id == projectId && project.TenantId == scope.TenantId && project.DeletedAt == null, token) &&
        ScopeIsCurrent(scope);

    private async Task<bool> AuthorizationStateIsCurrentAsync((Guid TenantId, Guid UserId) scope, Guid projectId, CancellationToken token)
    {
        // Existing authorization repositories may return tracked models. Compare
        // them with current rows without refreshing or detaching another use case's
        // entities. A stale scope must obtain a fresh authorization context.
        var entries = context.ChangeTracker.Entries().Where(entry => entry.Entity switch
        {
            Project project => project.Id == projectId,
            ProjectMember member => member.ProjectId == projectId && member.UserId == scope.UserId,
            Workspace workspace => workspace.TenantId == scope.TenantId,
            WorkspaceMember member => member.TenantId == scope.TenantId && member.UserId == scope.UserId,
            Group group => group.TenantId == scope.TenantId,
            GroupMember member => member.TenantId == scope.TenantId && member.UserId == scope.UserId,
            Tenant tenant => tenant.Id == scope.TenantId,
            TenantUser member => member.TenantId == scope.TenantId && member.UserId == scope.UserId,
            User user => user.Id == scope.UserId,
            _ => false
        }).ToArray();
        foreach (var entry in entries)
        {
            var current = await entry.GetDatabaseValuesAsync(token);
            if (current is null || entry.Properties.Any(property =>
                    !Equals(property.CurrentValue, current[property.Metadata.Name]))) return false;
        }
        return ScopeIsCurrent(scope);
    }

    private void EnsureIsolatedWrite(IReadOnlyCollection<object> owned)
    {
        if ((owned.Count == 0 && context.Database.CurrentTransaction is not null) || context.ChangeTracker.Entries()
            .Any(entry => (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted ||
                          entry.Entity is SecurityEvaluationRun or SecurityEvaluationRuleRecord) &&
                !owned.Any(item => ReferenceEquals(item, entry.Entity))))
            throw new InvalidOperationException("Security persistence requires its own clean unit of work.");
    }

    private void DetachOwned(IEnumerable<object> owned)
    {
        foreach (var entity in owned) context.Entry(entity).State = EntityState.Detached;
    }
}
