using System.Collections.Frozen;
using System.Text.Json;
using Coglatas.Domain.ProjectIde;
using Coglatas.Domain.ProjectIde.VersionedArtifacts;

namespace Coglatas.Application.ProjectIde.DependencyAcquisition;

/// <summary>One experimental Unit body. Other dependency kinds remain explicitly unsupported by this adapter.</summary>
public sealed class SyntheticUnitBody
{
    public const string ArtifactType = "coglatas.prototype.unit";
    public ArtifactIdentity Identity { get; }
    public ArtifactSourcePin Source { get; }
    public Guid TargetId { get; }
    public UnitDefinition Unit { get; }
    public SourceJson Data { get; }
    public int CanonicalByteCount { get; }

    /// <summary>Validates existing Unit semantics and binds its complete immutable body to one Source and target.</summary>
    public SyntheticUnitBody(ArtifactSourcePin source, Guid targetId, SourceJson unitDefinition)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(unitDefinition);
        if (targetId == Guid.Empty) throw new ArgumentException("Unit target requires an identity.");
        Unit = new(unitDefinition, ContentDigest.Compute("coglatas.unit-definition/1", unitDefinition.ToCanonicalBytes()));
        Source = source; TargetId = targetId;
        Data = SourceJson.Parse(JsonSerializer.Serialize(new
        {
            artifactType = ArtifactType, schemaVersion = 1, canonicalEncodingVersion = 1,
            source = source.Data.Value, targetId = targetId.ToString("D"), unitDefinition = Unit.Data.Value
        }));
        Identity = new(ArtifactType, 1, 1, ContentDigest.Compute(ArtifactType + "/1/canonical/1", Data.ToCanonicalBytes()).Value);
        CanonicalByteCount = System.Text.Encoding.UTF8.GetByteCount(Data.CanonicalText);
    }
}

/// <summary>Current synthetic server-side facts. Request fields never supply these facts to the resolver.</summary>
public sealed class SyntheticAcquisitionContext
{
    public Guid ActorId { get; }
    public ArtifactSourcePin Source { get; }
    public string AuthorizationRevision { get; }
    public IReadOnlySet<string> Capabilities { get; }
    public IReadOnlySet<string> AllowedSources { get; }

    /// <summary>Snapshots synthetic host authority without accepting request fields as permission evidence.</summary>
    public SyntheticAcquisitionContext(Guid actorId, ArtifactSourcePin source, string authorizationRevision,
        IEnumerable<string> capabilities, IEnumerable<string> allowedSources)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(capabilities); ArgumentNullException.ThrowIfNull(allowedSources);
        if (actorId == Guid.Empty || string.IsNullOrWhiteSpace(authorizationRevision) || authorizationRevision.Length > 256)
            throw new ArgumentException("Invalid synthetic authority context.");
        ActorId = actorId; Source = source; AuthorizationRevision = authorizationRevision;
        Capabilities = capabilities.ToFrozenSet(StringComparer.Ordinal); AllowedSources = allowedSources.ToFrozenSet(StringComparer.Ordinal);
    }
}

public interface ISyntheticAcquisitionContextProvider
{
    /// <summary>Reads current synthetic actor, scope and capability facts; absence denies acquisition.</summary>
    ValueTask<SyntheticAcquisitionContext?> ReadCurrentAsync(CancellationToken cancellationToken);
}

/// <summary>Exact named synthetic source only. Implementations must not access a network or production data.</summary>
public interface ISyntheticUnitBodyProvider
{
    /// <summary>Rejects oversized input with SyntheticBodyLimitException before constructing a body.</summary>
    ValueTask<SyntheticUnitBody?> ReadAsync(string sourceId, ArtifactIdentity expectedIdentity, int maximumBodyBytes, CancellationToken cancellationToken);
}

/// <summary>Provisional synthetic processing exhaustion, never a missing or successful empty body.</summary>
public sealed class SyntheticBodyLimitException() : Exception("Synthetic body processing bound exceeded.");

public enum SyntheticAcquisitionState { Present, Missing, Unsupported, Denied, Invalid, Cancelled, Exhausted, Failed }

public sealed class SyntheticAcquisitionResult
{
    public SyntheticAcquisitionState State { get; }
    public SyntheticUnitBody? Body { get; }
    public bool FormalEvidenceEligible => false;
    /// <summary>Only the resolver can publish an admitted body; all failure paths retain null.</summary>
    internal SyntheticAcquisitionResult(SyntheticAcquisitionState state, SyntheticUnitBody? body = null)
    {
        State = state; Body = body;
    }
}

