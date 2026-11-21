using Coglatas.ProjectIde.TestCli;

namespace Coglatas.Tests.ProjectIde;

public sealed class ProjectTestOutputTests
{
    [Fact]
    public async Task ConcurrentPublicationsRetainExactlyOneCompleteOutput()
    {
        var directory = Directory.CreateTempSubdirectory("projectide-test-output-");
        try
        {
            var destination = Path.Combine(directory.FullName, "result.json");
            var first = new byte[65_536];
            var second = new byte[first.Length + 1];
            Array.Fill(first, (byte)1);
            Array.Fill(second, (byte)2);

            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<Exception?> Publish(byte[] bytes)
            {
                await start.Task;
                return await Record.ExceptionAsync(() => ProjectTestOutput.WriteNewAsync(destination, bytes));
            }

            var firstWrite = Publish(first);
            var secondWrite = Publish(second);
            start.SetResult();
            var outcomes = await Task.WhenAll(firstWrite, secondWrite);

            var winner = Array.IndexOf(outcomes, null);
            Assert.InRange(winner, 0, 1);
            Assert.IsAssignableFrom<IOException>(outcomes[1 - winner]);
            Assert.Equal(winner == 0 ? first : second, await File.ReadAllBytesAsync(destination));
            Assert.Equal(destination, Assert.Single(Directory.GetFileSystemEntries(directory.FullName)));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task DirectoryFormDestinationIsRejectedAsInvalidBeforePublication()
    {
        var directory = Directory.CreateTempSubdirectory("projectide-test-directory-output-");
        try
        {
            var path = directory.FullName + Path.DirectorySeparatorChar;

            var error = await Assert.ThrowsAsync<ArgumentException>(
                () => ProjectTestOutput.WriteNewAsync(path, "[]"u8.ToArray()));

            Assert.Equal("path", error.ParamName);
            Assert.Empty(directory.EnumerateFileSystemInfos());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RefusedDestinationLeavesNoStagedOutput()
    {
        var directory = Directory.CreateTempSubdirectory("projectide-test-refusal-");
        try
        {
            var destination = Directory.CreateDirectory(Path.Combine(directory.FullName, "result.json"));

            var error = await Record.ExceptionAsync(() => ProjectTestOutput.WriteNewAsync(destination.FullName, "[]"u8.ToArray()));

            Assert.True(error is IOException or UnauthorizedAccessException, $"Unexpected result: {error}");
            Assert.Empty(destination.EnumerateFileSystemInfos());
            Assert.Equal(destination.FullName, Assert.Single(Directory.GetFileSystemEntries(directory.FullName)));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing/.")]
    [InlineData("missing/nested/..")]
    public async Task ExplicitDirectorySegmentsCannotPublishToTheNormalizedPath(string relativePath)
    {
        var directory = Directory.CreateTempSubdirectory("projectide-test-directory-segment-");
        try
        {
            var path = Path.Combine(directory.FullName, relativePath);

            var error = await Record.ExceptionAsync(() => ProjectTestOutput.WriteNewAsync(path, "[]"u8.ToArray()));

            Assert.Empty(directory.EnumerateFileSystemInfos());
            Assert.Equal("path", Assert.IsType<ArgumentException>(error).ParamName);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ADriveDesignatorAloneIsRejectedAsMissingAFileName()
    {
        if (!OperatingSystem.IsWindows()) return;
        var drive = Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd(Path.DirectorySeparatorChar);
        var error = await Assert.ThrowsAsync<ArgumentException>(() => ProjectTestOutput.WriteNewAsync(drive, "[]"u8.ToArray()));
        Assert.Equal("path", error.ParamName);
    }

    [Fact]
    public async Task AnExplicitFileNameRemainsValidInARelativeOutput()
    {
        var directory = Directory.CreateTempSubdirectory("projectide-test-relative-output-");
        try
        {
            var destination = Path.Combine(directory.FullName, "result.json");
            var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), destination);
            if (OperatingSystem.IsWindows() && Path.GetPathRoot(destination) is { Length: 3 } driveRoot)
            {
                var drive = driveRoot[..2];
                relative = drive + Path.GetRelativePath(Path.GetFullPath(drive), destination);
            }
            await ProjectTestOutput.WriteNewAsync(relative, "[]"u8.ToArray());
            Assert.Equal("[]", await File.ReadAllTextAsync(destination));
            Assert.Equal(destination, Assert.Single(Directory.GetFileSystemEntries(directory.FullName)));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
