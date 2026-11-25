using System.Collections.Immutable;
using System.Text.Json;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Runtime;

public sealed record ProjectHistorySource
{
    public ProjectHistorySource(SourceRevisionContext context, ContentDigest sourceDigest, ContentDigest inputDigest)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sourceDigest);
        ArgumentNullException.ThrowIfNull(inputDigest);
        Revision = context.CommittedRevision ?? throw new ArgumentException("History requires a committed Source.", nameof(context));
        Context = context;
        SourceDigest = sourceDigest;
        InputDigest = inputDigest;
    }

    public RevisionRef Revision { get; }
    public SourceRevisionContext Context { get; }
    public ContentDigest SourceDigest { get; }
    public ContentDigest InputDigest { get; }
}

/// <summary>Preconfigured operations whose policies require current History capability and exact Source scope.</summary>
public sealed record ProjectHistoryOperations(SecurityOperationRef Query, SecurityOperationRef Materialize,
    SecurityOperationRef Compare, SecurityOperationRef Navigate, SecurityOperationRef Export, SecurityOperationRef PreviewRevert);

public sealed class ProjectHistoryWindow(Guid version, IEnumerable<ProjectHistorySource> sources)
{
    public Guid Version { get; } = version;
    public IReadOnlyList<ProjectHistorySource> Sources { get; } = sources.Take(ProjectSourceHistory.MaximumRecords + 1).ToImmutableList();
}

public sealed record ProjectHistoryStoredSource(ProjectHistoryRevision Record, SourceJson? InputEnvelope);

/// <summary>Development storage contract for a consistent retained Source window.</summary>
public interface IProjectHistoryRuntimeStore
{
    /// <summary>Returns identities only. Omit tombstoned data and unprovable dependency windows.</summary>
    ValueTask<ProjectHistoryWindow?> CaptureAsync(IReadOnlyList<ProjectHistorySource> sources, RevisionRef? requiredHead,
        CancellationToken cancellationToken);

    /// <summary>Returns original retained bodies from the captured version or no result if it has changed.</summary>
    ValueTask<IReadOnlyList<ProjectHistoryStoredSource>?> LoadAsync(ProjectHistoryWindow window, CancellationToken cancellationToken);

    /// <summary>Confirms the captured retention version, all retained identities and any required live head.</summary>
    ValueTask<bool> IsCurrentAsync(ProjectHistoryWindow window, RevisionRef? requiredHead, CancellationToken cancellationToken);
}

public enum ProjectHistoryAccessState { Ready, NotFoundOrDenied, Unavailable }

public sealed record ProjectHistoryAccessResult<T>(ProjectHistoryAccessState State, T? Value) where T : class;

