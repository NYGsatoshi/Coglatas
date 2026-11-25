using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Coglatas.Application.ProjectIde.Runtime;
using Coglatas.Domain.ProjectIde;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>Trusted Branch head lookup supplied by explicit history composition.</summary>
public interface IProjectHistoryHeadReader
{
    ValueTask<RevisionRef?> ReadAsync(BranchRef branch, CancellationToken cancellationToken);
}

/// <summary>Loads bounded retained checkpoints from the current development Tenant.</summary>
public sealed class ProjectHistoryCheckpointStore(AppDbContext database, IProjectHistoryHeadReader heads) : IProjectHistoryRuntimeStore
{
    private static readonly JsonSerializerOptions ParentOptions = new()
    {
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async ValueTask<ProjectHistoryWindow?> CaptureAsync(IReadOnlyList<ProjectHistorySource> sources, RevisionRef? requiredHead,
        CancellationToken cancellationToken)
    {
        if (!await HeadMatchesAsync(requiredHead, cancellationToken))
            return null;

        var records = await ReadMetadataAsync(sources, cancellationToken);
        if (records is null)
            return null;

        if (requiredHead is not null && !await ResolveHistoryAsync(records, requiredHead, cancellationToken))
            return null;

        return new ProjectHistoryWindow(VersionOf(records), records.Values.Select(record => record.Identity));
    }

    public async ValueTask<IReadOnlyList<ProjectHistoryStoredSource>?> LoadAsync(ProjectHistoryWindow window, CancellationToken cancellationToken)
    {
        var metadata = await ReadMetadataAsync(window.Sources, cancellationToken);
        if (metadata is null || VersionOf(metadata) != window.Version)
            return null;

        var results = new List<ProjectHistoryStoredSource>(metadata.Count);
        var remainingSourceBytes = ProjectSourceHistory.MaximumStoredBytes;
        var remainingInputBytes = ProjectSourceHistory.MaximumStoredBytes;
        foreach (var (revision, record) in metadata)
        {
            var tenant = revision.Branch.TenantId.Value;
            if (database.ActiveTenantId != tenant)
                return null;

            var project = revision.Branch.ProjectId.Value;
            var branch = revision.Branch.BranchId.Value;
            var row = await database.Database.SqlQuery<CheckpointBody>($"""
                SELECT "SourceBytes", "InputBytes" FROM projectide_development.projectide_history_checkpoints
                WHERE "TenantId" = {tenant} AND "ProjectId" = {project} AND "BranchId" = {branch}
                  AND "RevisionId" = {revision.RevisionId.Value} AND "VersionId" = {record.Version}
                  AND "DeletedAtUtc" IS NULL AND octet_length("SourceBytes") <= {remainingSourceBytes}
                  AND ("InputBytes" IS NULL OR octet_length("InputBytes") <= {remainingInputBytes})
                """).SingleOrDefaultAsync(cancellationToken);
            if (row?.SourceBytes is null)
                return null;

            remainingSourceBytes -= row.SourceBytes.Length;
            remainingInputBytes -= row.InputBytes?.Length ?? 0;
            try
            {
                var source = ReadCanonical(row.SourceBytes);
                var input = row.InputBytes is null ? null : ReadCanonical(row.InputBytes);
                results.Add(new(ProjectHistoryRevision.MaterializedCheckpoint(revision, record.Sequence, record.Identity.SourceDigest,
                    source, record.Parents, cancellationToken), input));
            }
            catch (FormatException) { return null; }
            catch (ArgumentException) { return null; }
        }
        return results.ToImmutableList();
    }

    public async ValueTask<bool> IsCurrentAsync(ProjectHistoryWindow window, RevisionRef? requiredHead, CancellationToken cancellationToken)
    {
        if (!await HeadMatchesAsync(requiredHead, cancellationToken) || database.ActiveTenantId is not { } tenant ||
            window.Sources.Count is 0 or > ProjectSourceHistory.MaximumRecords ||
            window.Sources.Any(source => source.Revision.Branch.TenantId.Value != tenant))
            return false;

        var expected = JsonSerializer.Serialize(window.Sources.Select(source => new
        {
            ProjectId = source.Revision.Branch.ProjectId.Value,
            BranchId = source.Revision.Branch.BranchId.Value,
            RevisionId = source.Revision.RevisionId.Value,
            Context = source.Context.Data.CanonicalText,
            SourceDigest = source.SourceDigest.Value,
            InputDigest = source.InputDigest.Value
        }));
        // Check the complete window in one database statement so its members share one visibility snapshot.
        var retained = await database.Database.SqlQuery<CheckpointVersion>($"""
            SELECT checkpoint."BranchId", checkpoint."RevisionId", checkpoint."VersionId"
            FROM projectide_development.projectide_history_checkpoints AS checkpoint
            JOIN jsonb_to_recordset(CAST({expected} AS jsonb)) AS expected
                ("ProjectId" uuid, "BranchId" uuid, "RevisionId" uuid, "Context" text, "SourceDigest" text, "InputDigest" text)
                ON checkpoint."ProjectId" = expected."ProjectId"
               AND checkpoint."BranchId" = expected."BranchId"
               AND checkpoint."RevisionId" = expected."RevisionId"
               AND checkpoint."Context" = expected."Context"
               AND checkpoint."SourceDigest" = expected."SourceDigest"
               AND checkpoint."InputDigest" = expected."InputDigest"
            WHERE checkpoint."TenantId" = {tenant} AND checkpoint."DeletedAtUtc" IS NULL
            """).ToListAsync(cancellationToken);
        return retained.Count == window.Sources.Count && database.ActiveTenantId == tenant &&
            VersionOf(retained.Select(row => (row.BranchId, row.RevisionId, row.VersionId))) == window.Version;
    }

    private async Task<bool> HeadMatchesAsync(RevisionRef? requiredHead, CancellationToken cancellationToken) =>
        requiredHead is null || (database.ActiveTenantId == requiredHead.Branch.TenantId.Value &&
            requiredHead == await heads.ReadAsync(requiredHead.Branch, cancellationToken));

    private async Task<Dictionary<RevisionRef, Checkpoint>?> ReadMetadataAsync(IReadOnlyList<ProjectHistorySource> sources,
        CancellationToken cancellationToken)
    {
        if (sources.Count is 0 or > ProjectSourceHistory.MaximumRecords)
            return null;

        var result = new Dictionary<RevisionRef, Checkpoint>();
        var remainingBytes = ProjectSourceHistory.MaximumStoredBytes;
        foreach (var expected in sources)
        {
            var record = await FindAsync(expected.Revision, cancellationToken);
            if (record?.Identity != expected)
                return null;
            if (result.TryAdd(expected.Revision, record))
                remainingBytes -= ContextBytes(record);
            if (remainingBytes < 0)
                return null;
        }
        return result;
    }

    private async Task<bool> ResolveHistoryAsync(Dictionary<RevisionRef, Checkpoint> records, RevisionRef requiredHead,
        CancellationToken cancellationToken)
    {
        var pending = records.Keys.ToHashSet();
        var visited = new HashSet<RevisionRef>();
        var current = requiredHead;
        var remainingBytes = ProjectSourceHistory.MaximumStoredBytes - records.Values.Sum(ContextBytes);
        while (pending.Count > 0 && visited.Count < ProjectSourceHistory.MaximumReconstructionDepth && visited.Add(current))
        {
            if (!records.TryGetValue(current, out var checkpoint))
            {
                checkpoint = await FindAsync(current, cancellationToken);
                if (checkpoint is null)
                    return false;
                remainingBytes -= ContextBytes(checkpoint);
                if (remainingBytes < 0)
                    return false;
                records.Add(current, checkpoint);
            }
            pending.Remove(current);
            if (pending.Count == 0)
                return true;
            if (checkpoint.Parents is not [var parent] || parent.Branch != requiredHead.Branch)
                return false;
            current = parent;
        }
        return false;
    }

    private async Task<Checkpoint?> FindAsync(RevisionRef revision, CancellationToken cancellationToken)
    {
        var tenant = revision.Branch.TenantId.Value;
        if (database.ActiveTenantId != tenant)
            return null;

        var row = await database.Database.SqlQuery<CheckpointMetadata>($"""
            SELECT "VersionId", "BranchSequence", "Context", "SourceDigest", "InputDigest", CAST("Parents" AS text) AS "Parents"
            FROM projectide_development.projectide_history_checkpoints
            WHERE "TenantId" = {tenant} AND "ProjectId" = {revision.Branch.ProjectId.Value}
              AND "BranchId" = {revision.Branch.BranchId.Value} AND "RevisionId" = {revision.RevisionId.Value}
              AND "DeletedAtUtc" IS NULL
            """).SingleOrDefaultAsync(cancellationToken);
        if (row is null || row.VersionId == Guid.Empty)
            return null;

        try
        {
            var context = SourceRevisionContext.Parse(SourceJson.Parse(row.Context));
            if (context.CommittedRevision != revision || context.Data.CanonicalText != row.Context)
                return null;
            var parents = JsonSerializer.Deserialize<ParentLink?[]>(row.Parents, ParentOptions);
            if (parents is null || parents.Length > 8 || parents.Any(parent => parent is null))
                return null;
            var exactParents = parents.Select(parent => new RevisionRef(
                new BranchRef(revision.Branch.TenantId, revision.Branch.ProjectId, new BranchId(parent!.BranchId)),
                new RevisionId(parent.RevisionId))).ToArray();
            return new(row.VersionId, row.BranchSequence, new(context, Digest(row.SourceDigest), Digest(row.InputDigest)), exactParents);
        }
        catch (FormatException) { return null; }
        catch (ArgumentException) { return null; }
        catch (JsonException) { return null; }
        catch (UnsupportedSourceException) { return null; }
    }

    private static ContentDigest Digest(string value) => ContentDigest.Parse(SourceJson.Parse($$"""{"algorithm":"sha-256","value":"{{value}}"}"""));
    private static int ContextBytes(Checkpoint record) => Encoding.UTF8.GetByteCount(record.Identity.Context.Data.CanonicalText);

    private static SourceJson ReadCanonical(byte[] bytes)
    {
        var value = SourceJson.Parse(bytes);
        if (!value.ToCanonicalBytes().AsSpan().SequenceEqual(bytes))
            throw new FormatException("History body is not canonical.");
        return value;
    }

    private static Guid VersionOf(Dictionary<RevisionRef, Checkpoint> records) => VersionOf(records.Values.Select(record =>
        (record.Identity.Revision.Branch.BranchId.Value, record.Identity.Revision.RevisionId.Value, record.Version)));

    private static Guid VersionOf(IEnumerable<(Guid BranchId, Guid RevisionId, Guid Version)> records)
    {
        var version = string.Join('\n', records.Select(record => string.Join('/', record.BranchId, record.RevisionId, record.Version))
            .Order(StringComparer.Ordinal));
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(version)).AsSpan(0, 16));
    }

    private sealed record Checkpoint(Guid Version, long Sequence, ProjectHistorySource Identity, IReadOnlyList<RevisionRef> Parents);
    private sealed record ParentLink(Guid BranchId, Guid RevisionId);

    private sealed class CheckpointMetadata
    {
        public Guid VersionId { get; set; }
        public long BranchSequence { get; set; }
        public string Context { get; set; } = "";
        public string SourceDigest { get; set; } = "";
        public string InputDigest { get; set; } = "";
        public string Parents { get; set; } = "";
    }

    private sealed class CheckpointBody
    {
        public byte[]? SourceBytes { get; set; }
        public byte[]? InputBytes { get; set; }
    }

    private sealed class CheckpointVersion
    {
        public Guid BranchId { get; set; }
        public Guid RevisionId { get; set; }
        public Guid VersionId { get; set; }
    }
}
