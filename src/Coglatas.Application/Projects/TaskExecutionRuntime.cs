using Coglatas.Domain.Enums;

namespace Coglatas.Application.Projects;

/// <summary>
/// Opaque, server-built work identity given to a runtime only after the
/// accepted run is committed. It deliberately contains no URLs, source IDs,
/// file names, bytes, storage keys, credentials, prompts, or configuration.
/// The runtime derives all authority and input from the persisted run.
/// </summary>
public sealed record TaskExecutionRuntimeHandle(
    Guid RunId,
    Guid TenantId,
    int RuntimeContractVersion);

/// <summary>
/// Canonical V1 runtime contract. The concrete executor is deliberately
/// composed with server materialization in #462; this decision type performs
/// no I/O, dispatch, provider call, or synthetic completion.
/// </summary>
public static class FirstPartyProjectFilesRuntimeV1
{
    public const int ContractVersion = 1;
    public const TaskExecutionProvider Provider = TaskExecutionProvider.FirstPartyProjectFilesRuntimeV1;

    /// <summary>
    /// Web policy is represented for compatibility with the source-scope UI,
    /// but it is not executable by this runtime. It is rejected rather than
    /// silently ignored when a committed run is materialized.
    /// </summary>
    public static TaskExecutionRuntimeEligibility EvaluateScope(
        bool webEnabled,
        bool projectFilesEnabled) =>
        webEnabled
            ? TaskExecutionRuntimeEligibility.Rejected("TASK_EXECUTION_WEB_UNSUPPORTED")
            : !projectFilesEnabled
                ? TaskExecutionRuntimeEligibility.Rejected("TASK_EXECUTION_PROJECT_FILES_REQUIRED")
                : TaskExecutionRuntimeEligibility.Allowed();
}

/// <summary>
/// Bounded provider-neutral outcome used only for server-side source-policy
/// enforcement. Its code is safe for a later public failure projection and
/// cannot contain a source identifier or provider diagnostic.
/// </summary>
public sealed record TaskExecutionRuntimeEligibility(bool IsEligible, string? FailureCode)
{
    public static TaskExecutionRuntimeEligibility Allowed() => new(true, null);
    public static TaskExecutionRuntimeEligibility Rejected(string failureCode) => new(false, failureCode);
}

/// <summary>
/// Runtime invocation belongs to a post-commit server worker. Implementations
/// must re-read the run by <see cref="TaskExecutionRuntimeHandle.RunId"/>,
/// establish the supplied Tenant scope, materialize only server-authorized
/// Project files, and write durable lifecycle/result state. This port is not
/// invoked by the acceptance transaction.
/// </summary>
public interface ITaskExecutionRuntime
{
    Task ExecuteAsync(
        TaskExecutionRuntimeHandle run,
        CancellationToken cancellationToken = default);
}