/// <summary>Exact Source history use cases with separate operation authority and a final disclosure check.</summary>
public sealed class ProjectHistoryService(ProjectIdeOperationAccess access, IProjectHistoryRuntimeStore store,
    ProjectHistoryOperations operations)
{
    public Task<ProjectHistoryAccessResult<IReadOnlyList<RevisionRef>>> QueryAsync(ProjectHistorySource source,
        CancellationToken cancellationToken = default) =>
        ReadWindowAsync([source], operations.Query, null, (window, _) =>
            ValueTask.FromResult<IReadOnlyList<RevisionRef>?>(window.Sources.Select(item => item.Revision).ToImmutableList()), cancellationToken);

    public Task<ProjectHistoryAccessResult<ProjectSource>> MaterializeAsync(ProjectHistorySource source,
        CancellationToken cancellationToken = default) =>
        ReadAsync([source], operations.Materialize, null, (window, token) => window.Reconstruct(source, token)?.Source, cancellationToken);

    public Task<ProjectHistoryAccessResult<SourceDocument>> NavigateAsync(ProjectHistorySource source, DocumentId documentId,
        CancellationToken cancellationToken = default) =>
        ReadAsync([source], operations.Navigate, null, (window, token) => window.Reconstruct(source, token)?.Source?.Documents
            .SingleOrDefault(document => document.DocumentId == documentId), cancellationToken);

    public Task<ProjectHistoryAccessResult<SourceJson>> ExportAsync(ProjectHistorySource source,
        CancellationToken cancellationToken = default) =>
        ReadAsync([source], operations.Export, null, (window, token) => window.Reconstruct(source, token)?.Source?.Data, cancellationToken);

    public Task<ProjectHistoryAccessResult<ProjectSemanticDiff>> CompareAsync(ProjectHistorySource before, ProjectHistorySource after,
        CancellationToken cancellationToken = default) =>
        ReadAsync([before, after], operations.Compare, null, (window, token) =>
        {
            var left = window.Bind(before, token);
            var right = window.Bind(after, token);
            var diff = ProjectSemanticDiff.Compare(left, right, before.InputDigest, after.InputDigest, cancellationToken: token);
            return diff.State == SemanticDiffState.Complete ? diff : null;
        }, cancellationToken);

    public Task<ProjectHistoryAccessResult<DraftRevertPreview>> PreviewRevertAsync(ProjectHistorySource parent,
        ProjectHistorySource selected, ProjectHistorySource current, ProposalContext proposal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (proposal.BaseRevision != current.Revision || proposal.CapturedTargetHead != current.Revision ||
            parent.Revision.Branch != current.Revision.Branch || selected.Revision.Branch != current.Revision.Branch)
            return Task.FromResult(new ProjectHistoryAccessResult<DraftRevertPreview>(ProjectHistoryAccessState.NotFoundOrDenied, null));

        return ReadAsync<DraftRevertPreview>([parent, selected, current], operations.PreviewRevert, current.Revision, (window, token) =>
        {
            var left = window.Bind(parent, token);
            var right = window.Bind(selected, token);
            var head = window.Bind(current, token);
            if (left is null || right is null || head is null)
                return null;

            var preview = ProjectDraftRevert.Preview(left, right, head, parent.InputDigest, selected.InputDigest, current.InputDigest,
                proposal, window.Records, token);
            return preview.Plan is not null ? preview : new DraftRevertPreview(preview.State, null, []);
        }, cancellationToken);
    }

    private Task<ProjectHistoryAccessResult<T>> ReadAsync<T>(ProjectHistorySource[] requested, SecurityOperationRef operation,
        RevisionRef? requiredHead, Func<MaterializedWindow, CancellationToken, T?> project, CancellationToken cancellationToken) where T : class =>
        ReadWindowAsync<T>(requested, operation, requiredHead, async (window, token) =>
        {
            var stored = await store.LoadAsync(window, token);
            var materialized = MaterializedWindow.Create(window.Sources, stored);
            return materialized is null ? null : project(materialized, token);
        }, cancellationToken);

    private async Task<ProjectHistoryAccessResult<T>> ReadWindowAsync<T>(ProjectHistorySource[] requested, SecurityOperationRef operation,
        RevisionRef? requiredHead, Func<ProjectHistoryWindow, CancellationToken, ValueTask<T?>> read,
        CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var viewer = access.CaptureViewer();
        var scope = requested[0].Revision.Branch;
        if (viewer is null || requested.Any(item => item.Revision.Branch.TenantId != scope.TenantId || item.Revision.Branch.ProjectId != scope.ProjectId) ||
            !await AllowedAsync(viewer, operation, requested, cancellationToken))
            return new(ProjectHistoryAccessState.NotFoundOrDenied, null);

        var window = await store.CaptureAsync(requested.ToImmutableList(), requiredHead, cancellationToken);
        if (window is null)
            return await AllowedAsync(viewer, operation, requested, cancellationToken)
                ? new(ProjectHistoryAccessState.Unavailable, null)
                : new(ProjectHistoryAccessState.NotFoundOrDenied, null);

        var identities = window.Sources;
        if (window.Version == Guid.Empty || identities.Count is 0 or > ProjectSourceHistory.MaximumRecords ||
            identities.Any(item => item.Revision.Branch.TenantId != scope.TenantId || item.Revision.Branch.ProjectId != scope.ProjectId) ||
            identities.DistinctBy(item => item.Revision).Count() != identities.Count ||
            requested.Any(item => !identities.Contains(item)) || !await store.IsCurrentAsync(window, requiredHead, cancellationToken) ||
            !await AllowedAsync(viewer, operation, identities, cancellationToken))
            return new(ProjectHistoryAccessState.NotFoundOrDenied, null);

        var value = await read(window, cancellationToken);
        if (!await store.IsCurrentAsync(window, requiredHead, cancellationToken) ||
            !await AllowedAsync(viewer, operation, identities, cancellationToken))
            return new(ProjectHistoryAccessState.NotFoundOrDenied, null);

        cancellationToken.ThrowIfCancellationRequested();
        return value is null ? new(ProjectHistoryAccessState.Unavailable, null) : new(ProjectHistoryAccessState.Ready, value);
    }

    private async Task<bool> AllowedAsync(ProjectIdeViewerContext viewer, SecurityOperationRef operation,
        IEnumerable<ProjectHistorySource> sources, CancellationToken cancellationToken)
    {
        foreach (var source in sources)
        {
            if (!await access.IsAllowedAsync(viewer, operation, SecuritySourceIdentitySnapshot.Capture(source.Context, source.InputDigest),
                    source.SourceDigest.Value, cancellationToken))
                return false;
        }
        return true;
    }

    private sealed class MaterializedWindow(Dictionary<RevisionRef, ProjectHistoryStoredSource> sources)
    {
        public ProjectHistoryRevision[] Records { get; } = sources.Values.Select(source => source.Record).ToArray();

        public static MaterializedWindow? Create(IReadOnlyList<ProjectHistorySource> identities, IReadOnlyList<ProjectHistoryStoredSource>? stored)
        {
            if (stored is null || stored.Count != identities.Count)
                return null;

            var byRevision = new Dictionary<RevisionRef, ProjectHistoryStoredSource>();
            foreach (var item in stored.Take(identities.Count + 1))
            {
                if (!byRevision.TryAdd(item.Record.Revision, item))
                    return null;
            }
            return byRevision.Count == identities.Count && identities.All(identity =>
                byRevision.TryGetValue(identity.Revision, out var item) && MatchesSource(item.Record, identity))
                ? new MaterializedWindow(byRevision) : null;
        }

        private static bool MatchesSource(ProjectHistoryRevision record, ProjectHistorySource identity)
        {
            var metadata = record.CheckpointData ?? record.Delta?.AfterMetadata;
            return record.ExpectedSourceDigest.Equals(identity.SourceDigest) && metadata?.Value.ValueKind == JsonValueKind.Object &&
                metadata.Value.TryGetProperty("context", out var context) && context.GetRawText() == identity.Context.Data.CanonicalText;
        }

        public HistoryReconstruction? Reconstruct(ProjectHistorySource identity, CancellationToken cancellationToken)
        {
            var result = ProjectSourceHistory.Read(identity.Revision, Records, cancellationToken);
            return result.Source is { } source && source.Context.Equals(identity.Context) && source.Digest.Equals(identity.SourceDigest)
                ? result : null;
        }

        public PartialSemanticModel? Bind(ProjectHistorySource identity, CancellationToken cancellationToken)
        {
            if (Reconstruct(identity, cancellationToken)?.Source is not { } source || sources[identity.Revision].InputEnvelope is not { } envelope)
                return null;
            try
            {
                var input = CompilerInputManifest.ReadEnvelope(source, envelope);
                return input.InputDigest.Equals(identity.InputDigest) ? ProjectSemanticBinder.Bind(input, cancellationToken: cancellationToken) : null;
            }
            catch (FormatException) { return null; }
            catch (ArgumentException) { return null; }
            catch (UnsupportedSourceException) { return null; }
        }
    }
}
