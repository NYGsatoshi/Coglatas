using Coglatas.Application.Projects;

namespace Coglatas.Tests.Projects;

public sealed class TaskExecutionResultContractTests
{
    [Fact]
    [Trait("Scope", "Issue463")]
    public void DeterministicReportUsesOnlyBoundedApprovedMetadataAndContentStatistics()
    {
        var completedAt = new DateTimeOffset(2026, 8, 30, 22, 0, 0, TimeSpan.Zero);
        var source = new TaskExecutionReportSourceInput(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "text/plain",
            new string('a', 64),
            17,
            completedAt.AddSeconds(-1),
            "alpha beta\ngamma");

        var first = FirstPartyProjectFilesReportV1.Build([source], completedAt);
        var replay = FirstPartyProjectFilesReportV1.Build([source], completedAt);

        Assert.Equal(FirstPartyProjectFilesReportV1.SchemaVersion, first.SchemaVersion);
        Assert.Equal("Project Files Analysis Report", first.Title);
        Assert.Equal(first, replay);
        Assert.Equal(64, first.ContentSha256.Length);
        Assert.Matches("^[0-9a-f]{64}$", first.ContentSha256);
        Assert.Contains("Authorized source 1", first.BodyMarkdown, StringComparison.Ordinal);
        Assert.Contains("Lines: 2", first.BodyMarkdown, StringComparison.Ordinal);
        Assert.Contains("Words: 3", first.BodyMarkdown, StringComparison.Ordinal);
        Assert.Contains("Total materialized bytes: 17", first.BodyMarkdown, StringComparison.Ordinal);
        Assert.DoesNotContain("alpha beta", first.BodyMarkdown, StringComparison.Ordinal);
        Assert.DoesNotContain("filename", first.BodyMarkdown, StringComparison.OrdinalIgnoreCase);
        Assert.True(first.BodyMarkdown.Length <= FirstPartyProjectFilesReportV1.MaxBodyLength);
    }

    [Fact]
    [Trait("Scope", "Issue463")]
    public void ReportRejectsUnboundedOrUnapprovedProvenance()
    {
        var now = new DateTimeOffset(2026, 8, 30, 22, 0, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FirstPartyProjectFilesReportV1.Build([], now));
        Assert.Throws<ArgumentException>(() =>
            FirstPartyProjectFilesReportV1.Build([
                new TaskExecutionReportSourceInput(
                    Guid.NewGuid(),
                    "application/pdf",
                    new string('a', 64),
                    1,
                    now)
            ], now));
        Assert.Throws<ArgumentException>(() =>
            FirstPartyProjectFilesReportV1.Build([
                new TaskExecutionReportSourceInput(
                    Guid.NewGuid(),
                    "text/plain",
                    "not-a-hash",
                    1,
                    now)
            ], now));
    }
}
