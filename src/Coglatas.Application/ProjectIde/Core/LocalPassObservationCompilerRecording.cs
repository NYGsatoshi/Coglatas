using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Core;

/// <summary>Owned declaration before execution, retaining no request, Source bodies or compilation graph.</summary>
public sealed class LocalPassObservationCompilerRecordingManifest
{
    public const string Format = "coglatas.local-pass-observation-compiler-recording-manifest/1";
    internal object Token { get; } = new();
    internal object Owner { get; }
    private readonly object _requestToken;
    private readonly Guid[] _lineage;
    internal Guid[] CopyLineage() => _lineage.ToArray();
    public Guid RunId { get; }
    public long ClientGeneration { get; }
    public ContentDigest ExecutionInputDigest { get; }
    public ContentDigest BaseExecutionInputDigest { get; }
    public ContentDigest MaterializationsDigest { get; }
    public ContentDigest PolicyDigest { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }

    internal LocalPassObservationCompilerRecordingManifest(object owner, Guid sessionId,
        PassObservationCompilerHostRequest request, Guid runId, LocalPassObservationCompilerRecordingReceipt? origin)
    {
        if (runId.Version != 4) throw new ArgumentException("A local run UUID is required.");
        if (origin is not null && (!ReferenceEquals(origin.Manifest.Owner, owner) ||
            !origin.Manifest.BaseExecutionInputDigest.Equals(request.Core.ExecutionInputDigest)))
            throw new ArgumentException("Diagnostic origin requires the owned original base execution input.");
        _lineage = origin is null ? [] : origin.Manifest.CopyLineage().Append(origin.Manifest.RunId).ToArray();
        if (_lineage.Length > LocalPassObservationCompilerRecordingSession.MaximumDiagnosticAncestors || _lineage.Contains(runId))
            throw new ArgumentException("Diagnostic run identity or lineage bound violated.");
        var input = request.Core.Input.Materialize();
        Owner = owner; _requestToken = request.Token; RunId = runId; ClientGeneration = request.ClientGeneration;
        ExecutionInputDigest = request.ExecutionInputDigest; BaseExecutionInputDigest = request.Core.ExecutionInputDigest;
        MaterializationsDigest = request.Core.MaterializationDigest; PolicyDigest = request.PolicyDigest;
        Data = CompilerHostJson.Create(new { format = Format, sessionId = sessionId.ToString(), runId = runId.ToString(),
            executionInputDigest = ExecutionInputDigest.Data.Value, baseExecutionInputDigest = BaseExecutionInputDigest.Data.Value,
            inputDigest = input.InputDigest.Data.Value, sourceDigest = input.Source.Digest.Data.Value,
            context = input.Source.Context.Data.Value, compiler = input.Compiler.Data.Value,
            materializationsDigest = MaterializationsDigest.Data.Value, clientGeneration = ClientGeneration,
            capture = new { declared = "BeforeExecution", policy = request.PolicyData.Value, policyDigest = PolicyDigest.Data.Value },
            origin = origin is null ? null : new { runId = origin.Manifest.RunId.ToString(), manifestDigest = origin.Manifest.Digest.Data.Value,
                receiptDigest = origin.Digest.Data.Value, attemptId = origin.AttemptId.ToString() },
            knownLineage = _lineage.Select(id => id.ToString()).ToArray(), identityScope = "OwnedLocalSessionOnly",
            authority = "NotEvaluated", retentionPolicy = "NotAdopted" });
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }

    public bool IsFor(PassObservationCompilerHostRequest request) => ReferenceEquals(_requestToken, request.Token) &&
        ClientGeneration == request.ClientGeneration && ExecutionInputDigest.Equals(request.ExecutionInputDigest) &&
        BaseExecutionInputDigest.Equals(request.Core.ExecutionInputDigest) && MaterializationsDigest.Equals(request.Core.MaterializationDigest) &&
        PolicyDigest.Equals(request.PolicyDigest);
}

/// <summary>Actual attempt and original observation metadata. Retains no request, response or compilation graph.</summary>
public sealed class LocalPassObservationCompilerRecordingReceipt
{
    public const string Format = "coglatas.local-pass-observation-compiler-recording-receipt/1";
    private readonly object _responseToken;
    public LocalPassObservationCompilerRecordingManifest Manifest { get; }
    public Guid AttemptId { get; }
    public CompilerHostStatus HostStatus { get; }
    public ContentDigest HostResponseDigest { get; }
    public SourceJson? ObservationMetadata { get; }
    public ContentDigest? ObservationRecordDigest { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }

