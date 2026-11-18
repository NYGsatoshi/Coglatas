using System.Text;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.TestCli;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class ProjectTestCliDiscoveryTests
{
    [Theory]
    [InlineData("ready", false, 0)] [InlineData("ready", true, 3)]
    [InlineData("counterexample", false, 0)] [InlineData("counterexample", true, 3)]
    [InlineData("zero", false, 2)] [InlineData("zero", true, 3)]
    [InlineData("unsupported", false, 2)] [InlineData("unsupported", true, 3)]
    [InlineData("invalid", false, 3)] [InlineData("invalid", true, 3)]
    [InlineData("evaluator", false, 2)] [InlineData("evaluator", true, 3)]
    [InlineData("partial", false, 2)] [InlineData("partial", true, 3)]
    [InlineData("budget", false, 2)] [InlineData("budget", true, 3)]
    [InlineData("drift", false, 3)] [InlineData("drift", true, 3)]
    [InlineData("future-source", false, 2)] [InlineData("future-source", true, 3)]
    public async Task ActualDiscoveryCliPreservesInputsAndReportsInventoryWithoutExecutingTests(string mode, bool existingOutput, int expectedExit)
    {
        var directory = Path.Combine(Path.GetTempPath(), "projectide-cli-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var basis = ProjectTestRunnerTests.Fixture(mutate: (port, assertions, test) =>
            {
                if (mode == "counterexample") port["defaultValue"]!["value"] = false;
                if (mode == "zero") test["kind"] = "example.not-test";
                if (mode == "unsupported") assertions[0]!["kind"] = "example.future";
                if (mode == "invalid") assertions[0]!["expected"]!["type"]!["kind"] = "coglatas.decimal";
            });
            var settings = JsonNode.Parse(basis.Settings.CanonicalText)!.AsObject();
            if (mode != "evaluator") settings["evaluator"] = JsonNode.Parse((await HostEvaluator()).Data.CanonicalText);
            if (mode == "partial") settings["sourceCoverage"] = "coglatas.partial";
            if (mode == "budget") settings["executionProfile"]!["maximumOperations"] = 1;
            var input = new CompilerInputManifest(basis.Source, Json(settings));
            var sourceBytes = ProjectSourceCodec.Encode(input.Source);
            if (mode is "drift" or "future-source")
            {
                var source = JsonNode.Parse(input.Source.Data.CanonicalText)!.AsObject();
                if (mode == "future-source") source["format"] = "example.future";
                else source["declarations"]![1]!["displayName"] = "Changed after envelope";
                sourceBytes = Json(source).ToCanonicalBytes();
            }
            var envelopeBytes = input.ToEnvelope().ToCanonicalBytes();
            var retained = Encoding.UTF8.GetBytes("Existing output is immutable.");
            var sourcePath = Path.Combine(directory, "source.json"); var envelopePath = Path.Combine(directory, "input.json");
            var outputPath = Path.Combine(directory, "inventory.json");
            await File.WriteAllBytesAsync(sourcePath, sourceBytes); await File.WriteAllBytesAsync(envelopePath, envelopeBytes);
            if (existingOutput) await File.WriteAllBytesAsync(outputPath, retained);
            Assert.Equal(expectedExit, await Invoke(["--discover", sourcePath, envelopePath, outputPath]));
            Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(sourcePath)); Assert.Equal(envelopeBytes, await File.ReadAllBytesAsync(envelopePath));
            if (existingOutput) { Assert.Equal(retained, await File.ReadAllBytesAsync(outputPath)); return; }
            var artifact = SourceJson.Parse(await File.ReadAllBytesAsync(outputPath)).Value;
            Assert.Equal("NotRun", artifact.GetProperty("execution").GetString());
            if (mode is "drift" or "future-source")
            {
                Assert.Equal("coglatas.project-test-cli-failure/1", artifact.GetProperty("format").GetString());
                Assert.Equal(mode == "drift" ? "Error" : "NotRun", artifact.GetProperty("verdict").GetString()); return;
            }
            Assert.Equal("coglatas.project-test-discovery-cli/1", artifact.GetProperty("format").GetString());
            Assert.Equal("NotRun", artifact.GetProperty("verdict").GetString()); Assert.Equal(0, artifact.GetProperty("executedAssertions").GetInt32());
            Assert.Equal("coglatas.missing", artifact.GetProperty("evidenceKind").GetString());
            Assert.False(artifact.TryGetProperty("result", out _)); Assert.False(artifact.TryGetProperty("resultDigest", out _));
            if (mode == "budget")
            {
                Assert.Equal("Unavailable", artifact.GetProperty("state").GetString());
                Assert.Empty(artifact.GetProperty("tests").EnumerateArray()); Assert.Equal(System.Text.Json.JsonValueKind.Null, artifact.GetProperty("inputDigest").ValueKind);
            }
            else
            {
                Assert.Equal("Ready", artifact.GetProperty("state").GetString());
                Assert.Equal(input.InputDigest.Data.Value.GetRawText(), artifact.GetProperty("inputDigest").GetRawText());
                var tests = artifact.GetProperty("tests"); Assert.Equal(mode == "zero" ? 0 : 1, tests.GetArrayLength());
                if (mode != "zero") Assert.Equal(Id(9), tests[0].GetProperty("testId").GetString());
            }
            var reason = mode switch
            {
                "zero" => "coglatas.zero-tests", "unsupported" => "coglatas.unsupported-test-discovery",
                "invalid" => "coglatas.invalid-test-discovery", "evaluator" => "coglatas.evaluator-identity-mismatch",
                "partial" => "coglatas.incomplete-test-binding", "budget" => "coglatas.test-discovery-unavailable",
                _ => "coglatas.test-discovery-ready"
            };
            Assert.Equal(reason, artifact.GetProperty("reason").GetString());
            if (mode == "counterexample")
            {
                var runPath = Path.Combine(directory, "run.json");
                Assert.Equal(1, await Invoke([sourcePath, envelopePath, runPath]));
                Assert.Equal("Fail", SourceJson.Parse(await File.ReadAllBytesAsync(runPath)).Value.GetProperty("result").GetProperty("verdict").GetString());
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task DiscoveryCannotOverwriteItsSourceOrInputEnvelope(bool outputIsEnvelope)
    {
        var directory = Path.Combine(Path.GetTempPath(), "projectide-cli-discovery-input-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var basis = ProjectTestRunnerTests.Fixture(); var settings = JsonNode.Parse(basis.Settings.CanonicalText)!.AsObject();
            settings["evaluator"] = JsonNode.Parse((await HostEvaluator()).Data.CanonicalText);
            var input = new CompilerInputManifest(basis.Source, Json(settings));
            var source = ProjectSourceCodec.Encode(input.Source); var envelope = input.ToEnvelope().ToCanonicalBytes();
            var a = Path.Combine(directory, "source.json"); var b = Path.Combine(directory, "input.json");
            await File.WriteAllBytesAsync(a, source); await File.WriteAllBytesAsync(b, envelope);
            Assert.Equal(3, await Invoke(["--discover", a, b, outputIsEnvelope ? b : a]));
            Assert.Equal(source, await File.ReadAllBytesAsync(a)); Assert.Equal(envelope, await File.ReadAllBytesAsync(b));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task<CompilerArtifactIdentity> HostEvaluator() => new(Json(new
    {
        artifactId = "coglatas.local-project-test-evaluator", version = typeof(ProjectTestPlan).Assembly.GetName().Version!.ToString(),
        digest = ContentDigest.Compute("coglatas.project-test-evaluator-binary/1", await File.ReadAllBytesAsync(typeof(ProjectTestPlan).Assembly.Location)).Data.Value
    }));
    private static async Task<int> Invoke(string[] args) => await Task.Run(() => (int)typeof(ConsoleCancellation).Assembly.EntryPoint!.Invoke(null, [args])!);
}
