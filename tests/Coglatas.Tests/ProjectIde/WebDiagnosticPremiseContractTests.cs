using System.Text;
using System.Text.Json;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.Tests.ProjectIde;

public sealed class WebDiagnosticPremiseContractTests
{
    public static IEnumerable<object[]> Cases()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "frontend/public/assets/projectide/diagnostic-premises-v1.json"))) directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("Original native premise fixture is required.");
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "frontend/public/assets/projectide/diagnostic-premises-v1.json")));
        foreach (var row in json.RootElement.GetProperty("cases").EnumerateArray()) yield return [row.GetRawText()];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryWebSnapshotMetadataFrameReplaysTheExactOwnedNativeResultAndCaptureGap(string text)
    {
        using var json = JsonDocument.Parse(text); var row = json.RootElement;
        var (c, _, _) = DiagnosticPremiseSnapshotTests.Prepare(text);
        Assert.Equal(c.Result.Data, SourceJson.Parse(row.GetProperty("result").GetRawText()));
        var projection = row.GetProperty("diagnosticPremises"); Assert.Equal("coglatas.local-diagnostic-premise-projection/1", projection.GetProperty("format").GetString());
        var entries = projection.GetProperty("diagnostics").EnumerateArray().ToArray(); Assert.Equal(c.Result.Diagnostics.Count, entries.Length);
        for (var index = 0; index < entries.Length; index++)
        {
            var d = c.Result.Diagnostics[index]; Assert.Equal(d.DiagnosticId.ToString(), entries[index].GetProperty("diagnosticId").GetString());
            foreach (var (label, mode, budget) in new[] {
                ("manifest", DiagnosticPremiseSnapshotMode.Manifest, ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes),
                ("available", DiagnosticPremiseSnapshotMode.AvailableBodies, ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes),
                ("budget", DiagnosticPremiseSnapshotMode.AvailableBodies, 1) })
            {
                var actual = ProjectDiagnosticPremiseSnapshot.Read(c, d, c.Result.ResultDigest, mode, budget); var frame = entries[index].GetProperty(label);
                var originalText = frame.GetProperty("text").GetString()!;
                Assert.Equal(actual.Data.CanonicalText, originalText); Assert.Equal(actual.Data.ToCanonicalBytes(), Encoding.UTF8.GetBytes(originalText));
                Assert.Equal(actual.Digest.Data, SourceJson.Parse(frame.GetProperty("digest").GetRawText()));
                Assert.Equal(actual.Digest, ContentDigest.Compute(ProjectDiagnosticPremiseSnapshot.Format, Encoding.UTF8.GetBytes(originalText)));
                Assert.Equal(d.Evidence.Premises, actual.References.Select(reference => reference.Digest));
                Assert.Same(d, actual.Diagnostic);
            }
        }
    }
}
