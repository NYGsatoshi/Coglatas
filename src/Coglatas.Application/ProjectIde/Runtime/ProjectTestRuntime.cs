using System.Text.Json;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Runtime;

public sealed record ProjectTestJobKey(BranchRef Branch, Guid JobId);

public sealed record StartProjectTestJob(Guid JobId, SourceRevisionContext Context, ContentDigest SourceDigest,
    ContentDigest InputDigest, ContentDigest ExecutionInputDigest);

public enum ProjectTestJobState { Pending, Completed, Cancelled }

public sealed record ProjectTestResultSummary(CompilerExecutionState Execution, CompilerCoverage Coverage,
    CompilerVerdict Verdict, CompilerFreshness Freshness, CompilerEvidenceKind EvidenceKind,
    int DiscoveredTests, int ExecutedAssertions, string PlanDigest, string ResultDigest);

public sealed record ProjectTestJobView(Guid JobId, ProjectTestJobState State, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? TerminalAtUtc, ProjectTestResultSummary? Result);

public sealed record ProjectTestJobBinding(Guid RequestedByUserId, SecuritySourceIdentitySnapshot Source,
    string SourceDigest, string ExecutionInputDigest, string EvaluatorIdentityDigest);

public sealed record ProjectTestJob(ProjectTestJobKey Key, ProjectTestJobBinding Binding, ProjectTestJobState State,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? TerminalAtUtc = null, ProjectTestResultSummary? Result = null)
{
    public ProjectTestJobView ToView(bool includeResult = false) =>
        new(Key.JobId, State, CreatedAtUtc, TerminalAtUtc, includeResult ? Result : null);
}

public interface IProjectTestInputSource
{
    /// <summary>Resolves the exact fixed server Source and its currently permitted materializations.</summary>
    Task<CompilerHostRequest?> ResolveAsync(ProjectIdeViewerContext viewer, ProjectTestJobBinding binding,
        CancellationToken cancellationToken);
}

public interface IProjectTestJobStore
{
    Task<ProjectTestJob?> FindAsync(ProjectTestJobKey key, CancellationToken cancellationToken);

    /// <summary>Creates a pending job or returns the original job for an exact authorized retry.</summary>
    Task<ProjectTestJob?> CreateAsync(ProjectTestJob job, Func<CancellationToken, Task<bool>> reauthorize,
        CancellationToken cancellationToken);

    /// <summary>Serializes pending-to-terminal changes and checks authority inside the transaction.</summary>
    Task<ProjectTestJob?> TransitionAsync(ProjectTestJob pending, ProjectTestJob terminal,
        Func<CancellationToken, Task<bool>> reauthorize, CancellationToken cancellationToken);
}

