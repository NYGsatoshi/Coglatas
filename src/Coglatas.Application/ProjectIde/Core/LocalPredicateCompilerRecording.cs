using System.Collections.Frozen;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Core;

/// <summary>Owned metadata declared before execution. Retains no request, input bodies or compilation.</summary>
public sealed class LocalPredicateCompilerRecordingManifest
{
    public const string Format = "coglatas.local-predicate-compiler-recording-manifest/1";
    internal object Owner { get; }
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

    internal LocalPredicateCompilerRecordingManifest(object owner, Guid sessionId, PredicateCompilerHostRequest request,
        Guid runId, LocalPredicateCompilerRecordingReceipt? origin)
    {
        if (runId == Guid.Empty) throw new ArgumentException("A local run identity is required.");
        if (origin is not null && (!ReferenceEquals(origin.Manifest.Owner, owner) ||
            !origin.Manifest.BaseExecutionInputDigest.Equals(request.Core.ExecutionInputDigest)))
            throw new ArgumentException("Diagnostic origin requires the owned original base execution input.");
        _lineage = origin is null ? [] : origin.Manifest.CopyLineage().Append(origin.Manifest.RunId).ToArray();
        if (_lineage.Length > LocalPredicateCompilerRecordingSession.MaximumDiagnosticAncestors || _lineage.Contains(runId))
            throw new ArgumentException("Diagnostic run identity or lineage bound violated.");
        var input = request.Core.Input.Materialize();
        Owner = owner; RunId = runId; ClientGeneration = request.ClientGeneration;
        ExecutionInputDigest = request.ExecutionInputDigest; BaseExecutionInputDigest = request.Core.ExecutionInputDigest;
        MaterializationsDigest = request.Core.MaterializationDigest; PolicyDigest = request.PolicyDigest;
        Data = CompilerHostJson.Create(new { format = Format, sessionId = sessionId.ToString(), runId = runId.ToString(),
            executionInputDigest = ExecutionInputDigest.Data.Value, baseExecutionInputDigest = BaseExecutionInputDigest.Data.Value,
            inputDigest = input.InputDigest.Data.Value, sourceDigest = input.Source.Digest.Data.Value, context = input.Source.Context.Data.Value,
            compiler = input.Compiler.Data.Value, materializationsDigest = MaterializationsDigest.Data.Value, clientGeneration = ClientGeneration,
            capture = new { declared = "BeforeExecution", policy = request.PolicyData.Value, policyDigest = PolicyDigest.Data.Value },
            origin = origin is null ? null : new { runId = origin.Manifest.RunId.ToString(), manifestDigest = origin.Manifest.Digest.Data.Value,
                receiptDigest = origin.Digest.Data.Value, attemptId = origin.AttemptId.ToString() },
            knownLineage = _lineage.Select(id => id.ToString()).ToArray(), identityScope = "OwnedLocalSessionOnly",
            authority = "NotEvaluated", retentionPolicy = "NotAdopted" });
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }
}

/// <summary>Actual host attempt and original recorded frames; retains no host response, request or compilation graph.</summary>
public sealed class LocalPredicateCompilerRecordingReceipt
{
    public const string Format = "coglatas.local-predicate-compiler-recording-receipt/1";
    private readonly object _responseToken;
    public LocalPredicateCompilerRecordingManifest Manifest { get; }
    public Guid AttemptId { get; }
    public CompilerHostStatus HostStatus { get; }
    public ContentDigest HostResponseDigest { get; }
    public SourceJson? PredicateMetadata { get; }
    public ContentDigest? PredicateRecordDigest { get; }
    public IReadOnlyDictionary<string, SourceJson> Bodies { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }

