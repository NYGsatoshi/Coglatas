using System.Text.Json;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Core;

/// <summary>Capture declared before a local host invocation. Retains identity metadata, not input bodies or a request.</summary>
public sealed class LocalCompilerRecordingManifest
{
    internal object Owner { get; }
    private readonly Guid[] _lineage;
    internal Guid[] CopyLineage() => _lineage.ToArray();
    public Guid RunId { get; }
    public ContentDigest InputDigest { get; }
    public ContentDigest ExecutionInputDigest { get; }
    public ContentDigest MaterializationsDigest { get; }
    internal SecuritySourceIdentitySnapshot SourceIdentity { get; }
    internal ContentDigest SourceDigest { get; }
    internal Guid? OriginRunId { get; }
    internal ContentDigest? OriginReceiptDigest { get; }
    public long ClientGeneration { get; }
    public CompilerOutputSnapshotMode Mode { get; }
    public int MaximumBodyBytes { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }

    internal LocalCompilerRecordingManifest(object owner, Guid sessionId, CompilerHostRequest request, Guid runId,
        CompilerOutputSnapshotMode mode, int maximumBodyBytes, LocalCompilerRecordingReceipt? origin)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (runId == Guid.Empty || !Enum.IsDefined(mode) || maximumBodyBytes is < 1 or > ProjectCompilerOutputSnapshot.MaximumBodyBudgetBytes)
            throw new ArgumentException("Invalid local capture declaration.");
        var input = request.Input.Materialize();
        if (origin is not null && (!ReferenceEquals(origin.Manifest.Owner, owner) ||
            !origin.Manifest.ExecutionInputDigest.Equals(request.ExecutionInputDigest)))
            throw new ArgumentException("Diagnostic origin requires the actual owned exact execution input.");
        _lineage = origin is null ? [] : origin.Manifest.CopyLineage().Append(origin.Manifest.RunId).ToArray();
        if (_lineage.Length > LocalCompilerRecordingSession.MaximumDiagnosticAncestors || _lineage.Contains(runId))
            throw new ArgumentException("Diagnostic identity or lineage bound violated.");
        Owner = owner; RunId = runId; InputDigest = request.Input.ExpectedInputDigest;
        ExecutionInputDigest = request.ExecutionInputDigest; MaterializationsDigest = request.MaterializationDigest;
        SourceIdentity = SecuritySourceIdentitySnapshot.Capture(input.Source.Context, InputDigest);
        SourceDigest = input.Source.Digest;
        OriginRunId = origin?.Manifest.RunId;
        OriginReceiptDigest = origin?.Digest;
        ClientGeneration = request.ClientGeneration; Mode = mode; MaximumBodyBytes = maximumBodyBytes;
        Data = LocalCompilerRecordingJson.From(new {
            format = "coglatas.local-compiler-recording-manifest/1", sessionId = sessionId.ToString(), runId = runId.ToString(),
            inputDigest = InputDigest.Data.Value, executionInputDigest = ExecutionInputDigest.Data.Value,
            materializationsDigest = MaterializationsDigest.Data.Value, clientGeneration = ClientGeneration,
            sourceDigest = input.Source.Digest.Data.Value, context = input.Source.Context.Data.Value, compiler = input.Compiler.Data.Value,
            capture = new { declared = "BeforeExecution", mode = mode.ToString(), maximumBodyBytes, earlierIntermediateStates = "NotRecorded" },
            origin = origin is null ? null : new { runId = origin.Manifest.RunId.ToString(), manifestDigest = origin.Manifest.Digest.Data.Value,
                receiptDigest = origin.Digest.Data.Value, attemptId = origin.AttemptId.ToString() },
            knownLineage = _lineage.Select(id => id.ToString()).ToArray(), identityScope = "OwnedLocalSessionOnly",
            authority = "NotEvaluated", retentionPolicy = "NotAdopted"
        });
        Digest = ContentDigest.Compute("coglatas.local-compiler-recording-manifest/1", Data.ToCanonicalBytes());
    }
}

