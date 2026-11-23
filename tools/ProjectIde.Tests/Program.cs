using System.Text.Json;
using Coglatas.Domain.ProjectIde;
using Coglatas.ProjectIde.TestCli;

if (args.Length > 0 && args[0] is "--export-test" or "--preview-test-edit") return await ProjectTestDefinitionsCommand.Run(args);

// Local files only. The manifest must already bind the exact Source and fixed settings.
var evaluator = new CompilerArtifactIdentity(Json(new { artifactId = "coglatas.local-project-test-evaluator",
    version = typeof(ProjectTestPlan).Assembly.GetName().Version!.ToString(),
    digest = ContentDigest.Compute("coglatas.project-test-evaluator-binary/1", await File.ReadAllBytesAsync(typeof(ProjectTestPlan).Assembly.Location)).Data.Value }));
if (args is ["--evaluator"])
{
    Console.WriteLine(evaluator.Data.CanonicalText); return 0;
}
var discover = args.Length > 0 && args[0] == "--discover";
if (discover) args = args[1..];
if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: ProjectIde.Tests [--discover] SOURCE_JSON INPUT_ENVELOPE_JSON NEW_OUTPUT_JSON");
    return 3;
}
using var cancellation = new ConsoleCancellation();
try
{
    SourceJson artifact;
    int exitCode;
    try
    {
        var sourceBytes = await ReadBounded(args[0], cancellation.Token);
        var decoded = ProjectSourceCodec.Decode(sourceBytes, cancellationToken: cancellation.Token);
        cancellation.Token.ThrowIfCancellationRequested();
        if (decoded.Status == SourceDecodeStatus.Unsupported && decoded.Source is null)
            throw new UnsupportedSourceException("Source representation is unsupported.");
        var source = decoded.RequireSource();
        var envelope = SourceJson.Parse(await ReadBounded(args[1], cancellation.Token));
        // Reconstructing the envelope rejects Source, dependency and setting drift; it never rewrites a lock.
        var input = CompilerInputManifest.ReadEnvelope(source, envelope);
        var model = ProjectSemanticBinder.Bind(input, cancellationToken: cancellation.Token);
        var plan = ProjectTestPlan.Compile(model, evaluator, cancellationToken: cancellation.Token);
        if (discover)
        {
            var inventory = plan.Discover(input.InputDigest, cancellation.Token);
            try { artifact = Discovery(inventory, out exitCode); }
            catch (FormatException)
            {
                artifact = Failure("ResourceExhausted", "NotRun", "coglatas.test-discovery-artifact-bound-exhausted"); exitCode = 2;
            }
        }
        else
        {
            var run = plan.Execute(input.InputDigest, cancellation.Token);
            artifact = Json(new { format = "coglatas.project-test-result-envelope/1", resultDigest = run.Digest.Data.Value, result = run.Data.Value });
            exitCode = run.Verdict switch { CompilerVerdict.Pass => 0, CompilerVerdict.Fail => 1, CompilerVerdict.Error => 3, _ => 2 };
        }
    }
    catch (OperationCanceledException)
    {
        artifact = Failure("Cancelled", "NotRun", "coglatas.cancelled"); exitCode = 2;
    }
    catch (UnsupportedSourceException)
    {
        artifact = Failure("NotRun", "NotRun", "coglatas.unsupported-input"); exitCode = 2;
    }
    catch (Exception error) when (error is FormatException or ArgumentException or OverflowException or JsonException)
    {
        artifact = Failure("NotRun", "Error", "coglatas.invalid-or-mismatched-input"); exitCode = 3;
    }
    return await ProjectTestOutput.PublishResultAsync(args[2], artifact.ToCanonicalBytes(), exitCode, cancellation.Token,
        static () => Failure("Cancelled", "NotRun", "coglatas.cancelled").ToCanonicalBytes());
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
{
    Console.Error.WriteLine("Project Test input/output unavailable; no successful result was published.");
    return 3;
}


static SourceJson Discovery(ProjectTestDiscovery inventory, out int exitCode)
{
    var reason = inventory.State switch
    {
        ProjectTestDiscoveryState.StaleInput => "coglatas.stale-input",
        ProjectTestDiscoveryState.Unavailable => "coglatas.test-discovery-unavailable",
        ProjectTestDiscoveryState.ResourceLimit => "coglatas.test-discovery-bound-exhausted",
        _ when inventory.DiscoveredTests == 0 => "coglatas.zero-tests",
        _ when inventory.Tests.Any(test => test.DecodeStatus == ValueDecodeStatus.Invalid) => "coglatas.invalid-test-discovery",
        _ when !inventory.EvaluatorMatches => "coglatas.evaluator-identity-mismatch",
        _ when inventory.Tests.Any(test => test.DecodeStatus == ValueDecodeStatus.Unsupported) => "coglatas.unsupported-test-discovery",
        _ when !inventory.BindingComplete => "coglatas.incomplete-test-binding",
        _ => "coglatas.test-discovery-ready"
    };
    exitCode = reason == "coglatas.test-discovery-ready" ? 0 : reason == "coglatas.invalid-test-discovery" ? 3 : 2;
    return Json(new { format = "coglatas.project-test-discovery-cli/1", state = inventory.State.ToString(),
        execution = "NotRun", verdict = "NotRun", evidenceKind = "coglatas.missing", executedAssertions = 0, reason,
        inputDigest = inventory.InputDigest?.Data.Value, planDigest = inventory.PlanDigest?.Data.Value,
        discoveredTests = inventory.DiscoveredTests, discoveryComplete = inventory.State == ProjectTestDiscoveryState.Ready,
        bindingComplete = inventory.BindingComplete, evaluatorMatches = inventory.EvaluatorMatches,
        tests = inventory.Tests.Select(test => new { testId = test.TestId.ToString(), location = test.Location.Data.Value,
            displayName = test.DisplayName, decodeStatus = test.DecodeStatus.ToString(), assertionCount = test.AssertionCount }).ToArray() });
}

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
