using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Application.ProjectIde.Runtime;
using Coglatas.Domain.ProjectIde;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

public interface IProjectDraftMaterializationReader
{
    /// <summary>Resolves the current authorized materializations for an exact manifest, or refuses.</summary>
    Task<IReadOnlyList<CompilerMaterializedSnapshot>?> ReadAsync(ProjectIdeViewerContext viewer,
        CompilerInputManifest input, CancellationToken cancellationToken);
}

public interface IProjectDraftRenameOutbox
{
    /// <summary>Stages an approved private Draft invalidation in the caller's transaction.</summary>
    Task<bool> StageAsync(ProjectIdeViewerContext viewer, ProposalContext draft, CancellationToken cancellationToken);
}

/// <summary>PostgreSQL persistence for a host that supplies current authority and scoped event routing.</summary>
public sealed class ProjectDraftRenameStore(AppDbContext db, IProjectDraftMaterializationReader materializations,
    IAuditLogger audit, IProjectDraftRenameOutbox outbox) : IProjectDraftRenamePersistence
{
    public void EnsureIsolatedWriteScope()
    {
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Draft Rename requires an isolated write scope.");
    }

    public async Task<PartialSemanticModel?> LoadCurrentAsync(ProjectIdeViewerContext viewer, ProposalContext draft,
        bool forUpdate, CancellationToken cancellationToken)
    {
        if (viewer.TenantId != draft.TargetBranch.TenantId.Value ||
            (forUpdate && (!db.Database.IsNpgsql() || db.Database.CurrentTransaction is null)))
            return null;

        var candidates = db.Set<ProjectIdeDraftCandidate>();
        var query = forUpdate
            ? candidates.FromSqlInterpolated($"""
                SELECT * FROM project_ide_draft_candidates
                WHERE "TenantId" = {viewer.TenantId} AND "ProjectId" = {draft.TargetBranch.ProjectId.Value}
                  AND "BranchId" = {draft.TargetBranch.BranchId.Value} AND "ProposalId" = {draft.ProposalId.Value}
                  AND "CandidateId" = {draft.CandidateRevisionId.Value}
                FOR UPDATE
                """)
            : candidates;

        var row = await ForDraft(query.AsNoTracking(), draft)
            .SingleOrDefaultAsync(row => row.CandidateId == draft.CandidateRevisionId.Value, cancellationToken);
        if (row is null || await candidates.AnyAsync(candidate => candidate.PredecessorId == row.CandidateId, cancellationToken))
            return null;

        var input = row.Decode(draft);
        if (input is null)
            return null;

        var snapshots = await materializations.ReadAsync(viewer, input, cancellationToken);
        return snapshots is null ? null : ProjectSemanticBinder.Bind(input, snapshots, cancellationToken);
    }

    public async Task<bool> StageRenameAsync(ProjectIdeViewerContext viewer, ProjectDraftRenamePlan plan,
        CancellationToken cancellationToken)
    {
        var before = plan.Before.Source.Context.Proposal;
        var after = plan.After.Source.Context.Proposal;
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction is null || before is null || after is null ||
            viewer.TenantId != after.TargetBranch.TenantId.Value || !plan.ReferenceCoverageComplete)
            return false;

        var candidates = db.Set<ProjectIdeDraftCandidate>();
        var stored = await ForDraft(candidates.AsNoTracking(), before)
            .SingleOrDefaultAsync(row => row.CandidateId == before.CandidateRevisionId.Value, cancellationToken);
        if (stored?.Decode(before) is not { } current || plan.ApplyToDraft(current, cancellationToken) is null ||
            await candidates.AnyAsync(row => row.PredecessorId == before.CandidateRevisionId.Value ||
                row.CandidateId == after.CandidateRevisionId.Value, cancellationToken))
            return false;

        candidates.Add(ProjectIdeDraftCandidate.Create(plan.After, before.CandidateRevisionId.Value));
        await audit.LogAsync(new AuditLogEntry(viewer.UserId, "ProjectDraftRenamed", "ProjectDraftCandidate",
            after.CandidateRevisionId.Value, "A Project Draft display name changed.",
            ProjectId: after.TargetBranch.ProjectId.Value, TenantId: viewer.TenantId), cancellationToken);
        return await outbox.StageAsync(viewer, after, cancellationToken);
    }

    public async Task<ProjectDraftRenameSaved?> LoadSavedAsync(ProjectIdeViewerContext viewer, ProposalContext draft,
        CandidateRevisionId candidate, CancellationToken cancellationToken)
    {
        if (viewer.TenantId != draft.TargetBranch.TenantId.Value)
            return null;

        var row = await ForDraft(db.Set<ProjectIdeDraftCandidate>().AsNoTracking(), draft)
            .SingleOrDefaultAsync(row => row.CandidateId == candidate.Value &&
                row.PredecessorId == draft.CandidateRevisionId.Value, cancellationToken);
        var next = new ProposalContext(draft.ProposalId, candidate, draft.BaseRevision, draft.CapturedTargetHead);
        var input = row?.Decode(next);
        return input is null ? null : new(candidate, input.Source.Digest, input.InputDigest);
    }

    private static IQueryable<ProjectIdeDraftCandidate> ForDraft(IQueryable<ProjectIdeDraftCandidate> candidates,
        ProposalContext draft) => candidates.Where(row => row.TenantId == draft.TargetBranch.TenantId.Value &&
            row.ProjectId == draft.TargetBranch.ProjectId.Value && row.BranchId == draft.TargetBranch.BranchId.Value &&
            row.ProposalId == draft.ProposalId.Value);
}

