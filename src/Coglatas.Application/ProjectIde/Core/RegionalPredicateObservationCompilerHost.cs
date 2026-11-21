using System.Text.Json;
using System.Text.Json.Serialization;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Core;

/// <summary>Separate pre-run regional operator policy. Existing normal/output/array host protocols stay unchanged.</summary>
public sealed class RegionalPredicateObservationCompilerHostRequest
{
    public const string Format = "coglatas.local-regional-predicate-observation-compiler-host-input/1";
    internal object Token { get; } = new();
    [JsonIgnore]
    public CompilerHostRequest Core { get; }
    public CompilerRegionalPredicateObservationPolicy ObservationPolicy { get; }
    public SourceJson PolicyData { get; }
    public ContentDigest PolicyDigest { get; }
    public SourceJson Data { get; }
    public ContentDigest ExecutionInputDigest { get; }
    public long ClientGeneration => Core.ClientGeneration;

    public RegionalPredicateObservationCompilerHostRequest(CompilerHostRequest core, CompilerRegionalPredicateObservationPolicy observationPolicy)
    {
        ArgumentNullException.ThrowIfNull(core); ArgumentNullException.ThrowIfNull(observationPolicy);
        Core = core; ObservationPolicy = observationPolicy;
        PolicyData = CompilerHostJson.Create(new { format = "coglatas.local-regional-predicate-observation-capture-policy/1",
            mode = observationPolicy.Mode.ToString(), maximumEvaluations = observationPolicy.MaximumEvaluations,
            maximumNodes = observationPolicy.MaximumNodes, maximumBodyBytes = observationPolicy.MaximumBodyBytes,
            scope = "ActualRegionalEvaluationsOnly" });
        PolicyDigest = ContentDigest.Compute("coglatas.local-regional-predicate-observation-capture-policy/1", PolicyData.ToCanonicalBytes());
        Data = CompilerHostJson.Create(new { format = Format, baseExecutionInputDigest = core.ExecutionInputDigest.Data.Value,
            declaredInputDigest = core.Input.ExpectedInputDigest.Data.Value, materializationsDigest = core.MaterializationDigest.Data.Value,
            observationPolicy = PolicyData.Value, observationPolicyDigest = PolicyDigest.Data.Value });
        ExecutionInputDigest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }
}

/// <summary>One actual declared local attempt. Host outcome, Compiler execution and capture sufficiency remain independent.</summary>
public sealed class RegionalPredicateObservationCompilerHostResponse
{
    public const string Format = "coglatas.local-regional-predicate-observation-compiler-host-response/1";
    internal object Token { get; } = new();
    private readonly object _requestToken;
    private readonly object _manifestToken;
    public LocalRegionalPredicateObservationCompilerRecordingManifest Manifest { get; }
    public CompilerHostStatus Status { get; }
    public string ReasonCode { get; }
    public long ClientGeneration { get; }
    public Guid AttemptId { get; }
    public ContentDigest RequestedExecutionInputDigest { get; }
    [JsonIgnore]
    public RecordedRegionalPredicateCompilation? Execution { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }

    internal RegionalPredicateObservationCompilerHostResponse(RegionalPredicateObservationCompilerHostRequest request,
        LocalRegionalPredicateObservationCompilerRecordingManifest manifest, Guid attemptId, CompilerHostStatus status,
        string reasonCode, RecordedRegionalPredicateCompilation? execution = null)
    {
        if (attemptId.Version != 4 || !Enum.IsDefined(status) || !manifest.IsFor(request))
            throw new ArgumentException("Invalid local observation invocation identity.");
        if ((status == CompilerHostStatus.Completed) != (execution is not null))
            throw new ArgumentException("A completed host attempt requires its actual Compiler envelope.");
        if (execution is not null && (!execution.Observation.IsOwnedBy(execution) ||
            !execution.Compilation.Input.InputDigest.Equals(request.Core.Input.ExpectedInputDigest) ||
            !ReferenceEquals(execution.Observation.Policy, request.ObservationPolicy) ||
            execution.Compilation.Result.Execution == CompilerExecutionState.Cancelled))
            throw new ArgumentException("Actual observation differs from its predeclared input or policy.");
        _requestToken = request.Token; _manifestToken = manifest.Token; Manifest = manifest;
        Status = status; ReasonCode = reasonCode; ClientGeneration = request.ClientGeneration;
        AttemptId = attemptId; RequestedExecutionInputDigest = request.ExecutionInputDigest; Execution = execution;
        Data = CompilerHostJson.Create(new { format = Format, runId = manifest.RunId.ToString(), manifestDigest = manifest.Digest.Data.Value,
            executionInputDigest = RequestedExecutionInputDigest.Data.Value, baseExecutionInputDigest = request.Core.ExecutionInputDigest.Data.Value,
            materializationsDigest = request.Core.MaterializationDigest.Data.Value, observationPolicyDigest = request.PolicyDigest.Data.Value,
            clientGeneration = ClientGeneration, attemptId = attemptId.ToString(), hostStatus = status.ToString(), reasonCode,
            resultDigest = execution?.Compilation.Result.ResultDigest.Data.Value, observationDigest = execution?.Observation.Digest.Data.Value,
            compilerExecution = execution?.Compilation.Result.Execution.ToString(),
            compilerCoverage = execution?.Compilation.Result.Coverage.ToString(), compilerVerdict = execution?.Compilation.Result.Verdict.ToString(),
            evidenceComplete = execution?.Compilation.Result.EvidenceComplete, captureState = execution?.Observation.State.ToString(),
            identityScope = "OwnedLocalDeclaredInvocationOnly", captureScope = "ActualRegionalEvaluationsOnly", initialRootEvaluation = "NotRecorded", rootSummaryDiagnosticLinks = "NotRecorded", authority = "NotEvaluated",
            producerTrust = "NotEvaluated", eligibility = "NotEvaluated", retentionPolicy = "NotAdopted" });
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }

