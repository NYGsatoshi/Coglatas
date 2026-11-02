using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class SemanticInspectionTests
{
    private static CompilerHostProfile Profile(CompilerInputManifest input)
    {
        CompilerArtifactIdentity Artifact(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        return new(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public void InspectionIsDeterministicInputBoundAndRejectsAnotherRevisionOrLocation()
    {
        var input = Input(); var a = new CompilerSemanticInspection(ProjectSemanticBinder.Bind(input));
        var b = new CompilerSemanticInspection(ProjectSemanticBinder.Bind(input));
        Assert.True(a.BindingComplete); Assert.Equal(a.Data, b.Data); Assert.Equal(a.Digest, b.Digest);
        var symbol = Assert.Single(a.Symbols); Assert.Same(symbol, a.Inspect(symbol.Symbol.Location)); Assert.Null(symbol.DeclaredType);
        Assert.Null(a.Inspect(ProjectLocation.Create(symbol.Symbol.Location.DocumentId, Source(9).Context, entityId: symbol.Symbol.EntityId)));
        Assert.Null(a.Inspect(ProjectLocation.Create(symbol.Symbol.Location.DocumentId, input.Source.Context, "/not-a-symbol", entityId: symbol.Symbol.EntityId)));
        Assert.Empty(a.FindReferences(symbol.Symbol.Location));
        var next = new CompilerSemanticInspection(ProjectSemanticBinder.Bind(Input(source: Source(9))));
        Assert.NotEqual(a.Digest, next.Digest); Assert.Equal(input.Source.Data, a.Input.Source.Data);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => new CompilerSemanticInspection(ProjectSemanticBinder.Bind(input), cancelled.Token));
    }

    [Fact]
    public void PartialInspectionRetainsUnsupportedSymbolsWithoutClaimingIrOrKnownValues()
    {
        var original = Source(); var task = Obj(new { entityId = Id(7), documentId = Id(4), kind = "example.future", schemaVersion = 1, payload = new { opaque = true } });
        var source = ProjectSource.Create(original.Context, original.Documents, Json(new JsonArray(task)));
        var compilation = PureCoreCompiler.Compile(Input(source: source), Input().Compiler);
        Assert.Null(compilation.Validation!.Ir);
        var index = new CompilerSemanticInspection(compilation.Model!);
        Assert.False(index.BindingComplete);
        var unsupported = Assert.Single(index.Symbols, item => item.Symbol.EntityId == EntityId.Parse(Id(7)));
        Assert.Equal(ValueDecodeStatus.Unsupported, unsupported.Symbol.Status); Assert.Null(unsupported.DeclaredType);
    }

    [Fact]
    public void DefaultlessPortInspectionUsesItsActualSourceType()
    {
        var original = Source(); var type = Obj(new { kind = "coglatas.text", schemaVersion = 1, nullable = false, acceptsUnknown = true });
        var task = Obj(new { entityId = Id(7), documentId = Id(4), kind = "coglatas.task", schemaVersion = 1, payload = new
        {
            duration = new { type = new { kind = "coglatas.elapsed-duration", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = "1" },
            ports = new[] { new { portId = Id(8), direction = "coglatas.input", type, minimumConnections = 0, maximumConnections = 1 } }
        } });
        var input = Input(source: ProjectSource.Create(original.Context, original.Documents, Json(new JsonArray(task))));
        var index = new CompilerSemanticInspection(ProjectSemanticBinder.Bind(input));
        var port = Assert.Single(index.Symbols, item => item.Symbol.PortId is not null);
        Assert.Equal(Json(type), port.DeclaredType); Assert.Same(port, index.Inspect(port.Symbol.Location));
    }

    [Fact]
    public void FindReferencesUsesResolvedSymbolIdentityAndRetainsSourceCoverage()
    {
        var original = Source();
        var refs = Json(new JsonArray(Obj(new { kind = "coglatas.entity-reference", documentId = Id(4), entityId = Id(6) }),
            Obj(new { kind = "coglatas.entity-reference", documentId = Id(4), entityId = Id(99) })));
        var source = ProjectSource.Create(original.Context, original.Documents, sourceReferences: refs);
        var input = Input(source: source); var index = new CompilerSemanticInspection(ProjectSemanticBinder.Bind(input));
        Assert.False(index.BindingComplete);
        var symbol = Assert.Single(index.Symbols);
        Assert.Equal(ReferenceResolutionState.Resolved, Assert.Single(index.FindReferences(symbol.Symbol.Location)).State);
        Assert.Contains(index.References, item => item.State == ReferenceResolutionState.MissingWithinCompleteScope && item.SymbolKey is null);
        Assert.Equal(source.Data, index.Input.Source.Data);
    }

    [Fact]
    public async Task SyntheticWindowsViewModelPresentsSourceProblemsInspectorAndClearsEveryBoundary()
    {
        var input = CompilerAnalysisFixtures.Constraint(false); var host = new LocalNetCompilerHost(Profile(input));
        var request = new CompilerHostRequest(new(input), 7); var response = await host.CompileAsync(request);
        using var view = new SyntheticWorkbenchViewModel(); var notifications = 0;
        view.PropertyChanged += (_, _) => notifications++;
        view.BeginSynthetic(request); Assert.True(view.TryAccept(response));
        Assert.Equal("Ready", view.State); Assert.Equal(input.Source.Data, view.Source!.Data); Assert.NotEmpty(view.Problems);
        var symbol = view.Inspection!.Symbols[0]; Assert.True(view.Select(symbol.Symbol.Location)); Assert.True(view.Navigate());
        Assert.Equal(symbol.Symbol.Location, view.SourceTarget); Assert.True(notifications > 0);
        view.Reset(); Assert.Null(view.Source); Assert.Null(view.Result); Assert.Null(view.Inspection); Assert.Null(view.Selected);
        Assert.Null(view.SourceTarget); Assert.Empty(view.Problems); Assert.False(view.TryAccept(response));
        Assert.Throws<ArgumentException>(() => view.BeginSynthetic(request));
        view.BeginSynthetic(new(new(input), 8)); Assert.False(view.TryAccept(response)); Assert.Null(view.Result);
        view.Dispose(); Assert.False(view.TryAccept(response));
        Assert.Throws<ObjectDisposedException>(() => view.BeginSynthetic(new(new(input), 9)));
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("revision")]
    [InlineData("materialization")]
    [InlineData("status")]
    public async Task LateOrMismatchedResponsesCannotReplaceAnAcceptedSyntheticView(string mismatch)
    {
        var input = Input(); var host = new LocalNetCompilerHost(Profile(input)); var desired = new CompilerHostRequest(new(input), 4);
        var good = await host.CompileAsync(desired);
        var otherRequest = mismatch switch
        {
            "generation" => new CompilerHostRequest(new(input), 3),
            "revision" => new CompilerHostRequest(new(Input(source: Source(9))), 4),
            "materialization" => new CompilerHostRequest(new(input), 4, [new CompilerMaterializedSnapshot(ExternalSnapshotId.Parse(Id(20)), "1", new(Source(9), true, true, true))]),
            _ => desired
        };
        var bad = await host.CompileAsync(otherRequest);
        if (mismatch == "status") bad = bad with { Status = CompilerHostStatus.Cancelled };
        using var view = new SyntheticWorkbenchViewModel(); view.BeginSynthetic(desired); Assert.True(view.TryAccept(good));
        Assert.False(view.TryAccept(bad)); Assert.Same(good.Compilation!.Result, view.Result);
    }
}

public sealed class SyntheticWorkbenchFixtureTests
{
    [Theory]
    [MemberData(nameof(WebPreviewContractTests.Cases), MemberType = typeof(WebPreviewContractTests))]
    public async Task WindowsPureViewModelUsesTheSameCoreFixtureAndRejectsStaleOrUnexecutedResults(string fixture)
    {
        using var parsed = System.Text.Json.JsonDocument.Parse(fixture); var item = parsed.RootElement;
        var source = ProjectSourceCodec.Decode(System.Text.Encoding.UTF8.GetBytes(item.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(item.GetProperty("manifest").GetRawText()));
        CompilerArtifactIdentity Artifact(string field) => new(SourceJson.Parse(input.Settings.Value.GetProperty(field).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(value => value.GetString()));
        var request = new CompilerHostRequest(new(input), 17);
        var response = await new LocalNetCompilerHost(profile).CompileAsync(request);
        var completed = item.GetProperty("result").GetProperty("execution").GetString() == "coglatas.completed";
        var current = item.GetProperty("freshness").GetString() == "Current";
        using var view = new SyntheticWorkbenchViewModel();
        view.BeginSynthetic(current ? request : new(new(input), 18));
        if (!completed) response = response with { Status = CompilerHostStatus.Cancelled, Execution = null };
        Assert.Equal(completed && current, view.TryAccept(response));
        if (completed && current)
        {
            Assert.Equal(SourceJson.Parse(item.GetProperty("semanticInspection").GetRawText()), view.Inspection!.Data);
            Assert.Equal(SourceJson.Parse(item.GetProperty("result").GetRawText()), view.Result!.Data);
        }
        else { Assert.Null(view.Result); Assert.Empty(view.Problems); Assert.Null(view.Inspection); }
    }
}

public sealed class SyntheticWorkbenchTerminalStateTests
{
    [Theory]
    [InlineData(CompilerHostStatus.Cancelled)]
    [InlineData(CompilerHostStatus.Unsupported)]
    [InlineData(CompilerHostStatus.InvalidInput)]
    [InlineData(CompilerHostStatus.InternalError)]
    public void CurrentFailureStopsWaitingAndAChangedGenerationCanRetry(CompilerHostStatus status)
    {
        var input = Input(); var request = new CompilerHostRequest(new(input), 4);
        using var view = new SyntheticWorkbenchViewModel(); view.BeginSynthetic(request);
        var response = new CompilerHostResponse(status, "test.terminal", input.InputDigest, 4, Guid.NewGuid(), null);
        Assert.False(view.TryAccept(response)); Assert.Equal(status.ToString(), view.State);
        Assert.Empty(view.Problems); Assert.Null(view.Result); Assert.Null(view.Selected); Assert.Null(view.SourceTarget);
        view.BeginSynthetic(new(new(input), 5)); Assert.Equal("Waiting", view.State);
        Assert.False(view.TryAccept(response)); Assert.Equal("Waiting", view.State);
    }
}