    internal LocalPassObservationCompilerRecordingReceipt(LocalPassObservationCompilerRecordingManifest manifest,
        PassObservationCompilerHostRequest request, PassObservationCompilerHostResponse response)
    {
        if (!manifest.IsFor(request) || !response.IsFor(request) || !response.IsFor(manifest))
            throw new ArgumentException("Actual observation attempt differs from its pre-run declaration.");
        Manifest = manifest; AttemptId = response.AttemptId; HostStatus = response.Status; HostResponseDigest = response.Digest;
        _responseToken = response.Token; ObservationMetadata = response.Execution?.Observation.Data;
        ObservationRecordDigest = response.Execution?.Observation.Digest;
        Data = CompilerHostJson.Create(new { format = Format, runId = manifest.RunId.ToString(), manifestDigest = manifest.Digest.Data.Value,
            executionInputDigest = manifest.ExecutionInputDigest.Data.Value, observationPolicyDigest = manifest.PolicyDigest.Data.Value,
            attemptId = AttemptId.ToString(), hostStatus = HostStatus.ToString(), reasonCode = response.ReasonCode,
            hostResponseDigest = HostResponseDigest.Data.Value, resultDigest = response.Execution?.Compilation.Result.ResultDigest.Data.Value,
            observationRecordDigest = ObservationRecordDigest?.Data.Value,
            recordingState = response.Execution is null ? "HostOutcomeWithoutCompilerResult" : "OriginalInitialRootObservation",
            captureState = response.Execution?.Observation.State.ToString(),
            identityScope = "OwnedLocalSessionAndHostAttempt", earlierIntermediateStates = "NotRecorded",
            regionalEvaluation = "NotRecorded", authority = "NotEvaluated", producerTrust = "NotEvaluated",
            eligibility = "NotEvaluated", retentionPolicy = "NotAdopted" });
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }

    public bool IsFor(PassObservationCompilerHostResponse response) => ReferenceEquals(_responseToken, response.Token) &&
        response.IsFor(Manifest) && AttemptId == response.AttemptId && HostResponseDigest.Equals(response.Digest);
}

/// <summary>Actual response and receipt constructed together. Ownership does not grant current access or session liveness.</summary>
public sealed class PassObservationCompilerRecordedInvocation
{
    public PassObservationCompilerHostResponse Response { get; }
    public LocalPassObservationCompilerRecordingReceipt Receipt { get; }
    internal PassObservationCompilerRecordedInvocation(PassObservationCompilerHostResponse response,
        LocalPassObservationCompilerRecordingReceipt receipt)
    {
        if (!receipt.IsFor(response)) throw new ArgumentException("Foreign observation response or receipt.");
        Response = response; Receipt = receipt;
    }

    public bool IsFor(PassObservationCompilerHostRequest request, LocalPassObservationCompilerRecordingManifest intendedManifest) =>
        ReferenceEquals(Receipt.Manifest, intendedManifest) && intendedManifest.IsFor(request) &&
        Response.IsFor(request) && Response.IsFor(intendedManifest) && Receipt.IsFor(Response);
}

/// <summary>Bounded local actual runs; no older protocol, current authorization, durable storage or Pin adoption.</summary>
public sealed class LocalPassObservationCompilerRecordingSession : IDisposable
{
    public const int MaximumExecutions = 32;
    public const int MaximumDiagnosticAncestors = MaximumExecutions - 1;
    private readonly object _owner = new();
    private readonly object _gate = new();
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly HashSet<Guid> _ids = [];
    private readonly LocalPassObservationCompilerHost _host;
    private bool _disposed;
    public int ReservedExecutions { get { lock (_gate) return _ids.Count; } }

    public LocalPassObservationCompilerRecordingSession(CompilerHostProfile profile, PlanDeclarationReadCache? declarationCache = null)
    { _host = new(profile, declarationCache); }

    public LocalPassObservationCompilerRecordingManifest Prepare(PassObservationCompilerHostRequest request, Guid runId,
        LocalPassObservationCompilerRecordingReceipt? diagnosticOrigin = null)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(request);
            return new(_owner, _sessionId, request, runId, diagnosticOrigin);
        }
    }

    public async Task<PassObservationCompilerRecordedInvocation> ExecuteAsync(LocalPassObservationCompilerRecordingManifest manifest,
        PassObservationCompilerHostRequest request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(manifest); ArgumentNullException.ThrowIfNull(request);
            if (!ReferenceEquals(manifest.Owner, _owner) || !manifest.IsFor(request))
                throw new ArgumentException("Foreign declaration or changed actual observation request.");
            _ = request.Core.Input.Materialize();
            if (_ids.Count >= MaximumExecutions || !_ids.Add(manifest.RunId))
                throw new InvalidOperationException("Local capacity or duplicate run identity.");
        }
        var response = await _host.CompileAsync(request, manifest, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (cancellationToken.IsCancellationRequested && response.Execution is not null)
                response = new(request, manifest, response.AttemptId, CompilerHostStatus.Cancelled,
                    "coglatas.pass-observation-host.cancelled-before-recording");
            return new(response, new(manifest, request, response));
        }
    }

    /// <summary>Withdraws session execution metadata. Previously returned caller-owned frames are not physically erased.</summary>
    public void Dispose() { lock (_gate) { _disposed = true; _ids.Clear(); } }
}
