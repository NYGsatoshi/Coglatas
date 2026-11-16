using System.Text.Json;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Core;

/// <summary>Separate local pre-run capture input. The existing host protocol does not request or accept these records.</summary>
public sealed class PredicateCompilerHostRequest
{
    public const string Format = "coglatas.local-predicate-compiler-host-input/1";
    internal object Token { get; } = new();
    public CompilerHostRequest Core { get; }
    public CompilerPredicateCapturePolicy CapturePolicy { get; }
    public SourceJson PolicyData { get; }
    public ContentDigest PolicyDigest { get; }
    public SourceJson Data { get; }
    public ContentDigest ExecutionInputDigest { get; }
    public long ClientGeneration => Core.ClientGeneration;

    public PredicateCompilerHostRequest(CompilerHostRequest core, CompilerPredicateCapturePolicy capturePolicy)
    {
        ArgumentNullException.ThrowIfNull(core); ArgumentNullException.ThrowIfNull(capturePolicy);
        Core = core; CapturePolicy = capturePolicy;
        PolicyData = CompilerHostJson.Create(new { format = "coglatas.local-predicate-capture-policy/1",
            mode = capturePolicy.Mode.ToString(), maximumBodyBytes = capturePolicy.MaximumBodyBytes,
            maximumDistinctBodies = capturePolicy.MaximumDistinctBodies, scope = "RootInitialRuleEvaluation" });
        PolicyDigest = ContentDigest.Compute("coglatas.local-predicate-capture-policy/1", PolicyData.ToCanonicalBytes());
        Data = CompilerHostJson.Create(new { format = Format, baseExecutionInputDigest = core.ExecutionInputDigest.Data.Value,
            declaredInputDigest = core.Input.ExpectedInputDigest.Data.Value, materializationsDigest = core.MaterializationDigest.Data.Value,
            capturePolicy = PolicyData.Value, capturePolicyDigest = PolicyDigest.Data.Value });
        ExecutionInputDigest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }
}

/// <summary>Actual owned local invocation; attempts and recording metadata never change deterministic CompilerResult meaning.</summary>
public sealed class PredicateCompilerHostResponse
{
    public const string Format = "coglatas.local-predicate-compiler-host-response/1";
    internal object Token { get; } = new();
    private readonly object _requestToken;
    public CompilerHostStatus Status { get; }
    public string ReasonCode { get; }
    public long ClientGeneration { get; }
    public Guid AttemptId { get; }
    public ContentDigest RequestedExecutionInputDigest { get; }
    public CoreCompilation? Compilation { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }

    internal PredicateCompilerHostResponse(PredicateCompilerHostRequest request, Guid attemptId,
        CompilerHostStatus status, string reasonCode, CoreCompilation? compilation = null)
    {
        if (attemptId.Version != 4 || !Enum.IsDefined(status)) throw new ArgumentException("Invalid local predicate invocation identity.");
        if (compilation is not null && (status != CompilerHostStatus.Completed ||
            !compilation.Input.InputDigest.Equals(request.Core.Input.ExpectedInputDigest) ||
            compilation.PredicatePremises is not { } record || !record.IsOwnedBy(compilation) ||
            !ReferenceEquals(record.Policy, request.CapturePolicy)))
            throw new ArgumentException("Predicate result differs from its actual declared invocation.");
        if (status == CompilerHostStatus.Completed && compilation is null) throw new ArgumentException("Completed invocation requires its actual compilation.");
        _requestToken = request.Token; Status = status; ReasonCode = reasonCode; AttemptId = attemptId;
        ClientGeneration = request.ClientGeneration; RequestedExecutionInputDigest = request.ExecutionInputDigest; Compilation = compilation;
        Data = CompilerHostJson.Create(new { format = Format, executionInputDigest = RequestedExecutionInputDigest.Data.Value,
            baseExecutionInputDigest = request.Core.ExecutionInputDigest.Data.Value, materializationsDigest = request.Core.MaterializationDigest.Data.Value,
            capturePolicyDigest = request.PolicyDigest.Data.Value, clientGeneration = ClientGeneration, attemptId = attemptId.ToString(),
            hostStatus = status.ToString(), reasonCode, resultDigest = compilation?.Result.ResultDigest.Data.Value,
            predicateRecordDigest = compilation?.PredicatePremises?.Digest.Data.Value, identityScope = "OwnedLocalInvocationOnly",
            authority = "NotEvaluated", producerTrust = "NotEvaluated", eligibility = "NotEvaluated", retentionPolicy = "NotAdopted" });
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }

    public bool IsFor(PredicateCompilerHostRequest request) => ReferenceEquals(_requestToken, request.Token) &&
        ClientGeneration == request.ClientGeneration && RequestedExecutionInputDigest.Equals(request.ExecutionInputDigest);
}

/// <summary>Headless local capture only; no existing ICoreCompilerHost, remote transport or runtime authorization is extended.</summary>
public sealed class LocalPredicateCompilerHost(CompilerHostProfile profile, PlanDeclarationReadCache? declarationCache = null)
{
    private readonly CompilerHostProfile _profile = profile ?? throw new ArgumentNullException(nameof(profile));
    public Task<PredicateCompilerHostResponse> CompileAsync(PredicateCompilerHostRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var attempt = Guid.NewGuid();
        return Task.Run(() => Execute(request, attempt, cancellationToken), CancellationToken.None);
    }

    private PredicateCompilerHostResponse Execute(PredicateCompilerHostRequest request, Guid attempt, CancellationToken cancellationToken)
    {
        PredicateCompilerHostResponse Failure(CompilerHostStatus status, string code) => new(request, attempt, status, code);
        if (cancellationToken.IsCancellationRequested) return Failure(CompilerHostStatus.Cancelled, "coglatas.predicate-host.cancelled");
        try
        {
            var input = request.Core.Input.Materialize();
            if (!_profile.Supports(input)) return Failure(CompilerHostStatus.Unsupported, "coglatas.predicate-host.compatibility-unavailable");
            var compilation = PureCoreCompiler.CompileWithPredicateCapture(input, _profile.Compiler, request.CapturePolicy,
                request.Core.Snapshots, request.Core.Units, request.Core.Calendars, cancellationToken, declarationCache);
            if (cancellationToken.IsCancellationRequested || compilation.Result.Execution == CompilerExecutionState.Cancelled)
                return Failure(CompilerHostStatus.Cancelled, "coglatas.predicate-host.cancelled");
            var response = new PredicateCompilerHostResponse(request, attempt, CompilerHostStatus.Completed,
                "coglatas.predicate-host.result-produced", compilation);
            return cancellationToken.IsCancellationRequested ? Failure(CompilerHostStatus.Cancelled, "coglatas.predicate-host.cancelled") : response;
        }
        catch (UnsupportedSourceException) { return Failure(CompilerHostStatus.Unsupported, "coglatas.predicate-host.unsupported-input"); }
        catch (Exception error) when (error is FormatException or ArgumentException or JsonException)
        { return Failure(CompilerHostStatus.InvalidInput, "coglatas.predicate-host.invalid-input"); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { return Failure(CompilerHostStatus.Cancelled, "coglatas.predicate-host.cancelled"); }
        catch (Exception) { return Failure(CompilerHostStatus.InternalError, "coglatas.predicate-host.internal-error"); }
    }
}
