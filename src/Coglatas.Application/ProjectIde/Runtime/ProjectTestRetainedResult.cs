using System.Text.Json;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Application.ProjectIde.Runtime;

public enum ProjectTestRetainedResultState { Unavailable, Available, Mismatch }

/// <summary>Original local Test result bytes, not a formal Artifact registration or evidence admission.</summary>
public sealed class ProjectTestRetainedResult
{
    private const string ExistingFormat = "coglatas.project-test-run/1";
    private readonly SourceJson? _body;

    public ProjectTestRetainedResultState State { get; }
    public ContentDigest? Digest { get; }
    public static ProjectTestRetainedResult Unavailable { get; } = new(ProjectTestRetainedResultState.Unavailable);
    private static ProjectTestRetainedResult Mismatch { get; } = new(ProjectTestRetainedResultState.Mismatch);

    private ProjectTestRetainedResult(ProjectTestRetainedResultState state, SourceJson? body = null, ContentDigest? digest = null)
    {
        State = state;
        _body = body;
        Digest = digest;
    }

    public static ProjectTestRetainedResult Capture(ProjectTestRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new(ProjectTestRetainedResultState.Available, run.Data, run.Digest);
    }

    public byte[]? RecoverCanonicalBytes() => _body?.ToCanonicalBytes();

    /// <summary>Missing historical bytes stay unavailable; invalid bytes are never replaced by a rerun.</summary>
    public static ProjectTestRetainedResult ReadStored(byte[]? original, ProjectTestJobBinding binding,
        ProjectTestResultSummary? summary)
    {
        if (original is null)
            return Unavailable;
        if (summary is null)
            return Mismatch;

        try
        {
            var body = SourceJson.Parse(original);
            if (!original.AsSpan().SequenceEqual(body.ToCanonicalBytes()))
                return Mismatch;
            var retained = new ProjectTestRetainedResult(ProjectTestRetainedResultState.Available, body,
                ContentDigest.Compute(ExistingFormat, original));
            return retained.Matches(binding, summary) ? retained : Mismatch;
        }
        catch (Exception error) when (error is ArgumentException or FormatException or JsonException or
                                      InvalidOperationException or KeyNotFoundException or UnsupportedSourceException)
        {
            return Mismatch;
        }
    }

    public bool Matches(ProjectTestJobBinding binding, ProjectTestResultSummary summary)
    {
        if (State != ProjectTestRetainedResultState.Available || _body is null || Digest?.Value != summary.ResultDigest)
            return false;

        try
        {
            var root = _body.Value;
            var input = ReadDigest(root, "inputDigest");
            var evidence = summary.EvidenceKind switch
            {
                CompilerEvidenceKind.ModelProof => "coglatas.model-proof",
                CompilerEvidenceKind.Missing => "coglatas.missing",
                _ => null
            };
            return root.GetProperty("format").GetString() == ExistingFormat &&
                   input.Value == binding.Source.InputDigest &&
                   root.GetProperty("compilerInputId").GetString() == CompilerInputId.Derive(input).ToString() &&
                   ReadDigest(root, "sourceDigest").Value == binding.SourceDigest &&
                   ReadDigest(root, "planDigest").Value == summary.PlanDigest &&
                   root.GetProperty("execution").GetString() == summary.Execution.ToString() &&
                   root.GetProperty("coverage").GetString() == summary.Coverage.ToString() &&
                   root.GetProperty("verdict").GetString() == summary.Verdict.ToString() &&
                   root.GetProperty("freshness").GetString() == summary.Freshness.ToString() &&
                   evidence is not null && root.GetProperty("evidenceKind").GetString() == evidence &&
                   root.GetProperty("discoveredTests").GetInt32() == summary.DiscoveredTests &&
                   root.GetProperty("executedAssertions").GetInt32() == summary.ExecutedAssertions;
        }
        catch (Exception error) when (error is ArgumentException or FormatException or JsonException or
                                      InvalidOperationException or KeyNotFoundException or UnsupportedSourceException)
        {
            return false;
        }
    }

    private static ContentDigest ReadDigest(JsonElement root, string name) =>
        ContentDigest.Parse(SourceJson.Parse(root.GetProperty(name).GetRawText()));
}
