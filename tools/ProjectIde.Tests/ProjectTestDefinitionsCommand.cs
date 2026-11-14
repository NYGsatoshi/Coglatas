using System.Text.Json;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.TestCli;

internal static class ProjectTestDefinitionsCommand
{
    private static readonly SourceCodecLimits OutputLimits = new(MaximumBytes: 16_777_216, MaximumDepth: 68);
    internal static async Task<int> Run(string[] args)
    {
        var export = args[0] == "--export-test";
        if (args.Length != (export ? 5 : 7))
        {
            Console.Error.WriteLine("Usage: ProjectIde.Tests --export-test SOURCE INPUT_ENVELOPE ENTITY_ID NEW_DEFINITION | --preview-test-edit SOURCE INPUT_ENVELOPE ENTITY_ID DEFINITION NEXT_CONTEXT NEW_PLAN");
            return 3;
        }
        using var cancellation = new ConsoleCancellation(); var token = cancellation.Token;
        try
        {
            SourceJson artifact; int code;
            try
            {
                var source = ProjectSourceCodec.Decode(await Read(args[1], token), cancellationToken: token).RequireSource();
                var input = CompilerInputManifest.ReadEnvelope(source, SourceJson.Parse(await Read(args[2], token)));
                var model = ProjectSemanticBinder.Bind(input, cancellationToken: token); var id = EntityId.Parse(args[3]);
                var declarations = model.Declarations.Where(item => item.Data.Value.TryGetProperty("entityId", out var identity) &&
                    identity.ValueKind == JsonValueKind.String && identity.GetString() == id.ToString()).Take(2).ToArray();
                if (declarations.Length != 1) { artifact = Failure("Unavailable"); code = 2; }
                else
                {
                    var target = ProjectLocation.Create(declarations[0].DocumentId, source.Context, entityId: id);
                    if (export)
                    {
                        var result = ProjectTestEditing.Export(model, target, input.InputDigest, token);
                        artifact = result.Definition ?? Failure(result.State.ToString()); code = result.State == ProjectTestEditState.Ready ? 0 : 2;
                    }
                    else
                    {
                        var definition = SourceJson.Parse(await Read(args[4], token));
                        var next = SourceRevisionContext.Parse(SourceJson.Parse(await Read(args[5], token)));
                        var result = ProjectTestEditing.Preview(model, target, input.InputDigest, definition, next, token);
                        artifact = Json(new { format = "coglatas.project-test-edit-envelope/1", state = result.State.ToString(), plan = result.Plan?.Data.Value,
                            afterSource = result.Plan?.After.Source.Data.Value, afterManifest = result.Plan?.After.ToEnvelope().Value });
                        code = result.State switch { ProjectTestEditState.Ready => 0,
                            ProjectTestEditState.InvalidDefinition or ProjectTestEditState.IdentityMismatch => 3, _ => 2 };
                    }
                }
                token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) { artifact = Failure("Cancelled"); code = 2; }
            catch (UnsupportedSourceException) { artifact = Failure("Unsupported"); code = 2; }
            catch (Exception error) when (error is FormatException or ArgumentException or OverflowException or JsonException)
            { artifact = Failure("InvalidInput"); code = 3; }
            // Encode completely before CreateNew. An invalid/oversized response never leaves a partial new file.
            var bytes = artifact.ToCanonicalBytes();
            await using var output = new FileStream(args[^1], FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
            await output.WriteAsync(bytes); await output.FlushAsync(); return code;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or JsonException)
        {
            Console.Error.WriteLine("Project Test definition input/output unavailable; existing files are preserved."); return 3;
        }
    }
    private static SourceJson Failure(string state) => Json(new { format = "coglatas.project-test-definition-cli-failure/1", state });
    private static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value, new JsonSerializerOptions { MaxDepth = OutputLimits.MaximumDepth }), OutputLimits);
    private static async Task<byte[]> Read(string path, CancellationToken token)
    {
        var maximum = new SourceCodecLimits().MaximumBytes;
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bytes = new byte[maximum + 1];
        var count = await input.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, cancellationToken: token);
        if (count > maximum) throw new FormatException("Source input byte bound exceeded.");
        return bytes.AsSpan(0, count).ToArray();
    }
}
