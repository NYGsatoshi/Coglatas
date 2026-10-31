using System.Collections.ObjectModel;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Core;

/// <summary>Host-owned runtime binding. Populate from trusted startup artifacts, never from an untrusted request.</summary>
public sealed class CompilerHostProfile
{
    public const int ProtocolVersion = 1;
    public CompilerArtifactIdentity Compiler { get; }
    public IReadOnlyDictionary<string, CompilerArtifactIdentity> Contracts { get; }
    public IReadOnlyList<string> Capabilities { get; }
    public CompilerHostProfile(CompilerArtifactIdentity compiler, CompilerArtifactIdentity schema, CompilerArtifactIdentity ruleset,
        CompilerArtifactIdentity semantics, CompilerArtifactIdentity ir, CompilerArtifactIdentity evaluator, IEnumerable<string?> capabilities)
    {
        ArgumentNullException.ThrowIfNull(compiler); ArgumentNullException.ThrowIfNull(schema); ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(semantics); ArgumentNullException.ThrowIfNull(ir); ArgumentNullException.ThrowIfNull(evaluator); ArgumentNullException.ThrowIfNull(capabilities);
        var values = capabilities.Take(4097).ToArray();
        if (values.Length > 4096 || values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 256 || !value.Contains('.')) || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new ArgumentException("Capabilities must be bounded unique namespaced tokens.");
        Compiler = compiler;
        Contracts = new ReadOnlyDictionary<string, CompilerArtifactIdentity>(new Dictionary<string, CompilerArtifactIdentity>(StringComparer.Ordinal)
        { ["compiler"] = compiler, ["schema"] = schema, ["ruleset"] = ruleset, ["semantics"] = semantics, ["ir"] = ir, ["evaluator"] = evaluator });
        Capabilities = Array.AsReadOnly(values.Select(value => value!).Order(StringComparer.Ordinal).ToArray());
    }
    public bool Supports(CompilerInputManifest input)
    {
        ArgumentNullException.ThrowIfNull(input);
        foreach (var pair in Contracts)
            if (!SourceJson.Parse(input.Settings.Value.GetProperty(pair.Key).GetRawText()).Equals(pair.Value.Data)) return false;
        return input.Settings.Value.GetProperty("featureSet").EnumerateArray().All(value => Capabilities.Contains(value.GetString()!, StringComparer.Ordinal));
    }
}

/// <summary>Immutable transfer of exact canonical Source and manifest bytes. Every read reconstructs and verifies identity.</summary>
public sealed class CompilerInputTransfer
{
    private readonly byte[] _source;
    private readonly byte[] _manifest;
    public ContentDigest ExpectedInputDigest { get; }
    public CompilerInputTransfer(CompilerInputManifest input) : this(SourceBytes(input), ManifestBytes(input), input.InputDigest) { }
    public CompilerInputTransfer(ReadOnlySpan<byte> source, ReadOnlySpan<byte> manifest, ContentDigest expectedInputDigest)
    {
        ArgumentNullException.ThrowIfNull(expectedInputDigest);
        if (source.Length > 4_194_304 || manifest.Length > 4_194_304) throw new ArgumentException("Transfer exceeds the initial canonical byte profile.");
        _source = source.ToArray(); _manifest = manifest.ToArray(); ExpectedInputDigest = expectedInputDigest;
    }
    private static byte[] SourceBytes(CompilerInputManifest input) { ArgumentNullException.ThrowIfNull(input); return input.Source.Data.ToCanonicalBytes(); }
    private static byte[] ManifestBytes(CompilerInputManifest input) { ArgumentNullException.ThrowIfNull(input); return input.ToEnvelope().ToCanonicalBytes(); }
    public byte[] RecoverSourceBytes() => (byte[])_source.Clone();
    public byte[] RecoverManifestBytes() => (byte[])_manifest.Clone();
    public CompilerInputManifest Materialize()
    {
        var decoded = ProjectSourceCodec.Decode(_source);
        if (decoded.Source is null)
        {
            if (decoded.Status == SourceDecodeStatus.Invalid) throw new FormatException("Invalid transferred Source.");
            throw new UnsupportedSourceException("Unsupported transferred Source schema.");
        }
        var input = CompilerInputManifest.ReadEnvelope(decoded.Source, SourceJson.Parse(_manifest));
        if (!input.InputDigest.Equals(ExpectedInputDigest)) throw new FormatException("Transferred input differs from the requested digest.");
        return input;
    }
}

public sealed class CompilerHostRequest
{
    public CompilerInputTransfer Input { get; }
    public long ClientGeneration { get; }
    public IReadOnlyList<CompilerMaterializedSnapshot> Snapshots { get; }
    public IReadOnlyList<UnitDefinition> Units { get; }
    public IReadOnlyList<CalendarSnapshot> Calendars { get; }
    public CompilerHostRequest(CompilerInputTransfer input, long clientGeneration = 0, IEnumerable<CompilerMaterializedSnapshot?>? snapshots = null,
        IEnumerable<UnitDefinition?>? units = null, IEnumerable<CalendarSnapshot?>? calendars = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (clientGeneration < 0) throw new ArgumentOutOfRangeException(nameof(clientGeneration));
        Input = input; ClientGeneration = clientGeneration;
        Snapshots = Freeze(snapshots ?? [], 32); Units = Freeze(units ?? [], 1024); Calendars = Freeze(calendars ?? [], 128);
    }
    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T?> values, int maximum) where T : class
    {
        var frozen = values.Take(maximum + 1).ToArray();
        if (frozen.Length > maximum || frozen.Any(value => value is null)) throw new ArgumentException("Invalid or oversized materialized input collection.");
        return Array.AsReadOnly(frozen.Select(value => value!).ToArray());
    }
}

public enum CompilerHostStatus { Completed, Cancelled, Unsupported, InvalidInput, InternalError }

/// <summary>Invocation metadata is outside the deterministic CompilerResult. Host completion never implies Pass.</summary>
public sealed record CompilerHostResponse(CompilerHostStatus Status, string ReasonCode, ContentDigest RequestedInputDigest,
    long ClientGeneration, Guid AttemptId, CoreCompilation? Compilation);

public interface ICoreCompilerHost
{
    Task<CompilerHostResponse> CompileAsync(CompilerHostRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Transport seam only; real HTTP/job/authentication/operational adapters retain their existing gates.</summary>
public interface IServerCompilerTransport
{
    Task<CompilerHostResponse> SubmitAsync(CompilerHostRequest request, CancellationToken cancellationToken = default);
}

public sealed record CompilerHostFreshnessAssessment(CompilerFreshness State, CompilerResultId? ResultId, IReadOnlyList<string> ChangedDimensions)
{
    public static CompilerHostFreshnessAssessment Assess(CompilerHostResponse response, CompilerInputManifest? desired, long? desiredGeneration)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.Compilation is null || desired is null || desiredGeneration is null)
            return new(CompilerFreshness.Undetermined, response.Compilation?.Result.ResultId, Array.AsReadOnly(new[] { "coglatas.desired-binding-unavailable" }));
        var changed = new List<string>();
        if (!response.Compilation.Input.InputDigest.Equals(desired.InputDigest)) changed.Add("coglatas.input-manifest");
        if (response.ClientGeneration != desiredGeneration.Value) changed.Add("coglatas.client-generation");
        return new(changed.Count == 0 ? CompilerFreshness.Current : CompilerFreshness.Stale, response.Compilation.Result.ResultId, changed.AsReadOnly());
    }
}