/// <summary>Development runtime over the shared typed Project Test Core.</summary>
public sealed class ProjectTestRuntime(ProjectIdeOperationAccess access, IProjectTestInputSource inputs,
    IProjectTestJobStore jobs, IClock clock, CompilerArtifactIdentity evaluator)
{
    public static readonly SecurityOperationRef StartOperation = new("coglatas.project-test.start");
    public static readonly SecurityOperationRef ReadOperation = new("coglatas.project-test.read");
    public static readonly SecurityOperationRef CancelOperation = new("coglatas.project-test.cancel");
    public static readonly SecurityOperationRef ExportOperation = new("coglatas.project-test.export");

    private readonly string evaluatorIdentityDigest =
        ContentDigest.Compute("coglatas.project-test-evaluator-identity/1", evaluator.Data.ToCanonicalBytes()).Value;

    public static async Task<CompilerArtifactIdentity> ReadLoadedEvaluatorAsync(CancellationToken cancellationToken = default)
    {
        var assembly = typeof(ProjectTestPlan).Assembly;
        var bytes = await File.ReadAllBytesAsync(assembly.Location, cancellationToken);
        return new(CompilerHostJson.Create(new
        {
            artifactId = "coglatas.local-project-test-evaluator",
            version = assembly.GetName().Version!.ToString(),
            digest = ContentDigest.Compute("coglatas.project-test-evaluator-binary/1", bytes).Data.Value
        }));
    }

    public async Task<ProjectTestJobView?> StartAsync(StartProjectTestJob request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.JobId == Guid.Empty || access.CaptureViewer() is not { } viewer)
            return null;

        var job = new ProjectTestJob(new(request.Context.Branch, request.JobId),
            new(viewer.UserId, SecuritySourceIdentitySnapshot.Capture(request.Context, request.InputDigest),
                request.SourceDigest.Value, request.ExecutionInputDigest.Value, evaluatorIdentityDigest),
            ProjectTestJobState.Pending, clock.UtcNow.ToUniversalTime());
        Task<bool> Allowed(CancellationToken token) => IsAllowedAsync(viewer, StartOperation, job, token);
        if (!await Allowed(cancellationToken))
            return null;

        var existing = await jobs.FindAsync(job.Key, cancellationToken);
        if (existing is not null)
            return existing.Binding == job.Binding && await Allowed(cancellationToken) ? existing.ToView() : null;

        if (await ResolvePlanAsync(viewer, job, cancellationToken) is null || !await Allowed(cancellationToken))
            return null;

        var created = await jobs.CreateAsync(job, Allowed, cancellationToken);
        return created is not null && await Allowed(cancellationToken) ? created.ToView() : null;
    }

    public async Task<ProjectTestJobView?> ExecuteAsync(ProjectTestJobKey key, CancellationToken cancellationToken = default)
    {
        if (await FindAuthorizedAsync(key, StartOperation, cancellationToken) is not { } authorized)
            return null;

        var (viewer, job) = authorized;
        if (job.Binding.RequestedByUserId != viewer.UserId || job.Binding.EvaluatorIdentityDigest != evaluatorIdentityDigest)
            return null;
        if (job.State != ProjectTestJobState.Pending)
            return job.ToView();

        Task<bool> Allowed(CancellationToken token) => IsAllowedAsync(viewer, StartOperation, job, token);
        var plan = await ResolvePlanAsync(viewer, job, cancellationToken);
        if (plan is null || !await Allowed(cancellationToken))
            return null;

        var run = plan.Execute(plan.Input.InputDigest, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var summary = new ProjectTestResultSummary(run.Execution, run.Coverage, run.Verdict, run.Freshness,
            run.EvidenceKind, run.DiscoveredTests, run.ExecutedAssertions, plan.Digest.Value, run.Digest.Value);
        var completed = job with { State = ProjectTestJobState.Completed, TerminalAtUtc = clock.UtcNow.ToUniversalTime(), Result = summary };
        var written = await jobs.TransitionAsync(job, completed, Allowed, cancellationToken);
        return written is not null && await Allowed(cancellationToken) ? written.ToView() : null;
    }

    public async Task<ProjectTestJobView?> FindAsync(ProjectTestJobKey key, CancellationToken cancellationToken = default) =>
        await FindAuthorizedAsync(key, ReadOperation, cancellationToken) is { } authorized ? authorized.Job.ToView(includeResult: true) : null;

    public async Task<ProjectTestJobView?> CancelAsync(ProjectTestJobKey key, CancellationToken cancellationToken = default)
    {
        if (await FindAuthorizedAsync(key, CancelOperation, cancellationToken) is not { } authorized)
            return null;

        var (viewer, job) = authorized;
        if (job.State != ProjectTestJobState.Pending)
            return job.ToView();

        Task<bool> Allowed(CancellationToken token) => IsAllowedAsync(viewer, CancelOperation, job, token);
        var cancelled = job with { State = ProjectTestJobState.Cancelled, TerminalAtUtc = clock.UtcNow.ToUniversalTime() };
        var written = await jobs.TransitionAsync(job, cancelled, Allowed, cancellationToken);
        return written is not null && await Allowed(cancellationToken) ? written.ToView() : null;
    }

    public async Task<SourceJson?> ExportAsync(ProjectTestJobKey key, CancellationToken cancellationToken = default)
    {
        if (await FindAuthorizedAsync(key, ExportOperation, cancellationToken) is not { } authorized ||
            authorized.Job.Result is not { } result)
            return null;

        var job = authorized.Job;
        return SourceJson.Parse(JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = "coglatas.project-test-runtime-summary/1",
            jobId = job.Key.JobId,
            inputDigest = job.Binding.Source.InputDigest,
            sourceDigest = job.Binding.SourceDigest,
            executionInputDigest = job.Binding.ExecutionInputDigest,
            evaluatorIdentityDigest = job.Binding.EvaluatorIdentityDigest,
            planDigest = result.PlanDigest,
            resultDigest = result.ResultDigest,
            execution = result.Execution.ToString(),
            coverage = result.Coverage.ToString(),
            verdict = result.Verdict.ToString(),
            inputFreshness = result.Freshness.ToString(),
            evidenceKind = result.EvidenceKind.ToString()
        }));
    }

    private async Task<(ProjectIdeViewerContext Viewer, ProjectTestJob Job)?> FindAuthorizedAsync(
        ProjectTestJobKey key, SecurityOperationRef operation, CancellationToken cancellationToken)
    {
        if (key.JobId == Guid.Empty || access.CaptureViewer() is not { } viewer || viewer.TenantId != key.Branch.TenantId.Value)
            return null;

        var job = await jobs.FindAsync(key, cancellationToken);
        return job is not null && await IsAllowedAsync(viewer, operation, job, cancellationToken) ? (viewer, job) : null;
    }

    private Task<bool> IsAllowedAsync(ProjectIdeViewerContext viewer, SecurityOperationRef operation,
        ProjectTestJob job, CancellationToken cancellationToken) =>
        access.IsAllowedAsync(viewer, operation, job.Binding.Source, job.Binding.SourceDigest, cancellationToken);

    private async Task<ProjectTestPlan?> ResolvePlanAsync(ProjectIdeViewerContext viewer, ProjectTestJob job,
        CancellationToken cancellationToken)
    {
        var request = await inputs.ResolveAsync(viewer, job.Binding, cancellationToken);
        if (request is null || request.ExecutionInputDigest.Value != job.Binding.ExecutionInputDigest)
            return null;

        try
        {
            var input = request.Input.Materialize();
            if (input.Source.Digest.Value != job.Binding.SourceDigest ||
                SecuritySourceIdentitySnapshot.Capture(input.Source.Context, input.InputDigest) != job.Binding.Source ||
                !SourceJson.Parse(input.Settings.Value.GetProperty("evaluator").GetRawText()).Equals(evaluator.Data))
                return null;

            var model = ProjectSemanticBinder.Bind(input, request.Snapshots, cancellationToken);
            return ProjectTestPlan.Compile(model, evaluator, cancellationToken: cancellationToken);
        }
        catch (Exception error) when (error is ArgumentException or FormatException or UnsupportedSourceException or JsonException)
        {
            return null;
        }
    }
}
