using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.TestCli;
using static Coglatas.Tests.ProjectIde.CompilerContractFixtures;

namespace Coglatas.Tests.ProjectIde;

public sealed class ProjectTestCliUnsupportedInputTests
{
    [Theory]
    [InlineData("purpose", false)] [InlineData("purpose", true)]
    [InlineData("operation-bound", false)] [InlineData("operation-bound", true)]
    [InlineData("scheduler", false)] [InlineData("scheduler", true)]
    [InlineData("rounding", false)] [InlineData("rounding", true)]
    [InlineData("coverage", false)] [InlineData("coverage", true)]
    [InlineData("solver", false)] [InlineData("solver", true)]
    public async Task ActualCliReportsUnsupportedInputAndPreservesExistingFiles(string mode, bool existingOutput)
    {
        var directory = Path.Combine(Path.GetTempPath(), "projectide-cli-unsupported-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Source();
            var envelope = JsonNode.Parse(Input(source: source).ToEnvelope().CanonicalText)!.AsObject();
            var settings = envelope["manifest"]!["settings"]!.AsObject();
            switch (mode)
            {
                case "purpose": settings["executionProfile"]!["purpose"] = "example.future"; break;
                case "operation-bound": settings["executionProfile"]!["maximumOperations"] = 10_000_001; break;
                case "scheduler": settings["scheduler"] = new JsonObject { ["kind"] = "example.future", ["artifact"] = settings["compiler"]!.DeepClone() }; break;
                case "rounding": settings["executionProfile"]!["rounding"] = "example.future"; break;
                case "coverage": settings["sourceCoverage"] = "example.future"; break;
                case "solver": settings["solver"] = new JsonObject { ["kind"] = "example.future" }; break;
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }
            var sourceBytes = ProjectSourceCodec.Encode(source);
            var envelopeBytes = SourceJson.Parse(envelope.ToJsonString()).ToCanonicalBytes();
            var retainedOutput = Encoding.UTF8.GetBytes("Existing result must survive unsupported input.");
            var sourcePath = Path.Combine(directory, "source.json");
            var envelopePath = Path.Combine(directory, "input.json");
            var outputPath = Path.Combine(directory, "result.json");
            File.WriteAllBytes(sourcePath, sourceBytes); File.WriteAllBytes(envelopePath, envelopeBytes);
            if (existingOutput) File.WriteAllBytes(outputPath, retainedOutput);

            int? exitCode = null;
            var entry = typeof(ConsoleCancellation).Assembly.EntryPoint!;
            // The generated entry point blocks on async Main; keep it outside the test synchronization context.
            var error = await Record.ExceptionAsync(async () =>
            {
                exitCode = await Task.Run(() => (int)entry.Invoke(null, [new[] { sourcePath, envelopePath, outputPath }])!);
            });
            Assert.Equal(sourceBytes, File.ReadAllBytes(sourcePath));
            Assert.Equal(envelopeBytes, File.ReadAllBytes(envelopePath));
            if (existingOutput) Assert.Equal(retainedOutput, File.ReadAllBytes(outputPath));
            if (error is TargetInvocationException invocation) Assert.IsType<UnsupportedSourceException>(invocation.InnerException);
            Assert.Null(error);
            Assert.Equal(existingOutput ? 3 : 2, exitCode);
            if (!existingOutput)
            {
                var failure = SourceJson.Parse(File.ReadAllBytes(outputPath)).Value;
                Assert.Equal("coglatas.project-test-cli-failure/1", failure.GetProperty("format").GetString());
                Assert.Equal("NotRun", failure.GetProperty("execution").GetString());
                Assert.Equal("NotRun", failure.GetProperty("verdict").GetString());
                Assert.Equal("coglatas.unsupported-input", failure.GetProperty("reason").GetString());
                Assert.Equal(4, failure.EnumerateObject().Count());
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("format", false, false)] [InlineData("format", false, true)]
    [InlineData("format", true, false)] [InlineData("format", true, true)]
    [InlineData("schemaId", false, false)] [InlineData("schemaId", false, true)]
    [InlineData("schemaId", true, false)] [InlineData("schemaId", true, true)]
    [InlineData("schemaVersion", false, false)] [InlineData("schemaVersion", false, true)]
    [InlineData("schemaVersion", true, false)] [InlineData("schemaVersion", true, true)]
    [InlineData("canonicalEncodingVersion", false, false)] [InlineData("canonicalEncodingVersion", false, true)]
    [InlineData("canonicalEncodingVersion", true, false)] [InlineData("canonicalEncodingVersion", true, true)]
    public async Task UnrepresentableSourceIsUnsupportedInBothCliCommands(string field, bool export, bool existingOutput)
    {
        var directory = Path.Combine(Path.GetTempPath(), "projectide-cli-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Source();
            var future = JsonNode.Parse(source.Data.CanonicalText)!.AsObject();
            if (field is "schemaVersion" or "canonicalEncodingVersion") future[field] = 2;
            else future[field] = "example.future";
            var sourceBytes = SourceJson.Parse(future.ToJsonString()).ToCanonicalBytes();
            var admission = ProjectSourceCodec.Decode(sourceBytes);
            Assert.Equal(SourceDecodeStatus.Unsupported, admission.Status); Assert.Null(admission.Source);
            var envelopeBytes = Input(source: source).ToEnvelope().ToCanonicalBytes();
            var retainedOutput = Encoding.UTF8.GetBytes("Existing result must survive unsupported Source.");
            var sourcePath = Path.Combine(directory, "source.json");
            var envelopePath = Path.Combine(directory, "input.json");
            var outputPath = Path.Combine(directory, "result.json");
            File.WriteAllBytes(sourcePath, sourceBytes); File.WriteAllBytes(envelopePath, envelopeBytes);
            if (existingOutput) File.WriteAllBytes(outputPath, retainedOutput);
            string[] arguments = export ? ["--export-test", sourcePath, envelopePath, Id(9), outputPath] : [sourcePath, envelopePath, outputPath];
            int? exitCode = null;
            var entry = typeof(ConsoleCancellation).Assembly.EntryPoint!;
            var error = await Record.ExceptionAsync(async () =>
            {
                exitCode = await Task.Run(() => (int)entry.Invoke(null, [arguments])!);
            });
            Assert.Equal(sourceBytes, File.ReadAllBytes(sourcePath));
            Assert.Equal(envelopeBytes, File.ReadAllBytes(envelopePath));
            if (existingOutput) Assert.Equal(retainedOutput, File.ReadAllBytes(outputPath));
            Assert.Null(error);
            Assert.Equal(existingOutput ? 3 : 2, exitCode);
            if (!existingOutput)
            {
                var failure = SourceJson.Parse(File.ReadAllBytes(outputPath)).Value;
                Assert.Equal(export ? "coglatas.project-test-definition-cli-failure/1" : "coglatas.project-test-cli-failure/1", failure.GetProperty("format").GetString());
                if (export)
                {
                    Assert.Equal("Unsupported", failure.GetProperty("state").GetString());
                    Assert.Equal(2, failure.EnumerateObject().Count());
                }
                else
                {
                    Assert.Equal("NotRun", failure.GetProperty("execution").GetString());
                    Assert.Equal("NotRun", failure.GetProperty("verdict").GetString());
                    Assert.Equal("coglatas.unsupported-input", failure.GetProperty("reason").GetString());
                    Assert.Equal(4, failure.EnumerateObject().Count());
                }
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

}
