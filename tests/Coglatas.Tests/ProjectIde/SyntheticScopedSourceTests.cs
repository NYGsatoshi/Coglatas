using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;
using static Coglatas.Tests.ProjectIde.CompilerBindingClosureTests;
namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticScopedSourceTests
{
    private static LocalNetCompilerHost Host(CompilerInputManifest input)
    {
        CompilerArtifactIdentity Artifact(string key) => new(SourceJson.Parse(input.Settings.Value.GetProperty(key).GetRawText()));
        return new(new(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(value => value.GetString())));
    }
    [Fact]
    public async Task ExplicitAdmissionNavigatesOriginalForeignRecordsAndRetainsTheOrdinaryGuard()
    {
        var fixture = Graph(withRelation: true); var request = new CompilerHostRequest(new(fixture.Input), 1, fixture.Snapshots);
        using var view = new SyntheticWorkbenchViewModel(); view.BeginSynthetic(request); Assert.False(view.LoadSyntheticScopedSources());
        Assert.True(view.TryAccept(await Host(fixture.Input).CompileAsync(request))); Assert.Empty(view.ScopedSources);
        Assert.True(view.LoadSyntheticScopedSources()); Assert.Equal(3, view.ScopedSources.Count);
        var foreign = view.ScopedSources.Single(source => source.Context.Equals(fixture.A));
        var declaration = PlanDeclarationReader.Read(foreign).First(row => row.Declaration is TaskPlanDeclaration);
        var location = ProjectLocation.Create(declaration.DocumentId, foreign.Context, entityId: declaration.Declaration!.EntityId);
        Assert.False(view.Select(location)); Assert.True(view.NavigateScopedSource(location));
        Assert.Same(foreign, view.ScopedSource); Assert.Equal(declaration.Data, view.ScopedSourceRecord); Assert.Equal(location, view.ScopedSourceTarget);
        Assert.NotEqual(CompilerVerdict.Pass, view.Result!.Verdict); Assert.False(view.HasCompleteLocalPass);
        Assert.Contains(view.Problems, diagnostic => diagnostic.Code == "coglatas.semantic.external-closure-not-lowered");
        view.Reset(); Assert.Null(view.ScopedSourceTarget); Assert.Empty(view.ScopedSources);
    }
    [Fact]
    public async Task TwoVersionsShareStableEntitiesWithoutBorrowingSourcesOrCurrentRevision()
    {
        var fixture = Graph(twoBVersions: true); var request = new CompilerHostRequest(new(fixture.Input), 1, fixture.Snapshots);
        using var view = new SyntheticWorkbenchViewModel(); view.BeginSynthetic(request); Assert.True(view.TryAccept(await Host(fixture.Input).CompileAsync(request)));
        Assert.True(view.LoadSyntheticScopedSources()); var versions = view.ScopedSources.Where(source => source.ProjectId == fixture.B.Branch.ProjectId).ToArray();
        Assert.Equal(2, versions.Length);
        foreach (var source in versions)
        {
            var target = ProjectLocation.Create(DocumentId.Parse(Id(4)), source.Context, entityId: EntityId.Parse(Id(77)));
            Assert.True(view.NavigateScopedSource(target)); Assert.Equal(source.Data, view.ScopedSource!.Data);
        }
        var wrong = ProjectLocation.Create(DocumentId.Parse(Id(4)), SourceRevisionContext.Committed(new(fixture.B.Branch, RevisionId.Parse(Id(400)))), entityId: EntityId.Parse(Id(77)));
        Assert.False(view.NavigateScopedSource(wrong)); Assert.Null(view.ScopedSourceTarget); Assert.Null(view.ScopedSourceRecord);
        view.Reset(); Assert.Empty(view.ScopedSources); Assert.Null(view.ScopedSource);
    }
    [Theory]
    [InlineData("format")] [InlineData("input")] [InlineData("map")] [InlineData("region")]
    public void ExactCoreProjectionRefusesAlteredBytesEvenWithOtherwiseValidCompilation(string mode)
    {
        var fixture = Graph(withRelation: true); var compiled = PureCoreCompiler.Compile(fixture.Input, fixture.Input.Compiler, fixture.Snapshots);
        var exported = SyntheticScopedSourceProjection.Export(compiled)!; Assert.Same(compiled.Dependencies!.ScopedIr, SyntheticScopedSourceProjection.Read(compiled, exported));
        var changed = JsonNode.Parse(exported.CanonicalText)!.AsObject();
        if (mode == "format") changed["format"] = "example.future/2";
        if (mode == "input") changed["inputText"] = "{}";
        if (mode == "map") changed["sourceMap"]!["text"] = "{}";
        if (mode == "region") changed["regions"]!.AsArray().RemoveAt(0);
        Assert.Throws<FormatException>(() => SyntheticScopedSourceProjection.Read(compiled, SourceJson.Parse(changed.ToJsonString())));
    }
    [Fact]
    public async Task SynchronousResetDuringAdmissionCannotLeaveAScopedSelectionInANewerLifecycle()
    {
        var fixture = Graph(); var request = new CompilerHostRequest(new(fixture.Input), 1, fixture.Snapshots);
        using var view = new SyntheticWorkbenchViewModel(); view.BeginSynthetic(request); Assert.True(view.TryAccept(await Host(fixture.Input).CompileAsync(request)));
        var entered = false;
        view.PropertyChanged += (_, _) => { if (!entered && view.ScopedReadState == "Ready") { entered = true; view.Reset(); view.BeginSynthetic(new(new(fixture.Input), 2, fixture.Snapshots)); } };
        Assert.False(view.LoadSyntheticScopedSources()); Assert.True(entered); Assert.Empty(view.ScopedSources); Assert.Null(view.ScopedSourceTarget); Assert.Equal("Waiting", view.State);
    }
    [Fact]
    public async Task DeniedMaterializationCannotAcceptAnotherRequestsScopedResult()
    {
        var fixture = Graph(); var request = new CompilerHostRequest(new(fixture.Input), 1, fixture.Snapshots); var result = await Host(fixture.Input).CompileAsync(request);
        var denied = fixture.Snapshots.Select(item => new CompilerMaterializedSnapshot(item.SnapshotId, item.Version, new(item.Snapshot.Source, false, true, true))).ToArray();
        using var view = new SyntheticWorkbenchViewModel(); view.BeginSynthetic(new(new(fixture.Input), 1, denied));
        Assert.False(view.TryAccept(result)); Assert.False(view.LoadSyntheticScopedSources()); Assert.Empty(view.ScopedSources);
    }
    public static IEnumerable<object[]> ScopedCases()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "frontend/public/assets/projectide/scoped-sources-v1.json"))) directory = directory.Parent;
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory!.FullName, "frontend/public/assets/projectide/scoped-sources-v1.json")));
        foreach (var item in document.RootElement.GetProperty("cases").EnumerateArray()) yield return [item.GetRawText()];
    }
    [Theory]
    [MemberData(nameof(ScopedCases))]
    public async Task ActualCoreScenarioAndDraftFixturesOpenOriginalForeignDiagnosticsWithCompilerFail(string fixture)
    {
        new WebPreviewContractTests().BrowserFixturesReproduceExactCoreInputsResultsAndSourceLocations(fixture);
        using var document = JsonDocument.Parse(fixture); var item = document.RootElement;
        var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(item.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(item.GetProperty("manifest").GetRawText()));
        var request = new CompilerHostRequest(new(input), 1, WebPreviewContractTests.Snapshots(item, input));
        var response = await Host(input).CompileAsync(request); using var view = new SyntheticWorkbenchViewModel(); view.BeginSynthetic(request);
        Assert.True(view.TryAccept(response)); Assert.True(view.LoadSyntheticScopedSources());
        var region = response.Compilation!.Dependencies!.Regions.Single(region => region.Diagnostics.Any(diagnostic => diagnostic.Code == "coglatas.analysis.constraint-false"));
        var diagnostic = region.Diagnostics.First(diagnostic => diagnostic.Code == "coglatas.analysis.constraint-false");
        Assert.True(view.NavigateScopedDiagnostic(region, diagnostic)); Assert.Equal(diagnostic.Location.Context, view.ScopedSource!.Context);
        Assert.Equal(CompilerVerdict.Fail, view.Result!.Verdict); Assert.False(view.HasCompleteLocalPass);
        Assert.Equal(SourceJson.Parse(item.GetProperty("scopedSources").GetRawText()), SyntheticScopedSourceProjection.Export(response.Compilation));
        view.Reset(); Assert.Null(view.ScopedSourceRecord); Assert.Empty(view.ScopedSources);
    }

    [Theory]
    [InlineData("missing")] [InlineData("denied")] [InlineData("partial-declarations")] [InlineData("partial-relations")]
    public async Task IncompleteCapturedInputsCannotOpenForeignSources(string admission)
    {
        var fixture = Graph(bAdmission: admission); var request = new CompilerHostRequest(new(fixture.Input), 1, fixture.Snapshots);
        using var view = new SyntheticWorkbenchViewModel(); view.BeginSynthetic(request);
        Assert.True(view.TryAccept(await Host(fixture.Input).CompileAsync(request)));
        Assert.False(view.LoadSyntheticScopedSources()); Assert.Empty(view.ScopedSources); Assert.Null(view.ScopedSourceTarget);
    }
    [Fact]
    public async Task TypedForeignPortPathsUseTheirOriginalOwnerAndRejectMissingMembers()
    {
        var fixture = Graph(withRelation: true); var request = new CompilerHostRequest(new(fixture.Input), 1, fixture.Snapshots);
        using var view = new SyntheticWorkbenchViewModel(); view.BeginSynthetic(request); Assert.True(view.TryAccept(await Host(fixture.Input).CompileAsync(request)));
        Assert.True(view.LoadSyntheticScopedSources());
        var location = ProjectLocation.Create(DocumentId.Parse(Id(4)), fixture.A, "/payload/ports/" + Id(78), entityId: EntityId.Parse(Id(7)));
        Assert.True(view.NavigateScopedSource(location)); Assert.Equal(fixture.A, view.ScopedSource!.Context);
        Assert.False(view.NavigateScopedSource(ProjectLocation.Create(location.DocumentId, fixture.A, "/payload/ports/" + Id(400), entityId: location.EntityId)));
        Assert.Null(view.ScopedSourceTarget); Assert.Equal("invalid-location", view.NavigationState);
    }
}
