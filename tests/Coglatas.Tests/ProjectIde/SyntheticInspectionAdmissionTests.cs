using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticInspectionAdmissionTests
{
    public static IEnumerable<object[]> OversizedCases() =>
        from protocol in new[] { "plain", "predicate", "operator", "pass" }
        from notification in new[] { "none", "reset", "dispose", "new-waiting", "new-ready", "new-refused" }
        select new object[] { protocol, notification };

    private static CompilerInputManifest LargeInput()
    {
        var original = Source();
        var declarations = new JsonArray();
        for (var task = 0; task < 128; task++)
        {
            var ports = new JsonArray();
            for (var port = 0; port < 32; port++)
                ports.Add(Obj(new { portId = Id(100000 + task * 32 + port), direction = "coglatas.input",
                    type = new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = true },
                    minimumConnections = 0, maximumConnections = 1 }));
            declarations.Add(Obj(new { entityId = Id(100 + task), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1,
                payload = new { duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1,
                    nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "1" }, ports } }));
        }
        var settings = Settings(); settings["executionProfile"]!["maximumOperations"] = 10000000;
        return Input(settings, ProjectSource.Create(original.Context, original.Documents, Json(declarations)));
    }

    private static CompilerHostProfile Profile(CompilerInputManifest input)
    {
        CompilerArtifactIdentity At(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        return new(input.Compiler, At("schema"), At("ruleset"), At("semantics"), At("ir"), At("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(value => value.GetString()));
    }

    private sealed class Attempt(CoreCompilation compilation, Func<bool> accept, IDisposable? session = null) : IDisposable
    {
        internal CoreCompilation Compilation { get; } = compilation;
        internal Func<bool> Accept { get; } = accept;
        public void Dispose() => session?.Dispose();
    }

    private static async Task<Attempt> Prepare(SyntheticWorkbenchViewModel view, CompilerInputManifest input, string protocol)
    {
        var core = new CompilerHostRequest(new(input), 1); var profile = Profile(input);
        switch (protocol)
        {
            case "plain":
                var response = await new LocalNetCompilerHost(profile).CompileAsync(core);
                Assert.Equal(CompilerHostStatus.Completed, response.Status);
                view.BeginSynthetic(core);
                return new(response.Compilation!, () => view.TryAccept(response));
            case "predicate":
                var predicate = new PredicateCompilerHostRequest(core, new());
                var predicateSession = new LocalPredicateCompilerRecordingSession(profile);
                var predicateManifest = predicateSession.Prepare(predicate, Guid.NewGuid());
                view.BeginPredicateSynthetic(predicateManifest, predicate);
                var arrays = await predicateSession.ExecuteAsync(predicateManifest, predicate);
                Assert.Equal(CompilerHostStatus.Completed, arrays.Response.Status);
                return new(arrays.Response.Compilation!, () => view.TryAcceptPredicate(arrays), predicateSession);
            case "operator":
                var operators = new PredicateObservationCompilerHostRequest(core, new());
                var operatorSession = new LocalPredicateObservationCompilerRecordingSession(profile);
                var operatorManifest = operatorSession.Prepare(operators, Guid.NewGuid());
                view.BeginOperatorSynthetic(operatorManifest, operators);
                var observed = await operatorSession.ExecuteAsync(operatorManifest, operators);
                Assert.Equal(CompilerHostStatus.Completed, observed.Response.Status);
                return new(observed.Response.Execution!.Compilation, () => view.TryAcceptOperator(observed), operatorSession);
            case "pass":
                var passes = new PassObservationCompilerHostRequest(core, new());
                var passSession = new LocalPassObservationCompilerRecordingSession(profile);
                var passManifest = passSession.Prepare(passes, Guid.NewGuid());
                view.BeginPassSynthetic(passManifest, passes);
                var boundaries = await passSession.ExecuteAsync(passManifest, passes);
                Assert.Equal(CompilerHostStatus.Completed, boundaries.Response.Status);
                return new(boundaries.Response.Execution!.Compilation, () => view.TryAcceptPass(boundaries), passSession);
            default: throw new ArgumentOutOfRangeException(nameof(protocol));
        }
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("predicate")]
    [InlineData("operator")]
    [InlineData("pass")]
    public async Task AdmittedSmallInspectionRetainsActualCompilerAndRecordingProtocol(string protocol)
    {
        using var view = new SyntheticWorkbenchViewModel();
        using var actual = await Prepare(view, Input(), protocol);
        Assert.True(actual.Accept()); Assert.Equal("Ready", view.State);
        Assert.Same(actual.Compilation.Result, view.Result); Assert.Single(view.Inspection!.Symbols);
        Assert.Equal(actual.Compilation.Input.Source.Data, view.Source!.Data);
    }

    [Theory]
    [MemberData(nameof(OversizedCases))]
    public async Task OversizedInspectionRefusesDisplayWithoutThrowingChangingCompilerFactsOrOverwritingNewerView(string protocol, string notification)
    {
        var input = LargeInput(); var source = input.Source.Data;
        using var view = new SyntheticWorkbenchViewModel();
        using var actual = await Prepare(view, input, protocol);
        var compilation = actual.Compilation; var result = compilation.Result.Data;
        Assert.Equal(CompilerExecutionState.Completed, compilation.Result.Execution);
        Assert.Equal(CompilerVerdict.Pass, compilation.Result.Verdict);
        Assert.NotNull(compilation.Validation!.Ir); Assert.Equal(4225, compilation.Model!.Symbols.Count);
        Assert.Throws<FormatException>(() => new CompilerSemanticInspection(compilation.Model));
        var newerInput = Input(); var newer = new CompilerHostRequest(new(newerInput), 2);
        using var cancelled = new CancellationTokenSource();
        if (notification == "new-refused") cancelled.Cancel();
        var newerResponse = await new LocalNetCompilerHost(Profile(newerInput)).CompileAsync(newer, cancelled.Token);
        var notified = false;
        view.PropertyChanged += (sender, _) =>
        {
            var current = Assert.IsType<SyntheticWorkbenchViewModel>(sender);
            if (notified || current.State != "InspectionUnavailable") return;
            notified = true;
            switch (notification)
            {
                case "reset": current.Reset(); break;
                case "dispose": current.Dispose(); break;
                case "new-waiting": current.BeginSynthetic(newer); break;
                case "new-ready": current.BeginSynthetic(newer); Assert.True(current.TryAccept(newerResponse)); break;
                case "new-refused": current.BeginSynthetic(newer); Assert.False(current.TryAccept(newerResponse)); break;
            }
        };
        var accepted = true;
        var accept = actual.Accept;
        var error = Record.Exception(() => accepted = accept());
        Assert.Equal(source, input.Source.Data); Assert.Equal(result, compilation.Result.Data);
        Assert.Equal(CompilerVerdict.Pass, compilation.Result.Verdict);
        Assert.Null(error); Assert.False(accepted); Assert.True(notified);
        Assert.Equal(notification switch { "reset" => "Empty", "dispose" => "Empty", "new-waiting" => "Waiting",
            "new-ready" => "Ready", "new-refused" => "Cancelled", _ => "InspectionUnavailable" }, view.State);
        if (notification == "new-ready")
        {
            Assert.Same(newerResponse.Compilation!.Result, view.Result); Assert.Single(view.Inspection!.Symbols);
            Assert.Equal(newerInput.Source.Data, view.Source!.Data);
        }
        else
        {
            Assert.Null(view.Result); Assert.Null(view.Inspection); Assert.Empty(view.Problems);
            Assert.Null(view.Selected); Assert.Null(view.SourceTarget); Assert.Null(view.PredicateReceiptMetadata);
            Assert.Null(view.OperatorReceiptMetadata); Assert.Null(view.PassReceiptMetadata);
            if (notification is "none") Assert.Equal(source, view.Source!.Data);
        }
        Assert.False(actual.Accept());
    }
}
