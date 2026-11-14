using System.Text.Json.Nodes;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class ProjectTestAssertionNavigationTests
{
    // Exercise the internal resolver across the actual assembly boundary without widening its API.
    private static string Resolve(CompilerHostRequest request, ProjectSource source, CoreCompilation compilation, ProjectLocation location) =>
        (string)typeof(SyntheticWorkbenchViewModel).Assembly.GetType("Coglatas.ProjectIde.Workbench.Synthetic.SyntheticSourceNavigation", throwOnError: true)!
            .GetMethod("Resolve", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(null, new object?[] { request, source, compilation, location, false })!;

    [Theory]
    [InlineData("example.check")]
    [InlineData("example.a/b~c")]
    [InlineData("example.テスト/α~β")]
    public void ResolverOpensTheActualTestReferenceDiagnostic(string key)
    {
        var original = Source();
        var test = Obj(new { entityId = Id(9), documentId = Id(4), kind = "coglatas.project-test", schemaVersion = 1, payload = new
        {
            assertions = new[] { new { key, kind = "coglatas.assert-boolean", target = CompilerAnalysisFixtures.Reference(99, original.Context), portId = Id(8),
                expected = new { type = new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = true } } }
        } });
        var input = Input(source: ProjectSource.Create(original.Context, original.Documents, Json(new JsonArray(test))));
        var compilation = PureCoreCompiler.Compile(input, input.Compiler);
        Assert.Equal(CompilerVerdict.Fail, compilation.Result.Verdict);
        var diagnostic = Assert.Single(compilation.Result.Diagnostics);
        var request = new CompilerHostRequest(new(input), 1);
        Assert.Equal("ready", Resolve(request, input.Source, compilation, diagnostic.Location));
        Assert.Equal(CompilerVerdict.Fail, compilation.Result.Verdict);
        var foreign = ProjectLocation.Create(diagnostic.Location.DocumentId, Source(99).Context, diagnostic.Location.PropertyPath, entityId: diagnostic.Location.EntityId);
        Assert.Equal("wrong-scope", Resolve(request, input.Source, compilation, foreign));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("wrong-kind")]
    public void KeyMembershipIsUniqueAndRestrictedToTheTypedTestDeclaration(string mode)
    {
        var original = Source();
        var assertion = new { key = "example.check", kind = "coglatas.assert-boolean", target = CompilerAnalysisFixtures.Reference(99, original.Context), portId = Id(8),
            expected = new { type = new { kind = "coglatas.boolean", schemaVersion = 1, nullable = false, acceptsUnknown = false }, state = "coglatas.known", value = true } };
        var test = Obj(new { entityId = Id(9), documentId = Id(4), kind = mode == "wrong-kind" ? "coglatas.task" : "coglatas.project-test", schemaVersion = 1,
            payload = new { assertions = mode == "duplicate" ? new[] { assertion, assertion } : new[] { assertion } } });
        var input = Input(source: ProjectSource.Create(original.Context, original.Documents, Json(new JsonArray(test))));
        var compilation = PureCoreCompiler.Compile(input, input.Compiler);
        var location = ProjectLocation.Create(input.Source.Documents[0].DocumentId, input.Source.Context,
            "/payload/assertions/" + (mode == "missing" ? "example.absent" : "example.check") + "/target", entityId: EntityId.Parse(Id(9)));
        Assert.Equal("invalid-location", Resolve(new(new(input), 1), input.Source, compilation, location));
    }
}
