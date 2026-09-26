using System.Data;
using System.Data.Common;
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
/// Canonical contest runtime for #462 + #463. Lifecycle transitions are kept in
/// short transactions so a concurrent #370 Stop/Redirect command can win while
/// source materialization is in progress. Materialized bytes remain process-local
/// until a final locked transaction confirms that the Run is still Running.
/// </summary>
public sealed partial class DurableTaskExecutionResultRuntime : ITaskExecutionRuntime
{
    private readonly AppDbContext dbContext;
    private readonly ICurrentTenant currentTenant;
    private readonly IProjectAuthorizationService projectAuthorization;
    private readonly IFileAuthorizationService fileAuthorization;
    private readonly IFileStorageService storage;
    private readonly IClock clock;
    private readonly IAuditLogger audit;

    public DurableTaskExecutionResultRuntime(
        AppDbContext dbContext,
        ICurrentTenant currentTenant,
        IProjectAuthorizationService projectAuthorization,
        IFileAuthorizationService fileAuthorization,
        IFileStorageService storage,
        IClock clock,
        IAuditLogger audit)
    {
        this.dbContext = dbContext;
        this.currentTenant = currentTenant;
        this.projectAuthorization = projectAuthorization;
        this.fileAuthorization = fileAuthorization;
        this.storage = storage;
        this.clock = clock;
        this.audit = audit;
    }

    private const string GenericFailureCode = "TASK_EXECUTION_RESULT_PERSISTENCE_FAILED";
    private const string MissingSourceFailureCode = "TASK_EXECUTION_NO_AUTHORIZED_TEXT_SOURCES";
    private const string IntegrityFailureCode = "TASK_EXECUTION_SOURCE_INTEGRITY_FAILED";
    private const string IncompleteFailureCode = "TASK_EXECUTION_MATERIALIZATION_INCOMPLETE";