    internal LocalPredicateCompilerRecordingReceipt(LocalPredicateCompilerRecordingManifest manifest,
        PredicateCompilerHostRequest request, PredicateCompilerHostResponse response)
    {
        if (!response.IsFor(request) || !manifest.ExecutionInputDigest.Equals(request.ExecutionInputDigest) ||
            manifest.ClientGeneration != response.ClientGeneration)
            throw new ArgumentException("Actual predicate attempt differs from its pre-run declaration.");
        var record = response.Compilation?.PredicatePremises;
        Manifest = manifest; AttemptId = response.AttemptId; HostStatus = response.Status; HostResponseDigest = response.Digest;
        _responseToken = response.Token; PredicateMetadata = record?.Data; PredicateRecordDigest = record?.Digest;
        Bodies = record?.Bodies ?? new Dictionary<string, SourceJson>(StringComparer.Ordinal).ToFrozenDictionary(StringComparer.Ordinal);
        Data = CompilerHostJson.Create(new { format = Format, runId = manifest.RunId.ToString(), manifestDigest = manifest.Digest.Data.Value,
            executionInputDigest = manifest.ExecutionInputDigest.Data.Value, capturePolicyDigest = manifest.PolicyDigest.Data.Value,
            attemptId = AttemptId.ToString(), hostStatus = HostStatus.ToString(), reasonCode = response.ReasonCode,
            hostResponseDigest = HostResponseDigest.Data.Value, resultDigest = response.Compilation?.Result.ResultDigest.Data.Value,
            predicateRecordDigest = PredicateRecordDigest?.Data.Value,
            recordingState = record is null ? "HostOutcomeWithoutCompilerResult" : "OriginalRootPredicateRecording",
            identityScope = "OwnedLocalSessionAndHostAttempt", earlierIntermediateStates = "NotRecorded", relationshipStructure = "NotRecorded",
            authority = "NotEvaluated", producerTrust = "NotEvaluated", eligibility = "NotEvaluated", retentionPolicy = "NotAdopted" });
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }

    public bool IsFor(PredicateCompilerHostResponse response) => ReferenceEquals(_responseToken, response.Token) &&
        AttemptId == response.AttemptId && HostResponseDigest.Equals(response.Digest);
}

/// <summary>Original attempt and receipt constructed together by the actual owned session; no caller can substitute an older pair.</summary>
public sealed class PredicateCompilerRecordedInvocation
{
    public PredicateCompilerHostResponse Response { get; }
    public LocalPredicateCompilerRecordingReceipt Receipt { get; }
    internal PredicateCompilerRecordedInvocation(PredicateCompilerHostResponse response, LocalPredicateCompilerRecordingReceipt receipt)
    {
        if (!receipt.IsFor(response)) throw new ArgumentException("Foreign predicate receipt.");
        Response = response; Receipt = receipt;
    }
}

/// <summary>Bounded local invocation/run lineage. It grants no remote protocol, current access, durable storage or Pin policy.</summary>
public sealed class LocalPredicateCompilerRecordingSession : IDisposable
{
    public const int MaximumExecutions = 32;
    public const int MaximumDiagnosticAncestors = MaximumExecutions - 1;
    private readonly object _owner = new();
    private readonly object _gate = new();
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly HashSet<Guid> _ids = [];
    private readonly LocalPredicateCompilerHost _host;
    private bool _disposed;
    public int ReservedExecutions { get { lock (_gate) return _ids.Count; } }

    public LocalPredicateCompilerRecordingSession(CompilerHostProfile profile, PlanDeclarationReadCache? declarationCache = null)
    { _host = new(profile, declarationCache); }

    public LocalPredicateCompilerRecordingManifest Prepare(PredicateCompilerHostRequest request, Guid runId,
        LocalPredicateCompilerRecordingReceipt? diagnosticOrigin = null)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(request);
            return new(_owner, _sessionId, request, runId, diagnosticOrigin);
        }
    }

    public async Task<PredicateCompilerRecordedInvocation> ExecuteAsync(LocalPredicateCompilerRecordingManifest manifest,
        PredicateCompilerHostRequest request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(manifest); ArgumentNullException.ThrowIfNull(request);
            var actual = request.Core.Input.Materialize();
            if (!ReferenceEquals(manifest.Owner, _owner) || !actual.InputDigest.Equals(request.Core.Input.ExpectedInputDigest) ||
                !manifest.ExecutionInputDigest.Equals(request.ExecutionInputDigest) ||
                !manifest.BaseExecutionInputDigest.Equals(request.Core.ExecutionInputDigest) ||
                !manifest.MaterializationsDigest.Equals(request.Core.MaterializationDigest) ||
                !manifest.PolicyDigest.Equals(request.PolicyDigest) || manifest.ClientGeneration != request.ClientGeneration)
                throw new ArgumentException("Foreign declaration or changed actual predicate execution input.");
            if (_ids.Count >= MaximumExecutions || !_ids.Add(manifest.RunId))
                throw new InvalidOperationException("Local capacity or duplicate run identity.");
        }
        var response = await _host.CompileAsync(request, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (cancellationToken.IsCancellationRequested && response.Compilation is not null)
                response = new(request, response.AttemptId, CompilerHostStatus.Cancelled, "coglatas.predicate-host.cancelled-before-recording");
            return new(response, new(manifest, request, response));
        }
    }

    /// <summary>Withdraws owned session metadata. Previously returned caller-owned frames are not physically erased.</summary>
    public void Dispose() { lock (_gate) { _disposed = true; _ids.Clear(); } }
}
