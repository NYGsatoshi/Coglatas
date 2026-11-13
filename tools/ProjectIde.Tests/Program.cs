using System.Text.Json;
using Coglatas.Domain.ProjectIde;

// Local files only. The manifest must already bind the exact Source and fixed settings.
var evaluator = new CompilerArtifactIdentity(Json(new { artifactId = "coglatas.local-project-test-evaluator",
    version = typeof(ProjectTestPlan).Assembly.GetName().Version!.ToString(),
    digest = ContentDigest.Compute("coglatas.project-test-evaluator-binary/1", await File.ReadAllBytesAsync(typeof(ProjectTestPlan).Assembly.Location)).Data.Value }));
if (args is ["--evaluator"])
{
    Console.WriteLine(evaluator.Data.CanonicalText); return 0;
}
if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: ProjectIde.Tests SOURCE_JSON INPUT_ENVELOPE_JSON NEW_RESULT_JSON");
    return 3;
}
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += Cancel;
try
{
    SourceJson artifact;
    int exitCode;
    try
    {
        var sourceBytes = await ReadBounded(args[0], cancellation.Token);
        var decoded = ProjectSourceCodec.Decode(sourceBytes, cancellationToken: cancellation.Token);
        cancellation.Token.ThrowIfCancellationRequested();
        var source = decoded.RequireSource();
        var envelope = SourceJson.Parse(await ReadBounded(args[1], cancellation.Token));
        // Reconstructing the envelope rejects Source, dependency and setting drift; it never rewrites a lock.
        var input = CompilerInputManifest.ReadEnvelope(source, envelope);
        var model = ProjectSemanticBinder.Bind(input, cancellationToken: cancellation.Token);
        var plan = ProjectTestPlan.Compile(model, evaluator, cancellationToken: cancellation.Token);
        var run = plan.Execute(input.InputDigest, cancellation.Token);
        artifact = Json(new { format = "coglatas.project-test-result-envelope/1", resultDigest = run.Digest.Data.Value, result = run.Data.Value });
        exitCode = run.Verdict switch { CompilerVerdict.Pass => 0, CompilerVerdict.Fail => 1, CompilerVerdict.Error => 3, _ => 2 };
    }
    catch (OperationCanceledException)
    {
        artifact = Failure("Cancelled", "NotRun", "coglatas.cancelled"); exitCode = 2;
    }
    catch (Exception error) when (error is FormatException or ArgumentException or OverflowException or JsonException)
    {
        artifact = Failure("NotRun", "Error", "coglatas.invalid-or-mismatched-input"); exitCode = 3;
    }
    // CreateNew preserves all existing files, including the Source and its input envelope.
    await using var output = new FileStream(args[2], FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
    await output.WriteAsync(artifact.ToCanonicalBytes());
    await output.FlushAsync();
    return exitCode;
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine("Project Test input/output unavailable; no successful result was published.");
    return 3;
}
finally { Console.CancelKeyPress -= Cancel; }

void Cancel(object? sender, ConsoleCancelEventArgs eventArgs) { eventArgs.Cancel = true; cancellation.Cancel(); }
static SourceJson Failure(string execution, string verdict, string reason) => Json(new
{ format = "coglatas.project-test-cli-failure/1", execution, verdict, reason });
static async Task<byte[]> ReadBounded(string path, CancellationToken cancellationToken)
{
    var maximum = new SourceCodecLimits().MaximumBytes;
    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
    var buffer = new byte[maximum + 1];
    var length = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);
    if (length > maximum) throw new FormatException("Input byte processing bound exceeded.");
    return buffer.AsSpan(0, length).ToArray();
}

static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
