using System.Text.Json;
using System.Text.Json.Serialization;
using Coglatas.Application.ProjectIde.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Coglatas.Infrastructure.Persistence;

/// <summary>PostgreSQL persistence for fixed Project Test jobs used by explicitly composed development hosts.</summary>
public sealed class ProjectTestJobStore(AppDbContext db) : IProjectTestJobStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public Task<ProjectTestJob?> FindAsync(ProjectTestJobKey key, CancellationToken cancellationToken) =>
        ReadAsync(key, forUpdate: false, cancellationToken);

    public async Task<ProjectTestJob?> CreateAsync(ProjectTestJob job, Func<CancellationToken, Task<bool>> reauthorize,
        CancellationToken cancellationToken)
    {
        if (!CanAccess(job.Key) || job.State != ProjectTestJobState.Pending || job.Result is not null ||
            job.TerminalAtUtc is not null || job.RetainedResultState != ProjectTestRetainedResultState.Unavailable ||
            !BindingMatchesKey(job.Key, job.Binding))
            return null;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var key = job.Key;
        var binding = JsonSerializer.Serialize(job.Binding, JsonOptions);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO projectide_development.project_ide_test_jobs (tenant_id, project_id, branch_id, job_id, binding, state, created_at)
            VALUES ({key.Branch.TenantId.Value}, {key.Branch.ProjectId.Value}, {key.Branch.BranchId.Value},
                    {key.JobId}, CAST({binding} AS jsonb), 0, {job.CreatedAtUtc})
            ON CONFLICT (tenant_id, job_id) DO NOTHING
            """, cancellationToken);

        var saved = await ReadAsync(key, forUpdate: true, cancellationToken);
        if (saved is null || saved.RetainedResultState == ProjectTestRetainedResultState.Mismatch ||
            saved.Binding != job.Binding || !await reauthorize(cancellationToken) || !CanAccess(key))
            return null;

        await transaction.CommitAsync(cancellationToken);
        return saved;
    }

    public async Task<ProjectTestJob?> TransitionAsync(ProjectTestJob pending, ProjectTestJob terminal,
        Func<CancellationToken, Task<bool>> reauthorize, CancellationToken cancellationToken)
    {
        var key = pending.Key;
        if (!CanAccess(key) || pending.State != ProjectTestJobState.Pending || terminal.Key != key ||
            terminal.Binding != pending.Binding || terminal.CreatedAtUtc != pending.CreatedAtUtc ||
            terminal.TerminalAtUtc is not { } terminalAt || terminalAt < pending.CreatedAtUtc ||
            terminal.State is not (ProjectTestJobState.Completed or ProjectTestJobState.Cancelled) ||
            (terminal.State == ProjectTestJobState.Completed) != (terminal.Result is not null) ||
            (terminal.State == ProjectTestJobState.Completed
                ? terminal.RetainedResult is null || !terminal.RetainedResult.Matches(terminal.Binding, terminal.Result!)
                : terminal.RetainedResultState != ProjectTestRetainedResultState.Unavailable))
            return null;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var saved = await ReadAsync(key, forUpdate: true, cancellationToken);
        if (saved is null || saved.RetainedResultState == ProjectTestRetainedResultState.Mismatch ||
            saved.Binding != pending.Binding || saved.CreatedAtUtc != pending.CreatedAtUtc ||
            !await reauthorize(cancellationToken) || !CanAccess(key))
            return null;

        if (saved.State != ProjectTestJobState.Pending)
            return saved;

        var result = terminal.Result is null ? null : JsonSerializer.Serialize(terminal.Result, JsonOptions);
        var resultBody = terminal.RetainedResult?.RecoverCanonicalBytes();
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE projectide_development.project_ide_test_jobs
            SET state = {(int)terminal.State}, terminal_at = {terminalAt}, result = CAST({result} AS jsonb), result_body = {resultBody}
            WHERE tenant_id = {key.Branch.TenantId.Value} AND project_id = {key.Branch.ProjectId.Value}
                AND branch_id = {key.Branch.BranchId.Value} AND job_id = {key.JobId} AND state = 0
            """, cancellationToken);
        if (changed != 1)
            return null;

        var persisted = await ReadAsync(key, forUpdate: false, cancellationToken);
        if (persisted is null || persisted.RetainedResultState == ProjectTestRetainedResultState.Mismatch ||
            !await reauthorize(cancellationToken) || !CanAccess(key))
            return null;

        await transaction.CommitAsync(cancellationToken);
        return persisted;
    }

    private async Task<ProjectTestJob?> ReadAsync(ProjectTestJobKey key, bool forUpdate, CancellationToken cancellationToken)
    {
        if (!CanAccess(key))
            return null;

        FormattableString command = $"""
            SELECT binding::text AS "Binding", state AS "State", created_at AS "CreatedAtUtc",
                terminal_at AS "TerminalAtUtc", result::text AS "Result", result_body AS "ResultBody"
            FROM projectide_development.project_ide_test_jobs
            WHERE tenant_id = {key.Branch.TenantId.Value} AND project_id = {key.Branch.ProjectId.Value}
                AND branch_id = {key.Branch.BranchId.Value} AND job_id = {key.JobId}
            """;
        if (forUpdate)
            command = System.Runtime.CompilerServices.FormattableStringFactory.Create(
                command.Format + " FOR UPDATE", command.GetArguments());

        var row = await db.Database.SqlQuery<ProjectTestJobRow>(command).SingleOrDefaultAsync(cancellationToken);
        if (row is null || !CanAccess(key))
            return null;

        try
        {
            var binding = JsonSerializer.Deserialize<ProjectTestJobBinding>(row.Binding, JsonOptions);
            if (binding is null || !BindingMatchesKey(key, binding))
                return null;

            var state = (ProjectTestJobState)row.State;
            var result = row.Result is null ? null : JsonSerializer.Deserialize<ProjectTestResultSummary>(row.Result, JsonOptions);
            if ((state == ProjectTestJobState.Completed) != (result is not null))
                return null;

            var retained = ProjectTestRetainedResult.ReadStored(row.ResultBody, binding, result);
            return new(key, binding, state, row.CreatedAtUtc, row.TerminalAtUtc, result, retained);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private bool CanAccess(ProjectTestJobKey key) =>
        db.Database.IsNpgsql() && db.ActiveTenantId == key.Branch.TenantId.Value && key.JobId != Guid.Empty;

    private static bool BindingMatchesKey(ProjectTestJobKey key, ProjectTestJobBinding binding) =>
        binding.RequestedByUserId != Guid.Empty && binding.Source is { } source &&
        source.TenantId == key.Branch.TenantId.Value && source.ProjectId == key.Branch.ProjectId.Value &&
        source.BranchId == key.Branch.BranchId.Value;

    private sealed record ProjectTestJobRow(string Binding, int State, DateTimeOffset CreatedAtUtc,
        DateTimeOffset? TerminalAtUtc, string? Result, byte[]? ResultBody);
}
