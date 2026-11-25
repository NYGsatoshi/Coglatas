using System.Text;
using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Runtime;

public sealed record ProjectDraftRenameRequest(ProjectLocation Target, ContentDigest SourceDigest,
    ContentDigest InputDigest, string DisplayName);

public sealed record ProjectDraftRenameConfirmation(ProjectDraftRenameRequest Request,
    CandidateRevisionId NextCandidate, ContentDigest PreviewDigest, string RequestIdentity);

public sealed record ProjectDraftRenamePreview(CandidateRevisionId NextCandidate, ContentDigest PreviewDigest,
    string? PreviousName, string DisplayName);

public sealed record ProjectDraftRenameSaved(CandidateRevisionId Candidate, ContentDigest SourceDigest,
    ContentDigest InputDigest);

public enum ProjectDraftRenameSaveState { Saved, Unavailable, RequestMismatch }

public sealed record ProjectDraftRenameSaveResult(ProjectDraftRenameSaveState State, ProjectDraftRenameSaved? Saved = null);

public interface IProjectDraftRenamePersistence
{
    /// <summary>Ensures the idempotency transaction cannot flush an unrelated write.</summary>
    void EnsureIsolatedWriteScope();

    /// <summary>Loads the currently admitted Draft and its permitted materializations.</summary>
    Task<PartialSemanticModel?> LoadCurrentAsync(ProjectIdeViewerContext viewer, ProposalContext draft,
        bool forUpdate, CancellationToken cancellationToken);

    Task<bool> IsTargetCurrentAsync(ProjectIdeViewerContext viewer, ProposalContext draft, CancellationToken cancellationToken);

    /// <summary>Stages a compare-and-swap, immutable revision, audit and required Outbox in the current transaction.</summary>
    Task<bool> StageRenameAsync(ProjectIdeViewerContext viewer, ProjectDraftRenamePlan plan,
        CancellationToken cancellationToken);

    /// <summary>Reads the original result for the exact Draft and idempotency record's candidate.</summary>
    Task<ProjectDraftRenameSaved?> LoadSavedAsync(ProjectIdeViewerContext viewer, ProposalContext draft,
        CandidateRevisionId candidate, CancellationToken cancellationToken);
}