/// <summary>Pure synthetic acquisition seam. No registration, URL fetch, production capability or executable IR is enabled.</summary>
public sealed class SyntheticDependencyResolver(ISyntheticAcquisitionContextProvider contexts, ISyntheticUnitBodyProvider bodies)
{
    public const string Capability = "coglatas.prototype.acquire-dependency";
    public const string AllowedSource = "coglatas.synthetic.units";

    /// <summary>Rechecks current authority after successful or failed provider work before disclosing its outcome.</summary>
    public async ValueTask<SyntheticAcquisitionResult> AcquireAsync(PrototypeArtifact manifest, string entryKey,
        string sourceId, CancellationToken cancellationToken = default, int maximumBodyBytes = 1_048_576)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (maximumBodyBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBodyBytes));
        if (sourceId != AllowedSource) return new(SyntheticAcquisitionState.Denied);
        if (!manifest.SemanticsSupported || manifest.Identity.ArtifactType != PrototypeArtifactRegistry.DependencyManifest)
            return new(SyntheticAcquisitionState.Unsupported);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var authority = await contexts.ReadCurrentAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!Admits(authority, manifest.Source!, sourceId)) return new(SyntheticAcquisitionState.Denied);
            var entries = manifest.Data.Value.GetProperty("payload").GetProperty("entries");
            var found = entries.EnumerateArray().Where(e => e.GetProperty("entryKey").GetString() == entryKey).ToArray();
            if (found.Length != 1) return new(SyntheticAcquisitionState.Invalid);
            var entry = found[0];
            if (entry.GetProperty("dependencyKind").GetString() != "unit") return new(SyntheticAcquisitionState.Unsupported);
            var pin = entry.GetProperty("expected");
            var expected = new ArtifactIdentity(pin.GetProperty("artifactType").GetString()!, pin.GetProperty("schemaVersion").GetInt32(),
                pin.GetProperty("canonicalEncodingVersion").GetInt32(), pin.GetProperty("digest").GetString()!);
            SyntheticUnitBody? body = null;
            SyntheticAcquisitionState? providerFailure = null;
            try { body = await bodies.ReadAsync(sourceId, expected, maximumBodyBytes, cancellationToken); }
            catch (Exception error)
            {
                providerFailure = error switch
                {
                    SyntheticBodyLimitException => SyntheticAcquisitionState.Exhausted,
                    UnsupportedSourceException => SyntheticAcquisitionState.Unsupported,
                    FormatException => SyntheticAcquisitionState.Invalid,
                    UnauthorizedAccessException => SyntheticAcquisitionState.Denied,
                    _ => SyntheticAcquisitionState.Failed
                };
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Recheck before disclosing even absence. A previous permit is never substituted for current authority.
            var current = await contexts.ReadCurrentAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!Admits(current, manifest.Source!, sourceId) || current!.ActorId != authority!.ActorId ||
                current.AuthorizationRevision != authority.AuthorizationRevision)
                return new(SyntheticAcquisitionState.Denied);
            if (providerFailure is { } failed) return new(failed);
            if (body is null) return new(SyntheticAcquisitionState.Missing);
            if (body.CanonicalByteCount > maximumBodyBytes) return new(SyntheticAcquisitionState.Exhausted);
            if (body.Identity != expected || !body.Source.Data.Equals(manifest.Source!.Data) ||
                body.TargetId.ToString("D") != entry.GetProperty("target").GetProperty("id").GetString())
                return new(SyntheticAcquisitionState.Invalid);
            return new(SyntheticAcquisitionState.Present, body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(SyntheticAcquisitionState.Cancelled);
        }
        catch (UnsupportedSourceException) { return new(SyntheticAcquisitionState.Unsupported); }
        catch (FormatException) { return new(SyntheticAcquisitionState.Invalid); }
        catch (UnauthorizedAccessException) { return new(SyntheticAcquisitionState.Denied); }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        {
            return new(SyntheticAcquisitionState.Failed);
        }
    }

    /// <summary>Matches all requested scope and capability conditions against independently supplied current facts.</summary>
    private static bool Admits(SyntheticAcquisitionContext? authority, ArtifactSourcePin source, string sourceId) =>
        authority is not null && authority.Source.Data.Equals(source.Data) &&
        authority.Capabilities.Contains(Capability) && authority.AllowedSources.Contains(sourceId);
}
