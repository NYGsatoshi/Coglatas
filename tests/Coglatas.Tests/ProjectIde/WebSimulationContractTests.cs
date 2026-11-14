using System.Text;
using System.Text.Json;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class WebSimulationContractTests
{
    public static IEnumerable<object[]> Cases()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "frontend", "public", "assets", "projectide", "simulation-v1.json"))) directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("The actual simulation Product fixture is required.");
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "frontend", "public", "assets", "projectide", "simulation-v1.json")));
        var cases = data.RootElement.GetProperty("cases"); Assert.Equal(12, cases.GetArrayLength());
        foreach (var item in cases.EnumerateArray()) yield return [item.GetRawText()];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void BrowserRunBytesReplayTheActualFixedSourceCompilerPlanAndManifest(string fixture)
    {
        using var document = JsonDocument.Parse(fixture); var item = document.RootElement;
        var source = ProjectSourceCodec.Decode(Encoding.UTF8.GetBytes(item.GetProperty("sourceText").GetString()!)).RequireSource();
        var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(item.GetProperty("manifest").GetRawText()));
        var compilation = PureCoreCompiler.Compile(input, input.Compiler, WebPreviewContractTests.Snapshots(item, input));
        Assert.Equal(compilation.Result.Data, SourceJson.Parse(item.GetProperty("result").GetRawText()));
        if (compilation.Publication?.ExecutionPlan is not { } plan) {
            Assert.False(item.TryGetProperty("simulationRun", out _));
            Assert.Equal("coglatas.plan.required-validation-incomplete", compilation.Publication!.PlanNotProducedReason); return;
        }
        var frame = item.GetProperty("simulationRun"); var data = SourceJson.Parse(frame.GetProperty("text").GetString()!); var manifest = data.Value.GetProperty("manifest");
        var limits = manifest.GetProperty("limits");
        var request = new SimulationRunManifest(plan, Guid.Parse(manifest.GetProperty("runId").GetString()!), long.Parse(manifest.GetProperty("startTicks").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
            new(limits.GetProperty("maximumEvents").GetInt32(), limits.GetProperty("maximumOperations").GetInt32(),
                long.Parse(limits.GetProperty("maximumElapsedTicks").GetString()!, System.Globalization.CultureInfo.InvariantCulture), limits.GetProperty("maximumTraceBytes").GetInt32()));
        var run = ProjectSimulation.Execute(request, input.InputDigest);
        Assert.Equal(request.Data, SourceJson.Parse(manifest.GetRawText())); Assert.Equal(run.Data, data);
        Assert.Equal(run.Digest.Data, SourceJson.Parse(frame.GetProperty("digest").GetRawText()));
        Assert.Equal(item.GetProperty("key").GetString()!.Contains("-bounded-", StringComparison.Ordinal) ? CompilerExecutionState.ResourceExhausted : CompilerExecutionState.Completed, run.Execution);
        Assert.Equal(CompilerVerdict.NotRun, run.Verdict);
    }
}
