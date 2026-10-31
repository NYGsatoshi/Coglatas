using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;
using static Coglatas.Tests.ProjectIde.CompilerAnalysisFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class CompilerHostTests
{
    private static CompilerArtifactIdentity ArtifactAt(CompilerInputManifest input, string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
    private static CompilerHostProfile Profile(CompilerInputManifest input, bool noCapabilities = false) => new(input.Compiler,
        ArtifactAt(input, "schema"), ArtifactAt(input, "ruleset"), ArtifactAt(input, "semantics"), ArtifactAt(input, "ir"), ArtifactAt(input, "evaluator"),
        noCapabilities ? [] : input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString()));
    private sealed class FakeTransport(Func<CompilerHostRequest, CancellationToken, Task<CompilerHostResponse>> submit) : IServerCompilerTransport
    {
        public Task<CompilerHostResponse> SubmitAsync(CompilerHostRequest request, CancellationToken cancellationToken = default) => submit(request, cancellationToken);
    }
    private static (LocalNetCompilerHost Local, ServerCompilerHost Server) Hosts(CompilerInputManifest input)
    {
        var profile = Profile(input); var local = new LocalNetCompilerHost(profile);
        return (local, new(profile, new FakeTransport(local.CompileAsync)));
    }

    public static IEnumerable<object[]> Cases()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ProjectIde", "Fixtures", "compiler-host-parity-v1.json")));
        foreach (var item in fixture.RootElement.GetProperty("cases").EnumerateArray()) yield return [item.GetRawText()];
    }
    private static CompilerInputManifest Case(string name)
    {
        if (name == "constraint-pass") return Constraint(true);
        if (name == "constraint-fail") return Constraint(false);
        if (name == "constraint-unknown") return Constraint(null);
        if (name == "partial-fail") return Constraint(false, requirement: true);
        if (name == "budget") { var settings = Settings(); settings["executionProfile"]!["maximumOperations"] = 1; return Input(settings); }
        if (name == "opaque")
        {
            var original = Source(); var root = JsonNode.Parse(original.Documents[0].Root.Data.CanonicalText)!.AsObject(); root["example.future"] = true;
            return Input(source: ProjectSource.Create(original.Context, [SourceDocument.Create(DocumentId.Parse(Id(4)), "project", Json(root))]));
        }
        if (name == "empty") return Input();
        throw new ArgumentException("Unsupported parity fixture case.", nameof(name));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task LocalAndFakeServerAgreeOnExactSupportedAndIncompleteManifests(string fixture)
    {
        using var parsed = JsonDocument.Parse(fixture); var data = parsed.RootElement; var input = Case(data.GetProperty("case").GetString()!);
        var hosts = Hosts(input); var request = new CompilerHostRequest(new(input), 7);
        var local = await hosts.Local.CompileAsync(request); var server = await hosts.Server.CompileAsync(request);
        Assert.Equal(CompilerHostStatus.Completed, local.Status); Assert.Equal(CompilerHostStatus.Completed, server.Status);
        var a = local.Compilation!; var b = server.Compilation!;
        Assert.Equal(data.GetProperty("verdict").GetString(), a.Result.Verdict.ToString());
        Assert.Equal(data.GetProperty("coverage").GetString(), a.Result.Coverage.ToString());
        Assert.Equal(data.GetProperty("execution").GetString(), a.Result.Execution.ToString());
        Assert.Equal(a.Result.ResultDigest, b.Result.ResultDigest); Assert.Equal(a.Result.Data, b.Result.Data);
        Assert.Equal(a.Validation?.Ir?.Digest, b.Validation?.Ir?.Digest);
        Assert.Equal(a.Result.Diagnostics.Select(item => item.DiagnosticId), b.Result.Diagnostics.Select(item => item.DiagnosticId));
        Assert.Equal(CompilerFreshness.Current, CompilerHostFreshnessAssessment.Assess(local, input, 7).State);
        Assert.DoesNotContain(local.AttemptId.ToString(), a.Result.Data.CanonicalText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompletedGoldenMatchesIndependentCanonicalHashesAcrossHosts()
    {
        var golden = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ProjectIde", "Fixtures", "compiler-host-golden-v1.json")))!;
        var input = Input(); var hosts = Hosts(input); var response = await hosts.Local.CompileAsync(new(new(input)));
        var result = response.Compilation!;
        Assert.Equal(golden["inputDigest"]!.GetValue<string>(), input.InputDigest.Value);
        Assert.Equal(golden["inputId"]!.GetValue<string>(), input.InputId.ToString());
        Assert.Equal(golden["resultDigest"]!.GetValue<string>(), result.Result.ResultDigest.Value);
        Assert.Equal(golden["resultId"]!.GetValue<string>(), result.Result.ResultId.ToString());
        Assert.Equal(golden["irDigest"]!.GetValue<string>(), result.Validation!.Ir!.Digest.Value);
        Assert.Equal(golden["sourceMapDigest"]!.GetValue<string>(), result.Validation.Ir.SourceMapDigest.Value);
    }

    [Fact]
    public async Task TransferBuffersAndGenerationDoNotChangeSemanticIdentity()
    {
        var input = Input(); var source = input.Source.Data.ToCanonicalBytes(); var manifest = input.ToEnvelope().ToCanonicalBytes();
        var transfer = new CompilerInputTransfer(source, manifest, input.InputDigest); source[0] = 0; manifest[0] = 0;
        var recovered = transfer.RecoverSourceBytes(); recovered[0] = 0;
        Assert.Equal(input.InputDigest, transfer.Materialize().InputDigest);
        var host = Hosts(input).Local;
        var first = await host.CompileAsync(new(transfer, 1)); var next = await host.CompileAsync(new(transfer, 2));
        Assert.Equal(first.Compilation!.Result.ResultDigest, next.Compilation!.Result.ResultDigest);
        Assert.Equal(CompilerFreshness.Stale, CompilerHostFreshnessAssessment.Assess(first, input, 2).State);
        Assert.Equal(CompilerFreshness.Stale, CompilerHostFreshnessAssessment.Assess(first, Input(source: Source(9)), 1).State);
        Assert.Equal(CompilerFreshness.Undetermined, CompilerHostFreshnessAssessment.Assess(first, null, null).State);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("manifest")]
    [InlineData("expected-digest")]
    public async Task TamperedInputCannotReachSuccessfulCompilation(string subject)
    {
        var input = Input(); var source = input.Source.Data.ToCanonicalBytes(); var manifest = input.ToEnvelope().ToCanonicalBytes(); var digest = input.InputDigest;
        if (subject == "source") source[0] = 0;
        if (subject == "manifest") manifest[0] = 0;
        if (subject == "expected-digest") digest = ContentDigest.Compute("different", []);
        var response = await Hosts(input).Local.CompileAsync(new(new CompilerInputTransfer(source, manifest, digest)));
        Assert.Equal(CompilerHostStatus.InvalidInput, response.Status); Assert.Null(response.Compilation);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("capability")]
    public async Task HostCompatibilityIsCheckedBeforeTransportOrEvaluation(string mismatch)
    {
        var reference = Input(); var settings = Settings(); if (mismatch == "version") settings["ruleset"]!["version"] = "2";
        var requested = Input(settings); var profile = Profile(reference, noCapabilities: mismatch == "capability");
        var calls = 0; var server = new ServerCompilerHost(profile, new FakeTransport((_, _) => { calls++; throw new InvalidOperationException("Must not call transport"); }));
        var request = new CompilerHostRequest(new(requested));
        Assert.Equal(CompilerHostStatus.Unsupported, (await new LocalNetCompilerHost(profile).CompileAsync(request)).Status);
        Assert.Equal(CompilerHostStatus.Unsupported, (await server.CompileAsync(request)).Status); Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("inner-manifest")]
    [InlineData("generation")]
    [InlineData("outer-digest")]
    [InlineData("no-result")]
    [InlineData("invalid-status")]
    public async Task ServerRejectsResultSubstitutionAndIncompleteResponseBinding(string mismatch)
    {
        var input = Input(); var profile = Profile(input); var local = new LocalNetCompilerHost(profile); var request = new CompilerHostRequest(new(input), 3);
        var valid = await local.CompileAsync(request);
        var wrong = mismatch switch
        {
            "inner-manifest" => (await local.CompileAsync(new(new(Input(source: Source(9))), 3))) with { RequestedInputDigest = input.InputDigest },
            "generation" => valid with { ClientGeneration = 4 },
            "outer-digest" => valid with { RequestedInputDigest = ContentDigest.Compute("different", []) },
            "no-result" => valid with { Compilation = null },
            _ => valid with { Status = (CompilerHostStatus)999 }
        };
        var server = new ServerCompilerHost(profile, new FakeTransport((_, _) => Task.FromResult(wrong)));
        var rejected = await server.CompileAsync(request);
        Assert.Equal(CompilerHostStatus.InvalidInput, rejected.Status); Assert.Null(rejected.Compilation);
        Assert.Equal("coglatas.host.result-binding-mismatch", rejected.ReasonCode);
    }

    [Fact]
    public async Task CancellationDiscardsACompletedLateRemoteResponse()
    {
        var input = Input(); var profile = Profile(input); var request = new CompilerHostRequest(new(input), 5);
        var local = new LocalNetCompilerHost(profile); var completed = await local.CompileAsync(request);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<CompilerHostResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new ServerCompilerHost(profile, new FakeTransport((_, _) => { entered.SetResult(); return release.Task; }));
        using var cancellation = new CancellationTokenSource();
        var pending = server.CompileAsync(request, cancellation.Token); await entered.Task;
        cancellation.Cancel(); release.SetResult(completed);
        var response = await pending; Assert.Equal(CompilerHostStatus.Cancelled, response.Status); Assert.Null(response.Compilation);
        Assert.Equal(CompilerHostStatus.Cancelled, (await local.CompileAsync(request, cancellation.Token)).Status);
    }

    [Fact]
    public async Task HostErrorsAndUnsupportedSchemasExposeOnlySafeCodes()
    {
        var input = Input(); var profile = Profile(input); var request = new CompilerHostRequest(new(input));
        var server = new ServerCompilerHost(profile, new FakeTransport((_, _) => throw new InvalidOperationException("private details must not escape")));
        var error = await server.CompileAsync(request); Assert.Equal(CompilerHostStatus.InternalError, error.Status);
        Assert.Equal("coglatas.host.internal-error", error.ReasonCode); Assert.Null(error.Compilation);
        var node = JsonNode.Parse(input.Source.Data.CanonicalText)!; node["schemaVersion"] = 2;
        var future = new CompilerHostRequest(new CompilerInputTransfer(Encoding.UTF8.GetBytes(node.ToJsonString()), input.ToEnvelope().ToCanonicalBytes(), input.InputDigest));
        Assert.Equal(CompilerHostStatus.Unsupported, (await new LocalNetCompilerHost(profile).CompileAsync(future)).Status);
    }
}
