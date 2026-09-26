using Coglatas.Application.Projects;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.Common.Interfaces;

/// <summary>
/// Persistence boundary for Task execution policy and immutable run metadata.
/// Source-policy documents contain identities and states only; source content,
/// credentials, storage keys, and provider secrets never cross this boundary.
/// </summary>
public interface ITaskExecutionScopeRepository
{
    Task<ProjectExecutionScope?> GetProjectScopeAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<ProjectExecutionScope?> GetProjectScopeForUpdateAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<TaskExecutionScopeOverride?> GetTaskOverrideAsync(Guid taskItemId, CancellationToken cancellationToken = default);
    Task<TaskExecutionScopeOverride?> GetTaskOverrideForUpdateAsync(Guid taskItemId, CancellationToken cancellationToken = default);
    Task<TaskExecutionRun?> GetLatestRunAsync(Guid taskItemId, CancellationToken cancellationToken = default);
    Task<TaskExecutionRun?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default);
    Task AddProjectScopeAsync(ProjectExecutionScope scope, CancellationToken cancellationToken = default);
    Task AddTaskOverrideAsync(TaskExecutionScopeOverride scope, CancellationToken cancellationToken = default);
    Task AddRunAsync(TaskExecutionRun run, CancellationToken cancellationToken = default);
    void RemoveTaskOverride(TaskExecutionScopeOverride scope);

    Task<TaskExecutionSourcePolicyDocument?> GetSourcePolicyDocumentAsync(
        TaskExecutionSourcePolicyOwnerType ownerType,
        Guid ownerId,
        CancellationToken cancellationToken = default);

    void StageSourcePolicyDocument(TaskExecutionSourcePolicyDocument document);
    void StageSourcePolicyDocumentDelete(TaskExecutionSourcePolicyOwnerType ownerType, Guid ownerId);
    bool HasPendingSourcePolicyDocuments { get; }
    Task FlushPendingSourcePolicyDocumentsAsync(CancellationToken cancellationToken = default);
    void ClearPendingSourcePolicyDocuments();

    Task<IReadOnlyList<Attachment>> ListProjectSourceAttachmentsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Attachment>> ListTaskSourceAttachmentsAsync(
        Guid taskItemId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IntegrationAccount>> ListActiveIntegrationAccountsAsync(
        CancellationToken cancellationToken = default);
}
