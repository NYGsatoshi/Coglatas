using System.Text.Json;
using System.Text.Json.Serialization;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.ProjectIde.Runtime;
using Coglatas.Domain.ProjectIde;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>Persists only redacted output availability and result axes for explicitly composed development hosts.</summary>
public sealed class CompilerOutputCaptureStore(AppDbContext context, IClock clock) : ICompilerOutputCaptureStore
{
    private static readonly JsonSerializerOptions ProjectionOptions = new()
    {
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<CompilerOutputCapture?> FindAsync(CompilerOutputCaptureKey key, CancellationToken cancellationToken)
    {
        if (!MatchesScope(key))
            return null;

        var reference = key.Recording;
        var binding = BindingDigest(reference);
        var row = await context.Set<CompilerOutputCaptureRow>().AsNoTracking().SingleOrDefaultAsync(row =>
            row.TenantId == reference.Source.TenantId && row.ProjectId == reference.Source.ProjectId && row.CaptureId == key.CaptureId &&
            row.RunId == reference.RecordingId && row.BindingDigest == binding && row.DeletedAtUtc == null, cancellationToken);
        if (row is not { Projection: { } projection, MaximumBodyBytes: { } maximumBodyBytes } || !MatchesScope(key))
            return null;

        try
        {
            var view = JsonSerializer.Deserialize<CompilerOutputCaptureView>(projection, ProjectionOptions);
            return view is not null ? new(key, maximumBodyBytes, view) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<CompilerOutputCapture?> RetainAsync(CompilerOutputCapture capture, Func<CancellationToken, Task<bool>> reauthorize,
        CancellationToken cancellationToken)
    {
        if (!MatchesScope(capture.Key))
            return null;

        var reference = capture.Key.Recording;
        var binding = BindingDigest(reference);
        var projection = JsonSerializer.Serialize(capture.View, ProjectionOptions);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var written = await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO projectide_compiler_output_captures
                ("TenantId", "ProjectId", "CaptureId", "RunId", "BindingDigest", "CapturedAtUtc", "MaximumBodyBytes", "Projection")
            SELECT "TenantId", "ProjectId", {capture.Key.CaptureId}, "RunId", {binding}, {clock.UtcNow},
                {capture.MaximumBodyBytes}, CAST({projection} AS jsonb)
            FROM projectide_compiler_recording_evidence
            WHERE "TenantId" = {reference.Source.TenantId} AND "ProjectId" = {reference.Source.ProjectId} AND "RunId" = {reference.RecordingId}
                AND "Payload" ->> 'SourceDigest' = {reference.SourceDigest.Value}
                AND "Payload" ->> 'ReceiptDigest' = {reference.RecordingDigest.Value}
                AND "Payload" -> 'Source' ->> 'ContextDigest' = {reference.Source.ContextDigest}
                AND "Payload" -> 'Source' ->> 'InputDigest' = {reference.Source.InputDigest}
            ON CONFLICT DO NOTHING
            """, cancellationToken);
        if (written == 0 && await FindAsync(capture.Key, cancellationToken) != capture)
            return null;
        if (!await reauthorize(cancellationToken) || !MatchesScope(capture.Key))
            return null;

        await transaction.CommitAsync(cancellationToken);
        return capture;
    }

    public async Task<bool> DeleteAsync(CompilerOutputCapture capture, Func<CancellationToken, Task<bool>> reauthorize,
        CancellationToken cancellationToken)
    {
        if (!MatchesScope(capture.Key))
            return false;

        var reference = capture.Key.Recording;
        var binding = BindingDigest(reference);
        var projection = JsonSerializer.Serialize(capture.View, ProjectionOptions);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var deleted = await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE projectide_compiler_output_captures
            SET "DeletedAtUtc" = {clock.UtcNow}, "RunId" = NULL, "BindingDigest" = NULL, "MaximumBodyBytes" = NULL, "Projection" = NULL
            WHERE "TenantId" = {reference.Source.TenantId} AND "ProjectId" = {reference.Source.ProjectId} AND "CaptureId" = {capture.Key.CaptureId}
                AND "RunId" = {reference.RecordingId} AND "BindingDigest" = {binding} AND "DeletedAtUtc" IS NULL
                AND "MaximumBodyBytes" = {capture.MaximumBodyBytes} AND "Projection" = CAST({projection} AS jsonb)
            """, cancellationToken);
        if (deleted != 1 || !await reauthorize(cancellationToken) || !MatchesScope(capture.Key))
            return false;

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private bool MatchesScope(CompilerOutputCaptureKey key) =>
        context.Database.IsNpgsql() && context.ActiveTenantId == key.Recording.Source.TenantId && key.CaptureId != Guid.Empty;

    private static string BindingDigest(ProjectIdeInspectionReference reference) =>
        ContentDigest.Compute("coglatas.compiler-output-capture-binding/1", SourceJson.Parse(JsonSerializer.SerializeToUtf8Bytes(new
        {
            scope = reference.Source,
            source = reference.SourceDigest.Value,
            run = reference.RecordingId,
            receipt = reference.RecordingDigest.Value
        })).ToCanonicalBytes()).Value;
}

internal sealed class CompilerOutputCaptureRow
{
    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid CaptureId { get; private set; }
    public Guid? RunId { get; private set; }
    public string? BindingDigest { get; private set; }
    public DateTimeOffset CapturedAtUtc { get; private set; }
    public DateTimeOffset? DeletedAtUtc { get; private set; }
    public int? MaximumBodyBytes { get; private set; }
    public string? Projection { get; private set; }
}
