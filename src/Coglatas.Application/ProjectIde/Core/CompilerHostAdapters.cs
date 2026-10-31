using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Core;

/// <summary>Headless .NET adapter. Expensive parsing/compilation is scheduled away from the caller's UI context.</summary>
public sealed class LocalNetCompilerHost(CompilerHostProfile profile) : ICoreCompilerHost
{
    private readonly CompilerHostProfile _profile = profile ?? throw new ArgumentNullException(nameof(profile));
    public Task<CompilerHostResponse> CompileAsync(CompilerHostRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(() => Execute(request, cancellationToken), CancellationToken.None);
    }
    private CompilerHostResponse Execute(CompilerHostRequest request, CancellationToken cancellationToken)
    {
        var attempt = Guid.NewGuid();
        CompilerHostResponse Failure(CompilerHostStatus state, string code) => new(state, code, request.Input.ExpectedInputDigest, request.ClientGeneration, attempt, null);
        if (cancellationToken.IsCancellationRequested) return Failure(CompilerHostStatus.Cancelled, "coglatas.host.cancelled");
        try
        {
            var input = request.Input.Materialize();
            if (!_profile.Supports(input)) return Failure(CompilerHostStatus.Unsupported, "coglatas.host.compatibility-unavailable");
            var compilation = ProjectCompilation.Compile(input, _profile.Compiler, request.Snapshots, request.Units, request.Calendars, cancellationToken);
            if (cancellationToken.IsCancellationRequested) return Failure(CompilerHostStatus.Cancelled, "coglatas.host.cancelled");
            return new(compilation.Result.Execution == CompilerExecutionState.Cancelled ? CompilerHostStatus.Cancelled : CompilerHostStatus.Completed,
                "coglatas.host.result-produced", input.InputDigest, request.ClientGeneration, attempt, new CompilerHostExecutionReceipt(request, compilation));
        }
        catch (UnsupportedSourceException) { return Failure(CompilerHostStatus.Unsupported, "coglatas.host.unsupported-input"); }
        catch (Exception error) when (error is FormatException or ArgumentException)
        { return Failure(CompilerHostStatus.InvalidInput, "coglatas.host.invalid-input"); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { return Failure(CompilerHostStatus.Cancelled, "coglatas.host.cancelled"); }
        catch (Exception) { return Failure(CompilerHostStatus.InternalError, "coglatas.host.internal-error"); }
    }
}

/// <summary>Validates a server result against the original immutable request; it provides no production transport or access grant.</summary>
public sealed class ServerCompilerHost(CompilerHostProfile profile, IServerCompilerTransport transport) : ICoreCompilerHost
{
    private readonly CompilerHostProfile _profile = profile ?? throw new ArgumentNullException(nameof(profile));
    private readonly IServerCompilerTransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    public async Task<CompilerHostResponse> CompileAsync(CompilerHostRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        CompilerHostResponse Failure(CompilerHostStatus state, string code) => new(state, code, request.Input.ExpectedInputDigest, request.ClientGeneration, Guid.NewGuid(), null);
        if (cancellationToken.IsCancellationRequested) return Failure(CompilerHostStatus.Cancelled, "coglatas.host.cancelled");
        try
        {
            var input = await Task.Run(request.Input.Materialize, CancellationToken.None).ConfigureAwait(false);
            if (!_profile.Supports(input)) return Failure(CompilerHostStatus.Unsupported, "coglatas.host.compatibility-unavailable");
            var response = await _transport.SubmitAsync(request, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) return Failure(CompilerHostStatus.Cancelled, "coglatas.host.cancelled");
            var valid = await Task.Run(() => Verify(response, request, input), CancellationToken.None).ConfigureAwait(false);
            if (!valid) return Failure(CompilerHostStatus.InvalidInput, "coglatas.host.result-binding-mismatch");
            if (cancellationToken.IsCancellationRequested) return Failure(CompilerHostStatus.Cancelled, "coglatas.host.cancelled");
            return response with { ReasonCode = response.Compilation is null ? "coglatas.host.remote-not-completed" : "coglatas.host.result-produced" };
        }
        catch (UnsupportedSourceException) { return Failure(CompilerHostStatus.Unsupported, "coglatas.host.unsupported-input"); }
        catch (Exception error) when (error is FormatException or ArgumentException)
        { return Failure(CompilerHostStatus.InvalidInput, "coglatas.host.invalid-input"); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { return Failure(CompilerHostStatus.Cancelled, "coglatas.host.cancelled"); }
        catch (Exception) { return Failure(CompilerHostStatus.InternalError, "coglatas.host.internal-error"); }
    }
    private bool Verify(CompilerHostResponse? response, CompilerHostRequest request, CompilerInputManifest input)
    {
        if (response is null || !Enum.IsDefined(response.Status) || !Equals(response.RequestedInputDigest, input.InputDigest) || response.ClientGeneration != request.ClientGeneration || response.AttemptId.Version != 4)
            return false;
        if (response.Compilation is not { } compilation) return response.Status is not CompilerHostStatus.Completed;
        var execution = response.Execution!;
        if (!execution.InputBindingDigest.Equals(request.ExecutionInputDigest) || !execution.Materializations.Equals(request.Materializations) ||
            !execution.Digest.Equals(ContentDigest.Compute("coglatas.host-execution/1", execution.Data.ToCanonicalBytes()))) return false;
        if (response.Status is not (CompilerHostStatus.Completed or CompilerHostStatus.Cancelled)) return false;
        var result = compilation.Result;
        if (!compilation.Input.InputDigest.Equals(input.InputDigest) || !compilation.Input.Data.Equals(input.Data) ||
            !result.Input.InputDigest.Equals(input.InputDigest) || !result.Input.Data.Equals(input.Data) || !_profile.Supports(compilation.Input) ||
            !result.ResultDigest.Equals(ContentDigest.Compute("coglatas.compiler-result/1", result.Data.ToCanonicalBytes())) ||
            !result.ResultId.Equals(CompilerResultId.Derive(input.InputId, result.ResultDigest))) return false;
        if (response.Status == CompilerHostStatus.Cancelled && result.Execution != CompilerExecutionState.Cancelled) return false;
        if (response.Status == CompilerHostStatus.Completed && result.Execution == CompilerExecutionState.Cancelled) return false;
        if (compilation.Validation?.Ir is { } ir &&
            (!ir.Input.InputDigest.Equals(input.InputDigest) || !ir.Digest.Equals(ContentDigest.Compute("coglatas.project-ir/1", ir.Data.ToCanonicalBytes())) ||
             !result.Artifacts.Any(artifact => artifact.Role == "coglatas.ir" && artifact.Digest.Equals(ir.Digest)) ||
             !result.Artifacts.Any(artifact => artifact.Role == "coglatas.source-map" && artifact.Digest.Equals(ir.SourceMapDigest)) ||
             !result.Artifacts.Any(artifact => artifact.Role == "coglatas.semantic-model" && artifact.Digest.Equals(ir.SemanticModelDigest)))) return false;
        return true;
    }
}
