using System.Text;
using System.Text.Json;
using Coglatas.Application.ProjectIde.Core;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.Workbench.Synthetic;

namespace Coglatas.Tests.ProjectIde;

public sealed class SyntheticDiagnosticBasisTests
{
    public static IEnumerable<object[]> Cases() => WebCompilerOutputSnapshotContractTests.Cases().Where(row => {
        using var json = JsonDocument.Parse((string)row[0]);
        return json.RootElement.GetProperty("result").GetProperty("diagnostics").GetArrayLength() > 0;
    });
    private static (CompilerInputManifest Input, CompilerHostRequest Request, LocalNetCompilerHost Host) Prepare(string text)
    {
        using var json = JsonDocument.Parse(text); var row = json.RootElement;
        var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(row.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(row.GetProperty("manifest").GetRawText()));
        CompilerArtifactIdentity Artifact(string key) => new(SourceJson.Parse(input.Settings.Value.GetProperty(key).GetRawText()));
        var profile = new CompilerHostProfile(input.Compiler, Artifact("schema"), Artifact("ruleset"), Artifact("semantics"), Artifact("ir"), Artifact("evaluator"),
            input.Settings.Value.GetProperty("featureSet").EnumerateArray().Select(value => value.GetString()), input.PlanLowerer);
        return (input, new(new(input), 7, WebPreviewContractTests.Snapshots(row, input)), new(profile));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ActualCompilerBasisRetainsExactIdentityReferencesAndMissingReason(string text)
    {
        var (_, request, host) = Prepare(text);
        using var vm = new SyntheticWorkbenchViewModel(); vm.BeginSynthetic(request);
        Assert.Null(vm.ReadDiagnosticBasis()); Assert.True(vm.TryAccept(await host.CompileAsync(request)));
        var result = vm.Result!; var before = result.Data;
        Assert.NotEmpty(vm.Problems);
        foreach (var diagnostic in vm.Problems)
        {
            Assert.True(vm.SelectDiagnostic(diagnostic));
            var page = Assert.IsType<SyntheticDiagnosticBasisPage>(vm.ReadDiagnosticBasis());
            Assert.Same(diagnostic, page.Diagnostic); Assert.Equal(result.Input.InputId, page.InputId);
            Assert.Equal(result.Input.InputDigest, page.InputDigest); Assert.Equal(result.ResultId, page.ResultId);
            Assert.Equal(result.ResultDigest, page.ResultDigest); Assert.Equal(diagnostic.Evidence.Premises, page.Premises);
            Assert.Equal(diagnostic.Evidence.Premises.Count, page.Total); Assert.Equal(0, page.Offset); Assert.False(page.HasNext);
            Assert.False(page.LogicalStructureRecorded); Assert.False(page.CausalStructureRecorded); Assert.False(page.PremiseBodiesResolved);
            Assert.Equal(diagnostic.Location, page.Diagnostic.Location); Assert.Equal(diagnostic.Evidence.MissingReason, page.Diagnostic.Evidence.MissingReason);
        }
        Assert.Same(result, vm.Result); Assert.Equal(before, result.Data);
    }

    [Theory]
    [InlineData(1)] [InlineData(32)] [InlineData(33)] [InlineData(1023)] [InlineData(1024)]
    public void BoundedPagesPreserveEveryRecordedDigestWithoutInterpretingOrder(int count)
    {
        var (input, _, _) = Prepare((string)Cases().First()[0]);
        var original = PureCoreCompiler.Compile(input, input.Compiler).Result.Diagnostics.First();
        var evidence = new CompilerEvidenceBasis(CompilerEvidenceKind.Observation,
            Enumerable.Range(0, count).Select(index => ContentDigest.Compute("coglatas.test-basis/1", BitConverter.GetBytes(index))));
        var diagnostic = new CompilerDiagnostic(input, original.RuleId, original.RuleVersion, original.StageOrdinal,
            original.Code, original.Location, evidence);
        var result = new CompilerResult(input, CompilerExecutionState.Completed, CompilerCoverage.Partial, [], [diagnostic], [], false);
        var collected = new List<ContentDigest>();
        for (var offset = 0; offset < count; offset += SyntheticDiagnosticBasisPage.PageSize)
        {
            var page = Assert.IsType<SyntheticDiagnosticBasisPage>(SyntheticDiagnosticBasisPage.Read(result, diagnostic, offset));
            Assert.InRange(page.Premises.Count, 1, 32); Assert.Equal(count, page.Total); Assert.Equal(offset, page.Offset);
            Assert.Equal(offset + page.Premises.Count < count, page.HasNext); collected.AddRange(page.Premises);
        }
        Assert.Equal(evidence.Premises, collected); Assert.Equal(count, collected.Distinct().Count());
        foreach (var invalid in new[] { -32, 1, ((count + 31) / 32) * 32, int.MaxValue }) Assert.Null(SyntheticDiagnosticBasisPage.Read(result, diagnostic, invalid));
        var copied = new CompilerDiagnostic(input, original.RuleId, original.RuleVersion, original.StageOrdinal,
            original.Code, original.Location, evidence);
        Assert.Equal(diagnostic.DiagnosticId, copied.DiagnosticId); Assert.Null(SyntheticDiagnosticBasisPage.Read(result, copied));
    }

    [Theory]
    [InlineData("reset")] [InlineData("dispose")] [InlineData("new-input")] [InlineData("foreign-copy")] [InlineData("selection")]
    public async Task OldSelectionCannotSupplyBasisAfterInvalidation(string action)
    {
        var (input, request, host) = Prepare((string)Cases().First()[0]);
        var vm = new SyntheticWorkbenchViewModel();
        try
        {
            vm.BeginSynthetic(request); Assert.True(vm.TryAccept(await host.CompileAsync(request)));
            var diagnostic = vm.Problems.First(); Assert.True(vm.SelectDiagnostic(diagnostic));
            var retained = Assert.IsType<SyntheticDiagnosticBasisPage>(vm.ReadDiagnosticBasis());
            if (action == "reset") vm.Reset();
            else if (action == "dispose") vm.Dispose();
            else if (action == "new-input") vm.BeginSynthetic(new(new(input), 8, request.Snapshots));
            else if (action == "selection") vm.Select(diagnostic.Location);
            else {
                var other = await host.CompileAsync(request); var copy = other.Compilation!.Result.Diagnostics.Single(item => item.DiagnosticId == diagnostic.DiagnosticId);
                Assert.NotSame(diagnostic, copy); Assert.False(vm.SelectDiagnostic(copy));
            }
            Assert.Null(vm.ReadDiagnosticBasis()); Assert.Same(diagnostic, retained.Diagnostic);
        }
        finally { vm.Dispose(); }
    }
}