    public async Task ExecuteAsync(
        TaskExecutionRuntimeHandle handle,
        CancellationToken cancellationToken = default)
    {
        if (!IsCurrentTenant(handle) || handle.RunId == Guid.Empty)
        {
            return;
        }

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

    private async Task ExecuteCoreAsync(
        TaskExecutionRuntimeHandle handle,
        CancellationToken cancellationToken)
    {
        var run = await PrepareRunningSnapshotAsync(handle, cancellationToken);
        if (run is null)
        {
            return;
        }

        var eligibility = FirstPartyProjectFilesRuntimeV1.EvaluateScope(
            run.SnapshotWebEnabled,
            run.SnapshotProjectFilesEnabled);
        if (!eligibility.IsEligible)
        {
            await FinalizeFailureAsync(handle, eligibility.FailureCode ?? GenericFailureCode, cancellationToken);
            return;
        }

        if (!await IsCurrentRunScopeAuthorizedAsync(run, cancellationToken))
        {
            await FinalizeFailureAsync(handle, GenericFailureCode, cancellationToken);
            return;
        }

        Guid? existingResultId;
        IReadOnlyList<RuntimeSource> existingProvenance;
        await using (var readTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            existingResultId = await GetExistingResultIdAsync(run.Id, cancellationToken);
            existingProvenance = existingResultId.HasValue
                ? []
                : await LoadExistingProvenanceAsync(run, cancellationToken);
            await readTransaction.CommitAsync(cancellationToken);
        }

        if (existingResultId.HasValue)
        {
            await FinalizeExistingResultAsync(handle, existingResultId.Value, cancellationToken);
            return;
        }

        if (existingProvenance.Count > 0)
        {
            await FinalizeExistingProvenanceAsync(handle, existingProvenance, cancellationToken);
            return;
        }

        var outcome = await MaterializeAsync(run, cancellationToken);
        if (outcome.FailureCode is not null)
        {
            await FinalizeFailureAsync(handle, outcome.FailureCode, cancellationToken);
            return;
        }

        if (outcome.Sources.Count == 0)
        {
            await FinalizeFailureAsync(handle, MissingSourceFailureCode, cancellationToken);
            return;
        }

        var completionTime = clock.UtcNow;
        var document = FirstPartyProjectFilesReportV1.Build(
            outcome.Sources.Select(item => item.ReportSource).ToArray(),
            completionTime);
        await FinalizeMaterializedAsync(
            handle,
            outcome.Sources,
            document,
            completionTime,
            cancellationToken);
    }

    private async Task<TaskExecutionRun?> PrepareRunningSnapshotAsync(
        TaskExecutionRuntimeHandle handle,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var run = await LockRunAsync(handle.RunId, cancellationToken);
        if (!MatchesHandle(run, handle))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (TaskExecutionRunLifecycle.IsTerminal(run!.Status))
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
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

        if (run.Status != TaskExecutionRunStatus.Running)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await transaction.CommitAsync(cancellationToken);
        dbContext.Entry(run).State = EntityState.Detached;
        return run;
    }

    private async Task FinalizeFailureAsync(
        TaskExecutionRuntimeHandle handle,
        string failureCode,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var run = await LockRunAsync(handle.RunId, cancellationToken);
        if (!MatchesHandle(run, handle) || run!.Status != TaskExecutionRunStatus.Running)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        await FailRunAsync(run, failureCode, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task FinalizeExistingResultAsync(
        TaskExecutionRuntimeHandle handle,
        Guid resultId,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var run = await LockRunAsync(handle.RunId, cancellationToken);
        if (!MatchesHandle(run, handle) || run!.Status != TaskExecutionRunStatus.Running)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        if (await CountResultSourcesAsync(resultId, cancellationToken) <= 0)
        {
            await FailRunAsync(run, IncompleteFailureCode, cancellationToken);
        }
        else
        {
            await SucceedRunAsync(run, resultId, null, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task FinalizeExistingProvenanceAsync(
        TaskExecutionRuntimeHandle handle,
        IReadOnlyList<RuntimeSource> sources,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var run = await LockRunAsync(handle.RunId, cancellationToken);
        if (!MatchesHandle(run, handle) || run!.Status != TaskExecutionRunStatus.Running)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        if (!await ReauthorizeExistingProvenanceAsync(run, sources, cancellationToken))
        {
            await FailRunAsync(run, GenericFailureCode, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var completedAt = clock.UtcNow;
        var report = FirstPartyProjectFilesReportV1.Build(
            sources.Select(item => item.ReportSource).ToArray(),
            completedAt);
        var resultId = await InsertResultAsync(run, report, completedAt, cancellationToken);
        await InsertResultLinksAsync(run, resultId, sources, cancellationToken);
        await SucceedRunAsync(run, resultId, report, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task FinalizeMaterializedAsync(
        TaskExecutionRuntimeHandle handle,
        IReadOnlyList<RuntimeSource> sources,
        TaskExecutionReportDocument document,
        DateTimeOffset completionTime,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var run = await LockRunAsync(handle.RunId, cancellationToken);
        if (!MatchesHandle(run, handle) || run!.Status != TaskExecutionRunStatus.Running)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        if (!await ReauthorizeExistingProvenanceAsync(run, sources, cancellationToken))
        {
            await FailRunAsync(run, GenericFailureCode, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        await InsertProvenanceAsync(run, sources, cancellationToken);
        var resultId = await InsertResultAsync(run, document, completionTime, cancellationToken);
        await InsertResultLinksAsync(run, resultId, sources, cancellationToken);
        await SucceedRunAsync(run, resultId, document, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<MaterializationOutcome> MaterializeAsync(
        TaskExecutionRun run,
        CancellationToken cancellationToken)
    {
        var candidates = await dbContext.Set<Attachment>()
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
                attachment.FileObject.Status == FileObjectStatus.Active)
            .OrderBy(attachment => attachment.CreatedAt)
            .ThenBy(attachment => attachment.Id)
            .Take(FirstPartyProjectFilesMaterializationV1.MaxSourceCount)
            .ToListAsync(cancellationToken);

        var sources = new List<RuntimeSource>(candidates.Count);
        var remainingBytes = FirstPartyProjectFilesMaterializationV1.MaxTotalBytes;

        foreach (var candidate in candidates)
        {
            if (!await IsStillRunningAsync(run.Id, cancellationToken))
            {
                break;
            }

            if (remainingBytes <= 0 || candidate.FileObject is not { } fileObject)
            {
                break;
            }

            var mediaType = FirstPartyProjectFilesMaterializationV1
                .NormalizeSupportedMediaType(fileObject.ContentType);
            var maximumForSource = Math.Min(
                FirstPartyProjectFilesMaterializationV1.MaxSourceBytes,
                remainingBytes);
            if (mediaType is null || fileObject.SizeBytes < 0 || fileObject.SizeBytes > maximumForSource)
            {
                continue;
            }

            if (!await fileAuthorization.CanViewAttachment(
                    run.RequestedByUserId,
                    candidate,
                    cancellationToken))
            {
                continue;
            }

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

            if (materialized is null)
            {
                continue;
            }

            if (materialized.ByteCount != fileObject.SizeBytes)
            {
                return MaterializationOutcome.Failed(IntegrityFailureCode);
            }

            if (!string.IsNullOrWhiteSpace(fileObject.HashSha256) &&
                !string.Equals(
                    fileObject.HashSha256.Trim(),
                    materialized.ContentSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return MaterializationOutcome.Failed(IntegrityFailureCode);
            }

            var current = await CurrentCandidateAsync(candidate.Id, run, cancellationToken);
            if (current?.FileObject is not { } currentFile ||
                !await fileAuthorization.CanViewAttachment(
                    run.RequestedByUserId,
                    current,
                    cancellationToken) ||
                current.FileObjectId != fileObject.Id ||
                !string.Equals(currentFile.StorageKey, fileObject.StorageKey, StringComparison.Ordinal) ||
                currentFile.SizeBytes != fileObject.SizeBytes ||
                !string.Equals(currentFile.ContentType, fileObject.ContentType, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(currentFile.HashSha256, fileObject.HashSha256, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var provenanceId = Guid.NewGuid();
            var materializedAt = clock.UtcNow;
            sources.Add(new RuntimeSource(
                provenanceId,
                fileObject.Id,
                candidate.Id,
                new TaskExecutionReportSourceInput(
                    provenanceId,
                    materialized.MediaType,
                    materialized.ContentSha256,
                    materialized.ByteCount,
                    materializedAt,
                    materialized.Text)));
            remainingBytes -= checked((int)materialized.ByteCount);
        }

        return MaterializationOutcome.Succeeded(sources);
    }

    private Task<bool> IsStillRunningAsync(Guid runId, CancellationToken cancellationToken) =>
        dbContext.Set<TaskExecutionRun>()
            .AsNoTracking()
            .Where(run => run.Id == runId)
            .Select(run => run.Status == TaskExecutionRunStatus.Running)
            .SingleOrDefaultAsync(cancellationToken);

    private Task<Attachment?> CurrentCandidateAsync(
        Guid attachmentId,
        TaskExecutionRun run,
        CancellationToken cancellationToken) =>
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

    private async Task<bool> IsCurrentRunScopeAuthorizedAsync(
        TaskExecutionRun run,
        CancellationToken cancellationToken)
    {
        var projectExists = await dbContext.Set<Project>()
            .AsNoTracking()
            .AnyAsync(project =>
                project.Id == run.ProjectId &&
                project.TenantId == run.TenantId &&
                project.WorkspaceId == run.WorkspaceId &&
                !project.DeletedAt.HasValue,
                cancellationToken);
        if (!projectExists)
        {
            return false;
        }

        var taskExists = await dbContext.Set<TaskItem>()
            .AsNoTracking()
            .AnyAsync(task =>
                task.Id == run.TaskItemId &&
                task.TenantId == run.TenantId &&
                task.WorkspaceId == run.WorkspaceId &&
                task.ProjectId == run.ProjectId &&
                !task.DeletedAt.HasValue,
                cancellationToken);

        return taskExists && await projectAuthorization.CanViewProject(
            run.RequestedByUserId,
            run.ProjectId,
            cancellationToken);
    }

    private async Task<bool> ReauthorizeExistingProvenanceAsync(
        TaskExecutionRun run,
        IReadOnlyList<RuntimeSource> sources,
        CancellationToken cancellationToken)
    {
        foreach (var source in sources)
        {
            var attachment = await CurrentCandidateAsync(source.AttachmentId, run, cancellationToken);
            if (attachment?.FileObject is not { } fileObject ||
                attachment.FileObjectId != source.FileObjectId ||
                (!string.IsNullOrWhiteSpace(fileObject.HashSha256) &&
                 !string.Equals(fileObject.HashSha256.Trim(), source.ReportSource.ContentSha256, StringComparison.OrdinalIgnoreCase)) ||
                !await fileAuthorization.CanViewAttachment(
                    run.RequestedByUserId,
                    attachment,
                    cancellationToken))
            {
                return false;
            }
        }

        return true;
    }
}
