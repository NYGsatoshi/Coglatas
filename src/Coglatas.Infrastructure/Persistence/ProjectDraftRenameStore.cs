using System.Runtime.CompilerServices;
using System.Text.Json;
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

    /// <summary>Reads the current target independently of the stored Draft's captured target.</summary>
    Task<RevisionRef?> ReadCurrentHeadAsync(ProjectIdeViewerContext viewer, BranchRef branch, CancellationToken cancellationToken);
}

public interface IProjectDraftRenameOutbox
{
    /// <summary>Stages an approved private Draft invalidation in the caller's transaction.</summary>
    Task<bool> StageAsync(ProjectIdeViewerContext viewer, ProposalContext draft, CancellationToken cancellationToken);
}

public interface IProjectDraftRenameAudit
{
    /// <summary>Stages the private Draft audit event under the host's approved audit visibility policy.</summary>
    Task StageAsync(ProjectIdeViewerContext viewer, CancellationToken cancellationToken);
}

/// <summary>PostgreSQL persistence for a host that supplies current authority and scoped event routing.</summary>
public sealed class ProjectDraftRenameStore(AppDbContext db, IProjectDraftMaterializationReader materializations,
    IProjectDraftRenameAudit audit, IProjectDraftRenameOutbox outbox) : IProjectDraftRenamePersistence
{
    public void EnsureIsolatedWriteScope()
    {
        if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Draft Rename requires an isolated write scope.");
    }

    public async Task<PartialSemanticModel?> LoadCurrentAsync(ProjectIdeViewerContext viewer, ProposalContext draft,
        bool forUpdate, CancellationToken cancellationToken)
    {
        if (!CanAccess(viewer, draft.TargetBranch) || (forUpdate && db.Database.CurrentTransaction is null))
            return null;

        var candidate = draft.CandidateRevisionId.Value;
        var row = await FindAsync(draft, candidate, forUpdate, cancellationToken);
        if (row is null || await HasSuccessorAsync(draft, candidate, cancellationToken))
            return null;

        var input = row.Decode(draft);
        if (input is null)
            return null;

        var snapshots = await materializations.ReadAsync(viewer, input, cancellationToken);
        return snapshots is null ? null : ProjectSemanticBinder.Bind(input, snapshots, cancellationToken);
    }

    public async Task<bool> IsTargetCurrentAsync(ProjectIdeViewerContext viewer, ProposalContext draft,
        CancellationToken cancellationToken) => CanAccess(viewer, draft.TargetBranch) &&
        await materializations.ReadCurrentHeadAsync(viewer, draft.TargetBranch, cancellationToken) == draft.CapturedTargetHead &&
        CanAccess(viewer, draft.TargetBranch);

    public async Task<bool> StageRenameAsync(ProjectIdeViewerContext viewer, ProjectDraftRenamePlan plan,
        CancellationToken cancellationToken)
    {
        var before = plan.Before.Source.Context.Proposal;
        var after = plan.After.Source.Context.Proposal;
        if (db.Database.CurrentTransaction is null || before is null || after is null ||
            !CanAccess(viewer, after.TargetBranch) || !plan.ReferenceCoverageComplete)
            return false;

        var predecessor = before.CandidateRevisionId.Value;
        var stored = await FindAsync(before, predecessor, forUpdate: true, cancellationToken);
        if (stored?.Decode(before) is not { } current || plan.ApplyToDraft(current, cancellationToken) is null ||
            await HasSuccessorAsync(before, predecessor, cancellationToken))
            return false;

        var transfer = new CompilerInputTransfer(plan.After);
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO projectide_development.project_ide_draft_candidates
                ("CandidateId", "TenantId", "ProjectId", "BranchId", "ProposalId", "PredecessorId",
                 "SourceBytes", "InputBytes", "SourceDigest", "InputDigest")
            VALUES ({after.CandidateRevisionId.Value}, {viewer.TenantId}, {after.TargetBranch.ProjectId.Value},
                    {after.TargetBranch.BranchId.Value}, {after.ProposalId.Value}, {predecessor},
                    {transfer.RecoverSourceBytes()}, {transfer.RecoverManifestBytes()}, {plan.After.Source.Digest.Value},
                    {plan.After.InputDigest.Value})
            ON CONFLICT DO NOTHING
            """, cancellationToken);
        if (inserted != 1 || !CanAccess(viewer, after.TargetBranch))
            return false;
        await audit.StageAsync(viewer, cancellationToken);
        return await outbox.StageAsync(viewer, after, cancellationToken);
    }

    public async Task<ProjectDraftRenameSaved?> LoadSavedAsync(ProjectIdeViewerContext viewer, ProposalContext draft,
        CandidateRevisionId candidate, CancellationToken cancellationToken)
    {
        if (!CanAccess(viewer, draft.TargetBranch))
            return null;

        var row = await FindAsync(draft, candidate.Value, forUpdate: false, cancellationToken);
        if (row is null || row.PredecessorId != draft.CandidateRevisionId.Value || !CanAccess(viewer, draft.TargetBranch))
            return null;
        var next = new ProposalContext(draft.ProposalId, candidate, draft.BaseRevision, draft.CapturedTargetHead);
        var input = row.Decode(next);
        return input is null ? null : new(candidate, input.Source.Digest, input.InputDigest);
    }

    private bool CanAccess(ProjectIdeViewerContext viewer, BranchRef branch) =>
        db.Database.IsNpgsql() && db.ActiveTenantId == viewer.TenantId && viewer.TenantId == branch.TenantId.Value;

    private Task<CandidateRow?> FindAsync(ProposalContext draft, Guid candidate, bool forUpdate, CancellationToken cancellationToken)
    {
        FormattableString command = $"""
            SELECT "PredecessorId", "SourceBytes", "InputBytes", "SourceDigest", "InputDigest"
            FROM projectide_development.project_ide_draft_candidates
            WHERE "TenantId" = {draft.TargetBranch.TenantId.Value} AND "ProjectId" = {draft.TargetBranch.ProjectId.Value}
                AND "BranchId" = {draft.TargetBranch.BranchId.Value} AND "ProposalId" = {draft.ProposalId.Value}
                AND "CandidateId" = {candidate}
            """;
        if (forUpdate)
            command = FormattableStringFactory.Create(command.Format + " FOR UPDATE", command.GetArguments());
        return db.Database.SqlQuery<CandidateRow>(command).SingleOrDefaultAsync(cancellationToken);
    }

    private Task<bool> HasSuccessorAsync(ProposalContext draft, Guid candidate, CancellationToken cancellationToken) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT 1 FROM projectide_development.project_ide_draft_candidates
                WHERE "TenantId" = {draft.TargetBranch.TenantId.Value} AND "ProjectId" = {draft.TargetBranch.ProjectId.Value}
                    AND "BranchId" = {draft.TargetBranch.BranchId.Value} AND "ProposalId" = {draft.ProposalId.Value}
                    AND "PredecessorId" = {candidate}) AS "Value"
            """).SingleAsync(cancellationToken);

    private sealed record CandidateRow(Guid? PredecessorId, byte[] SourceBytes, byte[] InputBytes,
        string SourceDigest, string InputDigest)
    {
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
}