    public bool IsFor(RegionalPredicateObservationCompilerHostRequest request) => ReferenceEquals(_requestToken, request.Token) &&
        ClientGeneration == request.ClientGeneration && RequestedExecutionInputDigest.Equals(request.ExecutionInputDigest);
    public bool IsFor(LocalRegionalPredicateObservationCompilerRecordingManifest manifest) => ReferenceEquals(_manifestToken, manifest.Token) &&
        ReferenceEquals(Manifest, manifest);
}

/// <summary>Headless local actual observation; no transport, intended client selection or runtime authorization is adopted.</summary>
public sealed class LocalRegionalPredicateObservationCompilerHost(CompilerHostProfile profile, PlanDeclarationReadCache? declarationCache = null)
{
    private readonly CompilerHostProfile _profile = profile ?? throw new ArgumentNullException(nameof(profile));

    public Task<RegionalPredicateObservationCompilerHostResponse> CompileAsync(RegionalPredicateObservationCompilerHostRequest request,
        LocalRegionalPredicateObservationCompilerRecordingManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(manifest);
        if (!manifest.IsFor(request)) throw new ArgumentException("The actual request differs from its pre-run declaration.");
        var attempt = Guid.NewGuid();
        return Task.Run(() => Execute(request, manifest, attempt, cancellationToken), CancellationToken.None);
    }

    private RegionalPredicateObservationCompilerHostResponse Execute(RegionalPredicateObservationCompilerHostRequest request,
        LocalRegionalPredicateObservationCompilerRecordingManifest manifest, Guid attempt, CancellationToken token)
    {
        RegionalPredicateObservationCompilerHostResponse Failure(CompilerHostStatus status, string code) => new(request, manifest, attempt, status, code);
        if (token.IsCancellationRequested) return Failure(CompilerHostStatus.Cancelled, "coglatas.regional-predicate-observation-host.cancelled");
        try
        {
            var input = request.Core.Input.Materialize();
            if (!_profile.Supports(input)) return Failure(CompilerHostStatus.Unsupported, "coglatas.regional-predicate-observation-host.compatibility-unavailable");
            var execution = PureCoreCompiler.CompileWithRegionalPredicateObservation(input, _profile.Compiler, request.ObservationPolicy,
                request.Core.Snapshots, request.Core.Units, request.Core.Calendars, token, declarationCache);
            if (token.IsCancellationRequested || execution.Compilation.Result.Execution == CompilerExecutionState.Cancelled)
                return Failure(CompilerHostStatus.Cancelled, "coglatas.regional-predicate-observation-host.cancelled");
            var response = new RegionalPredicateObservationCompilerHostResponse(request, manifest, attempt, CompilerHostStatus.Completed,
                "coglatas.regional-predicate-observation-host.result-produced", execution);
            return token.IsCancellationRequested ? Failure(CompilerHostStatus.Cancelled, "coglatas.regional-predicate-observation-host.cancelled") : response;
        }
        catch (UnsupportedSourceException) { return Failure(CompilerHostStatus.Unsupported, "coglatas.regional-predicate-observation-host.unsupported-input"); }
        catch (Exception error) when (error is FormatException or ArgumentException or JsonException)
        { return Failure(CompilerHostStatus.InvalidInput, "coglatas.regional-predicate-observation-host.invalid-input"); }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { return Failure(CompilerHostStatus.Cancelled, "coglatas.regional-predicate-observation-host.cancelled"); }
        catch (Exception) { return Failure(CompilerHostStatus.InternalError, "coglatas.regional-predicate-observation-host.internal-error"); }
    }
}
