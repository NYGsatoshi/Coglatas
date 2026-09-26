using System.Data;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Files;
using Coglatas.Application.Projects;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;
using Coglatas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Coglatas.Infrastructure.TaskExecution;

/// <summary>
/// Post-commit, server-owned FirstPartyProjectFilesRuntimeV1 worker. Source
/// Policy V2 is enforced before materialization: Exclude never reaches storage
/// and Prioritize participates in the bounded candidate ordering.
/// </summary>
public sealed class FirstPartyProjectFilesTaskExecutionRuntime(
    AppDbContext dbContext,
    ICurrentTenant currentTenant,
    IProjectAuthorizationService projectAuthorization,
    IFileAuthorizationService fileAuthorization,
    IFileStorageService storage,
    IClock clock,
    IAuditLogger audit,
    ITaskExecutionScopeRepository? executionScopes = null) : ITaskExecutionRuntime
{
    private const string GenericFailureCode = "TASK_EXECUTION_MATERIALIZATION_FAILED";
    private const string MissingSourceFailureCode = "TASK_EXECUTION_NO_AUTHORIZED_TEXT_SOURCES";
    private const string IntegrityFailureCode = "TASK_EXECUTION_SOURCE_INTEGRITY_FAILED";
    private const string IncompleteFailureCode = "TASK_EXECUTION_MATERIALIZATION_INCOMPLETE";
    private const string PolicySnapshotFailureCode = "TASK_EXECUTION_POLICY_SNAPSHOT_INVALID";
    private const string UnsupportedSourceFailureCode = "TASK_EXECUTION_SOURCE_KIND_UNSUPPORTED";

    public async Task ExecuteAsync(TaskExecutionRuntimeHandle handle, CancellationToken cancellationToken = default)
    {
        if (!IsCurrentTenant(handle) || handle.RunId == Guid.Empty) return;

        try
        {
            await ExecuteCoreAsync(handle, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await FailAfterUnexpectedErrorAsync(handle, CancellationToken.None);
        }
    }

    private async Task ExecuteCoreAsync(TaskExecutionRuntimeHandle handle, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var run = await LockRunAsync(handle.RunId, cancellationToken);
        if (!MatchesHandle(run, handle))
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        if (TaskExecutionRunLifecycle.IsTerminal(run!.Status))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var hasMaterialization = await HasMaterializationAsync(run.Id, cancellationToken);
        if (run.Status == TaskExecutionRunStatus.Running)
        {
            if (!hasMaterialization) await FailRunAsync(run, IncompleteFailureCode, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        if (run.Status == TaskExecutionRunStatus.Accepted)
        {
            run.Status = TaskExecutionRunStatus.Queued;
            run.QueuedAtUtc = clock.UtcNow;
            run.VersionNo++;
            await AuditLifecycleAsync(run, "TaskExecutionRunQueued", cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (run.Status != TaskExecutionRunStatus.Queued)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        run.Status = TaskExecutionRunStatus.Running;
        run.StartedAtUtc = clock.UtcNow;
        run.VersionNo++;
        await AuditLifecycleAsync(run, "TaskExecutionRunStarted", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var policy = await ResolveRunPolicyAsync(run, cancellationToken);
        if (policy is null)
        {
            await FailRunAsync(run, PolicySnapshotFailureCode, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        if (policy.HasUnsupportedExecutableSources)
        {
            await FailRunAsync(run, UnsupportedSourceFailureCode, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var eligibility = FirstPartyProjectFilesRuntimeV1.EvaluateScope(
            policy.WebEnabled,
            policy.ProjectFilesEnabled);
        if (!eligibility.IsEligible)
        {
            await FailRunAsync(run, eligibility.FailureCode ?? GenericFailureCode, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        if (!await IsCurrentRunScopeAuthorizedAsync(run, cancellationToken))
        {
            await FailRunAsync(run, GenericFailureCode, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var outcome = await MaterializeAsync(run, policy, cancellationToken);
        if (outcome.FailureCode is not null)
        {
            await FailRunAsync(run, outcome.FailureCode, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        if (outcome.Batch is null || outcome.Batch.Sources.Count == 0)
        {
            await FailRunAsync(run, MissingSourceFailureCode, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        ValidateRuntimeBatch(outcome.Batch);
        await PersistProvenanceAsync(outcome.Provenance, cancellationToken);
        await audit.LogAsync(new AuditLogEntry(
            run.RequestedByUserId,
            "TaskExecutionSourcesMaterialized",
            "TaskExecutionRun",
            run.Id,
            WorkspaceId: run.WorkspaceId,
            ProjectId: run.ProjectId,
            TenantId: run.TenantId,
            Metadata: new Dictionary<string, object?>
            {
                ["materializationSchemaVersion"] = FirstPartyProjectFilesMaterializationV1.SchemaVersion,
                ["sourcePolicySchemaVersion"] = policy.SchemaVersion,
                ["runtimeProvider"] = run.RuntimeProvider.ToString(),
                ["runtimeContractVersion"] = run.RuntimeContractVersion
            }), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<TaskExecutionSourcePolicyV2?> ResolveRunPolicyAsync(
        TaskExecutionRun run,
        CancellationToken cancellationToken)
    {
        if (run.SnapshotSchemaVersion < TaskExecutionRun.SnapshotSchemaVersion3)
        {
            return TaskExecutionSourcePolicyV2.FromLegacy(run.SnapshotWebEnabled, run.SnapshotProjectFilesEnabled);
        }

        if (executionScopes is null)
        {
            return null;
        }

        var document = await executionScopes.GetSourcePolicyDocumentAsync(
            TaskExecutionSourcePolicyOwnerType.Run,
            run.Id,
            cancellationToken);
        if (document is null ||
            document.OwnerType != TaskExecutionSourcePolicyOwnerType.Run ||
            document.TenantId != run.TenantId ||
            document.WorkspaceId != run.WorkspaceId ||
            document.ProjectId != run.ProjectId ||
            document.TaskItemId != run.TaskItemId ||
            document.ProjectScopeVersion != run.SnapshotProjectScopeVersion ||
            document.TaskOverrideVersion != run.SnapshotTaskOverrideVersion)
        {
            return null;
        }

        return document.Policy;
    }

    private async Task<MaterializationOutcome> MaterializeAsync(
        TaskExecutionRun run,
        TaskExecutionSourcePolicyV2 policy,
        CancellationToken cancellationToken)
    {
        var projectFileRules = policy.Items
            .Where(rule => rule.Kind == TaskExecutionSourceKind.ProjectFile)
            .ToList();
        var excludedIds = projectFileRules
            .Where(rule => rule.State == TaskExecutionSourceState.Exclude)
            .Select(rule => ParseProjectFileId(rule.SourceId))
            .Where(id => id != Guid.Empty)
            .ToArray();
        var explicitlyAllowedIds = projectFileRules
            .Where(rule => rule.State != TaskExecutionSourceState.Exclude)
            .Select(rule => ParseProjectFileId(rule.SourceId))
            .Where(id => id != Guid.Empty)
            .ToArray();
        var prioritizedIds = projectFileRules
            .Where(rule => rule.State == TaskExecutionSourceState.Prioritize)
            .Select(rule => ParseProjectFileId(rule.SourceId))
            .Where(id => id != Guid.Empty)
            .ToArray();
        var explicitAllowIds = projectFileRules
            .Where(rule => rule.State == TaskExecutionSourceState.Allow)
            .Select(rule => ParseProjectFileId(rule.SourceId))
            .Where(id => id != Guid.Empty)
            .ToArray();

        var candidatesQuery = dbContext.Set<Attachment>()
            .AsNoTracking()
            .Include(attachment => attachment.FileObject)
            .Where(attachment =>
                attachment.TenantId == run.TenantId &&
                attachment.WorkspaceId == run.WorkspaceId &&
                attachment.OwnerType == AttachmentOwnerType.TaskItem &&
                attachment.OwnerId == run.TaskItemId &&
                !attachment.DeletedAt.HasValue &&
                attachment.ScanStatus == FileScanStatus.Clean &&
                attachment.FileObject != null &&
                attachment.FileObject.TenantId == run.TenantId &&
                attachment.FileObject.WorkspaceId == run.WorkspaceId &&
                attachment.FileObject.ProjectId == run.ProjectId &&
                !attachment.FileObject.DeletedAt.HasValue &&
                attachment.FileObject.Status == FileObjectStatus.Active);

        // Item rules override the ProjectFile kind default. Filtering happens
        // before Take and, critically, before any storage.OpenReadAsync call.
        if (policy.ProjectFile == TaskExecutionSourceState.Exclude)
        {
            candidatesQuery = candidatesQuery.Where(attachment => explicitlyAllowedIds.Contains(attachment.FileObjectId));
        }
        else if (excludedIds.Length > 0)
        {
            candidatesQuery = candidatesQuery.Where(attachment => !excludedIds.Contains(attachment.FileObjectId));
        }

        var defaultPrioritized = policy.ProjectFile == TaskExecutionSourceState.Prioritize;
        var candidates = await candidatesQuery
            .OrderBy(attachment => prioritizedIds.Contains(attachment.FileObjectId)
                ? 0
                : explicitAllowIds.Contains(attachment.FileObjectId)
                    ? 1
                    : defaultPrioritized ? 0 : 1)
            .ThenBy(attachment => attachment.CreatedAt)
            .ThenBy(attachment => attachment.Id)
            .Take(FirstPartyProjectFilesMaterializationV1.MaxSourceCount)
            .ToListAsync(cancellationToken);

        var provenance = new List<TaskExecutionMaterializedSource>(candidates.Count);
        var content = new List<TaskExecutionMaterializedSourceContent>(candidates.Count);
        var remainingBytes = FirstPartyProjectFilesMaterializationV1.MaxTotalBytes;

        foreach (var candidate in candidates)
        {
            if (remainingBytes <= 0 || candidate.FileObject is not { } fileObject) break;

            var mediaType = FirstPartyProjectFilesMaterializationV1.NormalizeSupportedMediaType(fileObject.ContentType);
            var maximumForSource = Math.Min(FirstPartyProjectFilesMaterializationV1.MaxSourceBytes, remainingBytes);
            if (mediaType is null || fileObject.SizeBytes < 0 || fileObject.SizeBytes > maximumForSource) continue;

            if (!await fileAuthorization.CanViewAttachment(run.RequestedByUserId, candidate, cancellationToken)) continue;

            TaskExecutionMaterializedText? materialized;
            try
            {
                await using var stream = await storage.OpenReadAsync(fileObject.StorageKey, cancellationToken);
                materialized = await FirstPartyProjectFilesMaterializationV1.ReadUtf8Async(
                    stream,
                    mediaType,
                    maximumForSource,
                    cancellationToken);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (NotSupportedException)
            {
                continue;
            }

            if (materialized is null) continue;

            if (!string.IsNullOrWhiteSpace(fileObject.HashSha256) &&
                !string.Equals(fileObject.HashSha256.Trim(), materialized.ContentSha256, StringComparison.OrdinalIgnoreCase))
            {
                return MaterializationOutcome.Failed(IntegrityFailureCode);
            }

            var current = await CurrentCandidateAsync(candidate.Id, run, cancellationToken);
            if (current?.FileObject is not { } currentFile ||
                !await fileAuthorization.CanViewAttachment(run.RequestedByUserId, current, cancellationToken) ||
                current.FileObjectId != fileObject.Id ||
                !string.Equals(currentFile.StorageKey, fileObject.StorageKey, StringComparison.Ordinal) ||
                currentFile.SizeBytes != fileObject.SizeBytes ||
                !string.Equals(currentFile.ContentType, fileObject.ContentType, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(currentFile.HashSha256, fileObject.HashSha256, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var materializedAt = clock.UtcNow;
            var provenanceSource = new TaskExecutionMaterializedSource
            {
                TenantId = run.TenantId,
                WorkspaceId = run.WorkspaceId,
                ProjectId = run.ProjectId,
                TaskItemId = run.TaskItemId,
                TaskExecutionRunId = run.Id,
                FileObjectId = fileObject.Id,
                AttachmentId = candidate.Id,
                SchemaVersion = TaskExecutionMaterializedSource.SchemaVersion1,
                ContentSha256 = materialized.ContentSha256,
                MediaType = materialized.MediaType,
                MaterializedByteCount = materialized.ByteCount,
                MaterializedAtUtc = materializedAt
            };
            provenance.Add(provenanceSource);
            content.Add(new TaskExecutionMaterializedSourceContent(
                provenanceSource.Id,
                fileObject.Id,
                candidate.Id,
                materialized.MediaType,
                materialized.ContentSha256,
                materialized.ByteCount,
                materialized.Text));
            remainingBytes -= checked((int)materialized.ByteCount);
        }

        return MaterializationOutcome.Succeeded(new TaskExecutionMaterializationBatch(run.Id, run.TenantId, content), provenance);
    }

    private static Guid ParseProjectFileId(string sourceId) =>
        TaskExecutionSourcePolicyV2.TryParseProjectFileSourceId(sourceId, out var id) ? id : Guid.Empty;

    private Task<Attachment?> CurrentCandidateAsync(Guid attachmentId, TaskExecutionRun run, CancellationToken cancellationToken) =>
        dbContext.Set<Attachment>()
            .AsNoTracking()
            .Include(attachment => attachment.FileObject)
            .SingleOrDefaultAsync(attachment =>
                attachment.Id == attachmentId &&
                attachment.TenantId == run.TenantId &&
                attachment.WorkspaceId == run.WorkspaceId &&
                attachment.OwnerType == AttachmentOwnerType.TaskItem &&
                attachment.OwnerId == run.TaskItemId &&
                !attachment.DeletedAt.HasValue &&
                attachment.ScanStatus == FileScanStatus.Clean &&
                attachment.FileObject != null &&
                attachment.FileObject.TenantId == run.TenantId &&
                attachment.FileObject.WorkspaceId == run.WorkspaceId &&
                attachment.FileObject.ProjectId == run.ProjectId &&
                !attachment.FileObject.DeletedAt.HasValue &&
                attachment.FileObject.Status == FileObjectStatus.Active,
                cancellationToken);

    private async Task<bool> IsCurrentRunScopeAuthorizedAsync(TaskExecutionRun run, CancellationToken cancellationToken)
    {
        var projectExists = await dbContext.Set<Project>()
            .AsNoTracking()
            .AnyAsync(project =>
                project.Id == run.ProjectId &&
                project.TenantId == run.TenantId &&
                project.WorkspaceId == run.WorkspaceId &&
                !project.DeletedAt.HasValue,
                cancellationToken);
        if (!projectExists) return false;

        var taskExists = await dbContext.Set<TaskItem>()
            .AsNoTracking()
            .AnyAsync(task =>
                task.Id == run.TaskItemId &&
                task.TenantId == run.TenantId &&
                task.WorkspaceId == run.WorkspaceId &&
                task.ProjectId == run.ProjectId &&
                !task.DeletedAt.HasValue,
                cancellationToken);

        return taskExists && await projectAuthorization.CanViewProject(run.RequestedByUserId, run.ProjectId, cancellationToken);
    }

    private async Task<TaskExecutionRun?> LockRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        if (dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT 1 FROM task_execution_runs WHERE "Id" = {runId} AND "TenantId" = {currentTenant.TenantId} FOR UPDATE""",
                cancellationToken);
        }

        return await dbContext.Set<TaskExecutionRun>().SingleOrDefaultAsync(run => run.Id == runId, cancellationToken);
    }

    private async Task<bool> HasMaterializationAsync(Guid runId, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM task_execution_materialized_sources WHERE "TaskExecutionRunId" = @runId
            );
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "runId";
        parameter.DbType = DbType.Guid;
        parameter.Value = runId;
        command.Parameters.Add(parameter);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private async Task PersistProvenanceAsync(
        IReadOnlyList<TaskExecutionMaterializedSource> provenance,
        CancellationToken cancellationToken)
    {
        foreach (var source in provenance)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO task_execution_materialized_sources (
                    "Id", "TenantId", "WorkspaceId", "ProjectId", "TaskItemId", "TaskExecutionRunId",
                    "FileObjectId", "AttachmentId", "SchemaVersion", "ContentSha256", "MediaType",
                    "MaterializedByteCount", "MaterializedAtUtc")
                VALUES (
                    {source.Id}, {source.TenantId}, {source.WorkspaceId}, {source.ProjectId}, {source.TaskItemId},
                    {source.TaskExecutionRunId}, {source.FileObjectId}, {source.AttachmentId}, {source.SchemaVersion},
                    {source.ContentSha256}, {source.MediaType}, {source.MaterializedByteCount}, {source.MaterializedAtUtc});
                """, cancellationToken);
        }
    }

    private async Task FailAfterUnexpectedErrorAsync(TaskExecutionRuntimeHandle handle, CancellationToken cancellationToken)
    {
        if (!IsCurrentTenant(handle)) return;

        try
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var run = await LockRunAsync(handle.RunId, cancellationToken);
            if (!MatchesHandle(run, handle) || TaskExecutionRunLifecycle.IsTerminal(run!.Status))
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            if (run.Status == TaskExecutionRunStatus.Accepted)
            {
                run.Status = TaskExecutionRunStatus.Queued;
                run.QueuedAtUtc = clock.UtcNow;
                run.VersionNo++;
                await AuditLifecycleAsync(run, "TaskExecutionRunQueued", cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            if (run.Status == TaskExecutionRunStatus.Queued)
            {
                run.Status = TaskExecutionRunStatus.Running;
                run.StartedAtUtc = clock.UtcNow;
                run.VersionNo++;
                await AuditLifecycleAsync(run, "TaskExecutionRunStarted", cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            if (run.Status == TaskExecutionRunStatus.Running)
                await FailRunAsync(run, GenericFailureCode, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // Accepted state remains durable; provider/source diagnostics are never surfaced through HTTP.
        }
    }

    private async Task FailRunAsync(TaskExecutionRun run, string failureCode, CancellationToken cancellationToken)
    {
        if (run.Status != TaskExecutionRunStatus.Running) return;

        run.Status = TaskExecutionRunStatus.Failed;
        run.FailureCode = failureCode.Length <= 100 ? failureCode : GenericFailureCode;
        run.FinishedAtUtc = clock.UtcNow;
        run.VersionNo++;
        await AuditLifecycleAsync(run, "TaskExecutionRunFailed", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task AuditLifecycleAsync(TaskExecutionRun run, string action, CancellationToken cancellationToken) =>
        audit.LogAsync(new AuditLogEntry(
            run.RequestedByUserId,
            action,
            "TaskExecutionRun",
            run.Id,
            WorkspaceId: run.WorkspaceId,
            ProjectId: run.ProjectId,
            TenantId: run.TenantId,
            Metadata: new Dictionary<string, object?>
            {
                ["status"] = run.Status.ToString(),
                ["runtimeProvider"] = run.RuntimeProvider.ToString(),
                ["runtimeContractVersion"] = run.RuntimeContractVersion
            }), cancellationToken);

    private bool IsCurrentTenant(TaskExecutionRuntimeHandle handle) =>
        currentTenant.IsAvailable && !currentTenant.IsPlatformScope && currentTenant.TenantId != Guid.Empty &&
        currentTenant.TenantId == handle.TenantId;

    private static bool MatchesHandle(TaskExecutionRun? run, TaskExecutionRuntimeHandle handle) =>
        run is not null &&
        run.Id == handle.RunId &&
        run.TenantId == handle.TenantId &&
        run.RuntimeProvider == FirstPartyProjectFilesRuntimeV1.Provider &&
        run.RuntimeContractVersion == FirstPartyProjectFilesRuntimeV1.ContractVersion &&
        handle.RuntimeContractVersion == FirstPartyProjectFilesRuntimeV1.ContractVersion;

    private static void ValidateRuntimeBatch(TaskExecutionMaterializationBatch batch)
    {
        if (batch.Sources.Count is <= 0 or > FirstPartyProjectFilesMaterializationV1.MaxSourceCount ||
            batch.TotalByteCount > FirstPartyProjectFilesMaterializationV1.MaxTotalBytes ||
            batch.Sources.Any(source =>
                source.ByteCount < 0 ||
                source.ByteCount > FirstPartyProjectFilesMaterializationV1.MaxSourceBytes ||
                source.Text is null ||
                FirstPartyProjectFilesMaterializationV1.NormalizeSupportedMediaType(source.MediaType) is null))
        {
            throw new InvalidOperationException("The server materialization did not satisfy the V1 runtime contract.");
        }
    }

    private sealed record MaterializationOutcome(
        TaskExecutionMaterializationBatch? Batch,
        IReadOnlyList<TaskExecutionMaterializedSource> Provenance,
        string? FailureCode)
    {
        public static MaterializationOutcome Succeeded(
            TaskExecutionMaterializationBatch batch,
            IReadOnlyList<TaskExecutionMaterializedSource> provenance) => new(batch, provenance, null);

        public static MaterializationOutcome Failed(string failureCode) =>
            new(null, Array.Empty<TaskExecutionMaterializedSource>(), failureCode);
    }
}
