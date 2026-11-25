using System.Text.Json;
using System.Text.Json.Serialization;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Application.ProjectIde.Runtime;
using Coglatas.Domain.ProjectIde;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>PostgreSQL recording evidence used only by explicitly composed development hosts.</summary>
public sealed class CompilerRecordingStore(AppDbContext context, ProjectIdeOperationAccess access, IClock clock) : ICompilerRecordingStore
{
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<CompilerRecordingWriteOutcome> RecordAsync(LocalCompilerRecordedInvocation invocation,
        CancellationToken cancellationToken)
    {
        var viewer = access.CaptureViewer();
        if (viewer is null)
            return CompilerRecordingWriteOutcome.Unavailable;

        var evidence = CompilerRecordingEvidence.FromInvocation(invocation);
        if (!await CanRecordAsync(viewer, evidence, cancellationToken))
            return CompilerRecordingWriteOutcome.Unavailable;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (evidence.OriginRunId is { } originId && !await OriginMatchesAsync(evidence, originId, cancellationToken))
            return CompilerRecordingWriteOutcome.Unavailable;

        var payload = JsonSerializer.Serialize(evidence, PayloadOptions);
        var recordedAtUtc = clock.UtcNow;
        var inserted = await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO projectide_compiler_recording_evidence
                ("TenantId", "ProjectId", "RunId", "AttemptId", "ActorUserId", "RecordedAtUtc", "Payload")
            VALUES ({evidence.Source.TenantId}, {evidence.Source.ProjectId}, {evidence.RunId}, {evidence.AttemptId},
                {viewer.UserId}, {recordedAtUtc}, CAST({payload} AS jsonb))
            ON CONFLICT DO NOTHING
            """, cancellationToken);

        if (inserted == 0)
        {
            var earlier = await FindRowAsync(evidence.Source.TenantId, evidence.Source.ProjectId, evidence.RunId, cancellationToken);
            if (earlier is null || earlier.ActorUserId != viewer.UserId || ReadEvidence(earlier) != evidence)
                return CompilerRecordingWriteOutcome.Unavailable;
        }

        if (!await CanRecordAsync(viewer, evidence, cancellationToken))
            return CompilerRecordingWriteOutcome.Unavailable;

        await transaction.CommitAsync(cancellationToken);
        return inserted == 1 ? CompilerRecordingWriteOutcome.Recorded : CompilerRecordingWriteOutcome.AlreadyRecorded;
    }

    public async Task<CompilerRecordingEvidence?> FindAsync(SecuritySourceIdentitySnapshot source, string sourceDigest,
        Guid runId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var viewer = access.CaptureViewer();
        if (viewer is null || runId == Guid.Empty ||
            !await access.IsAllowedAsync(viewer, CompilerRecordingOperations.Read, source, sourceDigest, cancellationToken))
            return null;

        var row = await FindRowAsync(source.TenantId, source.ProjectId, runId, cancellationToken);
        var evidence = row is null ? null : ReadEvidence(row);
        if (evidence is null || evidence.Source != source || evidence.SourceDigest != sourceDigest)
            return null;

        return await access.IsAllowedAsync(viewer, CompilerRecordingOperations.Read, evidence.Source, evidence.SourceDigest, cancellationToken)
            ? evidence : null;
    }

    private async Task<bool> CanRecordAsync(ProjectIdeViewerContext viewer, CompilerRecordingEvidence evidence, CancellationToken cancellationToken) =>
        await access.IsAllowedAsync(viewer, CompilerRecordingOperations.Record, evidence.Source, evidence.SourceDigest, cancellationToken) &&
        (evidence.OriginRunId is null ||
         await access.IsAllowedAsync(viewer, CompilerRecordingOperations.Read, evidence.Source, evidence.SourceDigest, cancellationToken));

    private async Task<bool> OriginMatchesAsync(CompilerRecordingEvidence evidence, Guid originId, CancellationToken cancellationToken)
    {
        var origin = await FindRowAsync(evidence.Source.TenantId, evidence.Source.ProjectId, originId, cancellationToken);
        var original = origin is null ? null : ReadEvidence(origin);
        return original is not null && original.Source == evidence.Source && original.SourceDigest == evidence.SourceDigest &&
            original.ExecutionInputDigest == evidence.ExecutionInputDigest && original.ReceiptDigest == evidence.OriginReceiptDigest;
    }

    private Task<CompilerRecordingEvidenceRow?> FindRowAsync(Guid tenantId, Guid projectId, Guid runId, CancellationToken cancellationToken) =>
        context.Set<CompilerRecordingEvidenceRow>().AsNoTracking().SingleOrDefaultAsync(row =>
            row.TenantId == tenantId && row.ProjectId == projectId && row.RunId == runId, cancellationToken);

    private static CompilerRecordingEvidence? ReadEvidence(CompilerRecordingEvidenceRow row)
    {
        try
        {
            return JsonSerializer.Deserialize<CompilerRecordingEvidence>(row.Payload, PayloadOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal sealed class CompilerRecordingEvidenceRow
{
    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid RunId { get; private set; }
    public Guid AttemptId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public DateTimeOffset RecordedAtUtc { get; private set; }
    public string Payload { get; private set; } = "";
}