internal sealed class ProjectIdeDraftCandidate
{
    private ProjectIdeDraftCandidate() { }

    public Guid CandidateId { get; private init; }
    public Guid TenantId { get; private init; }
    public Guid ProjectId { get; private init; }
    public Guid BranchId { get; private init; }
    public Guid ProposalId { get; private init; }
    public Guid? PredecessorId { get; private init; }
    public byte[] SourceBytes { get; private init; } = [];
    public byte[] InputBytes { get; private init; } = [];
    public string SourceDigest { get; private init; } = string.Empty;
    public string InputDigest { get; private init; } = string.Empty;

    public static ProjectIdeDraftCandidate Create(CompilerInputManifest input, Guid? predecessorId)
    {
        var draft = input.Source.Context.Proposal ?? throw new ArgumentException("A Draft candidate is required.", nameof(input));
        var transfer = new CompilerInputTransfer(input);
        return new()
        {
            CandidateId = draft.CandidateRevisionId.Value,
            TenantId = draft.TargetBranch.TenantId.Value,
            ProjectId = draft.TargetBranch.ProjectId.Value,
            BranchId = draft.TargetBranch.BranchId.Value,
            ProposalId = draft.ProposalId.Value,
            PredecessorId = predecessorId,
            SourceBytes = transfer.RecoverSourceBytes(),
            InputBytes = transfer.RecoverManifestBytes(),
            SourceDigest = input.Source.Digest.Value,
            InputDigest = input.InputDigest.Value
        };
    }

    public CompilerInputManifest? Decode(ProposalContext expected)
    {
        try
        {
            var digest = ContentDigest.Parse(SourceJson.Parse(JsonSerializer.SerializeToUtf8Bytes(
                new { algorithm = ContentDigest.Algorithm, value = InputDigest })));
            var input = new CompilerInputTransfer(SourceBytes, InputBytes, digest).Materialize();
            return input.Source.Context.Proposal == expected && input.Source.Digest.Value == SourceDigest ? input : null;
        }
        catch (Exception error) when (error is ArgumentException or FormatException or UnsupportedSourceException or JsonException)
        {
            return null;
        }
    }
}