/// <summary>Original local host outcome and optional output snapshot. Retains no host response or CoreCompilation.</summary>
public sealed class LocalCompilerRecordingReceipt
{
    public LocalCompilerRecordingManifest Manifest { get; }
    public Guid AttemptId { get; }
    public ProjectCompilerOutputSnapshot? Snapshot { get; }
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }

    internal LocalCompilerRecordingReceipt(LocalCompilerRecordingManifest manifest, CompilerHostResponse response)
    {
        if (!response.RequestedInputDigest.Equals(manifest.InputDigest) || response.ClientGeneration != manifest.ClientGeneration || response.AttemptId.Version != 4)
            throw new ArgumentException("Host invocation differs from declared input.");
        var execution = response.Execution;
        if (execution is not null && (!execution.InputBindingDigest.Equals(manifest.ExecutionInputDigest) ||
            !ContentDigest.Compute("coglatas.host-materializations/1", execution.Materializations.ToCanonicalBytes()).Equals(manifest.MaterializationsDigest)))
            throw new ArgumentException("Actual host materializations differ from declared input.");
        Manifest = manifest; AttemptId = response.AttemptId;
        Snapshot = execution is null ? null : ProjectCompilerOutputSnapshot.Read(execution.Compilation, execution.Compilation.Result.ResultDigest,
            manifest.Mode, manifest.MaximumBodyBytes, desiredInput: execution.Compilation.Input);
        Data = LocalCompilerRecordingJson.From(new {
            format = "coglatas.local-compiler-recording-receipt/1", runId = manifest.RunId.ToString(), manifestDigest = manifest.Digest.Data.Value,
            executionInputDigest = manifest.ExecutionInputDigest.Data.Value, attemptId = AttemptId.ToString(),
            hostStatus = response.Status.ToString(), reasonCode = response.ReasonCode, hostExecutionDigest = execution?.Digest.Data.Value,
            resultDigest = execution?.Compilation.Result.ResultDigest.Data.Value, captureDigest = Snapshot?.Digest.Data.Value,
            recordingState = execution is null ? "HostOutcomeWithoutCompilerResult" : "OriginalProducedOutputSnapshot",
            earlierIntermediateStates = "NotRecorded", producerTrust = "NotEvaluated", eligibility = "NotEvaluated",
            authority = "NotEvaluated", retentionPolicy = "NotAdopted"
        });
        Digest = ContentDigest.Compute("coglatas.local-compiler-recording-receipt/1", Data.ToCanonicalBytes());
    }
}

/// <summary>Bounded invocation identity for an owned local host. Grants no durable registry, access, retention or trust.</summary>
public sealed class LocalCompilerRecordingSession(CompilerHostProfile profile) : IDisposable
{
    // Initial local metadata limits only. These are not adopted retention or Pin policy.
    public const int MaximumExecutions = 32;
    public const int MaximumDiagnosticAncestors = MaximumExecutions - 1;
    private readonly object _owner = new();
    private readonly object _gate = new();
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly HashSet<Guid> _ids = [];
    private readonly LocalNetCompilerHost _host = new(profile);
    private bool _disposed;
    public int ReservedExecutions { get { lock (_gate) return _ids.Count; } }

    public LocalCompilerRecordingManifest Prepare(CompilerHostRequest request, Guid runId,
        CompilerOutputSnapshotMode mode = CompilerOutputSnapshotMode.Manifest,
        int maximumBodyBytes = ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes, LocalCompilerRecordingReceipt? diagnosticOrigin = null)
    {
        lock (_gate) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return new(_owner, _sessionId, request, runId, mode, maximumBodyBytes, diagnosticOrigin);
        }
    }

    public async Task<(CompilerHostResponse Response, LocalCompilerRecordingReceipt Receipt)> ExecuteAsync(
        LocalCompilerRecordingManifest manifest, CompilerHostRequest request, CancellationToken cancellationToken = default)
    {
        lock (_gate) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(manifest); ArgumentNullException.ThrowIfNull(request);
            var actualInput = request.Input.Materialize();
            if (!ReferenceEquals(manifest.Owner, _owner) || !actualInput.InputDigest.Equals(manifest.InputDigest) ||
                !manifest.InputDigest.Equals(request.Input.ExpectedInputDigest) || !manifest.ExecutionInputDigest.Equals(request.ExecutionInputDigest) ||
                !manifest.MaterializationsDigest.Equals(request.MaterializationDigest) || manifest.ClientGeneration != request.ClientGeneration)
                throw new ArgumentException("Foreign declaration or changed actual execution input.");
            if (_ids.Count >= MaximumExecutions || !_ids.Add(manifest.RunId))
                throw new InvalidOperationException("Local capacity or duplicate execution identity.");
        }
        var response = await _host.CompileAsync(request, cancellationToken).ConfigureAwait(false);
        lock (_gate) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return (response, new(manifest, response));
        }
    }

    /// <summary>Withdraw owned session metadata. Caller-owned returned snapshots are not physically erased.</summary>
    public void Dispose() { lock (_gate) { _disposed = true; _ids.Clear(); } }
}

internal static class LocalCompilerRecordingJson
{
    internal static SourceJson From(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
}