/// <summary>Development use case. Operation permissions and persisted Drafts must come from the host.</summary>
public sealed class ProjectDraftRenameService(ProjectIdeOperationAccess access,
    IProjectDraftRenamePersistence drafts, ICreateIdempotencyCoordinator idempotency)
{
    private static readonly SecurityOperationRef PreviewOperation = new("coglatas.draft.rename.preview");
    private static readonly SecurityOperationRef SaveOperation = new("coglatas.draft.rename.apply");
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const string FingerprintDomain = "coglatas.draft-rename-confirmation/1";

    public async Task<ProjectDraftRenamePreview?> PreviewAsync(ProjectDraftRenameRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!HasBoundedName(request.DisplayName) || request.Target.Context.Proposal is not { } draft ||
            access.CaptureViewer() is not { } viewer ||
            !await IsAllowedAsync(viewer, PreviewOperation, request, cancellationToken))
            return null;

        var next = CandidateRevisionId.New();
        var plan = await PrepareAsync(viewer, request, draft, next, forUpdate: false, cancellationToken);
        if (plan is null || !await drafts.IsTargetCurrentAsync(viewer, draft, cancellationToken) ||
            !await IsAllowedAsync(viewer, PreviewOperation, request, cancellationToken))
            return null;

        return new(next, DigestPlan(plan), plan.PreviousName, plan.NewName);
    }

    public async Task<ProjectDraftRenameSaveResult> SaveAsync(ProjectDraftRenameConfirmation confirmation,
        CancellationToken cancellationToken = default)
    {
        var request = confirmation.Request;
        var unavailable = new ProjectDraftRenameSaveResult(ProjectDraftRenameSaveState.Unavailable);
        if (!HasBoundedName(request.DisplayName) || request.Target.Context.Proposal is not { } draft ||
            access.CaptureViewer() is not { } viewer ||
            confirmation.NextCandidate.Value == Guid.Empty || confirmation.NextCandidate == draft.CandidateRevisionId ||
            !IsValidRequestIdentity(confirmation.RequestIdentity) ||
            !await IsAllowedAsync(viewer, SaveOperation, request, cancellationToken))
            return unavailable;

        var fingerprint = ContentDigest.Compute(FingerprintDomain, SourceJson.Parse(JsonSerializer.SerializeToUtf8Bytes(new
        {
            target = ContentDigest.Compute("coglatas.draft-rename-target/1", request.Target.Data.ToCanonicalBytes()).Value,
            source = request.SourceDigest.Value,
            input = request.InputDigest.Value,
            name = request.DisplayName,
            candidate = confirmation.NextCandidate.ToString(),
            preview = confirmation.PreviewDigest.Value
        })).ToCanonicalBytes());
        var binding = new CreateIdempotencyContext(viewer.TenantId, viewer.UserId, SaveOperation.OperationId,
            confirmation.RequestIdentity, fingerprint.Value, "ProjectDraftCandidate", confirmation.NextCandidate.Value);

        try
        {
            drafts.EnsureIsolatedWriteScope();
            var result = await idempotency.ExecuteWithCommitCheckAsync(binding, async token =>
            {
                var plan = await PrepareAsync(viewer, request, draft, confirmation.NextCandidate, forUpdate: true, token);
                if (plan is null || DigestPlan(plan) != confirmation.PreviewDigest ||
                    !await IsAllowedAsync(viewer, SaveOperation, request, token) ||
                    !await drafts.StageRenameAsync(viewer, plan, token) ||
                    !await IsAllowedAsync(viewer, SaveOperation, request, token))
                    throw new DraftRenameUnavailableException();

                return new ProjectDraftRenameSaved(confirmation.NextCandidate, plan.After.Source.Digest, plan.After.InputDigest);
            }, async (candidateId, token) =>
            {
                if (candidateId != confirmation.NextCandidate.Value ||
                    !await IsAllowedAsync(viewer, SaveOperation, request, token))
                    return null;

                var saved = await drafts.LoadSavedAsync(viewer, draft, new(candidateId), token);
                return saved is { } && saved.Candidate == confirmation.NextCandidate ? saved : null;
            }, async token =>
            {
                if (!await drafts.IsTargetCurrentAsync(viewer, draft, token) ||
                    !await IsAllowedAsync(viewer, SaveOperation, request, token))
                    throw new DraftRenameUnavailableException();
            }, cancellationToken);

            // Check again after the transaction or replay lookup before sending even receipt metadata.
            if (!await IsAllowedAsync(viewer, SaveOperation, request, cancellationToken))
                return unavailable;
            if (result.Disposition == IdempotentCreateDisposition.RequestMismatch)
                return new(ProjectDraftRenameSaveState.RequestMismatch);
            return result.Value is { } saved ? new(ProjectDraftRenameSaveState.Saved, saved) : unavailable;
        }
        catch (DraftRenameUnavailableException)
        {
            return unavailable;
        }
    }

    private async Task<ProjectDraftRenamePlan?> PrepareAsync(ProjectIdeViewerContext viewer,
        ProjectDraftRenameRequest request, ProposalContext draft, CandidateRevisionId next, bool forUpdate,
        CancellationToken cancellationToken)
    {
        var model = await drafts.LoadCurrentAsync(viewer, draft, forUpdate, cancellationToken);
        if (model is null || !model.Input.Source.Context.Equals(request.Target.Context) ||
            model.Input.Source.Digest != request.SourceDigest || model.Input.InputDigest != request.InputDigest ||
            !await drafts.IsTargetCurrentAsync(viewer, draft, cancellationToken))
            return null;

        var nextContext = SourceRevisionContext.Candidate(new(draft.ProposalId, next, draft.BaseRevision, draft.CapturedTargetHead));
        try
        {
            var preview = ProjectDraftRename.Preview(model, request.Target, request.InputDigest,
                request.DisplayName, nextContext, cancellationToken);
            return preview.Plan is { ReferenceCoverageComplete: true } plan ? plan : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private Task<bool> IsAllowedAsync(ProjectIdeViewerContext viewer, SecurityOperationRef operation,
        ProjectDraftRenameRequest request, CancellationToken cancellationToken) =>
        access.IsAllowedAsync(viewer, operation, SecuritySourceIdentitySnapshot.Capture(request.Target.Context,
            request.InputDigest), request.SourceDigest.Value, cancellationToken);

    private static ContentDigest DigestPlan(ProjectDraftRenamePlan plan) =>
        ContentDigest.Compute("coglatas.draft-rename-preview/1", plan.Data.ToCanonicalBytes());

    private static bool HasBoundedName(string name)
    {
        if (name.Length > ProjectDraftRename.MaximumNameBytes)
            return false;
        try
        {
            return StrictUtf8.GetByteCount(name) <= ProjectDraftRename.MaximumNameBytes;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsValidRequestIdentity(string value) =>
        value.Length is >= 8 and <= 128 && !string.IsNullOrWhiteSpace(value) &&
        value.All(character => character is >= ' ' and <= '~');

    private sealed class DraftRenameUnavailableException : Exception;
}
